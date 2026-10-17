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
/// Implementation of INavigationService for managing windows.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly List<Window> _openWindows = new();

    /// <inheritdoc/>
    /// <remarks>
    /// The window is added to the open-window list the first time it is passed in, and is
    /// removed from the list again when it closes.
    /// </remarks>
    public void OpenWindow(Window window)
    {
        if (!_openWindows.Contains(window))
        {
            _openWindows.Add(window);
            window.Closed += (s, e) => _openWindows.Remove(window);
        }

        window.Show();
    }

    /// <inheritdoc/>
    public void CloseWindow(Window window)
    {
        window.Close();
        _openWindows.Remove(window);
    }

    /// <summary>
    /// Shows <paramref name="window"/> as a modal dialog over <paramref name="parent"/>.
    /// </summary>
    /// <param name="window">
    /// The window to show.
    /// </param>
    /// <param name="parent">
    /// The window the dialog blocks. When it is null, <paramref name="window"/> is shown as an
    /// ordinary, non-modal window and the task finishes at once.
    /// </param>
    /// <returns>
    /// A task that finishes when the modal dialog closes.
    /// </returns>
    public async Task ShowDialog(Window window, Window? parent = null)
    {
        if (parent != null)
        {
            await window.ShowDialog(parent);
        }
        else
        {
            window.Show();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only windows opened through <see cref="OpenWindow"/> are listed. The list is a copy, so
    /// changing it does not change the service.
    /// </remarks>
    public List<Window> GetOpenWindows()
    {
        return new List<Window>(_openWindows);
    }

    /// <inheritdoc/>
    public void BringToFront(Window window)
    {
        window.Activate();
        window.BringIntoView();
    }
}
