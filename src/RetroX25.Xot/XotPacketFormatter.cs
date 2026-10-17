//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Text;
using RetroIO.Protocols.X25.Models;

namespace RetroX25.Xot
{
    /// <summary>
    /// Formats XOT and X.25 packets for display: hex dumps, ASCII, the XOT header, and building or
    /// taking apart an XOT packet.
    /// </summary>
    /// <remarks>
    /// What kind of X.25 packet something is, and where its user data starts, is decided by
    /// <see cref="X25PacketParser"/> in RetroIO. This class does not read X.25 header bits itself.
    /// </remarks>
    public static class XotPacketFormatter
    {
        /// <summary>
        /// Format a complete XOT packet (header + X.25 payload) for display
        /// </summary>
        public static string FormatXotPacket(byte[] xotPacket, string direction, DateTime timestamp)
        {
            if (xotPacket == null || xotPacket.Length < 4)
                return $"[{timestamp:HH:mm:ss.fff}] {direction} Invalid XOT packet (too short)";

            // Parse XOT header
            int version = (xotPacket[0] << 8) | xotPacket[1];
            int length = (xotPacket[2] << 8) | xotPacket[3];

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[{timestamp:HH:mm:ss.fff}] {direction} XOT Packet");
            sb.AppendLine($"  XOT Header: Version={version}, Length={length} bytes");

            // Display hex dump of XOT header
            sb.AppendLine($"  XOT Header Hex: {FormatHexBytes(xotPacket, 0, 4)}");

            // Parse and display X.25 payload if present
            if (xotPacket.Length > 4)
            {
                byte[] x25Payload = new byte[xotPacket.Length - 4];
                Array.Copy(xotPacket, 4, x25Payload, 0, x25Payload.Length);

                string packetType = PredefinedPackets.GetPacketName(x25Payload);
                sb.AppendLine($"  X.25 Packet: {packetType}");
                sb.AppendLine($"  X.25 Hex: {FormatHexDump(x25Payload)}");
            }
            else
            {
                sb.AppendLine("  X.25 Packet: (empty)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format X.25 packet only (without XOT header) for display
        /// </summary>
        public static string FormatX25Packet(byte[] x25Packet, string direction, DateTime timestamp)
        {
            if (x25Packet == null || x25Packet.Length == 0)
                return $"[{timestamp:HH:mm:ss.fff}] {direction} Empty packet";

            string packetType = PredefinedPackets.GetPacketName(x25Packet);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[{timestamp:HH:mm:ss.fff}] {direction} {x25Packet.Length} bytes - {packetType}");
            sb.AppendLine($"  Hex: {FormatHexDump(x25Packet)}");

            // A data packet also shows its user data as text. The parser says where the data starts, so
            // the offset is not worked out here a second time.
            if (x25Packet.Length >= 3)
            {
                X25PacketInfo info = X25PacketParser.ParsePacket(x25Packet);
                if (info.PacketType == X25PacketType.Data && info.UserData.Length > 0)
                {
                    string ascii = FormatAscii(x25Packet, x25Packet.Length - info.UserData.Length, info.UserData.Length);
                    if (!string.IsNullOrEmpty(ascii))
                    {
                        sb.AppendLine($"  ASCII: {ascii}");
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format byte array as hex dump with 16 bytes per line
        /// </summary>
        public static string FormatHexDump(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "(empty)";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                if (i > 0)
                    sb.Append("\n       ");

                int remaining = Math.Min(16, data.Length - i);
                sb.Append(FormatHexBytes(data, i, remaining));

                // Add spacing for alignment if less than 16 bytes
                if (remaining < 16)
                {
                    sb.Append(new string(' ', (16 - remaining) * 3));
                }

                // Add ASCII representation
                sb.Append("  ");
                for (int j = i; j < i + remaining; j++)
                {
                    char c = (char)data[j];
                    sb.Append(c >= 32 && c < 127 ? c : '.');
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format a range of bytes as space-separated hex
        /// </summary>
        public static string FormatHexBytes(byte[] data, int offset, int length)
        {
            if (data == null || offset < 0 || length <= 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            int end = Math.Min(offset + length, data.Length);

            for (int i = offset; i < end; i++)
            {
                if (i > offset)
                    sb.Append(' ');
                sb.Append($"{data[i]:X2}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Format byte array as continuous hex string (no spaces)
        /// </summary>
        public static string FormatHexString(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append($"{data[i]:X2}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parse hex string to byte array
        /// Accepts formats: "1001000B" or "10 01 00 0B"
        /// </summary>
        public static byte[]? ParseHexString(string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString))
                return null;

            // Remove spaces and other separators
            hexString = hexString.Replace(" ", "").Replace("-", "").Replace(":", "").Trim();

            // Must be even length
            if (hexString.Length % 2 != 0)
                return null;

            try
            {
                byte[] bytes = new byte[hexString.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
                }
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Format ASCII representation of byte array
        /// </summary>
        private static string FormatAscii(byte[] data, int offset, int length)
        {
            if (data == null || offset < 0 || length <= 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            int end = Math.Min(offset + length, data.Length);

            for (int i = offset; i < end; i++)
            {
                char c = (char)data[i];
                if (c >= 32 && c < 127)
                {
                    sb.Append(c);
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else
                {
                    sb.Append($"\\x{(byte)c:X2}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Create XOT packet from X.25 payload
        /// Adds 4-byte XOT header (version 0 + length)
        /// </summary>
        public static byte[] CreateXotPacket(byte[] x25Packet)
        {
            if (x25Packet == null || x25Packet.Length == 0)
                throw new ArgumentException("X.25 packet cannot be null or empty");

            byte[] xotPacket = new byte[4 + x25Packet.Length];

            // XOT header: version (0x0000) + length
            xotPacket[0] = 0x00; // Version high byte
            xotPacket[1] = 0x00; // Version low byte
            xotPacket[2] = (byte)(x25Packet.Length >> 8); // Length high byte
            xotPacket[3] = (byte)(x25Packet.Length & 0xFF); // Length low byte

            // Copy X.25 packet
            Array.Copy(x25Packet, 0, xotPacket, 4, x25Packet.Length);

            return xotPacket;
        }

        /// <summary>
        /// Parse XOT packet to extract X.25 payload
        /// Returns null if invalid
        /// </summary>
        public static byte[]? ParseXotPacket(byte[] xotPacket)
        {
            if (xotPacket == null || xotPacket.Length < 4)
                return null;

            int version = (xotPacket[0] << 8) | xotPacket[1];
            int length = (xotPacket[2] << 8) | xotPacket[3];

            // Validate version
            if (version != 0)
                return null;

            // Validate length
            if (length != xotPacket.Length - 4)
                return null;

            if (length == 0)
                return Array.Empty<byte>();

            byte[] x25Packet = new byte[length];
            Array.Copy(xotPacket, 4, x25Packet, 0, length);

            return x25Packet;
        }
    }
}
