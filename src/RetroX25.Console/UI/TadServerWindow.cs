using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Terminal.Gui;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Sintran;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// TAD Server Window for managing multiple PAD client connections
    /// Displays connected PADs with their logical channels, remote IDs, and statistics
    /// Connected to the built-in LocalTadProtocol on address "200"
    /// </summary>
    public class TadServerWindow : Window
    {
        private readonly ProtocolRuntime _runtime;
        private readonly ILogger _logger;
        private readonly LocalTadProtocol _localTadProtocol;
        
        // UI Controls
        private ListView _clientListView = null!;
        private TextView _clientDetailsView = null!;
        private TextView _x3ParametersView = null!;
        private Label _serverStatusLabel = null!;
        private Label _connectionStatsLabel = null!;
        private Button _disconnectClientButton = null!;
        private Button _refreshButton = null!;
        private Button _exitButton = null!;
        
        // Data
        private List<LocalTadSessionInfo> _connectedClients = new List<LocalTadSessionInfo>();
        private System.Threading.Timer? _refreshTimer;
        
        public TadServerWindow(ProtocolRuntime runtime, ILogger logger) : base("Local TAD Server - Built-in Address 200")
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localTadProtocol = runtime.GetLocalTadProtocol();
            
            X = 5;
            Y = 5;
            Width = Dim.Fill() - 4;  // Resizable width
            Height = Dim.Fill() - 4;  // Resizable height  
            ColorScheme = Colors.Dialog;
            Modal = false;
            
            CreateControls();
            SetupEventHandlers();
            StartRefreshTimer();
        }
        
        private void CreateControls()
        {
            // Server info (no configuration needed - built-in server)
            Add(new Label("Built-in LocalTAD Protocol Server (Address: 200)") { X = 1, Y = 1 });
            
            // Control buttons
            _refreshButton = new Button("Refresh") { X = 1, Y = 2 };
            Add(_refreshButton);
            
            _exitButton = new Button("Exit") { X = 12, Y = 2 };
            Add(_exitButton);
            
            // Server status
            _serverStatusLabel = new Label("Status: Always Running") { X = 1, Y = 4 };
            Add(_serverStatusLabel);
            
            _connectionStatsLabel = new Label("Active Sessions: 0") { X = 30, Y = 4 };
            Add(_connectionStatsLabel);
            
            // Connected clients list
            Add(new Label("Active PAD Sessions:") { X = 1, Y = 6 });
            _clientListView = new ListView()
            {
                X = 1,
                Y = 7,
                Width = 65,
                Height = 25
            };
            Add(_clientListView);
            
            // Client details panel
            Add(new Label("Session Details:") { X = 68, Y = 6 });
            _clientDetailsView = new TextView()
            {
                X = 68,
                Y = 7,
                Width = 70,
                Height = 15,
                ReadOnly = true
            };
            Add(_clientDetailsView);

            // X.3 Parameters panel
            Add(new Label("X.3 Parameters:") { X = 68, Y = 23 });
            _x3ParametersView = new TextView()
            {
                X = 68,
                Y = 24,
                Width = 70,
                Height = 8,
                ReadOnly = true
            };
            Add(_x3ParametersView);
            
            // Action buttons for selected client
            _disconnectClientButton = new Button("Disconnect Selected")
            {
                X = 1,
                Y = 33,
                Enabled = false
            };
            Add(_disconnectClientButton);
            
            // Help text
            Add(new Label("LocalTAD Protocol handles PAD connections to address 200") 
                { X = 1, Y = 36, Width = 60 });
            Add(new Label("Select a session from the list to view detailed information") 
                { X = 1, Y = 37, Width = 60 });
        }
        
        private void SetupEventHandlers()
        {
            _refreshButton.Clicked += OnRefresh;
            _exitButton.Clicked += OnExit;
            _disconnectClientButton.Clicked += OnDisconnectClient;
            
            _clientListView.SelectedItemChanged += OnClientSelected;
            
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
            RefreshClientList();
            UpdateConnectionStats();
        }
        
        private void OnExit()
        {
            StopRefreshTimer();
            this.Visible = false;
            Application.Top.Remove(this);
        }
        
        private void OnDisconnectClient()
        {
            if (_clientListView.SelectedItem >= 0 && _clientListView.SelectedItem < _connectedClients.Count)
            {
                var session = _connectedClients[_clientListView.SelectedItem];
                try
                {
                    var success = _localTadProtocol.DisconnectSession(session.LCN);
                    if (success)
                    {
                        _logger.LogInformation($"Disconnected LocalTAD session LCN {session.LCN}");
                        RefreshClientList(); // Update the list immediately
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to disconnect LocalTAD session LCN {session.LCN} - session not found");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error disconnecting LocalTAD session LCN {session.LCN}");
                }
            }
        }
        
        private void OnClientSelected(ListViewItemEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                var hasSelection = e.Item >= 0 && e.Item < _connectedClients.Count;
                _disconnectClientButton.Enabled = hasSelection;
                
                if (hasSelection)
                {
                    var session = _connectedClients[e.Item];
                    ShowSessionDetails(session);
                    ShowX3Parameters(session);
                }
                else
                {
                    _clientDetailsView.Text = "";
                    _x3ParametersView.Text = "";
                }
            });
        }
        
        private void RefreshClientList()
        {
            Application.MainLoop.Invoke(() =>
            {
                // Store currently selected LCN to preserve selection across refresh
                ushort? selectedLCN = null;
                if (_clientListView.SelectedItem >= 0 && _clientListView.SelectedItem < _connectedClients.Count)
                {
                    selectedLCN = _connectedClients[_clientListView.SelectedItem].LCN;
                }
                
                // Get current sessions from LocalTadProtocol
                var sessions = _localTadProtocol.GetActiveSessions();
                _connectedClients.Clear();
                _connectedClients.AddRange(sessions);
                
                var items = new List<string>(_connectedClients.Count);
                for (int i = 0; i < _connectedClients.Count; i++)
                {
                    var s = _connectedClients[i];
                    items.Add($"[LCN {s.LCN:D2}] Address: {s.CalledAddress} - {s.Duration:hh\\:mm\\:ss}");
                }
                _clientListView.SetSource(items);
                
                // Restore selection if the same LCN still exists
                if (selectedLCN.HasValue)
                {
                    for (int i = 0; i < _connectedClients.Count; i++)
                    {
                        if (_connectedClients[i].LCN == selectedLCN.Value)
                        {
                            _clientListView.SelectedItem = i;
                            break;
                        }
                    }
                }
            });
        }
        
        private void ShowSessionDetails(LocalTadSessionInfo session)
        {
            var details = $"LocalTAD Session Details\n" +
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
                         
            _clientDetailsView.Text = details;
        }
        
        private void ShowX3Parameters(LocalTadSessionInfo session)
        {
            try
            {
                // Get X.3 parameters from the TAD server for this session
                var parameters = _localTadProtocol.GetSessionX3Parameters(session.SessionId);
                
                if (parameters != null && parameters.Count > 0)
                {
                    var details = "X.3 Parameter Settings\n" +
                                 "======================\n\n";
                                 
                    // Show key parameters with descriptions
                    var keyParams = new[]
                    {
                        (byte)1,  // ESCAPE
                        (byte)2,  // ECHO
                        (byte)3,  // DATA_FORWARDING
                        (byte)4,  // IDLE_TIMER
                        (byte)12, // FORWARD_ON_N_CHARACTERS
                        (byte)15, // EDITING
                        (byte)16, // CHARACTER_DELETE
                        (byte)17, // LINE_DELETE
                    };
                    
                    foreach (var paramNum in keyParams)
                    {
                        if (parameters.TryGetValue(paramNum, out var value))
                        {
                            var desc = RetroIO.Protocols.X25.Models.X3Parameters.GetParameterDescription(paramNum);
                            details += $"P{paramNum:D2}: {value,3} - {desc}\n";
                        }
                    }
                    
                    details += "\nAll Parameters:\n";
                    var sortedKeys = new List<byte>(parameters.Keys);
                    sortedKeys.Sort();
                    for (int i = 0; i < sortedKeys.Count; i++)
                    {
                        var key = sortedKeys[i];
                        var value = parameters[key];
                        details += $"P{key:D2}:{value,3} ";
                        if ((key % 8) == 0) details += "\n";
                    }
                    
                    _x3ParametersView.Text = details;
                }
                else
                {
                    _x3ParametersView.Text = "X.3 parameters not available\nfor this session.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving X.3 parameters for session {SessionId}", session.SessionId);
                _x3ParametersView.Text = $"Error loading X.3 parameters:\n{ex.Message}";
            }
        }
        
        private void UpdateConnectionStats()
        {
            Application.MainLoop.Invoke(() =>
            {
                var activeSessions = _connectedClients.Count;
                _connectionStatsLabel.Text = $"Active Sessions: {activeSessions}";
            });
        }
        
        private void StartRefreshTimer()
        {
            _refreshTimer = new System.Threading.Timer((_) =>
            {
                Application.MainLoop.Invoke(() =>
                {
                    // Update session durations and refresh selected session details
                    RefreshClientList(); // This will get fresh data from LocalTadProtocol
                    UpdateConnectionStats();
                    
                    if (_clientListView.SelectedItem >= 0 && _clientListView.SelectedItem < _connectedClients.Count)
                    {
                        var selectedSession = _connectedClients[_clientListView.SelectedItem];
                        ShowSessionDetails(selectedSession);
                        ShowX3Parameters(selectedSession);
                    }
                });
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)); // Update every 2 seconds
        }
        
        private void StopRefreshTimer()
        {
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }
    }
}