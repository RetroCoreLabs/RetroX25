//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Models;

namespace RetroX25.Tests
{
    /// <summary>
    /// Test helper for the Q, M and D bit tests: numbers the packets a test sends (first four bytes, big
    /// endian), says which header bits packet number N carries, and checks what comes back. The traffic
    /// tool does none of this; the tests drive the client library directly.
    /// </summary>
    internal sealed class DeliveryTracker
    {
        /// <summary>Bytes at the start of each payload that carry the number.</summary>
        public const int SequenceBytes = 4;

        private readonly bool[] _seen;
        private long _highest = -1;

        /// <summary>
        /// Creates a tracker that remembers the first <paramref name="capacity"/> numbers.
        /// </summary>
        /// <param name="capacity">How many distinct numbers to remember.</param>
        public DeliveryTracker(int capacity)
        {
            _seen = new bool[capacity];
        }

        /// <summary>Distinct numbers received.</summary>
        public long Unique { get; private set; }

        /// <summary>Numbers received a second time.</summary>
        public long Duplicates { get; private set; }

        /// <summary>Numbers that arrived after a higher number.</summary>
        public long OutOfOrder { get; private set; }

        /// <summary>
        /// Writes the number into the first bytes of a payload.
        /// </summary>
        /// <param name="payload">The payload to stamp.</param>
        /// <param name="sequence">The number.</param>
        public static void Stamp(byte[] payload, long sequence)
        {
            uint s = (uint)sequence;
            payload[0] = (byte)(s >> 24);
            payload[1] = (byte)(s >> 16);
            payload[2] = (byte)(s >> 8);
            payload[3] = (byte)s;
        }

        /// <summary>
        /// Reads the number out of a payload.
        /// </summary>
        /// <param name="data">The buffer.</param>
        /// <returns>The number.</returns>
        public static long Read(byte[] data)
        {
            return ((long)data[0] << 24) | ((long)data[1] << 16) | ((long)data[2] << 8) | data[3];
        }

        /// <summary>
        /// Whether packet number <paramref name="index"/> carries the M bit when messages are
        /// <paramref name="chain"/> packets long: M on every packet except the last of its message.
        /// </summary>
        /// <param name="index">Packet number counted from 0.</param>
        /// <param name="chain">Packets per message (0 or 1: no chain).</param>
        /// <returns>True when M is set on this packet.</returns>
        public static bool MoreData(long index, int chain)
        {
            return chain > 1 && (index % chain) != chain - 1;
        }

        /// <summary>
        /// Records one received payload.
        /// </summary>
        /// <param name="data">The buffer holding the payload.</param>
        /// <param name="length">Valid bytes in <paramref name="data"/>.</param>
        public void Record(byte[] data, int length)
        {
            if (length < SequenceBytes)
            {
                throw new InvalidOperationException("a numbered packet has at least " + SequenceBytes + " bytes");
            }

            long seq = Read(data);
            if (_seen[seq])
            {
                Duplicates++;
                return;
            }

            _seen[seq] = true;
            Unique++;

            // X.25 delivers in sequence on one circuit; a number below one already seen means
            // something reordered the packets.
            if (seq < _highest)
            {
                OutOfOrder++;
            }
            else
            {
                _highest = seq;
            }
        }
    }

    /// <summary>
    /// Sends numbered data packets through the client library with the Q, M and D bits a test asks for, and
    /// checks the packets the wire tap saw.
    /// </summary>
    internal static class BitsDriver
    {
        /// <summary>
        /// Sends <paramref name="packets"/> numbered full-size packets. A refused send (window shut) is tried
        /// again until the window opens.
        /// </summary>
        /// <param name="client">The calling client.</param>
        /// <param name="vc">The open call.</param>
        /// <param name="packets">How many packets.</param>
        /// <param name="chain">Packets per message; M is set on all but the last of each.</param>
        /// <param name="q">Q bit on every packet.</param>
        /// <param name="d">D bit on every packet.</param>
        public static void Send(X25Client client, X25Vc vc, int packets, int chain, bool q, bool d)
        {
            int size = client.GetPacketSizeOut(vc);
            Assert.That(size, Is.GreaterThan(0));
            byte[] payload = new byte[size];
            for (int i = 0; i < packets; i++)
            {
                DeliveryTracker.Stamp(payload, i);
                bool m = DeliveryTracker.MoreData(i, chain);
                long deadline = Environment.TickCount64 + 10000;
                while (!client.Send(vc, payload, size, q, m, d))
                {
                    Assert.That(Environment.TickCount64, Is.LessThan(deadline), "window never opened for packet " + i);
                    Thread.Yield();
                }
            }
        }

        /// <summary>
        /// Reads the data packets of one direction off the wire tap, in order, and checks each one: its number,
        /// and its Q, M and D bits against what was asked for.
        /// </summary>
        /// <param name="tap">The tap.</param>
        /// <param name="fromClient">True for the packets the caller sent, false for the ones that came back.</param>
        /// <param name="packets">How many data packets must be there.</param>
        /// <param name="chain">The chain length used.</param>
        /// <param name="q">Expected Q on every packet.</param>
        /// <param name="d">Expected D on every packet.</param>
        public static void AssertWire(XotWireTap tap, bool fromClient, int packets, int chain, bool q, bool d)
        {
            List<TappedPacket> all = tap.Packets();
            int seen = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].FromClient != fromClient)
                {
                    continue;
                }

                X25PacketInfo info = all[i].Info;
                if (info.PacketType != X25PacketType.Data)
                {
                    continue;
                }

                string where = "data packet " + seen + (fromClient ? " sent" : " returned") + "\n" + tap.Dump();
                Assert.That(DeliveryTracker.Read(info.UserData), Is.EqualTo(seen), "number of " + where);
                Assert.That(info.QBit, Is.EqualTo(q), "Q bit of " + where);
                Assert.That(info.MBit, Is.EqualTo(DeliveryTracker.MoreData(seen, chain)), "M bit of " + where);
                Assert.That(info.DBit, Is.EqualTo(d), "D bit of " + where);
                seen++;
            }

            Assert.That(seen, Is.EqualTo(packets), "data packets on the wire, direction " + (fromClient ? "sent" : "returned") + "\n" + tap.Dump());
        }
    }
}
