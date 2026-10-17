//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Lapb;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Common;
using RetroIO.Protocols.X25.Layers;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Runtime.Layers;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;
using RetroX25.Console.UI;
using Terminal.Gui;
using Terminal.Gui.Trees;

namespace RetroX25.Console
{
    public class X25Gui
    {
        private Window _mainWindow = null!;
        private Window _statusWindow = null!;
        private Window _packetWindow = null!;
        private PacketWindowV2 _packetWindowV2 = null!;
        private Window _logWindow = null!;

        // Status labels
        private Label _comPortStatus = null!;
        private Label _hdlcPacketsOk = null!;
        private Label _lapbStatus = null!;
        private Label _x25Status = null!;

        // Log window controls
        private TextView _logTextView = null!;
        private Button _logLevelFilter = null!;
        private Button _clearLogButton = null!;
        private Button _exportLogButton = null!;

        // Packet list
        private List<PacketInfo> _packetHistory = new List<PacketInfo>();

        // Enhanced packet display fields
        private ListView _packetListView = null!;
        private TextView _packetDetailsView = null!;
        // Filter controls
        private Button _filterAllButton = null!;
        private Button _filterTXButton = null!;
        private Button _filterRXButton = null!;
        private Button _filterIButton = null!;
        private Button _filterSButton = null!;
        private Button _filterUButton = null!;
        private Button _filterDataButton = null!;
        private Button _filterCallButton = null!;
        private Button _filterClearButton = null!;
        private Button _filterResetButton = null!;
        private Button _filterRestartButton = null!;
        private Button _filterIntButton = null!;
        private Button _filterLCNButton = null!;
        private Button _filterErrorsButton = null!;
        private Button _collapseToggleButton = null!;
        private Button _debugButton = null!;
        private Button _testButton = null!;
        private Label _filterStatsLabel = null!;
        private Button _exportPacketsButton = null!;

        // Filter state
        private string _currentDirectionFilter = "All";
        private bool _showIFrames = true;
        private bool _showSFrames = true;
        private bool _showUFrames = true;
        private bool _showDataFrames = true;
        private bool _showCallFrames = true;
        private bool _showClearFrames = true;
        private bool _showResetFrames = true;
        private bool _showRestartFrames = true;
        private bool _showIntFrames = true;
        private int? _currentLCNFilter = null;
        private bool _showErrorsOnly = false;


        // Protocol instances
        private ProtocolRuntime? _runtime;
        private PacketDisplayManager? _displayManager;
        private X25Layer? _x25;

        // Child windows
        private List<PadTestWindow> _padTestWindows = new List<PadTestWindow>();
        private List<SintranTadClientWindow> _sintranTadClientWindows = new List<SintranTadClientWindow>();
        private TadServerWindow? _tadServerWindow;
        private SintranTadServerWindow? _sintranTadServerWindow;
        private XMSGServerWindow? _xmsgServerWindow;
        private LinkStatusWindow? _linkStatusWindow;

        // Window management
        private class ManagedWindow
        {
            public Window Window { get; set; }
            public string Name { get; set; }
            public string HotKey { get; set; }
            public bool IsVisible => Window?.Visible == true;

            public ManagedWindow(Window window, string name, string hotKey = "")
            {
                Window = window;
                Name = name;
                HotKey = hotKey;
            }
        }

        private List<ManagedWindow> _managedWindows = new List<ManagedWindow>();
        private MenuBarItem _windowsMenuBarItem = null!;

        // Logging
        private Microsoft.Extensions.Logging.ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        private HistoryLoggingProvider? _historyProvider;
        private int _currentLogFilter = 0; // 0=All, 1=Debug, 2=Info, 3=Warning, 4=Error

        // Statistics - Now handled by ProtocolRuntime

        // Packet queue for thread-safe UI updates


        // Packet numbering
        private int _nextPacketNumber = 0;

        public void Run(ProtocolRuntime runtime, Microsoft.Extensions.Logging.ILogger logger, HistoryLoggingProvider historyProvider)
        {
            // Initialize Terminal.GUI
            Application.Init();

            try
            {
                // Store references
                _runtime = runtime;
                _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                _historyProvider = historyProvider;

                // Initialize packet display manager for collapse functionality
                _displayManager = new PacketDisplayManager(_runtime);

                // Get layer references from runtime
                _x25 = runtime.X25Layer;

                // Subscribe to ProtocolRuntime history events to capture all frames
                // This is the single source of truth for all protocol events
                _runtime.HistoryItemAdded += OnHistoryItemAdded;

                // Subscribe to collapse state change events
                _displayManager.CollapseStateChanged += OnCollapseStateChanged;

                // Set initial button text to match display manager state
                if (_collapseToggleButton != null)
                {
                    _collapseToggleButton.Text = _displayManager.GlobalCollapseEnabled ? "[Expand]" : "[Collapse]";
                }

                // Subscribe to X.25 events for call/data handling
                _x25.IncomingCall += OnIncomingCall;
                _x25.DataReceived += OnData;
                _x25.CallCleared += OnClear;
                _x25.CallReset += OnReset;
                _x25.RestartRequested += OnRestart;

                // ProtocolRuntime starts automatically in constructor
                _logger.LogInformation("ProtocolRuntime initialized");

                // Create GUI first
                CreateMainWindow();
                CreateStatusWindow();
                CreatePacketWindow();
                CreateLogWindow();

                // Set up global key bindings
                SetupKeyBindings();

                // Serial port initialization now handled by ProtocolRuntime/PhysicalLayer

                // Subscribe to history provider events for log updates
                _historyProvider.LogEntryAdded += OnLogEntryAdded;

                // PhysicalLayer will handle serial port reading automatically
                // No need to start separate serial reader thread

                // Start status update timer using Application.MainLoop.AddTimeout
                // Subscribe to statistics updates (event-driven)
                _runtime.StatisticsUpdated += OnStatisticsUpdated;

                _logger.LogInformation("Starting application main loop");

                // Run the application
                Application.Run();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Application error: {Message}", ex.Message);
            }
            finally
            {
                _logger?.LogInformation("Shutting down application");

                // Stop the ProtocolRuntime
                if (_runtime != null)
                {
                    _runtime.Dispose();
                    _logger?.LogInformation("ProtocolRuntime disposed");
                }

                // Shutdown Terminal.GUI
                Application.Shutdown();
            }
        }

        private MenuBarItem CreateWindowsMenu()
        {
            var menuItems = new List<MenuItem>
            {
                // Static window creation options
                new MenuItem("_Status Window", "F1", () => ShowStatusWindow()),
                new MenuItem("_Packet Window", "F2", () => ShowPacketWindow()),
                new MenuItem("Packet Window _V2", "F3", () => ShowPacketWindowV2()),
                new MenuItem("_Routes Window", "F8", () => ShowRoutesWindow()),
                new MenuItem("_Log Window", "F7", () => ShowLogWindow()),
                new MenuItem("_PAD Test Window", "F10", () => ShowPadTestWindow()),
                new MenuItem("_SINTRAN TAD Client", "F4", () => ShowSintranTadClientWindow()),
                new MenuItem("_TAD Server Window", "F11", () => ShowTadServerWindow()),
                new MenuItem("SINTRAN TAD _Server", "Ctrl+T", () => ShowSintranTadServerWindow()),
                new MenuItem("_XMSG Server Window", "F12", () => ShowXMSGServerWindow()),
                new MenuItem("_Link Status", "Ctrl+L", () => ShowLinkStatusWindow()),
                new MenuItem("_Configuration Window", "F9", () => ShowConfigurationWindow())
            };

            // Add separator before open windows list
            var visibleWindows = new List<ManagedWindow>();
            foreach (var w in _managedWindows)
            {
                if (w.IsVisible)
                    visibleWindows.Add(w);
            }

            if (visibleWindows.Count > 0)
            {
                menuItems.Add(new MenuItem("---", "", null) { CanExecute = () => false }); // Separator
                menuItems.Add(new MenuItem("-- Open Windows --", "", null) { CanExecute = () => false });

                foreach (var managedWindow in visibleWindows)
                {
                    var windowName = managedWindow.Name;
                    var hotKey = managedWindow.HotKey;
                    var window = managedWindow.Window;

                    menuItems.Add(new MenuItem(
                        $"✓ {windowName}",
                        hotKey,
                        () => BringWindowToFront(window)
                    ));
                }
            }

            var menuItemArray = new MenuItem[menuItems.Count];
            for (int i = 0; i < menuItems.Count; i++)
            {
                menuItemArray[i] = menuItems[i];
            }
            _windowsMenuBarItem = new MenuBarItem("_Windows", menuItemArray);
            return _windowsMenuBarItem;
        }

        private void BringWindowToFront(Window window)
        {
            try
            {
                if (window != null)
                {
                    window.Visible = true;
                    Application.Top.BringSubviewToFront(window);
                    window.SetFocus();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error bringing window to front: {ex.Message}");
            }
        }

        private void RegisterManagedWindow(Window window, string name, string hotKey = "")
        {
            // Remove any existing entry for this window
            _managedWindows.RemoveAll(w => w.Window == window);

            // Add the window to managed list
            _managedWindows.Add(new ManagedWindow(window, name, hotKey));

            // Refresh the menu
            RefreshWindowsMenu();
        }

        private void UnregisterManagedWindow(Window window)
        {
            _managedWindows.RemoveAll(w => w.Window == window);
            RefreshWindowsMenu();
        }

        private void RefreshWindowsMenu()
        {
            try
            {
                // Use MainLoop.Invoke to ensure this runs on the UI thread
                Application.MainLoop.Invoke(() =>
                {
                    // Find the menu bar
                    MenuBar? menuBar = null;
                    foreach (var subview in Application.Top.Subviews)
                    {
                        if (subview is MenuBar mb)
                        {
                            menuBar = mb;
                            break;
                        }
                    }
                    if (menuBar != null)
                    {
                        // Find the Windows menu index
                        var items = new List<MenuBarItem>();
                        foreach (var menu in menuBar.Menus)
                        {
                            items.Add(menu);
                        }
                        var windowsMenuIndex = -1;

                        for (int i = 0; i < items.Count; i++)
                        {
                            if (items[i].Title.Contains("Windows"))
                            {
                                windowsMenuIndex = i;
                                break;
                            }
                        }

                        if (windowsMenuIndex >= 0)
                        {
                            // Replace the Windows menu with updated one
                            items[windowsMenuIndex] = CreateWindowsMenu();
                            var menuArray = new MenuBarItem[items.Count];
                            for (int i = 0; i < items.Count; i++)
                            {
                                menuArray[i] = items[i];
                            }
                            menuBar.Menus = menuArray;
                            menuBar.SetNeedsDisplay();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error refreshing windows menu");
            }
        }

        private void CreateMainWindow()
        {
            _mainWindow = new Window("X.25 Protocol Emulator")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill() - 1  // Leave space for status bar at bottom
            };

            // Add simulation window
            CreateSimulationWindow();

            // Create menu bar with proper hotkey handling
            var menuBar = new MenuBar(new MenuBarItem[]
            {
                new MenuBarItem("_File", new MenuItem[]
                {
                    new MenuItem("_Exit", "Ctrl+Q", () =>
                    {
                        Application.RequestStop();
                    })
                }),
                new MenuBarItem("_Tools", new MenuItem[]
                {
                    new MenuItem("_Configuration...", "F9", () => ShowConfigurationWindow()),
                    new MenuItem("---", "", null) { CanExecute = () => false }, // Separator
                    new MenuItem("_Reset Statistics", "F5", () => ResetStatistics()),
                    new MenuItem("_Clear Packet History", "F6", () => ClearPacketHistory()),
                    new MenuItem("_Show Log Window", "F7", () => ShowLogWindow())
                }),
                CreateWindowsMenu(),
                new MenuBarItem("_Help", new MenuItem[]
                {
                    new MenuItem("_About", "Ctrl+A", () => ShowAboutDialog()),
                    new MenuItem("_Keyboard Shortcuts", "Ctrl+H", () => ShowHelpDialog())
                })
            });

            // Create status bar with shortcuts
            var statusBar = new StatusBar(new StatusItem[]
            {
                new StatusItem(Key.F1, "~F1~ Status", () => ShowStatusWindow()),
                new StatusItem(Key.F2, "~F2~ Packets", () => ShowPacketWindow()),
                new StatusItem(Key.F3, "~F3~ PacketsV2", () => ShowPacketWindowV2()),
                new StatusItem(Key.F4, "~F4~ SINTRAN TAD", () => ShowSintranTadClientWindow()),
                new StatusItem(Key.F5, "~F5~ Reset", () => ResetStatistics()),
                new StatusItem(Key.F6, "~F6~ Clear", () => ClearPacketHistory()),
                new StatusItem(Key.F7, "~F7~ Log", () => ShowLogWindow()),
                new StatusItem(Key.F8, "~F8~ Routes", () => ShowRoutesWindow()),
                new StatusItem(Key.F9, "~F9~ Config", () => ShowConfigurationWindow()),
                new StatusItem(Key.F10, "~F10~ PAD Test", () => ShowPadTestWindow()),
                new StatusItem(Key.F11, "~F11~ TAD Server", () => ShowTadServerWindow()),
                new StatusItem(Key.F12, "~F12~ XMSG Server", () => ShowXMSGServerWindow()),
                new StatusItem(Key.CtrlMask | Key.L, "~Ctrl+L~ Links", () => ShowLinkStatusWindow()),
                new StatusItem(Key.CtrlMask | Key.H, "~Ctrl+H~ Help", () => ShowHelpDialog()),
                new StatusItem(Key.CtrlMask | Key.A, "~Ctrl+A~ About", () => ShowAboutDialog()),
                new StatusItem(Key.CtrlMask | Key.Q, "~Ctrl+Q~ Quit", () =>
                {
                    Application.RequestStop();
                })
            });

            Application.Top.Add(menuBar);
            Application.Top.Add(_mainWindow);
            Application.Top.Add(statusBar);
        }

        private void CreateStatusWindow()
        {
            _statusWindow = new Window("Status Information")
            {
                X = 0,
                Y = 1,
                Width = 80,
                Height = 8
            };

            _comPortStatus = new Label("Serial: COM7 [Disconnected]")
            {
                X = 1,
                Y = 1
            };

            _hdlcPacketsOk = new Label("HDLC packets:")
            {
                X = 1,
                Y = 2
            };

            _lapbStatus = new Label("LAPB:")
            {
                X = 1,
                Y = 3
            };

            _x25Status = new Label("X.25:")
            {
                X = 1,
                Y = 4
            };

            _statusWindow.Add(_comPortStatus, _hdlcPacketsOk, _lapbStatus, _x25Status);

            _mainWindow.Add(_statusWindow);
        }

        private void CreatePacketWindow()
        {
            _packetWindow = new Window("Packet History")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 2,
                Visible = false
            };

            // Create filter toolbar (2 lines)
            var filterLabel = new Label("Filters:")
            {
                X = 1,
                Y = 1
            };

            // Direction filters
            _filterAllButton = new Button("All")
            {
                X = 8,
                Y = 1,
                Width = 6
            };
            _filterAllButton.Clicked += () => SetDirectionFilter("All");

            _filterTXButton = new Button("TX")
            {
                X = 15,
                Y = 1,
                Width = 4
            };
            _filterTXButton.Clicked += () => SetDirectionFilter("TX");

            _filterRXButton = new Button("RX")
            {
                X = 20,
                Y = 1,
                Width = 4
            };
            _filterRXButton.Clicked += () => SetDirectionFilter("RX");

            // L2 filters
            _filterIButton = new Button("[I]")
            {
                X = 30,
                Y = 1,
                Width = 4
            };
            _filterIButton.Clicked += () => ToggleL2Filter("I");

            _filterSButton = new Button("[S]")
            {
                X = 35,
                Y = 1,
                Width = 4
            };
            _filterSButton.Clicked += () => ToggleL2Filter("S");

            _filterUButton = new Button("[U]")
            {
                X = 40,
                Y = 1,
                Width = 4
            };
            _filterUButton.Clicked += () => ToggleL2Filter("U");

            // L3 filters
            _filterDataButton = new Button("[DATA]")
            {
                X = 50,
                Y = 1,
                Width = 8
            };
            _filterDataButton.Clicked += () => ToggleL3Filter("DATA");

            _filterCallButton = new Button("[CALL]")
            {
                X = 59,
                Y = 1,
                Width = 8
            };
            _filterCallButton.Clicked += () => ToggleL3Filter("CALL");

            _filterClearButton = new Button("[CLEAR]")
            {
                X = 68,
                Y = 1,
                Width = 9
            };
            _filterClearButton.Clicked += () => ToggleL3Filter("CLEAR");

            // Second line of filters
            _filterResetButton = new Button("[RESET]")
            {
                X = 8,
                Y = 2,
                Width = 9
            };
            _filterResetButton.Clicked += () => ToggleL3Filter("RESET");

            _filterRestartButton = new Button("[RESTART]")
            {
                X = 18,
                Y = 2,
                Width = 11
            };
            _filterRestartButton.Clicked += () => ToggleL3Filter("RESTART");

            _filterIntButton = new Button("[INT]")
            {
                X = 30,
                Y = 2,
                Width = 6
            };
            _filterIntButton.Clicked += () => ToggleL3Filter("INT");

            _filterLCNButton = new Button("[LCN:All]")
            {
                X = 37,
                Y = 2,
                Width = 10
            };
            _filterLCNButton.Clicked += () => ToggleLCNFilter();

            _filterErrorsButton = new Button("[Errors]")
            {
                X = 48,
                Y = 2,
                Width = 9
            };
            _filterErrorsButton.Clicked += () => ToggleErrorsFilter();

            _collapseToggleButton = new Button("[Expand]")
            {
                X = 58,
                Y = 2,
                Width = 11
            };
            _collapseToggleButton.Clicked += () => ToggleCollapse();

            _debugButton = new Button("[Debug]")
            {
                X = 70,
                Y = 2,
                Width = 8
            };
            _debugButton.Clicked += () => ShowDebugInfo();

            _testButton = new Button("[Test]")
            {
                X = 79,
                Y = 2,
                Width = 7
            };
            _testButton.Clicked += () => CreateTestChain();

            _filterStatsLabel = new Label("Frames:0 Dropped:0 Link:DOWN Modulo:8")
            {
                X = 87,
                Y = 2
            };

            // Packet list view with frame - fixed height for 10 packets
            var packetListFrame = new FrameView("Packet List")
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                Height = 12  // 10 lines + 1 for frame + 1 for padding
            };

            _packetListView = new ListView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 10  // Exactly 10 lines for packet display
            };
            _packetListView.SelectedItemChanged += OnPacketSelected;

            packetListFrame.Add(_packetListView);

            // Details section
            var detailsLabel = new Label("Frame Detail:")
            {
                X = 1,
                Y = Pos.Bottom(packetListFrame) + 1
            };

            _packetDetailsView = new TextView()
            {
                X = 1,
                Y = Pos.Bottom(packetListFrame) + 2,
                Width = Dim.Fill() - 2,
                Height = 8,
                ReadOnly = true
            };

            // Status bar
            var statusBar = new Label("Dir: All | L2: [I] [S] [U] | L3: [DATA] [CALL] [CLEAR] [RESET] [RESTART] [INT] | Channel: [All] | ErrorsOnly: [ ] | Stats: Frames:0 Dropped:0 Link:DOWN Modulo:8")
            {
                X = 1,
                Y = Pos.Bottom(_packetDetailsView) + 1,
                Width = Dim.Fill() - 2
            };

            _exportPacketsButton = new Button("Export")
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(statusBar) + 1
            };
            _exportPacketsButton.Clicked += ExportPackets;

            var closeButton = new Button("Close")
            {
                X = Pos.Center() + 5,
                Y = Pos.Bottom(statusBar) + 1
            };
            closeButton.Clicked += () =>
            {
                _packetWindow.Visible = false;
                _mainWindow.SetFocus();
            };

            _packetWindow.Add(
                filterLabel, _filterAllButton, _filterTXButton, _filterRXButton,
                _filterIButton, _filterSButton, _filterUButton,
                _filterDataButton, _filterCallButton, _filterClearButton,
                _filterResetButton, _filterRestartButton, _filterIntButton,
                _filterLCNButton, _filterErrorsButton, _collapseToggleButton, _debugButton, _testButton, _filterStatsLabel,
                packetListFrame, detailsLabel, _packetDetailsView, statusBar, _exportPacketsButton, closeButton
            );

            // Add escape key handler
            _packetWindow.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Esc)
                {
                    _packetWindow.Visible = false;
                    _mainWindow.SetFocus();
                    e.Handled = true;
                }
            };

