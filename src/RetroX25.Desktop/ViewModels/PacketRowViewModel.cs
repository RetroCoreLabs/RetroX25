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
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// One line of the Packet window: a history item from the runtime plus what was read out of its
/// bytes (frame class, packet type, channel number, sequence numbers, flags, check result).
/// </summary>
/// <remarks>
/// <para>
/// The values are worked out once, when the line is made, and never change after that. The decoding
/// itself is done by <see cref="X25PacketParser"/> in RetroIO and by the fields the runtime already
/// put on its LAPB and HDLC history items; this class only copies them into text a column can show.
/// </para>
/// <para>
/// The text-mode console kept the same set of fields in its own packet record and filled them in
/// the same way.
/// </para>
/// </remarks>
public sealed class PacketRowViewModel
{
    private readonly X25PacketInfo? _parsed;

    /// <summary>
    /// Makes a line from a history item.
    /// </summary>
    /// <param name="item">
    /// The history item the runtime recorded.
    /// </param>
    /// <param name="runtimeFlagsError">
    /// True when the runtime itself marked this packet as one it could not process (it keeps that
    /// note in its packet relationship table, apart from the history item).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="item"/> is null.
    /// </exception>
    public PacketRowViewModel(HistoryItem item, bool runtimeFlagsError)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));

        L2Token = string.Empty;
        L2Name = string.Empty;
        L3Type = string.Empty;
        L3Group = string.Empty;
        Lcn = string.Empty;
        Sequence = string.Empty;
        Flags = string.Empty;
        Fcs = string.Empty;
        ErrorText = string.Empty;

        // A LAPB frame comes either as a plain LAPB item or as the LAPB layer of a combined
        // (unified) item, which the simulate-receive window and the runtime's own simulation make.
        bool hasLapb = false;
        LapbFrameType lapbType = LapbFrameType.Information;
        byte lapbControl = 0;
        byte? lapbNs = null;
        byte? lapbNr = null;
        if (item is LapbHistoryItem lapb)
        {
            hasLapb = true;
            lapbType = lapb.FrameType;
            lapbControl = lapb.Control;
            lapbNs = lapb.NS;
            lapbNr = lapb.NR;
        }
        else if (item is UnifiedHistoryItem unifiedLapb && unifiedLapb.PacketInfo.LAPB != null)
        {
            hasLapb = true;
            lapbType = unifiedLapb.PacketInfo.LAPB.FrameType;
            lapbControl = unifiedLapb.PacketInfo.LAPB.Control;
            lapbNs = unifiedLapb.PacketInfo.LAPB.NS;
            lapbNr = unifiedLapb.PacketInfo.LAPB.NR;
        }

        if (hasLapb)
        {
            L2Token = PacketDecoding.L2Token(lapbType);
            switch (lapbType)
            {
                case LapbFrameType.Information:
                    L2Name = "I";
                    break;
                case LapbFrameType.Supervisory:
                    L2Name = PacketDecoding.SupervisoryName(lapbControl);
                    break;
                case LapbFrameType.Unnumbered:
                    L2Name = PacketDecoding.UnnumberedName(lapbControl);
                    break;
                default:
                    L2Name = "?";
                    break;
            }

            Sequence = FormatSequence(lapbNs, lapbNr);
        }

        bool? fcsValid = null;
        if (item is HdlcHistoryItem hdlc)
        {
            fcsValid = hdlc.FcsValid;
        }
        else if (item is UnifiedHistoryItem unifiedHdlc && unifiedHdlc.PacketInfo.HDLC != null)
        {
            fcsValid = unifiedHdlc.PacketInfo.HDLC.FcsValid;
        }

        if (fcsValid.HasValue)
        {
            Fcs = fcsValid.Value ? "OK" : "BAD";
            if (!fcsValid.Value)
            {
                ErrorText = "HDLC frame check sequence did not match";
            }
        }

        if (!hasLapb && !fcsValid.HasValue && item.Layer == LayerType.X25 && item.Payload.Length >= 3)
        {
            // The same parser the engine uses reads the packet; its answer is what is shown.
            _parsed = X25PacketParser.ParsePacket(item.Payload);
            DecodeX25(_parsed);
        }

        if (runtimeFlagsError && ErrorText.Length == 0)
        {
            ErrorText = "the runtime marked this packet as not processed";
        }

        if (item is UnifiedHistoryItem unified && unified.HasProcessingError)
        {
            ErrorText = unified.PacketInfo.ErrorReason ?? "processing error";
        }

        IsError = ErrorText.Length > 0;
    }

    /// <summary>Gets the history item this line shows.</summary>
    public HistoryItem Item { get; }

    /// <summary>Gets the history item number.</summary>
    public long Id => Item.Id;

    /// <summary>Gets the time the runtime recorded the packet.</summary>
    public DateTime Timestamp => Item.Timestamp;

    /// <summary>Gets the protocol layer the packet belongs to (HDLC, LAPB or X.25).</summary>
    public LayerType Layer => Item.Layer;

    /// <summary>Gets the direction: TX for sent, RX for received.</summary>
    public Direction Direction => Item.Direction;

    /// <summary>Gets the length of the packet in bytes.</summary>
    public int Length => Item.Length;

    /// <summary>Gets the runtime's one-line description.</summary>
    public string Description => Item.Description;

    /// <summary>
    /// Gets the LAPB frame class as <c>I</c>, <c>S</c> or <c>U</c>; empty for a line that is not a LAPB frame.
    /// </summary>
    public string L2Token { get; }

    /// <summary>
    /// Gets the LAPB frame name, such as <c>I</c>, <c>RR</c> or <c>SABM</c>; empty for a line that is not a LAPB frame.
    /// </summary>
    public string L2Name { get; }

    /// <summary>
    /// Gets the X.25 packet type name, such as <c>CALL-REQ</c> or <c>DATA</c>; empty for a line that
    /// is not an X.25 packet.
    /// </summary>
    public string L3Type { get; private set; }

    /// <summary>
    /// Gets the filter group of the packet type (DATA, CALL, CLEAR, RESET, RESTART or INT); empty when
    /// the type has no filter of its own or the line is not an X.25 packet.
    /// </summary>
    public string L3Group { get; private set; }

    /// <summary>
    /// Gets the logical channel number as text; empty for a line that is not an X.25 packet.
    /// </summary>
    public string Lcn { get; private set; }

    /// <summary>
    /// Gets the logical channel number, or null for a line that is not an X.25 packet.
    /// </summary>
    public int? LcnValue { get; private set; }

    /// <summary>
    /// Gets the sequence numbers: N(S)/N(R) for a LAPB frame, P(S)/P(R) for an X.25 data packet,
    /// <c>-/n</c> when only the receive number exists; empty when the line has none.
    /// </summary>
    public string Sequence { get; private set; }

    /// <summary>
    /// Gets the Q, D and M flags that are set, as letters (for example <c>Q M</c>); empty when none is set.
    /// </summary>
    public string Flags { get; private set; }

    /// <summary>
    /// Gets the frame check result of an HDLC line: <c>OK</c> or <c>BAD</c>. It is empty for a LAPB
    /// or X.25 line, because that was already tested and removed by the HDLC layer and nobody
    /// tested it again.
    /// </summary>
    public string Fcs { get; }

    /// <summary>
    /// Gets a value indicating whether the line is an error: a packet whose fields run past its end,
    /// an HDLC frame with a bad check, or a packet the runtime marked as not processed.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// Gets the reason the line is an error, or an empty string.
    /// </summary>
    public string ErrorText { get; private set; }

    /// <summary>
    /// Gets the "L3" column text: the X.25 packet type, or the LAPB frame name for a LAPB line.
    /// </summary>
    public string TypeText => L3Type.Length > 0 ? L3Type : L2Name;

    private void DecodeX25(X25PacketInfo info)
    {
        L3Type = PacketDecoding.NameOf(info.PacketType, info.RawPTI);
        L3Group = PacketDecoding.GroupOf(info.PacketType);
        LcnValue = info.LCN;
        Lcn = info.LCN.ToString();

        bool isData = info.PacketType == X25PacketType.Data;
        bool isCallSetUp = info.PacketType == X25PacketType.CallRequest
            || info.PacketType == X25PacketType.CallAccepted;

        bool q = isData && info.QBit;
        bool d = (isData || isCallSetUp) && info.DBit;
        bool m = isData && !info.Modulo128 && info.MBit;

        StringBuilder flags = new StringBuilder(5);
        if (q)
        {
            flags.Append('Q');
        }

        if (d)
        {
            if (flags.Length > 0)
            {
                flags.Append(' ');
            }

            flags.Append('D');
        }

        if (m)
        {
            if (flags.Length > 0)
            {
                flags.Append(' ');
            }

            flags.Append('M');
        }

        Flags = flags.ToString();

        if (isData)
        {
            Sequence = info.PS + "/" + info.PR;
        }
        else if (info.PacketType == X25PacketType.RR
            || info.PacketType == X25PacketType.RNR
            || info.PacketType == X25PacketType.REJ)
        {
            Sequence = "-/" + info.PR;
        }

        if (info.ParseError != null)
        {
            ErrorText = info.ParseError;
        }
    }

    private static string FormatSequence(byte? ns, byte? nr)
    {
        if (!ns.HasValue && !nr.HasValue)
        {
            return string.Empty;
        }

        string left = ns.HasValue ? ns.Value.ToString() : "-";
        string right = nr.HasValue ? nr.Value.ToString() : "-";
        return left + "/" + right;
    }

    /// <summary>
    /// Builds the Wireshark-style tree of this packet: a heading per layer with its fields folded
    /// under it, each field with the payload bytes it was read from.
    /// </summary>
    /// <returns>The top-level lines.</returns>
    public System.Collections.Generic.List<PacketTreeNode> BuildDetailTree()
    {
        return PacketTreeBuilder.Build(Item, _parsed, ErrorText);
    }

    /// <summary>
    /// Builds the full text the detail pane shows for this line.
    /// </summary>
    /// <returns>
    /// Several lines of plain text: where the packet came from, its decoded fields, the cause and
    /// diagnostic codes with their meaning, the facilities as hex, any parse error, and the
    /// library's own packet-specific description.
    /// </returns>
    public string BuildDetailText()
    {
        StringBuilder text = new StringBuilder(512);
        text.Append("Packet #").Append(Item.Id)
            .Append("   ").Append(Item.Timestamp.ToString("HH:mm:ss.fff"))
            .Append("   ").Append(Item.Layer)
            .Append("   ").Append(Item.Direction)
            .Append("   ").Append(Item.Length).AppendLine(" bytes");
        text.Append("Description: ").AppendLine(Item.Description);

        if (Item is LapbHistoryItem lapb)
        {
            AppendLapb(text, lapb);
        }
        else if (Item is HdlcHistoryItem hdlc)
        {
            text.AppendLine();
            text.AppendLine("HDLC frame");
            text.Append("  Frame check (FCS): ").AppendLine(hdlc.FcsValid ? "OK" : "BAD, does not match the frame");
        }
        else if (_parsed != null)
        {
            AppendX25(text, _parsed);
        }

        if (Item is UnifiedHistoryItem unified)
        {
            text.AppendLine();
            text.AppendLine(unified.GetDetailedDisplay());
        }

        if (ErrorText.Length > 0)
        {
            text.AppendLine();
            text.Append("ERROR: ").AppendLine(ErrorText);
        }

        return text.ToString();
    }

    private static void AppendLapb(StringBuilder text, LapbHistoryItem lapb)
    {
        text.AppendLine();
        text.AppendLine("LAPB frame");
        text.Append("  Address: 0x").Append(lapb.Address.ToString("X2"))
            .Append(" (").Append(PacketDecoding.AddressName(lapb.Address)).AppendLine(")");
        text.Append("  Control: 0x").AppendLine(lapb.Control.ToString("X2"));
        text.Append("  Frame class: ").Append(PacketDecoding.L2Token(lapb.FrameType))
            .Append(" (").Append(lapb.FrameType).AppendLine(")");
        switch (lapb.FrameType)
        {
            case LapbFrameType.Information:
                text.Append("  N(S): ").Append(lapb.NS?.ToString() ?? "-")
                    .Append("   N(R): ").Append(lapb.NR?.ToString() ?? "-")
                    .Append("   P/F: ").AppendLine(lapb.PF ? "1" : "0");
                break;
            case LapbFrameType.Supervisory:
                text.Append("  Name: ").AppendLine(PacketDecoding.SupervisoryName(lapb.Control));
                text.Append("  N(R): ").Append(lapb.NR?.ToString() ?? "-")
                    .Append("   P/F: ").AppendLine(lapb.PF ? "1" : "0");
                break;
            case LapbFrameType.Unnumbered:
                text.Append("  Name: ").AppendLine(PacketDecoding.UnnumberedName(lapb.Control));
                text.Append("  P/F: ").AppendLine(lapb.PF ? "1" : "0");
                break;
        }

        text.AppendLine("  The bytes below have no HDLC flags and no FCS.");
    }

    private void AppendX25(StringBuilder text, X25PacketInfo info)
    {
        text.AppendLine();
        text.Append("X.25 packet: ").AppendLine(info.GetPacketTypeName());
        text.Append("  Logical channel (LCN): ").Append(info.LCN)
            .Append("   Modulo: ").AppendLine(info.Modulo128 ? "128" : "8");

        bool isData = info.PacketType == X25PacketType.Data;
        bool isCallSetUp = info.PacketType == X25PacketType.CallRequest
            || info.PacketType == X25PacketType.CallAccepted;

        if (isData)
        {
            text.Append("  Q bit: ").Append(info.QBit ? "1" : "0")
                .Append("   D bit: ").Append(info.DBit ? "1" : "0")
                .Append("   M bit: ");
            if (info.Modulo128)
            {
                text.AppendLine("not shown for modulo 128");
            }
            else
            {
                text.Append(info.MBit ? "1" : "0").AppendLine(info.MBit ? " (more data follows)" : " (last of the message)");
            }

            text.Append("  P(S): ").Append(info.PS).Append("   P(R): ").AppendLine(info.PR.ToString());
            text.Append("  User data: ").Append(info.UserData.Length).AppendLine(" bytes");
        }
        else if (isCallSetUp)
        {
            text.Append("  D bit: ").AppendLine(info.DBit ? "1" : "0");
        }
        else if (info.PacketType == X25PacketType.RR
            || info.PacketType == X25PacketType.RNR
            || info.PacketType == X25PacketType.REJ)
        {
            text.Append("  P(R): ").AppendLine(info.PR.ToString());
        }

        switch (info.PacketType)
        {
            case X25PacketType.ClearRequest:
                AppendCause(text, info.ClearingCause, PacketDecoding.ClearCauseText(info.ClearingCause), info.DiagnosticCode);
                break;
            case X25PacketType.ResetRequest:
                AppendCause(text, info.ResetCause, PacketDecoding.ResetCauseText(info.ResetCause), info.DiagnosticCode);
                break;
            case X25PacketType.RestartRequest:
                AppendCause(text, info.RestartCause, PacketDecoding.RestartCauseText(info.RestartCause), info.DiagnosticCode);
                break;
        }

        if (info.PacketType == X25PacketType.CallRequest || info.PacketType == X25PacketType.CallAccepted)
        {
            text.Append("  Facilities (hex): ");
            text.AppendLine(info.Facilities.Length == 0 ? "none" : PacketDecoding.FormatHex(info.Facilities));
        }

        if (info.ParseError != null)
        {
            text.Append("  Parse error: ").AppendLine(info.ParseError);
        }

        // The library's own plain-word description of the same packet (called address, user data
        // and so on). It is added after the fields above, which it does not all have.
        if (Item is X25HistoryItem x25)
        {
            text.AppendLine();
            text.AppendLine("From the library:");
            text.Append(x25.GetPacketSpecificDetails());
        }
    }

    private static void AppendCause(StringBuilder text, byte cause, string causeText, byte diagnostic)
    {
        text.Append("  Cause: 0x").Append(cause.ToString("X2")).Append(" (").Append(causeText).AppendLine(")");
        text.Append("  Diagnostic: 0x").Append(diagnostic.ToString("X2")).Append(" (")
            .Append(PacketDecoding.DiagnosticText(diagnostic)).AppendLine(")");
    }
}
