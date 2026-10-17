//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Sintran;

namespace RetroX25.Xot
{
    /// <summary>
    /// SINTRAN Routing Table Decoder
    /// Parses and decodes SINTRAN routing info responses
    /// </summary>
    public static class SintranRoutingDecoder
    {
        /// <summary>
        /// Decode a SINTRAN routing info response packet
        /// </summary>
        public static RoutingTableInfo DecodeRoutingResponse(byte[] data)
        {
            if (data == null || data.Length < 3)
            {
                throw new ArgumentException("Invalid routing response data");
            }

            var info = new RoutingTableInfo
            {
                RawData = data,
                ReceivedTime = DateTime.UtcNow
            };

            try
            {
                // Check if this is a SINTRAN routing protocol frame
                if (SintranBinaryProtocol.IsRoutingProtocolFrame(data))
                {
                    var frame = SintranBinaryProtocol.ParseRoutingFrameWithoutLapb(data);

                    info.IsValid = frame.IsValid;
                    info.ProtocolId = frame.ProtocolId;
                    info.SourceNode = frame.SourceNode;
                    info.DestinationNode = frame.DestinationNode;

                    if (frame.IsRoutingResponse && frame.Payload != null)
                    {
                        // Parse routing response payload
                        ParseRoutingPayload(frame.Payload, info);
                    }
                }
                else
                {
                    // Try parsing as legacy X.25 payload format
                    var packet = SintranBinaryProtocol.ParsePacket(data);
                    if (packet.IsResponse && packet.Payload != null)
                    {
                        ParseRoutingPayload(packet.Payload, info);
                    }
                }
            }
            catch (Exception ex)
            {
                info.IsValid = false;
                info.ErrorMessage = ex.Message;
            }

            return info;
        }

        /// <summary>
        /// Parse routing payload data
        /// NOTE: Format may vary - this is initial implementation based on available data
        /// </summary>
        private static void ParseRoutingPayload(byte[] payload, RoutingTableInfo info)
        {
            if (payload == null || payload.Length == 0)
                return;

            // First byte typically indicates status/count
            if (payload.Length > 0)
            {
                info.StatusByte = payload[0];

                // 0xFF often means "success, data follows"
                if (payload[0] == 0xFF)
                {
                    info.Status = "Success";
                }
                else if (payload[0] == 0x01)
                {
                    info.Status = "Error";
                    return;
                }
            }

            // Try to extract routing entries
            // Format varies - may contain:
            // - Node IDs
            // - Service addresses
            // - Interface identifiers
            // - Hop counts

            int offset = 1; // Skip status byte

            // Heuristic: look for patterns in the data
            while (offset < payload.Length)
            {
                // Check for potential node/service entry
                if (offset + 1 < payload.Length)
                {
                    ushort value = (ushort)((payload[offset] << 8) | payload[offset + 1]);

                    // If value looks like a machine ID (100-999) or service (200-299)
                    if (value >= 100 && value <= 999)
                    {
                        var entry = new RoutingEntry
                        {
                            Address = value,
                            Type = DetermineEntryType(value),
                            Offset = offset
                        };

                        info.Entries.Add(entry);
                    }

                    offset += 2;
                }
                else
                {
                    offset++;
                }
            }

            // If no entries found, mark entire payload as raw data
            if (info.Entries.Count == 0 && payload.Length > 1)
            {
                info.Notes.Add($"Raw routing data: {BitConverter.ToString(payload, 1)}");
            }
        }

        /// <summary>
        /// Determine entry type based on address range
        /// </summary>
        private static RoutingEntryType DetermineEntryType(ushort address)
        {
            // Heuristic classification
            if (address >= 200 && address < 300)
            {
                return RoutingEntryType.Service;
            }
            else if (address >= 100 && address < 200)
            {
                return RoutingEntryType.Node;
            }
            else if (address < 10)
            {
                return RoutingEntryType.Interface;
            }
            else
            {
                return RoutingEntryType.Unknown;
            }
        }

