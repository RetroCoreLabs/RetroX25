//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Protocols;

namespace RetroX25.Tests
{
    /// <summary>
    /// A caller that builds every X.25 packet byte by byte and sends it over one XOT connection, so a test can set bits no
    /// ready-made tool sets: Q, M and D bits, the packet size and window facilities, the clear and reset causes.
    /// </summary>
    /// <remarks>
    /// It keeps the sequence numbers itself (P(S), P(R), modulo 8) and acknowledges every data packet it receives with an RR,
    /// so a far end that echoes keeps flowing. Everything received is kept in <see cref="Received"/> in the order it came.
    /// </remarks>
    internal sealed class RawXotCaller : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly Thread _reader;
        private readonly object _lock = new object();
        private readonly List<byte[]> _received = new List<byte[]>();
        private readonly List<byte[]> _sent = new List<byte[]>();
        private int _vs;
        private int _vr;

        /// <summary>The channel number we put in the packets. Chosen by the caller; the far end may answer on another.</summary>
        internal int Channel { get; }

        /// <summary>The channel number the far end answers and sends on, learned from the Call Accepted.</summary>
        internal int WireChannel { get; private set; }

        internal RawXotCaller(string host, int port, int channel)
        {
            Channel = channel;
            WireChannel = channel;
            _tcp = new TcpClient();
            _tcp.Connect(host, port);
            _stream = _tcp.GetStream();
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "raw-xot-reader" };
            _reader.Start();
        }

        /// <summary>Every packet received, as raw bytes (without the XOT header).</summary>
        internal List<byte[]> Received
        {
            get
            {
                lock (_lock)
                {
                    return new List<byte[]>(_received);
                }
            }
        }

        /// <summary>Every packet sent, as raw bytes.</summary>
        internal List<byte[]> Sent
        {
            get
            {
                lock (_lock)
                {
                    return new List<byte[]>(_sent);
                }
            }
        }

        /// <summary>Hex of every packet, one per line, "->" for sent and "<-" for received, in a decoded form.</summary>
        internal string Dump()
        {
            StringBuilder text = new StringBuilder();
            lock (_lock)
            {
                for (int i = 0; i < _sent.Count; i++)
                {
                    text.Append("-> ").AppendLine(Describe(_sent[i]));
                }

                for (int i = 0; i < _received.Count; i++)
                {
                    text.Append("<- ").AppendLine(Describe(_received[i]));
                }
            }

            return text.ToString();
        }

        private static string Describe(byte[] packet)
        {
            X25PacketInfo info = X25PacketParser.ParsePacket(packet);
            return info.PacketType + " " + BitConverter.ToString(packet);
        }

        private void ReadLoop()
        {
            try
            {
                byte[] header = new byte[4];
                while (ReadExact(header, 4))
                {
                    byte[] packet = new byte[(header[2] << 8) | header[3]];
                    if (!ReadExact(packet, packet.Length))
                    {
                        break;
                    }

                    bool isData = packet.Length >= 3 && (packet[2] & 0x01) == 0;
                    lock (_lock)
                    {
                        _received.Add(packet);
                        if (isData)
                        {
                            _vr = (_vr + 1) & 7;
                        }
                    }

                    if (isData)
                    {
                        // Acknowledge it at once: an RR with the next P(R).
                        int pr;
                        lock (_lock)
                        {
                            pr = _vr;
                        }

                        SendRaw(new byte[] { (byte)(0x10 | (WireChannel >> 8 & 0x0F)), (byte)(WireChannel & 0xFF), (byte)(0x01 | (pr << 5)) });
                    }
                }
            }
            catch (System.IO.IOException)
            {
                // The far end or we closed the connection.
            }
            catch (ObjectDisposedException)
            {
                // Closed by Dispose.
            }
        }

        private bool ReadExact(byte[] buffer, int count)
        {
            int done = 0;
            while (done < count)
            {
                int n = _stream.Read(buffer, done, count - done);
                if (n <= 0)
                {
                    return false;
                }

                done += n;
            }

            return true;
        }

        private void SendRaw(byte[] packet)
        {
            byte[] frame = new byte[4 + packet.Length];
            frame[2] = (byte)(packet.Length >> 8);
            frame[3] = (byte)(packet.Length & 0xFF);
            Array.Copy(packet, 0, frame, 4, packet.Length);
            lock (_lock)
            {
                _sent.Add(packet);
                _stream.Write(frame, 0, frame.Length);
            }
        }

        private static byte[] PackBcd(string digits)
        {
            byte[] result = new byte[(digits.Length + 1) / 2];
            for (int i = 0; i < digits.Length; i++)
            {
                int digit = digits[i] - '0';
                result[i / 2] |= (byte)(i % 2 == 0 ? digit << 4 : digit);
            }

            return result;
        }

        /// <summary>
        /// Sends a Call Request (RFC 1613 6.1: with the packet size and window size facilities) and waits for the answer.
        /// </summary>
        /// <param name="called">Called address.</param>
        /// <param name="calling">Calling address.</param>
        /// <param name="packetSizeCode">The facility code for the packet size: 4 = 16 bytes, 7 = 128 bytes, 8 = 256 bytes.</param>
        /// <param name="window">The window size, 1 to 7.</param>
        /// <param name="timeoutMs">How long to wait for the Call Accepted.</param>
        /// <param name="callUserData">Call user data to send with the request, for example the X.29 marker a Cisco PAD needs; none when null.</param>
        /// <returns>True when a Call Accepted came; otherwise the answer (a Clear) is in <see cref="Received"/>.</returns>
        internal bool Call(string called, string calling, byte packetSizeCode, byte window, int timeoutMs, byte[]? callUserData = null)
        {
            List<byte> packet = new List<byte> { (byte)(0x10 | (Channel >> 8 & 0x0F)), (byte)(Channel & 0xFF), 0x0B };
            packet.Add((byte)((calling.Length << 4) | called.Length));
            packet.AddRange(PackBcd(called));
            packet.AddRange(PackBcd(calling));
            packet.Add(6);
            packet.AddRange(new byte[] { 0x42, packetSizeCode, packetSizeCode, 0x43, window, window });
            if (callUserData != null)
            {
                packet.AddRange(callUserData);
            }

            SendRaw(packet.ToArray());
            return SpinWait.SpinUntil(() =>
            {
                lock (_lock)
                {
                    for (int i = 0; i < _received.Count; i++)
                    {
                        if (_received[i].Length >= 3 && _received[i][2] == 0x0F)
                        {
                            WireChannel = ((_received[i][0] & 0x0F) << 8) | _received[i][1];
                            return true;
                        }

                        if (_received[i].Length >= 3 && _received[i][2] == 0x13)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }, timeoutMs) && Answered();
        }

        private bool Answered()
        {
            lock (_lock)
            {
                for (int i = 0; i < _received.Count; i++)
                {
                    if (_received[i].Length >= 3 && _received[i][2] == 0x0F)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Sends one data packet with the given bits. P(S) and P(R) are kept here.</summary>
        internal void SendData(byte[] payload, bool q = false, bool m = false, bool d = false)
        {
            int ps;
            int pr;
            lock (_lock)
            {
                ps = _vs;
                _vs = (_vs + 1) & 7;
                pr = _vr;
            }

            byte[] packet = new byte[3 + payload.Length];
            packet[0] = (byte)(0x10 | (q ? 0x80 : 0) | (d ? 0x40 : 0) | (WireChannel >> 8 & 0x0F));
            packet[1] = (byte)(WireChannel & 0xFF);
            packet[2] = (byte)((pr << 5) | (m ? 0x10 : 0) | (ps << 1));
            Array.Copy(payload, 0, packet, 3, payload.Length);
            SendRaw(packet);
        }

        /// <summary>
        /// Sends one Call Request on any channel number, with call user data, and does not wait. Used to place SEVERAL calls on one
        /// connection, or a call with the X.29 marker (01000000) that a Cisco PAD needs. Facilities: packet size 128 and window 2 both
        /// ways, the values our own engine asks for.
        /// </summary>
        internal void SendCall(int channel, string called, string calling, byte[]? callUserData)
        {
            List<byte> packet = new List<byte> { (byte)(0x10 | (channel >> 8 & 0x0F)), (byte)(channel & 0xFF), 0x0B };
            packet.Add((byte)((calling.Length << 4) | called.Length));
            packet.AddRange(PackBcd(called));
            packet.AddRange(PackBcd(calling));
            packet.Add(6);
            packet.AddRange(new byte[] { 0x42, 0x07, 0x07, 0x43, 0x02, 0x02 });
            if (callUserData != null)
            {
                packet.AddRange(callUserData);
            }

            SendRaw(packet.ToArray());
        }

        /// <summary>Waits until a received packet on the given channel has the given type byte (third byte), then returns it; null on timeout.</summary>
        internal byte[]? WaitForType(int channel, byte typeByte, int timeoutMs)
        {
            return WaitFor(p => p.Length >= 3 && p[2] == typeByte && (((p[0] & 0x0F) << 8) | p[1]) == channel, timeoutMs);
        }

        /// <summary>Sends a Clear Request with the given cause and diagnostic.</summary>
        internal void Clear(byte cause, byte diagnostic)
        {
            SendRaw(new byte[] { (byte)(0x10 | (WireChannel >> 8 & 0x0F)), (byte)(WireChannel & 0xFF), 0x13, cause, diagnostic });
        }

        /// <summary>Sends a Reset Request with the given cause and diagnostic. The sequence numbers start again from 0.</summary>
        internal void Reset(byte cause, byte diagnostic)
        {
            lock (_lock)
            {
                _vs = 0;
                _vr = 0;
            }

            SendRaw(new byte[] { (byte)(0x10 | (WireChannel >> 8 & 0x0F)), (byte)(WireChannel & 0xFF), 0x1B, cause, diagnostic });
        }

        /// <summary>Waits until a received packet matches, then returns it (null on timeout).</summary>
        internal byte[]? WaitFor(Func<byte[], bool> match, int timeoutMs)
        {
            byte[]? found = null;
            SpinWait.SpinUntil(() =>
            {
                lock (_lock)
                {
                    for (int i = 0; i < _received.Count; i++)
                    {
                        if (match(_received[i]))
                        {
                            found = _received[i];
                            return true;
                        }
                    }
                }

                return false;
            }, timeoutMs);
            return found;
        }

        public void Dispose()
        {
            try
            {
                _tcp.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed.
            }

            _reader.Join(2000);
        }
    }
}
