//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroX25.Traffic;

namespace RetroX25.Tests
{
    /// <summary>
    /// x25traffic --qbit, --mbit-chain and --dbit: which bits go on which packet, the numbering check,
    /// and the bits really crossing a switch (a chain arrives whole and in order, D is carried and acknowledged).
    /// </summary>
    [TestFixture]
    public class TrafficBitsTests
    {
        [Test]
        public void Chain_SetsMOnAllButTheLastPacketOfEachMessage()
        {
            TrafficFlags flags = new TrafficFlags { Chain = 3 };
            string pattern = string.Empty;
            for (int i = 0; i < 7; i++)
            {
                flags.For(i, out _, out bool m, out _);
                pattern += m ? "1" : "0";
            }

            Assert.That(pattern, Is.EqualTo("1101101"));
        }

        [Test]
        public void NoChain_NeverSetsM_AndQAndDFollowTheOptions()
        {
            TrafficFlags flags = new TrafficFlags { QBit = true, DBit = true };
            flags.For(5, out bool q, out bool m, out bool d);
            Assert.That((q, m, d), Is.EqualTo((true, false, true)));
        }

        [TestCase(2, true)]
        [TestCase(16, true)]
        [TestCase(1, false)]
        [TestCase(17, false)]
        [TestCase(0, false)]
        public void Chain_AcceptsTwoToSixteenOnly(int value, bool accepted)
        {
            Assert.That(TrafficFlags.TryParseChain(value.ToString(), out int chain), Is.EqualTo(accepted));
            if (accepted)
            {
                Assert.That(chain, Is.EqualTo(value));
            }
        }

        [Test]
        public void Chain_NeedsFullPackets_UnlessTheDBitIsAlsoAsked()
        {
            Assert.That(new TrafficFlags { Chain = 2 }.Fits(64, 128), Is.False, "X.25 4.3.3: M on a partial packet needs D");
            Assert.That(new TrafficFlags { Chain = 2 }.Fits(128, 128), Is.True);
            Assert.That(new TrafficFlags { Chain = 2, DBit = true }.Fits(64, 128), Is.True);
            Assert.That(new TrafficFlags().Fits(64, 128), Is.True, "no chain, no rule");
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

        [Test]
        public void Chain_AcrossASwitch_ArrivesWholeAndInOrder_WithDBitCarriedAndAcknowledged()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using X25Client receiver = new X25Client { ServiceName = "receiver" };
            using X25Client sender = new X25Client { ServiceName = "sender" };

            object gate = new object();
            System.Collections.Generic.List<(bool M, bool D, byte[] Data)> got = new System.Collections.Generic.List<(bool, bool, byte[])>();
            receiver.SetOnIncoming((c, vc) => c.Accept(vc));
            receiver.SetOnData((c, vc, data, length, q, m, d) =>
            {
                byte[] copy = new byte[length];
                Array.Copy(data, 0, copy, 0, length);
                lock (gate)
                {
                    got.Add((m, d, copy));
                }
            });
            receiver.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)sw.P1.BoundPort, LocalAddress = "23410001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            sender.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)sw.P0.BoundPort, LocalAddress = "23400001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

            X25Vc? vc = sender.CallWait("23410001", 3000);
            Assert.That(vc, Is.Not.Null);
            int size = sender.GetPacketSizeOut(vc!);
            Assert.That(size, Is.GreaterThan(0));

            TrafficFlags flags = new TrafficFlags { Chain = 4, DBit = true };
            const int packets = 40; // ten messages of four packets
            byte[] payload = new byte[size];
            for (int i = 0; i < packets; i++)
            {
                DeliveryTracker.Stamp(payload, i);
                flags.For(i, out bool q, out bool m, out bool d);
                // The window is 7: a refused send is tried again, as x25traffic does.
                DateTime end = DateTime.UtcNow.AddSeconds(5);
                while (!sender.Send(vc!, payload, size, q, m, d))
                {
                    Assert.That(DateTime.UtcNow, Is.LessThan(end), "window never opened for packet " + i);
                    Thread.Yield();
                }
            }

            Assert.That(sender.WaitForAcknowledgement(vc!, 5000), Is.True, "every D-bit packet is acknowledged");

            DateTime wait = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < wait)
            {
                lock (gate)
                {
                    if (got.Count >= packets)
                    {
                        break;
                    }
                }

                Thread.Sleep(5);
            }

            lock (gate)
            {
                Assert.That(got.Count, Is.EqualTo(packets));
                DeliveryTracker tracker = new DeliveryTracker(1000);
                for (int i = 0; i < got.Count; i++)
                {
                    bool expectedM = (i % 4) != 3;
                    Assert.That(got[i].M, Is.EqualTo(expectedM), "M bit of packet " + i);
                    Assert.That(got[i].D, Is.True, "D bit of packet " + i);
                    tracker.Record(got[i].Data, got[i].Data.Length);
                }

                Assert.That(tracker.Unique, Is.EqualTo(packets));
                Assert.That(tracker.Duplicates, Is.EqualTo(0));
                Assert.That(tracker.OutOfOrder, Is.EqualTo(0));
            }
        }
    }
}
