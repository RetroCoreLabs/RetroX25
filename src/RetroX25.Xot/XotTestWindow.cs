//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Terminal.Gui;

namespace RetroX25.Xot
{
    /// <summary>
    /// Main XOT test window with split-screen display
    /// Top half: sent data, Bottom half: received data
    /// </summary>
    public class XotTestWindow : Window
    {
        private readonly XotTestClient? _client;
        private readonly XotTestServer? _server;
        private readonly bool _isClient;

        private TextView _sentTextView = null!;
        private TextView _receivedTextView = null!;
        private Label _statusLabel = null!;
        private Label _helpLabel = null!;

        private int _sentPacketCount = 0;
        private int _receivedPacketCount = 0;

        /// <summary>
        /// Creates the window in client mode, titled "XOT Test Client", and subscribes to the client's events.
        /// </summary>
        /// <param name="client">
        /// The XOT test client whose sent and received packets the window shows.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="client"/> is null.
        /// </exception>
        public XotTestWindow(XotTestClient client) : base("XOT Test Client")
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _isClient = true;

            Initialize();
            SubscribeToClientEvents();
        }

        /// <summary>
        /// Creates the window in server mode, titled "XOT Test Server", and subscribes to the server's events.
        /// </summary>
        /// <param name="server">
        /// The XOT test server whose sent and received packets the window shows.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="server"/> is null.
        /// </exception>
        public XotTestWindow(XotTestServer server) : base("XOT Test Server")
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _isClient = false;

