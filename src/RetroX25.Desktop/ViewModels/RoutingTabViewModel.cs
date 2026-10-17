//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for Routing tab in Configuration Window.
/// Manages X.25 routing table entries for address pattern matching.
/// </summary>
public partial class RoutingTabViewModel : ViewModelWithLogger
{
    private readonly RuntimeConfig _config;
    private readonly IDialogService _dialogService;
    private Avalonia.Controls.Window? _ownerWindow;

    [ObservableProperty]
    private ObservableCollection<RoutingTableDisplayItem> _routes = new();

    [ObservableProperty]
    private RoutingTableDisplayItem? _selectedRoute;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// Creates the view-model and fills the list from the routing table in
    /// <paramref name="config"/>.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model.
    /// </param>
    /// <param name="config">
    /// The shared runtime configuration; add, edit and delete change its routing table in
    /// place.
    /// </param>
    /// <param name="dialogService">
    /// Service used for the delete confirmation and for error messages.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/>, <paramref name="config"/> or
    /// <paramref name="dialogService"/> is null.
    /// </exception>
    public RoutingTabViewModel(
        ILogger<RoutingTabViewModel> logger,
        RuntimeConfig config,
        IDialogService dialogService) : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        LoadRoutes();
        Logger.LogInformation("RoutingTabViewModel initialized");
    }

    /// <summary>
    /// Set the owner window for dialogs.
    /// </summary>
    public void SetOwnerWindow(Avalonia.Controls.Window ownerWindow)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
    }

    /// <summary>
    /// Load routes from configuration.
    /// </summary>
    private void LoadRoutes()
    {
        Routes.Clear();

        for (int i = 0; i < _config.Routing.RoutingTable.Count; i++)
        {
            var route = _config.Routing.RoutingTable[i];
            Routes.Add(new RoutingTableDisplayItem
            {
                Index = i,
                Name = route.Name,
                X121AddressPattern = route.X121AddressPattern,
                Type = route.Type.ToString(),
                Destination = route.Destination,
                Priority = route.Priority,
                Enabled = route.Enabled,
                Description = route.Description
            });
        }

        StatusText = $"{Routes.Count} route(s) configured";
        Logger.LogDebug("Loaded {Count} routes", Routes.Count);
    }

    [RelayCommand]
    private void AddRoute()
    {
        try
        {
            // Create new route with defaults
            var newRoute = new X25RouteEntry
            {
                Name = $"Route{_config.Routing.RoutingTable.Count}",
                X121AddressPattern = "",
                Type = RouteType.Interface,
                Destination = "",
                Priority = 100,
                Enabled = true,
                Description = ""
            };

            // Open edit dialog
            var dialogViewModel = new RouteEditDialogViewModel(Logger, newRoute, true);
            var dialog = new Views.RouteEditDialog
            {
                DataContext = dialogViewModel
            };

            // Show dialog and wait for result
            var result = dialog.ShowDialog<bool>(_ownerWindow!);
            result.ContinueWith(task =>
            {
                if (task.Result && dialogViewModel.Result != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _config.Routing.RoutingTable.Add(dialogViewModel.Result);
                        LoadRoutes();
                        StatusText = $"Added route: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Added route: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding route");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to add route:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void EditRoute()
    {
        if (SelectedRoute == null)
        {
            StatusText = "Please select a route to edit";
            return;
        }

        try
        {
            var index = SelectedRoute.Index;
            if (index < 0 || index >= _config.Routing.RoutingTable.Count)
                return;

            var existingRoute = _config.Routing.RoutingTable[index];

            // Open edit dialog
            var dialogViewModel = new RouteEditDialogViewModel(Logger, existingRoute, false);
            var dialog = new Views.RouteEditDialog
            {
                DataContext = dialogViewModel
            };

            // Show dialog and wait for result
            var result = dialog.ShowDialog<bool>(_ownerWindow!);
            result.ContinueWith(task =>
            {
                if (task.Result && dialogViewModel.Result != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _config.Routing.RoutingTable[index] = dialogViewModel.Result;
                        LoadRoutes();
                        StatusText = $"Updated route: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Updated route: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error editing route");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to edit route:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteRoute()
    {
        if (SelectedRoute == null)
        {
            StatusText = "Please select a route to delete";
            return;
        }

        try
        {
            var index = SelectedRoute.Index;
            if (index < 0 || index >= _config.Routing.RoutingTable.Count)
                return;

            var routeToDelete = _config.Routing.RoutingTable[index];

            var confirmed = await _dialogService.ShowConfirmation(
                "Confirm Delete",
                $"Delete route '{routeToDelete.Name}'?\n\nPattern: {routeToDelete.X121AddressPattern}\nDestination: {routeToDelete.Destination}");

            if (confirmed)
            {
                _config.Routing.RoutingTable.RemoveAt(index);
                LoadRoutes();
                StatusText = $"Deleted route: {routeToDelete.Name}";
                Logger.LogInformation("Deleted route: {Name}", routeToDelete.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting route");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to delete route:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadRoutes();
        StatusText = "Routes refreshed";
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Logger.LogInformation("RoutingTabViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// Display item for routing table list.
/// </summary>
public class RoutingTableDisplayItem
{
    /// <summary>
    /// Gets or sets the position of this route in the configuration's routing table, used
    /// to find the entry again on edit and delete.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the route name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the X.121 address pattern the route matches, for example <c>100.*</c>.
    /// </summary>
    public string X121AddressPattern { get; set; } = "";

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
    /// Gets or sets the route priority; a higher number is preferred when several routes
    /// match.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets whether the route is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the free-text description of the route.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets a one-line summary of the route: name, pattern, type, destination, priority, and
    /// whether it is enabled.
    /// </summary>
    public string DisplayText => $"{Name}: {X121AddressPattern} → {Type}/{Destination} (Pri:{Priority}) [{(Enabled ? "Enabled" : "Disabled")}]";
}
