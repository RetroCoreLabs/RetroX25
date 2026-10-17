//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Sintran;
using Terminal.Gui;

namespace RetroX25.Xot
{
    /// <summary>
    /// X.3 PAD Parameters Configuration Window
    /// Displays and allows editing of all 22 X.3 parameters per ITU-T X.3 specification
    /// </summary>
    public class X3ParametersWindow : Dialog
    {
        private readonly X3 _x3Protocol;
        private readonly Dictionary<byte, TextField> _paramFields = new Dictionary<byte, TextField>();
        private bool _wasModified = false;

        public bool WasModified => _wasModified;

        public X3ParametersWindow(X3 x3Protocol) : base("X.3 PAD Parameters", 80, 28)
        {
            _x3Protocol = x3Protocol ?? throw new ArgumentNullException(nameof(x3Protocol));

            CreateControls();
        }

        private void CreateControls()
        {
            // Instructions
            Add(new Label("Configure X.3 PAD parameters (ITU-T X.3 specification)")
            {
                X = 2,
                Y = 1,
                ColorScheme = Colors.Dialog
            });

            // Parameter list
            int yPos = 3;
            int col1X = 2;
            int col2X = 40;

            // Column 1: Parameters 1-11
            AddParameter(1, "Escape", col1X, yPos++);
            AddParameter(2, "Echo", col1X, yPos++);
            AddParameter(3, "Data Forward Character", col1X, yPos++);
            AddParameter(4, "Idle Timer Selection", col1X, yPos++);
            AddParameter(5, "Ancillary Device Control", col1X, yPos++);
            AddParameter(6, "Control of PAD Service Signals", col1X, yPos++);
            AddParameter(7, "Procedure on Break", col1X, yPos++);
            AddParameter(8, "Discard Output", col1X, yPos++);
            AddParameter(9, "Padding after CR", col1X, yPos++);
            AddParameter(10, "Line Folding", col1X, yPos++);
            AddParameter(11, "Binary Speed", col1X, yPos++);

            // Column 2: Parameters 12-22
            yPos = 3;
            AddParameter(12, "Flow Control (terminal TX)", col2X, yPos++);
            AddParameter(13, "Linefeed Insertion", col2X, yPos++);
            AddParameter(14, "Padding after LF", col2X, yPos++);
            AddParameter(15, "Editing", col2X, yPos++);
            AddParameter(16, "Character Delete", col2X, yPos++);
            AddParameter(17, "Line Delete", col2X, yPos++);
            AddParameter(18, "Line Display", col2X, yPos++);
            AddParameter(19, "Editing PAD Service Signals", col2X, yPos++);
            AddParameter(20, "Echo Mask", col2X, yPos++);
            AddParameter(21, "Parity Treatment", col2X, yPos++);
            AddParameter(22, "Page Wait", col2X, yPos++);

            // Buttons
            var applyBtn = new Button("Apply")
            {
                X = 15,
                Y = Pos.AnchorEnd(2),
                IsDefault = false
            };
            applyBtn.Clicked += OnApply;
            Add(applyBtn);

            var okBtn = new Button("OK")
            {
                X = 25,
                Y = Pos.AnchorEnd(2),
                IsDefault = true
            };
            okBtn.Clicked += () =>
            {
                OnApply();
                Application.RequestStop();
            };
            Add(okBtn);

            var cancelBtn = new Button("Cancel")
            {
                X = 32,
                Y = Pos.AnchorEnd(2)
            };
            cancelBtn.Clicked += () => Application.RequestStop();
            Add(cancelBtn);

            // Set focus to first field
            if (_paramFields.ContainsKey(1))
            {
                _paramFields[1].SetFocus();
            }
        }

        private void AddParameter(byte paramNum, string name, int x, int y)
        {
            // Parameter number and name
            var label = new Label($"{paramNum,2}. {name}:")
            {
                X = x,
                Y = y,
                Width = 30
            };
            Add(label);

            // Current value field
            byte currentValue = _x3Protocol.GetParameter(paramNum);
            var valueField = new TextField(currentValue.ToString())
            {
                X = x + 30,
                Y = y,
                Width = 5
            };

            // Validate input (only allow 0-255)
            valueField.TextChanged += (oldValue) =>
            {
                string text = valueField.Text?.ToString() ?? "";
                if (!string.IsNullOrEmpty(text) && byte.TryParse(text, out byte val))
                {
                    // Valid byte value
                    if (val > 255)
                    {
                        valueField.Text = "255";
                    }
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    // Invalid input, revert to current parameter value
                    valueField.Text = currentValue.ToString();
                }
            };

            Add(valueField);
            _paramFields[paramNum] = valueField;
        }

        private void OnApply()
        {
            try
            {
                bool anyChanged = false;

                foreach (var kvp in _paramFields)
                {
                    byte paramNum = kvp.Key;
                    string valueText = kvp.Value.Text?.ToString() ?? "";

                    if (byte.TryParse(valueText, out byte newValue))
                    {
                        byte oldValue = _x3Protocol.GetParameter(paramNum);
                        if (newValue != oldValue)
                        {
                            _x3Protocol.SetParameter(paramNum, newValue);
                            anyChanged = true;
                        }
                    }
                }

                if (anyChanged)
                {
                    _wasModified = true;
                    MessageBox.Query("Parameters Updated", "X.3 parameters have been updated successfully", "OK");
                }
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to update parameters: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Create X.3 parameters help text
        /// </summary>
        public static string GetParameterHelp(byte paramNum)
        {
            return paramNum switch
            {
                1 => "Escape from data transfer mode (0=no, 1=yes)",
                2 => "Echo characters (0=no, 1=yes)",
                3 => "Data forwarding character (0-127, special values)",
                4 => "Idle timer selection (0-255, units of 20ms)",
                5 => "Ancillary device control (0=no, 1=yes)",
                6 => "PAD service signals (0-8)",
                7 => "Break procedure (0-21)",
                8 => "Discard output (0=normal, 1=discard)",
                9 => "Padding after CR (0-255, units of 20ms)",
                10 => "Line folding (0-255 columns)",
                11 => "Binary speed (0-18)",
                12 => "Flow control of terminal transmission (0-3)",
                13 => "Linefeed insertion after CR (0-7)",
                14 => "Padding after LF (0-255, units of 20ms)",
                15 => "Editing (0=no, 1=yes)",
                16 => "Character delete character (0-127)",
                17 => "Line delete character (0-127)",
                18 => "Line display character (0-127)",
                19 => "Editing PAD service signals (0-8)",
                20 => "Echo mask (0-255 bitmap)",
                21 => "Parity treatment (0-3)",
                22 => "Page wait (0-255 lines)",
                _ => "Unknown parameter"
            };
        }
    }
}
