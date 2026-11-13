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
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the TCP LAPB tab in the Configuration window: the interfaces that carry LAPB (the
/// X.25 link layer) over a TCP connection.
/// </summary>
public partial class TcpLapbInterfacesTabViewModel : ViewModelWithLogger
{
    private readonly RuntimeConfig _config;
    private readonly IDialogService _dialogService;
    private Avalonia.Controls.Window? _ownerWindow;

    [ObservableProperty]
    private ObservableCollection<TcpLapbInterfaceDisplayItem> _tcpLapbInterfaces = new();

    [ObservableProperty]
    private TcpLapbInterfaceDisplayItem? _selectedInterface;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// Creates the view-model and fills the list from the TCP-LAPB interfaces in
    /// <paramref name="config"/>.
    /// </summary>
    /// <param name="logger">Logger for this view-model.</param>
    /// <param name="config">
    /// The shared runtime configuration; add, edit and delete change its TCP-LAPB interface list in place.
    /// </param>
    /// <param name="dialogService">Service used for the delete confirmation and for error messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TcpLapbInterfacesTabViewModel(
        ILogger<TcpLapbInterfacesTabViewModel> logger,
        RuntimeConfig config,
        IDialogService dialogService) : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        LoadInterfaces();
        Logger.LogInformation("TcpLapbInterfacesTabViewModel initialized");
    }

    /// <summary>
    /// Sets the window that owns the add and edit dialogs.
    /// </summary>
    /// <param name="ownerWindow">The Configuration window.</param>
    public void SetOwnerWindow(Avalonia.Controls.Window ownerWindow)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
    }

    private void LoadInterfaces()
    {
        TcpLapbInterfaces.Clear();

        for (int i = 0; i < _config.Routing.TcpLapbInterfaces.Count; i++)
        {
            TcpLapbInterfaceConfig entry = _config.Routing.TcpLapbInterfaces[i];
            TcpLapbInterfaces.Add(new TcpLapbInterfaceDisplayItem
            {
                Index = i,
                Name = entry.Name,
                Mode = entry.Mode.ToString(),
                RemoteAddress = entry.RemoteAddress ?? "",
                Port = entry.Port,
                RemoteMachineId = entry.RemoteMachineId,
                Enabled = entry.Enabled,
                Description = entry.Description
            });
        }

        StatusText = $"{TcpLapbInterfaces.Count} TCP-LAPB interface(s) configured";
        Logger.LogDebug("Loaded {Count} TCP-LAPB interfaces", TcpLapbInterfaces.Count);
    }

    /// <summary>
    /// Adds an interface to the configuration and redraws the list.
    /// </summary>
    /// <param name="added">The interface the dialog produced.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="added"/> is null.</exception>
    public void AddEntry(TcpLapbInterfaceConfig added)
    {
        if (added == null)
        {
            throw new ArgumentNullException(nameof(added));
        }

        _config.Routing.TcpLapbInterfaces.Add(added);
        LoadInterfaces();
        StatusText = $"Added TCP-LAPB interface: {added.Name}";
        Logger.LogInformation("Added TCP-LAPB interface: {Name}", added.Name);
    }

    /// <summary>
    /// Replaces the interface at <paramref name="index"/> in the configuration and redraws the list.
    /// </summary>
    /// <param name="index">Position in the configuration's TCP-LAPB list.</param>
    /// <param name="updated">The interface the dialog produced.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updated"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is not in the list.</exception>
    public void ReplaceEntry(int index, TcpLapbInterfaceConfig updated)
    {
        if (updated == null)
        {
            throw new ArgumentNullException(nameof(updated));
        }

        if (index < 0 || index >= _config.Routing.TcpLapbInterfaces.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _config.Routing.TcpLapbInterfaces[index] = updated;
        LoadInterfaces();
        StatusText = $"Updated TCP-LAPB interface: {updated.Name}";
        Logger.LogInformation("Updated TCP-LAPB interface: {Name}", updated.Name);
    }

    [RelayCommand]
    private void AddInterface()
    {
        try
        {
            var dialogViewModel = new TcpLapbInterfaceEditDialogViewModel(Logger, null, true)
            {
                Name = $"TcpLapb{_config.Routing.TcpLapbInterfaces.Count}"
            };
            var dialog = new Views.TcpLapbInterfaceEditDialog
            {
                DataContext = dialogViewModel
            };

            var result = dialog.ShowDialog<bool>(_ownerWindow!);
            result.ContinueWith(task =>
            {
                if (task.Result && dialogViewModel.Result != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => AddEntry(dialogViewModel.Result));
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding TCP-LAPB interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to add TCP-LAPB interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void EditInterface()
    {
        if (SelectedInterface == null)
        {
            StatusText = "Please select a TCP-LAPB interface to edit";
            return;
        }

        try
        {
            int index = SelectedInterface.Index;
            if (index < 0 || index >= _config.Routing.TcpLapbInterfaces.Count)
            {
                return;
            }

            var dialogViewModel = new TcpLapbInterfaceEditDialogViewModel(Logger, _config.Routing.TcpLapbInterfaces[index], false);
            var dialog = new Views.TcpLapbInterfaceEditDialog
            {
                DataContext = dialogViewModel
            };

            var result = dialog.ShowDialog<bool>(_ownerWindow!);
            result.ContinueWith(task =>
            {
                if (task.Result && dialogViewModel.Result != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ReplaceEntry(index, dialogViewModel.Result));
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error editing TCP-LAPB interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to edit TCP-LAPB interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteInterface()
    {
        if (SelectedInterface == null)
        {
            StatusText = "Please select a TCP-LAPB interface to delete";
            return;
        }

        try
        {
            int index = SelectedInterface.Index;
            if (index < 0 || index >= _config.Routing.TcpLapbInterfaces.Count)
            {
                return;
            }

            TcpLapbInterfaceConfig toDelete = _config.Routing.TcpLapbInterfaces[index];

            bool confirmed = await _dialogService.ShowConfirmation(
                "Confirm Delete",
                $"Delete TCP-LAPB interface '{toDelete.Name}'?\n\nMode: {toDelete.Mode}\nRemote: {toDelete.RemoteMachineId}");

            if (confirmed)
            {
                _config.Routing.TcpLapbInterfaces.RemoveAt(index);
                LoadInterfaces();
                StatusText = $"Deleted TCP-LAPB interface: {toDelete.Name}";
                Logger.LogInformation("Deleted TCP-LAPB interface: {Name}", toDelete.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting TCP-LAPB interface");
            StatusText = $"Error: {ex.Message}";
            _dialogService.ShowError("Error", $"Failed to delete TCP-LAPB interface:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadInterfaces();
        StatusText = "TCP-LAPB interfaces refreshed";
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Logger.LogInformation("TcpLapbInterfacesTabViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// Display item for the TCP-LAPB interface list.
/// </summary>
public class TcpLapbInterfaceDisplayItem
{
    /// <summary>Gets or sets the position in the configuration's TCP-LAPB list.</summary>
    public int Index { get; set; }

    /// <summary>Gets or sets the interface name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the TCP mode as text, "Client" or "Server".</summary>
    public string Mode { get; set; } = "";

    /// <summary>Gets or sets the remote IP address (used in client mode).</summary>
    public string RemoteAddress { get; set; } = "";

    /// <summary>Gets or sets the TCP port (the remote port for a client, the listening port for a server).</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the machine ID of the machine at the far end.</summary>
    public string RemoteMachineId { get; set; } = "";

    /// <summary>Gets or sets whether the interface is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the free-text description.</summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets a one-line summary: name, mode, address or port, machine and whether it is enabled.
    /// </summary>
    public string DisplayText => Mode == "Client"
        ? $"{Name} - {Mode} → {RemoteAddress}:{Port} (Machine {RemoteMachineId}) [{(Enabled ? "Enabled" : "Disabled")}]"
        : $"{Name} - {Mode} port {Port} (Machine {RemoteMachineId}) [{(Enabled ? "Enabled" : "Disabled")}]";
}
