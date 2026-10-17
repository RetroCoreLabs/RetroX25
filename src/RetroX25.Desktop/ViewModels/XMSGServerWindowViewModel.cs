//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Sintran;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for XMSG Server Window (F12) - monitors LocalXMSG protocol sessions for COSMOS file services.
/// </summary>
public partial class XMSGServerWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;
    private readonly LocalXMSGProtocol _localXmsgProtocol;
    private System.Threading.Timer? _refreshTimer;

    [ObservableProperty]
    private ObservableCollection<XMSGSessionInfo> _sessions = new();

    [ObservableProperty]
    private XMSGSessionInfo? _selectedSession;

    [ObservableProperty]
    private string _sessionDetails = "";

    [ObservableProperty]
    private string _statusText = "Status: Always Running";

    [ObservableProperty]
    private string _connectionStatsText = "Active Sessions: 0";

    [ObservableProperty]
    private string _serviceStatsText = "Services: File(1000), RPC(1001), Monitor(1002)";

    [ObservableProperty]
    private bool _canDisconnect = false;

    /// <summary>
    /// Creates XMSGServerWindowViewModel with dependency injection.
    /// </summary>
    public XMSGServerWindowViewModel(
        ILogger<XMSGServerWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localXmsgProtocol = _runtimeService.Runtime.GetLocalXMSGProtocol();

        // Initial refresh
        RefreshSessions();

        // Start refresh timer (update every 2 seconds)
        _refreshTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshSessions();
            });
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Logger.LogInformation("XMSGServerWindowViewModel initialized");
    }

    /// <summary>
    /// Refresh session list from LocalXMSGProtocol.
    /// </summary>
    private void RefreshSessions()
    {
        try
        {
            // Store selected LCN to preserve selection
            ushort? selectedLCN = SelectedSession?.LCN;

            // Get active sessions
            var activeSessions = _localXmsgProtocol.GetSessionInfo();
            Sessions.Clear();

            foreach (var session in activeSessions)
            {
                Sessions.Add(session);
            }

            // Update stats
            UpdateConnectionStats();

            // Restore selection if it still exists
            if (selectedLCN.HasValue)
            {
                foreach (var session in Sessions)
                {
                    if (session.LCN == selectedLCN.Value)
                    {
                        SelectedSession = session;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing XMSG sessions");
        }
    }

    /// <summary>
    /// Update connection statistics.
    /// </summary>
    private void UpdateConnectionStats()
    {
        var activeCount = _localXmsgProtocol.ActiveSessionsCount;
        ConnectionStatsText = $"Active Sessions: {activeCount}";

        // Update service statistics
        var fileSessions = 0;
        var rpcSessions = 0;
        var monitorSessions = 0;
        var otherSessions = 0;

        foreach (var session in Sessions)
        {
            switch (session.Port)
            {
                case 0x1000:
                    fileSessions++;
                    break;
                case 0x1001:
                    rpcSessions++;
                    break;
                case 0x1002:
                    monitorSessions++;
                    break;
                default:
                    otherSessions++;
                    break;
            }
        }

        ServiceStatsText = $"File({fileSessions}) RPC({rpcSessions}) Monitor({monitorSessions}) Other({otherSessions})";
    }

    /// <summary>
    /// Handle session selection change.
    /// </summary>
    partial void OnSelectedSessionChanged(XMSGSessionInfo? value)
    {
        if (value != null)
        {
            ShowSessionDetails(value);
            CanDisconnect = true;
        }
        else
        {
            SessionDetails = GetDefaultSessionInfo();
            CanDisconnect = false;
        }
    }

    /// <summary>
    /// Show detailed information for selected session.
    /// </summary>
    private void ShowSessionDetails(XMSGSessionInfo session)
    {
        SessionDetails = $"XMSG Session Details\n" +
                        $"====================\n\n" +
                        $"Logical Channel: {session.LCN}\n" +
                        $"Service Port: 0x{session.Port:X4} ({session.Port})\n" +
                        $"Service Type: {session.GetPortDescription()}\n" +
                        $"Service Name: {session.ServiceName}\n" +
                        $"Session ID: 0x{session.SessionId:X8}\n" +
                        $"State: {session.State}\n" +
                        $"Called Address: {session.CalledAddress}\n" +
                        $"Calling Address: {session.CallingAddress}\n" +
                        $"Start Time: {session.StartTime:yyyy-MM-dd HH:mm:ss} UTC\n" +
                        $"Duration: {session.Duration:hh\\:mm\\:ss}\n\n" +
                        $"Protocol Information:\n" +
                        $"- XMSG Version: L03 (0x4C03)\n" +
                        $"- Message Format: 12-byte header + data\n" +
                        $"- Service Registry: Built-in COSMOS services\n\n" +
                        $"Available Operations:\n" +
                        $"- File Service: Read, Write, Copy operations\n" +
                        $"- RPC Service: Remote procedure calls\n" +
                        $"- System Monitor: Status and statistics\n" +
                        $"- Session Management: Connect, Disconnect, Status\n\n" +
                        $"Last Updated: {DateTime.UtcNow:HH:mm:ss} UTC";
    }

    /// <summary>
    /// Get default session information when no session is selected.
    /// </summary>
    private string GetDefaultSessionInfo()
    {
        return "No session selected.\n\n" +
               "XMSG Services Available:\n" +
               "=======================\n\n" +
               "0x1000 - COSMOS File Service\n" +
               "  File operations: read, write, copy\n" +
               "  SINTRAN file specification support\n" +
               "  Block-based transfer with checksums\n\n" +
               "0x1001 - Remote Procedure Service\n" +
               "  Remote procedure call execution\n" +
               "  Parameter passing and return values\n" +
               "  Error handling and exceptions\n\n" +
               "0x1002 - System Monitor\n" +
               "  System status and statistics\n" +
               "  Performance monitoring\n" +
               "  Resource utilization\n\n" +
               "0x1003-0x10FF - User Services\n" +
               "0x1100-0x1FFF - Reserved Services";
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshSessions();
        Logger.LogInformation("XMSG sessions refreshed manually");
    }

    [RelayCommand]
    private async Task DisconnectSession()
    {
        if (SelectedSession != null)
        {
            var confirmed = await _dialogService.ShowConfirmation(
                "Disconnect Session",
                $"Disconnect XMSG session on LCN {SelectedSession.LCN} (Port {SelectedSession.Port:X4})?"
            );

            if (confirmed)
            {
                try
                {
                    _localXmsgProtocol.HandleCallClearing(SelectedSession.LCN);
                    Logger.LogInformation("XMSG session on LCN {LCN} disconnected by user", SelectedSession.LCN);
                    RefreshSessions();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error disconnecting XMSG session on LCN {LCN}", SelectedSession.LCN);
                    _dialogService.ShowError("Error", $"Failed to disconnect session: {ex.Message}");
                }
            }
        }
    }

    [RelayCommand]
    private async Task ClearAllSessions()
    {
        if (Sessions.Count == 0)
        {
            _dialogService.ShowInfo("No Sessions", "There are no active sessions to clear.");
            return;
        }

        var confirmed = await _dialogService.ShowConfirmation(
            "Clear All Sessions",
            $"Disconnect all {Sessions.Count} active XMSG sessions?"
        );

        if (confirmed)
        {
            try
            {
                var sessionCount = Sessions.Count;
                var sessionList = new List<XMSGSessionInfo>(Sessions);

                for (int i = 0; i < sessionList.Count; i++)
                {
                    _localXmsgProtocol.HandleCallClearing(sessionList[i].LCN);
                }

                Logger.LogInformation("All {Count} XMSG sessions cleared by user", sessionCount);
                RefreshSessions();
                _dialogService.ShowInfo("Success", $"Cleared {sessionCount} XMSG sessions.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error clearing XMSG sessions");
                _dialogService.ShowError("Error", $"Failed to clear sessions: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void ViewServices()
    {
        var serviceInfo = "XMSG Service Registry\n" +
                        "===================\n\n" +
                        "Port 0x1000 (4096) - COSMOS File Service\n" +
                        "  Description: File transfer operations\n" +
                        "  Operations: Read, Write, Copy\n" +
                        "  Format: SINTRAN file specification\n" +
                        "  Example: MACHINE(USER(PASSWORD)).(DIR:USER)FILE:TYPE;VER\n\n" +
                        "Port 0x1001 (4097) - Remote Procedure Service\n" +
                        "  Description: Remote procedure calls\n" +
                        "  Operations: Call, Return, Exception\n" +
                        "  Parameters: Serialized data structures\n\n" +
                        "Port 0x1002 (4098) - System Monitor\n" +
                        "  Description: System status and monitoring\n" +
                        "  Operations: Status, Statistics, Performance\n" +
                        "  Data: System metrics and health information\n\n" +
                        "Port Range 0x1003-0x10FF - User Services\n" +
                        "  Description: Application-specific services\n" +
                        "  Registration: Dynamic service registration\n\n" +
                        "Port Range 0x1100-0x1FFF - Reserved\n" +
                        "  Description: Future XMSG service expansion\n\n" +
                        $"Currently Active Sessions: {Sessions.Count}\n" +
                        $"Server Uptime: Running since startup\n" +
                        $"Protocol Version: XMSG L03 (0x4C03)";

        _dialogService.ShowInfo("XMSG Services", serviceInfo);
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        Logger.LogInformation("XMSGServerWindowViewModel disposed");
        base.Dispose();
    }
}
