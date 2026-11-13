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
    private string _mode = BothText;

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
    private bool _allowOnlyListedPeers;

    [ObservableProperty]
    private string _description = "";

    /// <summary>Window size the calls of this peer ask for (1 to 7); empty = the X.25 setting of the whole app.</summary>
    [ObservableProperty]
    private int? _requestedWindowSize;

    /// <summary>Packet size the calls of this peer ask for (a power of two, 16 to 4096); empty = the X.25 setting of the whole app.</summary>
    [ObservableProperty]
    private int? _requestedPacketSize;

    [ObservableProperty]
    private string _validationError = "";

    /// <summary>
    /// Available XOT modes.
    /// </summary>
    public List<string> AvailableModes { get; } = new() { ClientText, ServerText, BothText };

    /// <summary>
    /// The mode texts the window shows. "Both ways (peer)" is ONE entry that dials out when a call is
    /// routed to it and also accepts connections from the same peer (RFC 1613 has no client and server).
    /// </summary>
    public const string ClientText = "Client";

    /// <summary>The text of the Server mode.</summary>
    public const string ServerText = "Server";

    /// <summary>The text of the Both mode.</summary>
    public const string BothText = "Both ways (peer)";

    /// <summary>Turns a mode into the text the window shows.</summary>
    public static string ModeToText(XotMode mode)
    {
        return mode == XotMode.Both ? BothText : mode == XotMode.Server ? ServerText : ClientText;
    }

    /// <summary>Turns the text the window shows back into a mode.</summary>
    public static XotMode TextToMode(string text)
    {
        return text == BothText ? XotMode.Both : text == ServerText ? XotMode.Server : XotMode.Client;
    }

    /// <summary>
    /// The entry the dialog started from, or null for a new one. Settings the dialog has no field
    /// for (channel range, timeouts) are carried over from it instead of being reset to defaults.
    /// </summary>
    private readonly XotInterfaceConfig? _existing;

    /// <summary>
    /// Result of the dialog (null if cancelled).
    /// </summary>
    public XotInterfaceConfig? Result { get; private set; }

    /// <summary>
    /// Gets the title of the dialog window: <c>Add XOT Interface</c> when the dialog adds an entry and
    /// <c>Edit XOT Interface</c> when it edits one.
    /// </summary>
    public string DialogTitle => _isNew ? "Add XOT Interface" : "Edit XOT Interface";

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
    /// True when the dialog adds a new xot interface, false when it edits one. It decides the
    /// words in <see cref="DialogTitle"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    public XotInterfaceEditDialogViewModel(ILogger logger, XotInterfaceConfig? existing, bool isNew)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isNew = isNew;
        _existing = existing;

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
        Mode = ModeToText(config.Mode);
        RemoteIPAddress = config.RemoteIPAddress ?? "";
        RemotePort = config.RemotePort;
        LocalPort = config.LocalPort;
        RemoteMachineId = config.RemoteMachineId;
        TcpKeepalive = config.TcpKeepalive;
        AllowOnlyListedPeers = config.AllowOnlyListedPeers;
        Description = config.Description;
        RequestedWindowSize = config.RequestedWindowSize;
        RequestedPacketSize = config.RequestedPacketSize;
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

        if (Mode == ClientText && string.IsNullOrWhiteSpace(RemoteIPAddress))
        {
            ValidationError = "Remote IP address is required for Client mode";
            return false;
        }

        if (Mode == BothText && string.IsNullOrWhiteSpace(RemoteIPAddress))
        {
            ValidationError = "The peer's IP address is required for a peer entry: it is where we dial, and the address we accept connections from";
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

        // The same rules the engine checks when it starts (ProtocolRuntime.ValidateXotSizes), so a wrong value is
        // refused here and not when Apply restarts the engine.
        if (RequestedWindowSize.HasValue && (RequestedWindowSize.Value < 1 || RequestedWindowSize.Value > 7))
        {
            ValidationError = "Window size must be between 1 and 7, or empty to use the X.25 setting";
            return false;
        }

        if (RequestedPacketSize.HasValue)
        {
            int size = RequestedPacketSize.Value;
            bool powerOfTwo = size >= 16 && size <= 4096 && (size & (size - 1)) == 0;
            if (!powerOfTwo)
            {
                ValidationError = "Packet size must be a power of two from 16 to 4096 (16, 32, 64, 128, 256, 512, 1024, 2048, 4096), or empty to use the X.25 setting";
                return false;
            }
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
                Mode = TextToMode(Mode),
                RemoteIPAddress = RemoteIPAddress.Trim(),
                RemotePort = RemotePort,
                LocalPort = LocalPort,
                RemoteMachineId = RemoteMachineId.Trim(),
                TcpKeepalive = TcpKeepalive,
                AllowOnlyListedPeers = AllowOnlyListedPeers,
                Description = Description.Trim(),
                RequestedWindowSize = RequestedWindowSize.HasValue ? (byte?)RequestedWindowSize.Value : null,
                RequestedPacketSize = RequestedPacketSize
            };

            if (_existing != null)
            {
                Result.LciBase = _existing.LciBase;
                Result.LciCount = _existing.LciCount;
                Result.KeepAliveIntervalSeconds = _existing.KeepAliveIntervalSeconds;
                Result.ConnectTimeoutMs = _existing.ConnectTimeoutMs;
                Result.ReconnectDelayMs = _existing.ReconnectDelayMs;
                Result.ProtocolMode = _existing.ProtocolMode;
            }

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
