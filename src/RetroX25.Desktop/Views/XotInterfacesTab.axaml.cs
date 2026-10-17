//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The XOT Interfaces tab of the Configuration window, which lists the XOT (X.25 over TCP)
/// interfaces.
/// </summary>
public partial class XotInterfacesTab : UserControl
{
    /// <summary>
    /// Creates the tab and loads its layout.
    /// </summary>
    public XotInterfacesTab()
    {
        InitializeComponent();
    }
}
