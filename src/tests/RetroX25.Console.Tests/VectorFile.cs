//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Text.Json;
using RetroX25.Console.UI;

namespace RetroX25.Console.Tests
{
    /// <summary>
    /// Finds and reads the packet vectors of the shared X.25 conformance file.
    /// </summary>
    /// <remarks>
    /// The file is kept in the RetroIO repository, where it is edited, and is read by the C# stack and
    /// by the C emulator. This repository only reads it.
    /// </remarks>
    internal static class VectorFile
    {
        private const string RelativePath = "RetroIO/src/tests/x25-vectors/x25-vectors.json";

        /// <summary>
        /// One packet vector: the octets and the fields a decoder is expected to find in them.
        /// </summary>
        internal sealed class Packet
        {
            public string Id { get; set; } = string.Empty;

            public string Hex { get; set; } = string.Empty;

            public string Type { get; set; } = string.Empty;

            public int Lcn { get; set; }

            public bool? Q { get; set; }

            public bool? D { get; set; }

            public bool? M { get; set; }

            public int? Cause { get; set; }

            public int? Diag { get; set; }

            public string? FacilitiesHex { get; set; }
        }

        /// <summary>
        /// Finds the vector file.
        /// </summary>
        /// <returns>
        /// The path in the <c>X25_VECTORS</c> environment variable when that file exists; otherwise the
        /// file in a RetroIO folder beside this repository, found by walking up from the test binary;
        /// or null.
        /// </returns>
        internal static string? Find()
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable("X25_VECTORS");
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
            {
                return fromEnvironment;
            }

            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, RelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>
        /// Reads the packet vectors from the file.
        /// </summary>
        /// <param name="path">
        /// The path of the vector file.
        /// </param>
        /// <returns>
        /// One entry for each vector in the file's <c>packets</c> group.
        /// </returns>
        internal static Packet[] LoadPackets(string path)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement group = document.RootElement.GetProperty("packets");

            int count = group.GetArrayLength();
            List<Packet> packets = new List<Packet>(count);
            for (int i = 0; i < count; i++)
            {
                JsonElement entry = group[i];
                JsonElement parse = entry.GetProperty("parse");
                Packet packet = new Packet
                {
                    Id = entry.GetProperty("id").GetString() ?? string.Empty,
                    Hex = entry.GetProperty("hex").GetString() ?? string.Empty,
                    Type = parse.GetProperty("type").GetString() ?? string.Empty,
                    Lcn = parse.GetProperty("lcn").GetInt32(),
                    Q = OptionalBool(parse, "q"),
                    D = OptionalBool(parse, "d"),
                    M = OptionalBool(parse, "m"),
                    Cause = OptionalInt(parse, "cause"),
                    Diag = OptionalInt(parse, "diag"),
                    FacilitiesHex = OptionalString(parse, "facilitiesHex")
                };
                packets.Add(packet);
            }

            return packets.ToArray();
        }

        /// <summary>
        /// Gives the name the packet window shows for a vector's type, which is the C# name of the
        /// packet type in the vector file.
        /// </summary>
        /// <param name="vectorType">
        /// The <c>type</c> field of a vector, such as <c>CallRequest</c>.
        /// </param>
        /// <returns>
        /// The matching name constant of <see cref="X25PacketDecoder"/>.
        /// </returns>
        internal static string ExpectedName(string vectorType)
        {
            switch (vectorType)
            {
                case "CallRequest": return X25PacketDecoder.CallRequest;
                case "CallAccepted": return X25PacketDecoder.CallAccepted;
                case "ClearRequest": return X25PacketDecoder.ClearRequest;
                case "ClearConfirmation": return X25PacketDecoder.ClearConfirmation;
                case "Data": return X25PacketDecoder.Data;
                case "RR": return X25PacketDecoder.ReceiveReady;
                case "RNR": return X25PacketDecoder.ReceiveNotReady;
                case "REJ": return X25PacketDecoder.Reject;
                case "ResetRequest": return X25PacketDecoder.ResetRequest;
                case "ResetConfirmation": return X25PacketDecoder.ResetConfirmation;
                case "RestartRequest": return X25PacketDecoder.RestartRequest;
                case "RestartConfirmation": return X25PacketDecoder.RestartConfirmation;
                case "Interrupt": return X25PacketDecoder.Interrupt;
                case "InterruptConfirmation": return X25PacketDecoder.InterruptConfirmation;
                case "Diagnostic": return X25PacketDecoder.Diagnostic;
                default: throw new InvalidOperationException("The vector file has a packet type this test does not know: " + vectorType);
            }
        }

        private static bool? OptionalBool(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) ? value.GetBoolean() : null;
        }

        private static int? OptionalInt(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : null;
        }

        private static string? OptionalString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
        }
    }
}
