//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;

namespace RetroX25.Tests
{
    /// <summary>
    /// The M bit (message chains), the D bit (delivery confirmation) and the Q bit between RetroX25 and x25emu, both ways. Our client
    /// library sends the bits (x25emu's tools have no options for them), and a wire tap decodes the packets.
    /// Skipped unless the x25emu setup is there (see <see cref="X25emuEnvironment"/>).
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    public class X25emuBitsTests
    {
        private const int Chain = 4;
        private const int Packets = 400; // a multiple of Chain

        /// <summary>
        /// Our client (chain of 4 with the D bit) calls x25emu's echo through its three switches. The tap on our connection
        /// shows what we sent and what came back: every packet, numbers in order, the M bit on three packets in four and the D bit
        /// on all; and the far end has acknowledged everything we sent.
        /// </summary>
        [Test]
        public void OurChainedDBitBurst_ToX25emuEcho_ComesBackWholeAndInOrder()
        {
            X25emuEnvironment.RequireReady();
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
            using XotWireTap tap = new XotWireTap(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessPort);

            DeliveryTracker tracker = new DeliveryTracker(10000);
            long m = 0;
            long d = 0;
            long received = 0;
            object gate = new object();
            using X25Client client = new X25Client { ServiceName = "bits-client" };
            client.SetOnData((c, vc, data, length, q, mBit, dBit) =>
            {
                lock (gate)
                {
                    received++;
                    if (mBit) { m++; }
                    if (dBit) { d++; }
                    tracker.Record(data, length);
                }
            });
            client.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)tap.Port, LocalAddress = "23400098", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            X25Vc? call = client.CallWait(X25emuEnvironment.EchoAddress, 10000);
            Assert.That(call, Is.Not.Null, "x25emu did not accept the call");
            BitsDriver.Send(client, call!, Packets, Chain, false, true);

            Assert.That(client.WaitForAcknowledgement(call!, 10000), Is.True, "x25emu did not acknowledge every D-bit packet");
            Assert.That(SpinWait.SpinUntil(() => { lock (gate) { return received >= Packets; } }, 15000), Is.True, "only " + received + " of " + Packets + " came back");
            client.Clear(call!);

            lock (gate)
            {
                TestContext.Progress.WriteLine($"sent {Packets}, back {received}, distinct {tracker.Unique}, repeated {tracker.Duplicates}, out of order {tracker.OutOfOrder}, M {m}, D {d}");
                Assert.That(received, Is.EqualTo(Packets));
                Assert.That(tracker.Unique, Is.EqualTo(Packets));
                Assert.That(tracker.Duplicates, Is.EqualTo(0));
                Assert.That(tracker.OutOfOrder, Is.EqualTo(0));
                Assert.That(m, Is.EqualTo(Packets / Chain * (Chain - 1)), "M bit on all but the last packet of each message");
                Assert.That(d, Is.EqualTo(Packets), "D bit on every packet");
            }

            BitsDriver.AssertWire(tap, true, Packets, Chain, false, true);
            BitsDriver.AssertWire(tap, false, Packets, Chain, false, true);
        }

        /// <summary>
        /// Our client (chain of 4 with the D bit) calls an echo of ours that is attached to x25emu's Switch A, so x25emu carries the
        /// packets to a far end that is ours. Our echo sends each packet back with the bits it came with. The tap on the caller's
        /// connection decodes both directions; the echo's counts say the same.
        /// </summary>
        [Test]
        public void OurChainedDBitBurst_ThroughX25emu_ToOurEcho_ArrivesWholeAndTheBitsAreKept()
        {
            X25emuEnvironment.RequireReady();
            DeliveryTracker tracker = new DeliveryTracker(10000);
            long received = 0;
            long m = 0;
            long d = 0;
            object gate = new object();
            ManualResetEventSlim accepted = new ManualResetEventSlim(false);
            using X25Client echo = new X25Client { ServiceName = "bits-echo" };
            echo.SetOnIncoming((c, vc) => { c.Accept(vc); accepted.Set(); });

            // Replies go out from a thread of their own: waiting for a shut window inside the receive callback would stop
            // the very packets that open it again.
            BlockingCollection<(X25Vc Vc, byte[] Data, bool Q, bool M, bool D)> replies = new BlockingCollection<(X25Vc, byte[], bool, bool, bool)>();
            Thread sender = new Thread(() =>
            {
                try
                {
                    foreach ((X25Vc Vc, byte[] Data, bool Q, bool M, bool D) r in replies.GetConsumingEnumerable())
                    {
                        long deadline = Environment.TickCount64 + 10000;
                        while (!echo.Send(r.Vc, r.Data, r.Data.Length, r.Q, r.M, r.D) && Environment.TickCount64 < deadline)
                        {
                            Thread.Yield();
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // The client was closed under us: the test is over.
                }
            })
            { IsBackground = true, Name = "bits-echo-sender" };
            sender.Start();
            echo.SetOnData((c, vc, data, length, q, mBit, dBit) =>
            {
                byte[] copy = new byte[length];
                Array.Copy(data, 0, copy, 0, length);
                lock (gate)
                {
                    received++;
                    if (mBit) { m++; }
                    if (dBit) { d++; }
                    tracker.Record(copy, length);
                }

                replies.Add((vc, copy, q, mBit, dBit));
            });
            echo.Open(new X25ClientConfig { Host = X25emuEnvironment.WslHost!, Port = X25emuEnvironment.CSharpAccessA, LocalAddress = "23430001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            Assert.That(SpinWait.SpinUntil(() => echo.IsOpen, 5000), Is.True, "our echo could not connect to x25emu's access port 2007");

            using XotWireTap tap = new XotWireTap(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessPort);
            long back = 0;
            using X25Client caller = new X25Client { ServiceName = "bits-caller" };
            caller.SetOnData((c, vc, data, length, q, mBit, dBit) => Interlocked.Increment(ref back));
            caller.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)tap.Port, LocalAddress = "23400097", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            X25Vc? call = caller.CallWait("23430001", 10000);
            Assert.That(call, Is.Not.Null, "x25emu did not carry the call to our echo");
            BitsDriver.Send(caller, call!, Packets, Chain, false, true);

            Assert.That(caller.WaitForAcknowledgement(call!, 10000), Is.True, "every D-bit packet was acknowledged");
            Assert.That(SpinWait.SpinUntil(() => Interlocked.Read(ref back) >= Packets, 15000), Is.True, "only " + back + " came back to the caller");
            caller.Clear(call!);
            replies.CompleteAdding();
            sender.Join(5000);
            Assert.That(accepted.IsSet, Is.True, "our echo never saw the call");

            lock (gate)
            {
                TestContext.Progress.WriteLine($"our echo: arrived {received}, distinct {tracker.Unique}, repeated {tracker.Duplicates}, out of order {tracker.OutOfOrder}, M {m}, D {d}");
                Assert.That(received, Is.EqualTo(Packets));
                Assert.That(tracker.Unique, Is.EqualTo(Packets));
                Assert.That(tracker.OutOfOrder, Is.EqualTo(0));
                Assert.That(m, Is.EqualTo(Packets / Chain * (Chain - 1)));
                Assert.That(d, Is.EqualTo(Packets));
            }

            BitsDriver.AssertWire(tap, true, Packets, Chain, false, true);
            BitsDriver.AssertWire(tap, false, Packets, Chain, false, true);
        }
    }
}
