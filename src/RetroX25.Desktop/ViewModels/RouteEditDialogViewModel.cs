using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for Route Edit Dialog.
/// </summary>
public partial class RouteEditDialogViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly bool _isNew;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _x121AddressPattern = "";

    [ObservableProperty]
    private string _type = "Interface";

    [ObservableProperty]
    private string _destination = "";

    [ObservableProperty]
    private int _priority = 100;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _callUserDataPattern = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _validationError = "";

    /// <summary>
    /// Available route types.
    /// </summary>
    public List<string> AvailableRouteTypes { get; } = new() { "Interface", "Gateway", "Local" };

    /// <summary>
    /// Result of the dialog (null if cancelled).
    /// </summary>
    public X25RouteEntry? Result { get; private set; }

    public RouteEditDialogViewModel(ILogger logger, X25RouteEntry? existing, bool isNew)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isNew = isNew;

        if (existing != null)
        {
            LoadFromConfig(existing);
        }
    }

    /// <summary>
    /// Load values from existing configuration.
    /// </summary>
    private void LoadFromConfig(X25RouteEntry entry)
    {
        Name = entry.Name;
        X121AddressPattern = entry.X121AddressPattern;
        Type = entry.Type.ToString();
        Destination = entry.Destination;
        Priority = entry.Priority;
        Enabled = entry.Enabled;
        CallUserDataPattern = entry.CallUserDataPattern ?? "";
        Description = entry.Description;
    }

    /// <summary>
    /// Validate inputs.
    /// </summary>
    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Name is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(X121AddressPattern))
        {
            ValidationError = "X.121 address pattern is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Destination))
        {
            ValidationError = "Destination is required";
            return false;
        }

        if (Priority < 0 || Priority > 1000)
        {
            ValidationError = "Priority must be between 0 and 1000";
            return false;
        }

        ValidationError = "";
        return true;
    }

    [RelayCommand]
    private void Ok()
    {
        if (!Validate())
            return;

        try
        {
            // Create result configuration
            Result = new X25RouteEntry
            {
                Name = Name.Trim(),
                X121AddressPattern = X121AddressPattern.Trim(),
                Type = Enum.Parse<RouteType>(Type),
                Destination = Destination.Trim(),
                Priority = Priority,
                Enabled = Enabled,
                CallUserDataPattern = string.IsNullOrWhiteSpace(CallUserDataPattern) ? null : CallUserDataPattern.Trim(),
                Description = Description.Trim()
            };

            _logger.LogInformation("Route dialog OK: {Name}", Result.Name);

            // Dialog will be closed by the caller
        }
        catch (Exception ex)
        {
            ValidationError = $"Error: {ex.Message}";
            _logger.LogError(ex, "Error creating route config");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        _logger.LogDebug("Route dialog cancelled");
        // Dialog will be closed by the caller
    }
}
