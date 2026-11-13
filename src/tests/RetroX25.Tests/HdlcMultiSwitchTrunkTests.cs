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
    /// The three-switch demo chain (A, B, C) with HDLC leased lines between the switches, as the demo
    /// configuration now has: a call from an access port on A crosses two HDLC trunks to an echo on C and the data
    /// comes back, packets of 128 bytes with sequence numbers that wrap on the trunks.
    /// </summary>
    [TestFixture]
    public class HdlcMultiSwitchTrunkTests
    {
        private static PortConfig Trunk(int id, PortMode mode, int connectPort)
        {
            return new PortConfig
            {
                Id = id,
                Type = PortType.Trunk,
                Transport = PortTransport.HdlcTcp,
                Mode = mode,
                Host = "127.0.0.1",
                Port = (ushort)connectPort,
                LciBase = 1,
                LciCount = 16,
                LapbT1Ms = 500,
            };
        }

        private static XotSwitchPort Access(int id)
        {
            return new XotSwitchPort(
                new PortConfig { Id = id, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
        }

        /// <summary>
        /// A call crosses two HDLC trunks (A to B, B to C) and 300 packets of 128 bytes come back from the echo intact
        /// and in order. The LAPB counters of the trunks show the frames carried and nothing lost.
        /// </summary>
        [Test]
        public void CallRoutesAcrossTwoHdlcTrunks_AndDataRoundTrips()
        {
            // Switch C (DNIC 2352): trunk-in port 0 (DCE, listens), access (echo) port 1.
            SwitchConfig cfgC = new SwitchConfig { Dnic = "2352" };
            cfgC.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2352", DestPort = 1 });
            cfgC.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 0 });
            SwitchNode nodeC = new SwitchNode(cfgC);
            HdlcTcpSwitchPort cTrunk = new HdlcTcpSwitchPort(Trunk(0, PortMode.Dce, 0), NullLogger.Instance);
            XotSwitchPort cAccess = Access(1);
            nodeC.AddPort(cTrunk);
            nodeC.AddPort(cAccess);
            SwitchCore coreC = new SwitchCore(nodeC);

            // Switch B (DNIC 2351): trunk-in port 0 (DCE), trunk-out to C port 1 (DTE, dials).
            SwitchConfig cfgB = new SwitchConfig { Dnic = "2351" };
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2352", DestPort = 1 });
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
            SwitchNode nodeB = new SwitchNode(cfgB);

            // Switch A (DNIC 2350): access port 0, trunk-out to B port 1 (DTE, dials).
            SwitchConfig cfgA = new SwitchConfig { Dnic = "2350" };
            cfgA.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
            SwitchNode nodeA = new SwitchNode(cfgA);

            HdlcTcpSwitchPort bTrunkIn = new HdlcTcpSwitchPort(Trunk(0, PortMode.Dce, 0), NullLogger.Instance);
            HdlcTcpSwitchPort bTrunkOut = null!;
            HdlcTcpSwitchPort aTrunk = null!;
            XotSwitchPort aAccess = Access(0);
            try
            {
                Assert.That(cTrunk.Start(), Is.True);
                Assert.That(cAccess.Start(), Is.True);
                bTrunkOut = new HdlcTcpSwitchPort(Trunk(1, PortMode.Dte, cTrunk.BoundPort), NullLogger.Instance);
                nodeB.AddPort(bTrunkIn);
                nodeB.AddPort(bTrunkOut);
                SwitchCore coreB = new SwitchCore(nodeB);
                Assert.That(bTrunkIn.Start(), Is.True);
                Assert.That(bTrunkOut.Start(), Is.True);

                aTrunk = new HdlcTcpSwitchPort(Trunk(1, PortMode.Dte, bTrunkIn.BoundPort), NullLogger.Instance);
                nodeA.AddPort(aAccess);
                nodeA.AddPort(aTrunk);
                SwitchCore coreA = new SwitchCore(nodeA);
                Assert.That(aAccess.Start(), Is.True);
                Assert.That(aTrunk.Start(), Is.True);

                Assert.That(SpinWait.SpinUntil(() => aTrunk.PeerConnected && bTrunkIn.PeerConnected && bTrunkOut.PeerConnected && cTrunk.PeerConnected, 10000),
                    Is.True, "both HDLC trunks should come up");

                using X25Client echo = new X25Client { ServiceName = "echo" };
                using X25Client caller = new X25Client { ServiceName = "caller" };
                ManualResetEventSlim echoAccepted = new ManualResetEventSlim(false);
                echo.SetOnIncoming((c, vc) => { c.Accept(vc); echoAccepted.Set(); });
                echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    byte[] reply = new byte[length];
                    Array.Copy(data, 0, reply, 0, length);
                    c.Send(vc, reply, length, qBit);
                });
                echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = cAccess.BoundPort, LocalAddress = "23520001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

                const int Packets = 300;
                int received = 0;
                int wrong = 0;
                ManualResetEventSlim allBack = new ManualResetEventSlim(false);
                caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    // Each packet carries its own number in the first two bytes; they must come back in order.
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
                caller.Open(new X25ClientConfig { Host = "127.0.0.1", Port = aAccess.BoundPort, LocalAddress = "23500099", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

                X25Vc? call = caller.CallWait("23520001", 5000);
                Assert.That(call, Is.Not.Null, "the call should cross both HDLC trunks to the echo");
                Assert.That(echoAccepted.Wait(5000), Is.True);

                for (int i = 0; i < Packets; i++)
                {
                    byte[] payload = new byte[128];
                    payload[0] = (byte)(i >> 8);
                    payload[1] = (byte)(i & 0xFF);
                    for (int k = 2; k < payload.Length; k++)
                    {
                        payload[k] = (byte)(k ^ i);
                    }

                    // At most two packets are out at a time. The echo here answers with a plain Send, which gives up when
                    // its window is shut, so the caller must not run ahead of what the echo's window can carry.
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
                Assert.That(wrong, Is.EqualTo(0), "packets that came back wrong or out of order");

                // 300 packets each way is far more than the 8 sequence numbers of one window: they wrapped.
                HdlcLinkSnapshot ab = aTrunk.GetLinkSnapshot();
                HdlcLinkSnapshot bc = bTrunkOut.GetLinkSnapshot();
                Assert.That(ab.InformationFramesOut, Is.GreaterThanOrEqualTo(Packets));
                Assert.That(bc.InformationFramesOut, Is.GreaterThanOrEqualTo(Packets));
                Assert.That(ab.FcsErrors, Is.EqualTo(0));
                Assert.That(bc.FcsErrors, Is.EqualTo(0));
                Assert.That(ab.LinkResets, Is.EqualTo(0));
                Assert.That(bc.LinkResets, Is.EqualTo(0));
            }
            finally
            {
                aAccess.Stop();
                aTrunk?.Dispose();
                bTrunkOut?.Dispose();
                bTrunkIn.Dispose();
                cTrunk.Dispose();
                cAccess.Stop();
            }
        }
    }
}
