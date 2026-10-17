//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Link;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for Serial Interface Edit Dialog.
/// </summary>
public partial class SerialInterfaceEditDialogViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly bool _isNew;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _portName = "COM1";

    [ObservableProperty]
    private int _baudRate = 9600;

    [ObservableProperty]
    private int _dataBits = 8;

    [ObservableProperty]
    private string _parity = "None";

    [ObservableProperty]
    private string _stopBits = "One";

    [ObservableProperty]
    private string _framing = "Hdlc";

    [ObservableProperty]
    private string _remoteMachineId = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _validationError = "";

    /// <summary>
    /// Available baud rates.
    /// </summary>
    public List<int> AvailableBaudRates { get; } = new() { 300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

    /// <summary>
    /// Available parity options.
    /// </summary>
    public List<string> AvailableParities { get; } = new() { "None", "Odd", "Even", "Mark", "Space" };

    /// <summary>
    /// Available stop bits options.
    /// </summary>
    public List<string> AvailableStopBits { get; } = new() { "None", "One", "Two", "OnePointFive" };

    /// <summary>
    /// Available framing methods. Hdlc = RFC 1662 byte stuffing; Slip = RFC 1055 byte stuffing.
    /// </summary>
    public List<string> AvailableFramings { get; } = new() { "Hdlc", "Slip" };

    /// <summary>
    /// Result of the dialog (null if cancelled).
    /// </summary>
    public SerialInterfaceConfig? Result { get; private set; }

    /// <summary>
    /// Creates the dialog view-model and fills its fields from <paramref name="existing"/>.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model.
    /// </param>
    /// <param name="existing">
    /// The interface whose values fill the fields, or null to start from the field defaults.
    /// </param>
    /// <param name="isNew">
    /// True when the dialog adds a new interface, false when it edits one. The value is
    /// stored, but nothing in this view-model reads it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    public SerialInterfaceEditDialogViewModel(ILogger logger, SerialInterfaceConfig? existing, bool isNew)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isNew = isNew;

        if (existing != null)
        {
            LoadFromConfig(existing);
        }
    }

    /// <summary>
    /// Load values from existing configuration.
    /// </summary>
    private void LoadFromConfig(SerialInterfaceConfig config)
    {
        Name = config.Name;
        Enabled = config.Enabled;
        PortName = config.PortName;
        BaudRate = config.BaudRate;
        DataBits = config.DataBits;
        Parity = config.Parity.ToString();
        StopBits = config.StopBits.ToString();
        Framing = config.Framing.ToString();
        RemoteMachineId = config.RemoteMachineId;
        Description = config.Description;
    }

    /// <summary>
    /// Validate inputs.
    /// </summary>
    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Name is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(PortName))
        {
            ValidationError = "Port name is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RemoteMachineId))
        {
            ValidationError = "Remote machine ID is required";
            return false;
        }

        if (DataBits < 5 || DataBits > 8)
        {
            ValidationError = "Data bits must be between 5 and 8";
            return false;
        }

        ValidationError = "";
        return true;
    }

    [RelayCommand]
    private void Ok()
    {
        if (!Validate())
            return;

        try
        {
            // Create result configuration
            Result = new SerialInterfaceConfig
            {
                Name = Name.Trim(),
                Enabled = Enabled,
                PortName = PortName.Trim(),
                BaudRate = BaudRate,
                DataBits = DataBits,
                Parity = Enum.Parse<System.IO.Ports.Parity>(Parity),
                StopBits = Enum.Parse<System.IO.Ports.StopBits>(StopBits),
                Framing = Enum.Parse<FramingType>(Framing),
                RemoteMachineId = RemoteMachineId.Trim(),
                Description = Description.Trim()
            };

            _logger.LogInformation("Serial interface dialog OK: {Name}", Result.Name);

            // Dialog will be closed by the caller
        }
        catch (Exception ex)
        {
            ValidationError = $"Error: {ex.Message}";
            _logger.LogError(ex, "Error creating serial interface config");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        _logger.LogDebug("Serial interface dialog cancelled");
        // Dialog will be closed by the caller
    }
}
