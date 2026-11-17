//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Pad;

namespace RetroX25.Tests
{
    /// <summary>
    /// x25pad --telnet-port: a telnet client types at an X.28 PAD, which calls out through XOT.
    /// </summary>
    /// <remarks>
    /// The far end is a small echo that puts "ECHO:" in front of what it receives, so the test can tell
    /// text that came back over X.25 from text the PAD echoed locally while the person typed.
    /// </remarks>
    [TestFixture]
    public class PadTelnetTests
    {
        [Test]
        public void TelnetClient_CallsThroughTheSwitch_AndGetsTheEchoBack()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using X25Client echo = StartPrefixEcho((ushort)sw.P1.BoundPort);

            int telnetPort = FreePort();
            using TelnetPadHost host = new TelnetPadHost("127.0.0.1", (ushort)sw.P0.BoundPort, 0, NullLogger.Instance, NullLogger.Instance);
            Assert.That(host.Start(new TelnetPadConfig { Enabled = true, Port = telnetPort, BindAddress = "127.0.0.1" }), Is.True, host.GetStatus().Text);

            using TcpClient telnet = new TcpClient();
            telnet.Connect(IPAddress.Loopback, telnetPort);
            NetworkStream stream = telnet.GetStream();
            stream.ReadTimeout = 200;
            StringBuilder seen = new StringBuilder();

            Assert.That(ReadUntil(stream, seen, "PAD", 5000), Is.True, "the PAD greets the telnet client. Saw: " + seen);

            Send(stream, "CALL 23410001\r");
            Assert.That(ReadUntil(stream, seen, "COM", 5000), Is.True, "the call connects (service signal COM). Saw: " + seen);

            Send(stream, "hello\r");
            Assert.That(ReadUntil(stream, seen, "ECHO:hello", 5000), Is.True, "the text comes back over X.25 from the far end. Saw: " + seen);
        }

        [Test]
        public void TelnetClient_Closing_ClearsTheCall_AndFreesTheSession()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using X25Client echo = StartPrefixEcho((ushort)sw.P1.BoundPort);

            int telnetPort = FreePort();
            using TelnetPadHost host = new TelnetPadHost("127.0.0.1", (ushort)sw.P0.BoundPort, 0, NullLogger.Instance, NullLogger.Instance);
            Assert.That(host.Start(new TelnetPadConfig { Enabled = true, Port = telnetPort, BindAddress = "127.0.0.1" }), Is.True);

            using (TcpClient telnet = new TcpClient())
            {
                telnet.Connect(IPAddress.Loopback, telnetPort);
                NetworkStream stream = telnet.GetStream();
                stream.ReadTimeout = 200;
                StringBuilder seen = new StringBuilder();
                Assert.That(ReadUntil(stream, seen, "PAD", 5000), Is.True);
                Send(stream, "CALL 23410001\r");
                Assert.That(ReadUntil(stream, seen, "COM", 5000), Is.True, "Saw: " + seen);
            }

            // The session count drops back to 0 once the client has gone and the PAD is released.
            DateTime end = DateTime.UtcNow.AddSeconds(5);
            while (host.GetStatus().Sessions != 0 && DateTime.UtcNow < end)
            {
                Thread.Sleep(20);
            }

