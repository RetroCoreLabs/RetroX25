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
    /// Dialog for editing XotInterfaceConfig objects
    /// </summary>
    public class XotInterfaceEditDialog : Dialog
    {
        private readonly XotInterfaceConfig? _originalInterface;
        private TextField _nameField = null!;
        private CheckBox _enabledCheckBox = null!;
        private TextField _localPortField = null!;
        private TextField _remoteAddressField = null!;
        private TextField _remotePortField = null!;
        private TextField _remoteMachineIdField = null!;
        private CheckBox _isServerCheckBox = null!;
        private TextField _descriptionField = null!;

        public XotInterfaceConfig? Result { get; private set; }

        public XotInterfaceEditDialog(XotInterfaceConfig? interfaceConfig)
            : base(interfaceConfig == null ? "Add XOT Interface" : "Edit XOT Interface", 70, 14)
        {
            _originalInterface = interfaceConfig;
            InitializeComponent();
            LoadCurrentValues();
        }

        private void InitializeComponent()
        {
            // Name
            Add(new Label("Interface Name:") { X = 2, Y = 1 });
            _nameField = new TextField("") { X = 20, Y = 1, Width = 20 };
            Add(_nameField);

            // Enabled
            _enabledCheckBox = new CheckBox("Enabled") { X = 2, Y = 3 };
            Add(_enabledCheckBox);

            // Local Port
            Add(new Label("Local Port:") { X = 2, Y = 4 });
            _localPortField = new TextField("") { X = 20, Y = 4, Width = 10 };
            Add(_localPortField);

            // Remote Address
            Add(new Label("Remote IP Address:") { X = 2, Y = 5 });
            _remoteAddressField = new TextField("") { X = 20, Y = 5, Width = 20 };
            Add(_remoteAddressField);

            // Remote Port
            Add(new Label("Remote Port:") { X = 2, Y = 6 });
            _remotePortField = new TextField("") { X = 20, Y = 6, Width = 10 };
            Add(_remotePortField);

            // Remote Machine ID
            Add(new Label("Remote Machine ID:") { X = 2, Y = 7 });
            _remoteMachineIdField = new TextField("") { X = 20, Y = 7, Width = 20 };
            Add(_remoteMachineIdField);

            // Is Server
            _isServerCheckBox = new CheckBox("Server Mode (accept connections)") { X = 2, Y = 8 };
            Add(_isServerCheckBox);

            // Description
            Add(new Label("Description:") { X = 2, Y = 9 });
            _descriptionField = new TextField("") { X = 20, Y = 9, Width = 30 };
            Add(_descriptionField);

            // Buttons
            var okButton = new Button("OK") { X = 15, Y = 11 };
            okButton.Clicked += OnOk;
            AddButton(okButton);

            var cancelButton = new Button("Cancel") { X = 25, Y = 11 };
            cancelButton.Clicked += OnCancel;
            AddButton(cancelButton);

            // Help text
            Add(new Label("Standard XOT port: 1998")
            { X = 45, Y = 4, Width = 20 });
        }

        private void LoadCurrentValues()
        {
            if (_originalInterface != null)
            {
                _nameField.Text = _originalInterface.Name;
                _enabledCheckBox.Checked = _originalInterface.Enabled;
                _localPortField.Text = _originalInterface.LocalPort.ToString();
                _remoteAddressField.Text = _originalInterface.RemoteIPAddress ?? "";
                _remotePortField.Text = _originalInterface.RemotePort.ToString();
                _remoteMachineIdField.Text = _originalInterface.RemoteMachineId;
                _isServerCheckBox.Checked = _originalInterface.Mode == XotMode.Server;
                _descriptionField.Text = _originalInterface.Description;
            }
            else
            {
                // Default values for new interface
                _nameField.Text = "XOT0";
                _enabledCheckBox.Checked = true;
                _localPortField.Text = "1998";
                _remoteAddressField.Text = "";
                _remotePortField.Text = "1998";
                _remoteMachineIdField.Text = "";
                _isServerCheckBox.Checked = false;
                _descriptionField.Text = "";
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


                if (!int.TryParse(_localPortField.Text.ToString(), out int localPort) || localPort < 1 || localPort > 65535)
                {
                    MessageBox.ErrorQuery("Validation Error", "Invalid local port (1-65535)", "OK");
                    return;
                }

                if (!int.TryParse(_remotePortField.Text.ToString(), out int remotePort) || remotePort < 1 || remotePort > 65535)
                {
                    MessageBox.ErrorQuery("Validation Error", "Invalid remote port (1-65535)", "OK");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_remoteMachineIdField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Remote machine ID is required", "OK");
                    return;
                }

                // For client mode, remote address is required
                if (!_isServerCheckBox.Checked && string.IsNullOrWhiteSpace(_remoteAddressField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Remote IP address is required for client mode", "OK");
                    return;
                }

                // Create result
                Result = new XotInterfaceConfig
                {
                    Name = _nameField.Text?.ToString() ?? "",
                    Enabled = _enabledCheckBox.Checked,
                    LocalPort = localPort,
                    RemoteIPAddress = _remoteAddressField.Text?.ToString() ?? "",
                    RemotePort = remotePort,
                    RemoteMachineId = _remoteMachineIdField.Text?.ToString() ?? "",
                    Mode = _isServerCheckBox.Checked ? XotMode.Server : XotMode.Client,
                    Description = _descriptionField.Text?.ToString() ?? ""
                };

                Application.RequestStop();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to create XOT interface: {ex.Message}", "OK");
            }
        }

        private void OnCancel()
        {
            Result = null;
            Application.RequestStop();
        }
    }
}
