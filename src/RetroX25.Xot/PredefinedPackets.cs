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
    /// Ready-made X.25 packets for testing an XOT peer, and a namer for packet bytes.
    /// </summary>
    /// <remarks>
    /// <para><b>Built by RetroIO, not written out as bytes</b></para>
    /// Every packet comes from <see cref="X25PacketBuilder"/>, the builder the shared conformance
    /// vectors check against the C emulator, and is named by <see cref="X25PacketParser"/>. These
    /// packets used to be byte arrays written here, each with an extra octet after the first two, so
    /// the packet type landed one place too late and a call request went out as a receive ready. They
    /// are all modulo 8, logical channel 1.
    /// </remarks>
    public static class PredefinedPackets
    {
        /// <summary>
        /// The logical channel number every predefined packet is addressed to.
        /// </summary>
        public const ushort LogicalChannel = 1;

        /// <summary>
        /// The called address of the predefined call request.
        /// </summary>
        public const string CalledAddress = "200";

        /// <summary>
        /// The calling address of the predefined call request.
        /// </summary>
        public const string CallingAddress = "100";

        /// <summary>
        /// The text carried by the predefined data packet.
        /// </summary>
        public const string DataText = "Hello XOT";

        /// <summary>
        /// Gets a call request from <see cref="CallingAddress"/> to <see cref="CalledAddress"/>, with no
        /// facilities and no call user data.
        /// </summary>
        public static byte[] CallRequest => X25PacketBuilder.CreateCallRequest(
            LogicalChannel, CalledAddress, CallingAddress);

        /// <summary>
        /// Gets a call accepted packet in its basic format: the three header octets and nothing after them.
        /// </summary>
        public static byte[] CallAccept => X25PacketBuilder.CreateCallAccepted(LogicalChannel);

        /// <summary>
        /// Gets a clear request with cause 0 (DTE originated) and diagnostic 0.
        /// </summary>
        public static byte[] ClearRequest => X25PacketBuilder.CreateClearRequest(LogicalChannel, 0x00, 0x00);

        /// <summary>
        /// Gets a clear confirmation.
        /// </summary>
        public static byte[] ClearConfirmation => X25PacketBuilder.CreateClearConfirmation(LogicalChannel);

        /// <summary>
        /// Gets a data packet with P(S) 0, P(R) 0 and the M bit clear, carrying <see cref="DataText"/>.
        /// </summary>
        public static byte[] DataPacket => X25PacketBuilder.CreateDataPacket(
            LogicalChannel, 0, 0, Encoding.ASCII.GetBytes(DataText));

        /// <summary>
        /// Gets a receive ready with P(R) 0.
        /// </summary>
        public static byte[] ReceiverReady => X25PacketBuilder.CreateRR(LogicalChannel, 0);

        /// <summary>
        /// Gets a receive not ready with P(R) 0.
        /// </summary>
        public static byte[] ReceiverNotReady => X25PacketBuilder.CreateRNR(LogicalChannel, 0);

        /// <summary>
        /// Gets a reset request with cause 0 (DTE originated) and diagnostic 0.
        /// </summary>
        public static byte[] ResetRequest => X25PacketBuilder.CreateResetRequest(LogicalChannel, 0x00, 0x00);

        /// <summary>
        /// Gets a reset confirmation.
        /// </summary>
        public static byte[] ResetConfirmation => X25PacketBuilder.CreateResetConfirmation(LogicalChannel);

        /// <summary>
        /// Names an X.25 packet from its bytes, for a log line or a preview.
        /// </summary>
        /// <param name="packet">
        /// The X.25 packet from its first octet (the general format identifier), with no XOT header.
        /// </param>
        /// <returns>
        /// A short name such as <c>Call Request (Call, Incoming Call)</c>, with the send and receive
        /// sequence numbers added for a data packet and the receive sequence number for RR, RNR and REJ;
        /// <c>Unknown (0x..)</c> with the type octet when the type is not one X.25 defines; or
        /// <c>Invalid Packet</c> when there are fewer than the three octets every packet has.
        /// </returns>
        public static string GetPacketName(byte[] packet)
        {
            if (packet == null || packet.Length < 3)
            {
                return "Invalid Packet";
            }

            X25PacketInfo info = X25PacketParser.ParsePacket(packet);
            switch (info.PacketType)
            {
                case X25PacketType.Unknown:
                    return "Unknown (0x" + info.RawPTI.ToString("X2") + ")";
                case X25PacketType.Data:
                    return info.GetPacketTypeName() + " P(S)=" + info.PS + " P(R)=" + info.PR;
                case X25PacketType.RR:
                case X25PacketType.RNR:
                case X25PacketType.REJ:
                    return info.GetPacketTypeName() + " P(R)=" + info.PR;
                default:
                    return info.GetPacketTypeName();
            }
        }
    }
}
