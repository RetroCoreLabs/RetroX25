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
/// Service for managing window navigation and lifecycle.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Opens a new window.
    /// </summary>
    /// <param name="window">Window to open</param>
    void OpenWindow(Window window);

    /// <summary>
    /// Closes the specified window.
    /// </summary>
    /// <param name="window">Window to close</param>
    void CloseWindow(Window window);

    /// <summary>
    /// Shows a window modally (blocks parent window until closed).
    /// </summary>
    /// <param name="window">Window to show</param>
    /// <param name="parent">Parent window</param>
    Task ShowDialog(Window window, Window? parent = null);

    /// <summary>
    /// Gets all currently open windows.
    /// </summary>
    /// <returns>List of open windows</returns>
    List<Window> GetOpenWindows();

    /// <summary>
    /// Brings the specified window to front.
    /// </summary>
    /// <param name="window">Window to bring to front</param>
    void BringToFront(Window window);
}
