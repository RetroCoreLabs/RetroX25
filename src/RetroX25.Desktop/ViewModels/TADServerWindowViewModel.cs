using System;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Sintran;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for TAD Server Window (F11) - monitors LocalTAD protocol sessions.
/// </summary>
public partial class TADServerWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly LocalTadProtocol _localTadProtocol;
    private System.Threading.Timer? _refreshTimer;

    [ObservableProperty]
    private ObservableCollection<LocalTadSessionInfo> _sessions = new();

    [ObservableProperty]
    private LocalTadSessionInfo? _selectedSession;

    [ObservableProperty]
    private string _sessionDetails = "";

    [ObservableProperty]
    private string _x3Parameters = "";

    [ObservableProperty]
    private string _statusText = "Status: Always Running";

    [ObservableProperty]
    private string _connectionStatsText = "Active Sessions: 0";

    [ObservableProperty]
    private bool _canDisconnect = false;

    /// <summary>
    /// Creates TADServerWindowViewModel with dependency injection.
    /// </summary>
    public TADServerWindowViewModel(
        ILogger<TADServerWindowViewModel> logger,
        IRuntimeService runtimeService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _localTadProtocol = _runtimeService.Runtime.GetLocalTadProtocol();

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

        Logger.LogInformation("TADServerWindowViewModel initialized");
    }

    /// <summary>
    /// Refresh session list from LocalTadProtocol.
    /// </summary>
    private void RefreshSessions()
    {
        try
        {
            // Store selected LCN to preserve selection
            ushort? selectedLCN = SelectedSession?.LCN;

            // Get active sessions
            var activeSessions = _localTadProtocol.GetActiveSessions();
            Sessions.Clear();

            foreach (var session in activeSessions)
            {
                // Update duration
                session.Duration = DateTime.UtcNow - session.StartTime;
                Sessions.Add(session);
            }

            // Update stats
            ConnectionStatsText = $"Active Sessions: {Sessions.Count}";

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
            Logger.LogError(ex, "Error refreshing sessions");
        }
    }

    /// <summary>
    /// Handle session selection change.
    /// </summary>
    partial void OnSelectedSessionChanged(LocalTadSessionInfo? value)
    {
        if (value != null)
        {
            ShowSessionDetails(value);
            ShowX3Parameters(value);
            CanDisconnect = true;
        }
        else
        {
            SessionDetails = "";
            X3Parameters = "";
            CanDisconnect = false;
        }
    }

    /// <summary>
    /// Show detailed information for selected session.
    /// </summary>
    private void ShowSessionDetails(LocalTadSessionInfo session)
    {
        SessionDetails = $"LocalTAD Session Details\n" +
                        $"========================\n\n" +
                        $"Logical Channel: {session.LCN}\n" +
                        $"Called Address: {session.CalledAddress}\n" +
                        $"Calling Address: {session.CallingAddress}\n\n" +
                        $"Session Info:\n" +
                        $"Started: {session.StartTime:yyyy-MM-dd HH:mm:ss}\n" +
                        $"Duration: {session.Duration:hh\\:mm\\:ss}\n\n" +
                        $"Service Type:\n" +
                        $"Loopback Echo Server\n" +
                        $"Commands: HELP, STATUS, QUIT\n" +
                        $"All input text is echoed back";
    }

    /// <summary>
    /// Show X.3 parameters for selected session.
    /// </summary>
    private void ShowX3Parameters(LocalTadSessionInfo session)
    {
        try
        {
            var parameters = _localTadProtocol.GetSessionX3Parameters(session.SessionId);

            if (parameters != null && parameters.Count > 0)
            {
                var details = "X.3 Parameter Settings\n" +
                             "======================\n\n";

                // Show key parameters
                var keyParams = new byte[] { 1, 2, 3, 4, 12, 15, 16, 17 };

                foreach (var paramNum in keyParams)
                {
                    if (parameters.TryGetValue(paramNum, out var value))
                    {
                        var desc = GetParameterDescription(paramNum);
                        details += $"P{paramNum:D2}: {value,3} - {desc}\n";
                    }
                }

                details += "\nAll Parameters:\n";
                for (byte p = 1; p <= 22; p++)
                {
                    if (parameters.TryGetValue(p, out var value))
                    {
                        details += $"P{p:D2}:{value,3} ";
                        if ((p % 8) == 0) details += "\n";
                    }
                }

                X3Parameters = details;
            }
            else
            {
                X3Parameters = "X.3 parameters not available\nfor this session.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving X.3 parameters for session {SessionId}", session.SessionId);
            X3Parameters = $"Error loading X.3 parameters:\n{ex.Message}";
        }
    }

    /// <summary>
    /// Get parameter description.
    /// </summary>
    private string GetParameterDescription(byte paramNum)
    {
        return paramNum switch
        {
            1 => "ESCAPE",
            2 => "ECHO",
            3 => "DATA_FORWARDING",
            4 => "IDLE_TIMER",
            12 => "FORWARD_ON_N_CHARACTERS",
            15 => "EDITING",
            16 => "CHARACTER_DELETE",
            17 => "LINE_DELETE",
            _ => "Parameter"
        };
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshSessions();
        Logger.LogInformation("Sessions refreshed manually");
    }

    [RelayCommand]
    private void DisconnectSession()
    {
        if (SelectedSession != null)
        {
            try
            {
                var success = _localTadProtocol.DisconnectSession(SelectedSession.LCN);
                if (success)
                {
                    Logger.LogInformation("Disconnected LocalTAD session LCN {LCN}", SelectedSession.LCN);
                    RefreshSessions();
                }
                else
                {
                    Logger.LogWarning("Failed to disconnect LocalTAD session LCN {LCN} - session not found", SelectedSession.LCN);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error disconnecting LocalTAD session LCN {LCN}", SelectedSession.LCN);
            }
        }
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        Logger.LogInformation("TADServerWindowViewModel disposed");
        base.Dispose();
    }
}
