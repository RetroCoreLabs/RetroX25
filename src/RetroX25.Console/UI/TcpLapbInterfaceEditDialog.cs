//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using NStack;
using RetroIO.Link;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using Terminal.Gui;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Dialog for editing TcpLapbInterfaceConfig objects
    /// TCP-LAPB: HDLC byte-stuffing over TCP with per-link LAPB state machine
    /// </summary>
    public class TcpLapbInterfaceEditDialog : Dialog
    {
        private readonly TcpLapbInterfaceConfig? _originalInterface;
        private TextField _nameField = null!;
        private CheckBox _enabledCheckBox = null!;
        private TextField _ipAddressField = null!;
        private TextField _portField = null!;
        private TextField _remoteMachineIdField = null!;
        private CheckBox _isServerCheckBox = null!;
        private TextField _descriptionField = null!;

        // LAPB parameters
        private TextField _windowSizeField = null!;
        private TextField _t1TimeoutField = null!;
        private TextField _t2TimeoutField = null!;
        private TextField _t3TimeoutField = null!;
        private TextField _maxRetriesField = null!;

        // Protocol mode
        private RadioGroup _protocolModeGroup = null!;

        public TcpLapbInterfaceConfig? Result { get; private set; }

        public TcpLapbInterfaceEditDialog(TcpLapbInterfaceConfig? interfaceConfig)
            : base(interfaceConfig == null ? "Add TCP-LAPB Interface" : "Edit TCP-LAPB Interface", 75, 22)
        {
            _originalInterface = interfaceConfig;
            InitializeComponent();
            LoadCurrentValues();
        }

        private void InitializeComponent()
        {
            int y = 1;

            // Name
            Add(new Label("Interface Name:") { X = 2, Y = y });
            _nameField = new TextField("") { X = 22, Y = y, Width = 20 };
            Add(_nameField);

            // Enabled
            _enabledCheckBox = new CheckBox("Enabled") { X = 45, Y = y };
            Add(_enabledCheckBox);

            y += 2;
            Add(new Label("--- Connection Settings ---") { X = 2, Y = y });

            y++;
            // IP Address
            Add(new Label("IP Address:") { X = 2, Y = y });
            _ipAddressField = new TextField("") { X = 22, Y = y, Width = 20 };
            Add(_ipAddressField);

            y++;
            // Port
            Add(new Label("Port:") { X = 2, Y = y });
            _portField = new TextField("") { X = 22, Y = y, Width = 10 };
            Add(_portField);

            y++;
            // Remote Machine ID
            Add(new Label("Remote Machine ID:") { X = 2, Y = y });
            _remoteMachineIdField = new TextField("") { X = 22, Y = y, Width = 20 };
            Add(_remoteMachineIdField);

            y++;
            // Server/Client Mode
            _isServerCheckBox = new CheckBox("Server Mode (accept connections)") { X = 2, Y = y };
            Add(_isServerCheckBox);

            y += 2;
            Add(new Label("--- LAPB Parameters ---") { X = 2, Y = y });

            y++;
            // Window Size (K)
            Add(new Label("Window Size (K):") { X = 2, Y = y });
            _windowSizeField = new TextField("") { X = 22, Y = y, Width = 5 };
            Add(_windowSizeField);
            Add(new Label("(1-7, default 7)") { X = 28, Y = y });

            y++;
            // T1 Timeout
            Add(new Label("T1 Timeout (ms):") { X = 2, Y = y });
            _t1TimeoutField = new TextField("") { X = 22, Y = y, Width = 8 };
            Add(_t1TimeoutField);

            // T2 Timeout (same row)
            Add(new Label("T2 (ms):") { X = 35, Y = y });
            _t2TimeoutField = new TextField("") { X = 45, Y = y, Width = 8 };
            Add(_t2TimeoutField);

            y++;
            // T3 Timeout
            Add(new Label("T3 Timeout (ms):") { X = 2, Y = y });
            _t3TimeoutField = new TextField("") { X = 22, Y = y, Width = 8 };
            Add(_t3TimeoutField);
            Add(new Label("(0=disabled)") { X = 32, Y = y });

            // Max Retries (same row)
            Add(new Label("N2:") { X = 50, Y = y });
            _maxRetriesField = new TextField("") { X = 55, Y = y, Width = 5 };
            Add(_maxRetriesField);

            y += 2;
            Add(new Label("--- Protocol Mode ---") { X = 2, Y = y });

            y++;
            _protocolModeGroup = new RadioGroup(new ustring[] { "X.25 Only", "SINTRAN Only", "Auto-Detect" })
            {
                X = 2,
                Y = y,
                DisplayMode = DisplayModeLayout.Horizontal
            };
            Add(_protocolModeGroup);

            y += 2;
            // Description
            Add(new Label("Description:") { X = 2, Y = y });
            _descriptionField = new TextField("") { X = 22, Y = y, Width = 40 };
            Add(_descriptionField);

            y += 2;
            // Buttons
            var okButton = new Button("OK") { X = 20, Y = y };
            okButton.Clicked += OnOk;
            AddButton(okButton);

            var cancelButton = new Button("Cancel") { X = 35, Y = y };
            cancelButton.Clicked += OnCancel;
            AddButton(cancelButton);
        }

        private void LoadCurrentValues()
        {
            if (_originalInterface != null)
            {
                _nameField.Text = _originalInterface.Name;
                _enabledCheckBox.Checked = _originalInterface.Enabled;
                _ipAddressField.Text = _originalInterface.RemoteAddress ?? "127.0.0.1";
                _portField.Text = _originalInterface.Port.ToString();
                _remoteMachineIdField.Text = _originalInterface.RemoteMachineId;
                _isServerCheckBox.Checked = _originalInterface.Mode == TcpMode.Server;
                _descriptionField.Text = _originalInterface.Description;

                // LAPB parameters
                _windowSizeField.Text = _originalInterface.LapbWindowSize.ToString();
                _t1TimeoutField.Text = _originalInterface.LapbT1TimeoutMs.ToString();
                _t2TimeoutField.Text = _originalInterface.LapbT2TimeoutMs.ToString();
                _t3TimeoutField.Text = _originalInterface.LapbT3TimeoutMs.ToString();
                _maxRetriesField.Text = _originalInterface.LapbMaxRetries.ToString();

                // Protocol mode
                _protocolModeGroup.SelectedItem = _originalInterface.ProtocolMode switch
                {
                    ProtocolDetectionMode.X25Only => 0,
                    ProtocolDetectionMode.SintranOnly => 1,
                    ProtocolDetectionMode.AutoDetect => 2,
                    _ => 0
                };
            }
            else
            {
                // Default values for new interface
                _nameField.Text = "TcpLapb0";
                _enabledCheckBox.Checked = true;
                _ipAddressField.Text = "127.0.0.1";
                _portField.Text = "5555";
                _remoteMachineIdField.Text = "";
                _isServerCheckBox.Checked = true;
                _descriptionField.Text = "";

                // Default LAPB parameters
                _windowSizeField.Text = "7";
                _t1TimeoutField.Text = "3000";
                _t2TimeoutField.Text = "1500";
                _t3TimeoutField.Text = "0";
                _maxRetriesField.Text = "10";

                // Default to X.25 only
                _protocolModeGroup.SelectedItem = 0;
            }
        }

        private void OnOk()
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(_nameField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Interface name is required", "OK");
                    return;
                }

                if (!int.TryParse(_portField.Text.ToString(), out int port) || port < 1 || port > 65535)
                {
                    MessageBox.ErrorQuery("Validation Error", "Invalid port (1-65535)", "OK");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_remoteMachineIdField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Remote machine ID is required", "OK");
                    return;
                }

                // For client mode, IP address is required
                if (!_isServerCheckBox.Checked && string.IsNullOrWhiteSpace(_ipAddressField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "IP address is required for client mode", "OK");
                    return;
                }

                // Validate LAPB parameters
                if (!int.TryParse(_windowSizeField.Text.ToString(), out int windowSize) || windowSize < 1 || windowSize > 7)
                {
                    MessageBox.ErrorQuery("Validation Error", "Window size must be 1-7", "OK");
                    return;
                }

                if (!int.TryParse(_t1TimeoutField.Text.ToString(), out int t1) || t1 < 100)
                {
                    MessageBox.ErrorQuery("Validation Error", "T1 timeout must be >= 100ms", "OK");
                    return;
                }

                if (!int.TryParse(_t2TimeoutField.Text.ToString(), out int t2) || t2 < 0)
                {
                    MessageBox.ErrorQuery("Validation Error", "T2 timeout must be >= 0ms", "OK");
                    return;
                }

                if (!int.TryParse(_t3TimeoutField.Text.ToString(), out int t3) || t3 < 0)
                {
                    MessageBox.ErrorQuery("Validation Error", "T3 timeout must be >= 0ms (0=disabled)", "OK");
                    return;
                }

                if (!int.TryParse(_maxRetriesField.Text.ToString(), out int n2) || n2 < 1)
                {
                    MessageBox.ErrorQuery("Validation Error", "Max retries (N2) must be >= 1", "OK");
                    return;
                }

                // ITU-T X.25 timer validation: T2 <= T1/2
                if (t2 > t1 / 2)
                {
                    MessageBox.ErrorQuery("Validation Error", $"T2 ({t2}ms) must be <= T1/2 ({t1 / 2}ms) per ITU-T X.25", "OK");
                    return;
                }

                // T3 > T1 when enabled
                if (t3 > 0 && t3 <= t1)
                {
                    MessageBox.ErrorQuery("Validation Error", $"T3 ({t3}ms) must be > T1 ({t1}ms) when enabled", "OK");
                    return;
                }

                // Map protocol mode
                var protocolMode = _protocolModeGroup.SelectedItem switch
                {
                    0 => ProtocolDetectionMode.X25Only,
                    1 => ProtocolDetectionMode.SintranOnly,
                    2 => ProtocolDetectionMode.AutoDetect,
                    _ => ProtocolDetectionMode.X25Only
                };

                // Create result
                Result = new TcpLapbInterfaceConfig
                {
                    Name = _nameField.Text?.ToString() ?? "",
                    Enabled = _enabledCheckBox.Checked,
                    RemoteAddress = _ipAddressField.Text?.ToString() ?? "127.0.0.1",
                    Port = port,
                    RemoteMachineId = _remoteMachineIdField.Text?.ToString() ?? "",
                    Mode = _isServerCheckBox.Checked ? TcpMode.Server : TcpMode.Client,
                    Description = _descriptionField.Text?.ToString() ?? "",
                    LapbWindowSize = (byte)windowSize,
                    LapbT1TimeoutMs = t1,
                    LapbT2TimeoutMs = t2,
                    LapbT3TimeoutMs = t3,
                    LapbMaxRetries = (byte)n2,
                    ProtocolMode = protocolMode
                };

                Application.RequestStop();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to create TCP-LAPB interface: {ex.Message}", "OK");
            }
        }

        private void OnCancel()
        {
            Result = null;
            Application.RequestStop();
        }
    }
}
