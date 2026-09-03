using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for XOT Interface Edit Dialog.
/// </summary>
public partial class XotInterfaceEditDialogViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly bool _isNew;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _mode = "Client";

    [ObservableProperty]
    private string _remoteIPAddress = "";

    [ObservableProperty]
    private int _remotePort = 1998;

    [ObservableProperty]
    private int _localPort = 1998;

    [ObservableProperty]
    private string _remoteMachineId = "";

    [ObservableProperty]
    private bool _tcpKeepalive = true;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _validationError = "";

    /// <summary>
    /// Available XOT modes.
    /// </summary>
    public List<string> AvailableModes { get; } = new() { "Client", "Server" };

    /// <summary>
    /// Result of the dialog (null if cancelled).
    /// </summary>
    public XotInterfaceConfig? Result { get; private set; }

    public XotInterfaceEditDialogViewModel(ILogger logger, XotInterfaceConfig? existing, bool isNew)
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
    private void LoadFromConfig(XotInterfaceConfig config)
    {
        Name = config.Name;
        Enabled = config.Enabled;
        Mode = config.Mode.ToString();
        RemoteIPAddress = config.RemoteIPAddress ?? "";
        RemotePort = config.RemotePort;
        LocalPort = config.LocalPort;
        RemoteMachineId = config.RemoteMachineId;
        TcpKeepalive = config.TcpKeepalive;
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

        if (Mode == "Client" && string.IsNullOrWhiteSpace(RemoteIPAddress))
        {
            ValidationError = "Remote IP address is required for Client mode";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RemoteMachineId))
        {
            ValidationError = "Remote machine ID is required";
            return false;
        }

        if (RemotePort < 1 || RemotePort > 65535)
        {
            ValidationError = "Remote port must be between 1 and 65535";
            return false;
        }

        if (LocalPort < 1 || LocalPort > 65535)
        {
            ValidationError = "Local port must be between 1 and 65535";
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
            Result = new XotInterfaceConfig
            {
                Name = Name.Trim(),
                Enabled = Enabled,
                Mode = Enum.Parse<XotMode>(Mode),
                RemoteIPAddress = RemoteIPAddress.Trim(),
                RemotePort = RemotePort,
                LocalPort = LocalPort,
                RemoteMachineId = RemoteMachineId.Trim(),
                TcpKeepalive = TcpKeepalive,
                Description = Description.Trim()
            };

            _logger.LogInformation("XOT interface dialog OK: {Name}", Result.Name);

            // Dialog will be closed by the caller
        }
        catch (Exception ex)
        {
            ValidationError = $"Error: {ex.Message}";
            _logger.LogError(ex, "Error creating XOT interface config");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        _logger.LogDebug("XOT interface dialog cancelled");
        // Dialog will be closed by the caller
    }
}
