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
/// The Log window (F7), which shows the application log with filtering.
/// </summary>
public partial class LogWindow : Window
{
    /// <summary>
    /// Creates the window and loads its layout, with no view-model attached.
    /// </summary>
    public LogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window bound to <paramref name="viewModel"/>, and closes it when the user
    /// presses Esc.
    /// </summary>
    /// <param name="viewModel">
    /// The view-model the window binds to.
    /// </param>
    public LogWindow(LogWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Add Esc key handler to close window
        this.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        };
    }
}
