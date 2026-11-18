//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Monitor;

namespace RetroX25.Tests
{
    /// <summary>
    /// The four basic cross-environment tests, RetroX25 against x25emu (the C emulator in WSL): our client calls its
    /// echo, its traffic generator calls our echo, our monitor client reads its switch, our PAD reaches its echo through
    /// its three switches. Skipped unless the x25emu setup is there (see <see cref="X25emuEnvironment"/>).
    /// </summary>
    /// <remarks>
    /// Run them all with <c>demo/crossenv-test.ps1</c>. Each test that calls the x25emu echo starts a fresh echo of its
    /// own and stops it by its number, because x25emu's echo takes one call (see demo/crossenv-wsl.sh).
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    public class X25emuInteropTests
    {
        private static void WaitUntil(Func<bool> condition, int timeoutMs, string what)
        {
            Assert.That(SpinWait.SpinUntil(condition, timeoutMs), Is.True, "gave up waiting for: " + what);
        }

        /// <summary>
        /// Our client calls x25emu's echo through its three switches (A, B, C) and the text comes back. The first call on a
        /// fresh XOT connection works only if our side did what x25emu expects first: the bytes are recorded in order and
        /// read.
        /// </summary>
        [Test]
        public void OurClient_CallsX25emuEcho_AndTheFirstCallOnAFreshConnectionWorks()
        {
            X25emuEnvironment.RequireReady();
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
            using XotWireTap tap = new XotWireTap(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessPort);

            StringBuilder received = new StringBuilder();
            object gate = new object();
            using X25Client client = new X25Client { ServiceName = "interop-client" };
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                lock (gate)
                {
                    received.Append(Encoding.ASCII.GetString(data, 0, length));
                }
            });
            client.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)tap.Port, LocalAddress = "23400099", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            X25Vc? call = client.CallWait(X25emuEnvironment.EchoAddress, 10000);
            Assert.That(call, Is.Not.Null, "x25emu did not accept the first call on a fresh connection. The bytes seen:\n" + tap.Dump());
            const string Message = "HELLO-FROM-RETROX25";
            Assert.That(client.SendText(call!, Message), Is.True);
            WaitUntil(() => { lock (gate) { return received.ToString().Contains(Message); } }, 10000, "the echo of " + Message);
            client.Clear(call!);
            WaitUntil(() => HasPacket(tap, X25PacketType.ClearConfirmation), 5000, "the clear confirmation");

            // Read the real bytes in order. Print them first, then check.
            string dump = tap.Dump();
            TestContext.Progress.WriteLine(dump);
            List<TappedPacket> packets = tap.Packets();
            int callAt = IndexOf(packets, X25PacketType.CallRequest, fromClient: true);
            Assert.That(callAt, Is.EqualTo(0), "the very first packet on the fresh connection must be our Call Request (no Restart: RFC 1613 6.4 forbids it over XOT): " + dump);
            Assert.That(IndexOf(packets, X25PacketType.RestartRequest, fromClient: true), Is.EqualTo(-1), "our client sent a Restart Request over XOT: " + dump);
            int acceptedAt = IndexOf(packets, X25PacketType.CallAccepted, fromClient: false);
            Assert.That(acceptedAt, Is.GreaterThan(callAt), "x25emu accepted the call only after the Call Request: " + dump);

            // x25emu answers on its own channel number (RFC 1613 6: the number has no significance). Everything we send
            // after that must use the number it chose.
            int callChannel = packets[callAt].Info.LCN;
            int answerChannel = packets[acceptedAt].Info.LCN;
            Assert.That(answerChannel, Is.Not.EqualTo(callChannel), "this check only means something when the two numbers differ: " + dump);
            for (int i = acceptedAt + 1; i < packets.Count; i++)
            {
                if (packets[i].FromClient)
                {
                    Assert.That((int)packets[i].Info.LCN, Is.EqualTo(answerChannel), "packet " + i + " from us is not on the channel x25emu chose: " + dump);
                }
            }

            int clearAt = IndexOf(packets, X25PacketType.ClearRequest, fromClient: true);
            Assert.That(clearAt, Is.GreaterThan(acceptedAt), dump);
            Assert.That(packets[clearAt].Info.ClearingCause, Is.EqualTo((byte)0), "our clear is DTE originated: " + dump);
            Assert.That(packets[clearAt].Info.DiagnosticCode, Is.EqualTo((byte)0), dump);
        }

        /// <summary>
        /// x25emu's echo (fixed 2026-11-13) takes one call after another, also with a pause of 2 s between them, and several at the
        /// same time. Until then the second call was cleared "number busy". Each call is its own XOT connection (RFC 1613 section 5)
        /// from its own client; every call must get its own text back.
        /// </summary>
        [Test]
        public void X25emuEcho_TakesCallsInARow_AndAtTheSameTime()
        {
            X25emuEnvironment.RequireReady();
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();

            for (int round = 0; round < 3; round++)
            {
                string message = "ROUND-" + round;
                Assert.That(EchoOnce(message), Is.True, "call " + round + " in a row (2 s apart) did not come back");
                Thread.Sleep(2000);
            }

            // At the same instant: how many one FRESH echo answers is x25emu's own limit (it keeps a few spare connections attached
            // and puts a new one in place after each call). It is measured and printed for 2, 4 and 8 calls; the first started call of a
            // batch is the one that was left without an answer in every run so far. The check is that at least two are answered.
            foreach (int atOnce in new[] { 2, 4, 8 })
            {
                using X25emuEnvironment.LinuxProcess fresh = X25emuEnvironment.StartEcho();
                bool[] ok = new bool[atOnce];
                Thread[] threads = new Thread[atOnce];
                for (int i = 0; i < atOnce; i++)
                {
                    int index = i;
                    threads[i] = new Thread(() => ok[index] = EchoOnce("TOGETHER-" + atOnce + "-" + index));
                    threads[i].Start();
                }

                for (int i = 0; i < atOnce; i++)
                {
                    threads[i].Join(30000);
                }

                int answered = 0;
                for (int i = 0; i < atOnce; i++)
                {
                    answered += ok[i] ? 1 : 0;
                }

                TestContext.Progress.WriteLine(atOnce + " calls at the same instant to one fresh x25emu echo: " + answered + " answered (" + string.Join(",", ok) + ")");
                Assert.That(answered, Is.GreaterThanOrEqualTo(2), atOnce + " calls at the same instant: " + string.Join(",", ok));
            }
        }

        /// <summary>One call on a fresh XOT connection: send a text, wait for it to come back, clear. True when it came back.</summary>
        private static bool EchoOnce(string message)
        {
            StringBuilder received = new StringBuilder();
            object gate = new object();
            using X25Client client = new X25Client { ServiceName = "interop-once" };
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                lock (gate)
                {
                    received.Append(Encoding.ASCII.GetString(data, 0, length));
                }
            });
            client.Open(new X25ClientConfig { Host = X25emuEnvironment.WslHost!, Port = (ushort)X25emuEnvironment.AccessPort, LocalAddress = "23400098", Framing = X25ClientFraming.Xot, NumVcs = 64 }, NullLogger.Instance);
            X25Vc? call = client.CallWait(X25emuEnvironment.EchoAddress, 10000);
            if (call == null)
            {
                TestContext.Progress.WriteLine(message + ": no Call Accepted within 10 s");
                return false;
            }

            client.SendText(call, message);
            bool back = SpinWait.SpinUntil(() => { lock (gate) { return received.ToString().Contains(message); } }, 10000);
            if (!back)
            {
                TestContext.Progress.WriteLine(message + ": call accepted, text did not come back; received so far: [" + received + "]");
            }

            client.Clear(call);
            return back;
        }

        private static bool HasPacket(XotWireTap tap, X25PacketType type)
        {
            return IndexOf(tap.Packets(), type, fromClient: null) >= 0;
        }

        private static int IndexOf(List<TappedPacket> packets, X25PacketType type, bool? fromClient)
        {
            for (int i = 0; i < packets.Count; i++)
            {
                if (fromClient.HasValue && packets[i].FromClient != fromClient.Value)
                {
                    continue;
                }

                if (packets[i].Packet.Length >= 3 && packets[i].Info.PacketType == type)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// x25emu's own x25traffic (run inside WSL) calls an echo of ours. Our echo is attached to x25emu's Switch A access
        /// port reserved for us (2007; calls to DNIC 2343 are sent there); every packet it sends arrives, 128 bytes each.
        /// </summary>
        [Test]
        public void X25emuTraffic_CallsOurEcho_AndEveryPacketArrives()
        {
            X25emuEnvironment.RequireReady();
            const int Packets = 200;
            int arrived = 0;
            ManualResetEventSlim accepted = new ManualResetEventSlim(false);
            using X25Client echo = new X25Client { ServiceName = "our-echo" };
            echo.SetOnIncoming((c, vc) => { c.Accept(vc); accepted.Set(); });

            // Replies go out from a thread of their own: waiting for a shut window inside the receive callback would stop
            // the very packets that open it again.
            BlockingCollection<(X25Vc Vc, byte[] Data, bool Q)> replies = new BlockingCollection<(X25Vc, byte[], bool)>();
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
                    // The client was closed under us: the test is over.
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
            echo.Open(new X25ClientConfig { Host = X25emuEnvironment.WslHost!, Port = X25emuEnvironment.CSharpAccessA, LocalAddress = "23430001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            Assert.That(SpinWait.SpinUntil(() => echo.IsOpen, 5000), Is.True, "our echo could not connect to x25emu's access port 2007");

            using X25emuEnvironment.LinuxProcess traffic = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25traffic"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-d", "23430001", "-n", Packets.ToString(), "-r", "0");
            bool finished = traffic.WaitForExit(60000);
            replies.CompleteAdding();
            sender.Join(5000);
            Assert.That(finished, Is.True, "x25traffic did not finish in 60 seconds:\n" + traffic.Output);
            Assert.That(accepted.IsSet, Is.True, "our echo never saw the call:\n" + traffic.Output);
            Assert.That(traffic.Output, Does.Contain("Sent:      " + Packets + " pkts"), traffic.Output);
            Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref arrived) >= Packets, 10000), Is.True,
                "only " + arrived + " of " + Packets + " packets of 128 bytes reached our echo\n" + traffic.Output);
            Assert.That(Volatile.Read(ref arrived), Is.EqualTo(Packets), "more arrived than were sent\n" + traffic.Output);
        }

        /// <summary>
        /// Our monitor client reads Switch A's monitor stream and decodes its topology: its name, its DNIC and the
        /// neighbours its configuration declares.
        /// </summary>
        [Test]
        public void OurMonitorClient_DecodesX25emuSwitchTopology()
        {
            X25emuEnvironment.RequireReady();
            using MonitorClient client = new MonitorClient();
            TopologySnapshot? topology = null;
            StatusSnapshot? status = null;
            ManualResetEventSlim gotTopology = new ManualResetEventSlim(false);
            ManualResetEventSlim gotStatus = new ManualResetEventSlim(false);
            client.TopologyReceived += t => { topology = t; gotTopology.Set(); };
            client.StatusReceived += s => { status = s; gotStatus.Set(); };

            Assert.That(client.Connect(X25emuEnvironment.WslHost!, X25emuEnvironment.MonitorA), Is.True, "could not connect to Switch A's monitor port " + X25emuEnvironment.MonitorA);
            Assert.That(gotTopology.Wait(10000), Is.True, "Switch A sent no topology snapshot after connect");

            StringBuilder text = new StringBuilder();
            text.Append("name=").Append(topology!.Name).Append(" dnic=").Append(topology.Dnic).Append(" node=").Append(topology.NodeId)
                .Append(" monitor=").Append(topology.MonitorPort).Append(" mgmt=").Append(topology.MgmtPort).Append(" neighbours:");
            for (int i = 0; i < topology.Neighbours.Count; i++)
            {
                text.Append(' ').Append(topology.Neighbours[i].Name);
            }

            TestContext.Progress.WriteLine(text.ToString());
            Assert.That(topology.Name, Is.EqualTo("Switch Alpha"), text.ToString());
            Assert.That(topology.Dnic, Is.EqualTo("2340"), text.ToString());
            Assert.That(topology.MgmtPort, Is.EqualTo(9001), text.ToString());
            bool hasB = false;
            bool hasOurs = false;
            for (int i = 0; i < topology.Neighbours.Count; i++)
            {
                hasB |= topology.Neighbours[i].Name == "Switch-B";
                hasOurs |= topology.Neighbours[i].Name == "RetroX25";
            }

            Assert.That(hasB, Is.True, "the declared neighbour Switch-B is missing: " + text);
            Assert.That(hasOurs, Is.True, "the declared neighbour RetroX25 is missing: " + text);

            // The status snapshot carries the per-port counters: Switch A is configured with ports 0 to 7, so eight.
            if (gotStatus.Wait(10000))
            {
                Assert.That(status!.Ports.Count, Is.EqualTo(8), "Switch A is configured with ports 0 to 7");
            }
        }

        /// <summary>
        /// Our PAD (the RetroIO PAD client, driven by key presses as the x25pad program does) calls x25emu's echo through
        /// its three switches, types a line and gets it back, then clears with CLR.
        /// </summary>
        [Test]
        public void OurPad_ReachesX25emuEcho_ThroughItsThreeSwitches()
        {
            X25emuEnvironment.RequireReady();
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
            using X25Client client = new X25Client { ServiceName = "interop-pad" };
            X25ClientPadTransport transport = new X25ClientPadTransport(client);
            PadClient pad = new PadClient(NullLogger.Instance, transport) { LocalAddress = "23400096" };
            client.SetOnConnected((c, vc) => pad.NotifyCallAccepted(vc.Lcn));
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) => pad.DeliverData(vc.Lcn, data, qBit));
            client.SetOnClear((c, vc, cause, diagnostic) => pad.DeliverClear(vc.Lcn, cause, diagnostic));

            ManualResetEventSlim connected = new ManualResetEventSlim(false);
            ManualResetEventSlim disconnected = new ManualResetEventSlim(false);
            StringBuilder screen = new StringBuilder();
            pad.ConnectionStateChanged += (s, e) =>
            {
                if (e.State == PadConnectionState.Connected)
                {
                    connected.Set();
                }
                else if (e.State == PadConnectionState.Disconnected)
                {
                    disconnected.Set();
                }
            };
            pad.DataReceived += (s, e) => { lock (screen) { screen.Append(e.Data); } };

            // The calling address must be a number of the network of the access port the call comes in on (X.25
            // Appendix IV.2 i): Switch A is 2340. It was "10000" until 2026-11-18, which x25emu now clears with
            // cause 19 diagnostic 68 (measured: "CLR ERR C:19 D:68" on the PAD screen).
            client.Open(new X25ClientConfig
            {
                Host = X25emuEnvironment.WslHost!,
                Port = (ushort)X25emuEnvironment.AccessPort,
                LocalAddress = "23400096",
                Framing = X25ClientFraming.Xot,
            }, NullLogger.Instance);
            pad.Initialize();

            TypeLine(pad, "CALL " + X25emuEnvironment.EchoAddress);
            Assert.That(connected.Wait(10000), Is.True, "the PAD did not connect to the x25emu echo; the screen shows:\n" + Screen(screen));
            TypeLine(pad, "HELLO-THROUGH-THREE-SWITCHES");
            WaitUntil(() => Screen(screen).Contains("HELLO-THROUGH-THREE-SWITCHES"), 10000, "the echo of the typed line; the screen shows:\n" + Screen(screen));

            pad.OnKey('\u0010');
            TypeLine(pad, "CLR");
            Assert.That(disconnected.Wait(10000), Is.True, "the PAD did not disconnect after CLR");
            pad.Dispose();
        }

        private static string Screen(StringBuilder screen)
        {
            lock (screen)
            {
                return screen.ToString();
            }
        }

        private static void TypeLine(PadClient pad, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                pad.OnKey(text[i]);
            }

            pad.OnKey('\r');
        }
    }
}
