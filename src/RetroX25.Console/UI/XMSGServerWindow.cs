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
    /// XMSG Server Window for managing XMSG connections and services
    /// Displays connected XMSG clients with their logical channels, ports, and service types
    /// Connected to the built-in LocalXMSGProtocol for COSMOS File Services
    /// </summary>
    public class XMSGServerWindow : RefreshableWindow
    {
        private readonly LocalXMSGProtocol _localXmsgProtocol;
        
        // UI Controls
        private ListView _sessionListView = null!;
        private TextView _sessionDetailsView = null!;
        private Label _serverStatusLabel = null!;
        private Label _connectionStatsLabel = null!;
        private Label _serviceStatsLabel = null!;
        private Button _refreshButton = null!;
        private Button _exitButton = null!;
        private Button _disconnectSessionButton = null!;
        private Button _clearSessionsButton = null!;
        private Button _viewServicesButton = null!;
        
        // Data
        private List<XMSGSessionInfo> _activeSessions = new List<XMSGSessionInfo>();
        private int _selectedSessionIndex = -1;
        
        public XMSGServerWindow(ProtocolRuntime runtime, ILogger logger) : base("Local XMSG Server - COSMOS File Services", runtime, logger)
        {
            _localXmsgProtocol = runtime.GetLocalXMSGProtocol();
            
            X = 5;
            Y = 5;
            
            CreateControls();
            SetupEventHandlers();
            StartRefreshTimer();
        }
        
        private void CreateControls()
        {
            // Server info
            Add(new Label("Built-in LocalXMSG Protocol Server - COSMOS File Services") { X = 1, Y = 1 });
            
            // Control buttons
            _refreshButton = CreateRefreshButton(1, 2);
            Add(_refreshButton);
            
            _viewServicesButton = new Button("View Services") { X = 12, Y = 2 };
            Add(_viewServicesButton);
            
            _exitButton = CreateExitButton(28, 2);
            Add(_exitButton);
            
            // Server status
            _serverStatusLabel = new Label("Status: Always Running") { X = 1, Y = 4 };
            Add(_serverStatusLabel);
            
            _connectionStatsLabel = new Label("Active Sessions: 0") { X = 30, Y = 4 };
            Add(_connectionStatsLabel);
            
            _serviceStatsLabel = new Label("Services: File(1000), RPC(1001), Monitor(1002)") { X = 1, Y = 5 };
            Add(_serviceStatsLabel);
            
            // Active sessions list
            Add(new Label("Active XMSG Sessions:") { X = 1, Y = 7 });
            _sessionListView = new ListView()
            {
                X = 1,
                Y = 8,
                Width = Dim.Fill() - 1,
                Height = Dim.Percent(60)
            };
            Add(_sessionListView);
            
            // Session management buttons
            _disconnectSessionButton = new Button("Disconnect Session") { X = 1, Y = Pos.Bottom(_sessionListView) + 1 };
            Add(_disconnectSessionButton);
            
            _clearSessionsButton = new Button("Clear All Sessions") { X = 21, Y = Pos.Bottom(_sessionListView) + 1 };
            Add(_clearSessionsButton);
            
            // Session details view
            Add(new Label("Session Details:") { X = 1, Y = Pos.Bottom(_sessionListView) + 3 });
            _sessionDetailsView = new TextView()
            {
                X = 1,
                Y = Pos.Bottom(_sessionListView) + 4,
                Width = Dim.Fill() - 1,
                Height = Dim.Percent(30),
                ReadOnly = true,
                WordWrap = true
            };
            Add(_sessionDetailsView);
            
            UpdateSessionList();
        }
        
        private void SetupEventHandlers()
        {
            _disconnectSessionButton.Clicked += OnDisconnectSessionClicked;
            _clearSessionsButton.Clicked += OnClearSessionsClicked;
            _viewServicesButton.Clicked += OnViewServicesClicked;
            
            _sessionListView.SelectedItemChanged += OnSessionSelectionChanged;
        }
        
        protected override void OnRefresh()
        {
            UpdateSessionList();
            UpdateConnectionStats();
        }
        
        private void UpdateSessionList()
        {
            try
            {
                _activeSessions = _localXmsgProtocol.GetSessionInfo();
                
                var items = new string[_activeSessions.Count];
                for (int i = 0; i < _activeSessions.Count; i++)
                {
                    var session = _activeSessions[i];
                    items[i] = $"LCN={session.LCN:D3} Port={session.Port:X4} {session.GetPortDescription()} " +
                               $"From={session.CallingAddress} To={session.CalledAddress} " +
                               $"Session={session.SessionId:X8} State={session.State} " +
                               $"Duration={session.Duration:mm\\:ss}";
                }

                _sessionListView.SetSource(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating XMSG session list");
            }
        }
        
        private void UpdateConnectionStats()
        {
            try
            {
                var activeCount = _localXmsgProtocol.ActiveSessionsCount;
                _connectionStatsLabel.Text = $"Active Sessions: {activeCount}";
                
                // Update service statistics
                int fileSessions = 0, rpcSessions = 0, monitorSessions = 0, otherSessions = 0;
                for (int i = 0; i < _activeSessions.Count; i++)
                {
                    var port = _activeSessions[i].Port;
                    if (port == 0x1000) fileSessions++;
                    else if (port == 0x1001) rpcSessions++;
                    else if (port == 0x1002) monitorSessions++;
                    else otherSessions++;
                }

                _serviceStatsLabel.Text = $"File({fileSessions}) RPC({rpcSessions}) Monitor({monitorSessions}) Other({otherSessions})";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating XMSG connection stats");
            }
        }
        
        private void UpdateSessionDetails()
        {
            try
            {
                if (_selectedSessionIndex >= 0 && _selectedSessionIndex < _activeSessions.Count)
                {
                    var session = _activeSessions[_selectedSessionIndex];
                    var details = $"XMSG Session Details\\n" +
                                $"====================\\n\\n" +
                                $"Logical Channel: {session.LCN}\\n" +
                                $"Service Port: 0x{session.Port:X4} ({session.Port})\\n" +
                                $"Service Type: {session.GetPortDescription()}\\n" +
                                $"Service Name: {session.ServiceName}\\n" +
                                $"Session ID: 0x{session.SessionId:X8}\\n" +
                                $"State: {session.State}\\n" +
                                $"Called Address: {session.CalledAddress}\\n" +
                                $"Calling Address: {session.CallingAddress}\\n" +
                                $"Start Time: {session.StartTime:yyyy-MM-dd HH:mm:ss} UTC\\n" +
                                $"Duration: {session.Duration:hh\\:mm\\:ss}\\n\\n" +
                                $"Protocol Information:\\n" +
                                $"- XMSG Version: L03 (0x4C03)\\n" +
                                $"- Message Format: 12-byte header + data\\n" +
                                $"- Service Registry: Built-in COSMOS services\\n\\n" +
                                $"Available Operations:\\n" +
                                $"- File Service: Read, Write, Copy operations\\n" +
                                $"- RPC Service: Remote procedure calls\\n" +
                                $"- System Monitor: Status and statistics\\n" +
                                $"- Session Management: Connect, Disconnect, Status\\n\\n" +
                                $"Last Updated: {DateTime.UtcNow:HH:mm:ss} UTC";
                    
                    _sessionDetailsView.Text = details;
                }
                else
                {
                    _sessionDetailsView.Text = "No session selected.\\n\\n" +
                                            "XMSG Services Available:\\n" +
                                            "=======================\\n\\n" +
                                            "0x1000 - COSMOS File Service\\n" +
                                            "  File operations: read, write, copy\\n" +
                                            "  SINTRAN file specification support\\n" +
                                            "  Block-based transfer with checksums\\n\\n" +
                                            "0x1001 - Remote Procedure Service\\n" +
                                            "  Remote procedure call execution\\n" +
                                            "  Parameter passing and return values\\n" +
                                            "  Error handling and exceptions\\n\\n" +
                                            "0x1002 - System Monitor\\n" +
                                            "  System status and statistics\\n" +
                                            "  Performance monitoring\\n" +
                                            "  Resource utilization\\n\\n" +
                                            "0x1003-0x10FF - User Services\\n" +
                                            "0x1100-0x1FFF - Reserved Services";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating XMSG session details");
                _sessionDetailsView.Text = $"Error updating session details: {ex.Message}";
            }
        }
        
        #region Event Handlers
        
        private void OnDisconnectSessionClicked()
        {
            if (_selectedSessionIndex >= 0 && _selectedSessionIndex < _activeSessions.Count)
            {
                var session = _activeSessions[_selectedSessionIndex];
                var result = MessageBox.Query("Disconnect Session", 
                    $"Disconnect XMSG session on LCN {session.LCN} (Port {session.Port:X4})?", 
                    "Yes", "No");
                
                if (result == 0)
                {
                    try
                    {
                        _localXmsgProtocol.HandleCallClearing(session.LCN);
                        _logger.LogInformation("XMSG session on LCN {LCN} disconnected by user", session.LCN);
                        UpdateSessionList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.ErrorQuery("Error", $"Failed to disconnect session: {ex.Message}", "OK");
                        _logger.LogError(ex, "Error disconnecting XMSG session on LCN {LCN}", session.LCN);
                    }
                }
            }
            else
            {
                MessageBox.ErrorQuery("Error", "No session selected", "OK");
            }
        }
        
        private void OnClearSessionsClicked()
        {
            var result = MessageBox.Query("Clear All Sessions", 
                $"Disconnect all {_activeSessions.Count} active XMSG sessions?", 
                "Yes", "No");
            
            if (result == 0)
            {
                try
                {
                    var sessionCount = _activeSessions.Count;
                    var sessionsCopy = new List<XMSGSessionInfo>(_activeSessions);
                    for (int i = 0; i < sessionsCopy.Count; i++)
                    {
                        _localXmsgProtocol.HandleCallClearing(sessionsCopy[i].LCN);
                    }
                    
                    _logger.LogInformation("All {Count} XMSG sessions cleared by user", sessionCount);
                    UpdateSessionList();
                    MessageBox.Query("Success", $"Cleared {sessionCount} XMSG sessions", "OK");
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to clear sessions: {ex.Message}", "OK");
                    _logger.LogError(ex, "Error clearing XMSG sessions");
                }
            }
        }
        
        private void OnViewServicesClicked()
        {
            var serviceInfo = "XMSG Service Registry\\n" +
                            "===================\\n\\n" +
                            "Port 0x1000 (4096) - COSMOS File Service\\n" +
                            "  Description: File transfer operations\\n" +
                            "  Operations: Read, Write, Copy\\n" +
                            "  Format: SINTRAN file specification\\n" +
                            "  Example: MACHINE(USER(PASSWORD)).(DIR:USER)FILE:TYPE;VER\\n\\n" +
                            "Port 0x1001 (4097) - Remote Procedure Service\\n" +
                            "  Description: Remote procedure calls\\n" +
                            "  Operations: Call, Return, Exception\\n" +
                            "  Parameters: Serialized data structures\\n\\n" +
                            "Port 0x1002 (4098) - System Monitor\\n" +
                            "  Description: System status and monitoring\\n" +
                            "  Operations: Status, Statistics, Performance\\n" +
                            "  Data: System metrics and health information\\n\\n" +
                            "Port Range 0x1003-0x10FF - User Services\\n" +
                            "  Description: Application-specific services\\n" +
                            "  Registration: Dynamic service registration\\n\\n" +
                            "Port Range 0x1100-0x1FFF - Reserved\\n" +
                            "  Description: Future XMSG service expansion\\n\\n" +
                            $"Currently Active Sessions: {_activeSessions.Count}\\n" +
                            $"Server Uptime: Running since startup\\n" +
                            $"Protocol Version: XMSG L03 (0x4C03)";
            
            MessageBox.Query("XMSG Services", serviceInfo, "OK");
        }
        
        private void OnSessionSelectionChanged(ListViewItemEventArgs e)
        {
            _selectedSessionIndex = e.Item;
            UpdateSessionDetails();
        }
        
        #endregion
    }
}