//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Text;
using RetroIO.Protocols.X25.Models;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Fills the layer 3 fields of a <see cref="PacketInfo"/> from the bytes of an X.25 packet.
    /// </summary>
    /// <remarks>
    /// <para><b>Where the decoding lives</b></para>
    /// The bit positions are not read here. <see cref="X25PacketParser"/> in RetroIO does that, and
    /// it is the code the conformance vectors shared with the C emulator exercise, so this class only
    /// turns its result into the fields the packet window shows. A second set of masks in this
    /// project is how the Q, D and M bits ended up read from the wrong positions before.
    /// <para><b>Which bits mean what (X.25 5.1.2)</b></para>
    /// Bit 8 of the first octet is the Q bit in a data packet and the A bit in call set-up and
    /// clearing packets; bit 7 is the D bit in data, call request and call accepted packets. In every
    /// other packet type those bits are zero, so they are not reported as Q or D there. The M bit is
    /// only defined in a data packet.
    /// <para><b>Modulo 128</b></para>
    /// A modulo 128 data packet has a four-octet header (X.25 5.3.1), and the RetroIO parser reads
    /// that layout differently, while the C emulator refuses modulo 128 packets altogether. Until
    /// that is settled, a modulo 128 data packet is named and numbered but its M bit is not shown,
    /// because a wrong value looks the same as a right one.
    /// </remarks>
    public static class X25PacketDecoder
    {
        /// <summary>
        /// Name shown for a call request, which is also an incoming call (same type octet).
        /// </summary>
        public const string CallRequest = "CALL-REQ";

        /// <summary>
        /// Name shown for a call accepted, which is also a call connected (same type octet).
        /// </summary>
        public const string CallAccepted = "CALL-ACC";

        /// <summary>
        /// Name shown for a clear request, which is also a clear indication (same type octet).
        /// </summary>
        public const string ClearRequest = "CLEAR-REQ";

        /// <summary>
        /// Name shown for a clear confirmation.
        /// </summary>
        public const string ClearConfirmation = "CLEAR-CONF";

        /// <summary>
        /// Name shown for a data packet.
        /// </summary>
        public const string Data = "DATA";

        /// <summary>
        /// Name shown for an interrupt packet; also the name of the interrupt filter button.
        /// </summary>
        public const string Interrupt = "INT";

        /// <summary>
        /// Name shown for an interrupt confirmation.
        /// </summary>
        public const string InterruptConfirmation = "INT-CONF";

        /// <summary>
        /// Name shown for a receive ready packet.
        /// </summary>
        public const string ReceiveReady = "RR";

        /// <summary>
        /// Name shown for a receive not ready packet.
        /// </summary>
        public const string ReceiveNotReady = "RNR";

        /// <summary>
        /// Name shown for a reject packet.
        /// </summary>
        public const string Reject = "REJ";

        /// <summary>
        /// Name shown for a reset request, which is also a reset indication.
        /// </summary>
        public const string ResetRequest = "RESET-REQ";

        /// <summary>
        /// Name shown for a reset confirmation.
        /// </summary>
        public const string ResetConfirmation = "RESET-CONF";

        /// <summary>
        /// Name shown for a restart request, which is also a restart indication.
        /// </summary>
        public const string RestartRequest = "RESTART-REQ";

        /// <summary>
        /// Name shown for a restart confirmation.
        /// </summary>
        public const string RestartConfirmation = "RESTART-CONF";

        /// <summary>
        /// Name shown for a registration request.
        /// </summary>
        public const string RegistrationRequest = "REG-REQ";

        /// <summary>
        /// Name shown for a registration confirmation.
        /// </summary>
        public const string RegistrationConfirmation = "REG-CONF";

        /// <summary>
        /// Name shown for a diagnostic packet.
        /// </summary>
        public const string Diagnostic = "DIAG";

        /// <summary>
        /// Decodes the X.25 packet in <paramref name="x25Data"/> into the layer 3 fields of
        /// <paramref name="packet"/>.
        /// </summary>
        /// <param name="x25Data">
        /// The packet from the first octet (the general format identifier) onward, with no LAPB or
        /// HDLC bytes around it. Fewer than three octets is not a packet and leaves
        /// <paramref name="packet"/> unchanged.
        /// </param>
        /// <param name="packet">
        /// The packet record to fill: type name, logical channel, the Q, D and M bits, the cause and
        /// diagnostic codes, and the facilities.
        /// </param>
        /// <remarks>
        /// When the parser finds a field that runs past the end of the packet, such as an address or
        /// facility length that is too large, <see cref="PacketInfo.IsError"/> is set and the reason
        /// is added to <see cref="PacketInfo.Description"/>.
        /// </remarks>
        public static void Decode(byte[] x25Data, PacketInfo packet)
        {
            if (x25Data == null || x25Data.Length < 3)
            {
                return;
            }

            X25PacketInfo info = X25PacketParser.ParsePacket(x25Data);

            packet.L3LCN = info.LCN;
            packet.L3Type = NameOf(info.PacketType, info.RawPTI);

            bool isData = info.PacketType == X25PacketType.Data;
            bool isCallSetUp = info.PacketType == X25PacketType.CallRequest
                || info.PacketType == X25PacketType.CallAccepted;

            packet.L3Q = isData && info.QBit;
            packet.L3D = (isData || isCallSetUp) && info.DBit;
            packet.L3M = isData && !info.Modulo128 && info.MBit;

            switch (info.PacketType)
            {
                case X25PacketType.ClearRequest:
                    packet.L3Cause = info.ClearingCause;
                    packet.L3Diag = info.DiagnosticCode;
                    break;
                case X25PacketType.ResetRequest:
                    packet.L3Cause = info.ResetCause;
                    packet.L3Diag = info.DiagnosticCode;
                    break;
                case X25PacketType.RestartRequest:
                    packet.L3Cause = info.RestartCause;
                    packet.L3Diag = info.DiagnosticCode;
                    break;
            }

            packet.L3Facilities = FormatHex(info.Facilities);

            if (info.ParseError != null)
            {
                packet.IsError = true;
                packet.Description = packet.Description.Length == 0
                    ? info.ParseError
                    : packet.Description + " [" + info.ParseError + "]";
            }
        }

        /// <summary>
        /// Gives the short name the packet window shows for a packet type.
        /// </summary>
        /// <param name="type">
        /// The packet type the RetroIO parser found.
        /// </param>
        /// <param name="rawPti">
        /// The type octet, shown in hex when the type is not one the parser knows.
        /// </param>
        /// <returns>
        /// One of the name constants on this class, or <c>0x</c> and the type octet in hex.
        /// </returns>
        public static string NameOf(X25PacketType type, byte rawPti)
        {
            switch (type)
            {
                case X25PacketType.Data: return Data;
                case X25PacketType.RR: return ReceiveReady;
                case X25PacketType.RNR: return ReceiveNotReady;
                case X25PacketType.REJ: return Reject;
                case X25PacketType.CallRequest: return CallRequest;
                case X25PacketType.CallAccepted: return CallAccepted;
                case X25PacketType.ClearRequest: return ClearRequest;
                case X25PacketType.ClearConfirmation: return ClearConfirmation;
                case X25PacketType.Interrupt: return Interrupt;
                case X25PacketType.InterruptConfirmation: return InterruptConfirmation;
                case X25PacketType.ResetRequest: return ResetRequest;
                case X25PacketType.ResetConfirmation: return ResetConfirmation;
                case X25PacketType.RestartRequest: return RestartRequest;
                case X25PacketType.RestartConfirmation: return RestartConfirmation;
                case X25PacketType.RegistrationRequest: return RegistrationRequest;
                case X25PacketType.RegistrationConfirmation: return RegistrationConfirmation;
                case X25PacketType.Diagnostic: return Diagnostic;
                default: return "0x" + rawPti.ToString("X2");
            }
        }

        /// <summary>
        /// Formats bytes as upper-case hex pairs separated by spaces.
        /// </summary>
        /// <param name="bytes">
        /// The bytes to format.
        /// </param>
        /// <returns>
        /// The hex text, or an empty string when there are no bytes.
        /// </returns>
        private static string FormatHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder text = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(' ');
                }

                text.Append(bytes[i].ToString("X2"));
            }

            return text.ToString();
        }
    }
}
