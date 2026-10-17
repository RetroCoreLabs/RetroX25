//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using RetroX25.Monitor.Desktop.ViewModels;

namespace RetroX25.Monitor.Desktop.Views
{
    /// <summary>
    /// The link-status window: live per-port state and traffic counters for a selected switch.
    /// </summary>
    public partial class LinkStatusWindow : Window
    {
        /// <summary>
        /// Creates the link-status window.
        /// </summary>
        public LinkStatusWindow()
        {
            InitializeComponent();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is LinkStatusViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
