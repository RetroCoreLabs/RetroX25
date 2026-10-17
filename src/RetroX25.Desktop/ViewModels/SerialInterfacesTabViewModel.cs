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
/// ViewModel for Serial Interfaces tab in Configuration Window.
/// Manages serial port configurations for X.25 routing.
/// </summary>
public partial class SerialInterfacesTabViewModel : ViewModelWithLogger
{
    private readonly RuntimeConfig _config;
    private readonly IDialogService _dialogService;
    private Avalonia.Controls.Window? _ownerWindow;

    [ObservableProperty]
    private ObservableCollection<SerialInterfaceDisplayItem> _serialInterfaces = new();

    [ObservableProperty]
    private SerialInterfaceDisplayItem? _selectedInterface;

    [ObservableProperty]
    private string _statusText = "";

    public SerialInterfacesTabViewModel(
        ILogger<SerialInterfacesTabViewModel> logger,
        RuntimeConfig config,
        IDialogService dialogService) : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        LoadSerialInterfaces();
        Logger.LogInformation("SerialInterfacesTabViewModel initialized");
    }

    /// <summary>
    /// Set the owner window for dialogs.
    /// </summary>
    public void SetOwnerWindow(Avalonia.Controls.Window ownerWindow)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
    }

    /// <summary>
    /// Load serial interfaces from configuration.
    /// </summary>
    private void LoadSerialInterfaces()
    {
        SerialInterfaces.Clear();

        for (int i = 0; i < _config.Routing.SerialInterfaces.Count; i++)
        {
            var si = _config.Routing.SerialInterfaces[i];
            SerialInterfaces.Add(new SerialInterfaceDisplayItem
            {
                Index = i,
                Name = si.Name,
                PortName = si.PortName,
                BaudRate = si.BaudRate,
                RemoteMachineId = si.RemoteMachineId,
                Enabled = si.Enabled,
                Description = si.Description
            });
        }

        StatusText = $"{SerialInterfaces.Count} serial interface(s) configured";
        Logger.LogDebug("Loaded {Count} serial interfaces", SerialInterfaces.Count);
    }

    [RelayCommand]
    private void AddInterface()
    {
        try
        {
            // Create new interface with defaults
            var newInterface = new SerialInterfaceConfig
            {
                Name = $"Serial{_config.Routing.SerialInterfaces.Count}",
                Enabled = false,
                PortName = "COM1",
                BaudRate = 9600,
                RemoteMachineId = "",
                Description = ""
            };

            // Open edit dialog
            var dialogViewModel = new SerialInterfaceEditDialogViewModel(Logger, newInterface, true);
            var dialog = new Views.SerialInterfaceEditDialog
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
                        _config.Routing.SerialInterfaces.Add(dialogViewModel.Result);
                        LoadSerialInterfaces();
                        StatusText = $"Added serial interface: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Added serial interface: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding serial interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to add serial interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void EditInterface()
    {
        if (SelectedInterface == null)
        {
            StatusText = "Please select a serial interface to edit";
            return;
        }

        try
        {
            var index = SelectedInterface.Index;
            if (index < 0 || index >= _config.Routing.SerialInterfaces.Count)
                return;

            var existingInterface = _config.Routing.SerialInterfaces[index];

            // Open edit dialog
            var dialogViewModel = new SerialInterfaceEditDialogViewModel(Logger, existingInterface, false);
            var dialog = new Views.SerialInterfaceEditDialog
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
                        _config.Routing.SerialInterfaces[index] = dialogViewModel.Result;
                        LoadSerialInterfaces();
                        StatusText = $"Updated serial interface: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Updated serial interface: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error editing serial interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to edit serial interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteInterface()
    {
        if (SelectedInterface == null)
        {
            StatusText = "Please select a serial interface to delete";
            return;
        }

        try
        {
            var index = SelectedInterface.Index;
            if (index < 0 || index >= _config.Routing.SerialInterfaces.Count)
                return;

            var interfaceToDelete = _config.Routing.SerialInterfaces[index];

            var confirmed = await _dialogService.ShowConfirmation(
                "Confirm Delete",
                $"Delete serial interface '{interfaceToDelete.Name}'?\n\nPort: {interfaceToDelete.PortName}\nRemote: {interfaceToDelete.RemoteMachineId}");

            if (confirmed)
            {
                _config.Routing.SerialInterfaces.RemoveAt(index);
                LoadSerialInterfaces();
                StatusText = $"Deleted serial interface: {interfaceToDelete.Name}";
                Logger.LogInformation("Deleted serial interface: {Name}", interfaceToDelete.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting serial interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to delete serial interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadSerialInterfaces();
        StatusText = "Serial interfaces refreshed";
    }

    public override void Dispose()
    {
        Logger.LogInformation("SerialInterfacesTabViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// Display item for serial interface list.
/// </summary>
public class SerialInterfaceDisplayItem
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; }
    public string RemoteMachineId { get; set; } = "";
    public bool Enabled { get; set; }
    public string Description { get; set; } = "";

    public string DisplayText => $"{Name} - {PortName} @ {BaudRate} baud → {RemoteMachineId} [{(Enabled ? "Enabled" : "Disabled")}]";
}
