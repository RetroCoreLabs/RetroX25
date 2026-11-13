//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Models;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Status window (Ctrl+1): the one place in the application that shows counters.
/// </summary>
/// <remarks>
/// <para>
/// The window shows one table. The rows are counters (HDLC, LAPB, X.25, and the per-link figures);
/// the columns are the engine's total and then each link and each transport interface. The table is
/// built by <see cref="StatisticsReader"/>, so the figures are the same wherever they are read.
/// </para>
/// <para>
/// "Reset Statistics" calls the engine's own reset, which zeroes the engine's totals and the counters of
/// every link and interface. Nothing is hidden or subtracted here.
/// </para>
/// </remarks>
public partial class StatusWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private System.Threading.Timer? _refreshTimer;
    private int _refreshQueued;

    /// <summary>Gets or sets the table that is shown now.</summary>
    [ObservableProperty]
    private StatisticsTable _table = new StatisticsTable();

    /// <summary>Gets or sets the line above the table, with the uptime.</summary>
    [ObservableProperty]
    private string _headerText = "Counters since the engine started or was last reset";

    /// <summary>Gets or sets the text of the last action (for example when the counters were reset).</summary>
    [ObservableProperty]
    private string _actionText = string.Empty;

    /// <summary>
    /// Creates StatusWindowViewModel with dependency injection.
    /// </summary>
    /// <param name="logger">The logger for this view-model.</param>
    /// <param name="runtimeService">The service that owns the engine.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runtimeService"/> is null.</exception>
    public StatusWindowViewModel(
        ILogger<StatusWindowViewModel> logger,
        IRuntimeService runtimeService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));

        // Subscribe to statistics updates
        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.StatisticsUpdated += OnStatisticsUpdated;
            Refresh();
        }

        // Start refresh timer (update every 1 second)
        _refreshTimer = new System.Threading.Timer(_ => QueueRefresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Logger.LogInformation("StatusWindowViewModel initialized");
    }

    /// <summary>
    /// Handle statistics updates from ProtocolRuntime. The event comes on an engine thread, so the
    /// table is rebuilt later on the UI thread.
    /// </summary>
    private void OnStatisticsUpdated(object? sender, StatisticsSnapshot stats)
    {
        QueueRefresh();
    }

    // Asks the UI thread to rebuild the table, unless a rebuild is already waiting.
    private void QueueRefresh()
    {
        if (System.Threading.Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            System.Threading.Interlocked.Exchange(ref _refreshQueued, 0);
            if (_runtimeService.IsRunning)
            {
                Refresh();
            }
        });
    }

    /// <summary>
    /// Reads the engine again and builds a new table.
    /// </summary>
    [RelayCommand]
    public void Refresh()
    {
        if (!_runtimeService.IsRunning)
        {
            return;
        }

        StatisticsTable fresh = StatisticsReader.Build(_runtimeService.Runtime);
        HeaderText = "Counters since the engine started or was last reset. Uptime: " + fresh.UptimeText;
        Table = fresh;
    }

    /// <summary>
    /// Sets every counter of the engine back to zero (the engine's totals and the counters of each link
    /// and interface), then shows the zeros at once.
    /// </summary>
    [RelayCommand]
    public void ResetStatistics()
    {
        if (!_runtimeService.IsRunning)
        {
            return;
        }

        _runtimeService.Runtime.ResetStatistics();
        ActionText = "Counters reset at " + DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        Logger.LogInformation("Statistics reset from the Status window");
        Refresh();
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.StatisticsUpdated -= OnStatisticsUpdated;
        }

        Logger.LogInformation("StatusWindowViewModel disposed");
        base.Dispose();
    }
}
