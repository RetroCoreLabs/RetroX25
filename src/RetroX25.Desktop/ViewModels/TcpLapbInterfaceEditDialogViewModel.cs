//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RetroIO.Link;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the TCP-LAPB Interface Edit Dialog: one interface that carries LAPB (the X.25
/// link layer) over a TCP connection, with its own LAPB timers and window.
/// </summary>
public partial class TcpLapbInterfaceEditDialogViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly bool _isNew;
    private readonly TcpLapbInterfaceConfig? _original;

    [ObservableProperty]
    private string _name = "TcpLapb0";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _ipAddress = "127.0.0.1";

    [ObservableProperty]
    private int _port = 5555;

    [ObservableProperty]
    private string _remoteMachineId = "";

    [ObservableProperty]
    private bool _isServer = true;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private int _windowSize = 7;

    [ObservableProperty]
    private int _t1TimeoutMs = 3000;

    [ObservableProperty]
    private int _t2TimeoutMs = 1500;

    [ObservableProperty]
    private int _t3TimeoutMs = 0;

    [ObservableProperty]
    private int _maxRetries = 10;

    [ObservableProperty]
    private string _validationError = "";

    /// <summary>
    /// Gets the interface built from the fields after <see cref="TryAccept"/> succeeded; null before
    /// that, and null after a failed attempt.
    /// </summary>
    public TcpLapbInterfaceConfig? Result { get; private set; }

    /// <summary>
    /// Gets the title of the dialog window.
    /// </summary>
    public string DialogTitle => _isNew ? "Add TCP-LAPB Interface" : "Edit TCP-LAPB Interface";

    /// <summary>
    /// Creates the dialog view-model and fills its fields from <paramref name="existing"/>.
    /// </summary>
    /// <param name="logger">Logger for this view-model.</param>
    /// <param name="existing">
    /// The interface whose values fill the fields, or null to start from these defaults: name TcpLapb0,
    /// enabled, server mode on 127.0.0.1 port 5555, window 7, T1 3000 ms, T2 1500 ms, T3 off, N2 10,
    /// X.25 only. The object itself is not changed.
    /// </param>
    /// <param name="isNew">True when the dialog adds an interface; it decides the title.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public TcpLapbInterfaceEditDialogViewModel(ILogger logger, TcpLapbInterfaceConfig? existing, bool isNew)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isNew = isNew;
        _original = existing;

        if (existing != null)
        {
            Name = existing.Name;
            Enabled = existing.Enabled;
            IpAddress = existing.RemoteAddress ?? "127.0.0.1";
            Port = existing.Port;
            RemoteMachineId = existing.RemoteMachineId;
            IsServer = existing.Mode == TcpMode.Server;
            Description = existing.Description;
            WindowSize = existing.LapbWindowSize;
            T1TimeoutMs = existing.LapbT1TimeoutMs;
            T2TimeoutMs = existing.LapbT2TimeoutMs;
            T3TimeoutMs = existing.LapbT3TimeoutMs;
            MaxRetries = existing.LapbMaxRetries;
        }
    }

    /// <summary>
    /// Checks the fields and, when they are all right, builds <see cref="Result"/>.
    /// </summary>
    /// <remarks>
    /// The rules, in the order they are checked (the first that fails sets
    /// <see cref="ValidationError"/>):
    ///  - the name is filled in;
    ///  - the port is from 1 to 65535;
    ///  - the remote machine ID is filled in;
    ///  - an IP address is given unless server mode is on;
    ///  - the window size K is from 1 to 7;
    ///  - T1 is at least 100 ms;
    ///  - T2 is 0 or more;
    ///  - T3 is 0 (off) or more;
    ///  - N2, the number of retries, is from 1 to 255 (it is stored in one byte);
    ///  - T2 is no more than half of T1 (ITU-T X.25);
    ///  - T3, when on, is greater than T1.
    /// When the dialog edits an interface, the settings it does not show (primary side, connect
    /// timeout, reconnect delay, LAPB auto-connect, framing) are kept from the original.
    /// </remarks>
    /// <returns>True when the fields are right and <see cref="Result"/> is set.</returns>
    public bool TryAccept()
    {
        Result = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Interface name is required";
            return false;
        }

        if (Port < 1 || Port > 65535)
        {
            ValidationError = "Invalid port (1-65535)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RemoteMachineId))
        {
            ValidationError = "Remote machine ID is required";
            return false;
        }

        if (!IsServer && string.IsNullOrWhiteSpace(IpAddress))
        {
            ValidationError = "IP address is required for client mode";
            return false;
        }

        if (WindowSize < 1 || WindowSize > 7)
        {
            ValidationError = "Window size must be 1-7";
            return false;
        }

        if (T1TimeoutMs < 100)
        {
            ValidationError = "T1 timeout must be >= 100ms";
            return false;
        }

        if (T2TimeoutMs < 0)
        {
            ValidationError = "T2 timeout must be >= 0ms";
            return false;
        }

        if (T3TimeoutMs < 0)
        {
            ValidationError = "T3 timeout must be >= 0ms (0=disabled)";
            return false;
        }

        if (MaxRetries < 1 || MaxRetries > byte.MaxValue)
        {
            ValidationError = "Max retries (N2) must be 1-255";
            return false;
        }

        // ITU-T X.25: the acknowledge delay T2 must be short enough to beat the retransmit timer T1.
        if (T2TimeoutMs > T1TimeoutMs / 2)
        {
            ValidationError = $"T2 ({T2TimeoutMs}ms) must be <= T1/2 ({T1TimeoutMs / 2}ms) per ITU-T X.25";
            return false;
        }

        if (T3TimeoutMs > 0 && T3TimeoutMs <= T1TimeoutMs)
        {
            ValidationError = $"T3 ({T3TimeoutMs}ms) must be > T1 ({T1TimeoutMs}ms) when enabled";
            return false;
        }

        TcpLapbInterfaceConfig result = new TcpLapbInterfaceConfig
        {
            Name = Name.Trim(),
            Enabled = Enabled,
            RemoteAddress = IpAddress.Trim(),
            Port = Port,
            RemoteMachineId = RemoteMachineId.Trim(),
            Mode = IsServer ? TcpMode.Server : TcpMode.Client,
            Description = Description.Trim(),
            LapbWindowSize = (byte)WindowSize,
            LapbT1TimeoutMs = T1TimeoutMs,
            LapbT2TimeoutMs = T2TimeoutMs,
            LapbT3TimeoutMs = T3TimeoutMs,
            LapbMaxRetries = (byte)MaxRetries,
            // SINTRAN/XMSG is not part of RetroX25: a TCP-LAPB link carries X.25 only.
            ProtocolMode = ProtocolDetectionMode.X25Only
        };

        if (_original != null)
        {
            result.IsPrimary = _original.IsPrimary;
            result.ConnectTimeoutMs = _original.ConnectTimeoutMs;
            result.ReconnectDelayMs = _original.ReconnectDelayMs;
            result.LapbAutoConnect = _original.LapbAutoConnect;
            result.Framing = _original.Framing;
        }

        ValidationError = "";
        Result = result;
        _logger.LogInformation("TCP-LAPB interface dialog OK: {Name}", result.Name);
        return true;
    }
}
