//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;

namespace RetroX25.Traffic
{
    /// <summary>
    /// Which header bits x25traffic puts on its data packets: Q on every packet, M on every packet of a
    /// chain except the last, D on every packet.
    /// </summary>
    internal sealed class TrafficFlags
    {
        /// <summary>The most packets one chained message may have.</summary>
        public const int MaxChain = 16;

        /// <summary>Send every packet as qualified data (Q bit).</summary>
        public bool QBit { get; set; }

        /// <summary>
        /// Packets per message. 0 or 1 means no chain; 2 to <see cref="MaxChain"/> sends each message as that
        /// many linked packets (M bit set on all but the last).
        /// </summary>
        public int Chain { get; set; }

        /// <summary>Ask for delivery confirmation (D bit) on every packet.</summary>
        public bool DBit { get; set; }

        /// <summary>
        /// True when any of the three bits is asked for.
        /// </summary>
        public bool Any => QBit || Chain > 1 || DBit;

        /// <summary>
        /// Reads a chain length.
        /// </summary>
        /// <param name="text">The text after <c>--mbit-chain</c>.</param>
        /// <param name="chain">The number, 2 to <see cref="MaxChain"/>.</param>
        /// <returns>False when the text is not a whole number in that range.</returns>
        public static bool TryParseChain(string? text, out int chain)
        {
            return int.TryParse(text, out chain) && chain >= 2 && chain <= MaxChain;
        }

        /// <summary>
        /// The bits for packet number <paramref name="index"/> (counted from 0 over the whole run).
        /// </summary>
        /// <param name="index">Packet number.</param>
        /// <param name="q">Q bit for this packet.</param>
        /// <param name="m">M bit: set unless this packet ends its message.</param>
        /// <param name="d">D bit for this packet.</param>
        public void For(long index, out bool q, out bool m, out bool d)
        {
            q = QBit;
            d = DBit;
            m = Chain > 1 && (index % Chain) != Chain - 1;
        }

        /// <summary>
        /// Checks that the packets can carry the M bit. X.25 4.3.3 allows the more-data mark on a packet
        /// shorter than the circuit's packet size only together with the D bit.
        /// </summary>
        /// <param name="packetSize">Data bytes in each packet we send.</param>
        /// <param name="circuitPacketSize">The circuit's negotiated packet size.</param>
        /// <returns>True when the combination is allowed.</returns>
        public bool Fits(int packetSize, int circuitPacketSize)
        {
            return !(Chain > 1 && !DBit && packetSize != circuitPacketSize);
        }
    }

    /// <summary>
    /// Numbers the packets we send (first four bytes, big endian) and checks what comes back: how many
    /// distinct numbers, repeats, and numbers that arrive after a higher one. Safe to call from the
    /// data callback thread while the main thread reads the totals.
    /// </summary>
    internal sealed class DeliveryTracker
    {
        /// <summary>Bytes at the start of each payload that carry the number.</summary>
        public const int SequenceBytes = 4;

        private readonly object _lock = new object();
        private readonly bool[] _seen;
        private long _unique;
        private long _duplicates;
        private long _outOfOrder;
        private long _tooShort;
        private long _untracked;
        private long _highest = -1;

        /// <summary>
        /// Creates a tracker that remembers the first <paramref name="capacity"/> numbers.
        /// </summary>
        /// <param name="capacity">How many distinct numbers to remember.</param>
        public DeliveryTracker(int capacity = 1 << 20)
        {
            _seen = new bool[capacity];
        }

        /// <summary>Distinct numbers received.</summary>
        public long Unique { get { lock (_lock) { return _unique; } } }

        /// <summary>Numbers received a second time.</summary>
        public long Duplicates { get { lock (_lock) { return _duplicates; } }  }

        /// <summary>Numbers that arrived after a higher number.</summary>
        public long OutOfOrder { get { lock (_lock) { return _outOfOrder; } } }

        /// <summary>Packets too short to carry a number.</summary>
        public long TooShort { get { lock (_lock) { return _tooShort; } } }

        /// <summary>Numbers past what the tracker remembers (counted, not checked).</summary>
        public long Untracked { get { lock (_lock) { return _untracked; } } }

        /// <summary>
        /// Writes the number into the first bytes of a payload.
        /// </summary>
        /// <param name="payload">The payload to stamp; shorter than four bytes is left alone.</param>
        /// <param name="sequence">The number.</param>
        public static void Stamp(byte[] payload, long sequence)
        {
            if (payload.Length < SequenceBytes)
            {
                return;
            }

            uint s = (uint)sequence;
            payload[0] = (byte)(s >> 24);
            payload[1] = (byte)(s >> 16);
            payload[2] = (byte)(s >> 8);
            payload[3] = (byte)s;
        }

        /// <summary>
        /// Records one received payload.
        /// </summary>
        /// <param name="data">The buffer holding the payload.</param>
        /// <param name="length">Valid bytes in <paramref name="data"/>.</param>
        public void Record(byte[] data, int length)
        {
            lock (_lock)
            {
                if (length < SequenceBytes)
                {
                    _tooShort++;
                    return;
                }

                long seq = ((long)data[0] << 24) | ((long)data[1] << 16) | ((long)data[2] << 8) | data[3];
                if (seq >= _seen.Length)
                {
                    _untracked++;
                    return;
                }

                if (_seen[seq])
                {
                    _duplicates++;
                    return;
                }

                _seen[seq] = true;
                _unique++;

                // X.25 delivers in sequence on one circuit; a number below one already seen means
                // something reordered the packets.
                if (seq < _highest)
                {
                    _outOfOrder++;
                }
                else
                {
                    _highest = seq;
                }
            }
        }
    }
}
