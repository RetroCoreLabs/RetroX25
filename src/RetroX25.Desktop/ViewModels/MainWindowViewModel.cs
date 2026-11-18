//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetroIO.Link;
using RetroIO.Link.Lapb;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Runtime.Transport;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// Main window ViewModel managing application state and protocol runtime.
/// </summary>
/// <remarks>
/// The main window is the "is it up?" screen: the host information, one line for each interface or
/// link (is it up, what its data link is doing, which machine is at the far end), the built-in switch
/// and one line with the uptime and a plain "all links up" or "n link(s) down". It shows no counters:
/// every counter is in the Status window (Ctrl+1).
/// </remarks>
public partial class MainWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly PacketViewState _packetViewState;
    private int _padWindowCounter = 0;

    [ObservableProperty]
    private string _windowTitle = "X.25 Protocol Emulator";

    [ObservableProperty]
    private bool _isRuntimeInitialized;

    // Host Information
    [ObservableProperty]
    private string _localMachineId = "200";

    [ObservableProperty]
    private string _localDteAddress = "(none)";

    [ObservableProperty]
    private string _defaultGateway = "(none)";

    // One line for each interface or link.
    [ObservableProperty]
    private ObservableCollection<InterfaceStatusViewModel> _interfaces = new();

    /// <summary>Gets or sets how long the engine has run, as text such as "3m 12s".</summary>
    [ObservableProperty]
    private string _runtimeUptime = "0s";

    /// <summary>
    /// Gets or sets the one-line answer to "is it up?": "All links up", "2 link(s) down", "Checking
    /// links..." or "No links configured".
    /// </summary>
    [ObservableProperty]
    private string _linkSummary = "No links configured";

    /// <summary>Gets or sets a value indicating whether every enabled link is up.</summary>
    [ObservableProperty]
    private bool _allLinksUp;

    /// <summary>Gets or sets the colour of <see cref="LinkSummary"/>: green when all is up, red when a link is down.</summary>
    [ObservableProperty]
    private IBrush _linkSummaryBrush = Brushes.Gray;

    /// <summary>
    /// Gets the XOT peers shown in the "XOT links" section: one line for every peer that is not part of the
    /// built-in switch's own plumbing. Rebuilt about once a second from the real state of the links.
    /// </summary>
    public ObservableCollection<XotPeerLineViewModel> XotPeers { get; } = new();

    /// <summary>Gets or sets a value indicating whether the quiet "Built-in switch" line is shown.</summary>
    [ObservableProperty]
    private bool _hasBuiltInLine;

    /// <summary>Gets or sets the quiet line: "Built-in switch: OK", or "Built-in switch: PROBLEM - reason".</summary>
    [ObservableProperty]
    private string _builtInLine = string.Empty;

    /// <summary>Gets or sets the colour of <see cref="BuiltInLine"/>: grey when all is well, red with a reason.</summary>
    [ObservableProperty]
    private IBrush _builtInBrush = Brushes.Gray;

    /// <summary>Gets or sets a value indicating whether the "Trunk links" section has anything to show.</summary>
    [ObservableProperty]
    private bool _hasXotSection;

    /// <summary>
    /// Gets or sets the telnet PAD line: "Telnet PAD: listening on 127.0.0.1:2323, 2 sessions",
    /// "Telnet PAD: off", or "Telnet PAD: failed: port 2323 is already in use".
    /// </summary>
    [ObservableProperty]
    private string _telnetPadLine = "Telnet PAD: off";

    /// <summary>Gets or sets the colour of <see cref="TelnetPadLine"/>: grey when off, green when listening, red when it failed.</summary>
    [ObservableProperty]
    private IBrush _telnetPadBrush = Brushes.Gray;

    // Reads the XOT links again about once a second. Null where there is no Avalonia application (design time).
    private Avalonia.Threading.DispatcherTimer? _xotTimer;

    /// <summary>
    /// Creates MainWindowViewModel with dependency injection.
    /// </summary>
    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        PacketViewState packetViewState) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _packetViewState = packetViewState ?? throw new ArgumentNullException(nameof(packetViewState));

        IsRuntimeInitialized = _runtimeService.IsRunning;
        _runtimeService.RuntimeRestarted += OnRuntimeRestarted;

        if (IsRuntimeInitialized)
        {
            SubscribeToRuntimeEvents();
            PopulateInterfaces();
            UpdateHostInformation();
            UpdateStatus();
        }

        RefreshXotLinks();
        RefreshTelnetPad();
        if (Avalonia.Application.Current != null)
        {
            _xotTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _xotTimer.Tick += (_, _) =>
            {
                RefreshXotLinks();
                RefreshTelnetPad();
            };
            _xotTimer.Start();
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
    /// The runtime was replaced (the Configuration window's Apply): close the windows that hold the
    /// old engine and attach this window to the new one.
    /// </summary>
    /// <remarks>
    /// A PAD, TAD or packet window holds a call, a subscription or a history reader of the engine
    /// that was just disposed, and cannot be moved to the new one, so it is closed; open it again
    /// for a view of the new engine. The main window and the Configuration window (which is what
    /// asked for the restart) stay.
    /// </remarks>
    private void OnRuntimeRestarted(object? sender, EventArgs e)
    {
        System.Collections.Generic.List<Avalonia.Controls.Window> open = _navigationService.GetOpenWindows();
        for (int i = 0; i < open.Count; i++)
        {
            Avalonia.Controls.Window window = open[i];
            if (window is not RetroX25.Desktop.Views.MainWindow && window is not RetroX25.Desktop.Views.ConfigurationWindow)
            {
                _navigationService.CloseWindow(window);
            }
        }

        IsRuntimeInitialized = _runtimeService.IsRunning;
        if (IsRuntimeInitialized)
        {
            SubscribeToRuntimeEvents();
            PopulateInterfaces();
            UpdateHostInformation();
            UpdateStatus();
        }

        RefreshXotLinks();
        RefreshTelnetPad();
        Logger.LogInformation("Main window attached to the restarted runtime");
    }

    /// <summary>
    /// Handle statistics updates from ProtocolRuntime.
    /// </summary>
    private void OnStatisticsUpdated(object? sender, StatisticsSnapshot stats)
    {
        if (_serviceProvider.GetService(typeof(EmbeddedSwitchService)) is EmbeddedSwitchService embedded)
        {
            SwitchStatus = embedded.Describe();
        }

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
    /// Updates the uptime, every link's line and the "all links up" flag.
    /// </summary>
    /// <param name="stats">
    /// The snapshot the engine just raised, or null to read the engine now. Only its uptime is used:
    /// the main window shows no counters.
    /// </param>
    internal void UpdateStatus(StatisticsSnapshot? stats = null)
    {
        if (!IsRuntimeInitialized)
            return;

        TimeSpan uptime = stats != null ? stats.Uptime : _runtimeService.Runtime.GetStatistics().Uptime;
        RuntimeUptime = StatisticsReader.FormatTimeSpan(uptime);

        // Test the far end of each XOT client link again each time the statistics update, so a switch
        // or router that goes away (or comes back) shows within a few seconds.
        UpdateInterfaceLines(true);
        UpdateLinkSummary();
    }

    /// <summary>
    /// Says in plain words what state an XOT link is really in.
    /// </summary>
    /// <param name="details">
    /// The interface's detail text, which ends in (Client) or (Server).
    /// </param>
    /// <param name="status">
    /// The transport's own status word, such as Active. For a client, Active only means ready: the
    /// TCP connection is made at the first call.
    /// </param>
    /// <param name="connections">
    /// The number of open TCP connections.
    /// </param>
    /// <param name="probe">
    /// The result of the last quick connection test of the far end of a client link: null when it has not
    /// been tested yet, an empty string when the far end answered, otherwise the reason it did not.
    /// </param>
    /// <returns>The text for the interface line.</returns>
    internal static string DescribeXotLink(string details, string status, int connections, string? probe = null)
    {
        if (status != "Active")
        {
            return status;
        }

        if (details.EndsWith("(Server)", StringComparison.Ordinal))
        {
            return connections > 0
                ? $"Listening, {connections} connection(s) open"
                : "Listening, nobody connected";
        }

        if (connections > 0)
        {
            return $"Connected (TCP up, {connections})";
        }

        // No TCP connection yet (a client link connects at the first call): say what a quick
        // connection test to the far end found, so a dead switch or router shows before a call fails.
        if (probe == null)
        {
            return "Checking the far end...";
        }

        return probe.Length == 0
            ? "Far end answers (no call placed yet)"
            : "NOT REACHABLE: " + probe;
    }

    /// <summary>
    /// True for an XOT line that dials out (a Client, or a Both-ways peer entry), so its far end is worth a quick connection test.
    /// A Server line only listens.
    /// </summary>
    private static bool DialsOut(string details)
    {
        return details.EndsWith("(Client)", StringComparison.Ordinal) || details.EndsWith("(peer: dials out and accepts)", StringComparison.Ordinal);
    }

    // Result of the last far-end test per client link: "" = answers, text = why not. Missing = not tested yet.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _probeResults = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _probeRunning = new();

    /// <summary>
    /// Starts a background connection test of a client link's far end, unless one is already running.
    /// The result is used the next time the link's line is built.
    /// </summary>
    private void StartProbe(InterfaceStatusViewModel link)
    {
        // A link with a connection open is known to be reachable: a test connection to it would only add a
        // useless connection (the far end sees one more, and counts it).
        if (link.Transport != null && link.Transport.GetStatistics().ActiveConnections > 0)
        {
            return;
        }

        string details = link.Details;
        int colon = details.IndexOf(':');
        int space = details.IndexOf(' ');
        if (colon <= 0 || space <= colon || !int.TryParse(details.AsSpan(colon + 1, space - colon - 1), out int port))
        {
            return;
        }

        string host = details.Substring(0, colon);
        string name = link.Name;
        if (!_probeRunning.TryAdd(name, true))
        {
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            string? reason = await XotLinkProbe.CheckAsync(host, port, 1500).ConfigureAwait(false);
            _probeResults[name] = reason ?? "";
            _probeRunning.TryRemove(name, out _);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshXotLine(link));
        });
    }

    /// <summary>
    /// Tests the far end of every client link again and rebuilds every XOT line now, without waiting
    /// for the next statistics update.
    /// </summary>
    public void RefreshLinksNow()
    {
        for (int i = 0; i < Interfaces.Count; i++)
        {
            InterfaceStatusViewModel link = Interfaces[i];
            if (link.Type == "XOT" && DialsOut(link.Details))
            {
                StartProbe(link);
            }
        }
    }

    /// <summary>
    /// Rebuilds one XOT link's status line from the transport and the last far-end test, and the
    /// "all links up" flag.
    /// </summary>
    private void RefreshXotLine(InterfaceStatusViewModel link)
    {
        if (link.Transport == null)
        {
            return;
        }

        RefreshLine(link);
        UpdateLinkSummary();
    }

    /// <summary>
    /// Reads the telnet PAD's real state from the engine and updates the telnet line: off, listening
    /// (address, port, open sessions) or failed (with the reason). Safe to call at any time; it only reads.
    /// </summary>
    public void RefreshTelnetPad()
    {
        if (!_runtimeService.IsRunning)
        {
            TelnetPadLine = "Telnet PAD: off";
            TelnetPadBrush = Brushes.Gray;
            return;
        }

        TelnetPadStatus status = _runtimeService.Runtime.TelnetPadStatus;
        if (TelnetPadLine != status.Text)
        {
            TelnetPadLine = status.Text;
        }

        TelnetPadBrush = status.State == TelnetPadState.Failed ? Brushes.Red
            : status.State == TelnetPadState.Listening ? Brushes.Green
            : Brushes.Gray;
    }

    /// <summary>
    /// Reads the real state of every XOT link (the engine's XOT transports and the built-in switch's XOT
    /// ports) and updates the "XOT links" section: one line for every peer, and the one quiet line for the
    /// built-in switch's own plumbing. Safe to call at any time; it only reads.
    /// </summary>
    public void RefreshXotLinks()
    {
        if (!_runtimeService.IsRunning)
        {
            XotPeers.Clear();
            HasBuiltInLine = false;
            HasXotSection = false;
            return;
        }

        ProtocolRuntime runtime = _runtimeService.Runtime;
        EmbeddedSwitchService? embedded = _serviceProvider.GetService(typeof(EmbeddedSwitchService)) as EmbeddedSwitchService;
        XotLinkHealthSnapshot snapshot = XotLinkHealthReader.Read(runtime.Config, runtime.TransportInterfaces, embedded, DateTime.UtcNow);

        // Replace a line only when its text changed, so the list does not flicker or lose its place.
        bool same = XotPeers.Count == snapshot.Peers.Count;
        for (int i = 0; same && i < snapshot.Peers.Count; i++)
        {
            same = XotPeers[i].Row.Name == snapshot.Peers[i].Name && XotPeers[i].Row.Source == snapshot.Peers[i].Source;
        }

        if (!same)
        {
            XotPeers.Clear();
            for (int i = 0; i < snapshot.Peers.Count; i++)
            {
                XotPeers.Add(new XotPeerLineViewModel(snapshot.Peers[i]));
            }
        }
        else
        {
            for (int i = 0; i < snapshot.Peers.Count; i++)
            {
                XotPeerLineViewModel fresh = new XotPeerLineViewModel(snapshot.Peers[i]);
                if (fresh.Text != XotPeers[i].Text)
                {
                    XotPeers[i] = fresh;
                }
            }
        }

        HasBuiltInLine = snapshot.HasBuiltIn;
        BuiltInLine = snapshot.BuiltInText;
        BuiltInBrush = snapshot.BuiltInOk ? Brushes.Gray : Brushes.Red;
        HasXotSection = snapshot.Peers.Count > 0 || snapshot.HasBuiltIn;
    }

    /// <summary>The built-in switch's state in plain words, or why there is none.</summary>
    [ObservableProperty]
    private string _switchStatus = "";

    /// <summary>
    /// Says in words whether a serial line's data link (LAPB) is up.
    /// </summary>
    /// <param name="transport">
    /// The transport of the serial interface.
    /// </param>
    /// <returns>
    /// <c>Up</c> or <c>Down</c> when the line runs its own LAPB link, or a note that the line is
    /// not set up to run one (it then only carries raw bytes and has no state to show).
    /// </returns>
    internal static string DescribeSerialDataLink(ITransportInterface transport)
    {
        if (transport is SerialTransport serial)
        {
            if (serial.LinkRole == null)
            {
                return "no LAPB link configured (raw bytes only)";
            }

            return serial.IsLinkUp ? "Up (data transfer)" : "Down";
        }

        return "N/A";
    }

    /// <summary>
    /// Says in words whether a TCP-LAPB link's data link is up.
    /// </summary>
    /// <param name="state">The link's LAPB state.</param>
    /// <returns><c>Up (data transfer)</c>, or <c>Down</c> with the state in brackets.</returns>
    internal static string DescribeLapbState(LapbState state)
    {
        return state == LapbState.DataTransfer ? "Up (data transfer)" : "Down (" + state + ")";
    }

    /// <summary>
    /// Works out the state of one line (up, down, being checked or disabled) and writes its status
    /// text and LAPB text from the transport or link the line stands for.
    /// </summary>
    private void RefreshLine(InterfaceStatusViewModel line)
    {
        if (!line.Enabled)
        {
            line.Status = "Disabled";
            line.Health = LinkHealth.Disabled;
            return;
        }

        if (line.Type == "TCP-LAPB")
        {
            ILink? link = line.Link;
            if (link == null)
            {
                line.Status = "Not Started";
                line.Health = LinkHealth.Down;
                return;
            }

            line.Status = link.Status.ToString();
            ILapbCapable? lapb = link as ILapbCapable;
            if (lapb != null)
            {
                line.LapbState = DescribeLapbState(lapb.LapbState);
            }

            // The line is up when the TCP link runs AND its data link (LAPB) is in data transfer.
            line.Health = link.Status == LinkStatus.Active && lapb != null && lapb.LapbState == LapbState.DataTransfer
                ? LinkHealth.Up
                : LinkHealth.Down;
            return;
        }

        ITransportInterface? transport = line.Transport;
        if (transport == null)
        {
            line.Status = "Not Started";
            line.Health = LinkHealth.Down;
            return;
        }

        bool running = transport.Status == TransportStatus.Active || transport.Status == TransportStatus.Connected;

        if (line.Type == "XOT")
        {
            TransportStatistics stats = transport.GetStatistics();
            _probeResults.TryGetValue(line.Name, out string? probe);
            line.Status = DescribeXotLink(line.Details, stats.CurrentStatus.ToString(), stats.ActiveConnections, probe);
            if (!running)
            {
                line.Health = LinkHealth.Down;
            }
            else if (line.Details.EndsWith("(Server)", StringComparison.Ordinal) || stats.ActiveConnections > 0)
            {
                line.Health = LinkHealth.Up;
            }
            else if (probe == null)
            {
                line.Health = LinkHealth.Checking;
            }
            else
            {
                line.Health = probe.Length == 0 ? LinkHealth.Up : LinkHealth.Down;
            }

            return;
        }

        // A serial line: up when the port runs and, if it is set up to run LAPB, the data link is up.
        line.Status = transport.Status.ToString();

        // The state of this line's own data link comes from the serial transport itself; the engine
        // keeps no runtime-wide LAPB state for a line any more (each line has its own link).
        line.LapbState = DescribeSerialDataLink(transport);
        bool lapbConfigured = transport is SerialTransport configured && configured.LinkRole != null;
        bool lapbUp = !lapbConfigured || ((SerialTransport)transport).IsLinkUp;
        line.Health = running && lapbUp ? LinkHealth.Up : LinkHealth.Down;
    }

    /// <summary>
    /// Updates the line of every interface and link from the engine.
    /// </summary>
    /// <param name="probe">True to start a new far-end test for each XOT client link.</param>
    private void UpdateInterfaceLines(bool probe)
    {
        for (int i = 0; i < Interfaces.Count; i++)
        {
            InterfaceStatusViewModel line = Interfaces[i];
            if (probe && line.Type == "XOT" && DialsOut(line.Details))
            {
                StartProbe(line);
            }

            RefreshLine(line);
        }
    }

    /// <summary>
    /// Counts the lines that are down and writes the plain answer to "is it up?".
    /// </summary>
    private void UpdateLinkSummary()
    {
        int up = 0;
        int down = 0;
        int checking = 0;
        for (int i = 0; i < Interfaces.Count; i++)
        {
            switch (Interfaces[i].Health)
            {
                case LinkHealth.Up:
                    up++;
                    break;
                case LinkHealth.Down:
                    down++;
                    break;
                case LinkHealth.Checking:
                    checking++;
                    break;
            }
        }

        if (down > 0)
        {
            LinkSummary = down + " link(s) down";
            AllLinksUp = false;
            LinkSummaryBrush = Brushes.Red;
        }
        else if (checking > 0)
        {
            LinkSummary = "Checking links...";
            AllLinksUp = false;
            LinkSummaryBrush = Brushes.Gray;
        }
        else if (up > 0)
        {
            LinkSummary = "All links up";
            AllLinksUp = true;
            LinkSummaryBrush = Brushes.Green;
        }
        else
        {
            LinkSummary = "No links configured";
            AllLinksUp = false;
            LinkSummaryBrush = Brushes.Gray;
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

    /// <summary>
    /// Opens the chat monitor (Ctrl+8): the users and channels of the built-in Altos chat.
    /// </summary>
    [RelayCommand]
    private void ShowChatServerWindow()
    {
        Logger.LogInformation("Opening Chat Server Window");
        var viewModel = _serviceProvider.GetRequiredService<ChatServerWindowViewModel>();
        var window = new ChatServerWindow(viewModel);
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
    private void ShowInvoiceWindow()
    {
        Logger.LogInformation("Opening Bill Window");
        var viewModel = _serviceProvider.GetRequiredService<InvoiceWindowViewModel>();
        var window = new InvoiceWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    [RelayCommand]
    private void ShowRoutesWindow()
    {
        Logger.LogInformation("Opening Routing Window");
        var viewModel = _serviceProvider.GetRequiredService<RoutingViewModel>();
        var window = new RoutesWindow(viewModel);
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
    private void ShowLinkStatusWindow()
    {
        Logger.LogInformation("Opening Link Status Window");
        var viewModel = _serviceProvider.GetRequiredService<LinkStatusWindowViewModel>();
        var window = new LinkStatusWindow(viewModel);
        _navigationService.OpenWindow(window);
    }

    /// <summary>
    /// Hides the packets recorded so far, in every Packet window (open now or opened later).
    /// </summary>
    /// <remarks>
    /// The engine has no call to empty its packet history, so nothing is deleted: the packets are
    /// hidden from the Packet windows, and the engine's history stays as it is. This does not touch
    /// the counters; Reset Statistics does that.
    /// </remarks>
    [RelayCommand]
    private void ClearPacketHistory()
    {
        Logger.LogInformation("Clear packet history requested");
        _packetViewState.ClearAll();
    }

    /// <summary>
    /// Sets every counter of the engine back to zero (the engine's totals and the counters of each
    /// link and interface). The View menu's "Reset Statistics" runs this; the Status window has a
    /// button for the same thing.
    /// </summary>
    [RelayCommand]
    private void ResetStatistics()
    {
        if (!_runtimeService.IsRunning)
        {
            return;
        }

        Logger.LogInformation("Reset statistics requested from the View menu");
        _runtimeService.Runtime.ResetStatistics();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        Logger.LogInformation("Showing Help dialog");
        _dialogService.ShowInfo(
            "X.25 Emulator - Help",
            "Keyboard Shortcuts (main window):\n\n" +
            "The main window answers \"is it up?\": each link, up or down, and the uptime.\n" +
            "Every counter is in the Status window.\n\n" +
            "Ctrl+1 - Status Information (all counters, in one table: total, and a column for each link and interface)\n" +
            "  Has a Reset Statistics button; View > Reset Statistics does the same\n" +
            "Ctrl+2 - Packet Window\n" +
            "  Monitor all protocol traffic (HDLC, LAPB, X.25)\n" +
            "  Filter by direction, layer, LAPB frame class, X.25 packet group,\n" +
            "  channel number and errors; collapse mode shows only terminal packets\n" +
            "Ctrl+Shift+Delete - Clear Packet History (hides the packets recorded so far in the Packet windows)\n" +
            "Ctrl+3 - Log Window\n" +
            "  View system logs with filtering\n" +
            "  Filter by level (Debug/Info/Warning/Error)\n" +
            "  Text search in messages and categories\n" +
            "Ctrl+4 - Routes (view the routing table and test an address)\n" +
            "Ctrl+, - Configuration\n" +
            "  Adjust LAPB timers (T1, T2, T3)\n" +
            "  Configure X.25 parameters\n" +
            "  Set history capacity\n" +
            "Ctrl+5 - PAD Client Test\n" +
            "  Open X.28/X.29 PAD terminal\n" +
            "  Connect to TAD services\n" +
            "  Multiple instances supported\n" +
            "Ctrl+6 - TAD Server Monitor\n" +
            "  Monitor LocalTAD sessions on address 200\n" +
            "  View X.3 parameters per session\n" +
            "  Echo service with HELP/STATUS/QUIT commands\n" +
            "Ctrl+7 - Bill\n" +
            "  An account's bill for a month, from the call record file and the price list\n" +
            "Ctrl+L - Link Status (start and stop links, LAPB connect and disconnect)\n" +
            "F1 - this help\n" +
            "Alt+F4 - Exit (also File, Exit)\n\n" +
            "Testing PAD to TAD:\n" +
            "1. Press Ctrl+5 to open PAD Client\n" +
            "2. Type: CALL 200\n" +
            "3. Type any text - it will be echoed back\n" +
            "4. Press Ctrl+6 to see the active session\n" +
            "5. Press Ctrl+2 to see all X.25 packets"
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
        _runtimeService.RuntimeRestarted -= OnRuntimeRestarted;
        _xotTimer?.Stop();
        _xotTimer = null;

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
                LapbState = "Disconnected",
                Transport = transport,
            };


            Interfaces.Add(interfaceVm);
        }

        // Add XOT interfaces
        for (int i = 0; i < config.Routing.XotInterfaces.Count; i++)
        {
            var xotConfig = config.Routing.XotInterfaces[i];
            var transportFound = transports.TryGetValue(xotConfig.Name, out var transport);

            var details = xotConfig.Mode == XotMode.Client
                ? $"{xotConfig.RemoteIPAddress}:{xotConfig.RemotePort} (Client)"
                : xotConfig.Mode == XotMode.Both
                    ? $"{xotConfig.RemoteIPAddress}:{xotConfig.RemotePort} / :{xotConfig.LocalPort} (peer: dials out and accepts)"
                    : $":{xotConfig.LocalPort} (Server)";

            InterfaceStatusViewModel xotLine = new InterfaceStatusViewModel
            {
                Name = xotConfig.Name,
                Type = "XOT",
                Details = details,
                Status = transportFound && transport != null
                    ? DescribeXotLink(details, transport.Status.ToString(), 0)
                    : "Not Started",
                RemoteMachineId = xotConfig.RemoteMachineId,
                Enabled = xotConfig.Enabled,
                LapbState = "N/A",
                Transport = transport
            };
            Interfaces.Add(xotLine);
            if (xotConfig.Mode != XotMode.Server)
            {
                StartProbe(xotLine);
            }
        }

        // Add TCP-LAPB links. They are links of the engine's link manager (not transport interfaces),
        // so each one is found by its name among the engine's links.
        ILink[] links = runtime.GetAllLinks();
        for (int i = 0; i < config.Routing.TcpLapbInterfaces.Count; i++)
        {
            var tcpConfig = config.Routing.TcpLapbInterfaces[i];
            ILink? found = null;
            for (int j = 0; j < links.Length && found == null; j++)
            {
                if (string.Equals(links[j].Name, tcpConfig.Name, StringComparison.Ordinal))
                {
                    found = links[j];
                }
            }

            Interfaces.Add(new InterfaceStatusViewModel
            {
                Name = tcpConfig.Name,
                Type = "TCP-LAPB",
                Details = tcpConfig.Mode == TcpMode.Server
                    ? $":{tcpConfig.Port} (Server)"
                    : $"{tcpConfig.RemoteAddress}:{tcpConfig.Port} (Client)",
                Status = found != null ? found.Status.ToString() : "Not Started",
                RemoteMachineId = tcpConfig.RemoteMachineId,
                Enabled = tcpConfig.Enabled,
                LapbState = "Disconnected",
                Link = found
            });
        }

        SwitchStatus = _serviceProvider.GetService(typeof(EmbeddedSwitchService)) is EmbeddedSwitchService embedded
            ? embedded.Describe()
            : "";

        // Fill in up or down for every line now (the far-end tests of the XOT clients are already running).
        UpdateInterfaceLines(false);
        UpdateLinkSummary();
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
}

/// <summary>
/// Whether one line of the main window is up.
/// </summary>
public enum LinkHealth
{
    /// <summary>The interface is switched off in the configuration; it is not counted.</summary>
    Disabled,

    /// <summary>The link or interface is up.</summary>
    Up,

    /// <summary>The link or interface is down, not started or in error.</summary>
    Down,

    /// <summary>The far end of an XOT client link has not been tested yet.</summary>
    Checking
}

/// <summary>
/// View model for individual interface status display: one line of the main window.
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

    // The text after the status: the state of the data link, for the lines that have one.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LapbPart))]
    private string _lapbState = "N/A";

    /// <summary>Gets or sets whether the line is up, down, being checked or disabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private LinkHealth _health = LinkHealth.Down;

    /// <summary>
    /// Reference to the transport interface (for a serial line or an XOT interface), or null.
    /// </summary>
    public ITransportInterface? Transport { get; set; }

    /// <summary>
    /// Reference to the link (for a TCP-LAPB link), or null.
    /// </summary>
    public ILink? Link { get; set; }

    /// <summary>
    /// Gets the data link part of the line, such as " | LAPB link: Up (data transfer)", or an empty
    /// text for a line that has no LAPB (an XOT interface).
    /// </summary>
    public string LapbPart => LapbState == "N/A" ? string.Empty : " | LAPB link: " + LapbState;

    /// <summary>Gets the colour of the status word: green when up, red when down.</summary>
    public IBrush StatusBrush => Health switch
    {
        LinkHealth.Up => Brushes.Green,
        LinkHealth.Down => Brushes.Red,
        _ => Brushes.Gray
    };
}
