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

    public void OpenWindow(Window window)
    {
        if (!_openWindows.Contains(window))
        {
            _openWindows.Add(window);
            window.Closed += (s, e) => _openWindows.Remove(window);
        }

        window.Show();
    }

    public void CloseWindow(Window window)
    {
        window.Close();
        _openWindows.Remove(window);
    }

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

    public List<Window> GetOpenWindows()
    {
        return new List<Window>(_openWindows);
    }

    public void BringToFront(Window window)
    {
        window.Activate();
        window.BringIntoView();
    }
}