        /// <summary>
        /// Format routing table info as human-readable text
        /// </summary>
        public static string FormatRoutingTable(RoutingTableInfo info)
        {
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("  SINTRAN ROUTING TABLE");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine();

            // ReceivedTime is taken from DateTime.UtcNow, so the text says UTC.
            sb.AppendLine($"Received: {info.ReceivedTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
            sb.AppendLine($"Status: {info.Status}");
            sb.AppendLine($"Source Node: {info.SourceNode}");
            sb.AppendLine($"Destination Node: {info.DestinationNode}");
            sb.AppendLine();

            if (!info.IsValid)
            {
                sb.AppendLine($"ERROR: {info.ErrorMessage}");
                sb.AppendLine();
                sb.AppendLine("Raw Data:");
                sb.AppendLine(BitConverter.ToString(info.RawData));
                return sb.ToString();
            }

            if (info.Entries.Count > 0)
            {
                sb.AppendLine("Routing Entries:");
                sb.AppendLine();

                // Group by type
                var nodes = new List<RoutingEntry>();
                var services = new List<RoutingEntry>();
                var interfaces = new List<RoutingEntry>();
                var unknown = new List<RoutingEntry>();

                foreach (var entry in info.Entries)
                {
                    switch (entry.Type)
                    {
                        case RoutingEntryType.Node:
                            nodes.Add(entry);
                            break;
                        case RoutingEntryType.Service:
                            services.Add(entry);
                            break;
                        case RoutingEntryType.Interface:
                            interfaces.Add(entry);
                            break;
                        default:
                            unknown.Add(entry);
                            break;
                    }
                }

                if (nodes.Count > 0)
                {
                    sb.AppendLine("Nodes:");
                    foreach (var node in nodes)
                    {
                        sb.AppendLine($"  ├─ Node {node.Address}");
                    }
                    sb.AppendLine();
                }

                if (services.Count > 0)
                {
                    sb.AppendLine("Services:");
                    foreach (var service in services)
                    {
                        string serviceName = GetServiceName(service.Address);
                        sb.AppendLine($"  ├─ {serviceName} ({service.Address})");
                    }
                    sb.AppendLine();
                }

                if (interfaces.Count > 0)
                {
                    sb.AppendLine("Interfaces:");
                    foreach (var iface in interfaces)
                    {
                        sb.AppendLine($"  ├─ Interface {iface.Address}");
                    }
                    sb.AppendLine();
                }

                if (unknown.Count > 0)
                {
                    sb.AppendLine("Other:");
                    foreach (var entry in unknown)
                    {
                        sb.AppendLine($"  ├─ 0x{entry.Address:X4}");
                    }
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("No routing entries found");
                sb.AppendLine();
            }

            // Show notes
            if (info.Notes.Count > 0)
            {
                sb.AppendLine("Notes:");
                foreach (var note in info.Notes)
                {
                    sb.AppendLine($"  • {note}");
                }
                sb.AppendLine();
            }

            // Show raw data
            sb.AppendLine("Raw Data:");
            sb.AppendLine(FormatHexDump(info.RawData));

            return sb.ToString();
        }

        /// <summary>
        /// Get service name based on address
        /// </summary>
        private static string GetServiceName(ushort address)
        {
            return address switch
            {
                200 => "PAD",
                201 => "TAD",
                202 => "XMSG",
                203 => "FILE",
                _ => $"Service{address}"
            };
        }

        /// <summary>
        /// Format byte array as hex dump
        /// </summary>
        private static string FormatHexDump(byte[] data)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < data.Length; i += 16)
            {
                sb.Append($"{i:X4}: ");

                // Hex bytes
                for (int j = 0; j < 16 && i + j < data.Length; j++)
                {
                    sb.Append($"{data[i + j]:X2} ");
                    if (j == 7) sb.Append(" ");
                }

                // Padding
                for (int j = data.Length - i; j < 16; j++)
                {
                    sb.Append("   ");
                    if (j == 7) sb.Append(" ");
                }

                sb.Append(" |");

                // ASCII representation
                for (int j = 0; j < 16 && i + j < data.Length; j++)
                {
                    byte b = data[i + j];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }

                sb.AppendLine("|");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Routing table information
    /// </summary>
    public class RoutingTableInfo
    {
        /// <summary>
        /// The complete packet that was passed to <see cref="SintranRoutingDecoder.DecodeRoutingResponse"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="SintranRoutingDecoder.FormatRoutingTable"/> prints it as a hex dump.
        /// </remarks>
        public byte[] RawData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// The time the packet was decoded, taken from <see cref="DateTime.UtcNow"/>.
        /// </summary>
        public DateTime ReceivedTime { get; set; }

        /// <summary>
        /// Whether the decode succeeded.
        /// </summary>
        /// <remarks>
        /// Starts as true. For a routing protocol frame it is copied from the frame parser's own result,
        /// and it is set to false when decoding throws an exception, in which case
        /// <see cref="ErrorMessage"/> holds the reason.
        /// </remarks>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// A text form of <see cref="StatusByte"/>.
        /// </summary>
        /// <remarks>
        /// The decoder sets <c>"Success"</c> when the first payload byte is <c>0xFF</c> and <c>"Error"</c> when it
        /// is <c>0x01</c>. Any other value, or no payload at all, leaves it at <c>"Unknown"</c>.
        /// </remarks>
        public string Status { get; set; } = "Unknown";

        /// <summary>
        /// The first byte of the routing response payload, as read by the decoder.
        /// </summary>
        public byte StatusByte { get; set; }

        /// <summary>
        /// The protocol id byte of the routing protocol frame.
        /// </summary>
        /// <remarks>
        /// Set only when the packet is recognised as a SINTRAN routing protocol frame; it stays 0 for the
        /// legacy X.25 payload path.
        /// </remarks>
        public byte ProtocolId { get; set; }

        /// <summary>
        /// The source node number of the routing protocol frame.
        /// </summary>
        /// <remarks>
        /// Set only when the packet is recognised as a SINTRAN routing protocol frame; it stays 0 for the
        /// legacy X.25 payload path.
        /// </remarks>
        public byte SourceNode { get; set; }

        /// <summary>
        /// The destination node number of the routing protocol frame.
        /// </summary>
        /// <remarks>
        /// Set only when the packet is recognised as a SINTRAN routing protocol frame; it stays 0 for the
        /// legacy X.25 payload path.
        /// </remarks>
        public byte DestinationNode { get; set; }

        /// <summary>
        /// The message of the exception that stopped the decode, or an empty string when none was thrown.
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// The entries the decoder picked out of the payload.
        /// </summary>
        /// <remarks>
        /// After the status byte, the decoder reads the payload as 16-bit big-endian values, two bytes at a
        /// time, and adds an entry for each value from 100 to 999. This is a guess at the format, not a
        /// decoded layout; the code's own comments call it a heuristic.
        /// </remarks>
        public List<RoutingEntry> Entries { get; set; } = new List<RoutingEntry>();

        /// <summary>
        /// Free-text notes added during decoding.
        /// </summary>
        /// <remarks>
        /// When no entries are found in a payload longer than one byte, the decoder adds one note holding
        /// the payload bytes after the status byte in hex.
        /// </remarks>
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Single routing entry
    /// </summary>
    public class RoutingEntry
    {
        /// <summary>
        /// The 16-bit big-endian value read from the payload.
        /// </summary>
        /// <remarks>
        /// The decoder only creates entries for values from 100 to 999; <see cref="Type"/> is chosen from
        /// this value's range.
        /// </remarks>
        public ushort Address { get; set; }

        /// <summary>
        /// The kind of entry, chosen from the range of <see cref="Address"/>.
        /// </summary>
        public RoutingEntryType Type { get; set; }

        /// <summary>
        /// The byte offset in the routing response payload where <see cref="Address"/> starts.
        /// </summary>
        /// <remarks>
        /// The offset counts from the start of the payload, where offset 0 is the status byte, not from the
        /// start of <see cref="RoutingTableInfo.RawData"/>.
        /// </remarks>
        public int Offset { get; set; }

        /// <summary>
        /// A free-text description of the entry.
        /// </summary>
        /// <remarks>
        /// The decoder does not set it, so it stays empty.
        /// </remarks>
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Routing entry type
    /// </summary>
    public enum RoutingEntryType
    {
        /// <summary>
        /// An address that falls in no other range; the decoder gives this to values from 300 to 999.
        /// </summary>
        Unknown,

        /// <summary>
        /// An address from 100 to 199, which the decoder treats as a node.
        /// </summary>
        Node,

        /// <summary>
        /// An address from 200 to 299, which the decoder treats as a service.
        /// </summary>
        /// <remarks>
        /// <see cref="SintranRoutingDecoder.FormatRoutingTable"/> prints 200 as PAD, 201 as TAD, 202 as XMSG,
        /// 203 as FILE, and any other value as Service followed by the number.
        /// </remarks>
        Service,

        /// <summary>
        /// An address below 10, which the decoder treats as an interface.
        /// </summary>
        /// <remarks>
        /// The decoder only creates entries for values from 100 to 999, so it never produces this type today.
        /// </remarks>
        Interface,

        /// <summary>
        /// A route entry.
        /// </summary>
        /// <remarks>
        /// The decoder never assigns this value, and <see cref="SintranRoutingDecoder.FormatRoutingTable"/>
        /// prints such an entry in its Other group.
        /// </remarks>
        Route
    }
}
