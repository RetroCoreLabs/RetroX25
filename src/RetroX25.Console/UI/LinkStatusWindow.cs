using System;
using System.Collections.Generic;
using Terminal.Gui;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Link;
using RetroIO.Link.Lapb;
using RetroIO.Protocols.X25.Layers;
using RetroIO.Protocols.X25.Runtime.Layers;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Link Status Monitor Window - displays all links with LAPB states and statistics
    /// Shows active TCP-LAPB, XOT, and Serial links with their current status
    /// </summary>
    public class LinkStatusWindow : RefreshableWindow
    {
        // UI Controls
        private ListView _linkListView = null!;
        private TextView _linkDetailsView = null!;
        private Label _summaryLabel = null!;
        private Label _bytesLabel = null!;
        private Button _refreshButton = null!;
        private Button _startButton = null!;
        private Button _stopButton = null!;
        private Button _connectButton = null!;
        private Button _disconnectButton = null!;
        private Button _exitButton = null!;

        // Data
        private List<ILink> _links = new List<ILink>();
        private int _selectedLinkIndex = -1;

        public LinkStatusWindow(ProtocolRuntime runtime, ILogger logger)
            : base("Link Status Monitor (Ctrl+L)", runtime, logger)
        {
            X = 5;
            Y = 3;
            Width = 80;
            Height = 25;

            CreateControls();
            SetupEventHandlers();
            StartRefreshTimer();
        }

        private void CreateControls()
        {
            // Title
            Add(new Label("Multi-Link Status Monitor") { X = 1, Y = 1 });

            // Control buttons row 1
            _refreshButton = CreateRefreshButton(1, 2);
            Add(_refreshButton);

            _startButton = new Button("Start") { X = 12, Y = 2 };
            Add(_startButton);

            _stopButton = new Button("Stop") { X = 22, Y = 2 };
            Add(_stopButton);

            _exitButton = CreateExitButton(32, 2);
            Add(_exitButton);

            // Control buttons row 2 (LAPB control)
            _connectButton = new Button("LAPB Connect") { X = 1, Y = 3 };
            Add(_connectButton);

            _disconnectButton = new Button("LAPB Disconnect") { X = 18, Y = 3 };
            Add(_disconnectButton);

            // Summary labels
            _summaryLabel = new Label("Status: 0 Active, 0 Stopped, 0 Error") { X = 1, Y = 5 };
            Add(_summaryLabel);

            _bytesLabel = new Label("Total: 0 bytes RX | 0 bytes TX") { X = 1, Y = 6 };
            Add(_bytesLabel);

            // Links list
            Add(new Label("Links:") { X = 1, Y = 8 });
            _linkListView = new ListView()
            {
                X = 1,
                Y = 9,
                Width = 35,
                Height = Dim.Fill() - 2
            };
            Add(_linkListView);

            // Link details view
            Add(new Label("Details:") { X = 38, Y = 8 });
            _linkDetailsView = new TextView()
            {
                X = 38,
                Y = 9,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 2,
                ReadOnly = true,
                WordWrap = true
            };
            Add(_linkDetailsView);
        }

        private void SetupEventHandlers()
        {
            _linkListView.SelectedItemChanged += args =>
            {
                _selectedLinkIndex = args.Item;
                UpdateDetailsView();
            };

            _startButton.Clicked += () => SafeInvoke(OnStartClicked);
            _stopButton.Clicked += () => SafeInvoke(OnStopClicked);
            _connectButton.Clicked += () => SafeInvoke(OnConnectClicked);
            _disconnectButton.Clicked += () => SafeInvoke(OnDisconnectClicked);
        }

        private void OnStartClicked()
        {
            if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _links.Count)
            {
                var link = _links[_selectedLinkIndex];
                _runtime.StartLink(link.Name);
                OnRefresh();
            }
        }

        private void OnStopClicked()
        {
            if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _links.Count)
            {
                var link = _links[_selectedLinkIndex];
                _runtime.StopLink(link.Name);
                OnRefresh();
            }
        }

        private void OnConnectClicked()
        {
            if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _links.Count)
            {
                var link = _links[_selectedLinkIndex];
                if (link is ILapbCapable lapbLink)
                {
                    if (link is TcpLapbLink tcpLink)
                    {
                        tcpLink.Connect();
                    }
                    OnRefresh();
                }
            }
        }

        private void OnDisconnectClicked()
        {
            if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _links.Count)
            {
                var link = _links[_selectedLinkIndex];
                if (link is ILapbCapable lapbLink)
                {
                    if (link is TcpLapbLink tcpLink)
                    {
                        tcpLink.Disconnect();
                    }
                    OnRefresh();
                }
            }
        }

        protected override void OnRefresh()
        {
            try
            {
                // Get all links
                _links = new List<ILink>(_runtime.GetAllLinks());

                // Update summary
                var stats = _runtime.GetLinkManagerStatistics();
                _summaryLabel.Text = $"Status: {stats.ActiveLinks} Active, {stats.StoppedLinks} Stopped, {stats.ErrorLinks} Error";
                _bytesLabel.Text = $"Total: {FormatBytes(stats.TotalBytesReceived)} RX | {FormatBytes(stats.TotalBytesTransmitted)} TX";

                // Update list view
                var linkStrings = new List<string>();
                for (int i = 0; i < _links.Count; i++)
                {
                    var link = _links[i];
                    string statusChar = link.Status switch
                    {
                        LinkStatus.Active => "*",
                        LinkStatus.Error => "!",
                        _ => " "
                    };

                    string lapbState = "";
                    if (link is ILapbCapable lapb)
                    {
                        lapbState = $" [{lapb.LapbState}]";
                    }

                    linkStrings.Add($"[{statusChar}] {link.Name} - {link.Status}{lapbState}");
                }

                _linkListView.SetSource(linkStrings);

                // Restore selection if valid
                if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _links.Count)
                {
                    _linkListView.SelectedItem = _selectedLinkIndex;
                }

                UpdateDetailsView();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing link status");
            }
        }

        private void UpdateDetailsView()
        {
            if (_selectedLinkIndex < 0 || _selectedLinkIndex >= _links.Count)
            {
                _linkDetailsView.Text = "Select a link to view details";
                return;
            }

            var link = _links[_selectedLinkIndex];
            var stats = link.GetStatistics();

            var details = new System.Text.StringBuilder();
            details.AppendLine($"Name: {link.Name}");
            details.AppendLine($"Type: {link.Type}");
            details.AppendLine($"Status: {link.Status}");
            details.AppendLine($"Remote: {link.RemoteMachineId}");
            details.AppendLine($"Protocol: {link.ProtocolMode.Mode}");
            details.AppendLine();

            if (link is ILapbCapable lapb)
            {
                details.AppendLine($"LAPB State: {lapb.LapbState}");

                if (link is TcpLapbLink tcpLink)
                {
                    var (vs, vr, va) = tcpLink.GetSequenceNumbers();
                    details.AppendLine($"V(S)={vs} V(R)={vr} V(A)={va}");

                    var lapbStats = tcpLink.GetLapbStatistics();
                    details.AppendLine($"I-Frames TX: {lapbStats.IFramesSent}");
                    details.AppendLine($"I-Frames RX: {lapbStats.IFramesReceived}");
                    details.AppendLine($"S-Frames TX: {lapbStats.SFramesSent}");
                    details.AppendLine($"S-Frames RX: {lapbStats.SFramesReceived}");
                    details.AppendLine($"U-Frames TX: {lapbStats.UFramesSent}");
                    details.AppendLine($"U-Frames RX: {lapbStats.UFramesReceived}");
                    details.AppendLine($"Retransmits: {lapbStats.Retransmissions}");
                }
                details.AppendLine();
            }

            details.AppendLine("--- Statistics ---");
            details.AppendLine($"Bytes TX: {FormatBytes(stats.BytesTransmitted)}");
            details.AppendLine($"Bytes RX: {FormatBytes(stats.BytesReceived)}");
            details.AppendLine($"Frames TX: {stats.FramesTransmitted}");
            details.AppendLine($"Frames RX: {stats.FramesReceived}");
            details.AppendLine($"Errors: {stats.Errors}");

            _linkDetailsView.Text = details.ToString();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
