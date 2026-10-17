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
/// ViewModel for Known Hosts tab in Configuration Window.
/// Manages known host entries for multi-hop X.25 routing.
/// </summary>
public partial class KnownHostsTabViewModel : ViewModelWithLogger
{
    private readonly RuntimeConfig _config;
    private readonly IDialogService _dialogService;
    private Avalonia.Controls.Window? _ownerWindow;

    [ObservableProperty]
    private ObservableCollection<KnownHostDisplayItem> _knownHosts = new();

    [ObservableProperty]
    private KnownHostDisplayItem? _selectedHost;

    [ObservableProperty]
    private string _statusText = "";

    public KnownHostsTabViewModel(
        ILogger<KnownHostsTabViewModel> logger,
        RuntimeConfig config,
        IDialogService dialogService) : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        LoadKnownHosts();
        Logger.LogInformation("KnownHostsTabViewModel initialized");
    }

    /// <summary>
    /// Set the owner window for dialogs.
    /// </summary>
    public void SetOwnerWindow(Avalonia.Controls.Window ownerWindow)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
    }

    /// <summary>
    /// Load known hosts from configuration.
    /// </summary>
    private void LoadKnownHosts()
    {
        KnownHosts.Clear();

        for (int i = 0; i < _config.Routing.KnownHosts.Count; i++)
        {
            var host = _config.Routing.KnownHosts[i];
            KnownHosts.Add(new KnownHostDisplayItem
            {
                Index = i,
                Name = host.Name,
                MachineId = host.MachineId,
                Reachability = host.Reachability.ToString(),
                Via = host.Via,
                Enabled = host.Enabled,
                Description = host.Description
            });
        }

        StatusText = $"{KnownHosts.Count} known host(s) configured";
        Logger.LogDebug("Loaded {Count} known hosts", KnownHosts.Count);
    }

    [RelayCommand]
    private void AddHost()
    {
        try
        {
            // Create new host with defaults
            var newHost = new KnownHostEntry
            {
                Name = $"Host{_config.Routing.KnownHosts.Count}",
                MachineId = "",
                Reachability = HostReachability.Direct,
                Via = "",
                Enabled = true,
                Description = ""
            };

            // Open edit dialog
            var dialogViewModel = new KnownHostEditDialogViewModel(Logger, newHost, true);
            var dialog = new Views.KnownHostEditDialog
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
                        _config.Routing.KnownHosts.Add(dialogViewModel.Result);
                        LoadKnownHosts();
                        StatusText = $"Added known host: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Added known host: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding known host");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to add known host:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void EditHost()
    {
        if (SelectedHost == null)
        {
            StatusText = "Please select a known host to edit";
            return;
        }

        try
        {
            var index = SelectedHost.Index;
            if (index < 0 || index >= _config.Routing.KnownHosts.Count)
                return;

            var existingHost = _config.Routing.KnownHosts[index];

            // Open edit dialog
            var dialogViewModel = new KnownHostEditDialogViewModel(Logger, existingHost, false);
            var dialog = new Views.KnownHostEditDialog
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
                        _config.Routing.KnownHosts[index] = dialogViewModel.Result;
                        LoadKnownHosts();
                        StatusText = $"Updated known host: {dialogViewModel.Result.Name}";
                        Logger.LogInformation("Updated known host: {Name}", dialogViewModel.Result.Name);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error editing known host");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to edit known host:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteHost()
    {
        if (SelectedHost == null)
        {
            StatusText = "Please select a known host to delete";
            return;
        }

        try
        {
            var index = SelectedHost.Index;
            if (index < 0 || index >= _config.Routing.KnownHosts.Count)
                return;

            var hostToDelete = _config.Routing.KnownHosts[index];

            var confirmed = await _dialogService.ShowConfirmation(
                "Confirm Delete",
                $"Delete known host '{hostToDelete.Name}'?\n\nMachine ID: {hostToDelete.MachineId}\nVia: {hostToDelete.Via}");

            if (confirmed)
            {
                _config.Routing.KnownHosts.RemoveAt(index);
                LoadKnownHosts();
                StatusText = $"Deleted known host: {hostToDelete.Name}";
                Logger.LogInformation("Deleted known host: {Name}", hostToDelete.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting known host");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to delete known host:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadKnownHosts();
        StatusText = "Known hosts refreshed";
    }

    public override void Dispose()
    {
        Logger.LogInformation("KnownHostsTabViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// Display item for known host list.
/// </summary>
public class KnownHostDisplayItem
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string MachineId { get; set; } = "";
    public string Reachability { get; set; } = "";
    public string Via { get; set; } = "";
    public bool Enabled { get; set; }
    public string Description { get; set; } = "";

    public string DisplayText => $"{Name} (Machine {MachineId}) - {Reachability} via {Via} [{(Enabled ? "Enabled" : "Disabled")}]";
}
