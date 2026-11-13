//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Builds the Wireshark-style detail tree of one captured packet: a heading per layer (HDLC frame,
/// LAPB frame, X.25 packet) with its fields folded under it, and for each field the bytes it was
/// read from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the byte positions come from.</b> Every position is worked out here from the layout of
/// the X.25 packet header (ITU-T X.25, section 7) and of the LAPB frame (address, control,
/// information), and is only given when the payload is long enough to hold the field. A field whose
/// bytes cannot be placed with certainty gets no position, so the dump never marks bytes that are
/// not that field. The values themselves (LCN, P(S), cause and so on) are the ones the engine's own
/// parser read; the tests compare the bytes marked here with those values.
/// </para>
/// <para>
/// The HDLC frame and the combined records have no byte positions: how those payloads are laid out is
/// not known to this program, so their lines show text only.
/// </para>
/// </remarks>
public static class PacketTreeBuilder
{
    /// <summary>
    /// Builds the tree for one packet.
    /// </summary>
    /// <param name="item">The captured packet.</param>
    /// <param name="parsed">The engine parser's reading of an X.25 packet, or null when the item is not one.</param>
    /// <param name="errorText">The reason the packet is marked as an error, or empty.</param>
    /// <returns>The top-level lines, in the order they are shown.</returns>
    public static List<PacketTreeNode> Build(HistoryItem item, X25PacketInfo? parsed, string errorText)
    {
        List<PacketTreeNode> roots = new List<PacketTreeNode>();

        PacketTreeNode summary = new PacketTreeNode(
            "Packet #" + item.Id + "   " + item.Timestamp.ToString("HH:mm:ss.fff") + "   "
            + item.Layer + "   " + item.Direction + "   " + item.Length + " bytes");
        summary.Add("Description: " + item.Description);
        roots.Add(summary);

        if (item is LapbHistoryItem lapb)
        {
            AddLapb(roots, lapb);
        }
        else if (item is HdlcHistoryItem hdlc)
        {
            PacketTreeNode frame = new PacketTreeNode("HDLC frame");
            frame.Add("Frame check (FCS): " + (hdlc.FcsValid ? "OK" : "BAD, does not match the frame"));
            roots.Add(frame);
        }
        else if (parsed != null)
        {
            AddX25(roots, item.Payload, 0, parsed);
        }

        if (item is UnifiedHistoryItem unified)
        {
            PacketTreeNode combined = new PacketTreeNode("Combined record (all layers of one frame)");
            string[] lines = unified.GetDetailedDisplay().Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length > 0)
                {
                    combined.Add(line);
                }
            }

