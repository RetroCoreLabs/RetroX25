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
    /// Dialog for entering custom hex packet data
    /// </summary>
    public class HexInputDialog : Dialog
    {
        private TextField _hexInput = null!;
        private Label _statusLabel = null!;
        private Label _previewLabel = null!;
        private Button _sendButton = null!;

        public byte[]? PacketData { get; private set; }
        public bool WasSent { get; private set; }

        public HexInputDialog() : base("Custom Hex Packet", 70, 18)
        {
            CreateControls();
        }

        private void CreateControls()
        {
            // Instructions
            Add(new Label("Enter X.25 packet data in hex format:")
            {
                X = 1,
                Y = 1
            });

            Add(new Label("Format: \"1001000B\" or \"10 01 00 0B\"")
            {
                X = 1,
                Y = 2,
                ColorScheme = Colors.Dialog
            });

            // Hex input field
            Add(new Label("Hex Data:")
            {
                X = 1,
                Y = 4
            });

            _hexInput = new TextField("")
            {
                X = 12,
                Y = 4,
                Width = Dim.Fill() - 13
            };
            Add(_hexInput);

            // Example
            Add(new Label("Example: 1001000B21020001000 (Call Request)")
            {
                X = 12,
                Y = 5,
                ColorScheme = Colors.Dialog
            });

            // Preview label
            Add(new Label("Preview:")
            {
                X = 1,
                Y = 7
            });

            _previewLabel = new Label("")
            {
                X = 1,
                Y = 8,
                Width = Dim.Fill() - 2,
                Height = 4
            };
            Add(_previewLabel);

            // Status label
            _statusLabel = new Label("")
            {
                X = 1,
                Y = 13,
                Width = Dim.Fill() - 2,
                ColorScheme = Colors.Error
            };
            Add(_statusLabel);

            // Buttons
            _sendButton = new Button("Send")
            {
                X = Pos.Center() - 10,
                Y = 14,
                IsDefault = true
            };
            _sendButton.Clicked += OnSend;
            AddButton(_sendButton);

            Button cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = 14
            };
            cancelButton.Clicked += () => Application.RequestStop();
            AddButton(cancelButton);

            // Update preview as user types
            _hexInput.TextChanged += (oldValue) =>
            {
                UpdatePreview();
            };

            _hexInput.SetFocus();
        }

        private void UpdatePreview()
        {
            string hexString = _hexInput.Text?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(hexString))
            {
                _previewLabel.Text = "(empty)";
                _statusLabel.Text = "";
                return;
            }

            // Try to parse
            byte[]? bytes = XotPacketFormatter.ParseHexString(hexString);
            if (bytes == null)
            {
                _previewLabel.Text = "(invalid hex format)";
                _statusLabel.Text = "ERROR: Invalid hex format";
                _statusLabel.ColorScheme = Colors.Error;
                return;
            }

            if (bytes.Length == 0)
            {
                _previewLabel.Text = "(empty packet)";
                _statusLabel.Text = "";
                return;
            }

            // Show preview
            string packetType = PredefinedPackets.GetPacketName(bytes);
            string hexDump = XotPacketFormatter.FormatHexDump(bytes);

            _previewLabel.Text = $"{bytes.Length} bytes - {packetType}\n{hexDump}";
            _statusLabel.Text = $"OK: Ready to send {bytes.Length} bytes";
            _statusLabel.ColorScheme = Colors.TopLevel;
        }

        private void OnSend()
        {
            string hexString = _hexInput.Text?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(hexString))
            {
                _statusLabel.Text = "ERROR: Please enter hex data";
                _statusLabel.ColorScheme = Colors.Error;
                return;
            }

            // Parse hex string
            byte[]? bytes = XotPacketFormatter.ParseHexString(hexString);
            if (bytes == null || bytes.Length == 0)
            {
                _statusLabel.Text = "ERROR: Invalid hex format or empty packet";
                _statusLabel.ColorScheme = Colors.Error;
                return;
            }

            // Validate minimum X.25 packet size (at least GFI + LCN + PTI)
            if (bytes.Length < 4)
            {
                _statusLabel.Text = "ERROR: Packet too short (minimum 4 bytes for X.25)";
                _statusLabel.ColorScheme = Colors.Error;
                return;
            }

            // Success - store data and close
            PacketData = bytes;
            WasSent = true;
            Application.RequestStop();
        }
    }
}
