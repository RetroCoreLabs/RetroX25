using System;
using System.Collections.Generic;
using Terminal.Gui;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Sintran;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// SINTRAN TAD Server Window
    /// Displays active SINTRAN TAD terminal connections with auto-assigned Terminal IDs
    /// Monitors connections to addresses 100, 101, 102
    /// </summary>
    public class SintranTadServerWindow : Window
    {
        private readonly ProtocolRuntime _runtime;
        private readonly ILogger _logger;
        private readonly SintranPvcServer _pvcServer;

        // UI Controls
        private ListView _sessionListView = null!;
        private TextView _sessionDetailsView = null!;
        private Label _serverStatusLabel = null!;
        private Label _terminalStatsLabel = null!;
        private Button _refreshButton = null!;
        private Button _exitButton = null!;
        private Button _disconnectButton = null!;

        // Data
        private List<SintranTadSessionDisplay> _activeSessions = new List<SintranTadSessionDisplay>();
        private System.Threading.Timer? _refreshTimer;

        public SintranTadServerWindow(ProtocolRuntime runtime, ILogger logger) : base("SINTRAN TAD Server - Addresses 100, 101, 102")
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pvcServer = runtime.GetSintranPvcServer();

            X = 5;
            Y = 5;
            Width = Dim.Fill() - 4;
            Height = Dim.Fill() - 4;
            ColorScheme = Colors.Dialog;
            Modal = false;

            CreateControls();
            SetupEventHandlers();
            StartRefreshTimer();
        }

        private void CreateControls()
        {
            // Server info
            Add(new Label("SINTRAN TAD Protocol Server (Terminal IDs: 1-8)") { X = 1, Y = 1 });

            // Control buttons
            _refreshButton = new Button("Refresh") { X = 1, Y = 2 };
            Add(_refreshButton);

            _exitButton = new Button("Exit") { X = 12, Y = 2 };
            Add(_exitButton);

            // Server status
            _serverStatusLabel = new Label("Status: Running") { X = 1, Y = 4 };
            Add(_serverStatusLabel);

            _terminalStatsLabel = new Label("Terminals: 8/8 Available") { X = 40, Y = 4 };
            Add(_terminalStatsLabel);

            // Active sessions list
            Add(new Label("Active SINTRAN TAD Sessions:") { X = 1, Y = 6 });
            _sessionListView = new ListView()
            {
                X = 1,
                Y = 7,
                Width = 70,
                Height = 20
            };
            Add(_sessionListView);

            // Session details panel
            Add(new Label("Session Details:") { X = 73, Y = 6 });
            _sessionDetailsView = new TextView()
            {
                X = 73,
                Y = 7,
                Width = Dim.Fill() - 74,
                Height = 20,
                ReadOnly = true
            };
            Add(_sessionDetailsView);

            // Action buttons
            _disconnectButton = new Button("Disconnect Selected")
            {
                X = 1,
                Y = 28,
                Enabled = false
            };
            Add(_disconnectButton);

            // Help text
            Add(new Label("SINTRAN TAD server with automatic terminal ID assignment (1-8)")
                { X = 1, Y = 31, Width = 60 });
            Add(new Label("Select a session to view terminal ID and routing details")
                { X = 1, Y = 32, Width = 60 });
        }

        private void SetupEventHandlers()
        {
            _refreshButton.Clicked += OnRefresh;
            _exitButton.Clicked += OnExit;
            _disconnectButton.Clicked += OnDisconnect;

            _sessionListView.SelectedItemChanged += OnSessionSelected;

            // ESC key to exit
            KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Esc)
                {
                    OnExit();
                    e.Handled = true;
                }
            };
        }

        private void OnRefresh()
        {
            RefreshSessionList();
            UpdateStats();
        }

        private void OnExit()
        {
            StopRefreshTimer();
            this.Visible = false;
            Application.Top.Remove(this);
        }

        private void OnDisconnect()
        {
            if (_sessionListView.SelectedItem >= 0 && _sessionListView.SelectedItem < _activeSessions.Count)
            {
                var session = _activeSessions[_sessionListView.SelectedItem];
                try
                {
                    _logger.LogInformation("Disconnecting SINTRAN TAD session LCN {LCN}, Terminal ID {TerminalId}",
                        session.LCN, session.TerminalId);
                    // TODO: Implement disconnect via PVC server
                    RefreshSessionList();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disconnecting SINTRAN TAD session LCN {LCN}", session.LCN);
                }
            }
        }

        private void OnSessionSelected(ListViewItemEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                var hasSelection = e.Item >= 0 && e.Item < _activeSessions.Count;
                _disconnectButton.Enabled = hasSelection;

                if (hasSelection)
                {
                    var session = _activeSessions[e.Item];
                    ShowSessionDetails(session);
                }
                else
                {
                    _sessionDetailsView.Text = "";
                }
            });
        }

        private void RefreshSessionList()
        {
            Application.MainLoop.Invoke(() =>
            {
                // Store currently selected LCN
                ushort? selectedLCN = null;
                if (_sessionListView.SelectedItem >= 0 && _sessionListView.SelectedItem < _activeSessions.Count)
                {
                    selectedLCN = _activeSessions[_sessionListView.SelectedItem].LCN;
                }

                // Get active handlers from PVC server
                var handlers = _pvcServer.GetActiveHandlers();
                _activeSessions.Clear();

                foreach (var kvp in handlers)
                {
                    if (kvp.Value.Protocol == "TAD")
                    {
                        var stats = kvp.Value.GetStats();
                        var terminalId = _pvcServer.GetTerminalId(kvp.Key);
                        var duration = DateTime.UtcNow - stats.LastActivity;

                        _activeSessions.Add(new SintranTadSessionDisplay
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

                // Update list view
                var items = new List<string>(_activeSessions.Count);
                for (int i = 0; i < _activeSessions.Count; i++)
                {
                    var s = _activeSessions[i];
                    items.Add($"[LCN {s.LCN:D2}] Terminal ID {s.TerminalId} - {s.Status} - Rx:{s.MessagesRx} Tx:{s.MessagesTx}");
                }
                _sessionListView.SetSource(items);

                // Restore selection
                if (selectedLCN.HasValue)
                {
                    for (int i = 0; i < _activeSessions.Count; i++)
                    {
                        if (_activeSessions[i].LCN == selectedLCN.Value)
                        {
                            _sessionListView.SelectedItem = i;
                            break;
                        }
                    }
                }
            });
        }

        private void ShowSessionDetails(SintranTadSessionDisplay session)
        {
            var details = $"SINTRAN TAD Session\n" +
                         $"===================\n\n" +
                         $"Terminal ID: {session.TerminalId}\n" +
                         $"Logical Channel: {session.LCN}\n" +
                         $"Protocol: {session.Protocol}\n\n" +
                         $"Session Status:\n" +
                         $"{session.Status}\n\n" +
                         $"Statistics:\n" +
                         $"Messages RX: {session.MessagesRx}\n" +
                         $"Messages TX: {session.MessagesTx}\n" +
                         $"Bytes RX: {session.BytesRx:N0}\n" +
                         $"Bytes TX: {session.BytesTx:N0}\n\n" +
                         $"Last Activity:\n" +
                         $"{session.LastActivity:yyyy-MM-dd HH:mm:ss}\n\n" +
                         $"SINTRAN Routing:\n" +
                         $"Frame format: 0x21 0x13 0xDD\n" +
                         $"TAD protocol with auto terminal ID";

            _sessionDetailsView.Text = details;
        }

        private void UpdateStats()
        {
            Application.MainLoop.Invoke(() =>
            {
                var stats = _pvcServer.GetStats();
                var available = stats.AvailableTerminals;
                var total = stats.TotalTerminals;
                var used = total - available;

                _terminalStatsLabel.Text = $"Terminals: {used}/{total} In Use, {available} Available";
                _serverStatusLabel.Text = $"Status: Running ({stats.TotalHandlers} active handlers)";
            });
        }

        private void StartRefreshTimer()
        {
            _refreshTimer = new System.Threading.Timer((_) =>
            {
                Application.MainLoop.Invoke(() =>
                {
                    RefreshSessionList();
                    UpdateStats();

                    if (_sessionListView.SelectedItem >= 0 && _sessionListView.SelectedItem < _activeSessions.Count)
                    {
                        var selectedSession = _activeSessions[_sessionListView.SelectedItem];
                        ShowSessionDetails(selectedSession);
                    }
                });
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        }

        private void StopRefreshTimer()
        {
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }
    }

    /// <summary>
    /// Display data for SINTRAN TAD session
    /// </summary>
    internal class SintranTadSessionDisplay
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
}
