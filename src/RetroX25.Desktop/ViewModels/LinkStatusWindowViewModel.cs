//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Link;
using RetroIO.Link.Lapb;
using RetroIO.Protocols.X25.Runtime.Transport;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// One line of the Link Status window: either a link of the engine's link manager (a TCP-LAPB link)
/// or a transport interface the engine built from the configuration (a serial line or an XOT
/// interface).
/// </summary>
public partial class LinkRowViewModel : ObservableObject
{
    /// <summary>
    /// What the line is in a form the window can count: running, stopped, in error, or between those.
    /// </summary>
    public enum RowState
    {
        /// <summary>The link or interface is up and running.</summary>
        Active,

        /// <summary>The link or interface is stopped.</summary>
        Stopped,

        /// <summary>The link or interface reported an error.</summary>
        Error,

        /// <summary>The link or interface is starting or stopping.</summary>
        Changing
    }

    /// <summary>
    /// Creates a line for a link of the link manager.
    /// </summary>
    /// <param name="link">The link.</param>
    public LinkRowViewModel(ILink link)
    {
        Link = link ?? throw new ArgumentNullException(nameof(link));
        Name = link.Name;
        Key = "link:" + link.Name;
        Update();
    }

    /// <summary>
    /// Creates a line for a transport interface.
    /// </summary>
    /// <param name="transport">The transport interface.</param>
    public LinkRowViewModel(ITransportInterface transport)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Name = transport.Name;
        Key = "interface:" + transport.Name;
        Update();
    }

    /// <summary>Gets the link, or null when this line is a transport interface.</summary>
    public ILink? Link { get; }

    /// <summary>Gets the transport interface, or null when this line is a link.</summary>
    public ITransportInterface? Transport { get; }

    /// <summary>Gets the name shown for the line.</summary>
    public string Name { get; }

    /// <summary>Gets a key that is the same for the same link or interface on each refresh.</summary>
    public string Key { get; }

    /// <summary>Gets the link's LAPB side, or null when the link has no LAPB state machine of its own.</summary>
    public ILapbCapable? Lapb => Link as ILapbCapable;

    /// <summary>Gets or sets the state of the line.</summary>
    [ObservableProperty]
    private RowState _state;

    /// <summary>Gets or sets the status as text (Active, Stopped, Error and so on).</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Gets or sets the kind of line as text (TCP-LAPB link, XOT client, Serial).</summary>
    [ObservableProperty]
    private string _kindText = string.Empty;

    /// <summary>Gets or sets the LAPB state as text, or an empty text when the line has none.</summary>
    [ObservableProperty]
    private string _lapbText = string.Empty;

    /// <summary>Gets or sets the one-line text shown in the list.</summary>
    [ObservableProperty]
    private string _display = string.Empty;

    /// <summary>
    /// Reads the status of the link or interface again and rebuilds the texts.
    /// </summary>
    public void Update()
    {
        if (Link != null)
        {
            KindText = Link.Type switch
            {
                LinkType.TcpLapb => "TCP-LAPB link",
                LinkType.Xot => "XOT link",
                LinkType.Serial => "Serial link",
                _ => "Link"
            };
            StatusText = Link.Status.ToString();
            State = Link.Status switch
            {
                LinkStatus.Active => RowState.Active,
                LinkStatus.Stopped => RowState.Stopped,
                LinkStatus.Error => RowState.Error,
                _ => RowState.Changing
            };
            ILapbCapable? lapb = Lapb;
            LapbText = lapb != null ? lapb.LapbState.ToString() : string.Empty;
        }
        else if (Transport != null)
        {
            KindText = Transport.Type switch
            {
                TransportType.XOT_Client => "XOT client",
                TransportType.XOT_Server => "XOT server",
                TransportType.Serial => "Serial",
                _ => "Interface"
            };
            StatusText = Transport.Status.ToString();
            State = Transport.Status switch
            {
                TransportStatus.Active => RowState.Active,
                TransportStatus.Connected => RowState.Active,
                TransportStatus.Stopped => RowState.Stopped,
                TransportStatus.Error => RowState.Error,
                _ => RowState.Changing
            };
            LapbText = Transport is SerialTransport serial && serial.LinkRole != null
                ? (serial.IsLinkUp ? "DataTransfer" : "Down")
                : string.Empty;
        }

        string marker = State switch
        {
            RowState.Active => "*",
            RowState.Error => "!",
            _ => " "
        };
        string lapbPart = LapbText.Length > 0 ? " [" + LapbText + "]" : string.Empty;
        Display = "[" + marker + "] " + Name + " - " + StatusText + lapbPart + "  (" + KindText + ")";
    }
}

