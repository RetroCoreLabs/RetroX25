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
