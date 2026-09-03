using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;
using RetroIO.Protocols.X25.Runtime.Transport;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// Main window ViewModel managing application state and protocol runtime.
/// </summary>
public partial class MainWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private int _padWindowCounter = 0;
    private int _sintranTadClientWindowCounter = 0;

    // Observable properties for status display
    [ObservableProperty]
    private string _comPortStatus = "COM: Disconnected";

    [ObservableProperty]
    private string _hdlcStatus = "HDLC: 0 OK / 0 Errors";

    [ObservableProperty]
    private string _lapbStatus = "LAPB: Disconnected";

    [ObservableProperty]
    private string _x25Status = "X.25: 0 VCs";

    [ObservableProperty]
    private string _windowTitle = "X.25 Protocol Emulator";

    [ObservableProperty]
    private bool _isRuntimeInitialized;

    // Detailed HDLC Statistics for main window display
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

    // Detailed LAPB Statistics for main window display
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

    // Detailed X.25 Statistics for main window display
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

    // Host Information
    [ObservableProperty]
    private string _localMachineId = "200";

    [ObservableProperty]
    private string _localDteAddress = "(none)";

    [ObservableProperty]
    private string _defaultGateway = "(none)";

    // Interface collections
    [ObservableProperty]
    private ObservableCollection<InterfaceStatusViewModel> _interfaces = new();

    // Aggregate Statistics (for status bar)
    [ObservableProperty]
    private string _aggregateRxBytes = "0 B";

    [ObservableProperty]
    private string _aggregateRxFrames = "0";

    [ObservableProperty]
    private string _aggregateTxBytes = "0 B";

    [ObservableProperty]
    private string _aggregateTxFrames = "0";

    [ObservableProperty]
    private string _aggregateFcsErrors = "0";

    [ObservableProperty]
    private string _aggregateLapbErrors = "0";

    [ObservableProperty]
    private string _runtimeUptime = "0s";

    // Frame Counting Hierarchy (HDLC split)
    [ObservableProperty]
    private string _hdlcTotalRxFrames = "0";

    [ObservableProperty]
    private string _hdlcLapbRxFrames = "0";

    [ObservableProperty]
    private string _hdlcX25RxFrames = "0";

    [ObservableProperty]
    private string _hdlcOtherRxFrames = "0";

    [ObservableProperty]
    private string _hdlcTotalTxFrames = "0";

    [ObservableProperty]
    private string _hdlcLapbTxFrames = "0";

    [ObservableProperty]
    private string _hdlcX25TxFrames = "0";

    [ObservableProperty]
    private string _hdlcOtherTxFrames = "0";

    // Routing Statistics
    [ObservableProperty]
    private string _activeRoutes = "0";

    [ObservableProperty]
    private string _totalRoutes = "0";

    [ObservableProperty]
    private string _routingSummary = "No routes configured";

    /// <summary>
    /// Creates MainWindowViewModel with dependency injection.
    /// </summary>
    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService,
        INavigationService navigationService,
        IServiceProvider serviceProvider) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        IsRuntimeInitialized = _runtimeService.IsRunning;

        if (IsRuntimeInitialized)
        {
            SubscribeToRuntimeEvents();
            PopulateInterfaces();
            UpdateHostInformation();
            UpdateRoutingStatistics();
            UpdateStatus();
        }

        Logger.LogInformation("MainWindowViewModel initialized");
    }

    /// <summary>
    /// Subscribe to ProtocolRuntime events for status updates.
    /// </summary>
    private void SubscribeToRuntimeEvents()
    {
        var runtime = _runtimeService.Runtime;

        // Subscribe to statistics updates
        runtime.StatisticsUpdated += OnStatisticsUpdated;
        runtime.StateChanged += OnStateChanged;

        Logger.LogDebug("Subscribed to ProtocolRuntime events");
    }

    /// <summary>
    /// Handle statistics updates from ProtocolRuntime.
    /// </summary>
    private void OnStatisticsUpdated(object? sender, StatisticsSnapshot stats)
    {
        UpdateStatus(stats);
    }

    /// <summary>
    /// Handle state changes from ProtocolRuntime.
    /// </summary>
    private void OnStateChanged(object? sender, RuntimeStateChangedEventArgs e)
    {
        Logger.LogInformation("Protocol state changed: {State}", e.CurrentState);
    }

    /// <summary>
    /// Update all status displays from ProtocolRuntime statistics.
    /// </summary>
    private void UpdateStatus(StatisticsSnapshot? stats = null)
    {
        if (!IsRuntimeInitialized)
            return;

        // If stats not provided, don't update (wait for next StatisticsUpdated event)
        if (stats == null)
            return;

        // Update aggregate statistics (new status bar)
        UpdateAggregateStatistics(stats);

        // Update old status bar (will be replaced by aggregate)
        ComPortStatus = "COM: Runtime Active";
        HdlcStatus = $"HDLC: {stats.HDLC.FramesReceived} OK / {stats.HDLC.FramesInvalidFcs} Errors";
        LapbStatus = $"LAPB: {stats.LAPB.CurrentState}";
        X25Status = $"X.25: {stats.X25.ActiveVirtualCircuits} VCs";

        // Update detailed HDLC statistics (kept for compatibility)
        HdlcFramesReceived = stats.HDLC.FramesReceived.ToString();
        HdlcFramesTransmitted = stats.HDLC.FramesTransmitted.ToString();
        HdlcFramesInvalidFcs = stats.HDLC.FramesInvalidFcs.ToString();
        HdlcBytesReceived = stats.HDLC.BytesReceived.ToString();
        HdlcBytesTransmitted = stats.HDLC.BytesTransmitted.ToString();

        // Update detailed LAPB statistics (kept for compatibility)
        LapbCurrentState = stats.LAPB.CurrentState.ToString();
        LapbIFramesSent = stats.LAPB.IFramesTransmitted.ToString();
        LapbIFramesReceived = stats.LAPB.IFramesReceived.ToString();
        LapbSFramesSent = stats.LAPB.SFramesTransmitted.ToString();
        LapbSFramesReceived = stats.LAPB.SFramesReceived.ToString();
        LapbUFramesSent = stats.LAPB.UFramesTransmitted.ToString();
        LapbUFramesReceived = stats.LAPB.UFramesReceived.ToString();
        LapbRetransmissions = stats.LAPB.Retransmissions.ToString();
        LapbTimeouts = stats.LAPB.Timeouts.ToString();

        // Update detailed X.25 statistics (kept for compatibility)
        X25ActiveVirtualCircuits = stats.X25.ActiveVirtualCircuits.ToString();
        X25TotalVirtualCircuits = stats.X25.TotalVirtualCircuits.ToString();
        X25CallRequestsReceived = stats.X25.CallRequestsReceived.ToString();
        X25CallRequestsTransmitted = stats.X25.CallRequestsTransmitted.ToString();
        X25DataPacketsTransmitted = stats.X25.DataPacketsTransmitted.ToString();
        X25DataPacketsReceived = stats.X25.DataPacketsReceived.ToString();
        X25PacketsReceived = stats.X25.PacketsReceived.ToString();
        X25PacketsTransmitted = stats.X25.PacketsTransmitted.ToString();

        // Update interface statistics
        UpdateInterfaceStatistics(stats);
    }

    /// <summary>
    /// Update individual interface statistics from transport layer
    /// </summary>
    private void UpdateInterfaceStatistics(StatisticsSnapshot stats)
    {
        // Update each interface with its transport statistics
        for (int i = 0; i < Interfaces.Count; i++)
        {
            var interfaceVm = Interfaces[i];
            if (interfaceVm.Transport != null)
            {
                var transportStats = interfaceVm.Transport.GetStatistics();
                interfaceVm.RxBytes = transportStats.BytesReceived;
                interfaceVm.TxBytes = transportStats.BytesTransmitted;
                interfaceVm.Status = transportStats.CurrentStatus.ToString();

                // For serial interfaces, update LAPB state
                if (interfaceVm.Type == "Serial")
                {
                    interfaceVm.LapbState = stats.LAPB.CurrentState;
                }
            }
        }
    }

    // Commands for menu actions

    [RelayCommand]
    private void ShowPacketWindow()
    {
        Logger.LogInformation("Opening Packet Window");
        var viewModel = _serviceProvider.GetRequiredService<PacketWindowViewModel>();
        var window = new PacketWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowLogWindow()
    {
        Logger.LogInformation("Opening Log Window");
        var viewModel = _serviceProvider.GetRequiredService<LogWindowViewModel>();
        var window = new LogWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowConfigurationWindow()
    {
        Logger.LogInformation("Opening Configuration Window");
        var viewModel = _serviceProvider.GetRequiredService<ConfigurationWindowViewModel>();
        var window = new ConfigurationWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowPADClientWindow()
    {
        _padWindowCounter++;
        Logger.LogInformation("Opening PAD Client Window #{WindowNumber}", _padWindowCounter);
        var logger = _serviceProvider.GetRequiredService<ILogger<PADClientWindowViewModel>>();
        var viewModel = new PADClientWindowViewModel(logger, _runtimeService, _padWindowCounter);
        var window = new PADClientWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowTADServerWindow()
    {
        Logger.LogInformation("Opening TAD Server Window");
        var viewModel = _serviceProvider.GetRequiredService<TADServerWindowViewModel>();
        var window = new TADServerWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowXMSGServerWindow()
    {
        Logger.LogInformation("Opening XMSG Server Window");
        var viewModel = _serviceProvider.GetRequiredService<XMSGServerWindowViewModel>();
        var window = new XMSGServerWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowStatusWindow()
    {
        Logger.LogInformation("Opening Status Window");
        var viewModel = _serviceProvider.GetRequiredService<StatusWindowViewModel>();
        var window = new StatusWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowRoutesWindow()
    {
        Logger.LogInformation("Opening Routes Window");
        var viewModel = _serviceProvider.GetRequiredService<RoutesWindowViewModel>();
        var window = new RoutesWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowSintranTadServerWindow()
    {
        Logger.LogInformation("Opening SINTRAN TAD Server Window");
        var viewModel = _serviceProvider.GetRequiredService<SintranTadServerWindowViewModel>();
        var window = new SintranTadServerWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowSintranTadClientWindow()
    {
        _sintranTadClientWindowCounter++;
        Logger.LogInformation("Opening SINTRAN TAD Client Window #{WindowNumber}", _sintranTadClientWindowCounter);
        var logger = _serviceProvider.GetRequiredService<ILogger<SintranTadClientWindowViewModel>>();
        var viewModel = new SintranTadClientWindowViewModel(logger, _runtimeService, _dialogService, _sintranTadClientWindowCounter);
        var window = new SintranTadClientWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowSimulateReceiveWindow()
    {
        Logger.LogInformation("Opening Simulate Receive Window");
        var viewModel = _serviceProvider.GetRequiredService<SimulateReceiveWindowViewModel>();
        var window = new SimulateReceiveWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ResetStatistics()
    {
        Logger.LogInformation("Reset statistics requested");
        _dialogService.ShowInfo("Reset Statistics",
            "Statistics reset functionality will clear all counters.\n\nNote: This requires runtime restart to take effect.");
    }

    [RelayCommand]
    private void ClearPacketHistory()
    {
        Logger.LogInformation("Clear packet history requested");
        _dialogService.ShowInfo("Clear Packet History",
            "Packet history can be cleared from the Packet Window (F2).\n\nUse the 'Clear' button in the packet window toolbar.");
    }

    [RelayCommand]
    private void ShowHelp()
    {
        Logger.LogInformation("Showing Help dialog");
        _dialogService.ShowInfo(
            "X.25 Emulator - Help",
            "Keyboard Shortcuts:\n\n" +
            "F2 - Packet Window\n" +
            "  Monitor all protocol traffic (HDLC, LAPB, X.25)\n" +
            "  Filter by direction (TX/RX) and layer\n" +
            "  Collapse mode to show only terminal packets\n\n" +
            "F7 - Log Window\n" +
            "  View system logs with filtering\n" +
            "  Filter by level (Debug/Info/Warning/Error)\n" +
            "  Text search in messages and categories\n\n" +
            "F9 - Configuration\n" +
            "  Adjust LAPB timers (T1, T2, T3)\n" +
            "  Configure X.25 parameters\n" +
            "  Set history capacity\n\n" +
            "F10 - PAD Client Test\n" +
            "  Open X.28/X.29 PAD terminal\n" +
            "  Connect to TAD services\n" +
            "  Multiple instances supported\n\n" +
            "F11 - TAD Server Monitor\n" +
            "  Monitor LocalTAD sessions on address 200\n" +
            "  View X.3 parameters per session\n" +
            "  Echo service with HELP/STATUS/QUIT commands\n\n" +
            "F12 - XMSG Server Monitor\n" +
            "  Monitor COSMOS file transfer services\n" +
            "  View sessions by service port\n" +
            "  File(0x1000), RPC(0x1001), Monitor(0x1002)\n\n" +
            "Testing PAD to TAD:\n" +
            "1. Press F10 to open PAD Client\n" +
            "2. Type: CALL 200\n" +
            "3. Type any text - it will be echoed back\n" +
            "4. Press F11 to see the active session\n" +
            "5. Press F2 to see all X.25 packets"
        );
    }

    [RelayCommand]
    private void ShowAbout()
    {
        Logger.LogInformation("Showing About dialog");
        _dialogService.ShowInfo(
            "About X.25 Emulator",
            "X.25 Protocol Emulator v1.0\n\n" +
            "A comprehensive ITU-T X.25 protocol emulator with full stack implementation:\n" +
            "• Physical Layer - Frame detection and serial communication\n" +
            "• HDLC Layer - Frame processing and FCS validation\n" +
            "• LAPB Layer - Link management and state machine\n" +
            "• X.25 Layer - Packet processing and virtual circuits\n\n" +
            "Built-in Protocol Services:\n" +
            "• LocalTAD Protocol - Echo service on address 200\n" +
            "• LocalXMSG Protocol - COSMOS file transfer services\n" +
            "• PAD Client - ITU-T X.3/X.28/X.29 terminal emulation\n\n" +
            "Technology Stack:\n" +
            "• Avalonia UI 11.3.6 - Cross-platform UI framework\n" +
            "• .NET 9.0 - Modern C# runtime\n" +
            "• MVVM Architecture - Clean separation of concerns\n" +
            "• Dependency Injection - Microsoft.Extensions.DependencyInjection\n\n" +
            "© 2025 - Following ITU-T X.25 specifications and Fluent Design principles"
        );
    }

    [RelayCommand]
    private async Task Exit()
    {
        Logger.LogInformation("Exit requested");
        var confirmed = await _dialogService.ShowConfirmation(
            "Exit Application",
            "Are you sure you want to exit?"
        );

        if (confirmed)
        {
            Logger.LogInformation("Shutting down application");
            // Application shutdown handled by closing the main window
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            lifetime?.Shutdown();
        }
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        if (IsRuntimeInitialized)
        {
            var runtime = _runtimeService.Runtime;
            runtime.StatisticsUpdated -= OnStatisticsUpdated;
            runtime.StateChanged -= OnStateChanged;
        }

        Logger.LogInformation("MainWindowViewModel disposed");
        base.Dispose();
    }

    /// <summary>
    /// Populate interface collection from runtime
    /// </summary>
    private void PopulateInterfaces()
    {
        if (!IsRuntimeInitialized)
            return;

        Interfaces.Clear();

        var runtime = _runtimeService.Runtime;
        var config = runtime.Config;

        // Get all transport interfaces from runtime
        var transports = runtime.TransportInterfaces;

        // Add serial interfaces
        for (int i = 0; i < config.Routing.SerialInterfaces.Count; i++)
        {
            var serialConfig = config.Routing.SerialInterfaces[i];
            var transportFound = transports.TryGetValue(serialConfig.Name, out var transport);

            var interfaceVm = new InterfaceStatusViewModel
            {
                Name = serialConfig.Name,
                Type = "Serial",
                Details = $"{serialConfig.PortName} ({serialConfig.BaudRate} baud)",
                Status = transportFound && transport != null ? transport.Status.ToString() : "Not Started",
                RemoteMachineId = serialConfig.RemoteMachineId,
                Enabled = serialConfig.Enabled,
                RxBytes = 0,
                TxBytes = 0,
                RxFrames = 0,
                TxFrames = 0,
                LapbAutoConnect = serialConfig.LapbAutoConnect,
                LapbState = "Disconnected",
                Transport = transport,
                SerialConfig = serialConfig  // Store reference for updates
            };

            // Subscribe to LAPB auto-connect changes
            interfaceVm.LapbAutoConnectChanged += OnLapbAutoConnectChanged;

            Interfaces.Add(interfaceVm);
        }

        // Add XOT interfaces
        for (int i = 0; i < config.Routing.XotInterfaces.Count; i++)
        {
            var xotConfig = config.Routing.XotInterfaces[i];
            var transportFound = transports.TryGetValue(xotConfig.Name, out var transport);

            var details = xotConfig.Mode == XotMode.Client
                ? $"{xotConfig.RemoteIPAddress}:{xotConfig.RemotePort} (Client)"
                : $":{xotConfig.LocalPort} (Server)";

            Interfaces.Add(new InterfaceStatusViewModel
            {
                Name = xotConfig.Name,
                Type = "XOT",
                Details = details,
                Status = transportFound && transport != null ? transport.Status.ToString() : "Not Started",
                RemoteMachineId = xotConfig.RemoteMachineId,
                Enabled = xotConfig.Enabled,
                RxBytes = 0,
                TxBytes = 0,
                RxFrames = 0,
                TxFrames = 0,
                LapbAutoConnect = false,
                LapbState = "N/A",
                Transport = transport
            });
        }
    }

    /// <summary>
    /// Update aggregate statistics for status bar
    /// </summary>
    private void UpdateAggregateStatistics(StatisticsSnapshot stats)
    {
        // Aggregate bytes and frames
        long totalRxBytes = stats.HDLC.BytesReceived;
        long totalTxBytes = stats.HDLC.BytesTransmitted;
        long totalRxFrames = stats.HDLC.FramesReceived;
        long totalTxFrames = stats.HDLC.FramesTransmitted;

        // Format bytes with units
        AggregateRxBytes = FormatBytes(totalRxBytes);
        AggregateTxBytes = FormatBytes(totalTxBytes);
        AggregateRxFrames = totalRxFrames.ToString();
        AggregateTxFrames = totalTxFrames.ToString();

        // Error counts
        AggregateFcsErrors = stats.HDLC.FramesInvalidFcs.ToString();
        AggregateLapbErrors = stats.LAPB.Retransmissions.ToString();

        // Uptime
        RuntimeUptime = FormatTimeSpan(stats.Uptime);

        // Frame hierarchy (HDLC split)
        HdlcTotalRxFrames = stats.HDLC.FramesReceived.ToString();
        HdlcLapbRxFrames = stats.LAPB.FramesReceived.ToString();
        HdlcX25RxFrames = stats.X25.PacketsReceived.ToString();
        HdlcOtherRxFrames = (stats.HDLC.FramesReceived - stats.LAPB.FramesReceived - stats.X25.PacketsReceived).ToString();

        HdlcTotalTxFrames = stats.HDLC.FramesTransmitted.ToString();
        HdlcLapbTxFrames = stats.LAPB.FramesTransmitted.ToString();
        HdlcX25TxFrames = stats.X25.PacketsTransmitted.ToString();
        HdlcOtherTxFrames = (stats.HDLC.FramesTransmitted - stats.LAPB.FramesTransmitted - stats.X25.PacketsTransmitted).ToString();
    }

    /// <summary>
    /// Update host information from runtime configuration
    /// </summary>
    private void UpdateHostInformation()
    {
        if (!IsRuntimeInitialized)
            return;

        var config = _runtimeService.Runtime.Config;
        LocalMachineId = config.X25.LocalMachineId;
        LocalDteAddress = string.IsNullOrEmpty(config.X25.LocalDteAddress) ? "(none)" : config.X25.LocalDteAddress;
        DefaultGateway = string.IsNullOrEmpty(config.Routing.DefaultGateway) ? "(none)" : config.Routing.DefaultGateway;
    }

    /// <summary>
    /// Update routing statistics
    /// </summary>
    private void UpdateRoutingStatistics()
    {
        if (!IsRuntimeInitialized)
            return;

        var config = _runtimeService.Runtime.Config;

        int activeCount = 0;
        int totalCount = config.Routing.RoutingTable.Count;

        for (int i = 0; i < config.Routing.RoutingTable.Count; i++)
        {
            if (config.Routing.RoutingTable[i].Enabled)
                activeCount++;
        }

        ActiveRoutes = activeCount.ToString();
        TotalRoutes = totalCount.ToString();

        // Build routing summary (first 3 routes)
        if (totalCount == 0)
        {
            RoutingSummary = "No routes configured";
        }
        else
        {
            var summaryParts = new System.Collections.Generic.List<string>();
            int count = 0;
            for (int i = 0; i < config.Routing.RoutingTable.Count && count < 3; i++)
            {
                var route = config.Routing.RoutingTable[i];
                var status = route.Enabled ? "" : " (disabled)";
                summaryParts.Add($"{route.X121AddressPattern} → {route.Destination}{status}");
                count++;
            }
            RoutingSummary = string.Join(" | ", summaryParts);
        }
    }

    /// <summary>
    /// Format bytes with appropriate unit
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1048576)
            return $"{bytes / 1048576.0:F2} MB";
        else if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        else
            return $"{bytes} B";
    }

    /// <summary>
    /// Format timespan for display
    /// </summary>
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        else if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        else
            return $"{(int)ts.TotalSeconds}s";
    }

    /// <summary>
    /// Handle LAPB auto-connect checkbox changes
    /// </summary>
    private void OnLapbAutoConnectChanged(object? sender, bool enabled)
    {
        if (sender is not InterfaceStatusViewModel interfaceVm)
            return;

        Logger.LogInformation("LAPB auto-connect {Action} for interface {Interface}",
            enabled ? "enabled" : "disabled", interfaceVm.Name);

        if (enabled && IsRuntimeInitialized)
        {
            try
            {
                // Legacy single-LAPB SABM path retired. Stack B links establish LAPB themselves when
                // configured with LapbAutoConnect; there is no runtime-level LAPB layer to connect.
                Logger.LogWarning("LAPB auto-connect for {Interface}: legacy SABM path retired; configure the link with LapbAutoConnect", interfaceVm.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to send SABM for interface {Interface}", interfaceVm.Name);
                _dialogService.ShowError("LAPB Connect Failed",
                    $"Failed to establish LAPB connection for {interfaceVm.Name}:\n\n{ex.Message}");
            }
        }
    }
}

/// <summary>
/// View model for individual interface status display
/// </summary>
public partial class InterfaceStatusViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _type = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _remoteMachineId = string.Empty;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private long _rxBytes;

    [ObservableProperty]
    private long _txBytes;

    [ObservableProperty]
    private long _rxFrames;

    [ObservableProperty]
    private long _txFrames;

    [ObservableProperty]
    private bool _lapbAutoConnect;

    [ObservableProperty]
    private string _lapbState = "N/A";

    /// <summary>
    /// Reference to the transport interface (for statistics updates)
    /// </summary>
    public ITransportInterface? Transport { get; set; }

    /// <summary>
    /// Reference to serial interface config (for LAPB auto-connect updates)
    /// </summary>
    public SerialInterfaceConfig? SerialConfig { get; set; }

    /// <summary>
    /// Event fired when LAPB auto-connect changes
    /// </summary>
    public event EventHandler<bool>? LapbAutoConnectChanged;

    /// <summary>
    /// Formatted display strings
    /// </summary>
    public string RxBytesFormatted => FormatBytes(RxBytes);
    public string TxBytesFormatted => FormatBytes(TxBytes);

    /// <summary>
    /// Called when LapbAutoConnect property changes
    /// </summary>
    partial void OnLapbAutoConnectChanged(bool value)
    {
        // Update config if available
        if (SerialConfig != null)
        {
            SerialConfig.LapbAutoConnect = value;
        }

        // Notify subscribers
        LapbAutoConnectChanged?.Invoke(this, value);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1048576)
            return $"{bytes / 1048576.0:F2} MB";
        else if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        else
            return $"{bytes} B";
    }
}
