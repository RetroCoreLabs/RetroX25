//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Media;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// One trunk (an XOT peer or an HDLC trunk) in the main window's "Trunk links" section: three short lines of text and the colour
/// of the state word. It only presents a <see cref="XotPeerRow"/>; it reads nothing from the engine.
/// </summary>
public sealed class XotPeerLineViewModel
{
    /// <summary>
    /// Creates the line from a snapshot row.
    /// </summary>
    /// <param name="row">The row to present.</param>
    public XotPeerLineViewModel(XotPeerRow row)
    {
        Row = row;
        HeadStart = row.Name + " (" + row.Source + ")  " + row.Direction + " " + row.Peer + "  [";
        State = row.State;
        Behaviour = row.Behaviour;
        Detail = row.Detail;
        StateBrush = row.Health == LinkHealth.Up ? Brushes.Green : row.Health == LinkHealth.Down ? Brushes.Red : Brushes.Gray;
        Text = row.FullText;
    }

    /// <summary>Gets the row this line presents.</summary>
    public XotPeerRow Row { get; }

    /// <summary>Gets the first part of the first line, up to the state word.</summary>
    public string HeadStart { get; }

    /// <summary>Gets the state word, shown in colour.</summary>
    public string State { get; }

    /// <summary>Gets the second line: how the link behaves after a failure.</summary>
    public string Behaviour { get; }

    /// <summary>Gets the third line: history and counters.</summary>
    public string Detail { get; }

    /// <summary>Gets the colour of the state: green up, red down, grey not decided.</summary>
    public IBrush StateBrush { get; }

    /// <summary>Gets all the text of the line in one string, used to tell whether it changed.</summary>
    public string Text { get; }
}
