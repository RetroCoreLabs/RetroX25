using System;
using System.IO.Ports;
using Terminal.Gui;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Dialog for editing SerialInterfaceConfig objects
    /// </summary>
    public class SerialInterfaceEditDialog : Dialog
    {
        private readonly SerialInterfaceConfig? _originalInterface;
        private TextField _nameField = null!;
        private CheckBox _enabledCheckBox = null!;
        private TextField _portNameField = null!;
        private TextField _baudRateField = null!;
        private TextField _dataBitsField = null!;
        private ComboBox _parityComboBox = null!;
        private ComboBox _stopBitsComboBox = null!;
        private TextField _remoteMachineIdField = null!;
        private TextField _descriptionField = null!;

        public SerialInterfaceConfig? Result { get; private set; }

        public SerialInterfaceEditDialog(SerialInterfaceConfig? interfaceConfig) 
            : base(interfaceConfig == null ? "Add Serial Interface" : "Edit Serial Interface", 70, 18)
        {
            _originalInterface = interfaceConfig;
            InitializeComponent();
            LoadCurrentValues();
        }

        private void InitializeComponent()
        {
            // Name
            Add(new Label("Interface Name:") { X = 2, Y = 1 });
            _nameField = new TextField("") { X = 18, Y = 1, Width = 20 };
            Add(_nameField);

            // Enabled
            _enabledCheckBox = new CheckBox("Enabled") { X = 2, Y = 3 };
            Add(_enabledCheckBox);

            // Port Name
            Add(new Label("Port Name:") { X = 2, Y = 4 });
            _portNameField = new TextField("") { X = 18, Y = 4, Width = 15 };
            Add(_portNameField);

            // Baud Rate
            Add(new Label("Baud Rate:") { X = 2, Y = 5 });
            _baudRateField = new TextField("") { X = 18, Y = 5, Width = 15 };
            Add(_baudRateField);

            // Data Bits
            Add(new Label("Data Bits:") { X = 2, Y = 6 });
            _dataBitsField = new TextField("") { X = 18, Y = 6, Width = 15 };
            Add(_dataBitsField);

            // Parity
            Add(new Label("Parity:") { X = 2, Y = 7 });
            _parityComboBox = new ComboBox()
            {
                X = 18,
                Y = 7,
                Width = 15,
                Height = 4
            };
            _parityComboBox.SetSource(new string[] { "None", "Odd", "Even", "Mark", "Space" });
            Add(_parityComboBox);

            // Stop Bits
            Add(new Label("Stop Bits:") { X = 2, Y = 8 });
            _stopBitsComboBox = new ComboBox()
            {
                X = 18,
                Y = 8,
                Width = 15,
                Height = 4
            };
            _stopBitsComboBox.SetSource(new string[] { "None", "One", "Two", "OnePointFive" });
            Add(_stopBitsComboBox);

            // Remote Machine ID
            Add(new Label("Remote Machine ID:") { X = 2, Y = 9 });
            _remoteMachineIdField = new TextField("") { X = 18, Y = 9, Width = 20 };
            Add(_remoteMachineIdField);

            // Description
            Add(new Label("Description:") { X = 2, Y = 10 });
            _descriptionField = new TextField("") { X = 18, Y = 10, Width = 30 };
            Add(_descriptionField);

            // Buttons
            var okButton = new Button("OK") { X = 15, Y = 12 };
            okButton.Clicked += OnOk;
            AddButton(okButton);

            var cancelButton = new Button("Cancel") { X = 25, Y = 12 };
            cancelButton.Clicked += OnCancel;
            AddButton(cancelButton);

            // Help text
            Add(new Label("Common ports: COM1, COM2, /dev/ttyS0, /dev/ttyUSB0") 
                { X = 35, Y = 4, Width = 30 });
            Add(new Label("Common rates: 9600, 19200, 38400, 115200") 
                { X = 35, Y = 5, Width = 30 });
        }

        private void LoadCurrentValues()
        {
            if (_originalInterface != null)
            {
                _nameField.Text = _originalInterface.Name;
                _enabledCheckBox.Checked = _originalInterface.Enabled;
                _portNameField.Text = _originalInterface.PortName;
                _baudRateField.Text = _originalInterface.BaudRate.ToString();
                _dataBitsField.Text = _originalInterface.DataBits.ToString();
                _parityComboBox.SelectedItem = (int)_originalInterface.Parity;
                _stopBitsComboBox.SelectedItem = (int)_originalInterface.StopBits;
                _remoteMachineIdField.Text = _originalInterface.RemoteMachineId;
                _descriptionField.Text = _originalInterface.Description;
            }
            else
            {
                // Default values for new interface
                _nameField.Text = "Serial0";
                _enabledCheckBox.Checked = true;
                _portNameField.Text = "COM1";
                _baudRateField.Text = "19200";
                _dataBitsField.Text = "8";
                _parityComboBox.SelectedItem = 0; // None
                _stopBitsComboBox.SelectedItem = 1; // One
                _remoteMachineIdField.Text = "";
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

                if (string.IsNullOrWhiteSpace(_portNameField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Port name is required", "OK");
                    return;
                }

                if (!int.TryParse(_baudRateField.Text.ToString(), out int baudRate) || baudRate <= 0)
                {
                    MessageBox.ErrorQuery("Validation Error", "Invalid baud rate", "OK");
                    return;
                }

                if (!int.TryParse(_dataBitsField.Text.ToString(), out int dataBits) || dataBits < 5 || dataBits > 8)
                {
                    MessageBox.ErrorQuery("Validation Error", "Data bits must be between 5 and 8", "OK");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_remoteMachineIdField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Validation Error", "Remote machine ID is required", "OK");
                    return;
                }

                // Create result
                Result = new SerialInterfaceConfig
                {
                    Name = _nameField.Text?.ToString() ?? "",
                    Enabled = _enabledCheckBox.Checked,
                    PortName = _portNameField.Text?.ToString() ?? "",
                    BaudRate = baudRate,
                    DataBits = dataBits,
                    Parity = (Parity)_parityComboBox.SelectedItem,
                    StopBits = (StopBits)_stopBitsComboBox.SelectedItem,
                    RemoteMachineId = _remoteMachineIdField.Text?.ToString() ?? "",
                    Description = _descriptionField.Text?.ToString() ?? ""
                };

                Application.RequestStop();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to create interface: {ex.Message}", "OK");
            }
        }

        private void OnCancel()
        {
            Result = null;
            Application.RequestStop();
        }
    }
}