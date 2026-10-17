//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Opens, closes, lists and focuses the application's windows, so view-models can do it without
/// holding a reference to the user interface.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Shows a window as an ordinary, non-modal window and keeps track of it until it closes.
    /// </summary>
    /// <param name="window">
    /// The window to show.
    /// </param>
    void OpenWindow(Window window);

    /// <summary>
    /// Closes a window and stops tracking it.
    /// </summary>
    /// <param name="window">
    /// The window to close.
    /// </param>
    void CloseWindow(Window window);

    /// <summary>
    /// Shows a window as a modal dialog over a parent window.
    /// </summary>
    /// <param name="window">
    /// The window to show.
    /// </param>
    /// <param name="parent">
    /// The window the dialog blocks until it closes. When it is null there is nothing to block, so
    /// the window is shown as an ordinary, non-modal one.
    /// </param>
    /// <returns>
    /// A task that finishes when a modal dialog closes. With no parent it finishes at once, because
    /// the window is not modal.
    /// </returns>
    Task ShowDialog(Window window, Window? parent = null);

    /// <summary>
    /// Gets the windows that were opened through <see cref="OpenWindow"/> and are still open.
    /// </summary>
    /// <returns>
    /// A copy of the list; changing it does not change what the service tracks.
    /// </returns>
    List<Window> GetOpenWindows();

    /// <summary>
    /// Activates a window so that it comes to the front.
    /// </summary>
    /// <param name="window">
    /// The window to bring forward.
    /// </param>
    void BringToFront(Window window);
}
