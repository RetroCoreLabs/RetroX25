//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Sintran;
using Terminal.Gui;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// SINTRAN TAD Client Window
    /// Connects to remote SINTRAN TAD servers using routing protocol
    /// </summary>
    public class SintranTadClientWindow : Window
    {
        private readonly ProtocolRuntime _runtime;
        private readonly ILogger _logger;
        private SintranTadClient? _tadClient;

        /// <summary>
        /// Gets the number given to this window when it was opened.
        /// </summary>
        /// <value>
        /// The number shown in the title and used in the TAD client name <c>TADClient_n</c>.
        /// </value>
        public int WindowNumber { get; private set; }

        /// <summary>
        /// Raised when the user closes the window with F9 or the Exit button, after any TAD client
        /// has been disconnected and disposed.
        /// </summary>
        /// <remarks>
        /// Hides the <see cref="Window"/> event of the same name. The main window uses it to drop this
        /// window from its list of open SINTRAN TAD client windows.
        /// </remarks>
        public new event EventHandler? Closed;

        // UI Controls
        private Button _connectButton = null!;
        private Button _disconnectButton = null!;
        private Button _exitButton = null!;
        private TextField _addressField = null!;
        private TextView _console = null!;
        private Label _statusLabel = null!;
        private Label _sessionInfoLabel = null!;

        /// <summary>
        /// Creates the SINTRAN TAD client window in the disconnected state.
        /// </summary>
        /// <remarks>
        /// The user types a remote address and presses Connect; the window then creates a
        /// <see cref="SintranTadClient"/> and connects it. While connected, typed keys are sent to the
        /// remote TAD server and its output is shown in the console area.
        /// </remarks>
        /// <param name="runtime">
        /// The protocol runtime the TAD client connects through.
        /// </param>
        /// <param name="logger">
        /// The logger handed to the TAD client.
        /// </param>
        /// <param name="windowNumber">
        /// The number for this window; see <see cref="WindowNumber"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="runtime"/> or <paramref name="logger"/> is null.
        /// </exception>
        public SintranTadClientWindow(ProtocolRuntime runtime, ILogger logger, int windowNumber = 1)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            WindowNumber = windowNumber;

            Title = $"SINTRAN TAD Client #{windowNumber}";
            X = 2;
            Y = 2;
            Width = Dim.Fill() - 4;
            Height = Dim.Fill() - 4;

            ColorScheme = Colors.Dialog;
            Modal = false;

            CreateControls();
            SetupEventHandlers();

            // Add KeyPress event handler for character input
            KeyPress += (e) =>
            {
                ProcessKey(e.KeyEvent);
            };
        }

        /// <summary>
        /// Sends a key press to the connected TAD server, or closes the window on F9.
        /// </summary>
        /// <remarks>
        /// Enter is sent as CR LF. Ctrl+A to Ctrl+Z are sent as the control codes <c>0x01</c> to
        /// <c>0x1A</c>. Other characters are sent and also echoed locally into the console area.
        /// </remarks>
        /// <param name="keyEvent">
        /// The key that was pressed.
        /// </param>
        /// <returns>
        /// True when the key was F9 or was sent to the server; false when no TAD client is connected,
        /// so the key gets its normal handling.
        /// </returns>
        public override bool ProcessKey(KeyEvent keyEvent)
        {
            return HandleKeyPress(keyEvent);
        }

        private bool HandleKeyPress(KeyEvent keyEvent)
        {
            // F9 for exit
            if (keyEvent.Key == Key.F9)
            {
                OnExit();
                return true;
            }

            // Only process other keys if connected
            if (_tadClient == null || !_tadClient.IsConnected)
            {
                return false;
            }

            // Handle Enter key
            if (keyEvent.Key == Key.Enter)
            {
                _tadClient.SendData("\r\n");
                AppendText("\n");
                return true;
            }

            // Handle Ctrl combinations
            if (keyEvent.IsCtrl)
            {
                byte key = (byte)keyEvent.Key;
                if ((key >= (byte)Key.A) && (key <= (byte)Key.Z))
                {
                    char ctrlChar = (char)(keyEvent.KeyValue - (int)Key.A + 1);
                    _tadClient.SendData(new byte[] { (byte)ctrlChar });
                    return true;
                }
            }

            // Handle regular characters
            char charToSend = (char)keyEvent.KeyValue;
            _tadClient.SendData(charToSend.ToString());

            // Local echo (will be replaced by server echo if enabled)
            AppendText(charToSend.ToString());

            return true;
        }

        private void CreateControls()
        {
            // Address field
            Add(new Label("Remote Address:") { X = 1, Y = 1 });
            _addressField = new TextField("") { X = 18, Y = 1, Width = 20 };
            Add(_addressField);

            // Buttons
            _connectButton = new Button("Connect") { X = 40, Y = 1 };
            Add(_connectButton);

            _disconnectButton = new Button("Disconnect") { X = 51, Y = 1, Enabled = false };
            Add(_disconnectButton);

            _exitButton = new Button("Exit (F9)") { X = 65, Y = 1 };
            Add(_exitButton);

            // Status label
            _statusLabel = new Label("Status: Disconnected") { X = 1, Y = 3 };
            Add(_statusLabel);

            // Session info label
            _sessionInfoLabel = new Label("") { X = 40, Y = 3 };
            Add(_sessionInfoLabel);

            // Console output
            Add(new Label("Output:") { X = 1, Y = 5 });
            _console = new TextView
            {
                X = 1,
                Y = 6,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 10,
                ReadOnly = false,
                WordWrap = true
            };
            Add(_console);

            // Help text
            Add(new Label("Connected: Type to send data. F9=Exit | Disconnected: Enter address and click Connect")
            { X = 1, Y = Pos.Bottom(_console) + 1, Width = Dim.Fill() - 2 });
        }

        private void SetupEventHandlers()
        {
            _connectButton.Clicked += OnConnect;
            _disconnectButton.Clicked += OnDisconnect;
            _exitButton.Clicked += OnExit;

            // Add KeyPress handler to console TextView
            _console.KeyPress += (e) =>
            {
                e.Handled = HandleKeyPress(e.KeyEvent);
            };

            _console.SetFocus();
        }

        private void OnConnect()
        {
            if (_tadClient != null && _tadClient.IsConnected)
            {
                AppendText("Already connected\n");
                return;
            }

            // Get remote address from text field
            string remoteAddress = _addressField.Text?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(remoteAddress))
            {
                AppendText("ERROR: Please enter a remote address\n");
                return;
            }

            try
            {
                // Create TAD client
                _tadClient = new SintranTadClient(_logger, _runtime, $"TADClient_{WindowNumber}");

                // Subscribe to events
                _tadClient.ConnectionStateChanged += OnConnectionStateChanged;
                _tadClient.DataReceived += OnDataReceived;
                _tadClient.ErrorOccurred += OnErrorOccurred;

                // Connect to remote address
                bool success = _tadClient.Connect(remoteAddress);

                if (success)
                {
                    _connectButton.Enabled = false;
                    _disconnectButton.Enabled = true;
                    _addressField.Enabled = false;

                    AppendText($"Connecting to SINTRAN TAD server at {remoteAddress}...\n");
                }
                else
                {
                    AppendText($"Failed to initiate connection to {remoteAddress}\n");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect TAD client");
                AppendText($"Connection failed: {ex.Message}\n");
            }
        }

        private void OnDisconnect()
        {
            if (_tadClient == null || !_tadClient.IsConnected)
            {
                return;
            }

            try
            {
                _tadClient.Disconnect();

                _connectButton.Enabled = true;
                _disconnectButton.Enabled = false;
                _addressField.Enabled = true;

                AppendText("Disconnected from SINTRAN TAD server\n");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disconnect TAD client");
                AppendText($"Disconnect failed: {ex.Message}\n");
            }
        }

        private void OnExit()
        {
            // Clean up and close window
            if (_tadClient != null)
            {
                if (_tadClient.IsConnected)
                {
                    _tadClient.Disconnect();
                }
                _tadClient.Dispose();
            }

            // Fire closed event
            Closed?.Invoke(this, EventArgs.Empty);

            // Hide window and remove from Application.Top
            this.Visible = false;
            Application.Top.Remove(this);
        }

        private void OnConnectionStateChanged(object? sender, SintranTadClientEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                UpdateStatus($"Connection: {e.State} - {e.Message}");

                switch (e.State)
                {
                    case TadSessionState.Active:
                        _connectButton.Enabled = false;
                        _disconnectButton.Enabled = true;
                        AppendText("=== Connected to SINTRAN TAD server ===\n");
                        break;

                    case TadSessionState.Closed:
                        _connectButton.Enabled = true;
                        _disconnectButton.Enabled = false;
                        AppendText("=== Disconnected from SINTRAN TAD server ===\n");
                        break;

                    case TadSessionState.Closing:
                        AppendText("Disconnecting...\n");
                        break;
                }
            });
        }

        private void OnDataReceived(object? sender, SintranTadClientDataEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                AppendText(e.Data);
            });
        }

        private void OnErrorOccurred(object? sender, SintranTadClientEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                AppendText($"ERROR: {e.Message}\n");
                _logger.LogError("SINTRAN TAD Client error: {Message}", e.Message);
            });
        }

        private void AppendText(string text)
        {
            var current = _console.Text?.ToString() ?? "";

            foreach (char c in text)
            {
                switch (c)
                {
                    case '\b': // Backspace
                        if (current.Length > 0)
                            current = current.Substring(0, current.Length - 1);
                        break;

                    case '\n': // Line feed
                        current += Environment.NewLine;
                        break;

                    case '\r': // Carriage return (ignore, we use \n for newlines)
                        break;

                    default:
                        current += c;
                        break;
                }
            }

            _console.Text = current;
            _console.MoveEnd();
        }

        private void UpdateStatus(string status)
        {
            Application.MainLoop.Invoke(() =>
            {
                _statusLabel.Text = $"Status: {status}";
            });
        }
    }
}
