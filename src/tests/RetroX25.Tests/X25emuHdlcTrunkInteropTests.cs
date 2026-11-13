//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Tests
{
    /// <summary>
    /// Our <c>hdlc-tcp</c> trunk against the C emulator's (x25emu) <c>hdlc-tcp</c> trunk, with calls both ways. Our
    /// switch dials x25emu Switch A's trunk port (it listens as the DCE); calls to our DNIC 2350 go out of that port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The x25emu network is started by <c>demo/crossenv-test.ps1</c> (see docs/crossenv-testing.md); the setup is
    /// checked by <see cref="X25emuEnvironment"/>. Each test that calls the x25emu echo starts a fresh echo of its own,
    /// because x25emu's echo takes one call: after it, Switch C offers the next call to the connection the first call
    /// closed ("nothing to deliver ... number busy") until the echo is restarted.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    public class X25emuHdlcTrunkInteropTests
    {
        /// <summary>
        /// Our switch (DNIC 2350, an HDLC trunk that dials x25emu's Switch A, an access port with an echo) with the
        /// trunk up.
        /// </summary>
        private sealed class OurSwitch : IDisposable
        {
            public readonly XotSwitchPort Access;
            public readonly HdlcTcpSwitchPort Trunk;

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
                node.AddPort(Access);
                node.AddPort(Trunk);
                SwitchCore core = new SwitchCore(node);
                Assert.That(Access.Start(), Is.True);
                Assert.That(Trunk.Start(), Is.True, "the trunk could not start: " + Trunk.LastError);
                Assert.That(SpinWait.SpinUntil(() => Trunk.PeerConnected, 15000), Is.True,
                    "the LAPB link to x25emu's Switch A did not come up (last error: " + Trunk.LastError + ")");
            }

            public void Dispose()
            {
                Trunk.Dispose();
                Access.Stop();
            }
        }

        /// <summary>
        /// A call placed on our switch for x25emu's echo (address 23420001, behind its Switch C) crosses our trunk and
        /// its three switches, and 300 packets of 128 bytes come back intact and in order.
        /// </summary>
        [Test]
        public void CallFromOurSwitch_ReachesX25emuEcho_OverTheHdlcTrunk()
        {
            X25emuEnvironment.RequireReady();
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
            using OurSwitch ours = new OurSwitch();
            using X25Client caller = new X25Client { ServiceName = "interop-caller" };
            const int Packets = 300;
            int received = 0;
            int wrong = 0;
            ManualResetEventSlim allBack = new ManualResetEventSlim(false);
            caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                int number = (data[0] << 8) | data[1];
                int n = Interlocked.Increment(ref received);
                if (length != 128 || number != n - 1)
                {
                    Interlocked.Increment(ref wrong);
                }

                if (n == Packets)
                {
                    allBack.Set();
                }
            });
            caller.Open(new X25ClientConfig { Host = "127.0.0.1", Port = ours.Access.BoundPort, LocalAddress = "23500099", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            X25Vc? call = caller.CallWait("23420001", 10000);
            Assert.That(call, Is.Not.Null, "the call did not reach x25emu's echo");
            for (int i = 0; i < Packets; i++)
            {
                byte[] payload = new byte[128];
                payload[0] = (byte)(i >> 8);
                payload[1] = (byte)(i & 0xFF);
                long deadline = Environment.TickCount64 + 20000;
                while (i - Volatile.Read(ref received) >= 2)
                {
                    Assert.That(Environment.TickCount64, Is.LessThan(deadline), "the echoes stopped coming back at packet " + i);
                    Thread.Sleep(1);
                }

                while (!caller.Send(call!, payload, payload.Length, false))
                {
                    Assert.That(Environment.TickCount64, Is.LessThan(deadline), "the send window never opened");
                    Thread.Sleep(2);
                }
            }

            Assert.That(allBack.Wait(20000), Is.True, "only " + received + " of " + Packets + " packets came back");
            Assert.That(wrong, Is.EqualTo(0));
            HdlcLinkSnapshot link = ours.Trunk.GetLinkSnapshot();
            Assert.That(link.FcsErrors, Is.EqualTo(0), "x25emu's frames must pass our FCS check");
            Assert.That(link.LinkResets, Is.EqualTo(0));
        }

        /// <summary>
        /// A call placed by x25emu's own <c>x25traffic</c> at its Switch A for an echo on OUR switch (address 23500001)
        /// crosses the trunk the other way, and every packet it sends arrives at our echo, 128 bytes each. (The tool's own
        /// "Delivery" figure is not used: it stops waiting for echoes a moment after it has sent, and the echo here is
        /// slower than x25emu's, so the figure would measure the echo and not the trunk.)
        /// </summary>
        [Test]
        public void CallFromX25emu_ReachesOurEcho_OverTheHdlcTrunk()
        {
            X25emuEnvironment.RequireReady();
            using OurSwitch ours = new OurSwitch();
            using X25Client echo = new X25Client { ServiceName = "interop-echo" };
            int arrived = 0;
            ManualResetEventSlim accepted = new ManualResetEventSlim(false);
            echo.SetOnIncoming((c, vc) => { c.Accept(vc); accepted.Set(); });
            // Replies are queued and sent by a thread of their own. Waiting for a shut window inside the receive
            // callback would stop the very packets that open it again.
            System.Collections.Concurrent.BlockingCollection<(X25Vc Vc, byte[] Data, bool Q)> replies =
                new System.Collections.Concurrent.BlockingCollection<(X25Vc, byte[], bool)>();
            Thread sender = new Thread(() =>
            {
                try
                {
                    foreach ((X25Vc Vc, byte[] Data, bool Q) reply in replies.GetConsumingEnumerable())
                    {
                        long deadline = Environment.TickCount64 + 10000;
                        while (!echo.Send(reply.Vc, reply.Data, reply.Data.Length, reply.Q) && Environment.TickCount64 < deadline)
                        {
                            Thread.Sleep(1);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // The client was closed under us: the test is over, nothing is left to send.
                }
            })
            { IsBackground = true, Name = "interop-echo-sender" };
            sender.Start();
            echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                byte[] copy = new byte[length];
                Array.Copy(data, 0, copy, 0, length);
                if (length == 128)
                {
                    Interlocked.Increment(ref arrived);
                }

                replies.Add((vc, copy, qBit));
            });
            echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = ours.Access.BoundPort, LocalAddress = "23500001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            using X25emuEnvironment.LinuxProcess traffic = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25traffic"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-d", "23500001", "-n", "500", "-r", "0");
            bool finished = traffic.WaitForExit(60000);
            string output = traffic.Output;
            Assert.That(finished, Is.True, "x25traffic did not finish in 60 seconds: " + output);

            replies.CompleteAdding();
            sender.Join(5000);
            Assert.That(accepted.Wait(1000), Is.True, "our echo never saw the call: " + output);
            Assert.That(output, Does.Contain("Sent:      500 pkts"), output);
            Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref arrived) >= 500, 10000), Is.True,
                "only " + arrived + " of 500 packets of 128 bytes reached our echo" + Environment.NewLine + output);
            Assert.That(ours.Trunk.GetLinkSnapshot().FcsErrors, Is.EqualTo(0), "x25emu's frames must pass our FCS check");
        }
    }
}
