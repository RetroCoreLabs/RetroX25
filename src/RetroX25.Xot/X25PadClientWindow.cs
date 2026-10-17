//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Runtime.Transport;
using RetroIO.Protocols.X25.Sintran;
using RetroIO.Protocols.X25.Transport;
using Terminal.Gui;

namespace RetroX25.Xot
{
    /// <summary>
    /// X.25 PAD Client Window - Pure X.25 PAD (no LAPB, no SINTRAN)
    /// Connects directly to remote PAD/TAD services via XOT
    /// </summary>
    public class X25PadClientWindow : Window
    {
        private readonly ILogger _logger;

        // UI Components
        private TextView _terminalOutput = null!;
        private Label _statusLabel = null!;
        private Label _helpLabel = null!;
        private TextField _inputField = null!;

        // Network and protocol components
        private XotClientTransport _xotClient = null!;

        // Configuration
        private string _localAddress = "";
        private string _remoteAddress = "";
        private string _remoteHost = "";
        private int _remotePort = 1998;

        // Statistics
        private long _bytesTx = 0;
        private long _bytesRx = 0;
        private bool _isConnected = false;

        public X25PadClientWindow(ILogger logger, string localAddress = "12345") : base("X.25 PAD Client")
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localAddress = localAddress;

            Initialize();
        }

        private void Initialize()
        {
            X = 0;
            Y = 0;
            Width = Dim.Fill();
            Height = Dim.Fill();

            CreateControls();
            UpdateWindowTitle();
            UpdateStatus();
        }

        private void CreateControls()
        {
            // Title label
            var titleLabel = new Label($"X.25 PAD Client - Local Address: {_localAddress}")
            {
                X = 1,
                Y = 0,
                ColorScheme = Colors.Dialog
            };
            Add(titleLabel);

            // Terminal output area (takes most of the window)
            Add(new Label("═══════════════════════════ TERMINAL ═══════════════════════════")
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2
            });

