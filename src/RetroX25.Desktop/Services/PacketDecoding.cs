//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Text;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Turns the bytes of an X.25 packet or the fields of a history item into the short names and the
/// plain-word detail text the Packet window shows.
/// </summary>
/// <remarks>
/// <para><b>Where the bit positions are read</b></para>
/// Not here. <see cref="X25PacketParser"/> in RetroIO reads them, and it is the code the shared
/// conformance vectors exercise. This class only names what the parser found. A second set of masks
/// in this project is how the Q, D and M bits were once read from the wrong places.
/// <para><b>Which bits mean what (X.25 5.1.2)</b></para>
/// Bit 8 of the first octet is the Q bit in a data packet and the A bit in call set-up and clearing
/// packets; bit 7 is the D bit in data, call request and call accepted packets. In every other
/// packet type those bits are zero, so they are not reported as Q or D there. The M bit only means
/// something in a data packet.
/// <para><b>Modulo 128</b></para>
/// A modulo 128 data packet has a four-octet header (X.25 5.3.1), and the RetroIO parser reads that
/// layout differently. A modulo 128 data packet is named and numbered, but its M bit is not shown,
/// because a wrong value looks the same as a right one. This is the same rule the text-mode console
/// had.
/// </remarks>
public static class PacketDecoding
{
    /// <summary>Name shown for a call request (it is also an incoming call: same type octet).</summary>
    public const string CallRequest = "CALL-REQ";

    /// <summary>Name shown for a call accepted (also a call connected: same type octet).</summary>
    public const string CallAccepted = "CALL-ACC";

    /// <summary>Name shown for a clear request (also a clear indication: same type octet).</summary>
    public const string ClearRequest = "CLEAR-REQ";

    /// <summary>Name shown for a clear confirmation.</summary>
    public const string ClearConfirmation = "CLEAR-CONF";

    /// <summary>Name shown for a data packet.</summary>
    public const string Data = "DATA";

    /// <summary>Name shown for an interrupt packet.</summary>
    public const string Interrupt = "INT";

    /// <summary>Name shown for an interrupt confirmation.</summary>
    public const string InterruptConfirmation = "INT-CONF";

    /// <summary>Name shown for a receive ready packet.</summary>
    public const string ReceiveReady = "RR";

    /// <summary>Name shown for a receive not ready packet.</summary>
    public const string ReceiveNotReady = "RNR";

    /// <summary>Name shown for a reject packet.</summary>
    public const string Reject = "REJ";

    /// <summary>Name shown for a reset request (also a reset indication).</summary>
    public const string ResetRequest = "RESET-REQ";

    /// <summary>Name shown for a reset confirmation.</summary>
    public const string ResetConfirmation = "RESET-CONF";

    /// <summary>Name shown for a restart request (also a restart indication).</summary>
    public const string RestartRequest = "RESTART-REQ";

    /// <summary>Name shown for a restart confirmation.</summary>
    public const string RestartConfirmation = "RESTART-CONF";

    /// <summary>Name shown for a registration request.</summary>
    public const string RegistrationRequest = "REG-REQ";

    /// <summary>Name shown for a registration confirmation.</summary>
    public const string RegistrationConfirmation = "REG-CONF";

    /// <summary>Name shown for a diagnostic packet.</summary>
    public const string Diagnostic = "DIAG";

    /// <summary>The filter group for data packets.</summary>
    public const string GroupData = "DATA";

    /// <summary>The filter group for call request, incoming call, call accepted and call connected.</summary>
    public const string GroupCall = "CALL";

    /// <summary>The filter group for clear request, clear indication and clear confirmation.</summary>
    public const string GroupClear = "CLEAR";

    /// <summary>The filter group for reset request, reset indication and reset confirmation.</summary>
    public const string GroupReset = "RESET";

    /// <summary>The filter group for restart request, restart indication and restart confirmation.</summary>
    public const string GroupRestart = "RESTART";

    /// <summary>The filter group for interrupt and interrupt confirmation.</summary>
    public const string GroupInterrupt = "INT";

