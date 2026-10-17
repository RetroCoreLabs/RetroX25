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
/// The SINTRAN TAD Client window, a terminal that connects to a remote SINTRAN TAD server.
/// </summary>
public partial class SintranTadClientWindow : Window
{
    /// <summary>
    /// Creates the window and loads its layout, with no view-model attached.
    /// </summary>
    public SintranTadClientWindow()
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
    /// Pressing Enter in the input box runs the view-model's send-input command.
    /// </remarks>
    public SintranTadClientWindow(SintranTadClientWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Handle Enter key in input TextBox
        var inputBox = this.FindControl<TextBox>("InputTextBox");
        if (inputBox != null)
        {
            inputBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    viewModel.SendInputCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }
    }
}
