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

public partial class ConfigurationWindow : Window
{
    public ConfigurationWindow()
    {
        InitializeComponent();
    }

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
