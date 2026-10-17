//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for SINTRAN TAD Client Window - connects to remote SINTRAN TAD servers.
/// </summary>
public partial class SintranTadClientWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;
    private readonly int _windowNumber;
    private readonly StringBuilder _consoleBuffer = new();
    private SintranTadClient? _tadClient;

    [ObservableProperty]
    private string _consoleText = "";

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _remoteAddress = "";

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private string _windowTitle = "SINTRAN TAD Client #1";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _canConnect = true;

    [ObservableProperty]
    private bool _canDisconnect = false;

    /// <summary>
    /// Creates the view-model for one TAD client window and writes the opening help text to
    /// its console.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model; it is also handed to the TAD client created on connect.
    /// </param>
    /// <param name="runtimeService">
    /// Service that gives access to the protocol runtime the TAD client calls through.
    /// </param>
    /// <param name="dialogService">
    /// Service used for connection error messages.
    /// </param>
    /// <param name="windowNumber">
    /// The number of this client window. It appears in the window title and in the client
    /// ID, which is "TadClient" followed by the number.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/>, <paramref name="runtimeService"/> or
    /// <paramref name="dialogService"/> is null.
    /// </exception>
    public SintranTadClientWindowViewModel(
        ILogger<SintranTadClientWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService,
        int windowNumber = 1) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _windowNumber = windowNumber;

        WindowTitle = $"SINTRAN TAD Client #{windowNumber}";

        AppendText("SINTRAN TAD Client ready.\n");
        AppendText("Enter remote address (e.g., 100, 101, 102) and click Connect.\n\n");

        Logger.LogInformation("SintranTadClientWindowViewModel #{WindowNumber} initialized", windowNumber);
    }

    [RelayCommand]
    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(RemoteAddress))
        {
            AppendText("ERROR: Please enter a remote address\n");
            return;
        }

        if (!_runtimeService.IsRunning)
        {
            AppendText("ERROR: Protocol runtime is not running\n");
            _dialogService.ShowError("Connection Error", "Protocol runtime is not running");
            return;
        }

        try
        {
            AppendText($"Connecting to SINTRAN TAD server at {RemoteAddress}...\n");

            // Create SINTRAN TAD Client
            string clientId = $"TadClient{_windowNumber}";
            _tadClient = new SintranTadClient(Logger, _runtimeService.Runtime, clientId);

            // Subscribe to events
            _tadClient.ConnectionStateChanged += OnTadClientConnectionStateChanged;
            _tadClient.DataReceived += OnTadClientDataReceived;
            _tadClient.ErrorOccurred += OnTadClientError;

            // Attempt connection
            bool success = _tadClient.Connect(RemoteAddress);

            if (success)
            {
                AppendText($"Call placed to {RemoteAddress}. Waiting for connection...\n");
                CanConnect = false;
                CanDisconnect = true;
            }
            else
            {
                AppendText("ERROR: Failed to place call\n");
                _tadClient?.Dispose();
                _tadClient = null;
            }

            Logger.LogInformation("SINTRAN TAD Client #{WindowNumber} connecting to {Address}", _windowNumber, RemoteAddress);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to connect SINTRAN TAD client");
            AppendText($"Connection failed: {ex.Message}\n");
            _dialogService.ShowError("Connection Error", ex.Message);
            _tadClient?.Dispose();
            _tadClient = null;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        try
        {
            if (_tadClient != null)
            {
                _tadClient.Disconnect();
                _tadClient.ConnectionStateChanged -= OnTadClientConnectionStateChanged;
                _tadClient.DataReceived -= OnTadClientDataReceived;
                _tadClient.ErrorOccurred -= OnTadClientError;
                _tadClient.Dispose();
                _tadClient = null;
            }

            AppendText("Disconnected from SINTRAN TAD server\n");

            IsConnected = false;
            CanConnect = true;
            CanDisconnect = false;
            StatusText = "Disconnected";

            Logger.LogInformation("SINTRAN TAD Client #{WindowNumber} disconnected", _windowNumber);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to disconnect SINTRAN TAD client");
            AppendText($"Disconnect failed: {ex.Message}\n");
        }
    }

    [RelayCommand]
    private void SendInput()
    {
        if (string.IsNullOrEmpty(InputText))
            return;

        if (!IsConnected || _tadClient == null)
        {
            AppendText("Not connected. Please connect first.\n");
            return;
        }

        try
        {
            string textToSend = InputText + "\r\n";
            AppendText($"> {InputText}\n");
            _tadClient.SendData(textToSend);
            Logger.LogDebug("SINTRAN TAD Client #{WindowNumber} sent: {Input}", _windowNumber, InputText);
            InputText = "";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send data");
            AppendText($"ERROR: Failed to send data: {ex.Message}\n");
        }
    }

    [RelayCommand]
    private void ClearConsole()
    {
        _consoleBuffer.Clear();
        ConsoleText = "";
        Logger.LogInformation("Console cleared");
    }

    /// <summary>
    /// Handle connection state changes from TAD client.
    /// </summary>
    private void OnTadClientConnectionStateChanged(object? sender, SintranTadClientEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StatusText = $"{e.State}: {e.Message}";
            AppendText($"[Connection State] {e.State}: {e.Message}\n");

            if (e.State == TadSessionState.Active)
            {
                IsConnected = true;
                AppendText("Connected! You can now type and send messages.\n");
            }
            else if (e.State == TadSessionState.Closed)
            {
                IsConnected = false;
                CanConnect = true;
                CanDisconnect = false;
            }
        });
    }

    /// <summary>
    /// Handle data received from TAD client.
    /// </summary>
    private void OnTadClientDataReceived(object? sender, SintranTadClientDataEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AppendText(e.Data);
        });
    }

    /// <summary>
    /// Handle errors from TAD client.
    /// </summary>
    private void OnTadClientError(object? sender, SintranTadClientEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AppendText($"ERROR: {e.Message}\n");
        });
    }

    private void AppendText(string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\b':
                    if (_consoleBuffer.Length > 0)
                        _consoleBuffer.Length--;
                    break;
                case '\n':
                    _consoleBuffer.Append(Environment.NewLine);
                    break;
                case '\r':
                    break;
                default:
                    _consoleBuffer.Append(c);
                    break;
            }
        }

        ConsoleText = _consoleBuffer.ToString();
    }

    /// <summary>
    /// Releases the TAD client, if there is one, after removing this view-model's event
    /// handlers from it.
    /// </summary>
    /// <remarks>
    /// Unlike the Disconnect command, this does not call the client's own disconnect first;
    /// it disposes the client directly.
    /// </remarks>
    public override void Dispose()
    {
        if (_tadClient != null)
        {
            _tadClient.ConnectionStateChanged -= OnTadClientConnectionStateChanged;
            _tadClient.DataReceived -= OnTadClientDataReceived;
            _tadClient.ErrorOccurred -= OnTadClientError;
            _tadClient.Dispose();
            _tadClient = null;
        }

        Logger.LogInformation("SintranTadClientWindowViewModel #{WindowNumber} disposed", _windowNumber);
        base.Dispose();
    }
}
