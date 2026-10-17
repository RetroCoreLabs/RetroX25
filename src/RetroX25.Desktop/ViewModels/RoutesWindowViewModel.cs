//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for Routes Window - displays X.25 routing table.
/// </summary>
public partial class RoutesWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<RouteDisplayItem> _routes = new();

    [ObservableProperty]
    private RouteDisplayItem? _selectedRoute;

    [ObservableProperty]
    private string _routeDetails = "";

    [ObservableProperty]
    private string _routeSummary = "Loading routes...";

    [ObservableProperty]
    private string _testAddress = "31064001234";

    [ObservableProperty]
    private string _testResults = "";

    /// <summary>
    /// Creates the view-model and loads the routing table from the running protocol
    /// runtime's configuration.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model.
    /// </param>
    /// <param name="runtimeService">
    /// Service that gives access to the protocol runtime whose routes are shown.
    /// </param>
    /// <param name="dialogService">
    /// Service used to show an error when the routes cannot be loaded.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/>, <paramref name="runtimeService"/> or
    /// <paramref name="dialogService"/> is null.
    /// </exception>
    public RoutesWindowViewModel(
        ILogger<RoutesWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        LoadRoutes();
        Logger.LogInformation("RoutesWindowViewModel initialized");
    }

    private void LoadRoutes()
    {
        try
        {
            Routes.Clear();

            if (_runtimeService.Runtime.Config.Routing?.Enabled != true)
            {
                RouteSummary = "X.25 Routing is not enabled in configuration";
                return;
            }

            var routes = _runtimeService.Runtime.Config.Routing.RoutingTable;

            if (routes.Count == 0)
            {
                RouteSummary = "No X.25 routes configured";
                return;
            }

            // Convert to display items
            for (int i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                Routes.Add(new RouteDisplayItem
                {
                    Name = route.Name,
                    Pattern = route.X121AddressPattern,
                    Type = route.Type.ToString(),
                    Destination = route.Destination,
                    Priority = route.Priority,
                    Enabled = route.Enabled,
                    Description = route.Description ?? ""
                });
            }

            // Update summary
            int enabledCount = 0;
            int disabledCount = 0;
            for (int i = 0; i < Routes.Count; i++)
            {
                if (Routes[i].Enabled)
                    enabledCount++;
                else
                    disabledCount++;
            }

            RouteSummary = $"X.25 Routes: {enabledCount} enabled, {disabledCount} disabled, {Routes.Count} total";
            Logger.LogDebug("Loaded {Count} X.25 routes", Routes.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading X.25 routes");
            RouteSummary = "Error loading routes";
            _dialogService.ShowError("Error", $"Failed to load routes: {ex.Message}");
        }
    }

    partial void OnSelectedRouteChanged(RouteDisplayItem? value)
    {
        if (value != null)
        {
            var details = new StringBuilder();
            details.AppendLine($"Route Name: {value.Name}");
            details.AppendLine($"Status: {(value.Enabled ? "Enabled" : "Disabled")}");
            details.AppendLine($"Priority: {value.Priority}");
            details.AppendLine();
            details.AppendLine($"X.121 Pattern: {value.Pattern}");
            details.AppendLine($"Transport Type: {value.Type}");
            details.AppendLine($"Destination: {value.Destination}");
            details.AppendLine();

            if (!string.IsNullOrEmpty(value.Description))
            {
                details.AppendLine($"Description:");
                details.AppendLine(value.Description);
                details.AppendLine();
            }

            details.AppendLine("Pattern Examples:");
            details.AppendLine("^31064.*        - DNIC 31064 prefix");
            details.AppendLine("^2340123.*      - Network prefix");
            details.AppendLine("^31064(001|002) - Multiple service codes");
            details.AppendLine(".*              - Wildcard (matches all)");

            RouteDetails = details.ToString();
        }
        else
        {
            RouteDetails = "No route selected.";
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadRoutes();
        TestResults = "";
        Logger.LogInformation("Routes refreshed");
    }

    [RelayCommand]
    private void TestRoute()
    {
        if (string.IsNullOrWhiteSpace(TestAddress))
        {
            TestResults = "Please enter an X.121 address to test";
            return;
        }

        try
        {
            var results = new StringBuilder();
            results.AppendLine($"Testing X.121 address: {TestAddress}");
            results.AppendLine();

            // Find matching routes
            int matchCount = 0;
            RouteDisplayItem? bestMatch = null;
            int highestPriority = int.MinValue;

            for (int i = 0; i < Routes.Count; i++)
            {
                var route = Routes[i];
                if (!route.Enabled) continue;

                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(route.Pattern);
                    if (regex.IsMatch(TestAddress))
                    {
                        matchCount++;
                        if (route.Priority > highestPriority)
                        {
                            highestPriority = route.Priority;
                            bestMatch = route;
                        }
                    }
                }
                catch
                {
                    // Invalid regex
                }
            }

            if (matchCount == 0)
            {
                results.AppendLine("No matching routes found");
            }
            else
            {
                results.AppendLine($"Found {matchCount} matching route(s)");
                results.AppendLine();
                if (bestMatch != null)
                {
                    results.AppendLine("→ SELECTED ROUTE:");
                    results.AppendLine($"  Name: {bestMatch.Name}");
                    results.AppendLine($"  Priority: {bestMatch.Priority}");
                    results.AppendLine($"  Pattern: {bestMatch.Pattern}");
                    results.AppendLine($"  Destination: {bestMatch.Type}/{bestMatch.Destination}");
                }
            }

            TestResults = results.ToString();
            Logger.LogDebug("Route test completed for {Address}: {Count} matches", TestAddress, matchCount);
        }
        catch (Exception ex)
        {
            TestResults = $"Error testing route: {ex.Message}";
            Logger.LogError(ex, "Error testing route");
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Logger.LogInformation("RoutesWindowViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// Display item for route information.
/// </summary>
public class RouteDisplayItem
{
    /// <summary>
    /// Gets or sets the route name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the X.121 address pattern of the route; the route test treats it as a
    /// regular expression.
    /// </summary>
    public string Pattern { get; set; } = "";

    /// <summary>
    /// Gets or sets the route type, as the text of its route type value.
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Gets or sets the destination interface name (for an interface route) or the gateway
    /// machine ID (for a gateway route).
    /// </summary>
    public string Destination { get; set; } = "";

    /// <summary>
    /// Gets or sets the route priority; the route test picks the matching route with the
    /// highest number.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets whether the route is enabled; disabled routes are skipped by the route
    /// test.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the free-text description of the route; empty when it has none.
    /// </summary>
    public string Description { get; set; } = "";
}
