//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Monitor;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Tests
{
    /// <summary>
    /// Both directions with our own tools and x25emu's: x25emu's PAD through our switch to our echo, and our PAD through
    /// x25emu's three switches to its echo. Counters on both sides and the clear packets are read and compared.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    public class X25emuTwoWayTests
    {
        /// <summary>
        /// Our switch (DNIC 2350): an access port with an echo behind it, and an hdlc-tcp trunk that dials x25emu Switch A
        /// (which listens for it on 2010 and sends calls for 2350 out of that port). Every packet on both ports is kept.
        /// </summary>
        private sealed class OurSwitch : IDisposable
        {
            public readonly XotSwitchPort Access;
            public readonly HdlcTcpSwitchPort Trunk;
            public readonly List<string> Packets = new List<string>();
            public readonly List<(string Where, bool In, X25PacketInfo Info)> Decoded = new List<(string, bool, X25PacketInfo)>();

            public OurSwitch()
            {
                SwitchConfig config = new SwitchConfig { Dnic = "2350" };
                config.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2350", DestPort = 0 });
                config.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
                SwitchNode node = new SwitchNode(config);
                Access = new XotSwitchPort(
                    new PortConfig { Id = 0, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1, LciCount = 64 },
                    NullLogger.Instance);
                Trunk = new HdlcTcpSwitchPort(
                    new PortConfig
                    {
                        Id = 1,
                        Type = PortType.Trunk,
                        Transport = PortTransport.HdlcTcp,
                        Mode = PortMode.Dte,
                        Host = X25emuEnvironment.WslHost!,
                        Port = (ushort)X25emuEnvironment.HdlcTrunkListen,
                        LciBase = 1,
                        LciCount = 64,
                    },
                    NullLogger.Instance);
                Access.PacketCaptured += (id, inbound, packet) => Record("access", inbound, packet);
                Trunk.PacketCaptured += (id, inbound, packet) => Record("trunk ", inbound, packet);
                node.AddPort(Access);
                node.AddPort(Trunk);
                _core = new SwitchCore(node);
                Assert.That(Access.Start(), Is.True);
                Assert.That(Trunk.Start(), Is.True, "the trunk could not start: " + Trunk.LastError);
                Assert.That(SpinWait.SpinUntil(() => Trunk.PeerConnected, 15000), Is.True,
                    "the LAPB link to x25emu's Switch A did not come up (last error: " + Trunk.LastError + ")");
            }

            private readonly SwitchCore _core;

            private void Record(string where, bool inbound, byte[] packet)
            {
                lock (Packets)
                {
                    Packets.Add(where + (inbound ? " in  " : " out ") + BitConverter.ToString(packet));
                    if (packet.Length >= 3)
                    {
                        Decoded.Add((where.Trim(), inbound, X25PacketParser.ParsePacket(packet)));
                    }
                }
            }

            public string Dump()
            {
                lock (Packets)
                {
                    return string.Join("\n", Packets);
                }
            }

            public void Dispose()
            {
                GC.KeepAlive(_core);
                Trunk.Dispose();
                Access.Stop();
            }
        }

        private static string Counters(string name, PortCounters k)
        {
            return name + " calls_in=" + k.CallsIn + " calls_out=" + k.CallsOut + " cleared=" + k.CallsCleared + " data_in=" + k.PacketsIn + " data_out=" + k.PacketsOut;
        }

        /// <summary>
        /// x25emu's own PAD, started in WSL, calls our echo (23500001): Switch A sends the call out of its hdlc-tcp trunk
        /// port, our trunk dials it, our switch routes it to the access port, our echo answers. The typed line comes back
        /// on the PAD. Then the PAD clears. Counted: one call in and one out on each port of our switch and on the two
        /// x25emu ports involved, and the clear packets are decoded on both of our ports.
        /// </summary>
        [Test]
        public void X25emuPad_ThroughOurSwitch_ReachesOurEcho_AndTheCallIsCountedOnceOnEachSide()
        {
            X25emuEnvironment.RequireReady();
            Dictionary<int, PortStatus> beforeA = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            using OurSwitch ours = new OurSwitch();

            object gate = new object();
            int echoed = 0;
            (byte Cause, byte Diagnostic)? clearedAtEcho = null;
            using X25Client echo = new X25Client { ServiceName = "our-echo" };
            echo.SetOnIncoming((c, vc) => c.Accept(vc));
            echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                lock (gate) { echoed++; }
                c.Send(vc, data, length, qBit);
            });
            echo.SetOnClear((c, vc, cause, diagnostic) => { lock (gate) { clearedAtEcho = (cause, diagnostic); } });
            echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = ours.Access.BoundPort, LocalAddress = "23500001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "23400055");
            Assert.That(SpinWait.SpinUntil(() => pad.Output.Contains("PAD ready"), 10000), Is.True, pad.Output);
            pad.Write("CALL 23500001\r");
            Assert.That(SpinWait.SpinUntil(() => pad.Output.Contains("\nCON"), 15000), Is.True, "x25emu's PAD never said the call is connected:\n" + pad.Output + "\nours:\n" + ours.Dump());
            const int Lines = 5;
            for (int i = 0; i < Lines; i++)
            {
                pad.Write("LINE-" + i + "-FROM-X25EMU-PAD\r");
                Assert.That(SpinWait.SpinUntil(() => CountOf(pad.Output, "LINE-" + i + "-FROM-X25EMU-PAD") >= 1, 10000), Is.True,
                    "line " + i + " did not come back (echo got " + echoed + "):\n" + pad.Output + "\nours:\n" + ours.Dump());
            }

            pad.Write("\u0010");
            pad.Write("CLR\r");
            Assert.That(SpinWait.SpinUntil(() => { lock (gate) { return clearedAtEcho.HasValue; } }, 10000), Is.True, "our echo never saw the clear:\n" + pad.Output);
            Assert.That(SpinWait.SpinUntil(() => ours.Access.Counters.CallsCleared >= 1 && ours.Trunk.Counters.CallsCleared >= 1, 10000), Is.True, "not cleared on both ports");
            Thread.Sleep(700);
            Dictionary<int, PortStatus> afterA = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);

            string table = Counters("our trunk ", ours.Trunk.Counters) + "\n" + Counters("our access", ours.Access.Counters)
                + "\nx25emu Switch A changes:\n" + X25emuMonitor.Delta(beforeA, afterA);
            TestContext.Progress.WriteLine(table);
            TestContext.Progress.WriteLine("PAD screen:\n" + pad.Output);
            TestContext.Progress.WriteLine("our packets:\n" + ours.Dump());

            // Our side. The call came in on the trunk and went out of the access port.
            Assert.That(ours.Trunk.Counters.CallsIn, Is.EqualTo(1u), table);
            Assert.That(ours.Trunk.Counters.CallsOut, Is.EqualTo(0u), table);
            Assert.That(ours.Access.Counters.CallsOut, Is.EqualTo(1u), table);
            Assert.That(ours.Access.Counters.CallsIn, Is.EqualTo(0u), table);
            Assert.That(ours.Trunk.Counters.CallsCleared, Is.EqualTo(1u), table);
            Assert.That(ours.Access.Counters.CallsCleared, Is.EqualTo(1u), table);
            Assert.That(ours.Access.Counters.PacketsOut, Is.EqualTo(ours.Trunk.Counters.PacketsIn), "every data packet that came in on the trunk went out of the access port\n" + table);
            Assert.That(ours.Trunk.Counters.PacketsOut, Is.EqualTo(ours.Access.Counters.PacketsIn), "every data packet the echo returned went out of the trunk\n" + table);
            Assert.That(ours.Trunk.Counters.PacketsIn, Is.GreaterThanOrEqualTo((uint)Lines), table);

            // x25emu's side: Switch A took the call in on its access port 0 and put it out on its trunk port 5.
            Assert.That(X25emuMonitor.Rise(beforeA, afterA, 0, p => p.CallsIn), Is.EqualTo(1u), table);
            Assert.That(X25emuMonitor.Rise(beforeA, afterA, 5, p => p.CallsOut), Is.EqualTo(1u), table);
            Assert.That(X25emuMonitor.Rise(beforeA, afterA, 0, p => p.CallsCleared), Is.EqualTo(1u), table);
            Assert.That(X25emuMonitor.Rise(beforeA, afterA, 0, p => p.PacketsIn), Is.EqualTo((uint)Lines), table);
            Assert.That(X25emuMonitor.Rise(beforeA, afterA, 5, p => p.PacketsOut), Is.EqualTo((uint)Lines), table);
            // Measured difference in what "cleared" means: x25emu counts it ONCE per call, on the port where the Clear
            // Request arrived (port 0, the PAD's side). The other leg (port 5) stays at 0. We count once per leg, so our two
            // ports show 1 each. Both count the call once as far as calls_in and calls_out go.
            Assert.That(X25emuMonitor.Rise(beforeA, afterA, 5, p => p.CallsCleared), Is.EqualTo(0u), table);

            // The clear packets, decoded: the PAD's CLR is a clear with cause 0 (DTE originated) and diagnostic 0 on the
            // trunk (in) and on the access port (out), and the echo is told the same.
            lock (ours.Packets)
            {
                int clearsIn = 0;
                for (int i = 0; i < ours.Decoded.Count; i++)
                {
                    X25PacketInfo info = ours.Decoded[i].Info;
                    if (info.PacketType == X25PacketType.ClearRequest)
                    {
                        Assert.That(info.ClearingCause, Is.EqualTo((byte)0), "clear cause on " + ours.Decoded[i].Where + "\n" + ours.Dump());
                        Assert.That(info.DiagnosticCode, Is.EqualTo((byte)0), "clear diagnostic on " + ours.Decoded[i].Where + "\n" + ours.Dump());
                        clearsIn++;
                    }
                }

                Assert.That(clearsIn, Is.EqualTo(2), "one Clear Request crossed the trunk and one the access port\n" + ours.Dump());
            }

            lock (gate)
            {
                Assert.That(clearedAtEcho!.Value.Cause, Is.EqualTo((byte)0));
                Assert.That(clearedAtEcho.Value.Diagnostic, Is.EqualTo((byte)0));
            }
        }

        private static int CountOf(string text, string what)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(what, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += what.Length;
            }

            return count;
        }
    }
}
