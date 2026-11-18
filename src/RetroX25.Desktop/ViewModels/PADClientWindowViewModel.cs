//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Modules;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for PAD Client Window (Ctrl+5) - ITU-T X.3/X.28/X.29 PAD client.
/// </summary>
public partial class PADClientWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly PadClient _padClient;
    private readonly int _windowNumber;
    private readonly StringBuilder _consoleBuffer = new();

    [ObservableProperty]
    private string _consoleText = "";

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private string _charModeIndicator = "[Character Mode - Type directly, Ctrl+P for DLE]";

    [ObservableProperty]
    private ObservableCollection<string> _x3Parameters = new();

    [ObservableProperty]
    private string _windowTitle = "PAD Client Test #1";

    /// <summary>
    /// Creates PADClientWindowViewModel with dependency injection.
    /// </summary>
    public PADClientWindowViewModel(
        ILogger<PADClientWindowViewModel> logger,
        IRuntimeService runtimeService,
        int windowNumber = 1) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _windowNumber = windowNumber;

        // Create PAD client
        _padClient = _runtimeService.Runtime.CreatePadClient($"TestPAD_{windowNumber}");
        _padClient.LocalAddress = $"10{windowNumber}";

        // Subscribe to events
        _padClient.ConnectionStateChanged += OnConnectionStateChanged;
        _padClient.DataReceived += OnDataReceived;
        _padClient.CharacterEcho += OnCharacterEcho;
        _padClient.ParametersChanged += OnParametersChanged;

        WindowTitle = $"PAD Client Test #{windowNumber}";

        // Start PAD client
        _padClient.Start();
        UpdateX3ParametersList();

        AppendText("PAD Client ready - Character Mode Active\n");
        AppendText("Type CALL <address> to connect (press Enter to send)\n");
        AppendText("Commands: CALL, STAT, CLEAR, PAR?, SET <param>:<value>\n");
        AppendText("Special keys: Ctrl+P = DLE escape, Ctrl+A-Z = control codes\n\n");

        Logger.LogInformation("PADClientWindowViewModel #{WindowNumber} initialized", windowNumber);
    }

    /// <summary>
    /// Handle connection state changes.
    /// </summary>
    private void OnConnectionStateChanged(object? sender, PadConnectionEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StatusText = $"Connection: {e.State}";
            UpdateX3ParametersList();
        });
    }

    /// <summary>
    /// Handle received data from remote host.
    /// </summary>
    private void OnDataReceived(object? sender, PadDataEventArgs e)
    {
        QueueText(e.Data);
    }

    /// <summary>
    /// Text that arrived from the PAD (any thread) and is waiting for the UI thread.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _incomingText = new();

    /// <summary>
    /// 1 while a flush of <see cref="_incomingText"/> is already queued on the UI thread.
    /// </summary>
    private int _flushQueued;

    /// <summary>
    /// Queues text for the console. The UI thread takes everything that has arrived in ONE job and sets
    /// the console text once. One job per piece of text, each copying the whole console into a new string
    /// and redrawing it, made a burst of 300 typed and echoed lines take minutes (found by a burst test
    /// on 2026-11-12).
    /// </summary>
    private void QueueText(string text)
    {
        _incomingText.Enqueue(text);
        if (System.Threading.Interlocked.Exchange(ref _flushQueued, 1) == 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushIncomingText);
        }
    }

    private void FlushIncomingText()
    {
        // Open the gate BEFORE draining: text that arrives while this runs queues a new flush.
        System.Threading.Interlocked.Exchange(ref _flushQueued, 0);
        bool any = false;
        while (_incomingText.TryDequeue(out string? text))
        {
            AppendText(text, false);
            any = true;
        }

        if (any)
        {
            ConsoleText = _consoleBuffer.ToString();
        }
    }

    /// <summary>
    /// Handle character echo from PAD.
    /// </summary>
    private void OnCharacterEcho(object? sender, PadCharacterEventArgs e)
    {
        QueueText(((char)e.Character).ToString());
    }

    /// <summary>
    /// Handle X.3 parameter changes.
    /// </summary>
    private void OnParametersChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateX3ParametersList();
        });
    }

    /// <summary>
    /// Append text to console with proper handling of control characters.
    /// </summary>
    private void AppendText(string text, bool publish = true)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\b': // Backspace
                    if (_consoleBuffer.Length > 0)
                        _consoleBuffer.Length--;
                    break;

                case '\n': // Line feed
                    _consoleBuffer.Append(Environment.NewLine);
                    break;

                case '\r': // Carriage return (ignore)
                    break;

                case (char)0x7F: // DEL prints nothing on a terminal; the PAD echoes it and then shows "\" itself
                    break;

                default:
                    _consoleBuffer.Append(c);
                    break;
            }
        }

        if (publish)
        {
            ConsoleText = _consoleBuffer.ToString();
        }
    }

    /// <summary>
    /// Update X.3 parameters list.
    /// </summary>
    private void UpdateX3ParametersList()
    {
        try
        {
            var parameters = _padClient.X3Protocol.GetAllParameters();
            X3Parameters.Clear();

            for (byte paramNum = 1; paramNum <= 22; paramNum++)
            {
                if (parameters.TryGetValue(paramNum, out var value))
                {
                    var desc = GetShortParameterDescription(paramNum);
                    X3Parameters.Add($"P{paramNum:D2}: {value,3} - {desc}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating X.3 parameters list");
            X3Parameters.Clear();
            X3Parameters.Add("Error loading X.3 parameters");
        }
    }

    /// <summary>
    /// Get short description for X.3 parameters.
    /// </summary>
    private string GetShortParameterDescription(byte paramNum)
    {
        return paramNum switch
        {
            1 => "PAD Escape",
            2 => "Echo",
            3 => "Data Forward",
            4 => "Idle Timer",
            5 => "Device Control",
            6 => "Service Signals",
            7 => "Break Procedure",
            8 => "Discard Output",
            9 => "Padding CR",
            10 => "Line Folding",
            11 => "Binary Speed",
            12 => "Forward N Chars",
            13 => "LF Insertion",
            14 => "Padding LF",
            15 => "Editing",
            16 => "Char Delete",
            17 => "Line Delete",
            18 => "Line Display",
            19 => "Edit Signals",
            20 => "Echo Mask",
            21 => "Parity Check",
            22 => "Page Wait",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Handle KeyDown event for character mode input.
    /// Processes each key immediately and sends to PAD client.
    /// </summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        try
        {
            // Enter sends CR only. X.28 4.15: a terminal sends 0/13 (CR); the PAD inserts any LF itself,
            // as X.3 parameter 13 says. CR LF made the far side see a second, empty line.
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                _padClient.OnKey('\r'); // CR (0x0D)
                e.Handled = true;
                return;
            }

            // Handle Backspace
            if (e.Key == Key.Back)
            {
                _padClient.OnKey('\b'); // Backspace (0x08)
                e.Handled = true;
                return;
            }

            // The Delete key sends DEL (0x7F), the character the X.28 standard profiles and the Altos chat use as
            // "character delete" (X.3 parameter 16 = 127). Backspace above stays BS for hosts that edit with BS.
            if (e.Key == Key.Delete)
            {
                _padClient.OnKey((char)0x7F);
                e.Handled = true;
                return;
            }

            // Handle Tab
            if (e.Key == Key.Tab)
            {
                _padClient.OnKey('\t'); // Tab (0x09)
                e.Handled = true;
                return;
            }

            // Handle Escape
            if (e.Key == Key.Escape)
            {
                _padClient.OnKey((char)0x1B); // ESC (0x1B)
                e.Handled = true;
                return;
            }

            // Handle Ctrl+key combinations
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                // Ctrl+P = DLE (Data Link Escape, 0x10) per ITU-T X.28
                if (e.Key == Key.P)
                {
                    _padClient.OnKey((char)0x10); // DLE
                    Logger.LogDebug("Sent DLE (Ctrl+P)");
                    e.Handled = true;
                    return;
                }

                // Ctrl+A through Ctrl+Z = control codes 0x01 through 0x1A
                if (e.Key >= Key.A && e.Key <= Key.Z)
                {
                    char controlCode = (char)(e.Key - Key.A + 1);
                    _padClient.OnKey(controlCode);
                    Logger.LogDebug("Sent control code: Ctrl+{Key} = 0x{Code:X2}", e.Key, (int)controlCode);
                    e.Handled = true;
                    return;
                }

                // Ctrl+[ = ESC (0x1B)
                if (e.Key == Key.OemOpenBrackets)
                {
                    _padClient.OnKey((char)0x1B);
                    e.Handled = true;
                    return;
                }

                // Ctrl+\ = FS (0x1C)
                if (e.Key == Key.OemPipe)
                {
                    _padClient.OnKey((char)0x1C);
                    e.Handled = true;
                    return;
                }

                // Ctrl+] = GS (0x1D)
                if (e.Key == Key.OemCloseBrackets)
                {
                    _padClient.OnKey((char)0x1D);
                    e.Handled = true;
                    return;
                }

                // Ctrl+^ = RS (0x1E)
                if (e.Key == Key.D6) // ^ is on 6 key
                {
                    _padClient.OnKey((char)0x1E);
                    e.Handled = true;
                    return;
                }

                // Ctrl+_ = US (0x1F)
                if (e.Key == Key.OemMinus)
                {
                    _padClient.OnKey((char)0x1F);
                    e.Handled = true;
                    return;
                }

                // Ctrl+@ = NUL (0x00)
                if (e.Key == Key.D2) // @ is on 2 key
                {
                    _padClient.OnKey((char)0x00);
                    e.Handled = true;
                    return;
                }
            }

            // For printable characters, we'll handle them in TextInput event
            // This is intentionally left to fall through to TextInput
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling key down");
        }
    }

    /// <summary>
    /// Handle TextInput event for regular character input.
    /// This is called after KeyDown for printable characters.
    /// </summary>
    public void HandleTextInput(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Send each character to PAD client
            foreach (char c in text)
            {
                _padClient.OnKey(c);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling text input");
        }
    }

    [RelayCommand]
    private void ClearConsole()
    {
        _consoleBuffer.Clear();
        ConsoleText = "";
        // The prompt was on the lines just wiped. When the PAD is waiting for a command, write it
        // again, or the person sees a blank window and cannot tell where to type.
        _padClient.RedrawPrompt();
        Logger.LogInformation("Console cleared");
    }

    [RelayCommand]
    private void Disconnect()
    {
        _padClient.Disconnect();
        Logger.LogInformation("PAD client #{WindowNumber} disconnected", _windowNumber);
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        _padClient.ConnectionStateChanged -= OnConnectionStateChanged;
        _padClient.DataReceived -= OnDataReceived;
        _padClient.CharacterEcho -= OnCharacterEcho;
        _padClient.ParametersChanged -= OnParametersChanged;

        _padClient.Disconnect();
        _padClient.Dispose();

        Logger.LogInformation("PADClientWindowViewModel #{WindowNumber} disposed", _windowNumber);
        base.Dispose();
    }
}
