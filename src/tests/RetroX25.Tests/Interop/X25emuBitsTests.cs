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
using RetroX25.Traffic;

namespace RetroX25.Tests
{
    /// <summary>
    /// The M bit (message chains) and the D bit (delivery confirmation) between RetroX25 and x25emu, both ways.
    /// Skipped unless the x25emu setup is there (see <see cref="X25emuEnvironment"/>).
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    public class X25emuBitsTests
    {
        private const int Chain = 4;
        private const int Packets = 400; // a multiple of Chain

        /// <summary>
        /// Our traffic code (chain of 4 with the D bit) calls x25emu's echo through its three switches. Every packet
        /// comes back, each number once and in order, with the M bit on three packets in four and the D bit on all,
        /// and the far end has acknowledged everything we sent.
        /// </summary>
        [Test]
        public void OurChainedDBitBurst_ToX25emuEcho_ComesBackWholeAndInOrder()
        {
            X25emuEnvironment.RequireReady();
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();

            DeliveryTracker tracker = new DeliveryTracker(10000);
            long m = 0;
            long d = 0;
            long received = 0;
            using X25Client client = new X25Client { ServiceName = "bits-client" };
            client.SetOnData((c, vc, data, length, q, mBit, dBit) =>
            {
                Interlocked.Increment(ref received);
                if (mBit) { Interlocked.Increment(ref m); }
                if (dBit) { Interlocked.Increment(ref d); }
                tracker.Record(data, length);
            });
            client.Open(new X25ClientConfig { Host = X25emuEnvironment.WslHost!, Port = (ushort)X25emuEnvironment.AccessPort, LocalAddress = "23400098", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            X25Vc? call = client.CallWait(X25emuEnvironment.EchoAddress, 10000);
            Assert.That(call, Is.Not.Null, "x25emu did not accept the call");
            int size = client.GetPacketSizeOut(call!);

            TrafficFlags flags = new TrafficFlags { Chain = Chain, DBit = true };
            byte[] payload = new byte[size];
            for (int i = 0; i < Packets; i++)
            {
                DeliveryTracker.Stamp(payload, i);
                flags.For(i, out bool q, out bool mb, out bool db);
                long deadline = Environment.TickCount64 + 10000;
                while (!client.Send(call!, payload, size, q, mb, db))
                {
                    Assert.That(Environment.TickCount64, Is.LessThan(deadline), "window never opened for packet " + i);
                    Thread.Yield();
                }
            }

            Assert.That(client.WaitForAcknowledgement(call!, 10000), Is.True, "x25emu did not acknowledge every D-bit packet");
            Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref received) >= Packets, 15000), Is.True, "only " + received + " of " + Packets + " came back");
            client.Clear(call!);

            TestContext.Progress.WriteLine($"sent {Packets}, back {received}, distinct {tracker.Unique}, repeated {tracker.Duplicates}, out of order {tracker.OutOfOrder}, M {m}, D {d}");
            Assert.That(received, Is.EqualTo(Packets));
            Assert.That(tracker.Unique, Is.EqualTo(Packets));
            Assert.That(tracker.Duplicates, Is.EqualTo(0));
            Assert.That(tracker.OutOfOrder, Is.EqualTo(0));
            Assert.That(m, Is.EqualTo(Packets / Chain * (Chain - 1)), "M bit on all but the last packet of each message");
            Assert.That(d, Is.EqualTo(Packets), "D bit on every packet");
        }

        /// <summary>
        /// x25emu's own x25traffic (chain of 4, D bit) calls an echo of ours that sends each packet back with the bits it
        /// came with. x25emu's report and our counts say the same: everything sent arrived, with M on three in four and D on all.
        /// </summary>
        [Test]
        public void X25emuChainedDBitBurst_ToOurEcho_ArrivesWholeAndTheBitsAreKept()
        {
            X25emuEnvironment.RequireReady();
            DeliveryTracker tracker = new DeliveryTracker(10000);
            long received = 0;
            long m = 0;
            long d = 0;
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
                Interlocked.Increment(ref received);
                if (mBit) { Interlocked.Increment(ref m); }
                if (dBit) { Interlocked.Increment(ref d); }
                tracker.Record(copy, length);
                replies.Add((vc, copy, q, mBit, dBit));
            });
            echo.Open(new X25ClientConfig { Host = X25emuEnvironment.WslHost!, Port = X25emuEnvironment.CSharpAccessA, LocalAddress = "23430001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            Assert.That(SpinWait.SpinUntil(() => echo.IsOpen, 5000), Is.True, "our echo could not connect to x25emu's access port 2007");

            using X25emuEnvironment.LinuxProcess traffic = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25traffic"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-d", "23430001",
                "-n", Packets.ToString(), "-r", "0", "--mbit-chain", Chain.ToString(), "--dbit");
            bool finished = traffic.WaitForExit(60000);
            replies.CompleteAdding();
            sender.Join(5000);
            Assert.That(finished, Is.True, "x25traffic did not finish in 60 seconds:\n" + traffic.Output);
            Assert.That(accepted.IsSet, Is.True, "our echo never saw the call:\n" + traffic.Output);

            TestContext.Progress.WriteLine(traffic.Output);
            TestContext.Progress.WriteLine($"our side: arrived {received}, distinct {tracker.Unique}, repeated {tracker.Duplicates}, out of order {tracker.OutOfOrder}, M {m}, D {d}");

            // x25emu's report, field by field.
            Assert.That(traffic.Output, Does.Contain("Sent:      " + Packets + " pkts"), traffic.Output);
            Assert.That(traffic.Output, Does.Contain("Received:  " + Packets + " pkts"), "everything sent came back to x25emu\n" + traffic.Output);
            Assert.That(traffic.Output, Does.Contain("Flags back: Q 0  M " + (Packets / Chain * (Chain - 1)) + "  D " + Packets + " of " + Packets + " packets"), traffic.Output);

            // Our side saw the same.
            Assert.That(received, Is.EqualTo(Packets));
            Assert.That(tracker.Unique, Is.EqualTo(Packets));
            Assert.That(tracker.OutOfOrder, Is.EqualTo(0));
            Assert.That(m, Is.EqualTo(Packets / Chain * (Chain - 1)));
            Assert.That(d, Is.EqualTo(Packets));
        }
    }
}