    /// <summary>
    /// Gives the short name the packet list shows for a packet type.
    /// </summary>
    /// <param name="type">The packet type the RetroIO parser found.</param>
    /// <param name="rawPti">The type octet, shown in hex when the type is not one the parser knows.</param>
    /// <returns>One of the name constants on this class, or <c>0x</c> and the type octet in hex.</returns>
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
    /// Gives the filter group a packet type belongs to.
    /// </summary>
    /// <param name="type">The packet type the RetroIO parser found.</param>
    /// <returns>
    /// One of the <c>Group</c> constants, or an empty string for a type that has no filter button
    /// (receive ready, receive not ready, reject, registration and diagnostic). Those are always
    /// shown, as the console showed them.
    /// </returns>
    public static string GroupOf(X25PacketType type)
    {
        switch (type)
        {
            case X25PacketType.Data:
                return GroupData;
            case X25PacketType.CallRequest:
            case X25PacketType.CallAccepted:
                return GroupCall;
            case X25PacketType.ClearRequest:
            case X25PacketType.ClearConfirmation:
                return GroupClear;
            case X25PacketType.ResetRequest:
            case X25PacketType.ResetConfirmation:
                return GroupReset;
            case X25PacketType.RestartRequest:
            case X25PacketType.RestartConfirmation:
                return GroupRestart;
            case X25PacketType.Interrupt:
            case X25PacketType.InterruptConfirmation:
                return GroupInterrupt;
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Formats bytes as upper-case hex pairs separated by spaces.
    /// </summary>
    /// <param name="bytes">The bytes to format.</param>
    /// <returns>The hex text, or an empty string when there are no bytes.</returns>
    public static string FormatHex(byte[]? bytes)
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

    /// <summary>
    /// Says in words what a clearing cause code means.
    /// </summary>
    /// <param name="cause">The cause octet of a clear request or clear indication.</param>
    /// <returns>The meaning, or <c>unknown cause</c> for a code this table does not list.</returns>
    /// <remarks>
    /// The codes are the ones RetroIO names in <see cref="X25ClearingCause"/> (the same table its
    /// X.28 command-mode text uses). A cause with bit 8 set is the DTE's own code (X.25 Table 14):
    /// the network just passes it on.
    /// </remarks>
    public static string ClearCauseText(byte cause)
    {
        if ((cause & 0x80) != 0)
        {
            return "DTE originated (code " + (cause & 0x7F) + " from the far DTE)";
        }

        switch (cause)
        {
            case X25ClearingCause.DTE_ORIGINATED: return "DTE originated";
            case X25ClearingCause.NUMBER_BUSY: return "number busy";
            case X25ClearingCause.INVALID_FACILITY_REQUEST: return "invalid facility request";
            case X25ClearingCause.NETWORK_CONGESTION: return "network congestion";
            case X25ClearingCause.OUT_OF_ORDER: return "out of order";
            case X25ClearingCause.ACCESS_BARRED: return "access barred";
            case X25ClearingCause.NOT_OBTAINABLE: return "not obtainable";
            case X25ClearingCause.REMOTE_PROCEDURE_ERROR: return "remote procedure error";
            case X25ClearingCause.LOCAL_PROCEDURE_ERROR: return "local procedure error";
            case X25ClearingCause.RPOA_OUT_OF_ORDER: return "RPOA out of order";
            case X25ClearingCause.REVERSE_CHARGING_NOT_SUBSCRIBED: return "reverse charging acceptance not subscribed";
            case X25ClearingCause.INCOMPATIBLE_DESTINATION: return "incompatible destination";
            case X25ClearingCause.FAST_SELECT_NOT_SUBSCRIBED: return "fast select acceptance not subscribed";
            case X25ClearingCause.SHIP_ABSENT: return "ship absent";
            case X25ClearingCause.INVALID_CALLED_DTE_ADDRESS: return "invalid called DTE address";
            case X25ClearingCause.INVALID_CALLING_DTE_ADDRESS: return "invalid calling DTE address";
            case X25ClearingCause.INVALID_FACILITY_LENGTH: return "invalid facility length";
            case X25ClearingCause.ILLEGAL_FACILITY_CODE: return "illegal facility code";
            default: return "unknown cause";
        }
    }

    /// <summary>
    /// Says in words what a reset cause code means.
    /// </summary>
    /// <param name="cause">The cause octet of a reset request or reset indication.</param>
    /// <returns>The meaning, or <c>unknown cause</c> for a code this table does not list.</returns>
    public static string ResetCauseText(byte cause)
    {
        if ((cause & 0x80) != 0)
        {
            return "DTE originated (code " + (cause & 0x7F) + " from the far DTE)";
        }

        switch (cause)
        {
            case X25ResetCause.DTE_ORIGINATED: return "DTE originated";
            case X25ResetCause.OUT_OF_ORDER: return "out of order";
            case X25ResetCause.REMOTE_PROCEDURE_ERROR: return "remote procedure error";
            case X25ResetCause.LOCAL_PROCEDURE_ERROR: return "local procedure error";
            case X25ResetCause.NETWORK_CONGESTION: return "network congestion";
            case X25ResetCause.REMOTE_DTE_OPERATIONAL: return "remote DTE operational";
            case X25ResetCause.NETWORK_OPERATIONAL: return "network operational";
            case X25ResetCause.INCOMPATIBLE_DESTINATION: return "incompatible destination";
            case X25ResetCause.NETWORK_OUT_OF_ORDER: return "network out of order";
            default: return "unknown cause";
        }
    }

    /// <summary>
    /// Says in words what a restart cause code means.
    /// </summary>
    /// <param name="cause">The cause octet of a restart request or restart indication.</param>
    /// <returns>The meaning, or <c>unknown cause</c> for a code this table does not list.</returns>
    public static string RestartCauseText(byte cause)
    {
        if ((cause & 0x80) != 0)
        {
            return "DTE originated (code " + (cause & 0x7F) + " from the far DTE)";
        }

        switch (cause)
        {
            case X25RestartCause.DTE_ORIGINATED: return "DTE originated";
            case X25RestartCause.LOCAL_PROCEDURE_ERROR: return "local procedure error";
            case X25RestartCause.NETWORK_CONGESTION: return "network congestion";
            case X25RestartCause.NETWORK_OPERATIONAL: return "network operational";
            case X25RestartCause.REGISTRATION_CANCELLATION_CONFIRMED: return "registration/cancellation confirmed";
            default: return "unknown cause";
        }
    }

    /// <summary>
    /// Says in words what a diagnostic code means.
    /// </summary>
    /// <param name="diagnostic">The diagnostic octet of a clear, reset or restart packet.</param>
    /// <returns>The meaning, or <c>not in the table</c> for a code this table does not list.</returns>
    /// <remarks>
    /// Only the codes RetroIO itself names in <see cref="X25DiagnosticCode"/> are listed. X.25 Annex E
    /// has many more; a code outside this list is shown as a number with the words "not in the table",
    /// not guessed.
    /// </remarks>
    public static string DiagnosticText(byte diagnostic)
    {
        switch (diagnostic)
        {
            case X25DiagnosticCode.NO_ADDITIONAL_INFO: return "no additional information";
            case X25DiagnosticCode.INVALID_PS: return "invalid P(S)";
            case X25DiagnosticCode.INVALID_PR: return "invalid P(R)";
            case X25DiagnosticCode.PACKET_TYPE_INVALID: return "packet type invalid";
            case X25DiagnosticCode.PACKET_TYPE_INVALID_FOR_STATE: return "packet type invalid for this state";
            case X25DiagnosticCode.PACKET_LENGTH_INVALID: return "packet length invalid";
            case X25DiagnosticCode.PACKET_NOT_ALLOWED: return "packet not allowed";
            case X25DiagnosticCode.TIMER_EXPIRED_INCOMING_CALL: return "timer expired for incoming call";
            case X25DiagnosticCode.TIMER_EXPIRED_CLEAR_INDICATION: return "timer expired for clear indication";
            case X25DiagnosticCode.TIMER_EXPIRED_RESET_INDICATION: return "timer expired for reset indication";
            case X25DiagnosticCode.TIMER_EXPIRED_RESTART_INDICATION: return "timer expired for restart indication";
            default: return "not in the table";
        }
    }

    /// <summary>
    /// Names the LAPB address octet.
    /// </summary>
    /// <param name="address">The LAPB address octet.</param>
    /// <returns><c>DCE</c> for 0x01, <c>DTE</c> for 0x03, otherwise <c>unknown</c>.</returns>
    public static string AddressName(byte address)
    {
        switch (address)
        {
            case 0x01: return "DCE";
            case 0x03: return "DTE";
            default: return "unknown";
        }
    }

    /// <summary>
    /// Names the LAPB frame class as a single letter.
    /// </summary>
    /// <param name="type">The LAPB frame class.</param>
    /// <returns><c>I</c>, <c>S</c> or <c>U</c>, or <c>?</c> for an unknown class.</returns>
    public static string L2Token(LapbFrameType type)
    {
        switch (type)
        {
            case LapbFrameType.Information: return "I";
            case LapbFrameType.Supervisory: return "S";
            case LapbFrameType.Unnumbered: return "U";
            default: return "?";
        }
    }

    /// <summary>
    /// Names a LAPB supervisory frame from its control octet.
    /// </summary>
    /// <param name="control">The control octet (modulo 8 layout).</param>
    /// <returns><c>RR</c>, <c>RNR</c>, <c>REJ</c>, or the low four bits in hex.</returns>
    public static string SupervisoryName(byte control)
    {
        int kind = control & 0x0F;
        switch (kind)
        {
            case 0x01: return "RR";
            case 0x05: return "RNR";
            case 0x09: return "REJ";
            default: return "0x" + kind.ToString("X2");
        }
    }

    /// <summary>
    /// Names a LAPB unnumbered frame from its control octet.
    /// </summary>
    /// <param name="control">The control octet; the poll/final bit is ignored.</param>
    /// <returns><c>SABM</c>, <c>UA</c>, <c>DISC</c>, <c>DM</c>, <c>FRMR</c>, or the octet in hex.</returns>
    public static string UnnumberedName(byte control)
    {
        int kind = control & 0xEF;
        switch (kind)
        {
            case 0x2F: return "SABM";
            case 0x63: return "UA";
            case 0x43: return "DISC";
            case 0x0F: return "DM";
            case 0x87: return "FRMR";
            default: return "0x" + kind.ToString("X2");
        }
    }
}
