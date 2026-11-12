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
            else if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                // Ctrl+C copies the selected entry, or every shown entry when none is selected.
                _ = CopyToClipboardAsync(viewModel.GetTextToCopy());
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Puts the selected entry (or all shown entries when none is selected) on the clipboard.
    /// </summary>
    private void OnCopyClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LogWindowViewModel viewModel)
        {
            _ = CopyToClipboardAsync(viewModel.GetTextToCopy());
        }
    }

    /// <summary>
    /// Puts every entry the filters let through on the clipboard, whatever is selected.
    /// </summary>
    private void OnCopyAllClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LogWindowViewModel viewModel)
        {
            _ = CopyToClipboardAsync(viewModel.GetAllShownText());
        }
    }

    private async System.Threading.Tasks.Task CopyToClipboardAsync(string text)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard != null && text.Length > 0)
        {
            await top.Clipboard.SetTextAsync(text);
        }
    }
}