/// <summary>
/// ViewModel for the Link Status window (Ctrl+L): lists the links and interfaces of the running
/// engine, shows what the engine can tell about the selected one, and starts and stops it.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of line are listed. A TCP-LAPB link has its own LAPB state machine, so its LAPB
/// state and V(S), V(R), V(A) are shown, and the LAPB connect and disconnect buttons work. A serial
/// line or an XOT interface is a transport interface: it can be started and stopped. An XOT
/// interface has no LAPB at all (RFC 1613).
/// </para>
/// <para>
/// This window is for control and state only. It shows no counters at all (no bytes, frames,
/// calls or errors counts): every counter is in the Status window (Ctrl+1), so a number can never
/// differ between two windows.
/// </para>
/// </remarks>
public partial class LinkStatusWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private System.Threading.Timer? _refreshTimer;

    /// <summary>
    /// Gets the lines of the window.
    /// </summary>
    public ObservableCollection<LinkRowViewModel> Rows { get; } = new ObservableCollection<LinkRowViewModel>();

    /// <summary>Gets or sets the selected line.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectLapbCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectLapbCommand))]
    private LinkRowViewModel? _selectedRow;

    /// <summary>Gets or sets the line "n Active, n Stopped, n Error".</summary>
    [ObservableProperty]
    private string _summaryText = "0 Active, 0 Stopped, 0 Error";

    /// <summary>Gets or sets the details of the selected line.</summary>
    [ObservableProperty]
    private string _detailText = "Select a link to see its details.";

    /// <summary>Gets or sets the text of the last action, for example why a start did not work.</summary>
    [ObservableProperty]
    private string _actionText = string.Empty;

    /// <summary>
    /// Creates the view-model, reads the engine once and starts a timer that reads it every second.
    /// </summary>
    /// <param name="logger">The logger for this view-model.</param>
    /// <param name="runtimeService">The service that owns the engine.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public LinkStatusWindowViewModel(ILogger<LinkStatusWindowViewModel> logger, IRuntimeService runtimeService)
        : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        Refresh();

        _refreshTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Reads every link and interface of the engine again and updates the list, the summary and the
    /// details of the selected line.
    /// </summary>
    [RelayCommand]
    public void Refresh()
    {
        List<LinkRowViewModel> fresh = new List<LinkRowViewModel>();

        if (_runtimeService.IsRunning)
        {
            RetroIO.Protocols.X25.Runtime.ProtocolRuntime runtime = _runtimeService.Runtime;

            ILink[] links = runtime.GetAllLinks();
            for (int i = 0; i < links.Length; i++)
            {
                fresh.Add(new LinkRowViewModel(links[i]));
            }

            IReadOnlyDictionary<string, ITransportInterface> interfaces = runtime.TransportInterfaces;
            foreach (KeyValuePair<string, ITransportInterface> pair in interfaces)
            {
                fresh.Add(new LinkRowViewModel(pair.Value));
            }
        }

        MergeRows(fresh);

        int active = 0;
        int stopped = 0;
        int error = 0;
        for (int i = 0; i < Rows.Count; i++)
        {
            switch (Rows[i].State)
            {
                case LinkRowViewModel.RowState.Active:
                    active++;
                    break;
                case LinkRowViewModel.RowState.Stopped:
                    stopped++;
                    break;
                case LinkRowViewModel.RowState.Error:
                    error++;
                    break;
            }
        }

        SummaryText = active + " Active, " + stopped + " Stopped, " + error + " Error";
        UpdateDetail();
        StartSelectedCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
        ConnectLapbCommand.NotifyCanExecuteChanged();
        DisconnectLapbCommand.NotifyCanExecuteChanged();
    }

    // Keeps the line objects of links and interfaces that are still there (so the list does not
    // jump and the selection stays), and adds or removes the rest.
    private void MergeRows(List<LinkRowViewModel> fresh)
    {
        string? selectedKey = SelectedRow?.Key;

        bool sameSet = fresh.Count == Rows.Count;
        for (int i = 0; sameSet && i < fresh.Count; i++)
        {
            if (!string.Equals(fresh[i].Key, Rows[i].Key, StringComparison.Ordinal))
            {
                sameSet = false;
            }
        }

        if (sameSet)
        {
            // The engine's link or interface object is the same one each time, but a line made from a
            // link that was replaced (after an engine restart) would hold the old object; so the line is
            // replaced when its object differs.
            for (int i = 0; i < fresh.Count; i++)
            {
                bool sameObject = ReferenceEquals(fresh[i].Link, Rows[i].Link) &&
                                  ReferenceEquals(fresh[i].Transport, Rows[i].Transport);
                if (sameObject)
                {
                    Rows[i].Update();
                }
                else
                {
                    bool wasSelected = ReferenceEquals(SelectedRow, Rows[i]);
                    Rows[i] = fresh[i];
                    if (wasSelected)
                    {
                        SelectedRow = fresh[i];
                    }
                }
            }

            return;
        }

        Rows.Clear();
        LinkRowViewModel? reselect = null;
        for (int i = 0; i < fresh.Count; i++)
        {
            Rows.Add(fresh[i]);
            if (selectedKey != null && string.Equals(fresh[i].Key, selectedKey, StringComparison.Ordinal))
            {
                reselect = fresh[i];
            }
        }

        SelectedRow = reselect;
    }

    partial void OnSelectedRowChanged(LinkRowViewModel? value)
    {
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        LinkRowViewModel? row = SelectedRow;
        if (row == null)
        {
            DetailText = "Select a link to see its details.";
            return;
        }

        StringBuilder text = new StringBuilder();
        text.AppendLine("Name: " + row.Name);
        text.AppendLine("Kind: " + row.KindText);
        text.AppendLine("Status: " + row.StatusText);

        ILink? link = row.Link;
        if (link != null)
        {
            text.AppendLine("Remote machine: " + link.RemoteMachineId);
            text.AppendLine("Protocol: " + link.ProtocolMode.Mode);
            ILapbCapable? lapb = row.Lapb;
            if (lapb != null)
            {
                text.AppendLine();
                text.AppendLine("LAPB state: " + lapb.LapbState);
                (byte vs, byte vr, byte va) = lapb.GetSequenceNumbers();
                text.AppendLine("V(S)=" + vs + "  V(R)=" + vr + "  V(A)=" + va);
            }
            else
            {
                text.AppendLine();
                text.AppendLine("This link has no LAPB of its own.");
            }
        }
        else if (row.Transport != null)
        {
            ITransportInterface transport = row.Transport;
            if (transport is SerialTransport serial)
            {
                text.AppendLine("Data link: " + MainWindowViewModel.DescribeSerialDataLink(serial));
            }
            else
            {
                text.AppendLine("This interface carries X.25 over TCP with no LAPB (RFC 1613).");
            }
        }

        text.AppendLine();
        text.AppendLine("Counters are in the Status window (Ctrl+1).");
        DetailText = text.ToString();
    }

    private bool CanStartSelected()
    {
        return SelectedRow != null && SelectedRow.State == LinkRowViewModel.RowState.Stopped;
    }

    private bool CanStopSelected()
    {
        return SelectedRow != null && SelectedRow.State != LinkRowViewModel.RowState.Stopped;
    }

    private bool CanConnectLapb()
    {
        LinkRowViewModel? row = SelectedRow;
        return row != null && row.Lapb != null && row.State == LinkRowViewModel.RowState.Active &&
               row.Lapb.LapbState == LapbState.Disconnected;
    }

    private bool CanDisconnectLapb()
    {
        LinkRowViewModel? row = SelectedRow;
        return row != null && row.Lapb != null &&
               (row.Lapb.LapbState == LapbState.DataTransfer || row.Lapb.LapbState == LapbState.Connecting);
    }

    /// <summary>
    /// Starts the selected link or interface. Only a stopped one can be started; one in error is
    /// stopped first.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartSelected))]
    public void StartSelected()
    {
        LinkRowViewModel? row = SelectedRow;
        if (row == null)
        {
            return;
        }

        bool started;
        if (row.Link != null)
        {
            started = _runtimeService.Runtime.StartLink(row.Name);
        }
        else
        {
            started = row.Transport!.Start();
        }

        ActionText = started ? "Started " + row.Name : "Could not start " + row.Name + " (see the log window)";
        Logger.LogInformation("Link Status: start {Name}: {Result}", row.Name, started);
        Refresh();
    }

    /// <summary>
    /// Stops the selected link or interface.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopSelected))]
    public void StopSelected()
    {
        LinkRowViewModel? row = SelectedRow;
        if (row == null)
        {
            return;
        }

        if (row.Link != null)
        {
            _runtimeService.Runtime.StopLink(row.Name);
        }
        else
        {
            row.Transport!.Stop();
        }

        ActionText = "Stopped " + row.Name;
        Logger.LogInformation("Link Status: stop {Name}", row.Name);
        Refresh();
    }

    /// <summary>
    /// Starts LAPB link set-up on the selected link (sends SABM). Works only on a link that has its
    /// own LAPB, is running and has LAPB disconnected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnectLapb))]
    public void ConnectLapb()
    {
        LinkRowViewModel? row = SelectedRow;
        if (row?.Lapb == null)
        {
            return;
        }

        row.Lapb.Connect();
        ActionText = "LAPB connect sent on " + row.Name;
        Logger.LogInformation("Link Status: LAPB connect {Name}", row.Name);
        Refresh();
    }

    /// <summary>
    /// Starts LAPB link tear-down on the selected link (sends DISC).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDisconnectLapb))]
    public void DisconnectLapb()
    {
        LinkRowViewModel? row = SelectedRow;
        if (row?.Lapb == null)
        {
            return;
        }

        row.Lapb.Disconnect();
        ActionText = "LAPB disconnect sent on " + row.Name;
        Logger.LogInformation("Link Status: LAPB disconnect {Name}", row.Name);
        Refresh();
    }

    /// <summary>
    /// Stops the refresh timer.
    /// </summary>
    public override void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        base.Dispose();
    }
}
