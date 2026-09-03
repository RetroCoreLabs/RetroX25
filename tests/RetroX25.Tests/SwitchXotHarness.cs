using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Monitor;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroX25.Switch;
using RetroIO.Protocols.X25.Switch.Model;

namespace RetroX25.Tests
{
    /// <summary>
    /// Shared setup for socket-level switch interop tests: builds a two-port XOT switch on ephemeral loopback
    /// ports and runs a caller-to-echo scenario through it. Reused so the plain interop test and the
    /// monitoring test do not duplicate the wiring.
    /// </summary>
    internal static class SwitchXotHarness
    {
        /// <summary>
        /// A started two-port switch. Disposing stops the ports.
        /// </summary>
        internal sealed class Running : IDisposable
        {
            /// <summary>
            /// The ingress (caller-facing) port, id 0.
            /// </summary>
            public XotSwitchPort P0 { get; init; } = null!;

            /// <summary>
            /// The egress (echo-facing) port, id 1.
            /// </summary>
            public XotSwitchPort P1 { get; init; } = null!;

            /// <summary>
            /// The switch engine, kept referenced so its port subscriptions stay alive.
            /// </summary>
            public SwitchCore Core { get; init; } = null!;

            /// <inheritdoc/>
            public void Dispose()
            {
                P0.Stop();
                P1.Stop();
            }
        }

        /// <summary>
        /// Builds and starts a switch with two listening XOT ports on OS-assigned loopback ports; calls to
        /// 2341xxxx route to port 1.
        /// </summary>
        /// <returns>
        /// The started switch.
        /// </returns>
        public static Running StartTwoPortSwitch()
        {
            SwitchConfig cfg = new SwitchConfig { Dnic = "2340" };
            cfg.Routing.AddRoute(new X121Route
            {
                Type = X121RouteType.Prefix,
                Address = "2341",
                PrefixLen = 4,
                DestPort = 1
            });

            SwitchNode node = new SwitchNode(cfg);
            XotSwitchPort p0 = new XotSwitchPort(
                new PortConfig { Id = 0, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            XotSwitchPort p1 = new XotSwitchPort(
                new PortConfig { Id = 1, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            node.AddPort(p0);
            node.AddPort(p1);

            SwitchCore core = new SwitchCore(node);
            p0.Start();
            p1.Start();

            return new Running { P0 = p0, P1 = p1, Core = core };
        }

        /// <summary>
        /// Runs a full caller-to-echo scenario through the switch: an echo client connects to the egress
        /// port, a caller connects to the ingress port and calls the routed address, sends a message, and the
        /// echo returns. Asserts each step.
        /// </summary>
        /// <param name="sw">
        /// The started switch.
        /// </param>
        /// <param name="message">
        /// The message the caller sends and expects echoed.
        /// </param>
        public static void RunCallerEchoScenario(Running sw, string message)
        {
            using X25Client echo = new X25Client { ServiceName = "echo-callee" };
            using X25Client caller = new X25Client { ServiceName = "caller" };

            ManualResetEventSlim echoAccepted = new ManualResetEventSlim(false);
            echo.SetOnIncoming((c, vc) =>
            {
                c.Accept(vc);
                echoAccepted.Set();
            });
            echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                byte[] reply = new byte[length];
                Array.Copy(data, 0, reply, 0, length);
                c.Send(vc, reply, length, qBit);
            });
            echo.Open(new X25ClientConfig
            {
                Host = "127.0.0.1",
                Port = (ushort)sw.P1.ListenPort,
                LocalAddress = "23410001",
                Framing = X25ClientFraming.Xot
            }, NullLogger.Instance);

            byte[]? received = null;
            ManualResetEventSlim gotEcho = new ManualResetEventSlim(false);
            caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                received = new byte[length];
                Array.Copy(data, 0, received, 0, length);
                gotEcho.Set();
            });
            caller.Open(new X25ClientConfig
            {
                Host = "127.0.0.1",
                Port = (ushort)sw.P0.ListenPort,
                LocalAddress = "23400001",
                Framing = X25ClientFraming.Xot
            }, NullLogger.Instance);

            X25Vc? vc = caller.CallWait("23410001", 3000);
            Assert.That(vc, Is.Not.Null, "the call should be accepted by the switch");
            Assert.That(echoAccepted.Wait(3000), Is.True, "the echo endpoint should receive the onward call");

            Assert.That(caller.SendText(vc!, message), Is.True);
            Assert.That(gotEcho.Wait(3000), Is.True, "the echo should return through the switch");
            Assert.That(Encoding.UTF8.GetString(received!), Is.EqualTo(message));
        }

        /// <summary>
        /// Connects a monitoring viewer to a server port and pumps received bytes into the given reader on a
        /// background thread.
        /// </summary>
        /// <param name="port">
        /// The monitoring server port.
        /// </param>
        /// <param name="reader">
        /// The frame reader that decodes received messages.
        /// </param>
        /// <returns>
        /// The connected TCP client (dispose to disconnect).
        /// </returns>
        public static TcpClient ConnectViewer(int port, MonitorFrameReader reader)
        {
            TcpClient tcp = new TcpClient();
            tcp.Connect("127.0.0.1", port);
            NetworkStream stream = tcp.GetStream();

            Thread readThread = new Thread(() =>
            {
                byte[] buffer = new byte[8192];
                while (true)
                {
                    int n;
                    try
                    {
                        n = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    if (n <= 0)
                    {
                        break;
                    }

                    reader.ProcessBytes(buffer.AsSpan(0, n));
                }
            })
            {
                IsBackground = true
            };
            readThread.Start();
            return tcp;
        }
    }
}
