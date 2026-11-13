//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Generic;

namespace RetroX25.Desktop.Services;

/// <summary>
/// One line of the packet detail tree, the way Wireshark shows a packet: a heading such as "LAPB
/// frame" with the fields of that layer folded under it. A line can point at the bytes it was read
/// from, so the window can mark those bytes in the hex dump when the line is clicked.
/// </summary>
public sealed class PacketTreeNode
{
    /// <summary>
    /// Creates a line.
    /// </summary>
    /// <param name="text">The text shown, for example "Control: 0x3F".</param>
    /// <param name="byteStart">First byte of the payload this line was read from, or -1 for none.</param>
    /// <param name="byteLength">How many bytes it covers; ignored when <paramref name="byteStart"/> is -1.</param>
    public PacketTreeNode(string text, int byteStart = -1, int byteLength = 0)
    {
        Text = text;
        ByteStart = byteStart;
        ByteLength = byteStart < 0 ? 0 : byteLength;
    }

    /// <summary>
    /// Gets the text shown for the line.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the first byte of the payload the line covers, or -1 when it covers no bytes (a summary
    /// line, a verdict such as "frame check OK").
    /// </summary>
    public int ByteStart { get; }

    /// <summary>
    /// Gets how many payload bytes the line covers; 0 when <see cref="ByteStart"/> is -1.
    /// </summary>
    public int ByteLength { get; }

    /// <summary>
    /// Gets a value saying whether the line covers any payload bytes.
    /// </summary>
    public bool HasBytes => ByteStart >= 0 && ByteLength > 0;

    /// <summary>
    /// Gets the lines folded under this one.
    /// </summary>
    public List<PacketTreeNode> Children { get; } = new List<PacketTreeNode>();

    /// <summary>
    /// Adds a child line and returns it, so fields can be added under it in turn.
    /// </summary>
    /// <param name="text">The text of the new line.</param>
    /// <param name="byteStart">First byte it covers, or -1.</param>
    /// <param name="byteLength">How many bytes it covers.</param>
    /// <returns>The new line.</returns>
    public PacketTreeNode Add(string text, int byteStart = -1, int byteLength = 0)
    {
        PacketTreeNode child = new PacketTreeNode(text, byteStart, byteLength);
        Children.Add(child);
        return child;
    }
}
