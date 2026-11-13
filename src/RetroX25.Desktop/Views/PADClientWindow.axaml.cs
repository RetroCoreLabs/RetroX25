//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The PAD Client window (Ctrl+5), a terminal for the X.3/X.28/X.29 PAD client.
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
            // Ctrl+C copies the marked console text. With nothing marked it stays the terminal's
            // break key, as before.
            if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                SelectableTextBlock? box = this.FindControl<SelectableTextBlock>("ConsoleBox");
                if (box != null && !string.IsNullOrEmpty(box.SelectedText))
                {
                    box.Copy();
                    e.Handled = true;
                    return;
                }
            }

            viewModel.HandleKeyDown(e);
        };

        this.TextInput += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Text))
            {
                viewModel.HandleTextInput(e.Text);
            }
        };

        // Follow the output. "Following" means the person is looking at the end of the text. It changes
        // only when the person scrolls (the offset moves while the text and the visible area stay the
        // same): to the end = following, anywhere else = not following, so someone who scrolled up to
        // read is left alone. While following, every time the text grows or the visible area shrinks
        // (a horizontal scroll bar appears and takes room from the bottom) the view goes to the end
        // again. Deciding this from the scroll view's own change notices, and not from a guess made when
        // the text property changes, means no timing between text, layout and scrolling can lose it.
        ScrollViewer scroll = this.FindControl<ScrollViewer>("ConsoleScroll")!;
        bool following = true;
        scroll.ScrollChanged += (sender, e) =>
        {
            bool contentMoved = e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0;
            if (contentMoved)
            {
                if (following)
                {
                    scroll.ScrollToEnd();
                }
            }
            else if (e.OffsetDelta.Y != 0)
            {
                following = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 2;
            }
        };

        // Focus the window so it can receive keyboard input immediately
        this.Opened += (sender, e) =>
        {
            this.Focus();
        };
    }
}
