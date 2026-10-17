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
/// ViewModel for XOT Interfaces tab in Configuration Window.
/// Manages XOT (X.25 over TCP) interface configurations for X.25 routing.
/// </summary>
public partial class XotInterfacesTabViewModel : ViewModelWithLogger
{
    private readonly RuntimeConfig _config;
    private readonly IDialogService _dialogService;
    private Avalonia.Controls.Window? _ownerWindow;

    [ObservableProperty]
    private ObservableCollection<XotInterfaceDisplayItem> _xotInterfaces = new();

    [ObservableProperty]
    private XotInterfaceDisplayItem? _selectedInterface;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// Creates the view-model and fills the list from the XOT interfaces in
    /// <paramref name="config"/>.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model.
    /// </param>
    /// <param name="config">
    /// The shared runtime configuration; add, edit and delete change its XOT interface list
    /// in place.
    /// </param>
    /// <param name="dialogService">
    /// Service used for the delete confirmation and for error messages.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/>, <paramref name="config"/> or
    /// <paramref name="dialogService"/> is null.
    /// </exception>
    public XotInterfacesTabViewModel(
        ILogger<XotInterfacesTabViewModel> logger,
        RuntimeConfig config,
        IDialogService dialogService) : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        LoadXotInterfaces();
        Logger.LogInformation("XotInterfacesTabViewModel initialized");
    }

    /// <summary>
    /// Set the owner window for dialogs.
    /// </summary>
    public void SetOwnerWindow(Avalonia.Controls.Window ownerWindow)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
    }

    /// <summary>
    /// Load XOT interfaces from configuration.
    /// </summary>
    private void LoadXotInterfaces()
    {
        XotInterfaces.Clear();

        for (int i = 0; i < _config.Routing.XotInterfaces.Count; i++)
        {
            var xot = _config.Routing.XotInterfaces[i];
            XotInterfaces.Add(new XotInterfaceDisplayItem
            {
                Index = i,
                Name = xot.Name,
                Mode = xot.Mode.ToString(),
                RemoteIPAddress = xot.RemoteIPAddress ?? "",
                RemotePort = xot.RemotePort,
                LocalPort = xot.LocalPort,
                RemoteMachineId = xot.RemoteMachineId,
                Enabled = xot.Enabled,
                Description = xot.Description
            });
        }

        StatusText = $"{XotInterfaces.Count} XOT interface(s) configured";
        Logger.LogDebug("Loaded {Count} XOT interfaces", XotInterfaces.Count);
    }

    [RelayCommand]
    private void AddInterface()
    {
        try
        {
            // Create new interface with defaults
            var newInterface = new XotInterfaceConfig
            {
                Name = $"XOT{_config.Routing.XotInterfaces.Count}",
                Enabled = false,
                Mode = XotMode.Client,
                RemoteIPAddress = "127.0.0.1",
                RemotePort = 1998,
                LocalPort = 1998,
                RemoteMachineId = "",
                TcpKeepalive = true,
                Description = ""
            };

            // Open edit dialog
            var dialogViewModel = new XotInterfaceEditDialogViewModel(Logger, newInterface, true);
            var dialog = new Views.XotInterfaceEditDialog
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
                        _config.Routing.XotInterfaces.Add(dialogViewModel.Result);
                        LoadXotInterfaces();
                        StatusText = $"Added XOT interface: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Added XOT interface: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding XOT interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to add XOT interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void EditInterface()
    {
        if (SelectedInterface == null)
        {
            StatusText = "Please select an XOT interface to edit";
            return;
        }

        try
        {
            var index = SelectedInterface.Index;
            if (index < 0 || index >= _config.Routing.XotInterfaces.Count)
                return;

            var existingInterface = _config.Routing.XotInterfaces[index];

            // Open edit dialog
            var dialogViewModel = new XotInterfaceEditDialogViewModel(Logger, existingInterface, false);
            var dialog = new Views.XotInterfaceEditDialog
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
                        _config.Routing.XotInterfaces[index] = dialogViewModel.Result;
                        LoadXotInterfaces();
                        StatusText = $"Updated XOT interface: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Updated XOT interface: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error editing XOT interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to edit XOT interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteInterface()
    {
        if (SelectedInterface == null)
        {
            StatusText = "Please select an XOT interface to delete";
            return;
        }

        try
        {
            var index = SelectedInterface.Index;
            if (index < 0 || index >= _config.Routing.XotInterfaces.Count)
                return;

            var interfaceToDelete = _config.Routing.XotInterfaces[index];

            var confirmed = await _dialogService.ShowConfirmation(
                "Confirm Delete",
                $"Delete XOT interface '{interfaceToDelete.Name}'?\n\nMode: {interfaceToDelete.Mode}\nRemote: {interfaceToDelete.RemoteMachineId}");

            if (confirmed)
            {
                _config.Routing.XotInterfaces.RemoveAt(index);
                LoadXotInterfaces();
                StatusText = $"Deleted XOT interface: {interfaceToDelete.Name}";
                Logger.LogInformation("Deleted XOT interface: {Name}", interfaceToDelete.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting XOT interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to delete XOT interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadXotInterfaces();
        StatusText = "XOT interfaces refreshed";
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Logger.LogInformation("XotInterfacesTabViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// Display item for XOT interface list.
/// </summary>
public class XotInterfaceDisplayItem
{
    /// <summary>
    /// Gets or sets the position of this interface in the configuration's XOT interface
    /// list, used to find the entry again on edit and delete.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the interface name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the XOT mode as text, "Client" or "Server".
    /// </summary>
    public string Mode { get; set; } = "";

    /// <summary>
    /// Gets or sets the remote IP address used in client mode; empty when none is set.
    /// </summary>
    public string RemoteIPAddress { get; set; } = "";

    /// <summary>
    /// Gets or sets the remote TCP port used in client mode.
    /// </summary>
    public int RemotePort { get; set; }

    /// <summary>
    /// Gets or sets the local TCP port listened on in server mode.
    /// </summary>
    public int LocalPort { get; set; }

    /// <summary>
    /// Gets or sets the machine ID of the remote X.25 machine reached through this
    /// interface.
    /// </summary>
    public string RemoteMachineId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the interface is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the free-text description of the interface.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets a one-line summary of the interface.
    /// </summary>
    /// <remarks>
    /// In client mode it shows the remote address and port; in any other mode it shows the
    /// local port. Both forms include the name, mode, remote machine ID and whether the
    /// interface is enabled.
    /// </remarks>
    public string DisplayText => Mode == "Client"
        ? $"{Name} - {Mode} → {RemoteIPAddress}:{RemotePort} (Machine {RemoteMachineId}) [{(Enabled ? "Enabled" : "Disabled")}]"
        : $"{Name} - {Mode} port {LocalPort} (Machine {RemoteMachineId}) [{(Enabled ? "Enabled" : "Disabled")}]";
}