            _terminalOutput = new TextView
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 9,
                ReadOnly = true,
                WordWrap = true
            };
            Add(_terminalOutput);

            // Input field
            Add(new Label("Input:") { X = 1, Y = Pos.AnchorEnd(5) });
            _inputField = new TextField("")
            {
                X = 8,
                Y = Pos.AnchorEnd(5),
                Width = Dim.Fill() - 9
            };
            _inputField.KeyPress += OnInputKeyPress;
            Add(_inputField);

            // Help label
            _helpLabel = new Label("F2:Connect  F3:Disconnect  F4:X.3Params  F9:Quit")
            {
                X = 1,
                Y = Pos.AnchorEnd(3),
                Width = Dim.Fill() - 2,
                ColorScheme = Colors.TopLevel
            };
            Add(_helpLabel);

            // Connection info label
            Add(new Label("Connection:")
            {
                X = 1,
                Y = Pos.AnchorEnd(2),
                Width = 15
            });

            var connectionLabel = new Label("")
            {
                X = 13,
                Y = Pos.AnchorEnd(2),
                Width = Dim.Fill() - 14
            };
            Add(connectionLabel);

            // Status label
            _statusLabel = new Label("Status: Disconnected")
            {
                X = 1,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill() - 2
            };
            Add(_statusLabel);

            // Set up key bindings
            KeyPress += HandleKeyPress;

            // Show welcome message
            AppendToTerminal("X.25 PAD Client\n");
            AppendToTerminal("Press F2 to connect to a remote address\n");
            AppendToTerminal("Press F4 to configure X.3 parameters\n\n");
        }

        private void HandleKeyPress(KeyEventEventArgs e)
        {
            switch (e.KeyEvent.Key)
            {
                case Key.F2:
                    ShowConnectDialog();
                    e.Handled = true;
                    break;

                case Key.F3:
                    Disconnect();
                    e.Handled = true;
                    break;

                case Key.F4:
                    ShowX3ParametersDialog();
                    e.Handled = true;
                    break;

                case Key.F9:
                    RequestStop();
                    e.Handled = true;
                    break;
            }
        }

        private void OnInputKeyPress(KeyEventEventArgs e)
        {
            // Handle Enter key to send text
            if (e.KeyEvent.Key == Key.Enter && _isConnected)
            {
                string text = _inputField.Text?.ToString() ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    SendText(text + "\r\n");
                    _inputField.Text = "";
                }
                e.Handled = true;
            }
        }

        private void ShowConnectDialog()
        {
            if (_isConnected)
            {
                MessageBox.ErrorQuery("Already Connected", "Please disconnect first", "OK");
                return;
            }

            var dialog = new Dialog("Connect to Remote PAD", 60, 15);

            // Remote host field
            dialog.Add(new Label("Remote Host:") { X = 2, Y = 2 });
            var hostField = new TextField("localhost") { X = 16, Y = 2, Width = 30 };
            dialog.Add(hostField);

            // Remote port field
            dialog.Add(new Label("Remote Port:") { X = 2, Y = 4 });
            var portField = new TextField("1998") { X = 16, Y = 4, Width = 10 };
            dialog.Add(portField);

            // Remote X.121 address field
            dialog.Add(new Label("Remote Address:") { X = 2, Y = 6 });
            var addressField = new TextField("200") { X = 16, Y = 6, Width = 30 };
            dialog.Add(addressField);

            // Connect button
            var connectBtn = new Button("Connect") { X = 16, Y = 8 };
            connectBtn.Clicked += () =>
            {
                _remoteHost = hostField.Text?.ToString() ?? "";
                if (int.TryParse(portField.Text?.ToString(), out int port))
                {
                    _remotePort = port;
                }
                _remoteAddress = addressField.Text?.ToString() ?? "";

                Application.RequestStop();
                Connect();
            };
            dialog.Add(connectBtn);

            // Cancel button
            var cancelBtn = new Button("Cancel") { X = 28, Y = 8 };
            cancelBtn.Clicked += () => Application.RequestStop();
            dialog.Add(cancelBtn);

            Application.Run(dialog);
        }

        private void Connect()
        {
            if (string.IsNullOrEmpty(_remoteHost) || string.IsNullOrEmpty(_remoteAddress))
            {
                MessageBox.ErrorQuery("Invalid Input", "Please provide host and address", "OK");
                return;
            }

            try
            {
                AppendToTerminal($"\nConnecting to {_remoteHost}:{_remotePort}, address {_remoteAddress}...\n");

                // Create XOT client with config
                var xotConfig = new XotClientConfig
                {
                    Name = $"PAD-{_localAddress}",
                    ConnectTimeout = 5000
                };
                _xotClient = new XotClientTransport(_logger, xotConfig);

                // Start XOT client transport
                if (!_xotClient.Start())
                {
                    AppendToTerminal("ERROR: Failed to start XOT transport\n");
                    return;
                }

                // Create PAD client (this will create its own ProtocolRuntime)
                // Note: In pure X.25 mode, PAD communicates directly via XOT, no LAPB

                // Connect to XOT gateway
                if (!_xotClient.ConnectToXotGateway(_remoteHost, _remotePort))
                {
                    AppendToTerminal("ERROR: Failed to connect via XOT\n");
                    return;
                }

                AppendToTerminal("XOT connection established\n");

                // TODO: Initialize PAD client and place X.25 call
                // For now, just mark as connected
                _isConnected = true;
                UpdateWindowTitle();
                UpdateStatus();

                AppendToTerminal($"Connected to {_remoteAddress}\n\n");
                _inputField.SetFocus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting");
                AppendToTerminal($"ERROR: {ex.Message}\n");
            }
        }

        private void Disconnect()
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                AppendToTerminal("\nDisconnecting...\n");

                // TODO: Send X.25 Clear Request

                _xotClient?.Stop();
                _xotClient = null!;

                _isConnected = false;
                UpdateWindowTitle();
                UpdateStatus();

                AppendToTerminal("Disconnected\n\n");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting");
                AppendToTerminal($"ERROR: {ex.Message}\n");
            }
        }

        private void ShowX3ParametersDialog()
        {
            // TODO: Implement X.3 parameters dialog
            // For now, show placeholder
            MessageBox.Query("X.3 Parameters", "X.3 Parameter configuration not yet implemented", "OK");
        }

        private void SendText(string text)
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                // TODO: Encode text via X.29 protocol
                // TODO: Send via X.25 Data packet through XOT

                byte[] data = Encoding.ASCII.GetBytes(text);
                _bytesTx += data.Length;

                // For now, just echo locally
                AppendToTerminal(text);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending text");
                AppendToTerminal($"\nERROR: {ex.Message}\n");
            }
        }

        private void AppendToTerminal(string text)
        {
            _terminalOutput.Text += text;
            _terminalOutput.MoveEnd();
        }

        private void UpdateStatus()
        {
            string status;
            if (_isConnected)
            {
                status = $"Status: Connected to {_remoteAddress} | TX: {FormatBytes(_bytesTx)} | RX: {FormatBytes(_bytesRx)}";
            }
            else
            {
                status = "Status: Disconnected";
            }

            _statusLabel.Text = status;
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes}B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1}KB";
            else
                return $"{bytes / (1024.0 * 1024.0):F1}MB";
        }

        private void UpdateWindowTitle()
        {
            string title = $"X.25 PAD - Addr:{_localAddress}";

            if (_xotClient != null && !string.IsNullOrEmpty(_remoteHost))
            {
                // TCP Client mode
                string connectionStatus = _isConnected ? "CONNECTED" : "CONNECTING";
                title += $" - TCP Client {_remoteHost}:{_remotePort} - {connectionStatus}";
            }
            else
            {
                // Disconnected
                title += " - DISCONNECTED";
            }

            Title = title;
        }
    }
}
