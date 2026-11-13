//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using Avalonia.Interactivity;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

/// <summary>
/// Dialog where the user adds or edits one TCP-LAPB interface (LAPB carried over a TCP connection).
/// </summary>
/// <remarks>
/// The caller sets the <see cref="TcpLapbInterfaceEditDialogViewModel"/> as the data context. OK
/// checks the fields and closes the dialog with <c>true</c> only when they are all right, otherwise
/// the dialog stays open and shows the reason; Cancel closes it with <c>false</c>.
/// </remarks>
public partial class TcpLapbInterfaceEditDialog : Window
{
    /// <summary>
    /// Creates the dialog and loads its layout.
    /// </summary>
    public TcpLapbInterfaceEditDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TcpLapbInterfaceEditDialogViewModel viewModel && viewModel.TryAccept())
        {
            Close(true);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
