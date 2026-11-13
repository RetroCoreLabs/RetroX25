//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using Avalonia.Input;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The Link Status window (Ctrl+L): the links and interfaces of the running engine, with start,
/// stop and LAPB connect and disconnect for the selected one.
/// </summary>
public partial class LinkStatusWindow : Window
{
    /// <summary>
    /// Creates the window and loads its layout, with no view-model attached.
    /// </summary>
    public LinkStatusWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window bound to <paramref name="viewModel"/>, closes it on Esc, and stops the
    /// view-model's refresh timer when the window closes.
    /// </summary>
    /// <param name="viewModel">
    /// The view-model the window binds to.
    /// </param>
    public LinkStatusWindow(LinkStatusWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        Closed += (sender, e) => viewModel.Dispose();
    }
}
