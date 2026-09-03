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
/// ViewModel for the Log Window (F7) - displays application logs with filtering.
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
    /// Handle new log entries.
    /// </summary>
    private void OnLogEntryAdded(LogEntry entry)
    {
        // Add to collection if it matches filters
        if (ShouldShowLog(entry))
        {
            // UI updates must happen on UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Add(entry);
                UpdateStatus();
            });
        }
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
        LogEntries.Clear();

        var logs = _loggingProvider.GetAllLogs();
        foreach (var log in logs)
        {
            if (ShouldShowLog(log))
            {
                LogEntries.Add(log);
            }
        }

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

    [RelayCommand]
    private void CopySelectedLog()
    {
        if (SelectedLog != null)
        {
            var text = SelectedLog.FullMessage;
            // TODO: Copy to clipboard when Avalonia clipboard API is available
            Logger.LogInformation("Copied log entry to clipboard");
        }
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
