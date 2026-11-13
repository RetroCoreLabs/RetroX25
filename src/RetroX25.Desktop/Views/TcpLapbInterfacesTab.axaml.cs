//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The TCP LAPB tab of the Configuration window, which lists the interfaces that carry LAPB
/// (the X.25 link layer) over a TCP connection.
/// </summary>
public partial class TcpLapbInterfacesTab : UserControl
{
    /// <summary>
    /// Creates the tab and loads its layout.
    /// </summary>
    public TcpLapbInterfacesTab()
    {
        InitializeComponent();
    }
}
