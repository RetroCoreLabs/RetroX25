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
using RetroIO.Protocols.X25.Transport;
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
    /// Gets or sets the direction the route test is made for: <c>Outbound</c> (a call placed from
    /// here), <c>Inbound</c> (a call arriving) or <c>Bidirectional</c> (matches only routes that
    /// work both ways).
    /// </summary>
    [ObservableProperty]
    private string _testDirection = "Outbound";

    /// <summary>
    /// Gets the directions the route test can be made for.
    /// </summary>
    public System.Collections.Generic.List<string> AvailableDirections { get; } =
        new() { "Outbound", "Inbound", "Bidirectional" };

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
                    Direction = RouteDirection.Bidirectional.ToString(),
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
            details.AppendLine($"Direction: {value.Direction}");
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

    /// <summary>
    /// Asks the engine's own router which routes match the test address for the chosen direction,
    /// and shows them, best first.
    /// </summary>
    /// <remarks>
    /// The match is the engine's: the address is matched against each enabled route's pattern with
    /// upper and lower case treated alike, only routes that work in the chosen direction count, and
    /// the route with the highest priority is the selected one. Nothing is placed on the wire.
    /// </remarks>
    [RelayCommand]
    private void TestRoute()
    {
        if (string.IsNullOrWhiteSpace(TestAddress))
        {
            TestResults = "Please enter an X.121 address to test";
            return;
        }

        if (!_runtimeService.IsRunning)
        {
            TestResults = "The protocol runtime is not running";
            return;
        }

        if (!Enum.TryParse(TestDirection, true, out RouteDirection direction))
        {
            TestResults = $"Unknown direction: {TestDirection}";
            return;
        }

        string address = TestAddress.Trim();
        System.Collections.Generic.List<X25Route> matches = _runtimeService.Runtime.Router.TestRoute(address, direction);

        var results = new StringBuilder();
        results.AppendLine($"Testing X.121 address: {address}");
        results.AppendLine($"Direction: {direction}");
        results.AppendLine();

        if (matches.Count == 0)
        {
            results.AppendLine("No matching routes found");
        }
        else
        {
            results.AppendLine($"Found {matches.Count} matching route(s):");
            results.AppendLine();

            for (int i = 0; i < matches.Count; i++)
            {
                X25Route route = matches[i];
                results.AppendLine($"{route.Name} (Priority {route.Priority}) {(i == 0 ? "→ SELECTED" : "  (backup)")}");
                results.AppendLine($"  Pattern: {route.X121AddressPattern}");
                results.AppendLine($"  Destination: {route.Type}/{route.Destination}");
                if (i < matches.Count - 1)
                {
                    results.AppendLine();
                }
            }
        }

        TestResults = results.ToString();
        Logger.LogDebug("Route test completed for {Address} ({Direction}): {Count} matches", address, direction, matches.Count);
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
    /// Gets or sets the direction the route works in: <c>Outbound</c>, <c>Inbound</c> or <c>Bidirectional</c>.
    /// The engine makes every route from the routing table <c>Bidirectional</c>, so that is what is
    /// shown.
    /// </summary>
    public string Direction { get; set; } = "";

    /// <summary>
    /// Gets or sets the free-text description of the route; empty when it has none.
    /// </summary>
    public string Description { get; set; } = "";
}