            Initialize();
            SubscribeToServerEvents();
        }

        private void Initialize()
        {
            X = 0;
            Y = 0;
            Width = Dim.Fill();
            Height = Dim.Fill();

            CreateControls();
            UpdateStatus();
        }

        private void CreateControls()
        {
            // Title label
            string endpoint = _isClient ? _client!.RemoteEndpoint : _server!.ListenEndpoint;
            var titleLabel = new Label($"{(_isClient ? "Client" : "Server")} - {endpoint}")
            {
                X = 1,
                Y = 0,
                ColorScheme = Colors.Dialog
            };
            Add(titleLabel);

            // Sent data section
            Add(new Label("═══════════════════════════ SENT DATA ═══════════════════════════")
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2
            });

            _sentTextView = new TextView
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                Height = Dim.Percent(45) - 3,
                ReadOnly = true,
                WordWrap = false
            };
            Add(_sentTextView);

            // Received data section
            Add(new Label("══════════════════════════ RECEIVED DATA ══════════════════════════")
            {
                X = 1,
                Y = Pos.Percent(45),
                Width = Dim.Fill() - 2
            });

            _receivedTextView = new TextView
            {
                X = 1,
                Y = Pos.Percent(45) + 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 6
            };
            Add(_receivedTextView);

            // Help label
            _helpLabel = new Label("F1:CallReq  F2:CallAck  F3:Data  F4:Clear  F5:CustomHex  F6:RR  F7:RNR  F8:Reset  F9:Quit")
            {
                X = 1,
                Y = Pos.AnchorEnd(2),
                Width = Dim.Fill() - 2,
                ColorScheme = Colors.TopLevel
            };
            Add(_helpLabel);

            // Status label
            _statusLabel = new Label("Status: Initializing...")
            {
                X = 1,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill() - 2
            };
            Add(_statusLabel);

            // Set up key bindings
            KeyPress += HandleKeyPress;
        }

        private void SubscribeToClientEvents()
        {
            if (_client == null) return;

            _client.DataSent += OnDataSent;
            _client.DataReceived += OnDataReceived;
            _client.StatusChanged += OnStatusChanged;
            _client.Disconnected += OnDisconnected;
        }

        private void SubscribeToServerEvents()
        {
            if (_server == null) return;

            _server.DataSent += OnDataSent;
            _server.DataReceived += OnDataReceived;
            _server.StatusChanged += OnStatusChanged;
            _server.ClientConnected += OnClientConnected;
            _server.ClientDisconnected += OnClientDisconnected;
        }

        private void HandleKeyPress(KeyEventEventArgs e)
        {
            switch (e.KeyEvent.Key)
            {
                case Key.F1:
                    SendPredefinedPacket(PredefinedPackets.CallRequest, "Call Request");
                    e.Handled = true;
                    break;

                case Key.F2:
                    SendPredefinedPacket(PredefinedPackets.CallAccept, "Call Accept");
                    e.Handled = true;
                    break;

                case Key.F3:
                    SendPredefinedPacket(PredefinedPackets.DataPacket, "Data Packet");
                    e.Handled = true;
                    break;

                case Key.F4:
                    SendPredefinedPacket(PredefinedPackets.ClearRequest, "Clear Request");
                    e.Handled = true;
                    break;

                case Key.F5:
                    ShowHexInputDialog();
                    e.Handled = true;
                    break;

                case Key.F6:
                    SendPredefinedPacket(PredefinedPackets.ReceiverReady, "RR");
                    e.Handled = true;
                    break;

                case Key.F7:
                    SendPredefinedPacket(PredefinedPackets.ReceiverNotReady, "RNR");
                    e.Handled = true;
                    break;

                case Key.F8:
                    SendPredefinedPacket(PredefinedPackets.ResetRequest, "Reset Request");
                    e.Handled = true;
                    break;

                case Key.F9:
                    Application.RequestStop();
                    e.Handled = true;
                    break;
            }
        }

        private void SendPredefinedPacket(byte[] packet, string name)
        {
            bool success;
            if (_isClient)
            {
                success = _client!.SendPacket(packet);
            }
            else
            {
                if (!_server!.HasClient)
                {
                    AppendToSent($"[{DateTime.Now:HH:mm:ss.fff}] Cannot send: No client connected\n\n");
                    return;
                }
                success = _server.SendPacket(packet);
            }

            if (!success)
            {
                AppendToSent($"[{DateTime.Now:HH:mm:ss.fff}] Failed to send {name}\n\n");
            }
        }

        private void ShowHexInputDialog()
        {
            var dialog = new HexInputDialog();
            Application.Run(dialog);

            if (dialog.WasSent && dialog.PacketData != null)
            {
                bool success;
                if (_isClient)
                {
                    success = _client!.SendPacket(dialog.PacketData);
                }
                else
                {
                    if (!_server!.HasClient)
                    {
                        AppendToSent($"[{DateTime.Now:HH:mm:ss.fff}] Cannot send: No client connected\n\n");
                        return;
                    }
                    success = _server.SendPacket(dialog.PacketData);
                }

                if (!success)
                {
                    AppendToSent($"[{DateTime.Now:HH:mm:ss.fff}] Failed to send custom packet\n\n");
                }
            }
        }

        private void OnDataSent(object? sender, XotDataEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                _sentPacketCount++;
                string formatted = XotPacketFormatter.FormatXotPacket(e.Data, e.Direction, e.Timestamp);
                AppendToSent(formatted + "\n\n");
                UpdateStatus();
            });
        }

        private void OnDataReceived(object? sender, XotDataEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                _receivedPacketCount++;
                string formatted = XotPacketFormatter.FormatXotPacket(e.Data, e.Direction, e.Timestamp);
                AppendToReceived(formatted + "\n\n");
                UpdateStatus();
            });
        }

        private void OnStatusChanged(object? sender, string message)
        {
            Application.MainLoop.Invoke(() =>
            {
                UpdateStatus(message);
            });
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                UpdateStatus("Disconnected");
            });
        }

        private void OnClientConnected(object? sender, string clientEndpoint)
        {
            Application.MainLoop.Invoke(() =>
            {
                AppendToReceived($"[{DateTime.Now:HH:mm:ss.fff}] Client connected: {clientEndpoint}\n\n");
                UpdateStatus($"Client connected: {clientEndpoint}");
            });
        }

        private void OnClientDisconnected(object? sender, EventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                AppendToReceived($"[{DateTime.Now:HH:mm:ss.fff}] Client disconnected\n\n");
                UpdateStatus("Client disconnected - Waiting for new connection...");
            });
        }

        private void AppendToSent(string text)
        {
            _sentTextView.Text += text;
            _sentTextView.MoveEnd();
        }

        private void AppendToReceived(string text)
        {
            _receivedTextView.Text += text;
            _receivedTextView.MoveEnd();
        }

        private void UpdateStatus(string? message = null)
        {
            string status;
            if (message != null)
            {
                status = $"Status: {message}";
            }
            else if (_isClient)
            {
                status = _client!.IsConnected
                    ? $"Status: Connected to {_client.RemoteEndpoint} | TX: {_sentPacketCount} | RX: {_receivedPacketCount}"
                    : $"Status: Disconnected | TX: {_sentPacketCount} | RX: {_receivedPacketCount}";
            }
            else
            {
                status = _server!.HasClient
                    ? $"Status: Client {_server.ClientEndpoint} | TX: {_sentPacketCount} | RX: {_receivedPacketCount}"
                    : $"Status: Listening on port {_server!.ListenEndpoint} - Waiting for client...";
            }

            _statusLabel.Text = status;
        }
    }
}
