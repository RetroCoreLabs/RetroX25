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
/// Dialog where the user adds or edits one X.25 route.
/// </summary>
/// <remarks>
/// The caller sets the <see cref="RouteEditDialogViewModel"/> as the data context. OK closes
/// the dialog with <c>true</c> only when the view-model has produced a result; Cancel closes
/// it with <c>false</c>.
/// </remarks>
public partial class RouteEditDialog : Window
{
    /// <summary>
    /// Creates the dialog and loads its layout.
    /// </summary>
    public RouteEditDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        // Check if validation passed by checking if Result is set
        if (DataContext is RouteEditDialogViewModel viewModel && viewModel.Result != null)
        {
            Close(true); // Return true to indicate OK was clicked
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false); // Return false to indicate Cancel was clicked
    }
}
