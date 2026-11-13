//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Lapb;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Common;
using RetroX25.Desktop.Services;
using LogLevel = RetroIO.Protocols.X25.Common.LogLevel;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Log Window (Ctrl+3) - displays application logs with filtering.
/// </summary>
public partial class LogWindowViewModel : ViewModelWithLogger
{
    private readonly HistoryLoggingProvider _loggingProvider;
    private readonly IDialogService _dialogService;

    // Observable collections for display
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();

    [ObservableProperty]
    private LogEntry? _selectedLog;

    // Filter properties
    [ObservableProperty]
    private bool _showDebug = true;

    [ObservableProperty]
    private bool _showInfo = true;

    [ObservableProperty]
    private bool _showWarning = true;

    [ObservableProperty]
    private bool _showError = true;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private string _statusText = "0 log entries";

    [ObservableProperty]
    private bool _autoScroll = true; // Auto-scroll to bottom

    /// <summary>
    /// Creates LogWindowViewModel with dependency injection.
    /// </summary>
    public LogWindowViewModel(
        ILogger<LogWindowViewModel> logger,
        HistoryLoggingProvider loggingProvider,
        IDialogService dialogService) : base(logger)
    {
        _loggingProvider = loggingProvider ?? throw new ArgumentNullException(nameof(loggingProvider));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        // Subscribe to logging provider events
        _loggingProvider.LogEntryAdded += OnLogEntryAdded;
        LoadExistingLogs();

        Logger.LogInformation("LogWindowViewModel initialized");
    }

    /// <summary>
    /// Load existing logs from provider.
    /// </summary>
    private void LoadExistingLogs()
    {
        var logs = _loggingProvider.GetAllLogs();

        foreach (var log in logs)
        {
            if (ShouldShowLog(log))
            {
                LogEntries.Add(log);
            }
        }

        UpdateStatus();
        Logger.LogDebug("Loaded {Count} existing log entries", LogEntries.Count);
    }

    /// <summary>
    /// Log entries that arrived from any thread and are waiting for the UI thread.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _incoming = new();

    /// <summary>
    /// 1 while a flush of <see cref="_incoming"/> is already queued on the UI thread.
    /// </summary>
    private int _flushQueued;

    /// <summary>
    /// A batch of at most this many entries is added one at a time; a larger one replaces the list.
    /// </summary>
    private const int SmallBatch = 16;

