//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Runtime.Transport;
using RetroIO.Protocols.X25.Sintran;
using RetroIO.Protocols.X25.Transport;
using Terminal.Gui;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Window for displaying and managing X.25 routes
    /// </summary>
    public class RoutesWindow : Window
    {
        private readonly ProtocolRuntime _runtime;
        private readonly ILogger _logger;
        private ListView _routesListView = null!;
        private TextView _routeDetailsView = null!;
        private Label _routesSummaryLabel = null!;
        private Button _refreshButton = null!;
        private Button _testRouteButton = null!;
        private Button _closeButton = null!;
        private TextField _testAddressField = null!;
        private ComboBox _testDirectionCombo = null!;
        private TextView _testResultsView = null!;

        private List<RouteDisplayItem> _routeItems = new List<RouteDisplayItem>();

        /// <summary>
        /// Creates the modal routes window and loads the routing table from the runtime configuration.
        /// </summary>
        /// <remarks>
        /// The window lists the configured routes sorted by priority, highest first, and shows the
        /// details of the selected route. Under Route Testing the user enters an X.121 address and a
        /// direction; Test Route matches the address against the pattern of every enabled route and
        /// lists the matches, the highest priority marked as selected. When routing is disabled or no
        /// routes are configured, the list says so instead. Close ends the modal run.
        /// </remarks>
        /// <param name="runtime">
        /// The protocol runtime whose routing configuration is shown.
        /// </param>
        /// <param name="logger">
        /// The logger that load and test errors are written to.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="runtime"/> or <paramref name="logger"/> is null.
        /// </exception>
        public RoutesWindow(ProtocolRuntime runtime, ILogger logger) : base("X.25 Routes")
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            X = Pos.Center();
            Y = Pos.Center();
            Width = Dim.Fill() - 6;  // Resizable width with margin
            Height = Dim.Fill() - 6;  // Resizable height with margin
            Modal = true;

            CreateLayout();
            LoadRoutes();
        }

        private void CreateLayout()
        {
            // Summary label at top
            _routesSummaryLabel = new Label("Loading routes...")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2
            };
            Add(_routesSummaryLabel);

            // Routes list view
            _routesListView = new ListView()
            {
                X = 1,
                Y = 3,
                Width = Dim.Percent(60),
                Height = Dim.Fill() - 10
            };
            _routesListView.SelectedItemChanged += OnRouteSelected;
            Add(_routesListView);

            // Route details view
            var detailsLabel = new Label("Route Details:")
            {
                X = Pos.Right(_routesListView) + 1,
                Y = 3
            };
            Add(detailsLabel);

            _routeDetailsView = new TextView()
            {
                X = Pos.Right(_routesListView) + 1,
                Y = 4,
                Width = Dim.Fill() - 1,
                Height = 12,
                ReadOnly = true,
                WordWrap = true
            };
            Add(_routeDetailsView);

            // Test section
            var testLabel = new Label("Route Testing:")
            {
                X = Pos.Right(_routesListView) + 1,
                Y = Pos.Bottom(_routeDetailsView) + 1
            };
            Add(testLabel);

            var addressLabel = new Label("X.121 Address:")
            {
                X = Pos.Right(_routesListView) + 1,
                Y = Pos.Bottom(testLabel) + 1
            };
            Add(addressLabel);

            _testAddressField = new TextField("31064001234")
            {
                X = Pos.Right(addressLabel) + 1,
                Y = Pos.Bottom(testLabel) + 1,
                Width = 15
            };
            Add(_testAddressField);

            var directionLabel = new Label("Direction:")
            {
                X = Pos.Right(_testAddressField) + 2,
                Y = Pos.Bottom(testLabel) + 1
            };
            Add(directionLabel);

            _testDirectionCombo = new ComboBox()
            {
                X = Pos.Right(directionLabel) + 1,
                Y = Pos.Bottom(testLabel) + 1,
                Width = 15,
                Height = 4
            };
            _testDirectionCombo.SetSource(new string[] { "Outbound", "Inbound", "Bidirectional" });
            _testDirectionCombo.SelectedItem = 0;
            Add(_testDirectionCombo);

            _testRouteButton = new Button("Test Route")
            {
                X = Pos.Right(_routesListView) + 1,
                Y = Pos.Bottom(_testAddressField) + 1
            };
            _testRouteButton.Clicked += OnTestRoute;
            Add(_testRouteButton);

            _testResultsView = new TextView()
            {
                X = Pos.Right(_routesListView) + 1,
                Y = Pos.Bottom(_testRouteButton) + 1,
                Width = Dim.Fill() - 1,
                Height = 4,
                ReadOnly = true,
                WordWrap = true
            };
            Add(_testResultsView);

            // Bottom buttons
            _refreshButton = new Button("Refresh")
            {
                X = 1,
                Y = Pos.Bottom(_routesListView) + 1
            };
            _refreshButton.Clicked += OnRefresh;
            Add(_refreshButton);

            _closeButton = new Button("Close")
            {
                X = Pos.Right(_refreshButton) + 2,
                Y = Pos.Bottom(_routesListView) + 1
            };
            _closeButton.Clicked += OnClose;
            Add(_closeButton);
        }

        private void LoadRoutes()
        {
            try
            {
                _routeItems.Clear();

                // Check if routing is enabled
                if (_runtime.Config.Routing?.Enabled != true)
                {
                    _routesSummaryLabel.Text = "X.25 Routing is not enabled in configuration";
                    _routesListView.SetSource(new List<string> { "Routing disabled - no routes available" });
                    return;
                }

                // Load configured routes from runtime configuration
                var routes = _runtime.Config.Routing.RoutingTable;

                if (routes.Count == 0)
                {
                    _routesSummaryLabel.Text = "No X.25 routes configured";
                    _routesListView.SetSource(new List<string> { "No routes configured" });
                    return;
                }

                // Convert to display items
                foreach (var route in routes)
                {
                    var displayItem = new RouteDisplayItem
                    {
                        Name = route.Name,
                        Pattern = route.X121AddressPattern,
                        Type = route.Type.ToString(),
                        Destination = route.Destination,
                        Priority = route.Priority,
                        Direction = "Outbound", // All routes in new system are outbound
                        Enabled = route.Enabled,
                        Description = route.Description ?? "",
                        BackupCount = 0, // New system doesn't have backup destinations
                        HasCudPattern = false // New system doesn't have CUD patterns yet
                    };
                    _routeItems.Add(displayItem);
                }

                // Sort by priority (highest first)
                _routeItems.Sort((a, b) => b.Priority.CompareTo(a.Priority));

                // Update summary
                int enabledCount = 0;
                for (int i = 0; i < _routeItems.Count; i++)
                {
                    if (_routeItems[i].Enabled) enabledCount++;
                }
                var disabledCount = _routeItems.Count - enabledCount;
                _routesSummaryLabel.Text = $"X.25 Routes: {enabledCount} enabled, {disabledCount} disabled, {_routeItems.Count} total";

                // Create display strings for ListView
                var displayStrings = new List<string>(_routeItems.Count);
                for (int i = 0; i < _routeItems.Count; i++)
                {
                    displayStrings.Add(FormatRouteListItem(_routeItems[i]));
                }
                _routesListView.SetSource(displayStrings);

                _logger.LogDebug("Loaded {Count} X.25 routes for display", _routeItems.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading X.25 routes");
                _routesSummaryLabel.Text = "Error loading routes";
                _routesListView.SetSource(new List<string> { $"Error: {ex.Message}" });
            }
        }

        private string FormatRouteListItem(RouteDisplayItem route)
        {
            var status = route.Enabled ? "✓" : "✗";
            var backup = route.BackupCount > 0 ? $"+{route.BackupCount}" : "";
            var cud = route.HasCudPattern ? "CUD" : "";

            return $"{status} {route.Name,-20} {route.Pattern,-15} → {route.Type}/{route.Destination}{backup} ({route.Priority}) {cud}";
        }

        private void OnRouteSelected(ListViewItemEventArgs e)
        {
            if (e.Item < 0 || e.Item >= _routeItems.Count)
            {
                _routeDetailsView.Text = "";
                return;
            }

            var route = _routeItems[e.Item];
            var details = new StringBuilder();

            details.AppendLine($"Route Name: {route.Name}");
            details.AppendLine($"Status: {(route.Enabled ? "Enabled" : "Disabled")}");
            details.AppendLine($"Priority: {route.Priority}");
            details.AppendLine();

            details.AppendLine($"X.121 Pattern: {route.Pattern}");
            details.AppendLine($"Transport Type: {route.Type}");
            details.AppendLine($"Destination: {route.Destination}");
            details.AppendLine($"Direction: {route.Direction}");
            details.AppendLine();

            if (route.BackupCount > 0)
            {
                details.AppendLine($"Backup Destinations: {route.BackupCount} configured");
            }

            if (route.HasCudPattern)
            {
                details.AppendLine("Call User Data pattern matching enabled");
            }

            if (!string.IsNullOrEmpty(route.Description))
            {
                details.AppendLine();
                details.AppendLine($"Description:");
                details.AppendLine(route.Description);
            }

            details.AppendLine();
            details.AppendLine("Pattern Examples:");
            details.AppendLine("^31064.*        - DNIC 31064 prefix");
            details.AppendLine("^2340123.*      - Network prefix");
            details.AppendLine("^31064(001|002) - Multiple service codes");
            details.AppendLine(".*              - Wildcard (matches all)");

            _routeDetailsView.Text = details.ToString();
        }

        private void OnTestRoute()
        {
            try
            {
                var testAddress = _testAddressField.Text?.ToString() ?? "";
                if (string.IsNullOrEmpty(testAddress))
                {
                    _testResultsView.Text = "Please enter an X.121 address to test";
                    return;
                }

                var directionText = _testDirectionCombo.Text?.ToString() ?? "Outbound";

                // Parse direction
                RouteDirection direction;
                switch (directionText)
                {
                    case "Inbound":
                        direction = RouteDirection.Inbound;
                        break;
                    case "Bidirectional":
                        direction = RouteDirection.Bidirectional;
                        break;
                    default:
                        direction = RouteDirection.Outbound;
                        break;
                }

                var results = new StringBuilder();
                results.AppendLine($"Testing X.121 address: {testAddress}");
                results.AppendLine($"Direction: {direction}");
                results.AppendLine();

                // Test route matching manually since we don't have access to X25Router directly
                var matchingRoutes = new List<RouteDisplayItem>();

                for (int i = 0; i < _routeItems.Count; i++)
                {
                    var route = _routeItems[i];
                    if (route.Enabled && TestRouteMatch(route, testAddress, direction))
                    {
                        matchingRoutes.Add(route);
                    }
                }

                if (matchingRoutes.Count == 0)
                {
                    results.AppendLine("No matching routes found");
                }
                else
                {
                    results.AppendLine($"Found {matchingRoutes.Count} matching route(s):");
                    results.AppendLine();

                    // Sort by priority (highest first)
                    matchingRoutes.Sort((a, b) => b.Priority.CompareTo(a.Priority));

                    for (int i = 0; i < matchingRoutes.Count; i++)
                    {
                        var route = matchingRoutes[i];
                        var selectedText = i == 0 ? "→ SELECTED" : "  (backup)";
                        results.AppendLine($"{route.Name} (Priority {route.Priority}) {selectedText}");
                        results.AppendLine($"  Pattern: {route.Pattern}");
                        results.AppendLine($"  Destination: {route.Type}/{route.Destination}");
                        if (i < matchingRoutes.Count - 1) results.AppendLine();
                    }
                }

                _testResultsView.Text = results.ToString();
                _logger.LogDebug("Route test completed for address {Address}: {Count} matches", testAddress, matchingRoutes.Count);
            }
            catch (Exception ex)
            {
                _testResultsView.Text = $"Error testing route: {ex.Message}";
                _logger.LogError(ex, "Error testing route");
            }
        }

        private bool TestRouteMatch(RouteDisplayItem route, string x121Address, RouteDirection direction)
        {
            try
            {
                // Check direction compatibility
                if (!SupportsDirection(route.Direction, direction))
                    return false;

                // Test regex pattern
                var regex = new System.Text.RegularExpressions.Regex(route.Pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return regex.IsMatch(x121Address);
            }
            catch (Exception)
            {
                // Invalid regex pattern
                return false;
            }
        }

        private bool SupportsDirection(string routeDirection, RouteDirection testDirection)
        {
            if (routeDirection == "Bidirectional")
                return true;

            return routeDirection.Equals(testDirection.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private void OnRefresh()
        {
            LoadRoutes();
            _testResultsView.Text = "";
        }

        private void OnClose()
        {
            Application.RequestStop();
        }
    }

    /// <summary>
    /// Display item for route information
    /// </summary>
    public class RouteDisplayItem
    {
        /// <summary>
        /// Gets or sets the route name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the X.121 address pattern of the route.
        /// </summary>
        /// <remarks>
        /// The routes window tests addresses against it as a regular expression, ignoring case.
        /// A pattern that is not a valid regular expression matches nothing.
        /// </remarks>
        public string Pattern { get; set; } = "";

        /// <summary>
        /// Gets or sets the transport type of the route, as text.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Gets or sets the destination the route sends calls to.
        /// </summary>
        public string Destination { get; set; } = "";

        /// <summary>
        /// Gets or sets the route priority; a higher number is listed and chosen first.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Gets or sets the direction the route applies to: <c>Outbound</c>, <c>Inbound</c> or
        /// <c>Bidirectional</c>.
        /// </summary>
        /// <remarks>
        /// The routes window sets this to <c>Outbound</c> for every route. A <c>Bidirectional</c>
        /// route matches a test in any direction.
        /// </remarks>
        public string Direction { get; set; } = "";

        /// <summary>
        /// Gets or sets whether the route is enabled; only enabled routes take part in a route test.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the free-text description of the route, or an empty string.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Gets or sets the number of backup destinations to show for the route.
        /// </summary>
        /// <remarks>
        /// The routes window always sets this to 0, because the routing table has no backup
        /// destinations.
        /// </remarks>
        public int BackupCount { get; set; }

        /// <summary>
        /// Gets or sets whether the route also matches on a Call User Data pattern.
        /// </summary>
        /// <remarks>
        /// The routes window always sets this to false, because the routing table has no Call User
        /// Data patterns.
        /// </remarks>
        public bool HasCudPattern { get; set; }
    }
}
