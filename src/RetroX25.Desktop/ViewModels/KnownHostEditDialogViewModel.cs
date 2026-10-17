//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for Known Host Edit Dialog.
/// </summary>
public partial class KnownHostEditDialogViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly bool _isNew;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _machineId = "";

    [ObservableProperty]
    private string _reachability = "Direct";

    [ObservableProperty]
    private string _via = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _validationError = "";

    /// <summary>
    /// Available reachability options.
    /// </summary>
    public List<string> AvailableReachability { get; } = new() { "Direct", "Gateway" };

    /// <summary>
    /// Result of the dialog (null if cancelled).
    /// </summary>
    public KnownHostEntry? Result { get; private set; }

    /// <summary>
    /// Gets the title of the dialog window: <c>Add Known Host</c> when the dialog adds an entry and
    /// <c>Edit Known Host</c> when it edits one.
    /// </summary>
    public string DialogTitle => _isNew ? "Add Known Host" : "Edit Known Host";

    /// <summary>
    /// Creates the dialog view-model and fills its fields from <paramref name="existing"/>.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model.
    /// </param>
    /// <param name="existing">
    /// The entry whose values fill the fields, or null to start from the field defaults.
    /// </param>
    /// <param name="isNew">
    /// True when the dialog adds a new known host, false when it edits one. It decides the
    /// words in <see cref="DialogTitle"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    public KnownHostEditDialogViewModel(ILogger logger, KnownHostEntry? existing, bool isNew)
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
    private void LoadFromConfig(KnownHostEntry entry)
    {
        Name = entry.Name;
        MachineId = entry.MachineId;
        Reachability = entry.Reachability.ToString();
        Via = entry.Via;
        Description = entry.Description;
        Enabled = entry.Enabled;
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

        if (string.IsNullOrWhiteSpace(MachineId))
        {
            ValidationError = "Machine ID is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Via))
        {
            ValidationError = "Via field is required (interface name for Direct, gateway ID for Gateway)";
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
            Result = new KnownHostEntry
            {
                Name = Name.Trim(),
                MachineId = MachineId.Trim(),
                Reachability = Enum.Parse<HostReachability>(Reachability),
                Via = Via.Trim(),
                Description = Description.Trim(),
                Enabled = Enabled
            };

            _logger.LogInformation("Known host dialog OK: {Name}", Result.Name);

            // Dialog will be closed by the caller
        }
        catch (Exception ex)
        {
            ValidationError = $"Error: {ex.Message}";
            _logger.LogError(ex, "Error creating known host config");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        _logger.LogDebug("Known host dialog cancelled");
        // Dialog will be closed by the caller
    }
}