    /// <summary>
    /// Handle new log entries (any thread). They are queued and the UI thread takes everything that has
    /// arrived in ONE job: one job per entry, each changing the grid, kept the window thread busy for
    /// up to 18 seconds during a burst of 2000 packets (measured with x25traffic on 2026-11-12).
    /// </summary>
    private void OnLogEntryAdded(LogEntry entry)
    {
        _incoming.Enqueue(entry);
        if (System.Threading.Interlocked.Exchange(ref _flushQueued, 1) == 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushIncoming);
        }
    }

    /// <summary>
    /// UI thread: adds the queued entries that match the filters and keeps the list at the size the log
    /// provider keeps (the window must not hold more than the provider, or it grows for as long as the
    /// app runs).
    /// </summary>
    private void FlushIncoming()
    {
        // Open the gate BEFORE draining: an entry that arrives while this runs queues a new flush.
        System.Threading.Interlocked.Exchange(ref _flushQueued, 0);

        List<LogEntry> add = new List<LogEntry>();
        while (_incoming.TryDequeue(out LogEntry? entry))
        {
            if (ShouldShowLog(entry))
            {
                add.Add(entry);
            }
        }

        if (add.Count == 0)
        {
            return;
        }

        int max = _loggingProvider.MaxEntries;
        if (add.Count <= SmallBatch)
        {
            for (int i = 0; i < add.Count; i++)
            {
                LogEntries.Add(add[i]);
            }

            int extra = LogEntries.Count - max;
            for (int i = 0; i < extra; i++)
            {
                LogEntries.RemoveAt(0);
            }
        }
        else
        {
            // Keep the newest `max` of the old and the new entries, and give the grid one new list.
            List<LogEntry> all = new List<LogEntry>(LogEntries.Count + add.Count);
            all.AddRange(LogEntries);
            all.AddRange(add);
            int skip = Math.Max(0, all.Count - max);
            LogEntries = new ObservableCollection<LogEntry>(all.GetRange(skip, all.Count - skip));
        }

        UpdateStatus();
    }

    /// <summary>
    /// Check if log should be shown based on current filters.
    /// </summary>
    private bool ShouldShowLog(LogEntry entry)
    {
        // Level filter
        if (entry.Level == LogLevel.Debug && !ShowDebug) return false;
        if (entry.Level == LogLevel.Info && !ShowInfo) return false;
        if (entry.Level == LogLevel.Warning && !ShowWarning) return false;
        if (entry.Level == LogLevel.Error && !ShowError) return false;

        // Text filter (case-insensitive search in message or category)
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.ToLowerInvariant();
            if (!entry.Message.ToLowerInvariant().Contains(filter) &&
                !entry.Category.ToLowerInvariant().Contains(filter))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Update status text.
    /// </summary>
    private void UpdateStatus()
    {
        StatusText = $"{LogEntries.Count} log entries";
    }

    /// <summary>
    /// Refresh display when filters change.
    /// </summary>
    partial void OnShowDebugChanged(bool value) => RefreshDisplay();
    partial void OnShowInfoChanged(bool value) => RefreshDisplay();
    partial void OnShowWarningChanged(bool value) => RefreshDisplay();
    partial void OnShowErrorChanged(bool value) => RefreshDisplay();
    partial void OnFilterTextChanged(string value) => RefreshDisplay();

    /// <summary>
    /// Refresh the log display based on current filters.
    /// </summary>
    private void RefreshDisplay()
    {
        var logs = _loggingProvider.GetAllLogs();
        List<LogEntry> shown = new List<LogEntry>(logs.Count);
        for (int i = 0; i < logs.Count; i++)
        {
            if (ShouldShowLog(logs[i]))
            {
                shown.Add(logs[i]);
            }
        }

        // One new list for the grid, one redraw (adding the entries one at a time redraws it for each).
        LogEntries = new ObservableCollection<LogEntry>(shown);
        UpdateStatus();
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _loggingProvider.ClearHistory();
        LogEntries.Clear();
        UpdateStatus();
        Logger.LogInformation("Log display cleared");
    }

    /// <summary>
    /// The text to put on the clipboard for a copy: the selected entry if there is one, otherwise
    /// every entry the filters let through, one per line, oldest first.
    /// </summary>
    /// <returns>The text, or an empty string when nothing is shown.</returns>
    /// <remarks>
    /// The view puts it on the clipboard, because the clipboard belongs to the window.
    /// </remarks>
    public string GetTextToCopy()
    {
        if (SelectedLog != null)
        {
            return SelectedLog.FullMessage;
        }

        StringBuilder text = new StringBuilder();
        for (int i = 0; i < LogEntries.Count; i++)
        {
            text.AppendLine(LogEntries[i].FullMessage);
        }

        return text.ToString();
    }

    /// <summary>
    /// The text of every entry the filters let through, whatever is selected, for "Copy all".
    /// </summary>
    /// <returns>The text, one entry per line.</returns>
    public string GetAllShownText()
    {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < LogEntries.Count; i++)
        {
            text.AppendLine(LogEntries[i].FullMessage);
        }

        return text.ToString();
    }

    [RelayCommand]
    private void SaveLogsToFile()
    {
        try
        {
            Logger.LogInformation("Export button clicked - starting export process");

            // Get all logs from provider
            var entries = _loggingProvider.GetAllLogs();
            Logger.LogInformation("Retrieved {Count} log entries", entries.Count);

            // Apply current filters to exported entries
            var filteredEntries = new List<LogEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (ShouldShowLog(entry))
                {
                    filteredEntries.Add(entry);
                }
            }
            Logger.LogInformation("Filtered to {Count} entries", filteredEntries.Count);

            // Generate timestamped filename
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"X25Emulator_logs_{timestamp}.txt";
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, filename);
            Logger.LogInformation("Export file path: {FilePath}", filePath);

            // Build export content
            var sb = new StringBuilder();

            // Add header information
            sb.AppendLine("X.25 Emulator Log Export");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Filters Applied: Debug={ShowDebug}, Info={ShowInfo}, Warning={ShowWarning}, Error={ShowError}");
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                sb.AppendLine($"Text Filter: {FilterText}");
            }
            sb.AppendLine($"Total Entries: {filteredEntries.Count}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            // Add log entries
            for (int i = 0; i < filteredEntries.Count; i++)
            {
                sb.AppendLine(filteredEntries[i].FullMessage);
            }

            // Write to file
            Logger.LogInformation("Writing export content to file");
            File.WriteAllText(filePath, sb.ToString());
            Logger.LogInformation("File written successfully");

            // Show confirmation message
            _dialogService.ShowInfo("Export Successful",
                $"Log successfully exported!\n\nEntries exported: {filteredEntries.Count}\nFile saved to:\n{filePath}");

            Logger.LogInformation("Log export completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting log: {Message}", ex.Message);
            _dialogService.ShowError("Export Failed", $"Failed to export log:\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        _loggingProvider.LogEntryAdded -= OnLogEntryAdded;
        Logger.LogInformation("LogWindowViewModel disposed");
        base.Dispose();
    }
}
