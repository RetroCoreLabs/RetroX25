//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Lapb;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Common;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop;

/// <summary>
/// The Avalonia application for the RetroX25 desktop app.
/// </summary>
/// <remarks>
/// Builds the dependency-injection container, starts the protocol runtime and opens the main
/// window. When the app shuts down it disposes the runtime and the container.
/// </remarks>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    /// <summary>
    /// The configuration file the app starts on, named on the command line, or null to start on the
    /// built-in defaults.
    /// </summary>
    /// <remarks>
    /// Set by <c>Program.Main</c> before Avalonia builds the application, because Avalonia creates
    /// <see cref="App"/> itself and gives it no constructor arguments.
    /// </remarks>
    public static string? ConfigFilePath { get; set; }

    /// <summary>
    /// Reads a runtime configuration from a JSON file, the same way the text-mode console does.
    /// </summary>
    /// <param name="path">The JSON file.</param>
    /// <returns>The configuration in the file.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is empty or not a configuration.</exception>
    /// <remarks>
    /// It fails loudly on purpose. Starting on the defaults after a bad file would show a window that
    /// looks right and sends calls nowhere, which is how this was found: nothing in the app could
    /// load a file at all, and a call printed COM and left the PC without a packet.
    /// </remarks>
    public static RuntimeConfig LoadConfigFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Configuration file not found: " + path, path);
        }

        string json = File.ReadAllText(path);
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() }
        };

        RuntimeConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<RuntimeConfig>(json, options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Configuration file is not valid: " + path + " (" + ex.Message + ")", ex);
        }

        if (config == null)
        {
            throw new InvalidDataException("Configuration file is empty: " + path);
        }

        return config;
    }

    /// <summary>
    /// Loads the application's XAML resources and styles.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Sets up the desktop app once the Avalonia framework is ready.
    /// </summary>
    /// <remarks>
    /// On a classic desktop lifetime this turns off Avalonia's own data-annotation validation,
    /// builds the services with <see cref="ConfigureServices"/>, initializes the
    /// <see cref="IRuntimeService"/> with the registered <see cref="RuntimeConfig"/>, opens
    /// <see cref="MainWindow"/> with its view-model, and hooks shutdown so the runtime is
    /// disposed.
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            // Set up dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Initialize RuntimeService with registered config
            var runtimeService = _serviceProvider.GetRequiredService<IRuntimeService>();
            var config = _serviceProvider.GetRequiredService<RuntimeConfig>();
            runtimeService.Initialize(config);

            // The built-in switch (if the configuration file asks for one) starts AFTER the runtime:
            // the switch dials the runtime's own XOT listener, and a dialling port that finds nobody
            // there waits before it tries again. A failure is shown in the main window, not hidden.
            _serviceProvider.GetRequiredService<EmbeddedSwitchService>().StartIfEnabled();

            // Create main window with DI
            var mainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>()
            };

            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Registers every service and view-model the desktop app runs on.
    /// </summary>
    /// <param name="services">The collection to populate.</param>
    /// <remarks>
    /// Public and static so the headless UI tests build the SAME graph the app does. A test that
    /// hand-rolled its own registrations would drift, and would then pass while the real startup
    /// path was broken - which is the failure it exists to catch.
    /// </remarks>
    /// <param name="config">
    /// The configuration to run on. When null, the file named in <see cref="ConfigFilePath"/> is
    /// read, or the built-in defaults are used if no file was named.
    /// </param>
    /// <param name="switchOptions">
    /// Whether the app runs a switch inside itself. When null, the "EmbeddedSwitch" section of the file named in
    /// <see cref="ConfigFilePath"/> is read; no file, or no section, means no built-in switch.
    /// </param>
    public static void ConfigureServices(IServiceCollection services, RuntimeConfig? config = null,
        EmbeddedSwitchOptions? switchOptions = null)
    {
        // The built-in switch setting comes from the same configuration file as the rest.
        switchOptions ??= ConfigFilePath != null ? EmbeddedSwitchOptions.LoadFrom(ConfigFilePath) : EmbeddedSwitchOptions.Off;
        services.AddSingleton(switchOptions);
        services.AddSingleton<EmbeddedSwitchService>();

        // Logging
        var historyProvider = new HistoryLoggingProvider(1000);
        services.AddLogging(builder =>
        {
            builder.AddProvider(historyProvider);
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });
        services.AddSingleton(historyProvider);

        // Services
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IRuntimeService, RuntimeService>();

        // Remembers which packets the user cleared from view; shared by the main window and every Packet window.
        services.AddSingleton<PacketViewState>();

        // Configuration (shared across all ViewModels that need it)
        config ??= ConfigFilePath != null ? LoadConfigFile(ConfigFilePath) : CreateDefaultConfig();
        services.AddSingleton(config);

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<StatusWindowViewModel>();
        services.AddTransient<PacketWindowViewModel>();
        services.AddTransient<LogWindowViewModel>();
        services.AddTransient<ConfigurationWindowViewModel>();
        services.AddTransient<TADServerWindowViewModel>();
        services.AddTransient<ChatServerWindowViewModel>();
        // The one routing screen: the Routing window and the Routing tab of the Configuration window.
        services.AddTransient<RoutingViewModel>();
        services.AddTransient<SimulateReceiveWindowViewModel>();
        services.AddTransient<LinkStatusWindowViewModel>();
        services.AddTransient<InvoiceWindowViewModel>();

        // Configuration Routing Tab ViewModels
        services.AddTransient<SerialInterfacesTabViewModel>();
        services.AddTransient<XotInterfacesTabViewModel>();
        services.AddTransient<TcpLapbInterfacesTabViewModel>();
        services.AddTransient<KnownHostsTabViewModel>();

        // Note: PADClientWindowViewModel requires a windowNumber parameter, created manually in MainWindowViewModel
    }

    /// <summary>
    /// Builds the default runtime configuration the app starts on.
    /// </summary>
    /// <returns>The configuration.</returns>
    public static RuntimeConfig CreateDefaultConfig()
    {
        // Use RuntimeConfig.CreateDefault() which includes proper routing setup
        // User can modify this through the Configuration window (Ctrl+,)
        var config = RuntimeConfig.CreateDefault();

        // Override with UI-specific settings
        config.HistoryCapacity = 1000;

        // Set LAPB parameters (DTE mode, ITU-T X.25 compliant)
        config.Lapb.IsDCE = false; // DTE mode for peer-to-peer
        config.Lapb.T1Timeout = TimeSpan.FromMilliseconds(3000);
        config.Lapb.T2Timeout = TimeSpan.FromMilliseconds(1500);
        config.Lapb.T3Timeout = TimeSpan.FromMilliseconds(0); // Disabled
        config.Lapb.MaxRetries = 10;
        config.Lapb.WindowSize = 7;

        return config;
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Clean up runtime
        _serviceProvider?.GetService<IRuntimeService>()?.Dispose();
        _serviceProvider?.GetService<EmbeddedSwitchService>()?.Dispose();
        _serviceProvider?.Dispose();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove = new List<DataAnnotationsValidationPlugin>();
        for (int i = 0; i < BindingPlugins.DataValidators.Count; i++)
        {
            if (BindingPlugins.DataValidators[i] is DataAnnotationsValidationPlugin plugin)
            {
                dataValidationPluginsToRemove.Add(plugin);
            }
        }

        // remove each entry found
        for (int i = 0; i < dataValidationPluginsToRemove.Count; i++)
        {
            BindingPlugins.DataValidators.Remove(dataValidationPluginsToRemove[i]);
        }
    }
}
