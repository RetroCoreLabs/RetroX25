//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using Avalonia.Input;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The Configuration window (F9), where the user edits the runtime settings, the serial and
/// XOT interfaces, the known hosts and the routing table.
/// </summary>
public partial class ConfigurationWindow : Window
{
    /// <summary>
    /// Creates the window and loads its layout, with no view-model attached.
    /// </summary>
    public ConfigurationWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window bound to <paramref name="viewModel"/>.
    /// </summary>
    /// <param name="viewModel">
    /// The view-model the window binds to.
    /// </param>
    /// <remarks>
    /// Hands the window's storage provider to the view-model once the window opens, gives
    /// each tab view-model this window as the owner for its dialogs, and closes the window
    /// when the user presses Esc.
    /// </remarks>
    public ConfigurationWindow(ConfigurationWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Set storage provider for file operations after window is opened
        this.Opened += (sender, e) =>
        {
            viewModel.SetStorageProvider(this.StorageProvider);
        };

        // Set owner window reference for tab ViewModels to show dialogs
        viewModel.SerialInterfacesTab.SetOwnerWindow(this);
        viewModel.XotInterfacesTab.SetOwnerWindow(this);
        viewModel.KnownHostsTab.SetOwnerWindow(this);
        viewModel.RoutingTab.SetOwnerWindow(this);

        // Add Esc key handler to close window
        this.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        };
    }
}
