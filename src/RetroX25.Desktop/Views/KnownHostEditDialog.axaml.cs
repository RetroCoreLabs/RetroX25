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
/// Dialog where the user adds or edits one known host entry.
/// </summary>
/// <remarks>
/// The caller sets the <see cref="KnownHostEditDialogViewModel"/> as the data context. OK
/// closes the dialog with <c>true</c> only when the view-model has produced a result; Cancel
/// closes it with <c>false</c>.
/// </remarks>
public partial class KnownHostEditDialog : Window
{
    /// <summary>
    /// Creates the dialog and loads its layout.
    /// </summary>
    public KnownHostEditDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        // Check if validation passed by checking if Result is set
        if (DataContext is KnownHostEditDialogViewModel viewModel && viewModel.Result != null)
        {
            Close(true); // Return true to indicate OK was clicked
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false); // Return false to indicate Cancel was clicked
    }
}
