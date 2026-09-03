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

            sb.AppendLine($"Received: {info.ReceivedTime:yyyy-MM-dd HH:mm:ss.fff}");
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
        public byte[] RawData { get; set; } = Array.Empty<byte>();
        public DateTime ReceivedTime { get; set; }
        public bool IsValid { get; set; } = true;
        public string Status { get; set; } = "Unknown";
        public byte StatusByte { get; set; }
        public byte ProtocolId { get; set; }
        public byte SourceNode { get; set; }
        public byte DestinationNode { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<RoutingEntry> Entries { get; set; } = new List<RoutingEntry>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Single routing entry
    /// </summary>
    public class RoutingEntry
    {
        public ushort Address { get; set; }
        public RoutingEntryType Type { get; set; }
        public int Offset { get; set; }
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Routing entry type
    /// </summary>
    public enum RoutingEntryType
    {
        Unknown,
        Node,
        Service,
        Interface,
        Route
    }
}
