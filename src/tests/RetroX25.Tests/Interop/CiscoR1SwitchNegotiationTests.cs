//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Tests
{
    /// <summary>
    /// Calls with other packet sizes and window sizes than our defaults go through OUR switch to the real Cisco router R1
    /// (address 23600001, over an XOT trunk), and the facility bytes on both sides of the switch are read in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why: on 2026-11-14 the switch began to pass the window and packet size the caller asked for on to the next leg, and to tell the
    /// caller what the far end agreed to (X.25 6.9 and 6.12). Before that it always asked for 128 and window 2 on the onward leg. The
    /// tests against x25emu and between two of our own switches pass; this measures what the Cisco does with the new requests.
    /// </para>
    /// <para>Environment variable <c>CISCO_R1_HOST</c> (the router's LAN address; the tests are skipped without it). Nothing is changed on
    /// the router; every call is cleared.</para>
    /// </remarks>
    [TestFixture]
    [Category("CiscoLive")]
    [Explicit("needs the real Cisco router R1; set CISCO_R1_HOST")]
    public class CiscoR1SwitchNegotiationTests
    {
        private static readonly byte[] PadMarker = { 0x01, 0x00, 0x00, 0x00 };

        /// <summary>
        /// A call asking for the given packet size code and window crosses our switch to the Cisco PAD. The call must be accepted, and the
        /// packet size and window pairs the caller is told must be the pairs the Cisco answered with.
        /// </summary>
        /// <param name="sizeCode">The packet size facility code: 7 = 128, 8 = 256, 9 = 512, 10 = 1024.</param>
        /// <param name="window">The window size asked for.</param>
        [TestCase((byte)7, (byte)2)]
        [TestCase((byte)8, (byte)4)]
        [TestCase((byte)9, (byte)7)]
        [TestCase((byte)10, (byte)7)]
        public void CallThroughOurSwitch_ToTheCisco_TheCallerIsToldWhatTheCiscoAnswered(byte sizeCode, byte window)
        {
            string? host = Environment.GetEnvironmentVariable("CISCO_R1_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Ignore("CISCO_R1_HOST is not set");
            }

            SwitchConfig cfg = new SwitchConfig { Dnic = "2340" };
            cfg.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
            SwitchNode node = new SwitchNode(cfg);
            XotSwitchPort access = new XotSwitchPort(new PortConfig { Id = 0, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1, MaxPacketSize = 1024 }, NullLogger.Instance);
            XotSwitchPort trunk = new XotSwitchPort(
                new PortConfig { Id = 1, Type = PortType.Trunk, Transport = PortTransport.Xot, Mode = PortMode.Dte, Host = host!, Port = 1998, LciBase = 1, LciCount = 16, MaxPacketSize = 1024 },
                NullLogger.Instance);

            List<byte[]> trunkOut = new List<byte[]>();
            List<byte[]> trunkIn = new List<byte[]>();
            trunk.PacketCaptured += (id, inbound, packet) =>
            {
                lock (trunkOut)
                {
                    (inbound ? trunkIn : trunkOut).Add(packet);
                }
            };

            node.AddPort(access);
            node.AddPort(trunk);
            SwitchCore core = new SwitchCore(node);
            try
            {
                Assert.That(access.Start(), Is.True);
                Assert.That(trunk.Start(), Is.True);
                Assert.That(SpinWait.SpinUntil(() => trunk.PeerConnected, 10000), Is.True, "the trunk never connected to the Cisco");

                using RawXotCaller caller = new RawXotCaller("127.0.0.1", access.BoundPort, 1);
                bool accepted = caller.Call("23600001", "23400077", sizeCode, window, 10000, PadMarker);
                Thread.Sleep(500);

                StringBuilder report = new StringBuilder();
                report.AppendLine("asked: packet size code " + sizeCode + ", window " + window);
                report.AppendLine("caller side:");
                report.Append(caller.Dump());
                lock (trunkOut)
                {
                    report.AppendLine("trunk to the Cisco, out:");
                    for (int i = 0; i < trunkOut.Count; i++)
                    {
                        report.AppendLine("  " + BitConverter.ToString(trunkOut[i], 0, Math.Min(trunkOut[i].Length, 40)));
                    }

                    report.AppendLine("trunk from the Cisco, in:");
                    for (int i = 0; i < trunkIn.Count; i++)
                    {
                        report.AppendLine("  " + BitConverter.ToString(trunkIn[i], 0, Math.Min(trunkIn[i].Length, 40)));
                    }
                }

                TestContext.Progress.WriteLine(report.ToString());
                Assert.That(accepted, Is.True, "the call was not accepted\n" + report);

                byte[]? ciscoAccept;
                byte[]? callerAccept = null;
                lock (trunkOut)
                {
                    ciscoAccept = trunkIn.Find(p => p.Length >= 3 && p[2] == 0x0F);
                }

                List<byte[]> received = caller.Received;
                for (int i = 0; i < received.Count; i++)
                {
                    if (received[i].Length >= 3 && received[i][2] == 0x0F)
                    {
                        callerAccept = received[i];
                    }
                }

                Assert.That(ciscoAccept, Is.Not.Null, "no Call Accepted came from the Cisco\n" + report);
                Assert.That(callerAccept, Is.Not.Null, "the caller got no Call Accepted\n" + report);

                // The facility pairs are written caller-to-callee first on every leg, so the caller must be told what the Cisco said.
                Assert.That(FacilityPair(callerAccept!, 0x42), Is.EqualTo(FacilityPair(ciscoAccept!, 0x42)), "packet size told to the caller differs from the Cisco's answer\n" + report);
                Assert.That(FacilityPair(callerAccept!, 0x43), Is.EqualTo(FacilityPair(ciscoAccept!, 0x43)), "window told to the caller differs from the Cisco's answer\n" + report);

                caller.Clear(0, 0);
                caller.WaitFor(p => p.Length >= 3 && p[2] == 0x17, 5000);
            }
            finally
            {
                access.Stop();
                trunk.Stop();
            }
        }

        /// <summary>
        /// Finds a two-octet facility (code followed by two parameter octets) in a Call Accepted packet and returns it as "aa-bb"; an empty
        /// string when the facility is not there. A Call Accepted is: three header octets, an address-length octet, the addresses
        /// (BCD, two digits per octet), the facility length octet and the facilities.
        /// </summary>
        private static string FacilityPair(byte[] callAccepted, byte code)
        {
            if (callAccepted.Length < 5)
            {
                return string.Empty;
            }

            int digits = (callAccepted[3] >> 4) + (callAccepted[3] & 0x0F);
            int at = 4 + ((digits + 1) / 2);
            if (at >= callAccepted.Length)
            {
                return string.Empty;
            }

            int end = Math.Min(callAccepted.Length, at + 1 + callAccepted[at]);
            at++;
            while (at + 2 < end + 0 && at < end)
            {
                byte c = callAccepted[at];
                int parameterLength = (c >> 6) switch { 0 => 1, 1 => 2, 2 => 3, _ => at + 1 < end ? 1 + callAccepted[at + 1] : 0 };
                if (c == code && at + 2 < callAccepted.Length)
                {
                    return callAccepted[at + 1].ToString("X2") + "-" + callAccepted[at + 2].ToString("X2");
                }

                at += 1 + parameterLength;
                if (parameterLength == 0)
                {
                    break;
                }
            }

            return string.Empty;
        }
    }
}
