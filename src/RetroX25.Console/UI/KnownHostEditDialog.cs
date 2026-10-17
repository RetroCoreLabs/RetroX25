//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using Terminal.Gui;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Dialog for editing known host entries with Name support for name-based connections
    /// </summary>
    public class KnownHostEditDialog : Dialog
    {
        private TextField _nameField = null!;
        private TextField _machineIdField = null!;
        private ComboBox _reachabilityCombo = null!;
        private TextField _viaField = null!;
        private TextField _descriptionField = null!;
        private CheckBox _enabledCheckBox = null!;

        /// <summary>
        /// Gets the host entry built from the fields when the user presses OK.
        /// </summary>
        /// <value>
        /// A new <see cref="KnownHostEntry"/> after OK with valid input, or null when the user cancels
        /// or the dialog has not been closed with OK.
        /// </value>
        public KnownHostEntry? Result { get; private set; }

        /// <summary>
        /// Creates the dialog for adding a new known host or editing an existing one.
        /// </summary>
        /// <remarks>
        /// OK requires a host name, a machine ID and a Via value; when one is empty an error box is
        /// shown and the dialog stays open.
        /// </remarks>
        /// <param name="existingHost">
        /// The host whose values fill the fields, titled "Edit Known Host"; or null to start from
        /// empty fields with Direct reachability and Enabled checked, titled "Add Known Host".
        /// The entry itself is not changed; the edited values go into <see cref="Result"/>.
        /// </param>
        public KnownHostEditDialog(KnownHostEntry? existingHost = null) : base(existingHost != null ? "Edit Known Host" : "Add Known Host")
        {
            Width = 65;
            Height = 16;

            CreateLayout();

            if (existingHost != null)
            {
                LoadExistingHost(existingHost);
            }
            else
            {
                LoadDefaults();
            }
        }

        private void CreateLayout()
        {
            // Name
            Add(new Label("Host Name:") { X = 2, Y = 1 });
            _nameField = new TextField("") { X = 18, Y = 1, Width = 25 };
            Add(_nameField);

            // Machine ID
            Add(new Label("Machine ID:") { X = 2, Y = 2 });
            _machineIdField = new TextField("") { X = 18, Y = 2, Width = 20 };
            Add(_machineIdField);

            // Reachability
            Add(new Label("Reachability:") { X = 2, Y = 4 });
            _reachabilityCombo = new ComboBox()
            {
                X = 18,
                Y = 4,
                Width = 15,
                Height = 3
            };
            _reachabilityCombo.SetSource(new string[] { "Direct", "Gateway" });
            Add(_reachabilityCombo);

            // Via
            Add(new Label("Via:") { X = 2, Y = 6 });
            _viaField = new TextField("") { X = 18, Y = 6, Width = 25 };
            Add(_viaField);

            // Description
            Add(new Label("Description:") { X = 2, Y = 8 });
            _descriptionField = new TextField("") { X = 18, Y = 8, Width = 30 };
            Add(_descriptionField);

            // Enabled
            _enabledCheckBox = new CheckBox("Enabled") { X = 2, Y = 10 };
            Add(_enabledCheckBox);

            // Buttons
            var okButton = new Button("OK") { X = 15, Y = 12 };
            okButton.Clicked += OnOK;
            AddButton(okButton);

            var cancelButton = new Button("Cancel") { X = 25, Y = 12 };
            cancelButton.Clicked += OnCancel;
            AddButton(cancelButton);

            // Help text
            Add(new Label("Name: Friendly name for connections (e.g., 'MainServer')")
            { X = 2, Y = 3, Width = 50 });
            Add(new Label("Via: Interface name (Direct) or gateway machine ID (Gateway)")
            { X = 2, Y = 7, Width = 50 });
        }

        private void LoadDefaults()
        {
            _nameField.Text = "";
            _machineIdField.Text = "";
            _reachabilityCombo.SelectedItem = 0; // Direct
            _viaField.Text = "";
            _descriptionField.Text = "";
            _enabledCheckBox.Checked = true;
        }

        private void LoadExistingHost(KnownHostEntry host)
        {
            _nameField.Text = host.Name;
            _machineIdField.Text = host.MachineId;
            _reachabilityCombo.SelectedItem = (int)host.Reachability;
            _viaField.Text = host.Via;
            _descriptionField.Text = host.Description;
            _enabledCheckBox.Checked = host.Enabled;
        }

        private void OnOK()
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(_nameField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Host name is required", "OK");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_machineIdField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Machine ID is required", "OK");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_viaField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Via field is required (interface name or gateway)", "OK");
                    return;
                }

                // Create the result
                Result = new KnownHostEntry
                {
                    Name = _nameField.Text?.ToString() ?? "",
                    MachineId = _machineIdField.Text?.ToString() ?? "",
                    Reachability = (HostReachability)_reachabilityCombo.SelectedItem,
                    Via = _viaField.Text?.ToString() ?? "",
                    Description = _descriptionField.Text?.ToString() ?? "",
                    Enabled = _enabledCheckBox.Checked
                };

                Application.RequestStop();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to create host entry: {ex.Message}", "OK");
            }
        }

        private void OnCancel()
        {
            Result = null;
            Application.RequestStop();
        }
    }
}
