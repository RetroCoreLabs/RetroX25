using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for SINTRAN TAD Server Window - monitors SINTRAN TAD sessions.
/// </summary>
public partial class SintranTadServerWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;
    private System.Threading.Timer? _refreshTimer;

    [ObservableProperty]
    private ObservableCollection<SintranTadSessionDisplay> _sessions = new();

    [ObservableProperty]
    private SintranTadSessionDisplay? _selectedSession;

    [ObservableProperty]
    private string _sessionDetails = "";

    [ObservableProperty]
    private string _serverStatus = "Status: Checking...";

    [ObservableProperty]
    private string _terminalStats = "Terminals: Checking...";

    [ObservableProperty]
    private bool _canDisconnect = false;

    public SintranTadServerWindowViewModel(
        ILogger<SintranTadServerWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        RefreshSessions();

        // Start refresh timer (update every 2 seconds)
        _refreshTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshSessions();
            });
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Logger.LogInformation("SintranTadServerWindowViewModel initialized");
    }

    private void RefreshSessions()
    {
        try
        {
            // Store selected LCN
            ushort? selectedLCN = SelectedSession?.LCN;

            Sessions.Clear();

            // Get PVC server
            var pvcServer = _runtimeService.Runtime.GetSintranPvcServer();
            var handlers = pvcServer.GetActiveHandlers();

            // Get active TAD sessions
            foreach (var kvp in handlers)
            {
                if (kvp.Value.Protocol == "TAD")
                {
                    var stats = kvp.Value.GetStats();
                    var terminalId = pvcServer.GetTerminalId(kvp.Key);

                    Sessions.Add(new SintranTadSessionDisplay
                    {
                        LCN = kvp.Key,
                        TerminalId = terminalId,
                        Protocol = stats.Protocol,
                        Status = stats.Status,
                        MessagesRx = stats.MessagesReceived,
                        MessagesTx = stats.MessagesSent,
                        BytesRx = stats.BytesReceived,
                        BytesTx = stats.BytesSent,
                        LastActivity = stats.LastActivity,
                        IsActive = stats.IsActive
                    });
                }
            }

            // Update stats
            var serverStats = pvcServer.GetStats();
            var available = serverStats.AvailableTerminals;
            var total = serverStats.TotalTerminals;
            var used = total - available;

            TerminalStats = $"Terminals: {used}/{total} In Use, {available} Available";
            ServerStatus = $"Status: Running ({serverStats.TotalHandlers} active handlers)";

            // Restore selection
            if (selectedLCN.HasValue)
            {
                for (int i = 0; i < Sessions.Count; i++)
                {
                    if (Sessions[i].LCN == selectedLCN.Value)
                    {
                        SelectedSession = Sessions[i];
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing SINTRAN TAD sessions");
            ServerStatus = "Status: Error loading sessions";
        }
    }

    partial void OnSelectedSessionChanged(SintranTadSessionDisplay? value)
    {
        if (value != null)
        {
            SessionDetails = $"SINTRAN TAD Session\n" +
                           $"===================\n\n" +
                           $"Terminal ID: {value.TerminalId}\n" +
                           $"Logical Channel: {value.LCN}\n" +
                           $"Protocol: {value.Protocol}\n\n" +
                           $"Session Status:\n" +
                           $"{value.Status}\n\n" +
                           $"Statistics:\n" +
                           $"Messages RX: {value.MessagesRx}\n" +
                           $"Messages TX: {value.MessagesTx}\n" +
                           $"Bytes RX: {value.BytesRx:N0}\n" +
                           $"Bytes TX: {value.BytesTx:N0}\n\n" +
                           $"Last Activity:\n" +
                           $"{value.LastActivity:yyyy-MM-dd HH:mm:ss}\n\n" +
                           $"SINTRAN Routing:\n" +
                           $"Frame format: 0x21 0x13 0xDD\n" +
                           $"TAD protocol with auto terminal ID";

            CanDisconnect = true;
        }
        else
        {
            SessionDetails = "No session selected.";
            CanDisconnect = false;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshSessions();
        Logger.LogInformation("SINTRAN TAD sessions refreshed manually");
    }

    [RelayCommand]
    private void DisconnectSession()
    {
        if (SelectedSession != null)
        {
            try
            {
                Logger.LogInformation("Disconnect SINTRAN TAD session LCN {LCN}, Terminal ID {TerminalId}",
                    SelectedSession.LCN, SelectedSession.TerminalId);
                _dialogService.ShowInfo("Disconnect", "SINTRAN TAD session disconnect not yet implemented.");
                RefreshSessions();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error disconnecting SINTRAN TAD session");
                _dialogService.ShowError("Error", $"Failed to disconnect: {ex.Message}");
            }
        }
    }

    public override void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        Logger.LogInformation("SintranTadServerWindowViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// Display data for SINTRAN TAD session.
/// </summary>
public class SintranTadSessionDisplay
{
    public ushort LCN { get; set; }
    public int TerminalId { get; set; }
    public string Protocol { get; set; } = "";
    public string Status { get; set; } = "";
    public int MessagesRx { get; set; }
    public int MessagesTx { get; set; }
    public long BytesRx { get; set; }
    public long BytesTx { get; set; }
    public DateTime LastActivity { get; set; }
    public bool IsActive { get; set; }
}
