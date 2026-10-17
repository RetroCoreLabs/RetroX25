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
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for Status Window (F1) - displays detailed runtime statistics.
/// </summary>
public partial class StatusWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private System.Threading.Timer? _refreshTimer;

    // HDLC Statistics
    [ObservableProperty]
    private string _hdlcFramesReceived = "0";

    [ObservableProperty]
    private string _hdlcFramesTransmitted = "0";

    [ObservableProperty]
    private string _hdlcFramesInvalidFcs = "0";

    [ObservableProperty]
    private string _hdlcBytesReceived = "0";

    [ObservableProperty]
    private string _hdlcBytesTransmitted = "0";

    // LAPB Statistics
    [ObservableProperty]
    private string _lapbCurrentState = "Disconnected";

    [ObservableProperty]
    private string _lapbIFramesSent = "0";

    [ObservableProperty]
    private string _lapbIFramesReceived = "0";

    [ObservableProperty]
    private string _lapbSFramesSent = "0";

    [ObservableProperty]
    private string _lapbSFramesReceived = "0";

    [ObservableProperty]
    private string _lapbUFramesSent = "0";

    [ObservableProperty]
    private string _lapbUFramesReceived = "0";

    [ObservableProperty]
    private string _lapbRetransmissions = "0";

    [ObservableProperty]
    private string _lapbTimeouts = "0";

    // X.25 Statistics
    [ObservableProperty]
    private string _x25ActiveVirtualCircuits = "0";

    [ObservableProperty]
    private string _x25TotalVirtualCircuits = "0";

    [ObservableProperty]
    private string _x25CallRequestsReceived = "0";

    [ObservableProperty]
    private string _x25CallRequestsTransmitted = "0";

    [ObservableProperty]
    private string _x25DataPacketsTransmitted = "0";

    [ObservableProperty]
    private string _x25DataPacketsReceived = "0";

    [ObservableProperty]
    private string _x25PacketsReceived = "0";

    [ObservableProperty]
    private string _x25PacketsTransmitted = "0";

    /// <summary>
    /// Creates StatusWindowViewModel with dependency injection.
    /// </summary>
    public StatusWindowViewModel(
        ILogger<StatusWindowViewModel> logger,
        IRuntimeService runtimeService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));

        // Subscribe to statistics updates
        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.StatisticsUpdated += OnStatisticsUpdated;
            UpdateStatistics(_runtimeService.Runtime.GetStatistics());
        }

        // Start refresh timer (update every 1 second)
        _refreshTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_runtimeService.IsRunning)
                {
                    UpdateStatistics(_runtimeService.Runtime.GetStatistics());
                }
            });
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Logger.LogInformation("StatusWindowViewModel initialized");
    }

    /// <summary>
    /// Handle statistics updates from ProtocolRuntime.
    /// </summary>
    private void OnStatisticsUpdated(object? sender, StatisticsSnapshot stats)
    {
        UpdateStatistics(stats);
    }

    /// <summary>
    /// Update all statistics displays.
    /// </summary>
    private void UpdateStatistics(StatisticsSnapshot stats)
    {
        // HDLC Statistics
        HdlcFramesReceived = stats.HDLC.FramesReceived.ToString();
        HdlcFramesTransmitted = stats.HDLC.FramesTransmitted.ToString();
        HdlcFramesInvalidFcs = stats.HDLC.FramesInvalidFcs.ToString();
        HdlcBytesReceived = stats.HDLC.BytesReceived.ToString();
        HdlcBytesTransmitted = stats.HDLC.BytesTransmitted.ToString();

        // LAPB Statistics
        LapbCurrentState = stats.LAPB.CurrentState.ToString();
        LapbIFramesSent = stats.LAPB.IFramesTransmitted.ToString();
        LapbIFramesReceived = stats.LAPB.IFramesReceived.ToString();
        LapbSFramesSent = stats.LAPB.SFramesTransmitted.ToString();
        LapbSFramesReceived = stats.LAPB.SFramesReceived.ToString();
        LapbUFramesSent = stats.LAPB.UFramesTransmitted.ToString();
        LapbUFramesReceived = stats.LAPB.UFramesReceived.ToString();
        LapbRetransmissions = stats.LAPB.Retransmissions.ToString();
        LapbTimeouts = stats.LAPB.Timeouts.ToString();

        // X.25 Statistics
        X25ActiveVirtualCircuits = stats.X25.ActiveVirtualCircuits.ToString();
        X25TotalVirtualCircuits = stats.X25.TotalVirtualCircuits.ToString();
        X25CallRequestsReceived = stats.X25.CallRequestsReceived.ToString();
        X25CallRequestsTransmitted = stats.X25.CallRequestsTransmitted.ToString();
        X25DataPacketsTransmitted = stats.X25.DataPacketsTransmitted.ToString();
        X25DataPacketsReceived = stats.X25.DataPacketsReceived.ToString();
        X25PacketsReceived = stats.X25.PacketsReceived.ToString();
        X25PacketsTransmitted = stats.X25.PacketsTransmitted.ToString();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_runtimeService.IsRunning)
        {
            UpdateStatistics(_runtimeService.Runtime.GetStatistics());
        }
        Logger.LogInformation("Statistics refreshed manually");
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
