//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;

namespace RetroX25.Tests
{
    /// <summary>
    /// The Q (qualified data), M (more data) and D (delivery confirmation) bits of X.25 data packets, driven through the client
    /// library (<see cref="X25Client.Send(X25Vc, byte[], int, bool, bool, bool)"/>) and read off the wire: a chain arrives whole and in order,
    /// D is carried and acknowledged, Q is carried, and our echo returns all three. The traffic tool has no options for these bits;
    /// it is a data generator, and the bits are a property of the library, so the library is what is tested.
    /// </summary>
    [TestFixture]
    public class QmdBitsTests
    {
        private const int Packets = 40;

        [Test]
        public void MoreData_IsSetOnAllButTheLastPacketOfEachMessage()
        {
            string pattern = string.Empty;
            for (int i = 0; i < 7; i++)
            {
                pattern += DeliveryTracker.MoreData(i, 3) ? "1" : "0";
            }

            Assert.That(pattern, Is.EqualTo("1101101"));
            Assert.That(DeliveryTracker.MoreData(5, 0), Is.False, "no chain, no M");
            Assert.That(DeliveryTracker.MoreData(5, 1), Is.False, "a chain of one is no chain");
        }

        [Test]
        public void Tracker_CountsDistinctRepeatedAndOutOfOrder()
        {
            DeliveryTracker tracker = new DeliveryTracker(100);
            byte[] p = new byte[8];
            long[] order = { 0, 1, 3, 2, 3, 4 };
            for (int i = 0; i < order.Length; i++)
            {
                DeliveryTracker.Stamp(p, order[i]);
                tracker.Record(p, p.Length);
            }

            Assert.That(tracker.Unique, Is.EqualTo(5));
            Assert.That(tracker.Duplicates, Is.EqualTo(1), "number 3 came twice");
            Assert.That(tracker.OutOfOrder, Is.EqualTo(1), "number 2 came after 3");
        }

        /// <summary>
        /// A chain of 4 full packets with the D bit, sent through the client library across our switch. The wire tap on the caller's
        /// connection shows M on three packets in four and D on all, numbers in order; the far end receives them whole and in order;
        /// and the far end has acknowledged every D-bit packet.
        /// </summary>
        [Test]
        public void Chain_AcrossASwitch_ArrivesWholeAndInOrder_WithDBitCarriedAndAcknowledged()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using XotWireTap tap = new XotWireTap("127.0.0.1", sw.P0.BoundPort);
            using X25Client receiver = new X25Client { ServiceName = "receiver" };
            using X25Client sender = new X25Client { ServiceName = "sender" };

            object gate = new object();
            System.Collections.Generic.List<(bool Q, bool M, bool D, byte[] Data)> got = new System.Collections.Generic.List<(bool, bool, bool, byte[])>();
            receiver.SetOnIncoming((c, vc) => c.Accept(vc));
            receiver.SetOnData((c, vc, data, length, q, m, d) =>
            {
                byte[] copy = new byte[length];
                Array.Copy(data, 0, copy, 0, length);
                lock (gate)
                {
                    got.Add((q, m, d, copy));
                }
            });
            receiver.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)sw.P1.BoundPort, LocalAddress = "23410001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            sender.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)tap.Port, LocalAddress = "23400001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            X25Vc? vc = sender.CallWait("23410001", 3000);
            Assert.That(vc, Is.Not.Null);
            BitsDriver.Send(sender, vc!, Packets, 4, false, true);
            Assert.That(sender.WaitForAcknowledgement(vc!, 5000), Is.True, "every D-bit packet is acknowledged");
            Assert.That(SpinWait.SpinUntil(() => { lock (gate) { return got.Count >= Packets; } }, 5000), Is.True, "all packets arrive");

            BitsDriver.AssertWire(tap, true, Packets, 4, false, true);
            lock (gate)
            {
                Assert.That(got.Count, Is.EqualTo(Packets));
                DeliveryTracker tracker = new DeliveryTracker(1000);
                for (int i = 0; i < got.Count; i++)
                {
                    Assert.That(got[i].M, Is.EqualTo(DeliveryTracker.MoreData(i, 4)), "M bit of packet " + i);
                    Assert.That(got[i].D, Is.True, "D bit of packet " + i);
                    Assert.That(got[i].Q, Is.False, "Q bit of packet " + i);
                    tracker.Record(got[i].Data, got[i].Data.Length);
                }

                Assert.That(tracker.Unique, Is.EqualTo(Packets));
                Assert.That(tracker.Duplicates, Is.EqualTo(0));
                Assert.That(tracker.OutOfOrder, Is.EqualTo(0));
            }
        }

        /// <summary>
        /// Our x25echo program in quiet mode (a byte for byte echo) returns every packet with the Q, M and D bits it came with.
        /// The wire tap on the caller's connection decodes the packets that came back.
        /// </summary>
        /// <param name="q">Q bit on every packet sent.</param>
        /// <param name="chain">Packets per message (0 = no chain).</param>
        /// <param name="d">D bit on every packet sent.</param>
        [TestCase(false, 4, true)]
        [TestCase(true, 0, false)]
        [TestCase(true, 3, true)]
        [TestCase(false, 0, true)]
        public void OurEcho_ReturnsTheBitsItWasSent(bool q, int chain, bool d)
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using XotWireTap tap = new XotWireTap("127.0.0.1", sw.P0.BoundPort);

            // The echo is the real x25echo program, started here and stopped by its process id.
            string exe = Path.Combine(AppContext.BaseDirectory, "x25echo.exe");
            Assert.That(File.Exists(exe), Is.True, "x25echo.exe is built next to the tests: " + exe);
            ProcessStartInfo start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-x");
            start.ArgumentList.Add("127.0.0.1:" + sw.P1.BoundPort);
            start.ArgumentList.Add("-a");
            start.ArgumentList.Add("23410001");
            start.ArgumentList.Add("-q");
            using Process echo = Process.Start(start)!;
            int echoPid = echo.Id;
            try
            {
                long returned = 0;
                using X25Client client = new X25Client { ServiceName = "bits-caller" };
                client.SetOnData((c, vc, data, length, qb, mb, db) => Interlocked.Increment(ref returned));
                client.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)tap.Port, LocalAddress = "23400001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

                // The echo needs a moment to attach to the switch; a call before that is cleared, so ask again.
                X25Vc? call = null;
                long until = Environment.TickCount64 + 15000;
                while (call == null && Environment.TickCount64 < until)
                {
                    call = client.CallWait("23410001", 2000);
                }

                Assert.That(call, Is.Not.Null, "the echo did not answer");
                BitsDriver.Send(client, call!, Packets, chain, q, d);
                Assert.That(SpinWait.SpinUntil(() => Interlocked.Read(ref returned) >= Packets, 10000), Is.True, "only " + returned + " of " + Packets + " came back");
                client.Clear(call!);

                BitsDriver.AssertWire(tap, true, Packets, chain, q, d);
                BitsDriver.AssertWire(tap, false, Packets, chain, q, d);
            }
            finally
            {
                if (!echo.HasExited)
                {
                    echo.Kill(true);
                    echo.WaitForExit(5000);
                }

                TestContext.Progress.WriteLine("x25echo process " + echoPid + " stopped");
            }
        }
    }
}
