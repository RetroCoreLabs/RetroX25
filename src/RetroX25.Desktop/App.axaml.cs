using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Link.Lapb;
using RetroIO.Protocols.X25.Common;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

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
    public static void ConfigureServices(IServiceCollection services)
    {
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

        // Configuration (shared across all ViewModels that need it)
        var config = CreateDefaultConfig();
        services.AddSingleton(config);

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<StatusWindowViewModel>();
        services.AddTransient<PacketWindowViewModel>();
        services.AddTransient<LogWindowViewModel>();
        services.AddTransient<ConfigurationWindowViewModel>();
        services.AddTransient<TADServerWindowViewModel>();
        services.AddTransient<XMSGServerWindowViewModel>();
        services.AddTransient<RoutesWindowViewModel>();
        services.AddTransient<SintranTadServerWindowViewModel>();
        services.AddTransient<SimulateReceiveWindowViewModel>();

        // Configuration Routing Tab ViewModels
        services.AddTransient<SerialInterfacesTabViewModel>();
        services.AddTransient<XotInterfacesTabViewModel>();
        services.AddTransient<KnownHostsTabViewModel>();
        services.AddTransient<RoutingTabViewModel>();

        // Note: PADClientWindowViewModel and SintranTadClientWindowViewModel require windowNumber parameter, created manually in MainWindowViewModel
    }

    /// <summary>
    /// Builds the default runtime configuration the app starts on.
    /// </summary>
    /// <returns>The configuration.</returns>
    public static RuntimeConfig CreateDefaultConfig()
    {
        // Use RuntimeConfig.CreateDefault() which includes proper routing setup
        // User can modify this through the Configuration window (F9)
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