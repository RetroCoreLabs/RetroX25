using System;
using Terminal.Gui;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Modules;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// PAD/TAD Testing Window for local loopback testing
    /// </summary>
    public class PadTestWindow : Window
    {
        private readonly ProtocolRuntime _runtime;
        private readonly ILogger _logger;
        private readonly PadClient _padClient;
        
        public int WindowNumber { get; private set; }
        public new event EventHandler? Closed;
        
        // UI Controls
        private Button _exitButton = null!;
        private TextView _console = null!;
        private Label _statusLabel = null!;
        private ListView _x3ParametersListView = null!;
        
        public PadTestWindow(ProtocolRuntime runtime, ILogger logger, int windowNumber = 1)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            WindowNumber = windowNumber;
            
            // Create PAD client once - it persists for the lifetime of this window
            _padClient = _runtime.CreatePadClient($"TestPAD_{windowNumber}");
            
            // Set a unique calling address for this PAD client
            _padClient.LocalAddress = $"10{windowNumber}";
            
            // Set up PAD client event handlers once
            _padClient.ConnectionStateChanged += OnConnectionStateChanged;
            _padClient.DataReceived += OnDataReceived;
            _padClient.CharacterEcho += OnCharacterEcho;
            _padClient.ParametersChanged += OnParametersChanged;
            
            Title = $"PAD Client Test #{windowNumber}";
            X = 2;
            Y = 2;

            ColorScheme = Colors.Dialog;
            Modal = false; // Make it non-modal
            
            CreateControls();
            SetupEventHandlers();
            UpdateX3ParametersList(); // Show X.3 parameters from persistent PAD client

            // Add KeyPress event handler as backup for key capture
            KeyPress += (e) =>
            {

                ProcessKey(e.KeyEvent);
                
            };

            Width = Dim.Fill() - 4;  // Resizable width
            Height = Dim.Fill() - 4;  // Resizable height

            _padClient.Start();
        }

        public override bool ProcessKey(KeyEvent keyEvent)
        {
            return HandleKeyPress(keyEvent);
        }

        /// <summary>
        /// Common key handling logic used by both window and control KeyPress events
        /// </summary>
        private bool HandleKeyPress(KeyEvent keyEvent)
        {
            // Only handle F9 for exit (ESC is now used as PAD escape character)
            if (keyEvent.Key == Key.F9)
            {
                OnExit();
                return true;
            }

            if (_padClient == null)
            {
                return false; // Let default handling occur
            }

            // Forward every key directly to your pad client as a character
            // ITU-T X.28 specifies Enter key should send CR+LF sequence (0x0D 0x0A)
            // X.3 Parameter 3 (DATA_FORWARDING) triggers on CR (0x0D) for data forwarding
            if (keyEvent.Key == Key.Enter)
            {
                // Send CR+LF sequence per ITU-T X.28 specification
                _padClient.OnKey('\r'); // CR (0x0D) - triggers data forwarding
                _padClient.OnKey('\n'); // LF (0x0A) - completes line ending
            }
            else
            {
                if (keyEvent.IsCtrl)
                {
                    byte key = (byte)keyEvent.Key;
                    // Handle Ctrl+<char> combinations
                    if ((key >= (byte)Key.A) && (key <= (byte)Key.Z))
                    {
                        char ctrlChar = (char)(keyEvent.KeyValue - (int)Key.A + 1); // Ctrl+A = 0x01, Ctrl+B = 0x02, etc.
                        _padClient.OnKey(ctrlChar);
                        return true; // consume key
                    }
                    // Add more Ctrl+<char> handling if needed
                }
                else
                {
                    char charToSend = (char)keyEvent.KeyValue;
                    _padClient.OnKey(charToSend);
                    return true;
                }
                
            }
            return true; // consume all keys
        }

        private void CreateControls()
        {
            // Exit button
            _exitButton = new Button("Exit") { X = 1, Y = 1 };
            Add(_exitButton);
            
            // Status label
            _statusLabel = new Label("Status: Disconnected") { X = 1, Y = 3 };
            Add(_statusLabel);
            
            // Output text view (left side, reduced width to make room for X.3 parameters)
            Add(new Label("Output:") { X = 1, Y = 5 });
            _console = new TextView
            {
                X = 1,
                Y = 6,
                Width = 75,   // Reduced width
                Height = 22, //
                             // Dim.Fill(),   // Fixed height? =22
                ReadOnly = true,
                WordWrap = false
            };
            Add(_console);
            
            // X.3 Parameters list view (right side)
            Add(new Label("X.3 Parameters:") { X = 78, Y = 5 });
            _x3ParametersListView = new ListView()
            {
                X = 78,
                Y = 6,
                Width = 37,   // Right side width
                Height = 22
            };
            Add(_x3ParametersListView);
                      
            
            // Help text for commands
            Add(new Label("X.28 commands: CALL <address>, STAT, CLEAR, PAR?, SET <param>:<value>") 
                { X = 1, Y = 30, Width = 75 });
            Add(new Label("Press Enter to send. Connected chars forwarded per X.3 parameters") 
                { X = 1, Y = 31, Width = 75 });
        }
        
        private void SetupEventHandlers()
        {
            _exitButton.Clicked += OnExit;

            // Add KeyPress handler to console TextView so it also processes keys
            _console.KeyPress += (e) =>
            {
                e.Handled = HandleKeyPress(e.KeyEvent);
            };

            _console.SetFocus();
        }

             
        
        
        private void OnExit()
        {
            // Clean up and close window
            _padClient.Disconnect(); // Disconnect if connected
            _padClient.Dispose();    // Dispose only on window exit
            
            // Fire closed event so parent can remove from tracking list
            Closed?.Invoke(this, EventArgs.Empty);
            
            // Hide window and remove from Application.Top
            this.Visible = false;
            Application.Top.Remove(this);
        }
        
        
        private void OnConnectionStateChanged(object? sender, PadConnectionEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                UpdateStatus($"Connection: {e.State}");                
                
                if (e.State == PadConnectionState.Connected)
                {
                    // AppendText($"PAD ready - Type characters directly (they will be forwarded per X.3 parameters)");                    
                }
                else if (e.State == PadConnectionState.Disconnected)
                {
                }
                
                // Always update X.3 parameters display regardless of connection state
                // X.3 parameters persist throughout PAD client lifetime per ITU-T specification
                UpdateX3ParametersList();
            });
        }


        private void AppendText(string s)
        {
            var current = _console.Text?.ToString() ?? "";

            foreach (char c in s)
            {
                switch (c)
                {
                    case '\b': // Backspace
                        if (current.Length > 0)
                            current = current.Substring(0, current.Length - 1);
                        break;

                    case '\n': // Line feed: start a new line
                        current += Environment.NewLine;
                        break;
                    
                    case '\r':
                        break; // ignore CR

                    default:
                        current += c;
                        break;
                }
            }

            _console.Text = current;
            _console.MoveEnd();
        }

        private void OnDataReceived(object? sender, PadDataEventArgs e)
        {
            Application.MainLoop.Invoke(() =>
            {
                AppendText(e.Data);
            });
        }
       
        
        private void OnCharacterEcho(object? sender, PadCharacterEventArgs e)
        {
            // Display echoed character in the output window for visual feedback
            Application.MainLoop.Invoke(() =>
            {
                // Convert byte to character for display
                AppendText(((char)e.Character).ToString());                
            });
        }
        
        private void OnParametersChanged(object? sender, EventArgs e)
        {
            // Update X.3 parameters display when parameters change
            Application.MainLoop.Invoke(() =>
            {
                UpdateX3ParametersList();
            });
        }


        private void UpdateStatus(string status)
        {
            Application.MainLoop.Invoke(() =>
            {
                _statusLabel.Text = $"Status: {status}";
            });
        }
    
        /// <summary>
        /// Update the X.3 parameters list with current PAD client parameters
        /// </summary>
        private void UpdateX3ParametersList()
        {
            try
            {
                var parameters = _padClient.X3Protocol.GetAllParameters();
                var parameterLines = new List<string>();

                // Show all 22 X.3 parameters with user-friendly descriptions
                for (byte paramNum = 1; paramNum <= 22; paramNum++)
                {
                    if (parameters.TryGetValue(paramNum, out var value))
                    {
                        var shortDesc = GetShortParameterDescription(paramNum);
                        parameterLines.Add($"P{paramNum:D2}: {value,3} - {shortDesc}");
                    }
                }

                _x3ParametersListView.SetSource(parameterLines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating X.3 parameters list");
                _x3ParametersListView.SetSource(new List<string> { "Error loading X.3 parameters" });
            }
        }

        

        /// <summary>
        /// Get short, user-friendly descriptions for X.3 parameters
        /// </summary>
        private string GetShortParameterDescription(byte paramNum)
        {
            return paramNum switch
            {
                1 => "PAD Escape",
                2 => "Echo",  
                3 => "Data Forward",
                4 => "Idle Timer", 
                5 => "Device Control",
                6 => "Service Signals",
                7 => "Break Procedure",
                8 => "Discard Output",
                9 => "Padding CR",
                10 => "Line Folding",
                11 => "Binary Speed",
                12 => "Forward N Chars",
                13 => "LF Insertion",
                14 => "Padding LF", 
                15 => "Editing",
                16 => "Char Delete",
                17 => "Line Delete",
                18 => "Line Display",
                19 => "Edit Signals",
                20 => "Echo Mask",
                21 => "Parity Check",
                22 => "Page Wait",
                _ => "Unknown"
            };
        }
    }
}