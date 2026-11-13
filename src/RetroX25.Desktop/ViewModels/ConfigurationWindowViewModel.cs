//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Configuration Window (Ctrl+,) - manages ProtocolRuntime settings.
/// </summary>
public partial class ConfigurationWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;
    private RuntimeConfig _config;
    private IStorageProvider? _storageProvider;

    // General Settings
    [ObservableProperty]
    private int _historyCapacity = 1000;

    // LAPB Settings
    [ObservableProperty]
    private int _lapbT1Timeout = 3000; // milliseconds

    [ObservableProperty]
    private int _lapbT2Timeout = 200;

    [ObservableProperty]
    private int _lapbT3Timeout = 0;

    [ObservableProperty]
    private int _lapbMaxRetries = 5;

    [ObservableProperty]
    private int _lapbWindowSize = 7;

    [ObservableProperty]
    private bool _lapbIsDCE = false;

    [ObservableProperty]
    private int _lapbModulo = 8;

    [ObservableProperty]
    private int _lapbMaxInfoFieldSize = 128;

    // X.25 Settings
    [ObservableProperty]
    private string _x25LocalMachineId = "200";

    [ObservableProperty]
    private string _x25LocalDteAddress = "";

    [ObservableProperty]
    private int _x25MaxPacketSize = 128;

    [ObservableProperty]
    private int _x25DefaultPacketWindowSize = 2;

    [ObservableProperty]
    private int _x25T20Timeout = 180000; // milliseconds (3 minutes)

    [ObservableProperty]
    private int _x25T21Timeout = 200000; // milliseconds (3.33 minutes)

    [ObservableProperty]
    private int _x25T22Timeout = 180000;

    [ObservableProperty]
    private int _x25T23Timeout = 60000;

    [ObservableProperty]
    private bool _x25ExtendedPacketSequencing = false;

    // Telnet PAD: a TCP port where an ordinary terminal program (telnet, PuTTY) gets an X.28 PAD.
    /// <summary>Gets or sets whether the telnet PAD listens for connections.</summary>
    [ObservableProperty]
    private bool _telnetPadEnabled = false;

    /// <summary>Gets or sets the TCP port of the telnet PAD (1 to 65535).</summary>
    [ObservableProperty]
    private int _telnetPadPort = TelnetPadConfig.DefaultPort;

    /// <summary>Gets or sets the IP address the telnet PAD listens on (127.0.0.1 = this machine only).</summary>
    [ObservableProperty]
    private string _telnetPadBindAddress = TelnetPadConfig.DefaultBindAddress;

    /// <summary>Gets or sets how many telnet sessions may be open at once.</summary>
    [ObservableProperty]
    private int _telnetPadMaxSessions = TelnetPadConfig.DefaultMaxSessions;

    /// <summary>Gets or sets what the running telnet PAD is doing now, in one line (listening, off, or why it failed).</summary>
    [ObservableProperty]
    private string _telnetPadStatusText = "Telnet PAD: off";

    [ObservableProperty]
    private string _statusText = "Ready";

    // Routing Tab ViewModels
    /// <summary>
    /// Gets the view-model behind the Serial Interfaces tab.
    /// </summary>
    public SerialInterfacesTabViewModel SerialInterfacesTab { get; }

    /// <summary>
    /// Gets the view-model behind the XOT Interfaces tab.
    /// </summary>
    public XotInterfacesTabViewModel XotInterfacesTab { get; }

    /// <summary>
    /// Gets the view-model behind the TCP LAPB tab.
    /// </summary>
    public TcpLapbInterfacesTabViewModel TcpLapbInterfacesTab { get; }

    /// <summary>
    /// Gets the view-model behind the Known Hosts tab.
    /// </summary>
    public KnownHostsTabViewModel KnownHostsTab { get; }

    /// <summary>
    /// Gets the view-model behind the Routing tab.
    /// </summary>
    public RoutingTabViewModel RoutingTab { get; }

    /// <summary>
    /// Creates ConfigurationWindowViewModel with dependency injection.
    /// </summary>
    public ConfigurationWindowViewModel(
        ILogger<ConfigurationWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService,
        SerialInterfacesTabViewModel serialInterfacesTab,
        XotInterfacesTabViewModel xotInterfacesTab,
        TcpLapbInterfacesTabViewModel tcpLapbInterfacesTab,
        KnownHostsTabViewModel knownHostsTab,
        RoutingTabViewModel routingTab,
        RuntimeConfig config) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        // Assign tab ViewModels
        SerialInterfacesTab = serialInterfacesTab ?? throw new ArgumentNullException(nameof(serialInterfacesTab));
        XotInterfacesTab = xotInterfacesTab ?? throw new ArgumentNullException(nameof(xotInterfacesTab));
        TcpLapbInterfacesTab = tcpLapbInterfacesTab ?? throw new ArgumentNullException(nameof(tcpLapbInterfacesTab));
        KnownHostsTab = knownHostsTab ?? throw new ArgumentNullException(nameof(knownHostsTab));
        RoutingTab = routingTab ?? throw new ArgumentNullException(nameof(routingTab));

        // Load current configuration
        // The SAME configuration object the tabs edit and the engine was started from (the one in the
        // service container). It used to be a fresh default configuration, so Apply restarted the engine
        // on defaults and lost the interfaces and routes loaded from the file.
        _config = config ?? throw new ArgumentNullException(nameof(config));
        LoadConfigurationIntoProperties();

        Logger.LogInformation("ConfigurationWindowViewModel initialized");
    }

    /// <summary>
    /// Set the storage provider for file operations (called from Window).
    /// </summary>
    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
    }

    /// <summary>
    /// Load configuration values into observable properties.
    /// </summary>
    private void LoadConfigurationIntoProperties()
    {
        // General
        HistoryCapacity = _config.HistoryCapacity;

        // LAPB
        LapbT1Timeout = (int)_config.Lapb.T1Timeout.TotalMilliseconds;
        LapbT2Timeout = (int)_config.Lapb.T2Timeout.TotalMilliseconds;
        LapbT3Timeout = (int)_config.Lapb.T3Timeout.TotalMilliseconds;
        LapbMaxRetries = _config.Lapb.MaxRetries;
        LapbWindowSize = _config.Lapb.WindowSize;
        LapbIsDCE = _config.Lapb.IsDCE;
        LapbModulo = _config.Lapb.Modulo;
        LapbMaxInfoFieldSize = _config.Lapb.MaxInfoFieldSize;

        // X.25
        X25LocalMachineId = _config.X25.LocalMachineId;
        X25LocalDteAddress = _config.X25.LocalDteAddress;
        X25MaxPacketSize = _config.X25.MaxPacketSize;
        X25DefaultPacketWindowSize = _config.X25.DefaultPacketWindowSize;
        X25T20Timeout = (int)_config.X25.T20Timeout.TotalMilliseconds;
        X25T21Timeout = (int)_config.X25.T21Timeout.TotalMilliseconds;
        X25T22Timeout = (int)_config.X25.T22Timeout.TotalMilliseconds;
        X25T23Timeout = (int)_config.X25.T23Timeout.TotalMilliseconds;
        X25ExtendedPacketSequencing = _config.X25.ExtendedPacketSequencing;

        // Telnet PAD (a file written before this section existed has the defaults: off).
        TelnetPadConfig telnet = _config.TelnetPad ?? new TelnetPadConfig();
        TelnetPadEnabled = telnet.Enabled;
        TelnetPadPort = telnet.Port;
        TelnetPadBindAddress = telnet.BindAddress;
        TelnetPadMaxSessions = telnet.MaxSessions;
        TelnetPadStatusText = _runtimeService.IsRunning ? _runtimeService.Runtime.TelnetPadStatus.Text : "Telnet PAD: off";

        StatusText = "Configuration loaded";
    }

    /// <summary>
    /// Save properties back into configuration object.
    /// </summary>
    private void SavePropertiesIntoConfiguration()
    {
        // General
        _config.HistoryCapacity = HistoryCapacity;

        // LAPB
        _config.Lapb.T1Timeout = TimeSpan.FromMilliseconds(LapbT1Timeout);
        _config.Lapb.T2Timeout = TimeSpan.FromMilliseconds(LapbT2Timeout);
        _config.Lapb.T3Timeout = TimeSpan.FromMilliseconds(LapbT3Timeout);
        _config.Lapb.MaxRetries = LapbMaxRetries;
        _config.Lapb.WindowSize = (byte)LapbWindowSize;
        _config.Lapb.IsDCE = LapbIsDCE;
        _config.Lapb.Modulo = (byte)LapbModulo;
        _config.Lapb.MaxInfoFieldSize = (ushort)LapbMaxInfoFieldSize;

        // X.25
        _config.X25.LocalMachineId = X25LocalMachineId;
        _config.X25.LocalDteAddress = X25LocalDteAddress;
        _config.X25.MaxPacketSize = (ushort)X25MaxPacketSize;
        _config.X25.DefaultPacketWindowSize = (byte)X25DefaultPacketWindowSize;
        _config.X25.T20Timeout = TimeSpan.FromMilliseconds(X25T20Timeout);
        _config.X25.T21Timeout = TimeSpan.FromMilliseconds(X25T21Timeout);
        _config.X25.T22Timeout = TimeSpan.FromMilliseconds(X25T22Timeout);
        _config.X25.T23Timeout = TimeSpan.FromMilliseconds(X25T23Timeout);
        _config.X25.ExtendedPacketSequencing = X25ExtendedPacketSequencing;

        // Telnet PAD
        _config.TelnetPad ??= new TelnetPadConfig();
        _config.TelnetPad.Enabled = TelnetPadEnabled;
        _config.TelnetPad.Port = TelnetPadPort;
        _config.TelnetPad.BindAddress = TelnetPadBindAddress;
        _config.TelnetPad.MaxSessions = TelnetPadMaxSessions;
    }

    [RelayCommand]
    private void ApplySettings()
    {
        try
        {
            SavePropertiesIntoConfiguration();

            // Validate configuration
            if (!ValidateConfiguration())
            {
                return;
            }

            // Stop the running engine and start a new one on these settings, so they take effect now.
            _runtimeService.Restart(_config);
            StatusText = "Settings applied: the X.25 engine was restarted";
            Logger.LogInformation("Configuration applied, runtime restarted");

            // The telnet PAD is started by the engine. If it could not listen (the port is in use, the
            // address is not on this machine) say so here, in words, and not only in the main window.
            TelnetPadStatus telnetStatus = _runtimeService.Runtime.TelnetPadStatus;
            TelnetPadStatusText = telnetStatus.Text;
            string telnetNote = string.Empty;
            if (telnetStatus.State == TelnetPadState.Failed)
            {
                StatusText = "Settings applied, but the " + telnetStatus.Text;
                telnetNote = "\n\nThe telnet PAD did not start: " + telnetStatus.Error + ". Change the port or the address and press Apply again.";
            }

            _dialogService.ShowInfo("Settings Applied",
                "The X.25 engine was restarted with these settings.\n\nPAD, TAD and other windows that were open were closed because they belonged to the old engine. Open them again from the Windows menu." + telnetNote);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Logger.LogError("Error applying configuration: {Error}", ex.Message);
            _dialogService.ShowError("Error", $"Failed to apply settings:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _config = RuntimeConfig.CreateDefault();
        LoadConfigurationIntoProperties();
        StatusText = "Reset to default values";
        Logger.LogInformation("Configuration reset to defaults");
    }

    [RelayCommand]
    private async Task SaveToFile()
    {
        try
        {
            if (_storageProvider == null)
            {
                _dialogService.ShowError("Error", "Storage provider not initialized. Please try again.");
                Logger.LogError("SaveToFile called but storage provider is null");
                return;
            }

            // Save current properties to config object
            SavePropertiesIntoConfiguration();

            // Note: Routing tab data is already in _config.Routing collections
            // (SerialInterfaces, XotInterfaces, KnownHosts, RoutingTable)
            // The DisplayItem collections in the tab ViewModels are just for display

            // Open save file dialog
            var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Configuration",
                SuggestedFileName = "x25config.json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON Configuration Files")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });

            if (file == null)
            {
                StatusText = "Save cancelled";
                return;
            }

            // Serialize to JSON with indentation
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            string json = JsonSerializer.Serialize(_config, options);

            // Write to file
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);

            StatusText = $"Configuration saved to {file.Name}";
            Logger.LogInformation("Configuration saved to {FileName}", file.Name);
            _dialogService.ShowInfo("Save Successful", $"Configuration saved to:\n{file.Name}");
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving file: {ex.Message}";
            Logger.LogError(ex, "Error saving configuration file");
            _dialogService.ShowError("Save Error", $"Failed to save configuration:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadFromFile()
    {
        try
        {
            if (_storageProvider == null)
            {
                _dialogService.ShowError("Error", "Storage provider not initialized. Please try again.");
                Logger.LogError("LoadFromFile called but storage provider is null");
                return;
            }

            // Open file dialog
            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Configuration",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON Configuration Files")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });

            if (files.Count == 0)
            {
                StatusText = "Load cancelled";
                return;
            }

            var file = files[0];

            // Read from file
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            string json = await reader.ReadToEndAsync();

            // Deserialize from JSON
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var loadedConfig = JsonSerializer.Deserialize<RuntimeConfig>(json, options);
            if (loadedConfig == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration - result was null");
            }

            // Copy loaded config into existing _config to preserve references in tab ViewModels
            CopyConfiguration(loadedConfig, _config);

            // Load into properties
            LoadConfigurationIntoProperties();

            // Refresh routing tab displays by calling their Refresh commands
            // This will reload their display items from the updated _config
            SerialInterfacesTab.RefreshCommand.Execute(null);
            XotInterfacesTab.RefreshCommand.Execute(null);
            TcpLapbInterfacesTab.RefreshCommand.Execute(null);
            KnownHostsTab.RefreshCommand.Execute(null);
            RoutingTab.RefreshCommand.Execute(null);

            StatusText = $"Configuration loaded from {file.Name}";
            Logger.LogInformation("Configuration loaded from {FileName}", file.Name);
            _dialogService.ShowInfo("Load Successful", $"Configuration loaded from:\n{file.Name}\n\nNote: A runtime restart is required for changes to take effect.");
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading file: {ex.Message}";
            Logger.LogError(ex, "Error loading configuration file");
            _dialogService.ShowError("Load Error", $"Failed to load configuration:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Copy configuration from source to destination (preserves object references).
    /// </summary>
    private void CopyConfiguration(RuntimeConfig source, RuntimeConfig destination)
    {
        // Copy general settings
        destination.TestMode = source.TestMode;
        destination.HistoryCapacity = source.HistoryCapacity;

        // Copy LAPB settings
        destination.Lapb.T1Timeout = source.Lapb.T1Timeout;
        destination.Lapb.T2Timeout = source.Lapb.T2Timeout;
        destination.Lapb.T3Timeout = source.Lapb.T3Timeout;
        destination.Lapb.MaxRetries = source.Lapb.MaxRetries;
        destination.Lapb.WindowSize = source.Lapb.WindowSize;
        destination.Lapb.IsDCE = source.Lapb.IsDCE;
        destination.Lapb.Modulo = source.Lapb.Modulo;
        destination.Lapb.MaxInfoFieldSize = source.Lapb.MaxInfoFieldSize;

        // Copy X.25 settings
        destination.X25.DefaultPacketWindowSize = source.X25.DefaultPacketWindowSize;
        destination.X25.MaxPacketSize = source.X25.MaxPacketSize;
        destination.X25.T20Timeout = source.X25.T20Timeout;
        destination.X25.T21Timeout = source.X25.T21Timeout;
        destination.X25.T22Timeout = source.X25.T22Timeout;
        destination.X25.T23Timeout = source.X25.T23Timeout;
        destination.X25.LocalDteAddress = source.X25.LocalDteAddress;
        destination.X25.LocalMachineId = source.X25.LocalMachineId;
        destination.X25.ExtendedPacketSequencing = source.X25.ExtendedPacketSequencing;

        // Copy telnet PAD settings
        destination.TelnetPad ??= new TelnetPadConfig();
        TelnetPadConfig sourceTelnet = source.TelnetPad ?? new TelnetPadConfig();
        destination.TelnetPad.Enabled = sourceTelnet.Enabled;
        destination.TelnetPad.Port = sourceTelnet.Port;
        destination.TelnetPad.BindAddress = sourceTelnet.BindAddress;
        destination.TelnetPad.MaxSessions = sourceTelnet.MaxSessions;

        // Copy routing settings
        destination.Routing.Enabled = source.Routing.Enabled;
        destination.Routing.DefaultGateway = source.Routing.DefaultGateway;
        destination.Routing.EnableFailover = source.Routing.EnableFailover;
        destination.Routing.FailoverTimeout = source.Routing.FailoverTimeout;

        // Replace routing collections
        destination.Routing.SerialInterfaces.Clear();
        for (int i = 0; i < source.Routing.SerialInterfaces.Count; i++)
        {
            destination.Routing.SerialInterfaces.Add(source.Routing.SerialInterfaces[i]);
        }

        destination.Routing.XotInterfaces.Clear();
        for (int i = 0; i < source.Routing.XotInterfaces.Count; i++)
        {
            destination.Routing.XotInterfaces.Add(source.Routing.XotInterfaces[i]);
        }

        // Interfaces that carry LAPB over TCP: Save and Load used to drop these.
        destination.Routing.TcpLapbInterfaces.Clear();
        for (int i = 0; i < source.Routing.TcpLapbInterfaces.Count; i++)
        {
            destination.Routing.TcpLapbInterfaces.Add(source.Routing.TcpLapbInterfaces[i]);
        }

        destination.Routing.KnownHosts.Clear();
        for (int i = 0; i < source.Routing.KnownHosts.Count; i++)
        {
            destination.Routing.KnownHosts.Add(source.Routing.KnownHosts[i]);
        }

        destination.Routing.RoutingTable.Clear();
        for (int i = 0; i < source.Routing.RoutingTable.Count; i++)
        {
            destination.Routing.RoutingTable.Add(source.Routing.RoutingTable[i]);
        }

        destination.Routing.PvcBindings.Clear();
        for (int i = 0; i < source.Routing.PvcBindings.Count; i++)
        {
            destination.Routing.PvcBindings.Add(source.Routing.PvcBindings[i]);
        }

        // Copy Timer settings
        destination.Timers.CentralTimerManagement = source.Timers.CentralTimerManagement;
        destination.Timers.MarshalCallbacks = source.Timers.MarshalCallbacks;
        destination.Timers.DefaultPrecision = source.Timers.DefaultPrecision;

        // Copy Logging settings
        destination.Logging.EnableLayerTags = source.Logging.EnableLayerTags;
        destination.Logging.EnableIdTags = source.Logging.EnableIdTags;
        destination.Logging.EnableStatisticsLogging = source.Logging.EnableStatisticsLogging;
        destination.Logging.StatisticsUpdateInterval = source.Logging.StatisticsUpdateInterval;
    }

    /// <summary>
    /// Validate configuration settings.
    /// </summary>
    private bool ValidateConfiguration()
    {
        // Validate LAPB timers (ITU-T X.25 specifications)
        if (LapbT2Timeout > LapbT1Timeout / 2)
        {
            _dialogService.ShowWarning("Invalid Configuration",
                $"LAPB T2 timer ({LapbT2Timeout}ms) must be ≤ T1/2 ({LapbT1Timeout / 2}ms) per ITU-T X.25 specification.");
            return false;
        }

        if (LapbT3Timeout > 0 && LapbT3Timeout <= LapbT1Timeout)
        {
            _dialogService.ShowWarning("Invalid Configuration",
                $"LAPB T3 timer ({LapbT3Timeout}ms) must be > T1 ({LapbT1Timeout}ms) per ITU-T X.25 specification.");
            return false;
        }

        // The LAPB layer of the engine counts frames modulo 8 only: window 1..7, no SABME. A loaded file
        // that asks for modulo 128 is refused out loud rather than run as modulo 8 without a word.
        // What 128 would need: src/docs/modulo-128.md.
        if (LapbModulo != 8)
        {
            _dialogService.ShowWarning("Invalid Configuration",
                "LAPB modulo 128 is not implemented. The engine runs LAPB with modulo 8 only (see docs/modulo-128.md).");
            return false;
        }

        // Validate window sizes
        if (LapbWindowSize < 1 || LapbWindowSize > 7)
        {
            _dialogService.ShowWarning("Invalid Configuration",
                "LAPB window size must be between 1 and 7 (modulo 8).");
            return false;
        }

        // The X.25 packet layer is a separate setting from LAPB and also counts modulo 8 only: window 1..7,
        // and a configuration that asks for extended (modulo 128) packet sequencing is refused out loud.
        if (X25ExtendedPacketSequencing)
        {
            _dialogService.ShowWarning("Invalid Configuration",
                "X.25 packet modulo 128 (extended packet sequencing) is not implemented. The X.25 layer runs modulo 8 only (see docs/modulo-128.md).");
            return false;
        }

        if (X25DefaultPacketWindowSize < 1 || X25DefaultPacketWindowSize > 7)
        {
            _dialogService.ShowWarning("Invalid Configuration",
                "X.25 packet window size must be between 1 and 7 (modulo 8).");
            return false;
        }

        // Telnet PAD: the same rules the engine applies when it starts the listener. Checked even when
        // the PAD is off, so a bad value is not saved to a file and found only when it is switched on.
        TelnetPadConfig telnetCheck = new TelnetPadConfig
        {
            Enabled = TelnetPadEnabled,
            Port = TelnetPadPort,
            BindAddress = TelnetPadBindAddress,
            MaxSessions = TelnetPadMaxSessions
        };
        string? telnetProblem = telnetCheck.Validate();
        if (telnetProblem != null)
        {
            _dialogService.ShowWarning("Invalid Configuration", telnetProblem);
            StatusText = telnetProblem;
            return false;
        }

        // Validate history capacity
        if (HistoryCapacity < 10 || HistoryCapacity > 100000)
        {
            _dialogService.ShowWarning("Invalid Configuration",
                "History capacity must be between 10 and 100000.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        Logger.LogInformation("ConfigurationWindowViewModel disposed");
        base.Dispose();
    }
}
