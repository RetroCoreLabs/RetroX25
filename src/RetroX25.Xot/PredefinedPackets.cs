//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;

namespace RetroX25.Xot
{
    /// <summary>
    /// Predefined X.25 test packets for XOT testing
    /// Following X.25 packet format: GFI + LCN + PTI + payload
    /// </summary>
    public static class PredefinedPackets
    {
        /// <summary>
        /// X.25 Call Request packet
        /// GFI=0x10 (Q=0, D=0, Modulo=8), LCN=0x0001, PTI=0x0B (Call Request)
        /// Called address: 200, Calling address: 100
        /// </summary>
        public static byte[] CallRequest => new byte[]
        {
            0x10,       // GFI: General Format Identifier
            0x00,       // LCN high nibble (Logical Channel Number)
            0x01,       // LCN low byte
            0x0B,       // PTI: Packet Type Identifier (Call Request)
            0x21,       // Address length: 2 digits called, 1 digit calling
            0x02, 0x00, // Called address: "200"
            0x01,       // Calling address: "100" (first digit)
            0x00,       // Calling address continued + padding
            0x00,       // Facilities length (no facilities)
            // No Call User Data
        };

        /// <summary>
        /// X.25 Call Accept packet
        /// GFI=0x10, LCN=0x0001, PTI=0x0F (Call Accept)
        /// </summary>
        public static byte[] CallAccept => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x0F,       // PTI: Call Accept
            0x00,       // Addresses length (responding station)
            0x00,       // Facilities length
            // No data
        };

        /// <summary>
        /// X.25 Clear Request packet
        /// GFI=0x10, LCN=0x0001, PTI=0x13 (Clear Request)
        /// Clearing cause: 0x00 (DTE originated)
        /// Diagnostic: 0x00
        /// </summary>
        public static byte[] ClearRequest => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x13,       // PTI: Clear Request
            0x00,       // Clearing cause
            0x00,       // Diagnostic code
        };

        /// <summary>
        /// X.25 Clear Confirmation packet
        /// GFI=0x10, LCN=0x0001, PTI=0x17 (Clear Confirmation)
        /// </summary>
        public static byte[] ClearConfirmation => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x17,       // PTI: Clear Confirmation
        };

        /// <summary>
        /// X.25 Data packet with sample payload
        /// GFI=0x10, LCN=0x0001, P(S)=0, P(R)=0, M=0
        /// Payload: "Hello XOT"
        /// </summary>
        public static byte[] DataPacket => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x00,       // Data packet: P(R)=0, M=0, P(S)=0
            // Payload: "Hello XOT"
            0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x58, 0x4F, 0x54
        };

        /// <summary>
        /// X.25 RR (Receiver Ready) packet
        /// GFI=0x10, LCN=0x0001, PTI=0x01 (RR with P(R)=0)
        /// </summary>
        public static byte[] ReceiverReady => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x01,       // PTI: RR, P(R)=0
        };

        /// <summary>
        /// X.25 RNR (Receiver Not Ready) packet
        /// GFI=0x10, LCN=0x0001, PTI=0x05 (RNR with P(R)=0)
        /// </summary>
        public static byte[] ReceiverNotReady => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x05,       // PTI: RNR, P(R)=0
        };

        /// <summary>
        /// X.25 Reset Request packet
        /// GFI=0x10, LCN=0x0001, PTI=0x1B (Reset Request)
        /// Resetting cause: 0x00 (DTE originated)
        /// Diagnostic: 0x00
        /// </summary>
        public static byte[] ResetRequest => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x1B,       // PTI: Reset Request
            0x00,       // Resetting cause
            0x00,       // Diagnostic code
        };

        /// <summary>
        /// X.25 Reset Confirmation packet
        /// GFI=0x10, LCN=0x0001, PTI=0x1F (Reset Confirmation)
        /// </summary>
        public static byte[] ResetConfirmation => new byte[]
        {
            0x10,       // GFI
            0x00,       // LCN high nibble
            0x01,       // LCN low byte
            0x1F,       // PTI: Reset Confirmation
        };

        /// <summary>
        /// Get packet name for display
        /// </summary>
        public static string GetPacketName(byte[] packet)
        {
            if (packet.Length < 4)
                return "Invalid Packet";

            byte pti = packet[3];
            return pti switch
            {
                0x0B => "Call Request",
                0x0F => "Call Accept",
                0x13 => "Clear Request",
                0x17 => "Clear Confirmation",
                0x1B => "Reset Request",
                0x1F => "Reset Confirmation",
                0x01 => "RR (Receiver Ready)",
                0x05 => "RNR (Receiver Not Ready)",
                0x09 => "REJ (Reject)",
                _ when (pti & 0x01) == 0 => $"Data (P(S)={(pti >> 1) & 0x07})",
                _ => $"Unknown (0x{pti:X2})"
            };
        }
    }
}
