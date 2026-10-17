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
/// Dialog where the user adds or edits one serial interface.
/// </summary>
/// <remarks>
/// The caller sets the <see cref="SerialInterfaceEditDialogViewModel"/> as the data context.
/// OK closes the dialog with <c>true</c> only when the view-model has produced a result;
/// Cancel closes it with <c>false</c>.
/// </remarks>
public partial class SerialInterfaceEditDialog : Window
{
    /// <summary>
    /// Creates the dialog and loads its layout.
    /// </summary>
    public SerialInterfaceEditDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        // Check if validation passed by checking if Result is set
        if (DataContext is SerialInterfaceEditDialogViewModel viewModel && viewModel.Result != null)
        {
            Close(true); // Return true to indicate OK was clicked
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false); // Return false to indicate Cancel was clicked
    }
}