            roots.Add(combined);
        }

        if (errorText.Length > 0)
        {
            roots.Add(new PacketTreeNode("ERROR: " + errorText));
        }

        return roots;
    }

    private static string Hex(byte value)
    {
        return "0x" + value.ToString("X2");
    }

    private static void AddLapb(List<PacketTreeNode> roots, LapbHistoryItem lapb)
    {
        byte[] p = lapb.Payload;
        PacketTreeNode frame = new PacketTreeNode("LAPB frame", 0, p.Length);
        roots.Add(frame);

        // The payload is address, control, then the information field. It has no HDLC flags and no FCS.
        frame.Add("Address: " + Hex(lapb.Address) + " (" + PacketDecoding.AddressName(lapb.Address) + ")",
            0, p.Length >= 1 ? 1 : 0);

        PacketTreeNode control = frame.Add("Control: " + Hex(lapb.Control), 1, p.Length >= 2 ? 1 : 0);
        control.Add("Frame class: " + PacketDecoding.L2Token(lapb.FrameType) + " (" + lapb.FrameType + ")");
        switch (lapb.FrameType)
        {
            case LapbFrameType.Information:
                control.Add("N(S): " + (lapb.NS?.ToString() ?? "-"));
                control.Add("N(R): " + (lapb.NR?.ToString() ?? "-"));
                break;
            case LapbFrameType.Supervisory:
                control.Add("Name: " + PacketDecoding.SupervisoryName(lapb.Control));
                control.Add("N(R): " + (lapb.NR?.ToString() ?? "-"));
                break;
            case LapbFrameType.Unnumbered:
                control.Add("Name: " + PacketDecoding.UnnumberedName(lapb.Control));
                break;
        }

        control.Add("Poll/Final bit: " + (lapb.PF ? "1" : "0"));

        // An information frame carries an X.25 packet: read it with the engine's own parser and show it
        // under the frame, with positions counted from the start of the LAPB payload.
        if (lapb.FrameType == LapbFrameType.Information && p.Length > 2)
        {
            PacketTreeNode information = frame.Add("Information field", 2, p.Length - 2);
            if (p.Length - 2 >= 3)
            {
                byte[] inner = new byte[p.Length - 2];
                Array.Copy(p, 2, inner, 0, inner.Length);
                X25PacketInfo innerInfo = X25PacketParser.ParsePacket(inner);
                AddX25(information.Children, inner, 2, innerInfo);
            }
            else
            {
                information.Add("Too short to be an X.25 packet (" + (p.Length - 2) + " bytes)");
            }
        }
    }

    private static void AddX25(List<PacketTreeNode> into, byte[] p, int offset, X25PacketInfo info)
    {
        // 'p' holds the X.25 packet; 'offset' is where it starts in the payload the dump shows
        // (0 for a plain X.25 item, 2 for the information field of a LAPB frame).
        int len = p.Length;
        PacketTreeNode root = new PacketTreeNode("X.25 packet: " + info.GetPacketTypeName(), offset, len);
        into.Add(root);

        bool isData = info.PacketType == X25PacketType.Data;
        bool isCallSetUp = info.PacketType == X25PacketType.CallRequest
            || info.PacketType == X25PacketType.CallAccepted;
        bool isSupervisory = info.PacketType == X25PacketType.RR
            || info.PacketType == X25PacketType.RNR
            || info.PacketType == X25PacketType.REJ;

        // Octet 1: general format identifier (Q, D, modulo) in the high four bits, LCN high bits below.
        PacketTreeNode gfi = root.Add("General format identifier", offset, 1);
        gfi.Add("Modulo: " + (info.Modulo128 ? "128" : "8"));
        if (isData)
        {
            gfi.Add("Q bit: " + (info.QBit ? "1" : "0"));
            gfi.Add("D bit: " + (info.DBit ? "1" : "0"));
        }
        else if (isCallSetUp)
        {
            gfi.Add("D bit: " + (info.DBit ? "1" : "0"));
        }

        // Octets 1 and 2 together hold the 12-bit logical channel number.
        root.Add("Logical channel (LCN): " + info.LCN, offset, len >= 2 ? 2 : len);

        // Modulo 128 data, RR, RNR and REJ packets have a four-octet header; every other packet three.
        bool longHeader = info.Modulo128 && (isData || isSupervisory);
        int header = longHeader ? 4 : 3;

        if (isData)
        {
            if (info.Modulo128)
            {
                // Octet 3 is P(S) (bit 1 is 0), octet 4 is P(R) with the M bit in bit 1.
                root.Add("P(S): " + info.PS, offset + 2, len >= 3 ? 1 : 0);
                root.Add("P(R): " + info.PR + "   (the M bit of a modulo 128 packet is not decoded here)", offset + 3, len >= 4 ? 1 : 0);
            }
            else
            {
                // Octet 3 holds P(R) in bits 8-6, the M bit in bit 5 and P(S) in bits 4-2; bit 1 is 0.
                PacketTreeNode seq = root.Add("Sequence octet", offset + 2, len >= 3 ? 1 : 0);
                seq.Add("P(R): " + info.PR);
                seq.Add("M bit: " + (info.MBit ? "1 (more data follows)" : "0 (last of the message)"));
                seq.Add("P(S): " + info.PS);
            }

            if (len > header)
            {
                root.Add("User data: " + (len - header) + " bytes", offset + header, len - header);
            }
            else
            {
                root.Add("User data: none");
            }
        }
        else if (isSupervisory)
        {
            if (info.Modulo128)
            {
                root.Add("Packet type: " + PacketDecoding.NameOf(info.PacketType, info.RawPTI), offset + 2, len >= 3 ? 1 : 0);
                root.Add("P(R): " + info.PR, offset + 3, len >= 4 ? 1 : 0);
            }
            else
            {
                PacketTreeNode typeOctet = root.Add("Type and P(R) octet", offset + 2, len >= 3 ? 1 : 0);
                typeOctet.Add("Packet type: " + PacketDecoding.NameOf(info.PacketType, info.RawPTI));
                typeOctet.Add("P(R): " + info.PR);
            }
        }
        else
        {
            root.Add("Packet type: " + PacketDecoding.NameOf(info.PacketType, info.RawPTI) + " (" + Hex(info.RawPTI) + ")",
                offset + 2, len >= 3 ? 1 : 0);
        }

        if (isCallSetUp)
        {
            AddCallFields(root, p, offset, info);
        }

        switch (info.PacketType)
        {
            case X25PacketType.ClearRequest:
                AddCause(root, p, offset, info.ClearingCause, PacketDecoding.ClearCauseText(info.ClearingCause), info.DiagnosticCode);
                break;
            case X25PacketType.ResetRequest:
                AddCause(root, p, offset, info.ResetCause, PacketDecoding.ResetCauseText(info.ResetCause), info.DiagnosticCode);
                break;
            case X25PacketType.RestartRequest:
                AddCause(root, p, offset, info.RestartCause, PacketDecoding.RestartCauseText(info.RestartCause), info.DiagnosticCode);
                break;
        }

        if (info.ParseError != null)
        {
            root.Add("Parse error: " + info.ParseError);
        }
    }

    /// <summary>
    /// Call Request and Call Accepted: octet 4 holds the two address lengths (called in the low four
    /// bits, calling in the high four), then the addresses as packed digits, then one octet with the
    /// facilities length, the facilities, and the call user data to the end.
    /// </summary>
    private static void AddCallFields(PacketTreeNode root, byte[] p, int offset, X25PacketInfo info)
    {
        if (p.Length < 4)
        {
            return;
        }

        int called = p[3] & 0x0F;
        int calling = (p[3] >> 4) & 0x0F;
        int addressBytes = (called + calling + 1) / 2;

        PacketTreeNode lengths = root.Add("Address lengths: called " + called + ", calling " + calling + " digits", offset + 3, 1);
        lengths.Add("Called address length: " + called);
        lengths.Add("Calling address length: " + calling);

        if (p.Length < 4 + addressBytes)
        {
            root.Add("The packet ends inside the addresses");
            return;
        }

        // The digits run on from one address into the next, two to an octet. An address that starts
        // or ends in the middle of an octet is shown with the whole octet(s) it touches.
        int calledBytes = (called + 1) / 2;
        int callingFirst = called / 2;
        int callingBytes = ((called + calling + 1) / 2) - callingFirst;
        if (called > 0)
        {
            root.Add("Called address: " + info.CalledAddress, offset + 4, calledBytes);
        }
        else
        {
            root.Add("Called address: none");
        }

        if (calling > 0)
        {
            root.Add("Calling address: " + info.CallingAddress, offset + 4 + callingFirst, callingBytes);
        }
        else
        {
            root.Add("Calling address: none");
        }

        int facilityLengthAt = 4 + addressBytes;
        if (p.Length <= facilityLengthAt)
        {
            return;
        }

        int facilityLength = p[facilityLengthAt];
        if (p.Length < facilityLengthAt + 1 + facilityLength)
        {
            root.Add("The packet ends inside the facilities");
            return;
        }

        PacketTreeNode facilities = root.Add(
            facilityLength == 0 ? "Facilities: none" : "Facilities: " + facilityLength + " bytes",
            offset + facilityLengthAt, 1 + facilityLength);
        if (facilityLength > 0)
        {
            byte[] raw = new byte[facilityLength];
            Array.Copy(p, facilityLengthAt + 1, raw, 0, facilityLength);
            facilities.Add("Hex: " + PacketDecoding.FormatHex(raw), offset + facilityLengthAt + 1, facilityLength);
        }

        int userDataAt = facilityLengthAt + 1 + facilityLength;
        if (p.Length > userDataAt)
        {
            root.Add("Call user data: " + (p.Length - userDataAt) + " bytes", offset + userDataAt, p.Length - userDataAt);
        }
    }

    /// <summary>
    /// Clear, Reset and Restart requests: octet 4 is the cause and octet 5 the diagnostic.
    /// </summary>
    private static void AddCause(PacketTreeNode root, byte[] p, int offset, byte cause, string causeText, byte diagnostic)
    {
        root.Add("Cause: " + Hex(cause) + " (" + causeText + ")", offset + 3, p.Length >= 4 ? 1 : 0);
        root.Add("Diagnostic: " + Hex(diagnostic) + " (" + PacketDecoding.DiagnosticText(diagnostic) + ")",
            offset + 4, p.Length >= 5 ? 1 : 0);
    }
}
