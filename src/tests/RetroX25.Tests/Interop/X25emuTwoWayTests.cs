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

            /// <summary>Every call record our switch wrote, as the line the record file would hold.</summary>
            public readonly List<string> Records = new List<string>();

            public OurSwitch(AccountConfig? account = null)
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
                if (account != null)
                {
                    Assert.That(node.AddAccount(account), Is.True);
                }

                _core = new SwitchCore(node);
                _core.CdrRecorded += cdr => { lock (Records) { Records.Add(CdrFileWriter.Format(cdr)); } };
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

        /// <summary>
        /// Billing through both switches. x25emu's x25traffic (calling address 23409901, given with -a: x25emu checks that a call from an access port comes from one of its own numbers) sends 20 data packets of 128
        /// octets from Switch A's access port to an echo behind our switch (23500001), and the echo returns each one. Both
        /// switches bill the call to an account named <c>traffic</c> covering 23409900-23409999 (the network script gives
        /// Switch A the same one), and both write a record line.
        /// </summary>
        /// <remarks>
        /// The expected figures are worked from the rules, not taken from either switch: each packet is a whole message
        /// (M bit off), and ITU-T D.12 counts a message in 64-octet segments rounded up once - ceil(128 / 64) = 2. So each
        /// direction is 20 packets, 20 x 128 = 2560 octets and 20 x 2 = 40 segments. The columns are those of
        /// src/docs/billing.md: 7/8 packets, 9/10 bytes, 17/18 segments, 19 the paying account, 24 the called side's account (1-based).
        /// </remarks>
        [Test]
        public void ACallThroughBothSwitches_IsBilledToTheSameAccount_WithTheSameSegments()
        {
            X25emuEnvironment.RequireReady();
            if (string.IsNullOrWhiteSpace(X25emuEnvironment.State))
            {
                Assert.Ignore("Set X25EMU_STATE (the WSL folder of the running x25emu network); demo/crossenv-test.ps1 does it.");
            }

            const int Packets = 20;
            const int Octets = 128;
            string cdrPath = X25emuEnvironment.State + "/calls.cdr";
            int linesBefore = ReadLines(cdrPath).Length;

            using OurSwitch ours = new OurSwitch(new AccountConfig { Name = "traffic", AddrStart = "23409900", AddrEnd = "23409999" });
            int returned = 0;
            using X25Client echo = new X25Client { ServiceName = "our-echo" };
            echo.SetOnIncoming((c, vc) => c.Accept(vc));
            echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                if (c.Send(vc, data, length, qBit))
                {
                    Interlocked.Increment(ref returned);
                }
            });
            echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = ours.Access.BoundPort, LocalAddress = "23500001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            // -r 20: twenty a second, so the echo's replies never wait on a shut window inside the receive callback.
            using X25emuEnvironment.LinuxProcess traffic = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25traffic"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-d", "23500001", "-a", "23409901",
                "-n", Packets.ToString(), "-s", Octets.ToString(), "-r", "20");
            Assert.That(traffic.WaitForExit(60000), Is.True, "x25traffic did not finish:\n" + traffic.Output);
            Assert.That(Volatile.Read(ref returned), Is.EqualTo(Packets), "the echo did not return every packet:\n" + traffic.Output + "\nours:\n" + ours.Dump());

            Assert.That(SpinWait.SpinUntil(() => { lock (ours.Records) { return ours.Records.Count >= 1; } }, 10000), Is.True, "our switch wrote no record");
            string[] after = Array.Empty<string>();
            Assert.That(SpinWait.SpinUntil(() => (after = ReadLines(cdrPath)).Length > linesBefore, 10000), Is.True, "x25emu wrote no record in " + cdrPath);
            string theirs = after[after.Length - 1];
            string mine;
            lock (ours.Records)
            {
                Assert.That(ours.Records.Count, Is.EqualTo(1), string.Join("\n", ours.Records));
                mine = ours.Records[0];
            }

            TestContext.Progress.WriteLine("x25emu Switch A: " + theirs);
            TestContext.Progress.WriteLine("our switch:      " + mine);
            string[] lines = { theirs, mine };
            for (int i = 0; i < lines.Length; i++)
            {
                string[] col = lines[i].Split(',');
                string who = (i == 0 ? "x25emu: " : "ours: ") + lines[i];
                // 24 columns since 2026-11-17 in both switches (x25emu 6dc74f8, RetroIO e0db294): column 24 is the called
                // side's account, the one that pays with reverse charging (X.25 6.18).
                Assert.That(col.Length, Is.EqualTo(24), who);
                Assert.That(col[2], Is.EqualTo("23409901"), "calling address, " + who);
                Assert.That(col[3], Is.EqualTo("23500001"), "called address, " + who);
                Assert.That(col[6], Is.EqualTo(Packets.ToString()), "packets forward, " + who);
                Assert.That(col[7], Is.EqualTo(Packets.ToString()), "packets back, " + who);
                Assert.That(col[8], Is.EqualTo((Packets * Octets).ToString()), "bytes forward, " + who);
                Assert.That(col[9], Is.EqualTo((Packets * Octets).ToString()), "bytes back, " + who);
                Assert.That(col[16], Is.EqualTo((Packets * 2).ToString()), "segments forward, " + who);
                Assert.That(col[17], Is.EqualTo((Packets * 2).ToString()), "segments back, " + who);
                Assert.That(col[18], Is.EqualTo("traffic"), "paying account, " + who);
                Assert.That(col[21], Is.EqualTo("caller"), "who is charged, " + who);

                // 23500001 is in no account's range on either switch (the network script's accounts cover 2340 only),
                // and the echo names no NUI in its Call Accepted (X.25 6.21.3), so neither has a called-side account.
                Assert.That(col[23], Is.Empty, "called side's account, " + who);
            }
        }

        /// <summary>
        /// A NUI is billed where it enters the network and goes no further. x25emu's PAD, started with <c>-n CARD7</c>,
        /// calls our echo through Switch A; Switch A bills the account that has that NUI (<c>card7</c>, from the network
        /// script) and, as X.25 6.21 says ("never transmitted to the remote DTE"), takes the NUI off the call before
        /// passing it on. Our switch, with no accounts, therefore records no NUI and no paying account.
        /// </summary>
        [Test]
        public void ANui_IsBilledAtTheFirstSwitch_AndDoesNotTravelFurther()
        {
            X25emuEnvironment.RequireReady();
            if (string.IsNullOrWhiteSpace(X25emuEnvironment.State))
            {
                Assert.Ignore("Set X25EMU_STATE (the WSL folder of the running x25emu network); demo/crossenv-test.ps1 does it.");
            }

            string cdrPath = X25emuEnvironment.State + "/calls.cdr";
            int linesBefore = ReadLines(cdrPath).Length;
            using OurSwitch ours = new OurSwitch();
            using X25Client echo = new X25Client { ServiceName = "our-echo" };
            echo.SetOnIncoming((c, vc) => c.Accept(vc));
            echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) => c.Send(vc, data, length, qBit));
            echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = ours.Access.BoundPort, LocalAddress = "23500001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "23400055", "-n", "CARD7");
            Assert.That(SpinWait.SpinUntil(() => pad.Output.Contains("PAD ready"), 10000), Is.True, pad.Output);
            pad.Write("CALL 23500001\r");
            Assert.That(SpinWait.SpinUntil(() => pad.Output.Contains("\nCON"), 15000), Is.True, "not connected:\n" + pad.Output + "\nours:\n" + ours.Dump());
            pad.Write("PAID-BY-CARD7\r");
            Assert.That(SpinWait.SpinUntil(() => CountOf(pad.Output, "PAID-BY-CARD7") >= 1, 10000), Is.True, pad.Output);
            pad.Write("\u0010");
            pad.Write("CLR\r");

            Assert.That(SpinWait.SpinUntil(() => { lock (ours.Records) { return ours.Records.Count >= 1; } }, 10000), Is.True, "our switch wrote no record");
            string[] after = Array.Empty<string>();
            Assert.That(SpinWait.SpinUntil(() => (after = ReadLines(cdrPath)).Length > linesBefore, 10000), Is.True, "x25emu wrote no record");
            string[] theirs = after[after.Length - 1].Split(',');
            string[] mine;
            lock (ours.Records)
            {
                mine = ours.Records[0].Split(',');
            }

            string both = "x25emu: " + after[after.Length - 1] + "\nours:   " + string.Join(",", mine);
            TestContext.Progress.WriteLine(both);
            Assert.That(theirs[2], Is.EqualTo("23400055"), both);
            Assert.That(theirs[18], Is.EqualTo("card7"), "Switch A bills the NUI's account, not the address range's\n" + both);
            Assert.That(theirs[19], Is.EqualTo("CARD7"), both);
            Assert.That(mine[2], Is.EqualTo("23400055"), both);
            Assert.That(mine[19], Is.Empty, "the NUI reached our switch: X.25 6.21 says it does not travel\n" + both);
            Assert.That(mine[18], Is.Empty, "our switch has no accounts\n" + both);
        }

        private static string[] ReadLines(string wslPath)
        {
            string text = X25emuEnvironment.RunWsl("cat '" + wslPath + "' 2>/dev/null", 10000, out _);
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
