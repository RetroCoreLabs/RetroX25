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
/// The PAD Client window (F10), a terminal for the X.3/X.28/X.29 PAD client.
/// </summary>
public partial class PADClientWindow : Window
{
    /// <summary>
    /// Creates the window and loads its layout, with no view-model attached.
    /// </summary>
    public PADClientWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window bound to <paramref name="viewModel"/>.
    /// </summary>
    /// <param name="viewModel">
    /// The view-model the window binds to.
    /// </param>
    /// <remarks>
    /// Every key press and every piece of typed text in the window goes straight to the
    /// view-model, and the window takes keyboard focus as soon as it opens.
    /// </remarks>
    public PADClientWindow(PADClientWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Wire up KeyDown and TextInput events for character mode
        // All keypresses are sent immediately to the PAD client
        this.KeyDown += (sender, e) =>
        {
            viewModel.HandleKeyDown(e);
        };

        this.TextInput += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Text))
            {
                viewModel.HandleTextInput(e.Text);
            }
        };

        // Focus the window so it can receive keyboard input immediately
        this.Opened += (sender, e) =>
        {
            this.Focus();
        };
    }
}
