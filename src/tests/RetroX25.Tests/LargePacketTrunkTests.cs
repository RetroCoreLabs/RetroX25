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
    /// Data packets of every size X.25 allows (clause 4.3.2: 128 standard, up to 4096 octets) across a trunk between two
    /// switches, over XOT and over HDLC (byte-stuffed, with LAPB). A caller on switch A calls an echo on switch B; each packet
    /// carries its own number and a pattern, and must come back the same size, in order and unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A 4096-octet data packet is 4099 octets with its header (modulo 8) and 4100 (modulo 128), and as a LAPB frame with
    /// FCS 4103 and 4105 octets. Before 2026-11-14 the XOT and HDLC readers stopped at 4096.
    /// </para>
    /// <para>
    /// The same day this test found a second fault, in the switch: it asked for its own default sizes (128, window 2) on the
    /// onward leg of every call and told the caller the call had the sizes the caller asked for, so any packet above 128
    /// octets reset the call. The switch now passes the sizes asked for to the onward leg and the sizes agreed back to the
    /// caller (X.25 6.9 and 6.12). Set <c>LARGE_DIAG</c> to print the packets and clears seen on the four ports.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class LargePacketTrunkTests
    {
        private static PortConfig Trunk(int id, PortMode mode, PortTransport transport, int connectPort)
        {
            return new PortConfig
            {
                Id = id,
                Type = PortType.Trunk,
                Transport = transport,
                Mode = mode,
                Host = "127.0.0.1",
                Port = (ushort)connectPort,
                LciBase = 1,
                LciCount = 16,
                LapbT1Ms = 500,
                MaxPacketSize = 4096,
            };
        }

        private static PortConfig Access(int id)
        {
            return new PortConfig { Id = id, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1, MaxPacketSize = 4096 };
        }

        private static IMonitoredSwitchPort NewTrunk(PortConfig config)
        {
            if (config.Transport == PortTransport.HdlcTcp)
            {
                return new HdlcTcpSwitchPort(config, NullLogger.Instance);
            }

            return new XotSwitchPort(config, NullLogger.Instance);
        }

        private static void Trace(IMonitoredSwitchPort port, string name)
        {
            if (Environment.GetEnvironmentVariable("LARGE_DIAG") == null)
            {
                return;
            }

            port.PacketCaptured += (id, inbound, packet) =>
                TestContext.Progress.WriteLine(name + (inbound ? " in  " : " out ") + packet.Length + " octets: " + BitConverter.ToString(packet, 0, Math.Min(packet.Length, 40)));
            port.CircuitReset += (p, lci, cause, diag) => TestContext.Progress.WriteLine(name + " RESET lci " + lci + " cause " + cause + " diag " + diag);
            port.CallCleared += (p, lci, cause, diag) => TestContext.Progress.WriteLine(name + " CLEAR lci " + lci + " cause " + cause + " diag " + diag);
        }

        private static int BoundPortOf(IMonitoredSwitchPort port)
        {
            return port is HdlcTcpSwitchPort h ? h.BoundPort : ((XotSwitchPort)port).BoundPort;
        }

        private static bool Up(IMonitoredSwitchPort port)
        {
            return port is HdlcTcpSwitchPort h ? h.PeerConnected : true;
        }

        /// <summary>
        /// Packets of the given size cross the trunk to an echo and come back whole.
        /// </summary>
        [TestCase(PortTransport.Xot, 128)]
        [TestCase(PortTransport.Xot, 1024)]
        [TestCase(PortTransport.Xot, 2048)]
        [TestCase(PortTransport.Xot, 4096)]
        [TestCase(PortTransport.HdlcTcp, 128)]
        [TestCase(PortTransport.HdlcTcp, 1024)]
        [TestCase(PortTransport.HdlcTcp, 2048)]
        [TestCase(PortTransport.HdlcTcp, 4096)]
        public void PacketsOfTheGivenSize_CrossTheTrunkAndComeBackWhole(PortTransport transport, int packetSize)
        {
            const int Packets = 40;

            SwitchConfig cfgB = new SwitchConfig { Dnic = "2352" };
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2352", DestPort = 1 });
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 0 });
            SwitchNode nodeB = new SwitchNode(cfgB);
            IMonitoredSwitchPort bTrunk = NewTrunk(Trunk(0, PortMode.Dce, transport, 0));
            XotSwitchPort bAccess = new XotSwitchPort(Access(1), NullLogger.Instance);
            Trace(bTrunk, "B.trunk"); Trace(bAccess, "B.access");
            nodeB.AddPort(bTrunk);
            nodeB.AddPort(bAccess);
            SwitchCore coreB = new SwitchCore(nodeB);

            SwitchConfig cfgA = new SwitchConfig { Dnic = "2350" };
            cfgA.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
            SwitchNode nodeA = new SwitchNode(cfgA);
            XotSwitchPort aAccess = new XotSwitchPort(Access(0), NullLogger.Instance);
            IMonitoredSwitchPort aTrunk = null!;

            try
            {
                Assert.That(bAccess.Start(), Is.True);
                Assert.That(bTrunk.Start(), Is.True);
                aTrunk = NewTrunk(Trunk(1, PortMode.Dte, transport, BoundPortOf(bTrunk)));
                Trace(aTrunk, "A.trunk"); Trace(aAccess, "A.access");
                nodeA.AddPort(aAccess);
                nodeA.AddPort(aTrunk);
                SwitchCore coreA = new SwitchCore(nodeA);
                Assert.That(aAccess.Start(), Is.True);
                Assert.That(aTrunk.Start(), Is.True);
                Assert.That(SpinWait.SpinUntil(() => Up(aTrunk) && Up(bTrunk), 10000), Is.True, "the trunk should come up");

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
                echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = bAccess.BoundPort, LocalAddress = "23520001", Framing = X25ClientFraming.Xot, PacketSize = (ushort)packetSize }, NullLogger.Instance);

                int received = 0;
                int wrong = 0;
                int wrongLength = 0;
                ManualResetEventSlim allBack = new ManualResetEventSlim(false);
                caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    int n = Interlocked.Increment(ref received);
                    int number = (data[0] << 8) | data[1];
                    if (length != packetSize)
                    {
                        Interlocked.Increment(ref wrongLength);
                    }

                    if (number != n - 1)
                    {
                        Interlocked.Increment(ref wrong);
                    }
                    else
                    {
                        for (int k = 2; k < length; k++)
                        {
                            if (data[k] != (byte)(k ^ number))
                            {
                                Interlocked.Increment(ref wrong);
                                break;
                            }
                        }
                    }

                    if (n == Packets)
                    {
                        allBack.Set();
                    }
                });
                caller.Open(new X25ClientConfig { Host = "127.0.0.1", Port = aAccess.BoundPort, LocalAddress = "23500099", Framing = X25ClientFraming.Xot, PacketSize = (ushort)packetSize }, NullLogger.Instance);

                X25Vc? call = caller.CallWait("23520001", 5000);
                Assert.That(call, Is.Not.Null, "the call should cross the trunk to the echo");
                Assert.That(echoAccepted.Wait(5000), Is.True);

                for (int i = 0; i < Packets; i++)
                {
                    byte[] payload = new byte[packetSize];
                    payload[0] = (byte)(i >> 8);
                    payload[1] = (byte)(i & 0xFF);
                    for (int k = 2; k < payload.Length; k++)
                    {
                        payload[k] = (byte)(k ^ i);
                    }

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

                Assert.That(allBack.Wait(30000), Is.True, "only " + received + " of " + Packets + " packets came back");
                Assert.That(wrongLength, Is.EqualTo(0), "packets that came back with another size than " + packetSize);
                Assert.That(wrong, Is.EqualTo(0), "packets that came back changed or out of order");
            }
            finally
            {
                aAccess.Stop();
                (aTrunk as IDisposable)?.Dispose();
                (bTrunk as IDisposable)?.Dispose();
                bAccess.Stop();
            }
        }
    }
}