            Assert.That(host.GetStatus().Sessions, Is.EqualTo(0), "the session ends when the telnet client closes");
        }

        /// <summary>
        /// A PAD call carries the X.29 call user data (01 00 00 00) by itself: nothing is typed or switched on for it. The
        /// Call Request is decoded off the wire between the telnet PAD and the switch. A router's PAD refuses a call without it.
        /// </summary>
        [Test]
        public void TelnetPadCall_CarriesTheX29CallUserData_Automatically()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using X25Client echo = StartPrefixEcho((ushort)sw.P1.BoundPort);
            using XotWireTap tap = new XotWireTap("127.0.0.1", sw.P0.BoundPort);

            int telnetPort = FreePort();
            using TelnetPadHost host = new TelnetPadHost("127.0.0.1", (ushort)tap.Port, 0, NullLogger.Instance, NullLogger.Instance);
            Assert.That(host.Start(new TelnetPadConfig { Enabled = true, Port = telnetPort, BindAddress = "127.0.0.1" }), Is.True, host.GetStatus().Text);

            using TcpClient telnet = new TcpClient();
            telnet.Connect(IPAddress.Loopback, telnetPort);
            NetworkStream stream = telnet.GetStream();
            stream.ReadTimeout = 200;
            StringBuilder seen = new StringBuilder();
            Assert.That(ReadUntil(stream, seen, "PAD", 5000), Is.True, "Saw: " + seen);
            Send(stream, "CALL 23410001\r");
            Assert.That(ReadUntil(stream, seen, "COM", 5000), Is.True, "Saw: " + seen);

            System.Collections.Generic.List<TappedPacket> packets = tap.Packets();
            int calls = 0;
            for (int i = 0; i < packets.Count; i++)
            {
                if (packets[i].FromClient && packets[i].Info.PacketType == RetroIO.Protocols.X25.Models.X25PacketType.CallRequest)
                {
                    calls++;
                    Assert.That(packets[i].Info.CallUserData, Is.EqualTo(new byte[] { 0x01, 0x00, 0x00, 0x00 }), tap.Dump());
                }
            }

            Assert.That(calls, Is.EqualTo(1), tap.Dump());
        }

        /// <summary>
        /// x25pad -n CARD7 with --telnet-port: a telnet session starts as if <c>IDCARD7</c> had been typed, so its Call
        /// Request carries the NUI selection facility (X.25 7.3.7: code C6, a length, the NUI's octets) without anything
        /// being typed for it. Decoded off the wire between the PAD and the switch.
        /// </summary>
        [Test]
        public void TelnetPadStartedWithANui_ItsCallCarriesTheNuiFacility()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using X25Client echo = StartPrefixEcho((ushort)sw.P1.BoundPort);
            using XotWireTap tap = new XotWireTap("127.0.0.1", sw.P0.BoundPort);

            int telnetPort = FreePort();
            using TelnetPadHost host = new TelnetPadHost("127.0.0.1", (ushort)tap.Port, 0, NullLogger.Instance, NullLogger.Instance, "CARD7");
            Assert.That(host.Start(new TelnetPadConfig { Enabled = true, Port = telnetPort, BindAddress = "127.0.0.1" }), Is.True, host.GetStatus().Text);

            using TcpClient telnet = new TcpClient();
            telnet.Connect(IPAddress.Loopback, telnetPort);
            NetworkStream stream = telnet.GetStream();
            stream.ReadTimeout = 200;
            StringBuilder seen = new StringBuilder();
            Assert.That(ReadUntil(stream, seen, "PAD", 5000), Is.True, "Saw: " + seen);
            Send(stream, "CALL 23410001\r");
            Assert.That(SpinWait.SpinUntil(() => tap.Packets().Exists(p => p.FromClient && p.Info.PacketType == RetroIO.Protocols.X25.Models.X25PacketType.CallRequest), 5000), Is.True, tap.Dump());

            TappedPacket call = tap.Packets().Find(p => p.FromClient && p.Info.PacketType == RetroIO.Protocols.X25.Models.X25PacketType.CallRequest)!;
            string facilities = BitConverter.ToString(call.Info.Facilities);
            Assert.That(facilities, Does.Contain("C6-05-43-41-52-44-37"), "C6, length 5, \"CARD7\". " + tap.Dump());
            Assert.That(seen.ToString(), Does.Not.Contain("CARD7"), "the NUI is never shown to the telnet user");
        }

        /// <summary>
        /// The command-line PAD (keyboard mode builds its PAD the same way as telnet mode) puts the same call user data on its
        /// Call Request.
        /// </summary>
        [Test]
        public void CommandLinePad_CallRequest_CarriesTheX29CallUserData()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using X25Client echo = StartPrefixEcho((ushort)sw.P1.BoundPort);
            using XotWireTap tap = new XotWireTap("127.0.0.1", sw.P0.BoundPort);

            Program.OpenPad("127.0.0.1", (ushort)tap.Port, 0, NullLogger.Instance, out X25Client client, out RetroIO.Protocols.X25.Modules.PadClient pad);
            using (client)
            using (pad)
            {
                Assert.That(pad.Connect("23410001"), Is.True);
                Assert.That(SpinWait.SpinUntil(() => tap.Packets().Exists(p => !p.FromClient && p.Info.PacketType == RetroIO.Protocols.X25.Models.X25PacketType.CallAccepted), 5000), Is.True, tap.Dump());
                TappedPacket call = tap.Packets().Find(p => p.FromClient && p.Info.PacketType == RetroIO.Protocols.X25.Models.X25PacketType.CallRequest)!;
                Assert.That(call.Info.CallUserData, Is.EqualTo(new byte[] { 0x01, 0x00, 0x00, 0x00 }), tap.Dump());
            }
        }

        private static X25Client StartPrefixEcho(ushort switchPort)
        {
            X25Client echo = new X25Client { ServiceName = "prefix-echo" };
            echo.SetOnIncoming((c, vc) => c.Accept(vc));
            echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                byte[] reply = new byte[length + 5];
                Encoding.ASCII.GetBytes("ECHO:").CopyTo(reply, 0);
                Array.Copy(data, 0, reply, 5, length);
                c.Send(vc, reply, reply.Length, qBit);
            });
            echo.Open(new X25ClientConfig
            {
                Host = "127.0.0.1",
                Port = switchPort,
                LocalAddress = "23410001",
                Framing = X25ClientFraming.Xot
            }, NullLogger.Instance);
            return echo;
        }

        private static int FreePort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static void Send(NetworkStream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        // Reads what arrives (dropping telnet option bytes' effect by plain substring search) until the wanted text shows.
        private static bool ReadUntil(NetworkStream stream, StringBuilder seen, string wanted, int milliseconds)
        {
            byte[] buffer = new byte[512];
            DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end)
            {
                if (seen.ToString().Contains(wanted))
                {
                    return true;
                }

                try
                {
                    int n = stream.Read(buffer, 0, buffer.Length);
                    if (n == 0)
                    {
                        break;
                    }

                    seen.Append(Encoding.ASCII.GetString(buffer, 0, n));
                }
                catch (IOException)
                {
                    // read timeout: loop and check the clock
                }
            }

            return seen.ToString().Contains(wanted);
        }
    }
}
