//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Implementation of IDialogService using Avalonia MessageBox and dialogs.
/// </summary>
public class DialogService : IDialogService
{
    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    /// <inheritdoc/>
    public void ShowInfo(string title, string message)
    {
        var msgBox = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Info);
        msgBox.ShowAsync();
    }

    /// <inheritdoc/>
    public void ShowWarning(string title, string message)
    {
        var msgBox = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Warning);
        msgBox.ShowAsync();
    }

    /// <inheritdoc/>
    public void ShowError(string title, string message)
    {
        var msgBox = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Error);
        msgBox.ShowAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> ShowConfirmation(string title, string message)
    {
        var msgBox = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Question);
        var result = await msgBox.ShowAsync();
        return result == ButtonResult.Yes;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The picker is shown over the main window. When there is no main window the result is
    /// null and no picker is shown.
    /// </remarks>
    public async Task<string?> ShowOpenFileDialog(string title, string? filters = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null)
            return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrEmpty(filters))
        {
            options.FileTypeFilter = ParseFileFilters(filters);
        }

        var result = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The picker is shown over the main window. When there is no main window the result is
    /// null and no picker is shown.
    /// </remarks>
    public async Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null)
            return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName
        };

        if (!string.IsNullOrEmpty(filters))
        {
            options.FileTypeChoices = ParseFileFilters(filters);
        }

        var result = await mainWindow.StorageProvider.SaveFilePickerAsync(options);
        return result?.Path.LocalPath;
    }

    private List<FilePickerFileType> ParseFileFilters(string filters)
    {
        var result = new List<FilePickerFileType>();

        // Format: "JSON files:*.json;All files:*.*"
        var filterPairs = filters.Split(';');
        foreach (var pair in filterPairs)
        {
            var parts = pair.Split(':');
            if (parts.Length == 2)
            {
                var name = parts[0].Trim();
                var rawPatterns = parts[1].Trim().Split(',');
                var patterns = new List<string>(rawPatterns.Length);
                for (int i = 0; i < rawPatterns.Length; i++)
                {
                    patterns.Add(rawPatterns[i].Trim());
                }

                result.Add(new FilePickerFileType(name)
                {
                    Patterns = patterns
                });
            }
        }

        return result;
    }
}
