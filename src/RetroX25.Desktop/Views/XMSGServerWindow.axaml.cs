//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The XMSG Server window (F12), which shows the LocalXMSG protocol sessions used for COSMOS
/// file services.
/// </summary>
public partial class XMSGServerWindow : Window
{
    /// <summary>
    /// Creates the window and loads its layout, with no view-model attached.
    /// </summary>
    public XMSGServerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window bound to <paramref name="viewModel"/>.
    /// </summary>
    /// <param name="viewModel">
    /// The view-model the window binds to.
    /// </param>
    public XMSGServerWindow(XMSGServerWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
