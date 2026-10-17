//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Text.Json;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using Terminal.Gui;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Configuration window with tabbed interface for all X.25 Emulator settings
    /// </summary>
    public class ConfigurationWindow : Window
    {
        private readonly RuntimeConfig _config;
        private readonly string _configFilePath;

        // Main UI Controls
        private TabView _tabView = null!;
        private Button _saveButton = null!;
        private Button _loadButton = null!;
        private Button _resetButton = null!;
        private Button _closeButton = null!;
        private Label _statusLabel = null!;

        // Common tab controls
        private TextField _localMachineIdField = null!;
        private CheckBox _isDCECheckBox = null!;
        private TextField _t1TimeoutField = null!;
        private TextField _t2TimeoutField = null!;
        private TextField _t3TimeoutField = null!;
        private TextField _maxRetriesField = null!;
        private TextField _windowSizeField = null!;
        private TextField _historyCapacityField = null!;

        // Serial tab controls
        private ListView _serialInterfacesList = null!;
        private Button _addSerialButton = null!;
        private Button _editSerialButton = null!;
        private Button _deleteSerialButton = null!;

        // XOT tab controls
        private ListView _xotInterfacesList = null!;
        private Button _addXotButton = null!;
        private Button _editXotButton = null!;
        private Button _deleteXotButton = null!;

        // TCP-LAPB tab controls
        private ListView _tcpLapbInterfacesList = null!;
        private Button _addTcpLapbButton = null!;
        private Button _editTcpLapbButton = null!;
        private Button _deleteTcpLapbButton = null!;

        // Hosts tab controls
        private ListView _knownHostsList = null!;
        private Button _addHostButton = null!;
        private Button _editHostButton = null!;
        private Button _deleteHostButton = null!;

        public ConfigurationWindow(RuntimeConfig config, string configFilePath = "x25config.json") : base("X.25 Configuration")
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _configFilePath = configFilePath;

            InitializeComponent();
            LoadCurrentSettings();
        }

        private void InitializeComponent()
        {
            Width = Dim.Fill() - 8;
            Height = Dim.Fill() - 4;
            X = Pos.Center();
            Y = Pos.Center();

            // Create TabView
            _tabView = new TabView()
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 6
            };

            // Create tabs
            CreateCommonTab();
            CreateSerialTab();
            CreateXotTab();
            CreateTcpLapbTab();
            CreateHostsTab();

            Add(_tabView);

            // Bottom buttons
            var buttonY = Pos.Bottom(_tabView) + 1;

            _saveButton = new Button("Save") { X = 2, Y = buttonY };
            _saveButton.Clicked += OnSave;
            Add(_saveButton);

            _loadButton = new Button("Load") { X = 10, Y = buttonY };
            _loadButton.Clicked += OnLoad;
            Add(_loadButton);

            _resetButton = new Button("Reset") { X = 18, Y = buttonY };
            _resetButton.Clicked += OnReset;
            Add(_resetButton);

            _closeButton = new Button("Close") { X = 27, Y = buttonY };
            _closeButton.Clicked += OnClose;
            Add(_closeButton);

            // Status
            _statusLabel = new Label("Ready") { X = 2, Y = buttonY + 2, Width = Dim.Fill() - 4 };
            Add(_statusLabel);
        }

        private void CreateCommonTab()
        {
            var commonTab = new TabView.Tab("Common", new View());

            // X.25 Settings
            commonTab.View.Add(new Label("X.25 Settings") { X = 1, Y = 1 });
            commonTab.View.Add(new Label("Local Machine ID:") { X = 3, Y = 3 });
            _localMachineIdField = new TextField("") { X = 20, Y = 3, Width = 15 };
            commonTab.View.Add(_localMachineIdField);

            // LAPB Settings
            commonTab.View.Add(new Label("LAPB Settings") { X = 1, Y = 5 });

            _isDCECheckBox = new CheckBox("DCE Mode (Primary Station)") { X = 3, Y = 7 };
            commonTab.View.Add(_isDCECheckBox);

            commonTab.View.Add(new Label("T1 Timeout (ms):") { X = 3, Y = 9 });
            _t1TimeoutField = new TextField("") { X = 20, Y = 9, Width = 10 };
            commonTab.View.Add(_t1TimeoutField);

            commonTab.View.Add(new Label("T2 Timeout (ms):") { X = 3, Y = 10 });
            _t2TimeoutField = new TextField("") { X = 20, Y = 10, Width = 10 };
            commonTab.View.Add(_t2TimeoutField);

            commonTab.View.Add(new Label("T3 Timeout (ms):") { X = 3, Y = 11 });
            _t3TimeoutField = new TextField("") { X = 20, Y = 11, Width = 10 };
            commonTab.View.Add(_t3TimeoutField);

            commonTab.View.Add(new Label("Max Retries:") { X = 3, Y = 12 });
            _maxRetriesField = new TextField("") { X = 20, Y = 12, Width = 10 };
            commonTab.View.Add(_maxRetriesField);

            commonTab.View.Add(new Label("Window Size:") { X = 3, Y = 13 });
            _windowSizeField = new TextField("") { X = 20, Y = 13, Width = 10 };
            commonTab.View.Add(_windowSizeField);

            // Runtime Settings
            commonTab.View.Add(new Label("Runtime Settings") { X = 1, Y = 15 });
            commonTab.View.Add(new Label("History Capacity:") { X = 3, Y = 16 });
            _historyCapacityField = new TextField("") { X = 20, Y = 16, Width = 10 };
            commonTab.View.Add(_historyCapacityField);

            // Help text
            commonTab.View.Add(new Label("Note: DTE Mode (unchecked) recommended for peer-to-peer communication")
            { X = 35, Y = 7, Width = 35 });
            commonTab.View.Add(new Label("T3=0 disables idle supervision (ITU-T X.25 default)")
            { X = 35, Y = 11, Width = 35 });

            _tabView.AddTab(commonTab, false);
        }

        private void CreateSerialTab()
        {
            var serialTab = new TabView.Tab("Serial", new View());

            serialTab.View.Add(new Label("Serial Interfaces") { X = 1, Y = 1 });

            _serialInterfacesList = new ListView()
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 20,
                Height = Dim.Fill() - 4
            };
            serialTab.View.Add(_serialInterfacesList);

            // Buttons
            _addSerialButton = new Button("Add") { X = Pos.Right(_serialInterfacesList) + 1, Y = 3 };
            _addSerialButton.Clicked += OnAddSerial;
            serialTab.View.Add(_addSerialButton);

            _editSerialButton = new Button("Edit") { X = Pos.Right(_serialInterfacesList) + 1, Y = 5 };
            _editSerialButton.Clicked += OnEditSerial;
            serialTab.View.Add(_editSerialButton);

            _deleteSerialButton = new Button("Delete") { X = Pos.Right(_serialInterfacesList) + 1, Y = 7 };
            _deleteSerialButton.Clicked += OnDeleteSerial;
            serialTab.View.Add(_deleteSerialButton);

            _tabView.AddTab(serialTab, false);
        }

        private void CreateXotTab()
        {
            var xotTab = new TabView.Tab("XOT", new View());

            xotTab.View.Add(new Label("XOT Interfaces") { X = 1, Y = 1 });

            _xotInterfacesList = new ListView()
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 20,
                Height = Dim.Fill() - 4
            };
            xotTab.View.Add(_xotInterfacesList);

            // Buttons
            _addXotButton = new Button("Add") { X = Pos.Right(_xotInterfacesList) + 1, Y = 3 };
            _addXotButton.Clicked += OnAddXot;
            xotTab.View.Add(_addXotButton);

            _editXotButton = new Button("Edit") { X = Pos.Right(_xotInterfacesList) + 1, Y = 5 };
            _editXotButton.Clicked += OnEditXot;
            xotTab.View.Add(_editXotButton);

            _deleteXotButton = new Button("Delete") { X = Pos.Right(_xotInterfacesList) + 1, Y = 7 };
            _deleteXotButton.Clicked += OnDeleteXot;
            xotTab.View.Add(_deleteXotButton);

            _tabView.AddTab(xotTab, false);
        }

        private void CreateTcpLapbTab()
        {
            var tcpLapbTab = new TabView.Tab("TCP-LAPB", new View());

            tcpLapbTab.View.Add(new Label("TCP-LAPB Interfaces (HDLC over TCP with per-link LAPB)") { X = 1, Y = 1 });

            _tcpLapbInterfacesList = new ListView()
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 20,
                Height = Dim.Fill() - 4
            };
            tcpLapbTab.View.Add(_tcpLapbInterfacesList);

            // Buttons
            _addTcpLapbButton = new Button("Add") { X = Pos.Right(_tcpLapbInterfacesList) + 1, Y = 3 };
            _addTcpLapbButton.Clicked += OnAddTcpLapb;
            tcpLapbTab.View.Add(_addTcpLapbButton);

            _editTcpLapbButton = new Button("Edit") { X = Pos.Right(_tcpLapbInterfacesList) + 1, Y = 5 };
            _editTcpLapbButton.Clicked += OnEditTcpLapb;
            tcpLapbTab.View.Add(_editTcpLapbButton);

            _deleteTcpLapbButton = new Button("Delete") { X = Pos.Right(_tcpLapbInterfacesList) + 1, Y = 7 };
            _deleteTcpLapbButton.Clicked += OnDeleteTcpLapb;
            tcpLapbTab.View.Add(_deleteTcpLapbButton);

            _tabView.AddTab(tcpLapbTab, false);
        }

        private void CreateHostsTab()
        {
            var hostsTab = new TabView.Tab("HOSTS", new View());

            hostsTab.View.Add(new Label("Known Hosts (Name-based connections supported)") { X = 1, Y = 1 });

            _knownHostsList = new ListView()
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 20,
                Height = Dim.Fill() - 4
            };
            hostsTab.View.Add(_knownHostsList);

            // Buttons
            _addHostButton = new Button("Add") { X = Pos.Right(_knownHostsList) + 1, Y = 3 };
            _addHostButton.Clicked += OnAddHost;
            hostsTab.View.Add(_addHostButton);

            _editHostButton = new Button("Edit") { X = Pos.Right(_knownHostsList) + 1, Y = 5 };
            _editHostButton.Clicked += OnEditHost;
            hostsTab.View.Add(_editHostButton);

            _deleteHostButton = new Button("Delete") { X = Pos.Right(_knownHostsList) + 1, Y = 7 };
            _deleteHostButton.Clicked += OnDeleteHost;
            hostsTab.View.Add(_deleteHostButton);

            _tabView.AddTab(hostsTab, false);
        }

        private void LoadCurrentSettings()
        {
            try
            {
                // Load common settings
                _localMachineIdField.Text = _config.X25.LocalMachineId ?? "";
                _isDCECheckBox.Checked = _config.Lapb.IsDCE;
                _t1TimeoutField.Text = ((int)_config.Lapb.T1Timeout.TotalMilliseconds).ToString();
                _t2TimeoutField.Text = ((int)_config.Lapb.T2Timeout.TotalMilliseconds).ToString();
                _t3TimeoutField.Text = ((int)_config.Lapb.T3Timeout.TotalMilliseconds).ToString();
                _maxRetriesField.Text = _config.Lapb.MaxRetries.ToString();
                _windowSizeField.Text = _config.Lapb.WindowSize.ToString();
                _historyCapacityField.Text = _config.HistoryCapacity.ToString();

                // Load serial interfaces
                RefreshSerialInterfaces();

                // Load XOT interfaces
                RefreshXotInterfaces();

                // Load TCP-LAPB interfaces
                RefreshTcpLapbInterfaces();

                // Load known hosts
                RefreshKnownHosts();

                _statusLabel.Text = "Current settings loaded";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error loading settings: {ex.Message}";
            }
        }

        private void RefreshSerialInterfaces()
        {
            var items = new string[_config.Routing.SerialInterfaces.Count];
            for (int i = 0; i < _config.Routing.SerialInterfaces.Count; i++)
            {
                var si = _config.Routing.SerialInterfaces[i];
                items[i] = $"{si.Name} - {si.PortName} @ {si.BaudRate} baud -> {si.RemoteMachineId}";
            }
            _serialInterfacesList.SetSource(items);
        }

        private void RefreshXotInterfaces()
        {
            var items = new string[_config.Routing.XotInterfaces.Count];
            for (int i = 0; i < _config.Routing.XotInterfaces.Count; i++)
            {
                var xi = _config.Routing.XotInterfaces[i];
                items[i] = $"{xi.Name} - {xi.Mode} port {xi.LocalPort} -> {xi.RemoteMachineId} ({xi.RemoteIPAddress}:{xi.RemotePort})";
            }
            _xotInterfacesList.SetSource(items);
        }

        private void RefreshTcpLapbInterfaces()
        {
            var items = new string[_config.Routing.TcpLapbInterfaces.Count];
            for (int i = 0; i < _config.Routing.TcpLapbInterfaces.Count; i++)
            {
                var ti = _config.Routing.TcpLapbInterfaces[i];
                items[i] = $"{ti.Name} - {ti.Mode} {ti.RemoteAddress}:{ti.Port} -> {ti.RemoteMachineId} [K={ti.LapbWindowSize}] {(ti.Enabled ? "" : "(disabled)")}";
            }
            _tcpLapbInterfacesList.SetSource(items);
        }

        private void RefreshKnownHosts()
        {
            var items = new string[_config.Routing.KnownHosts.Count];
            for (int i = 0; i < _config.Routing.KnownHosts.Count; i++)
            {
                var h = _config.Routing.KnownHosts[i];
                items[i] = $"{h.Name} ({h.MachineId}) - {h.Reachability} via {h.Via} [{(h.Enabled ? "Enabled" : "Disabled")}]";
            }
            _knownHostsList.SetSource(items);
        }

        private void OnSave()
        {
            try
            {
                // Validate and update configuration
                UpdateConfigFromUI();

                // Validate timer relationships
                ValidateTimerSettings();

                // Save to file
                SaveToFile();

                _statusLabel.Text = $"Configuration saved to {_configFilePath}";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error saving: {ex.Message}";
            }
        }

        private void OnLoad()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    _statusLabel.Text = $"Configuration file {_configFilePath} not found";
                    return;
                }

                LoadFromFile();
                LoadCurrentSettings(); // Refresh UI

                _statusLabel.Text = $"Configuration loaded from {_configFilePath}";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error loading: {ex.Message}";
            }
        }

        private void OnReset()
        {
            try
            {
                // Reset to default values
                var defaults = new RuntimeConfig();
                _config.X25.LocalMachineId = defaults.X25.LocalMachineId;
                _config.Lapb.IsDCE = defaults.Lapb.IsDCE;
                _config.Lapb.T1Timeout = defaults.Lapb.T1Timeout;
                _config.Lapb.T2Timeout = defaults.Lapb.T2Timeout;
                _config.Lapb.T3Timeout = defaults.Lapb.T3Timeout;
                _config.Lapb.MaxRetries = defaults.Lapb.MaxRetries;
                _config.Lapb.WindowSize = defaults.Lapb.WindowSize;
                _config.HistoryCapacity = defaults.HistoryCapacity;

                LoadCurrentSettings(); // Refresh UI
                _statusLabel.Text = "Settings reset to defaults";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error resetting: {ex.Message}";
            }
        }

        private void OnClose()
        {
            // Hide window and remove from Application.Top
            this.Visible = false;
            Application.Top.Remove(this);
        }

        private void UpdateConfigFromUI()
        {
            _config.X25.LocalMachineId = _localMachineIdField.Text?.ToString() ?? "";

            _config.Lapb.IsDCE = _isDCECheckBox.Checked;

            if (!int.TryParse(_t1TimeoutField.Text.ToString(), out int t1))
                throw new ArgumentException("Invalid T1 timeout");
            _config.Lapb.T1Timeout = TimeSpan.FromMilliseconds(t1);

            if (!int.TryParse(_t2TimeoutField.Text.ToString(), out int t2))
                throw new ArgumentException("Invalid T2 timeout");
            _config.Lapb.T2Timeout = TimeSpan.FromMilliseconds(t2);

            if (!int.TryParse(_t3TimeoutField.Text.ToString(), out int t3))
                throw new ArgumentException("Invalid T3 timeout");
            _config.Lapb.T3Timeout = TimeSpan.FromMilliseconds(t3);

            if (!int.TryParse(_maxRetriesField.Text.ToString(), out int retries))
                throw new ArgumentException("Invalid max retries");
            _config.Lapb.MaxRetries = retries;

            if (!byte.TryParse(_windowSizeField.Text.ToString(), out byte windowSize))
                throw new ArgumentException("Invalid window size");
            _config.Lapb.WindowSize = windowSize;

            if (!int.TryParse(_historyCapacityField.Text.ToString(), out int capacity))
                throw new ArgumentException("Invalid history capacity");
            _config.HistoryCapacity = capacity;
        }

        private void ValidateTimerSettings()
        {
            // ITU-T X.25 timer validation
            var t1Ms = _config.Lapb.T1Timeout.TotalMilliseconds;
            var t2Ms = _config.Lapb.T2Timeout.TotalMilliseconds;
            var t3Ms = _config.Lapb.T3Timeout.TotalMilliseconds;

            // T2 ≤ T1/2
            if (t2Ms > t1Ms / 2)
                throw new ArgumentException($"T2 ({t2Ms}ms) must be ≤ T1/2 ({t1Ms / 2}ms) per ITU-T X.25");

            // T3 > T1 (when enabled)
            if (t3Ms > 0 && t3Ms <= t1Ms)
                throw new ArgumentException($"T3 ({t3Ms}ms) must be > T1 ({t1Ms}ms) when enabled per ITU-T X.25");

            // Window size validation
            if (_config.Lapb.WindowSize < 1 || _config.Lapb.WindowSize > 127)
                throw new ArgumentException("Window size must be between 1 and 127");
        }

        private void SaveToFile()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            };

            var json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configFilePath, json);
        }

        private void LoadFromFile()
        {
            var json = File.ReadAllText(_configFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var loadedConfig = JsonSerializer.Deserialize<RuntimeConfig>(json, options);
            if (loadedConfig == null)
                throw new InvalidOperationException("Failed to deserialize configuration");

            // Update ALL config sections with loaded values
            _config.X25.LocalMachineId = loadedConfig.X25.LocalMachineId;
            _config.Lapb.IsDCE = loadedConfig.Lapb.IsDCE;
            _config.Lapb.T1Timeout = loadedConfig.Lapb.T1Timeout;
            _config.Lapb.T2Timeout = loadedConfig.Lapb.T2Timeout;
            _config.Lapb.T3Timeout = loadedConfig.Lapb.T3Timeout;
            _config.Lapb.MaxRetries = loadedConfig.Lapb.MaxRetries;
            _config.Lapb.WindowSize = loadedConfig.Lapb.WindowSize;
            _config.HistoryCapacity = loadedConfig.HistoryCapacity;

            // Update routing configuration
            _config.Routing.Enabled = loadedConfig.Routing.Enabled;
            _config.Routing.SerialInterfaces.Clear();
            foreach (var si in loadedConfig.Routing.SerialInterfaces)
            {
                _config.Routing.SerialInterfaces.Add(si);
            }

            _config.Routing.XotInterfaces.Clear();
            foreach (var xi in loadedConfig.Routing.XotInterfaces)
            {
                _config.Routing.XotInterfaces.Add(xi);
            }

            _config.Routing.TcpLapbInterfaces.Clear();
            foreach (var ti in loadedConfig.Routing.TcpLapbInterfaces)
            {
                _config.Routing.TcpLapbInterfaces.Add(ti);
            }

            _config.Routing.RoutingTable.Clear();
            foreach (var route in loadedConfig.Routing.RoutingTable)
            {
                _config.Routing.RoutingTable.Add(route);
            }

            _config.Routing.KnownHosts.Clear();
            foreach (var host in loadedConfig.Routing.KnownHosts)
            {
                _config.Routing.KnownHosts.Add(host);
            }
        }

        // Serial interface event handlers
        private void OnAddSerial()
        {
            var dialog = new SerialInterfaceEditDialog(null);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.SerialInterfaces.Add(dialog.Result);
                RefreshSerialInterfaces();
                _statusLabel.Text = $"Added serial interface: {dialog.Result.Name}";
            }
        }

        private void OnEditSerial()
        {
            var selectedIndex = _serialInterfacesList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.SerialInterfaces.Count)
            {
                _statusLabel.Text = "Please select a serial interface to edit";
                return;
            }

            var selectedInterface = _config.Routing.SerialInterfaces[selectedIndex];
            var dialog = new SerialInterfaceEditDialog(selectedInterface);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.SerialInterfaces[selectedIndex] = dialog.Result;
                RefreshSerialInterfaces();
                _statusLabel.Text = $"Updated serial interface: {dialog.Result.Name}";
            }
        }

        private void OnDeleteSerial()
        {
            var selectedIndex = _serialInterfacesList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.SerialInterfaces.Count)
            {
                _statusLabel.Text = "Please select a serial interface to delete";
                return;
            }

            var selectedInterface = _config.Routing.SerialInterfaces[selectedIndex];
            var result = MessageBox.Query("Confirm Delete", $"Delete serial interface '{selectedInterface.Name}'?", "Yes", "No");
            if (result == 0)
            {
                _config.Routing.SerialInterfaces.RemoveAt(selectedIndex);
                RefreshSerialInterfaces();
                _statusLabel.Text = $"Deleted serial interface: {selectedInterface.Name}";
            }
        }

        // XOT interface event handlers
        private void OnAddXot()
        {
            var dialog = new XotInterfaceEditDialog(null);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.XotInterfaces.Add(dialog.Result);
                RefreshXotInterfaces();
                _statusLabel.Text = $"Added XOT interface: {dialog.Result.Name}";
            }
        }

        private void OnEditXot()
        {
            var selectedIndex = _xotInterfacesList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.XotInterfaces.Count)
            {
                _statusLabel.Text = "Please select an XOT interface to edit";
                return;
            }

            var selectedInterface = _config.Routing.XotInterfaces[selectedIndex];
            var dialog = new XotInterfaceEditDialog(selectedInterface);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.XotInterfaces[selectedIndex] = dialog.Result;
                RefreshXotInterfaces();
                _statusLabel.Text = $"Updated XOT interface: {dialog.Result.Name}";
            }
        }

        private void OnDeleteXot()
        {
            var selectedIndex = _xotInterfacesList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.XotInterfaces.Count)
            {
                _statusLabel.Text = "Please select an XOT interface to delete";
                return;
            }

            var selectedInterface = _config.Routing.XotInterfaces[selectedIndex];
            var result = MessageBox.Query("Confirm Delete", $"Delete XOT interface '{selectedInterface.Name}'?", "Yes", "No");
            if (result == 0)
            {
                _config.Routing.XotInterfaces.RemoveAt(selectedIndex);
                RefreshXotInterfaces();
                _statusLabel.Text = $"Deleted XOT interface: {selectedInterface.Name}";
            }
        }

        // TCP-LAPB interface event handlers
        private void OnAddTcpLapb()
        {
            var dialog = new TcpLapbInterfaceEditDialog(null);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.TcpLapbInterfaces.Add(dialog.Result);
                RefreshTcpLapbInterfaces();
                _statusLabel.Text = $"Added TCP-LAPB interface: {dialog.Result.Name}";
            }
        }

        private void OnEditTcpLapb()
        {
            var selectedIndex = _tcpLapbInterfacesList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.TcpLapbInterfaces.Count)
            {
                _statusLabel.Text = "Please select a TCP-LAPB interface to edit";
                return;
            }

            var selectedInterface = _config.Routing.TcpLapbInterfaces[selectedIndex];
            var dialog = new TcpLapbInterfaceEditDialog(selectedInterface);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.TcpLapbInterfaces[selectedIndex] = dialog.Result;
                RefreshTcpLapbInterfaces();
                _statusLabel.Text = $"Updated TCP-LAPB interface: {dialog.Result.Name}";
            }
        }

        private void OnDeleteTcpLapb()
        {
            var selectedIndex = _tcpLapbInterfacesList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.TcpLapbInterfaces.Count)
            {
                _statusLabel.Text = "Please select a TCP-LAPB interface to delete";
                return;
            }

            var selectedInterface = _config.Routing.TcpLapbInterfaces[selectedIndex];
            var result = MessageBox.Query("Confirm Delete", $"Delete TCP-LAPB interface '{selectedInterface.Name}'?", "Yes", "No");
            if (result == 0)
            {
                _config.Routing.TcpLapbInterfaces.RemoveAt(selectedIndex);
                RefreshTcpLapbInterfaces();
                _statusLabel.Text = $"Deleted TCP-LAPB interface: {selectedInterface.Name}";
            }
        }

        // Known hosts event handlers
        private void OnAddHost()
        {
            var dialog = new KnownHostEditDialog(null);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.KnownHosts.Add(dialog.Result);
                RefreshKnownHosts();
                _statusLabel.Text = $"Added known host: {dialog.Result.Name} ({dialog.Result.MachineId})";
            }
        }

        private void OnEditHost()
        {
            var selectedIndex = _knownHostsList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.KnownHosts.Count)
            {
                _statusLabel.Text = "Please select a known host to edit";
                return;
            }

            var selectedHost = _config.Routing.KnownHosts[selectedIndex];
            var dialog = new KnownHostEditDialog(selectedHost);
            Application.Run(dialog);

            if (dialog.Result != null)
            {
                _config.Routing.KnownHosts[selectedIndex] = dialog.Result;
                RefreshKnownHosts();
                _statusLabel.Text = $"Updated known host: {dialog.Result.Name} ({dialog.Result.MachineId})";
            }
        }

        private void OnDeleteHost()
        {
            var selectedIndex = _knownHostsList.SelectedItem;
            if (selectedIndex < 0 || selectedIndex >= _config.Routing.KnownHosts.Count)
            {
                _statusLabel.Text = "Please select a known host to delete";
                return;
            }

            var selectedHost = _config.Routing.KnownHosts[selectedIndex];
            var result = MessageBox.Query("Confirm Delete", $"Delete known host '{selectedHost.Name}' ({selectedHost.MachineId})?", "Yes", "No");
            if (result == 0)
            {
                _config.Routing.KnownHosts.RemoveAt(selectedIndex);
                RefreshKnownHosts();
                _statusLabel.Text = $"Deleted known host: {selectedHost.Name} ({selectedHost.MachineId})";
            }
        }
    }
}