            Application.Top.Add(_packetWindow);
            RegisterManagedWindow(_packetWindow, "Packet History", "F2");
        }


        private void CreateSimulationWindow()
        {
            var simulationWindow = new Window("Simulate Receive")
            {
                X = 0,   // Position at bottom left
                Y = 10,   // Position at bottom left
                Width = 60,
                Height = 7  // Make taller to ensure button is visible
            };

            var hexLabel = new Label("Enter HDLC packet (hex):")
            {
                X = 1,
                Y = 1
            };

            var hexField = new TextField("0x01 0x3F 0x00 0x64")
            {
                X = 1,
                Y = 2,
                Width = 40  // Further reduced width to fit narrower window
            };

            var receiveButton = new Button("RECEIVE")
            {
                X = 1,
                Y = 4,
                Width = 10
            };
            receiveButton.Clicked += () => SimulateReceivePacket(hexField.Text?.ToString() ?? "");

            var clearButton = new Button("CLEAR")
            {
                X = 12,
                Y = 4,
                Width = 10
            };
            clearButton.Clicked += () => hexField.Text = "";

            simulationWindow.Add(hexLabel, hexField, receiveButton, clearButton);
            _mainWindow.Add(simulationWindow);
        }


        private void CreateLogWindow()
        {
            _logWindow = new Window("Application Log")
            {
                X = 10,  // Fixed position instead of center to avoid overlap
                Y = 10,  // Fixed position instead of center to avoid overlap
                Width = 100,  // Smaller width
                Height = 25,  // Smaller height
                Visible = false
            };

            var logLabel = new Label("Application Log:")
            {
                X = 1,
                Y = 1
            };

            _logTextView = new TextView()
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 6,
                ReadOnly = true
            };

            var filterLabel = new Label("Filter Level:")
            {
                X = 1,
                Y = Pos.Bottom(_logTextView) + 1
            };

            _logLevelFilter = new Button("All")
            {
                X = 12,
                Y = Pos.Bottom(_logTextView) + 1,
                Width = 10
            };
            _logLevelFilter.Clicked += CycleLogFilter;

            _clearLogButton = new Button("Clear Log")
            {
                X = 25,
                Y = Pos.Bottom(_logTextView) + 1
            };
            _clearLogButton.Clicked += ClearLog;

            _exportLogButton = new Button("Export")
            {
                X = 37,
                Y = Pos.Bottom(_logTextView) + 1
            };
            _exportLogButton.Clicked += ExportLog;

            var closeButton = new Button("Close")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(_logTextView) + 3
            };
            closeButton.Clicked += () =>
            {
                _logWindow.Visible = false;
                // Return focus to main window
                _mainWindow.SetFocus();
            };

            _logWindow.Add(logLabel, _logTextView, filterLabel, _logLevelFilter,
              _clearLogButton, _exportLogButton, closeButton);

            // Add escape key handler to close log window
            _logWindow.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Esc)
                {
                    _logWindow.Visible = false;
                    _mainWindow.SetFocus();
                    e.Handled = true;
                }
            };

            Application.Top.Add(_logWindow);
            RegisterManagedWindow(_logWindow, "Application Log", "F7");
        }




        /// <summary>
        /// Handle statistics updates (event-driven)
        /// </summary>
        private void OnStatisticsUpdated(object? sender, StatisticsSnapshot stats)
        {
            try
            {
                // Update status displays using ProtocolRuntime statistics with proper column alignment
                // Format: Label + RX column + TX column (aligned at positions 35 and 55)

                // Show complete routing configuration details
                var routing = _runtime?.Config?.Routing;
                if (routing?.Enabled == true)
                {
                    // Serial interfaces details
                    var serialDetails = "";
                    if (routing.SerialInterfaces?.Count > 0)
                    {
                        foreach (var intf in routing.SerialInterfaces)
                        {
                            var status = intf.Enabled ? "ON" : "OFF";
                            serialDetails += $"{intf.Name}:{intf.PortName}({status}) ";
                        }
                    }
                    else
                    {
                        serialDetails = "None ";
                    }

                    // XOT interfaces details
                    var xotDetails = "";
                    if (routing.XotInterfaces?.Count > 0)
                    {
                        foreach (var intf in routing.XotInterfaces)
                        {
                            var status = intf.Enabled ? "ON" : "OFF";
                            var mode = intf.Mode == XotMode.Client ? "C" : "S";
                            xotDetails += $"{intf.Name}:{intf.RemoteIPAddress}({mode},{status}) ";
                        }
                    }
                    else
                    {
                        xotDetails = "None ";
                    }

                    var serialStatus = DetermineSerialStatus(stats);
                    _comPortStatus.Text = $"Serial: {serialDetails.Trim()} [{serialStatus}]".PadRight(50) +
                                         $"RX: {stats.HDLC.BytesReceived} B".PadRight(20) +
                                         $"TX: {stats.HDLC.BytesTransmitted} B";
                }
                else
                {
                    _comPortStatus.Text = "Routing: DISABLED".PadRight(50) +
                                         $"RX: {stats.HDLC.BytesReceived} B".PadRight(20) +
                                         $"TX: {stats.HDLC.BytesTransmitted} B";
                }

                // HDLC packets: RX: [128 OK | 3 BAD] TX: [130 OK | 1 BAD]
                _hdlcPacketsOk.Text = $"HDLC packets:".PadRight(35) +
                                     $"RX: {stats.HDLC.FramesValidFcs} OK | {stats.HDLC.BadFramesReceived} BAD".PadRight(20) +
                                     $"TX: {stats.HDLC.FramesTransmitted} OK | {stats.HDLC.BadFramesTransmitted} BAD";

                // LAPB: [State] RX: [124 OK | 0 BAD] TX: [126 OK | 0 BAD]
                _lapbStatus.Text = $"LAPB: {stats.LAPB.CurrentState.ToUpper()}".PadRight(35) +
                                  $"RX: {stats.LAPB.FramesReceived} OK | {stats.LAPB.BadFramesReceived} BAD".PadRight(20) +
                                  $"TX: {stats.LAPB.FramesTransmitted} OK | {stats.LAPB.BadFramesTransmitted} BAD";

                // X.25 and XOT status with routing details
                if (routing?.Enabled == true)
                {
                    var routingInfo = "";

                    // XOT interfaces
                    if (routing.XotInterfaces?.Count > 0)
                    {
                        foreach (var intf in routing.XotInterfaces)
                        {
                            var status = intf.Enabled ? "ON" : "OFF";
                            var mode = intf.Mode == XotMode.Client ? "C" : "S";
                            routingInfo += $"XOT {intf.Name}:{intf.RemoteIPAddress}({mode},{status}) ";
                        }
                    }

                    // Routing table summary
                    if (routing.RoutingTable?.Count > 0)
                    {
                        int activeRoutes = 0;
                        foreach (var route in routing.RoutingTable)
                        {
                            if (route.Enabled) activeRoutes++;
                        }
                        routingInfo += $"Routes:{activeRoutes}/{routing.RoutingTable.Count} ";
                    }

                    // Default gateway
                    if (!string.IsNullOrEmpty(routing.DefaultGateway))
                    {
                        routingInfo += $"Gateway:{routing.DefaultGateway} ";
                    }

                    _x25Status.Text = $"X.25: {stats.X25.CurrentState.ToUpper()} | {routingInfo.Trim()}".PadRight(50) +
                                     $"RX: {stats.X25.PacketsReceived}/{stats.X25.BadPacketsReceived}".PadRight(15) +
                                     $"TX: {stats.X25.PacketsTransmitted}/{stats.X25.BadPacketsTransmitted}";
                }
                else
                {
                    _x25Status.Text = $"X.25: {stats.X25.CurrentState.ToUpper()}".PadRight(35) +
                                     $"RX: {stats.X25.PacketsReceived} OK | {stats.X25.BadPacketsReceived} BAD".PadRight(20) +
                                     $"TX: {stats.X25.PacketsTransmitted} OK | {stats.X25.BadPacketsTransmitted} BAD";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Statistics update error: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Determine serial port status based on available statistics
        /// </summary>
        private string DetermineSerialStatus(StatisticsSnapshot stats)
        {
            try
            {
                // Check if there's any activity indicating the serial port is active
                var hasActivity = stats.HDLC.BytesReceived > 0 || stats.HDLC.BytesTransmitted > 0;
                var hasErrors = stats.HDLC.BadFramesReceived > 0 || stats.HDLC.BadFramesTransmitted > 0;
                var hasModules = stats.Runtime.RegisteredModules > 0;

                // Status determination based on available information
                if (hasActivity && !hasErrors)
                    return "Active";
                else if (hasActivity && hasErrors)
                    return "Active (Errors)";
                else if (hasModules)
                    return "Ready";
                else
                    return "Stopped";
            }
            catch
            {
                return "Unknown";
            }
        }







        private void OnHistoryItemAdded(object? sender, HistoryItem item)
        {
            Debug.WriteLine($"[DEBUG] OnHistoryItemAdded: {item.Layer} {item.Direction} - ID: {item.Id}");
            Debug.WriteLine($"[DEBUG]   IsMainLine: {item.IsMainLine}, IsSecondaryLine: {item.IsSecondaryLine}");
            Debug.WriteLine($"[DEBUG]   RelatedItem: {(item.RelatedItem != null ? $"Present (ID: {item.RelatedItem.Id}, Layer: {item.RelatedItem.Layer})" : "NULL")}");

            // Convert ProtocolRuntime history items to GUI packet info
            // The payload here is already pure protocol data (no HDLC flags/FCS)
            var packetInfo = new PacketInfo
            {
                PacketNumber = Interlocked.Increment(ref _nextPacketNumber), // Start from 1, not 0
                Timestamp = item.Timestamp,
                Type = item.Layer.ToString(),
                Direction = item.Direction == Direction.TX ? "TX" : "RX",
                Length = (ushort)item.Payload.Length,
                Data = item.Payload, // This is pure protocol data (LAPB/X.25 without HDLC overhead)
                Description = item.Description,
                HistoryItem = item // Store reference to business model
            };

            // Handle relationships for two-line display - simplified using business logic flags
            if (item.RelatedItem != null)
            {
                Debug.WriteLine($"[DEBUG]   Setting up relationship between {item.Layer} and {item.RelatedItem.Layer}");

                // Find the related PacketInfo by history item ID (more reliable)
                PacketInfo? relatedPacketInfo = null;

                // Look for the related packet in our GUI history
                for (int i = _packetHistory.Count - 1; i >= 0; i--)
                {
                    var p = _packetHistory[i];
                    if (p.HistoryItem != null && p.HistoryItem.Id == item.RelatedItem.Id)
                    {
                        relatedPacketInfo = p;
                        break;
                    }
                }

                if (relatedPacketInfo != null)
                {
                    Debug.WriteLine($"[DEBUG]   Found related PacketInfo: #{relatedPacketInfo.PacketNumber}");
                    packetInfo.RelatedPacket = relatedPacketInfo;
                    packetInfo.RelationshipType = item.RelationshipType.ToString();

                    // Set the reverse relationship
                    relatedPacketInfo.RelatedPacket = packetInfo;
                    relatedPacketInfo.RelationshipType = item.RelatedItem.RelationshipType.ToString();

                    // Refresh display to show two-line format
                    Debug.WriteLine($"[DEBUG]   Refreshing display for two-line format");
                    RefreshPacketDisplay();
                }
                else
                {
                    Debug.WriteLine($"[DEBUG]   Related PacketInfo NOT found in history!");
                }
            }

            // Process packet immediately (event-driven) instead of queuing
            ProcessPacketImmediately(packetInfo);
        }


        /// <summary>
        /// Process a packet immediately (event-driven) instead of queuing
        /// </summary>
        private void ProcessPacketImmediately(PacketInfo packetInfo)
        {
            // Process packet immediately (event-driven)
            if (packetInfo.Type == "HDLC" || packetInfo.Type == "LAPB" || packetInfo.Type == "X25")
            {
                AddPacketToHistory(packetInfo);
            }
            else if (packetInfo.Type == "LOG" && _logWindow.Visible)
            {
                // Update log window if it's visible
                AppendLogEntry(new LogEntry
                {
                    Timestamp = packetInfo.Timestamp,
                    Level = RetroIO.Protocols.X25.Common.LogLevel.Info,
                    Category = "GUI",
                    Message = packetInfo.Description
                });
            }
        }

        private void AddPacketToHistory(PacketInfo packetInfo)
        {
            // Parse packet details for display
            ParsePacketDetails(packetInfo);

            _packetHistory.Add(packetInfo);

            // Keep only last 100 packets (much smaller for performance)
            if (_packetHistory.Count > 100)
            {
                _packetHistory.RemoveAt(0);
            }

            // Debug: log packet count
            if (_packetHistory.Count % 10 == 0)
            {
                _logger.LogInformation("Packet history count: {Count}", _packetHistory.Count);
            }
        }

        private void ParsePacketDetails(PacketInfo packet)
        {
            if (packet.Data.Length < 2) return;

            // Handle different packet types
            if (packet.Type == "HDLC")
            {
                // For HDLC frames, we need to strip flags and FCS to get LAPB data
                ParseHdlcPacket(packet);
            }
            else if (packet.Type == "LAPB")
            {
                // For LAPB frames, data is already pure LAPB content
                ParseLapbPacket(packet);
            }
            else if (packet.Type == "X25")
            {
                // For X.25 packets, data is already pure X.25 content
                ParseX25Packet(packet);
            }
        }

        private void ParseHdlcPacket(PacketInfo packet)
        {
            // HDLC frames have flags (0x7E) at start and end, and FCS at the end
            // We need to extract the LAPB data from the middle
            if (packet.Data.Length < 4) return; // Need at least flags + 2 bytes of data

            // Find the actual LAPB data (between flags, before FCS)
            int startIndex = 0;
            int endIndex = packet.Data.Length - 1;

            // Skip leading flag if present
            if (packet.Data[0] == 0x7E) startIndex = 1;

            // Skip trailing flag and FCS if present
            if (packet.Data[packet.Data.Length - 1] == 0x7E) endIndex = packet.Data.Length - 3; // Skip flag + 2-byte FCS

            if (endIndex <= startIndex) return;

            // Extract LAPB data
            byte[] lapbData = new byte[endIndex - startIndex + 1];
            Array.Copy(packet.Data, startIndex, lapbData, 0, lapbData.Length);

            // Now parse as LAPB packet
            ParseLapbData(lapbData, packet);
        }

        private void ParseLapbPacket(PacketInfo packet)
        {
            // Data is already pure LAPB content
            ParseLapbData(packet.Data, packet);
        }

        private void ParseLapbData(byte[] data, PacketInfo packet)
        {
            if (data.Length < 2) return;

            byte address = data[0];
            byte control = data[1];

            // Parse LAPB details
            packet.L2Control = control;
            packet.L2Address = address == 0x01 ? "DCE" : address == 0x03 ? "DTE" : $"0x{address:X2}";
            packet.L2PF = (control & 0x10) != 0;

            if ((control & 0x01) == 0x00) // Information frame
            {
                packet.L2Type = "I";
                packet.Type = "I-Frame";
                packet.L2NS = (byte)((control >> 1) & 0x07);
                packet.L2NR = (byte)((control >> 5) & 0x07);

                // Only parse X.25 if we have enough data
                if (data.Length > 4)
                {
                    int x25Length = data.Length - 2;
                    if (x25Length > 0)
                    {
                        byte[] x25Bytes = new byte[x25Length];
                        Array.Copy(data, 2, x25Bytes, 0, x25Length);
                        ParseX25DetailsFromBytes(x25Bytes, packet);
                    }
                }
            }
            else if ((control & 0x03) == 0x01) // Supervisory frame
            {
                packet.L2Type = "S";
                packet.L2NR = (byte)((control >> 5) & 0x07);

                // Parse S-frame subtype
                byte sType = (byte)(control & 0x0C);
                packet.Type = sType switch
                {
                    0x00 => "RR",
                    0x04 => "RNR",
                    0x08 => "REJ",
                    _ => $"0x{control:X2}"
                };
            }
            else // Unnumbered frame
            {
                packet.L2Type = "U";
                // Parse U-frame type
                byte uType = (byte)(control & 0xEF);
                packet.Type = GetUFrameTypeName(uType);
            }

            // For LAPB frames, we don't validate FCS since it's handled by HDLC layer
            packet.L2FCS = 0;
            packet.L2FCSValid = true;
            packet.IsError = false;
        }

        private void ParseX25Packet(PacketInfo packet)
        {
            // PacketInfo.Data already contains pure X.25 bytes (GFI, LCN, PTI, ...)
            ParseX25DetailsFromBytes(packet.Data, packet);
        }

        // Optimized U-frame type lookup
        private static readonly Dictionary<byte, string> UFrameTypes = new Dictionary<byte, string>
        {
            { 0x2F, "SABM" },
            { 0x6F, "SABME" },
            { 0x43, "DISC" },
            { 0x63, "UA" },
            { 0x0F, "DM" },
            { 0x87, "FRMR" },
            { 0x83, "SNRM" },
            { 0x0C, "SARM" },
            { 0x9E, "SNRME" },
            { 0x8C, "SARME" },
            { 0x03, "UI" },
            { 0xAF, "XID" },
            { 0xE3, "TEST" }
        };

        private static string GetUFrameTypeName(byte uType)
        {
            return UFrameTypes.TryGetValue(uType, out string? name) ? name : $"0x{uType:X2}";
        }

        private void ParseX25DetailsFromBytes(byte[] x25Data, PacketInfo packet)
        {
            if (x25Data == null || x25Data.Length < 3) return;

            // X.25 header starts at offset 0 in provided buffer
            byte gfid = x25Data[0];
            packet.L3M = (gfid & 0x80) != 0;
            packet.L3Q = (gfid & 0x40) != 0;
            packet.L3D = (gfid & 0x20) != 0;

            // LCI = (GFI & 0x07) << 8 | LCN. For display we keep LCN byte, grid shows LCN or full LCI elsewhere.
            byte lcnLow = x25Data[1];
            byte lcg = (byte)(gfid & 0x07);
            int lci = (lcg << 8) | lcnLow;

            // Preserve both for future enhancements; grid currently uses L3LCN
            packet.L3LCN = (ushort)lci; // show full LCI to avoid ambiguity across groups

            byte pktType = x25Data[2];
            packet.L3Type = pktType switch
            {
                0x0B => "CALL-REQ",
                0x0F => "CALL-ACC",
                0x13 => "CLEAR-REQ",
                0x17 => "CLEAR-CONF",
                0x23 => "INT",
                0x27 => "INT-CONF",
                0x1B => "RESET-REQ",
                0x1F => "RESET-CONF",
                0xFB => "RESTART-REQ",
                0xFF => "RESTART-CONF",
                0xF3 => "REG-REQ",
                0xF7 => "REG-CONF",
                0xF1 => "DIAG",
                // Data packets have LSB=0, show based on P(S)/P(R) 
                _ when (pktType & 0x01) == 0x00 => "DATA",
                // Flow control packets: RR, RNR, REJ have variable P(R)
                _ when (pktType & 0x1F) == 0x01 => "RR",
                _ when (pktType & 0x1F) == 0x05 => "RNR",
                _ when (pktType & 0x1F) == 0x09 => "REJ",
                _ => $"0x{pktType:X2}"
            };

            // Parse additional fields based on packet type  
            if ((pktType & 0x01) == 0x00 && x25Data.Length >= 4) // DATA packets have LSB=0
            {
                packet.L3M = (x25Data[3] & 0x80) != 0;
                packet.L3Q = (x25Data[3] & 0x40) != 0;
                packet.L3D = (x25Data[3] & 0x20) != 0;
            }
            else if ((pktType == 0x13 || pktType == 0x1B || pktType == 0xFB) && x25Data.Length >= 5) // CLEAR/RESET/RESTART
            {
                packet.L3Cause = x25Data[3];
                packet.L3Diag = x25Data[4];
            }
        }

        private string GenerateDisplayRow(PacketInfo packet)
        {
            var sb = new StringBuilder();

            // ID (6 chars)
            sb.Append($"#{packet.PacketNumber:D4} ");

            // Time (12 chars)
            sb.Append($"{packet.Timestamp:HH:mm:ss.fff} ");

            // Direction (3 chars)
            sb.Append($"{packet.Direction} ");

            // L2 token (5 chars)
            sb.Append($"[{packet.L2Type} ] ");

            // L3 type (12 chars)
            string l3Type = packet.L3Type.Length > 0 ? packet.L3Type : "—";
            sb.Append($"{l3Type,-12} ");

            // Channel/Address (14 chars)
            if (packet.L3LCN.HasValue)
            {
                sb.Append($"LCN:{packet.L3LCN,-9} ");
            }
            else
            {
                sb.Append($"Addr:{packet.L2Address,-8} ");
            }

            // Sequence (14 chars)
            if (packet.L2Type == "I")
            {
                sb.Append($"NS/NR={packet.L2NS}/{packet.L2NR,-8} ");
            }
            else if (packet.L2Type == "S")
            {
                sb.Append($"—(N(R)={packet.L2NR,-6}) ");
            }
            else
            {
                sb.Append($"—{' ',12} ");
            }

            // Flags (10 chars)
            string flags = packet.L2PF ? "PF:P" : "PF:—";
            if (packet.L3M || packet.L3Q || packet.L3D)
            {
                flags += $" M={(packet.L3M ? 1 : 0)} Q={(packet.L3Q ? 1 : 0)} D={(packet.L3D ? 1 : 0)}";
            }
            sb.Append($"{flags,-10} ");

            // Length (7 chars)
            sb.Append($"Len:{packet.Length,-3} ");

            // FCS (6 chars)
            sb.Append($"FCS:{(packet.L2FCSValid ? "OK" : "BAD")} ");

            // Notes (rest)
            string notes = packet.Description;
            if (packet.L3Cause != 0)
            {
                notes += $" Cause={packet.L3Cause:X2} Diag={packet.L3Diag:X2}";
            }
            sb.Append(notes);

            return sb.ToString();
        }

        private string GetHexBytes(byte[] data, int maxBytes)
        {
            if (data == null || data.Length == 0)
                return "";

            var sb = new StringBuilder();
            int bytesToShow = Math.Min(data.Length, maxBytes);

            for (int i = 0; i < bytesToShow; i++)
            {
                sb.Append($"{data[i]:X2}");
                if (i < bytesToShow - 1)
                    sb.Append(" ");
            }

            if (data.Length > maxBytes)
                sb.Append("...");

            return sb.ToString();
        }

        private string GetPacketDescription(byte[] frame, ushort length)
        {
            if (length < 2) return "Invalid frame";

            byte address = frame[0];
            byte control = frame[1];

            if ((control & 0x01) == 0x01) // Information frame
            {
                return "I-Frame (Data)";
            }
            else if ((control & 0x03) == 0x01) // Supervisory frame
            {
                byte sType = (byte)(control & 0x0F);
                return sType switch
                {
                    0x01 => "RR (Receive Ready)",
                    0x05 => "RNR (Receive Not Ready)",
                    0x09 => "REJ (Reject)",
                    _ => "Unknown Supervisory"
                };
            }
            else // Unnumbered frame
            {
                byte uType = (byte)(control & 0xEF);
                return uType switch
                {
                    0x2F => "SABM (Set Async Balanced Mode)",
                    0x6F => "SABME (Set Async Balanced Mode Extended)",
                    0x43 => "DISC (Disconnect)",
                    0x63 => "UA (Unnumbered Acknowledgment)",
                    0x0F => "DM (Disconnected Mode)",
                    0x87 => "FRMR (Frame Reject)",
                    0x83 => "SNRM (Set Normal Response Mode)",
                    0x0C => "SARM (Set Async Response Mode)",
                    0x9E => "SNRME (Set Normal Response Mode Extended)",
                    0x8C => "SARME (Set Async Response Mode Extended)",
                    0x03 => "UI (Unnumbered Information)",
                    0xAF => "XID (Exchange Identification)",
                    0xE3 => "TEST (Test)",
                    _ => $"Unknown Unnumbered (0x{uType:X2})"
                };
            }
        }

        private void OnPacketSelected(ListViewItemEventArgs e)
        {
            if (e.Item >= 0 && e.Item < _packetHistory.Count)
            {
                var packet = _packetHistory[_packetHistory.Count - 1 - e.Item];

                // Check if we have unified packet data for this packet ID
                var unifiedPackets = _runtime!.GetUnifiedPacketsForDisplay();
                PacketDisplayItem? matchingUnified = null;
                foreach (var up in unifiedPackets)
                {
                    if (up.Packet.Id == packet.PacketNumber)
                    {
                        matchingUnified = up;
                        break;
                    }
                }

                if (matchingUnified?.Packet is UnifiedHistoryItem unifiedItem)
                {
                    // Use unified packet details - shows all layer information
                    ShowUnifiedPacketDetails(unifiedItem);
                }
                else
                {
                    // Fallback to legacy packet details
                    ShowEnhancedPacketDetails(packet);
                }
            }
        }


        private string FormatPacketDisplayLine(PacketInfo packet)
        {
            var sb = new StringBuilder();

            // Fixed-width columns for better alignment
            // ID (6 chars)
            sb.Append($"#{packet.PacketNumber:D4} ");

            // Time (12 chars)
            sb.Append($"{packet.Timestamp:HH:mm:ss.fff} ");

            // Direction (3 chars)
            sb.Append($"{packet.Direction,-3} ");

            // L2 token (4 chars)
            string l2Token = GetL2Token(packet);
            sb.Append($"{l2Token,-4} ");

            // L3 type (10 chars)
            string l3Type = GetL3Type(packet);
            sb.Append($"{l3Type,-10} ");

            // Channel/Address (12 chars)
            string channelAddr = GetChannelAddress(packet);
            sb.Append($"{channelAddr,-12} ");

            // Sequence (12 chars)
            string sequence = GetSequenceInfo(packet);
            sb.Append($"{sequence,-12} ");

            // Flags (8 chars)
            string flags = GetFlags(packet);
            sb.Append($"{flags,-8} ");

            // Length (6 chars)
            sb.Append($"Len:{packet.Length,-3} ");

            // FCS (6 chars)
            string fcsStatus = packet.Direction == "RX" ? (packet.L2FCSValid ? "OK" : "BAD") : "OK";
            sb.Append($"FCS:{fcsStatus,-3}");

            // Notes (rest)
            string notes = GetNotes(packet);
            if (!string.IsNullOrEmpty(notes))
                sb.Append(notes);

            return sb.ToString();
        }

        /// <summary>
        /// Format packet display lines handling relationships for two-line display
        /// </summary>
        private List<string> FormatPacketDisplayLines(PacketInfo packet)
        {
            var lines = new List<string>();

            // Check if we have a relationship and if this packet should be the parent
            if (packet.RelatedPacket != null && ShouldShowAsParent(packet))
            {
                // Show primary line first (the "important" layer)
                var primaryPacket = GetPrimaryPacket(packet);
                var secondaryPacket = GetSecondaryPacket(packet);

                lines.Add(FormatPacketDisplayLine(primaryPacket));
                lines.Add($"   ==>  {FormatRelatedPacketLine(secondaryPacket)}");
            }
            else if (packet.RelatedPacket == null)
            {
                // Single line for unrelated packets
                lines.Add(FormatPacketDisplayLine(packet));
            }
            // Skip packets that are already shown as secondary lines

            return lines;
        }

        /// <summary>
        /// Determine if this packet should be shown as the parent in a relationship
        /// </summary>
        private bool ShouldShowAsParent(PacketInfo packet)
        {
            if (packet.RelatedPacket == null) return false;

            // For TX: X.25 is primary (business logic), LAPB is secondary (transport)
            // For RX: LAPB is primary (received), X.25 is secondary (extracted content)
            if (packet.Direction == "TX")
            {
                return packet.Type == "X25";
            }
            else
            {
                return packet.Type == "LAPB";
            }
        }

        /// <summary>
        /// Get the primary packet (the one that should be shown first)
        /// </summary>
        private PacketInfo GetPrimaryPacket(PacketInfo packet)
        {
            if (ShouldShowAsParent(packet))
                return packet;
            else
                return packet.RelatedPacket!;
        }

        /// <summary>
        /// Get the secondary packet (the one that should be shown indented)
        /// </summary>
        private PacketInfo GetSecondaryPacket(PacketInfo packet)
        {
            if (ShouldShowAsParent(packet))
                return packet.RelatedPacket!;
            else
                return packet;
        }

        /// <summary>
        /// Format the secondary line with timestamp and abbreviated information
        /// </summary>
        private string FormatRelatedPacketLine(PacketInfo packet)
        {
            var sb = new StringBuilder();

            // Time (12 chars) - slightly different timestamp precision for visual hierarchy  
            sb.Append($"{packet.Timestamp:HH:mm:ss.fff} ");

            // Direction (3 chars)
            sb.Append($"{packet.Direction,-3} ");

            // L2 token (4 chars)
            string l2Token = GetL2Token(packet);
            sb.Append($"{l2Token,-4} ");

            // L3 type (10 chars)  
            string l3Type = GetL3Type(packet);
            sb.Append($"{l3Type,-10} ");

            // Channel/Address (12 chars)
            string channelAddr = GetChannelAddress(packet);
            sb.Append($"{channelAddr,-12} ");

            // Sequence (12 chars)
            string sequence = GetSequenceInfo(packet);
            sb.Append($"{sequence,-12} ");

            // Length (8 chars)
            sb.Append($"Len:{packet.Length,-4} ");

            // Status (8 chars)
            string status = packet.L2FCSValid ? "FCS:OK" : "FCS:BAD";
            sb.Append($"{status,-8} ");

            // Description
            sb.Append(GetNotes(packet));

            return sb.ToString();
        }

        private string GetL2Token(PacketInfo packet)
        {
            if (packet.L2Type == "I") return "[I]";
            if (packet.L2Type == "S") return "[S]";
            if (packet.L2Type == "U") return "[U]";

            // For X.25 packets (no L2 layer analysis), show [X] instead of [?]
            if (!string.IsNullOrEmpty(packet.L3Type))
                return "[X]";

            return "[?]";
        }

        private string GetL3Type(PacketInfo packet)
        {
            // First check for X.25 packet types
            if (!string.IsNullOrEmpty(packet.L3Type))
            {
                return packet.L3Type switch
                {
                    "DATA" => "DATA",
                    "CALL-REQ" => "CALL-REQ",
                    "CALL-CONF" => "CALL-CONF",
                    "CLR-IND" => "CLR-IND",
                    "RESET" => "RESET",
                    "RESTART" => "RESTART",
                    "INT" => "INT",
                    _ => packet.L3Type
                };
            }

            // If no X.25 data, show LAPB frame name
            if (!string.IsNullOrEmpty(packet.Type))
            {
                return packet.Type switch
                {
                    "SABM" => "SABM",
                    "SABME" => "SABME",
                    "UA" => "UA",
                    "DISC" => "DISC",
                    "DM" => "DM",
                    "FRMR" => "FRMR",
                    "SNRM" => "SNRM",
                    "SARM" => "SARM",
                    "SNRME" => "SNRME",
                    "SARME" => "SARME",
                    "UI" => "UI",
                    "XID" => "XID",
                    "TEST" => "TEST",
                    "RR" => "RR",
                    "RNR" => "RNR",
                    "REJ" => "REJ",
                    _ => packet.Type
                };
            }

            return "";
        }

        private string GetChannelAddress(PacketInfo packet)
        {
            if (packet.L3LCN.HasValue)
                return $"LCN:{packet.L3LCN}";

            if (packet.L2Address == "DCE") return "Addr:DCE";
            if (packet.L2Address == "DTE") return "Addr:DTE";
            return $"Addr:{packet.L2Address}";
        }

        private string GetSequenceInfo(PacketInfo packet)
        {
            if (packet.L2Type == "I")
                return $"NS/NR={packet.L2NS}/{packet.L2NR}";

            if (packet.L2Type == "S")
                return $"N(R)={packet.L2NR}";

            return "";
        }

        private string GetFlags(PacketInfo packet)
        {
            var flags = new List<string>();

            if (packet.L2PF)
                flags.Add("PF:F");
            else if (packet.L2Type == "U")
                flags.Add("PF:P");
            else
                flags.Add("PF:—");

            if (packet.L3M) flags.Add("M=1");
            if (packet.L3Q) flags.Add("Q=1");
            if (packet.L3D) flags.Add("D=1");

            return string.Join(" ", flags);
        }

        private string GetNotes(PacketInfo packet)
        {
            if (!packet.L2FCSValid && packet.Direction == "RX")
                return "FCS error";

            if (packet.L3Type == "CALL-CONF")
                return "VC established";
            if (packet.L3Type == "CALL-REQ")
                return "VC setup";
            if (packet.L3Type == "DATA")
                return "Data";
            if (packet.L3Type == "CLR-IND")
                return $"Cause={packet.L3Cause:X2} Diag={packet.L3Diag:X2}";
            if (packet.L2Type == "S" && packet.L2Control == 0x01)
                return "Ack";

            return packet.Description;
        }

        private void ShowEnhancedPacketDetails(PacketInfo packet)
        {
            var details = new StringBuilder();

            // Use packet-specific ToString() methods for self-decoding
            var packetSummary = GetPacketSummaryUsingToString(packet);
            if (!string.IsNullOrEmpty(packetSummary))
            {
                details.AppendLine("Protocol Analysis:");
                details.AppendLine(packetSummary);
                details.AppendLine();
            }

            // Context information (keep this for additional insight)
            string contextDescription = GetContextDescription(packet);
            if (!string.IsNullOrEmpty(contextDescription))
            {
                details.AppendLine($"Context: {contextDescription}");
                details.AppendLine();
            }

            // Additional layer-specific details using packet objects
            var layerDetails = GetLayerSpecificDetails(packet);
            if (!string.IsNullOrEmpty(layerDetails))
            {
                details.AppendLine("Layer Details:");
                details.AppendLine(layerDetails);
                details.AppendLine();
            }

            // Hex/ASCII dump (keep this for debugging)
            details.AppendLine("Raw Data:");
            details.Append("  Hex: ");
            for (int i = 0; i < packet.Data.Length; i++)
            {
                details.Append($"{packet.Data[i]:X2} ");
            }
            details.AppendLine();
            details.Append("  ASCII: ");
            for (int i = 0; i < packet.Data.Length; i++)
            {
                char c = (char)packet.Data[i];
                details.Append(char.IsControl(c) || c < 32 || c > 126 ? '.' : c);
            }

            _packetDetailsView.Text = details.ToString();
        }

        /// <summary>
        /// Show enhanced packet details for unified packets
        /// Displays all layer information accumulated as packet traversed protocol stack
        /// </summary>
        private void ShowUnifiedPacketDetails(UnifiedHistoryItem unifiedPacket)
        {
            if (unifiedPacket?.PacketInfo == null)
            {
                _packetDetailsView.Text = "No unified packet information available.";
                return;
            }

            // Use the comprehensive GetDetailedInfo method from UnifiedPacketInfo
            var detailedInfo = unifiedPacket.PacketInfo.GetDetailedInfo();

            _packetDetailsView.Text = detailedInfo;
        }

        /// <summary>
        /// Get packet summary using the new ToString() methods of packet objects
        /// </summary>
        private string GetPacketSummaryUsingToString(PacketInfo packet)
        {
            var summary = new StringBuilder();

            try
            {
                // Determine layer based on packet type and content
                var packetType = packet.Type.ToUpper();

                // Check if it's an X.25 packet
                if (packetType == "X25" || packetType == "DATA" || packetType == "CALL REQUEST" ||
                    packetType == "CALL ACCEPTED" || packetType == "CLEAR REQUEST" || packetType == "RESET REQUEST")
                {
                    var x25Summary = GetX25PacketSummary(packet);
                    if (!string.IsNullOrEmpty(x25Summary))
                        summary.AppendLine(x25Summary);
                }
                // Check if it's a LAPB frame (SABM, UA, RR, RNR, REJ, I-frames, etc.)
                else if (packetType == "LAPB" || packetType == "SABM" || packetType == "UA" || packetType == "RR" ||
                         packetType == "RNR" || packetType == "REJ" || packetType == "DISC" || packetType == "DM" ||
                         packetType == "FRMR" || packetType == "UI" || packetType == "XID" || packetType == "TEST" ||
                         packetType == "SNRM" || packetType == "SARM" || packetType == "SNRME" || packetType == "SARME" ||
                         packetType.StartsWith("I"))
                {
                    var lapbSummary = GetLapbFrameSummary(packet);
                    if (!string.IsNullOrEmpty(lapbSummary))
                        summary.AppendLine(lapbSummary);
                }
                // Check if it's an HDLC frame
                else if (packetType == "HDLC" || packet.Data.Length >= 2 && packet.Data[0] == 0x7E)
                {
                    var hdlcSummary = GetHdlcFrameSummary(packet);
                    if (!string.IsNullOrEmpty(hdlcSummary))
                        summary.AppendLine(hdlcSummary);
                }
                else
                {
                    // Fallback: try to determine by analyzing the data
                    if (packet.Data.Length >= 4)
                    {
                        // Try LAPB first (most common case)
                        var lapbSummary = GetLapbFrameSummary(packet);
                        if (!string.IsNullOrEmpty(lapbSummary))
                        {
                            summary.AppendLine(lapbSummary);
                        }
                        else
                        {
                            // Ask the packet object for its details using toString()
                            var hdlcSummary = GetHdlcFrameSummary(packet);
                            if (!string.IsNullOrEmpty(hdlcSummary))
                            {
                                summary.AppendLine(hdlcSummary);
                            }
                            else
                            {
                                // Last resort - packet has no self-decoding capability
                                summary.AppendLine($"Basic frame info: {packet.Type}");
                            }
                        }
                    }
                    else
                    {
                        // Try HDLC summary for short packets
                        var hdlcSummary = GetHdlcFrameSummary(packet);
                        if (!string.IsNullOrEmpty(hdlcSummary))
                        {
                            summary.AppendLine(hdlcSummary);
                        }
                        else
                        {
                            summary.AppendLine($"Frame: {packet.Type}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                summary.AppendLine($"Error decoding packet: {ex.Message}");
                // Fallback info
                summary.AppendLine($"Frame Type: {packet.Type}");
                summary.AppendLine($"Description: {packet.Description}");
            }

            return summary.ToString();
        }

        /// <summary>
        /// Get X.25 packet summary using X25Packet.ToString()
        /// </summary>
        private string GetX25PacketSummary(PacketInfo packet)
        {
            try
            {
                // Reconstruct X25Packet from PacketInfo data
                var x25Packet = new X25Packet(packet.Data)
                {
                    Direction = packet.Direction == "TX" ? PacketDirection.Transmit : PacketDirection.Receive,
                    Timestamp = packet.Timestamp
                };

                return x25Packet.ToString();
            }
            catch
            {
                return $"X25 packet decode error - Length: {packet.Data.Length}";
            }
        }

        /// <summary>
        /// Get LAPB frame summary using LapbFrameEventArgs.GetDetailedFrameInfo()
        /// </summary>
        private string GetLapbFrameSummary(PacketInfo packet)
        {
            try
            {
                // Create LapbFrameEventArgs from PacketInfo data
                var lapbFrame = new LapbFrameEventArgs
                {
                    FrameData = packet.Data,
                    Length = packet.Data.Length,
                    Direction = packet.Direction == "TX" ? FrameDirection.Transmit : FrameDirection.Receive,
                    Timestamp = packet.Timestamp,
                    FrameType = DetermineFrameType(packet.L2Control),
                    Address = ConvertAddressStringToByte(packet.L2Address),
                    Control = packet.L2Control,
                    Information = packet.L3Type != null ? packet.Data.Skip(3).ToArray() : Array.Empty<byte>(),
                    NS = packet.L2NS,
                    NR = packet.L2NR,
                    PF = packet.L2PF,
                    FrameId = packet.PacketNumber
                };

                return lapbFrame.GetDetailedFrameInfo();
            }
            catch
            {
                return $"LAPB frame decode error - Length: {packet.Data.Length}";
            }
        }

        /// <summary>
        /// Get HDLC frame summary using HdlcFrameEventArgs.GetDetailedFrameInfo()
        /// </summary>
        private string GetHdlcFrameSummary(PacketInfo packet)
        {
            try
            {
                // Create HdlcFrameEventArgs from PacketInfo data  
                var hdlcFrame = new HdlcFrameEventArgs
                {
                    FrameData = packet.Data,
                    Length = packet.Data.Length,
                    Direction = packet.Direction == "TX" ? FrameDirection.Transmit : FrameDirection.Receive,
                    Timestamp = packet.Timestamp,
                    FcsValid = packet.L2FCSValid,
                    FrameId = packet.PacketNumber
                };

                return hdlcFrame.GetDetailedFrameInfo();
            }
            catch
            {
                return $"HDLC frame decode error - Length: {packet.Data.Length}";
            }
        }

        /// <summary>
        /// Determine LAPB frame type from control byte
        /// </summary>
        private LapbFrameType DetermineFrameType(byte control)
        {
            if ((control & 0x01) == 0)
                return LapbFrameType.Information;
            else if ((control & 0x03) == 0x01)
                return LapbFrameType.Supervisory;
            else if ((control & 0x03) == 0x03)
                return LapbFrameType.Unnumbered;
            else
                return LapbFrameType.Unknown;
        }

        /// <summary>
        /// Get additional layer-specific details for supplementary information
        /// </summary>
        private string GetLayerSpecificDetails(PacketInfo packet)
        {
            var details = new StringBuilder();

            // Add any additional details not covered by ToString() methods
            if (packet.L2FCS != 0)
            {
                details.AppendLine($"FCS: 0x{packet.L2FCS:X4} ({(packet.L2FCSValid ? "Valid" : "Invalid")})");
            }

            if (!string.IsNullOrEmpty(packet.L3Facilities))
            {
                details.AppendLine($"Facilities: {packet.L3Facilities}");
            }

            return details.ToString();
        }

        /// <summary>
        /// Convert address string back to byte value
        /// </summary>
        private byte ConvertAddressStringToByte(string address)
        {
            return address switch
            {
                "DCE" => 0x01,
                "DTE" => 0x03,
                _ when address.StartsWith("0x") => Convert.ToByte(address, 16),
                _ => 0x03 // Default to DTE
            };
        }

        private string AnalyzeCudData(string cudHex)
        {
            try
            {
                // Convert hex string to bytes
                var cudBytes = ParseHexString(cudHex);
                if (cudBytes == null || cudBytes.Length < 2) return "";

                // Check if it's XMSG protocol
                var version = XmsgProtocolHelper.ExtractVersionFromCud(cudBytes);
                if (version.HasValue)
                {
                    var port = XmsgProtocolHelper.ExtractPortFromCud(cudBytes);
                    var result = new StringBuilder();
                    result.Append($"XMSG Protocol: {version.Value} (0x{(ushort)version.Value:X4}) - {version.Value.GetVersionDescription()}");

                    if (port.HasValue)
                    {
                        result.Append($", Port: 0x{port.Value:X4} ({port.Value})");
                        if (XmsgProtocolHelper.IsValidXmsgPortRange(port.Value))
                        {
                            var servicePort = (XmsgServicePort)port.Value;
                            result.Append($" - {servicePort.GetServiceDescription()}");
                        }
                    }
                    return result.ToString();
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private byte[]? ParseHexString(string hex)
        {
            try
            {
                var cleanHex = hex.Replace(" ", "").Replace("-", "");
                if (cleanHex.Length % 2 != 0) return null;

                var bytes = new byte[cleanHex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
                }
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        private string GetAddressFieldDescription(string address)
        {
            try
            {
                if (byte.TryParse(address, out byte addressByte))
                {
                    if (Enum.IsDefined(typeof(HdlcAddress), addressByte))
                    {
                        var hdlcAddress = (HdlcAddress)addressByte;
                        return hdlcAddress.GetDescription();
                    }
                }

                return address switch
                {
                    "DTE" => "Data Terminal Equipment originated frame",
                    "DCE" => "Data Communication Equipment originated frame",
                    _ => "Unknown address type"
                };
            }
            catch
            {
                return "Unknown address";
            }
        }

        private string GetControlFieldDescription(byte controlByte)
        {
            try
            {
                // Try to get LAPB control field description
                if (Enum.IsDefined(typeof(LapbControlField), controlByte))
                {
                    var controlField = (LapbControlField)controlByte;
                    var category = controlField.GetCategory();
                    var description = controlField.GetDescription();
                    return $"{controlField} - {description}";
                }

                // If not a defined constant, try to categorize
                if ((controlByte & 0x01) == 0)
                {
                    return "I-frame (Information)";
                }
                else if ((controlByte & 0x03) == 0x01)
                {
                    return "S-frame (Supervisory)";
                }
                else if ((controlByte & 0x03) == 0x03)
                {
                    return "U-frame (Unnumbered)";
                }
                else
                {
                    return "Unknown frame type";
                }
            }
            catch
            {
                return "Unknown control field";
            }
        }

        private string GetFrameDescription(string frameType)
        {
            return frameType switch
            {
                "SABM" => "Set Asynchronous Balanced Mode",
                "SABME" => "Set Asynchronous Balanced Mode Extended",
                "UA" => "Unnumbered Acknowledgment",
                "DISC" => "Disconnect",
                "DM" => "Disconnected Mode",
                "FRMR" => "Frame Reject",
                "SNRM" => "Set Normal Response Mode",
                "SARM" => "Set Asynchronous Response Mode",
                "SNRME" => "Set Normal Response Mode Extended",
                "SARME" => "Set Asynchronous Response Mode Extended",
                "UI" => "Unnumbered Information",
                "XID" => "Exchange Identification",
                "TEST" => "Test",
                "RR" => "Receiver Ready",
                "RNR" => "Receiver Not Ready",
                "REJ" => "Reject",
                _ => ""
            };
        }

        private string GetContextDescription(PacketInfo packet)
        {
            if (!packet.L2FCSValid && packet.Direction == "RX")
                return "FCS error detected";

            return packet.Type switch
            {
                "SABM" or "SABME" => "Link setup from peer",
                "UA" => "Link established",
                "DISC" => "Link disconnection",
                "DM" => "Link disconnected",
                "FRMR" => "Frame error detected",
                "RR" => "Acknowledgment",
                "RNR" => "Flow control - not ready",
                "REJ" => "Retransmission request",
                _ when packet.L3Type == "CALL-REQ" => "VC setup request",
                _ when packet.L3Type == "CALL-CONF" => "VC established",
                _ when packet.L3Type == "DATA" => "User data",
                _ when packet.L3Type == "CLR-IND" => "VC clearing",
                _ when packet.L2Type == "I" => "Information transfer",
                _ => ""
            };
        }

        private void ShowPacketDetailsInView(PacketInfo packet)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ID: #{packet.PacketNumber:D3}");
            sb.AppendLine($"Timestamp: {packet.Timestamp:HH:mm:ss.fff}");
            sb.AppendLine($"Direction: {packet.Direction}");
            sb.AppendLine($"Type: {packet.Type}");
            sb.AppendLine();

            // DEBUG: Two-line display iterator information
            sb.AppendLine("=== ITERATOR DEBUG INFO ===");
            sb.AppendLine($"HistoryItem present: {packet.HistoryItem != null}");
            if (packet.HistoryItem != null)
            {
                sb.AppendLine($"HistoryItem.Id: {packet.HistoryItem.Id}");
                sb.AppendLine($"HistoryItem.IsMainLine: {packet.HistoryItem.IsMainLine}");
                sb.AppendLine($"HistoryItem.IsSecondaryLine: {packet.HistoryItem.IsSecondaryLine}");
                sb.AppendLine($"HistoryItem.RelatedItem: {(packet.HistoryItem.RelatedItem != null ? $"Present (ID: {packet.HistoryItem.RelatedItem.Id})" : "NULL")}");
                if (packet.HistoryItem.RelatedItem != null)
                {
                    sb.AppendLine($"RelatedItem.Layer: {packet.HistoryItem.RelatedItem.Layer}");
                    sb.AppendLine($"RelatedItem.IsMainLine: {packet.HistoryItem.RelatedItem.IsMainLine}");
                    sb.AppendLine($"RelatedItem.IsSecondaryLine: {packet.HistoryItem.RelatedItem.IsSecondaryLine}");
                    sb.AppendLine($"Relationship: {packet.HistoryItem.RelationshipType}");
                }
            }

            // DEBUG: Test PacketDisplayIterator with this single item
            if (packet.HistoryItem != null)
            {
                sb.AppendLine();
                sb.AppendLine("=== ITERATOR TEST ===");
                var testList = new List<HistoryItem> { packet.HistoryItem };
                if (packet.HistoryItem.RelatedItem != null)
                {
                    testList.Add(packet.HistoryItem.RelatedItem);
                }

                var displayPackets = new List<DisplayPacket>(PacketDisplayIterator.GetDisplayPackets(testList));
                sb.AppendLine($"Iterator returned {displayPackets.Count} display packets:");
                for (int i = 0; i < displayPackets.Count; i++)
                {
                    var dp = displayPackets[i];
                    sb.AppendLine($"  [{i}] SharedPacketId: {dp.SharedPacketId}, IsIndented: {dp.IsIndented}, Layer: {dp.Layer}");
                }
            }

            // DEBUG: PacketInfo relationship data
            sb.AppendLine();
            sb.AppendLine("=== PACKETINFO LEGACY INFO ===");
            sb.AppendLine($"PacketInfo.RelatedPacket: {(packet.RelatedPacket != null ? $"Present (#{packet.RelatedPacket.PacketNumber})" : "NULL")}");
            if (packet.RelatedPacket != null)
            {
                sb.AppendLine($"Related.Type: {packet.RelatedPacket.Type}");
                sb.AppendLine($"Related.Direction: {packet.RelatedPacket.Direction}");
                sb.AppendLine($"PacketInfo.RelationshipType: {packet.RelationshipType}");
            }
            sb.AppendLine("=============================");
            sb.AppendLine();

            // Enhanced packet analysis based on type
            if (packet.Type == "LAPB" && packet.Data.Length >= 2)
            {
                byte address = packet.Data[0];
                byte control = packet.Data[1];

                sb.AppendLine($"LAPB Analysis:");
                sb.AppendLine($"  Address: 0x{address:X2} ({GetAddressName(address)})");
                sb.AppendLine($"  Control: 0x{control:X2}");

                // Determine frame type
                if ((control & 0x01) == 0x00)
                {
                    // I-frame
                    byte ns = (byte)((control >> 1) & 0x07);
                    byte nr = (byte)((control >> 5) & 0x07);
                    bool pf = (control & 0x10) != 0;
                    sb.AppendLine($"  Frame Type: I-Frame");
                    sb.AppendLine($"  N(S): {ns}, N(R): {nr}, P/F: {pf}");
                }
                else if ((control & 0x03) == 0x01)
                {
                    // S-frame
                    byte nr = (byte)((control >> 5) & 0x07);
                    byte type = (byte)(control & 0x0F);
                    bool pf = (control & 0x10) != 0;
                    string sType = type switch
                    {
                        0x01 => "RR",
                        0x05 => "RNR",
                        0x09 => "REJ",
                        _ => $"0x{type:X2}"
                    };
                    sb.AppendLine($"  Frame Type: S-Frame ({sType})");
                    sb.AppendLine($"  N(R): {nr}, P/F: {pf}");
                }
                else if ((control & 0x03) == 0x03)
                {
                    // U-frame
                    byte type = (byte)(control & 0xEF); // Remove P/F bit
                    bool pf = (control & 0x10) != 0;
                    string uType = type switch
                    {
                        0x2F => "SABM",
                        0x63 => "UA",
                        0x43 => "DISC",
                        0x53 => "DM",
                        0x87 => "FRMR",
                        _ => $"0x{type:X2}"
                    };
                    sb.AppendLine($"  Frame Type: U-Frame ({uType})");
                    sb.AppendLine($"  P/F: {pf}");

                    // Show machine ID if present
                    if (packet.Data.Length >= 4 && (type == 0x63 || type == 0x2F)) // UA or SABM
                    {
                        ushort machineId = (ushort)((packet.Data[2] << 8) | packet.Data[3]);
                        sb.AppendLine($"  Machine ID: 0x{machineId:X4} ({machineId})");
                    }
                }

                // Note: This is pure LAPB frame data (no HDLC flags or FCS)
                sb.AppendLine($"  Note: Pure LAPB frame (no HDLC flags/FCS)");
            }
            else if (packet.Type == "HDLC")
            {
                // For HDLC frames, show FCS analysis
                // Note: This should only happen for pure HDLC frames, not LAPB/X.25 frames
                if (packet.Data.Length >= 4)
                {
                    ushort receivedFCS = (ushort)(packet.Data[packet.Data.Length - 2] | (packet.Data[packet.Data.Length - 1] << 8));

                    // Validate FCS using the shared CRC-16 helper (Stack A HDLC layer retired).
                    ushort calculatedFCS = RetroIO.Link.Lapb.Fcs16.Compute(packet.Data.AsSpan(0, packet.Data.Length - 2));
                    bool isValid = (receivedFCS == calculatedFCS);

                    sb.AppendLine($"HDLC Analysis:");
                    sb.AppendLine($"  FCS: 0x{receivedFCS:X4} {(isValid ? "(VALID)" : "(INVALID)")}");
                }
            }

            sb.AppendLine($"Length: {packet.Length} bytes");
            sb.AppendLine($"Description: {packet.Description}");
            sb.AppendLine();
            sb.AppendLine("Hex dump:");

            // Build hex string manually
            var hexList = new List<string>();
            foreach (var b in packet.Data)
            {
                hexList.Add($"{b:X2}");
            }
            var allHex = string.Join(" ", hexList);
            sb.AppendLine(allHex);

            _packetDetailsView.Text = sb.ToString();
        }

        private string GetAddressName(byte address)
        {
            return address switch
            {
                0x01 => "DCE",
                0x03 => "DTE",
                _ => $"Unknown"
            };
        }

        private void ShowPacketDetails(PacketInfo packet)
        {
            var detailWindow = new Window($"Packet Details - {packet.Timestamp:HH:mm:ss.fff}")
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 80,
                Height = 20
            };

            var textView = new TextView()
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 3,
                ReadOnly = true
            };

            var sb = new StringBuilder();
            sb.AppendLine($"Type: {packet.Type}");
            sb.AppendLine($"Length: {packet.Length} bytes");
            sb.AppendLine($"Description: {packet.Description}");
            sb.AppendLine();
            sb.AppendLine("Hex dump:");

            for (int i = 0; i < Math.Min(packet.Data.Length, 64); i++)
            {
                if (i % 16 == 0) sb.AppendLine();
                sb.Append($"{packet.Data[i]:X2} ");
            }

            textView.Text = sb.ToString();

            var closeButton = new Button("Close")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(textView) + 1
            };
            closeButton.Clicked += () => detailWindow.Visible = false;

            detailWindow.Add(textView, closeButton);
            Application.Top.Add(detailWindow);
        }


        private void SimulateReceivePacket(string hexString)
        {
            try
            {
                // Parse hex string like "0x01 0x3F 0x00 0x64"
                var hexValues = hexString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var packet = new List<byte>();

                foreach (var hex in hexValues)
                {
                    string cleanHex = hex.Trim();
                    if (cleanHex.StartsWith("0x"))
                        cleanHex = cleanHex.Substring(2);

                    if (byte.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, null, out byte value))
                    {
                        packet.Add(value);
                    }
                }

                if (packet.Count > 0)
                {
                    // Use simulation method that creates unified packets
                    _logger.LogInformation("Simulating received packet: {HexString}", hexString);

                    if (_runtime != null)
                    {
                        var frameData = packet.ToArray();
                        // Build byte string manually
                        var byteList = new List<string>();
                        for (int i = 0; i < packet.Count; i++)
                        {
                            byteList.Add($"0x{packet[i]:X2}");
                        }
                        _logger.LogDebug(">>> DEBUG: Simulating LAPB received frame with {Count} bytes: {Bytes}", packet.Count, string.Join(" ", byteList));

                        // Use simulation method that creates unified packets properly
                        _runtime.SimulateLapbFrame(frameData, frameData.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error simulating packet: {Message}", ex.Message);
            }
        }


        private byte[] CreateCallRequestPacket(ushort lcn, string calledAddr, string callingAddr)
        {
            List<byte> packet = new List<byte>();

            // Called address
            packet.Add((byte)calledAddr.Length);
            foreach (char c in calledAddr)
            {
                packet.Add((byte)c);
            }

            // Calling address
            packet.Add((byte)callingAddr.Length);
            foreach (char c in callingAddr)
            {
                packet.Add((byte)c);
            }

            return packet.ToArray();
        }

        private void OnIncomingCall(object? sender, X25PacketEventArgs e)
        {
            // Process UI update immediately (event-driven)
            ProcessPacketImmediately(new PacketInfo
            {
                Timestamp = DateTime.Now,
                Type = "CALL",
                Description = $"Incoming call on LCN {e.Packet.LCN}"
            });

            // Accept the call immediately (protocol logic)
            _x25?.AcceptCall(e.Packet.LCN);
        }

        private void OnData(object? sender, X25PacketEventArgs e)
        {
            string dataStr = e.GetDataString();

            // Process UI update immediately (event-driven)
            ProcessPacketImmediately(new PacketInfo
            {
                Timestamp = DateTime.Now,
                Type = "DATA",
                Description = $"LCN {e.Packet.LCN}: {dataStr}"
            });
        }

        private void OnClear(object? sender, X25PacketEventArgs e)
        {
            // Process UI update immediately (event-driven)
            ProcessPacketImmediately(new PacketInfo
            {
                Timestamp = DateTime.Now,
                Type = "CALL",
                Description = $"Call cleared on LCN {e.Packet.LCN}"
            });
        }

        private void OnReset(object? sender, X25PacketEventArgs e)
        {
            // Process UI update immediately (event-driven)
            ProcessPacketImmediately(new PacketInfo
            {
                Timestamp = DateTime.Now,
                Type = "CALL",
                Description = $"Call reset on LCN {e.Packet.LCN}"
            });
        }

        private void OnRestart(object? sender, X25PacketEventArgs e)
        {
            // Process UI update immediately (event-driven)
            ProcessPacketImmediately(new PacketInfo
            {
                Timestamp = DateTime.Now,
                Type = "CALL",
                Description = "Restart requested"
            });
        }

        private void ResetStatistics()
        {
            // Reset packet numbering only - statistics are now handled by ProtocolRuntime
            Interlocked.Exchange(ref _nextPacketNumber, 0);
            _logger.LogInformation("Statistics reset (packet numbering)");
        }

        private void ClearPacketHistory()
        {
            // We're already in the main loop when this is called from key bindings
            _packetHistory.Clear();
            _packetListView.SetSource(new List<string>());
        }

        private void ShowLogWindow()
        {
            try
            {
                if (_logWindow.Visible)
                {
                    // If window is already visible, close it
                    _logWindow.Visible = false;
                    _mainWindow.SetFocus();
                    RefreshWindowsMenu(); // Update menu to reflect window state
                }
                else
                {
                    // Make sure the log window is visible and bring it to front
                    _logWindow.Visible = true;
                    Application.Top.BringSubviewToFront(_logWindow);

                    // Refresh the display
                    RefreshLogDisplay();

                    // Add a test message to verify the window is working
                    if (_logTextView != null && string.IsNullOrEmpty(_logTextView.Text.ToString()))
                    {
                        _logTextView.Text = "Log window is working! Press F7 again to close.\n";
                    }

                    // Don't set focus to the log window - let the main window keep focus
                    // This prevents the app from hanging waiting for input
                    RefreshWindowsMenu(); // Update menu to reflect window state                                        
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing log window: {Message}", ex.Message);
                // Fallback: just show the window without refreshing
                _logWindow.Visible = true;
                Application.Top.BringSubviewToFront(_logWindow);
            }
        }

        private void ShowStatusWindow()
        {
            _statusWindow.Visible = true;
        }

        private void ShowPacketWindow()
        {
            if (_packetWindow == null) return;

            if (_packetWindow.Visible)
            {
                _packetWindow.Visible = false;
                _mainWindow.SetFocus();
                RefreshWindowsMenu(); // Update menu to reflect window state
            }
            else
            {
                _packetWindow.Visible = true;
                Application.Top.BringSubviewToFront(_packetWindow);
                _packetWindow.SetFocus();
                RefreshWindowsMenu(); // Update menu to reflect window state

                // Update list immediately with proper UI refresh
                if (_packetListView != null)
                {
                    var items = new List<string>();
                    for (int i = _packetHistory.Count - 1; i >= 0; i--)
                    {
                        var packet = _packetHistory[i];
                        items.Add(FormatPacketDisplayLine(packet));
                    }
                    _packetListView.SetSource(items);

                    // Force UI refresh
                    _packetListView.SetNeedsDisplay();
                    _packetWindow.SetNeedsDisplay();
                    Application.Refresh();
                }
            }
        }

        private void ShowPacketWindowV2()
        {
            if (_packetWindowV2 == null)
            {
                _packetWindowV2 = new PacketWindowV2(_runtime!);
                Application.Top.Add(_packetWindowV2);
                RefreshWindowsMenu(); // Update menu to reflect window state
            }

            if (_packetWindowV2.Visible)
            {
                _packetWindowV2.Visible = false;
                _mainWindow.SetFocus();
                RefreshWindowsMenu(); // Update menu to reflect window state
            }
            else
            {
                _packetWindowV2.Visible = true;
                Application.Top.BringSubviewToFront(_packetWindowV2);
                _packetWindowV2.SetFocus();
                RefreshWindowsMenu(); // Update menu to reflect window state
            }
        }

        // Filter methods
        private void SetDirectionFilter(string direction)
        {
            _currentDirectionFilter = direction;
            RefreshPacketDisplay();
        }

        private void ToggleL2Filter(string type)
        {
            switch (type)
            {
                case "I": _showIFrames = !_showIFrames; break;
                case "S": _showSFrames = !_showSFrames; break;
                case "U": _showUFrames = !_showUFrames; break;
            }
            RefreshPacketDisplay();
        }

        private void ToggleL3Filter(string type)
        {
            switch (type)
            {
                case "DATA": _showDataFrames = !_showDataFrames; break;
                case "CALL": _showCallFrames = !_showCallFrames; break;
                case "CLEAR": _showClearFrames = !_showClearFrames; break;
                case "RESET": _showResetFrames = !_showResetFrames; break;
                case "RESTART": _showRestartFrames = !_showRestartFrames; break;
                case "INT": _showIntFrames = !_showIntFrames; break;
            }
            RefreshPacketDisplay();
        }

        private void ToggleLCNFilter()
        {
            if (_currentLCNFilter == null)
            {
                // Set to current selected packet's LCN if available
                if (_packetListView.SelectedItem >= 0 && _packetListView.SelectedItem < _packetHistory.Count)
                {
                    var packet = _packetHistory[_packetHistory.Count - 1 - _packetListView.SelectedItem];
                    _currentLCNFilter = packet.L3LCN;
                }
            }
            else
            {
                _currentLCNFilter = null;
            }
            RefreshPacketDisplay();
        }

        private void ToggleErrorsFilter()
        {
            _showErrorsOnly = !_showErrorsOnly;
            RefreshPacketDisplay();
        }

        private void ToggleCollapse()
        {
            if (_displayManager != null)
            {
                var newState = _displayManager.ToggleGlobalCollapse();
                RefreshPacketDisplay();
                _logger?.LogInformation($"Collapse toggled: {(newState ? "Collapsed" : "Expanded")}");
            }
        }

        private void OnCollapseStateChanged(object? sender, bool collapsed)
        {
            if (_collapseToggleButton != null)
            {
                _collapseToggleButton.Text = collapsed ? "[Expand]" : "[Collapse]";
            }
        }

        /// <summary>
        /// Show debug information about collapse functionality
        /// </summary>
        private void ShowDebugInfo()
        {
            if (_displayManager == null) return;

            var debugInfo = _displayManager.GetDebugInfo();

            // Create a debug window
            var debugWindow = new Window("Collapse Debug Info")
            {
                X = 5,
                Y = 5,
                Width = Dim.Fill() - 10,
                Height = Dim.Fill() - 10
            };

            var debugText = new TextView()
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 4,
                ReadOnly = true,
                Text = debugInfo
            };

            var closeButton = new Button("Close")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(debugText) + 1
            };
            closeButton.Clicked += () =>
            {
                debugWindow.Visible = false;
                Application.Top.Remove(debugWindow);
                _packetWindow?.SetFocus();
            };

            debugWindow.Add(debugText, closeButton);
            Application.Top.Add(debugWindow);
            debugWindow.SetFocus();
        }

        /// <summary>
        /// Create a test chain to demonstrate collapse functionality
        /// </summary>
        private void CreateTestChain()
        {
            if (_displayManager == null) return;

            _displayManager.CreateTestCollapseChain();
            RefreshPacketDisplay();

            MessageBox.Query("Test Chain", "Created test chain relationships.\nTry toggling collapse now!", "OK");
        }

        private void RefreshPacketDisplay()
        {
            // Only update if window is visible
            if (_packetWindow?.Visible == true)
            {
                UpdatePacketListView();
            }
        }

        private void UpdatePacketListView()
        {
            if (_packetListView == null || _runtime == null) return;

            var items = new List<string>();

            // DEBUG: Log that we're updating the packet list
            Debug.WriteLine($"[DEBUG] UpdatePacketListView: Processing {_packetHistory.Count} PacketInfo items (Collapsed: {_displayManager?.GlobalCollapseEnabled ?? false})");

            // Get HistoryItems from PacketInfo (reverse order for newest first)
            var historyItems = new List<HistoryItem>();
            int nullHistoryItems = 0;
            for (int i = _packetHistory.Count - 1; i >= 0; i--)
            {
                var packetInfo = _packetHistory[i];
                if (packetInfo.HistoryItem != null)
                {
                    historyItems.Add(packetInfo.HistoryItem);
                }
                else
                {
                    nullHistoryItems++;
                }
            }

            Debug.WriteLine($"[DEBUG] Found {historyItems.Count} HistoryItems, {nullHistoryItems} null HistoryItems");

            // Use the new PacketDisplayManager system instead of the old PacketDisplayIterator
            if (_displayManager != null)
            {
                // Create PacketDisplayItems from HistoryItems
                var allDisplayItems = new List<PacketDisplayItem>();
                for (int i = 0; i < historyItems.Count; i++)
                {
                    var historyItem = historyItems[i];
                    allDisplayItems.Add(new PacketDisplayItem(historyItem, _runtime));
                }

                // Get filtered display items based on collapse state
                var displayItems = _displayManager.GlobalCollapseEnabled ?
                    _displayManager.FilterForCollapsedView(allDisplayItems) :
                    allDisplayItems;

                Debug.WriteLine($"[DEBUG] PacketDisplayManager returned {displayItems.Count} display items (from {allDisplayItems.Count} total)");

                // Format display items
                for (int i = 0; i < displayItems.Count; i++)
                {
                    var displayItem = displayItems[i];
                    var formattedLine = displayItem.GetDisplayText(_displayManager);

                    // Add line numbering (using packet ID)
                    formattedLine = $"{displayItem.Packet.Id,3}: {formattedLine}";
                    Debug.WriteLine($"[DEBUG] [{i}] Display item (Id: {displayItem.Packet.Id}): {formattedLine}");

                    items.Add(formattedLine);
                }
            }
            else
            {
                // Fallback to old system if no display manager
                var displayPackets = new List<DisplayPacket>(PacketDisplayIterator.GetDisplayPackets(historyItems));
                Debug.WriteLine($"[DEBUG] Fallback: PacketDisplayIterator returned {displayPackets.Count} display packets");

                for (int i = 0; i < displayPackets.Count; i++)
                {
                    var displayPacket = displayPackets[i];
                    string formattedLine;

                    if (displayPacket.IsIndented)
                    {
                        formattedLine = $"   ==>  {FormatDisplayPacketLine(displayPacket)}";
                    }
                    else
                    {
                        formattedLine = $"{displayPacket.SharedPacketId,3}: {FormatDisplayPacketLine(displayPacket)}";
                    }

                    items.Add(formattedLine);
                }
            }

            Debug.WriteLine($"[DEBUG] Setting {items.Count} items to packet list view");
            _packetListView.SetSource(items);

            // Force UI refresh
            _packetListView.SetNeedsDisplay();
            Application.Refresh();
        }

        /// <summary>
        /// Apply collapse filtering to display packets using the collapse logic
        /// </summary>
        private List<DisplayPacket> ApplyCollapseFiltering(List<DisplayPacket> displayPackets)
        {
            if (_displayManager == null) return displayPackets;

            var filteredPackets = new List<DisplayPacket>();

            // Simple collapse logic: only show packets that would be terminal or error packets
            for (int i = 0; i < displayPackets.Count; i++)
            {
                var packet = displayPackets[i];
                var relationship = _runtime?.GetPacketRelationship(packet.HistoryItem.Id);

                if (relationship != null)
                {
                    // Always show error packets
                    if (relationship.HasProcessingError)
                    {
                        filteredPackets.Add(packet);
                    }
                    // Show terminal packets (last in successful chains)
                    else if (relationship.IsTerminal)
                    {
                        filteredPackets.Add(packet);
                    }
                    // For non-collapsed mode, show all (this branch won't be hit when collapsed)
                }
                else
                {
                    // If no relationship info, show the packet (fallback)
                    filteredPackets.Add(packet);
                }
            }

            return filteredPackets;
        }

        /// <summary>
        /// Apply GUI filter settings to display items (on top of collapse filtering)
        /// </summary>
        private List<PacketDisplayItem> ApplyGuiFilters(List<PacketDisplayItem> items)
        {
            var filteredItems = new List<PacketDisplayItem>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                // Skip if direction filter doesn't match
                if (_currentDirectionFilter != "All")
                {
                    var direction = item.Packet.Direction == Direction.TX ? "TX" : "RX";
                    if (direction != _currentDirectionFilter) continue;
                }

                // Skip if errors only mode and item has no error
                if (_showErrorsOnly)
                {
                    var relationship = _runtime?.GetPacketRelationship(item.Packet.Id);
                    if (relationship?.HasProcessingError != true) continue;
                }

                // Apply frame type filters (simplified - using item description)
                var description = item.Packet.Description.ToUpper();

                if (description.Contains("I-FRAME") && !_showIFrames) continue;
                if ((description.Contains("RR") || description.Contains("RNR") || description.Contains("REJ")) && !_showSFrames) continue;
                if ((description.Contains("SABM") || description.Contains("UA") || description.Contains("DM") || description.Contains("DISC")) && !_showUFrames) continue;
                if (description.Contains("DATA") && !_showDataFrames) continue;
                if (description.Contains("CALL") && !_showCallFrames) continue;
                if (description.Contains("CLEAR") && !_showClearFrames) continue;
                if (description.Contains("RESET") && !_showResetFrames) continue;
                if (description.Contains("RESTART") && !_showRestartFrames) continue;
                if (description.Contains("INTERRUPT") && !_showIntFrames) continue;

                // Apply LCN filter if set
                if (_currentLCNFilter.HasValue)
                {
                    // This would need access to LCN info - for now skip this filter on PacketDisplayItems
                    // TODO: Enhance PacketDisplayItem with LCN information if needed
                }

                filteredItems.Add(item);
            }

            return filteredItems;
        }

        /// <summary>
        /// Format a single display item line using PacketDisplayManager
        /// </summary>
        private string FormatDisplayItemLine(PacketDisplayItem displayItem)
        {
            // Use the PacketDisplayManager to get formatted text with visual indicators
            var displayText = displayItem.GetDisplayText(_displayManager);
            return displayText;
        }

        /// <summary>
        /// Format a single display packet line (simplified from complex PacketInfo logic)
        /// </summary>
        private string FormatDisplayPacketLine(DisplayPacket displayPacket)
        {
            var item = displayPacket.HistoryItem;
            var timestamp = item.Timestamp.ToString("HH:mm:ss.fff");
            var direction = item.Direction == Direction.TX ? "TX" : "RX";
            var layer = item.Layer.ToString().PadRight(4);
            var description = item.Description;
            var length = item.Length;

            return $"{timestamp} {direction} {layer} {description} ({length} bytes)";
        }


        private void ShowAboutDialog()
        {
            var aboutDialog = new Dialog("About X.25 Protocol Emulator", 60, 15)
            {
                X = Pos.Center(),
                Y = Pos.Center()
            };

            var textView = new TextView()
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 3,
                ReadOnly = true,
                Text = "X.25 Protocol Emulator v1.0\n\n" +
                       "A comprehensive X.25 protocol stack emulator\n" +
                       "with HDLC, LAPB, and X.25 layers.\n\n" +
                       "Features:\n" +
                       "• Real-time protocol monitoring\n" +
                       "• Packet analysis and logging\n" +
                       "• X.25 call management\n" +
                       "• Data transfer capabilities\n\n" +
                       "Built with Terminal.GUI and .NET 9.0\n\n" +
                       "TEST: If you can see this dialog, the GUI is working!"
            };

            var closeButton = new Button("Close")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(textView) + 1
            };
            closeButton.Clicked += () => aboutDialog.Visible = false;

            aboutDialog.Add(textView, closeButton);
            Application.Top.Add(aboutDialog);
        }

        private void ShowHelpDialog()
        {
            var helpDialog = new Dialog("Keyboard Shortcuts", 70, 20)
            {
                X = Pos.Center(),
                Y = Pos.Center()
            };

            var textView = new TextView()
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 3,
                ReadOnly = true,
                Text = "Keyboard Shortcuts:\n\n" +
                       "Function Keys:\n" +
                       "F1 - Show Status Window\n" +
                       "F2 - Toggle Packet Window\n" +
                       "F5 - Reset Statistics\n" +
                       "F6 - Clear Packet History\n" +
                       "F7 - Toggle Log Window\n\n" +
                       "Control Keys:\n" +
                       "Ctrl+Q - Quit Application\n" +
                       "Ctrl+H - Show Help\n" +
                       "Ctrl+A - Show About\n\n" +
                       "Navigation:\n" +
                       "Tab - Next control\n" +
                       "Shift+Tab - Previous control\n" +
                       "Enter - Activate button/select item\n" +
                       "Escape - Close dialogs\n\n" +
                       "Menu Access:\n" +
                       "Alt+Letter - Access menu items\n" +
                       "Arrow Keys - Navigate menus\n\n" +
                       "TEST: If you can see this dialog, the GUI is working!"
            };

            var closeButton = new Button("Close")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(textView) + 1
            };
            closeButton.Clicked += () => helpDialog.Visible = false;

            helpDialog.Add(textView, closeButton);
            Application.Top.Add(helpDialog);
        }

        private void SetupKeyBindings()
        {
            // Set up key bindings on the main window
            _mainWindow.KeyPress += (e) =>
            {
                _logger.LogDebug("Key pressed: {Key}", e.KeyEvent.Key);

                switch (e.KeyEvent.Key)
                {
                    case Key.F1:
                        ShowStatusWindow();
                        e.Handled = true;
                        break;
                    case Key.F2:
                        ShowPacketWindow();
                        e.Handled = true;
                        break;
                    case Key.F5:
                        ResetStatistics();
                        e.Handled = true;
                        break;
                    case Key.F6:
                        ClearPacketHistory();
                        e.Handled = true;
                        break;
                    case Key.F7:
                        ShowLogWindow();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.Q:
                        Application.RequestStop();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.H:
                        ShowHelpDialog();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.A:
                        _logger.LogInformation("Ctrl+A pressed - showing about");
                        ShowAboutDialog();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.L:
                        _logger.LogInformation("Ctrl+L pressed - showing link status");
                        ShowLinkStatusWindow();
                        e.Handled = true;
                        break;
                    case Key.AltMask | Key.C:
                        _logger.LogInformation("Alt+C pressed - toggling collapse");
                        ToggleCollapse();
                        e.Handled = true;
                        break;
                }
            };

            // Also set up on Application.Top as backup
            Application.Top.KeyPress += (e) =>
            {
                switch (e.KeyEvent.Key)
                {
                    case Key.F1:
                        ShowStatusWindow();
                        e.Handled = true;
                        break;
                    case Key.F2:
                        ShowPacketWindow();
                        e.Handled = true;
                        break;
                    case Key.F5:
                        ResetStatistics();
                        e.Handled = true;
                        break;
                    case Key.F6:
                        ClearPacketHistory();
                        e.Handled = true;
                        break;
                    case Key.F7:
                        ShowLogWindow();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.Q:
                        Application.RequestStop();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.H:
                        ShowHelpDialog();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.A:
                        ShowAboutDialog();
                        e.Handled = true;
                        break;
                    case Key.CtrlMask | Key.L:
                        ShowLinkStatusWindow();
                        e.Handled = true;
                        break;
                }
            };
        }

        private void OnLogEntryAdded(LogEntry entry)
        {
            // Update log window if visible (event-driven)
            if (_logWindow?.Visible == true)
            {
                AppendLogEntry(entry);
            }
        }

        private void CycleLogFilter()
        {
            _currentLogFilter = (_currentLogFilter + 1) % 5; // Cycle through 0,1,2,3,4

            string[] filterNames = { "All", "Debug", "Info", "Warning", "Error" };
            _logLevelFilter.Text = filterNames[_currentLogFilter];

            RefreshLogDisplay();
        }

        private void RefreshLogDisplay()
        {
            try
            {
                if (_logTextView == null)
                {
                    _logger.LogError("Log text view is null");
                    return;
                }

                var entries = _historyProvider?.GetLogEntries() ?? new List<LogEntry>();
                var filteredEntries = new List<LogEntry>();

                // Apply filter
                // _currentLogFilter: 0="All", 1="Debug", 2="Info", 3="Warning", 4="Error"
                // LogLevel: Debug=0, Info=1, Warning=2, Error=3
                if (_currentLogFilter > 0)
                {
                    var minLevel = (RetroIO.Protocols.X25.Common.LogLevel)(_currentLogFilter - 1);
                    filteredEntries = entries.FindAll(entry => entry.Level >= minLevel);
                }
                else
                {
                    // "All" selected - show all entries
                    filteredEntries = entries;
                }

                // Build display text
                var sb = new StringBuilder();
                foreach (var entry in filteredEntries)
                {
                    sb.AppendLine(entry.FullMessage);
                }

                _logTextView.Text = sb.ToString();
                _logTextView.CursorPosition = new Point(0, _logTextView.Lines - 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing log display: {Message}", ex.Message);
            }
        }

        private void AppendLogEntry(LogEntry entry)
        {
            try
            {
                if (_logTextView == null)
                {
                    return;
                }

                // Check if entry should be shown based on current filter
                bool shouldShow = true;
                if (_currentLogFilter > 0)
                {
                    var minLevel = (RetroIO.Protocols.X25.Common.LogLevel)(_currentLogFilter - 1);
                    shouldShow = entry.Level >= minLevel;
                }
                // If "All" is selected (_currentLogFilter = 0), always show

                if (shouldShow)
                {
                    _logTextView.Text += entry.FullMessage + Environment.NewLine;
                    _logTextView.CursorPosition = new Point(0, _logTextView.Lines - 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error appending log entry: {Message}", ex.Message);
            }
        }

        private void ClearLog()
        {
            _historyProvider?.ClearHistory();
            _logTextView.Text = "";
        }

        private void ExportLog()
        {
            // Add immediate visual feedback
            _exportLogButton.Text = "Exporting...";
            Application.Refresh();

            // Add immediate debug logging to verify the method is called
            _logger?.LogInformation("Export button clicked - starting export process");

            try
            {
                _logger?.LogInformation("Retrieving log entries from history provider");
                var entries = _historyProvider?.GetLogEntries() ?? new List<LogEntry>();
                _logger?.LogInformation("Retrieved {Count} log entries", entries.Count);

                // Apply current filter to exported entries
                var filteredEntries = new List<LogEntry>();
                if (_currentLogFilter > 0)
                {
                    var minLevel = (RetroIO.Protocols.X25.Common.LogLevel)(_currentLogFilter - 1);
                    filteredEntries = entries.FindAll(entry => entry.Level >= minLevel);
                    _logger?.LogInformation("Applied filter {Filter}, filtered to {Count} entries", _currentLogFilter, filteredEntries.Count);
                }
                else
                {
                    filteredEntries = entries;
                    _logger?.LogInformation("No filter applied, using all {Count} entries", entries.Count);
                }

                // Generate timestamped filename
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var filename = $"X25Emulator_logs_{timestamp}.txt";
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var filePath = Path.Combine(desktopPath, filename);
                _logger?.LogInformation("Export file path: {FilePath}", filePath);

                // Build export content
                var sb = new StringBuilder();

                // Add header information
                string[] filterNames = { "All", "Debug", "Info", "Warning", "Error" };
                sb.AppendLine($"X.25 Emulator Log Export");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Filter Applied: {filterNames[_currentLogFilter]}");
                sb.AppendLine($"Total Entries: {filteredEntries.Count}");
                sb.AppendLine(new string('=', 60));
                sb.AppendLine();

                // Add log entries
                foreach (var entry in filteredEntries)
                {
                    sb.AppendLine(entry.FullMessage);
                }

                // Write to file
                _logger?.LogInformation("Writing export content to file");
                File.WriteAllText(filePath, sb.ToString());
                _logger?.LogInformation("File written successfully");

                // Show confirmation message
                _logger?.LogInformation("Showing export confirmation dialog");
                ShowExportConfirmation(filePath, filteredEntries.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error exporting log: {Message}", ex.Message);
                ShowExportError(ex.Message);
            }
            finally
            {
                // Reset button text
                _exportLogButton.Text = "Export";
                Application.Refresh();
            }
        }

        private void ShowExportConfirmation(string filePath, int entryCount)
        {
            var confirmWindow = new Window("Export Successful")
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 70,
                Height = 12
            };

            var message = new Label($"Log successfully exported!\n\nEntries exported: {entryCount}\nFile saved to:\n{filePath}")
            {
                X = 2,
                Y = 1,
                Width = Dim.Fill() - 4,
                Height = 6
            };

            var okButton = new Button("OK")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(message) + 2
            };
            okButton.Clicked += () =>
            {
                Application.RequestStop();
            };

            confirmWindow.Add(message, okButton);

            // Run the window as a modal dialog
            Application.Run(confirmWindow);
        }

        private void ShowExportError(string errorMessage)
        {
            var errorWindow = new Window("Export Failed")
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 60,
                Height = 10
            };

            var message = new Label($"Failed to export log:\n\n{errorMessage}")
            {
                X = 2,
                Y = 1,
                Width = Dim.Fill() - 4,
                Height = 4
            };

            var okButton = new Button("OK")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(message) + 1
            };
            okButton.Clicked += () =>
            {
                Application.RequestStop();
            };

            errorWindow.Add(message, okButton);

            // Run the window as a modal dialog
            Application.Run(errorWindow);
        }

        private void ShowRoutesWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    _logger?.LogWarning("Cannot show routes window - runtime or logger is null");
                    return;
                }

                _logger.LogInformation("Opening X.25 routes window");

                // Create and show the routes window as a modal dialog
                var routesWindow = new RoutesWindow(_runtime, _logger);
                Application.Run(routesWindow);

                _logger.LogInformation("X.25 routes window closed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing routes window");
                // TODO: Show error dialog
            }
        }


        private void ShowPadTestWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    _logger?.LogWarning("Cannot show PAD test window - runtime or logger is null");
                    return;
                }

                _logger.LogInformation("Creating new PAD test window");

                // Create new PAD window for each connection
                var windowNumber = _padTestWindows.Count + 1;
                var padTestWindow = new PadTestWindow(_runtime, _logger, windowNumber);

                // Position windows staggered so they don't overlap completely
                padTestWindow.X = 2 + (_padTestWindows.Count * 3);
                padTestWindow.Y = 2 + (_padTestWindows.Count * 2);

                // Handle window closing to remove from both lists
                padTestWindow.Closed += (sender, e) =>
                {
                    if (sender is PadTestWindow window)
                    {
                        _padTestWindows.Remove(window);
                        UnregisterManagedWindow(window);
                        _logger.LogInformation($"PAD test window #{window.WindowNumber} removed from list");
                    }
                };

                _padTestWindows.Add(padTestWindow);
                Application.Top.Add(padTestWindow);
                RegisterManagedWindow(padTestWindow, $"PAD Test #{windowNumber}", "F10");
                Application.Top.BringSubviewToFront(padTestWindow);
                padTestWindow.SetFocus();

                _logger.LogInformation($"PAD test window #{windowNumber} created and opened");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing PAD test window");
                // TODO: Show error dialog
            }
        }

        private void ShowSintranTadClientWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    _logger?.LogWarning("Cannot show SINTRAN TAD client window - runtime or logger is null");
                    return;
                }

                _logger.LogInformation("Creating new SINTRAN TAD client window");

                // Create new SINTRAN TAD client window for each connection
                var windowNumber = _sintranTadClientWindows.Count + 1;
                var tadClientWindow = new SintranTadClientWindow(_runtime, _logger, windowNumber);

                // Position windows staggered so they don't overlap completely
                tadClientWindow.X = 2 + (_sintranTadClientWindows.Count * 3);
                tadClientWindow.Y = 2 + (_sintranTadClientWindows.Count * 2);

                // Handle window closing to remove from both lists
                tadClientWindow.Closed += (sender, e) =>
                {
                    if (sender is SintranTadClientWindow window)
                    {
                        _sintranTadClientWindows.Remove(window);
                        UnregisterManagedWindow(window);
                        _logger.LogInformation($"SINTRAN TAD client window #{window.WindowNumber} removed from list");
                    }
                };

                _sintranTadClientWindows.Add(tadClientWindow);
                Application.Top.Add(tadClientWindow);
                RegisterManagedWindow(tadClientWindow, $"SINTRAN TAD Client #{windowNumber}", "F4");
                Application.Top.BringSubviewToFront(tadClientWindow);
                tadClientWindow.SetFocus();

                _logger.LogInformation($"SINTRAN TAD client window #{windowNumber} created and opened");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing SINTRAN TAD client window");
                // TODO: Show error dialog
            }
        }

        private void ShowTadServerWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    _logger?.LogWarning("Cannot show TAD server window - runtime or logger is null");
                    return;
                }

                // Create window only once
                if (_tadServerWindow == null)
                {
                    _logger.LogInformation("Creating TAD server window");
                    _tadServerWindow = new TadServerWindow(_runtime, _logger);
                    Application.Top.Add(_tadServerWindow);
                    RegisterManagedWindow(_tadServerWindow, "TAD Server", "F11");
                }

                // Toggle visibility like other windows
                if (_tadServerWindow.Visible)
                {
                    _tadServerWindow.Visible = false;
                    _mainWindow.SetFocus();
                    RefreshWindowsMenu();
                    _logger.LogInformation("TAD server window closed");
                }
                else
                {
                    _tadServerWindow.Visible = true;
                    Application.Top.BringSubviewToFront(_tadServerWindow);
                    _tadServerWindow.SetFocus();
                    _tadServerWindow.SetNeedsDisplay();
                    RefreshWindowsMenu();
                    _logger.LogInformation("TAD server window opened");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing TAD server window");
            }
        }

        private void ShowSintranTadServerWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    _logger?.LogWarning("Cannot show SINTRAN TAD server window - runtime or logger is null");
                    return;
                }

                // Create window only once
                if (_sintranTadServerWindow == null)
                {
                    _logger.LogInformation("Creating SINTRAN TAD server window");
                    _sintranTadServerWindow = new SintranTadServerWindow(_runtime, _logger);
                    Application.Top.Add(_sintranTadServerWindow);
                    RegisterManagedWindow(_sintranTadServerWindow, "SINTRAN TAD Server", "Ctrl+T");
                }

                // Toggle visibility like other windows
                if (_sintranTadServerWindow.Visible)
                {
                    _sintranTadServerWindow.Visible = false;
                    _mainWindow.SetFocus();
                    RefreshWindowsMenu();
                    _logger.LogInformation("SINTRAN TAD server window closed");
                }
                else
                {
                    _sintranTadServerWindow.Visible = true;
                    Application.Top.BringSubviewToFront(_sintranTadServerWindow);
                    _sintranTadServerWindow.SetFocus();
                    _sintranTadServerWindow.SetNeedsDisplay();
                    RefreshWindowsMenu();
                    _logger.LogInformation("SINTRAN TAD server window opened");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing SINTRAN TAD server window");
            }
        }

        private void ShowXMSGServerWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    _logger?.LogWarning("Cannot show XMSG server window - runtime or logger is null");
                    return;
                }

                // Create window only once
                if (_xmsgServerWindow == null)
                {
                    _logger.LogInformation("Creating XMSG server window");
                    _xmsgServerWindow = new XMSGServerWindow(_runtime, _logger);
                    Application.Top.Add(_xmsgServerWindow);
                    RegisterManagedWindow(_xmsgServerWindow, "XMSG Server", "F12");
                }

                // Toggle visibility like other windows
                if (_xmsgServerWindow.Visible)
                {
                    _xmsgServerWindow.Visible = false;
                    _mainWindow.SetFocus();
                    RefreshWindowsMenu();
                    _logger.LogInformation("XMSG server window closed");
                }
                else
                {
                    _xmsgServerWindow.Visible = true;
                    Application.Top.BringSubviewToFront(_xmsgServerWindow);
                    _xmsgServerWindow.SetFocus();
                    _xmsgServerWindow.SetNeedsDisplay();
                    RefreshWindowsMenu();
                    _logger.LogInformation("XMSG server window opened");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing XMSG server window");
            }
        }

        private void ShowLinkStatusWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    _logger?.LogWarning("Cannot show link status window - runtime or logger is null");
                    return;
                }

                // Create window only once
                if (_linkStatusWindow == null)
                {
                    _logger.LogInformation("Creating link status window");
                    _linkStatusWindow = new LinkStatusWindow(_runtime, _logger);
                    Application.Top.Add(_linkStatusWindow);
                    RegisterManagedWindow(_linkStatusWindow, "Link Status", "Ctrl+L");
                }

                // Toggle visibility
                if (_linkStatusWindow.Visible)
                {
                    _linkStatusWindow.Visible = false;
                    _mainWindow.SetFocus();
                    RefreshWindowsMenu();
                    _logger.LogInformation("Link status window closed");
                }
                else
                {
                    _linkStatusWindow.Visible = true;
                    Application.Top.BringSubviewToFront(_linkStatusWindow);
                    _linkStatusWindow.SetFocus();
                    _linkStatusWindow.SetNeedsDisplay();
                    RefreshWindowsMenu();
                    _logger.LogInformation("Link status window opened");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing link status window");
            }
        }

        private void ShowConfigurationWindow()
        {
            try
            {
                if (_runtime == null || _logger == null)
                {
                    return;
                }

                var configWindow = new ConfigurationWindow(_runtime.Config, "x25config.json");
                Application.Top.Add(configWindow);
                RegisterManagedWindow(configWindow, "Configuration", "F9");
                configWindow.SetFocus();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing configuration window");
                // TODO: Show error dialog
            }
        }

        private void ExportPackets()
        {
            // Add immediate visual feedback
            _exportPacketsButton.Text = "Exporting...";
            Application.Refresh();

            try
            {
                var allPackets = new List<PacketInfo>(_packetHistory);

                // Apply current filters to exported packets
                var filteredPackets = ApplyCurrentFiltersToExport(allPackets);
                _logger?.LogInformation("Applied filters, filtered to {Count} packets", filteredPackets.Count);

                // Generate timestamped filename
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var baseFilename = $"X25Emulator_packets_{timestamp}";
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                // Export in multiple formats
                var csvPath = Path.Combine(desktopPath, $"{baseFilename}.csv");
                var txtPath = Path.Combine(desktopPath, $"{baseFilename}.txt");

                _logger?.LogInformation("Export file paths: CSV={CsvPath}, TXT={TxtPath}", csvPath, txtPath);

                // Build CSV export
                var csvContent = BuildCsvExport(filteredPackets);

                // Build detailed text export using ToString() methods
                var txtContent = BuildDetailedTextExport(filteredPackets);

                // Write both files
                File.WriteAllText(csvPath, csvContent.ToString());
                File.WriteAllText(txtPath, txtContent.ToString());

                // Show enhanced confirmation message
                ShowPacketExportConfirmation(csvPath, txtPath, filteredPackets.Count, allPackets.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error exporting packets: {Message}", ex.Message);
                ShowPacketExportError(ex.Message);
            }
            finally
            {
                // Reset button text
                _exportPacketsButton.Text = "Export";
                Application.Refresh();
            }
        }

        /// <summary>
        /// Apply current GUI filters to packet list for export
        /// </summary>
        private List<PacketInfo> ApplyCurrentFiltersToExport(List<PacketInfo> packets)
        {
            var filtered = packets.AsEnumerable();

            // Apply direction filter
            if (_currentDirectionFilter != "All")
            {
                // Filter by direction manually
                var directionFiltered = new List<PacketInfo>();
                foreach (var p in filtered)
                {
                    if (p.Direction == _currentDirectionFilter)
                    {
                        directionFiltered.Add(p);
                    }
                }
                filtered = directionFiltered;
            }

            // Apply L2 frame type filters (simplified - you could enhance this with your actual filter state)
            // Note: This is a basic implementation - you may need to track filter states more precisely

            // Apply L3 packet type filters (simplified)

            // Apply LCN filter if active

            // Apply error filter if active

            return new List<PacketInfo>(filtered);
        }

        /// <summary>
        /// Build CSV export content
        /// </summary>
        private StringBuilder BuildCsvExport(List<PacketInfo> packets)
        {
            var csv = new StringBuilder();

            // CSV header
            csv.AppendLine("Timestamp,PacketID,Direction,Protocol,Message,LCN,Address,Control," +
                          "NS,NR,PF,PacketType,Cause,Diagnostic,Description,RawDataLength,RawDataHex,RawDataAscii");

            // CSV data rows
            foreach (var packet in packets)
            {
                var rawHex = packet.Data != null ? BitConverter.ToString(packet.Data).Replace("-", " ") : "";
                var rawAscii = ConvertToAscii(packet.Data);
                var protocol = GetProtocolForExport(packet);
                var message = GetMessageForExport(packet);

                csv.AppendLine($"{packet.Timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                              $"{packet.PacketNumber}," +
                              $"{packet.Direction}," +
                              $"{protocol}," +
                              $"{message}," +
                              $"{packet.L3LCN?.ToString() ?? ""}," +
                              $"{packet.L2Address}," +
                              $"0x{packet.L2Control:X2}," +
                              $"{packet.L2NS}," +
                              $"{packet.L2NR}," +
                              $"{packet.L2PF}," +
                              $"{packet.L3Type}," +
                              $"{packet.L3Cause}," +
                              $"{packet.L3Diag}," +
                              $"\"{packet.Description.Replace("\"", "\"\"")}\"," +
                              $"{packet.Data?.Length ?? 0}," +
                              $"\"{rawHex}\"," +
                              $"\"{rawAscii}\"");
            }

            return csv;
        }

        /// <summary>
        /// Get protocol name for CSV export (HDLC, LAPB, X25)
        /// </summary>
        private string GetProtocolForExport(PacketInfo packet)
        {
            // Determine protocol based on packet analysis
            if (packet.Type == "HDLC" || (packet.Type == "X25" && packet.L3Type == null))
            {
                return "HDLC";
            }
            else if (!string.IsNullOrEmpty(packet.L3Type))
            {
                return "X25";
            }
            else
            {
                return "LAPB";
            }
        }

        /// <summary>
        /// Get message type for CSV export (SABM, UA, RR for LAPB; X25 message types for X25)
        /// </summary>
        private string GetMessageForExport(PacketInfo packet)
        {
            // For X25 packets, return the X25 packet type
            if (!string.IsNullOrEmpty(packet.L3Type))
            {
                return packet.L3Type;
            }

            // For LAPB packets, return the frame type (SABM, UA, RR, etc.)
            return packet.Type;
        }

        /// <summary>
        /// Convert byte array to ASCII string for export, replacing non-printable chars with dots
        /// </summary>
        private string ConvertToAscii(byte[]? data)
        {
            if (data == null || data.Length == 0)
                return "";

            var ascii = new StringBuilder();
            foreach (byte b in data)
            {
                // Convert to ASCII, use '.' for non-printable characters
                if (b >= 32 && b <= 126)
                    ascii.Append((char)b);
                else
                    ascii.Append('.');
            }
            return ascii.ToString();
        }

        /// <summary>
        /// Build detailed text export using packet ToString() methods
        /// </summary>
        private StringBuilder BuildDetailedTextExport(List<PacketInfo> packets)
        {
            var txt = new StringBuilder();

            // Add header information
            txt.AppendLine("X.25 Emulator Packet Export");
            txt.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            txt.AppendLine($"Total Packets Exported: {packets.Count}");
            txt.AppendLine($"Filter Applied: {GetCurrentFilterDescription()}");
            txt.AppendLine(new string('=', 80));
            txt.AppendLine();

            // Add detailed packet information using ToString() methods
            int packetNumber = 1;
            foreach (var packet in packets)
            {
                txt.AppendLine($"Packet #{packetNumber++} (ID: {packet.PacketNumber})");
                txt.AppendLine($"Timestamp: {packet.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                txt.AppendLine($"Direction: {packet.Direction} (Transmitted/Received)");

                txt.AppendLine($"Frame Type: {packet.Type}");

                txt.AppendLine();

                // Use packet self-decoding methods
                var packetSummary = GetPacketSummaryUsingToString(packet);
                if (!string.IsNullOrEmpty(packetSummary))
                {
                    txt.AppendLine("Protocol Analysis:");
                    txt.AppendLine(packetSummary);
                }
                else
                {
                    // Fallback to basic info
                    txt.AppendLine($"Protocol: {packet.Type}");
                    txt.AppendLine($"Description: {packet.Description}");
                }

                // Raw data
                if (packet.Data?.Length > 0)
                {
                    txt.AppendLine();
                    txt.AppendLine($"Raw Data ({packet.Data.Length} bytes):");
                    txt.AppendLine($"  Hex: {BitConverter.ToString(packet.Data).Replace("-", " ")}");

                    // ASCII representation
                    txt.Append("  ASCII: ");
                    foreach (byte b in packet.Data)
                    {
                        char c = (char)b;
                        txt.Append(char.IsControl(c) || c < 32 || c > 126 ? '.' : c);
                    }
                    txt.AppendLine();
                }

                txt.AppendLine(new string('-', 60));
                txt.AppendLine();
            }

            return txt;
        }


        /// <summary>
        /// Get current filter description for export header
        /// </summary>
        private string GetCurrentFilterDescription()
        {
            var filters = new List<string>();

            if (_currentDirectionFilter != "All")
            {
                filters.Add($"Direction: {_currentDirectionFilter}");
            }

            // Add other active filters here as needed

            return filters.Count > 0 ? string.Join(", ", filters) : "No filters applied";
        }

        /// <summary>
        /// Show enhanced packet export confirmation dialog
        /// </summary>
        private void ShowPacketExportConfirmation(string csvPath, string txtPath, int exportedCount, int totalCount)
        {
            var confirmWindow = new Window("Packet Export Successful")
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 80,
                Height = 15
            };

            var message = new Label($"Packets successfully exported in multiple formats!\n\n" +
                                   $"Packets exported: {exportedCount} of {totalCount} total\n" +
                                   $"Filter: {GetCurrentFilterDescription()}\n\n" +
                                   $"Files created:\n" +
                                   $"CSV (spreadsheet): {csvPath}\n" +
                                   $"TXT (detailed):    {txtPath}")
            {
                X = 2,
                Y = 1,
                Width = Dim.Fill() - 4,
                Height = 10
            };

            var okButton = new Button("OK")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(message) + 2
            };
            okButton.Clicked += () =>
            {
                confirmWindow.RequestStop();
            };

            confirmWindow.Add(message, okButton);

            // Add Escape key handler to close dialog
            confirmWindow.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Esc)
                {
                    confirmWindow.RequestStop();
                    e.Handled = true;
                }
            };

            // Run the window as a modal dialog
            Application.Run(confirmWindow);
        }

        /// <summary>
        /// Show packet export error dialog
        /// </summary>
        private void ShowPacketExportError(string errorMessage)
        {
            var errorWindow = new Window("Packet Export Failed")
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 60,
                Height = 10
            };

            var message = new Label($"Failed to export packets:\n\n{errorMessage}")
            {
                X = 2,
                Y = 1,
                Width = Dim.Fill() - 4,
                Height = 5
            };

            var okButton = new Button("OK")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(message) + 1
            };
            okButton.Clicked += () =>
            {
                errorWindow.RequestStop();
            };

            errorWindow.Add(message, okButton);

            // Add Escape key handler to close dialog
            errorWindow.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Esc)
                {
                    errorWindow.RequestStop();
                    e.Handled = true;
                }
            };

            // Run the window as a modal dialog
            Application.Run(errorWindow);
        }
    }

    /// <summary>
    /// Packet information for GUI display
    /// </summary>
    public class PacketInfo
    {
        public int PacketNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = "";
        public string Direction { get; set; } = "";
        public ushort Length { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Description { get; set; } = "";

        // Layer 2 (LAPB) fields
        public byte L2Control { get; set; }
        public string L2Address { get; set; } = "";
        public string L2Type { get; set; } = "";
        public byte L2NS { get; set; }
        public byte L2NR { get; set; }
        public bool L2PF { get; set; }
        public bool L2FCSValid { get; set; }
        public ushort L2FCS { get; set; }

        // Layer 3 (X.25) fields
        public string L3Type { get; set; } = "";
        public ushort? L3LCN { get; set; }
        public string L3GFI { get; set; } = "";
        public bool L3Q { get; set; }
        public bool L3D { get; set; }
        public bool L3M { get; set; }
        public byte L3Cause { get; set; }
        public byte L3Diag { get; set; }
        public string L3Facilities { get; set; } = "";

        // Error tracking
        public bool IsError { get; set; }

        // Relationship fields for two-line display
        public PacketInfo? RelatedPacket { get; set; }
        public string RelationshipType { get; set; } = "";

        // Reference to business model for accessing flags
        public HistoryItem? HistoryItem { get; set; }
    }
}
