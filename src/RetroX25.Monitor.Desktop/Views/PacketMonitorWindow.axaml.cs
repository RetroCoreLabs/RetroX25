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
    /// The packet-analyzer window: a three-pane Wireshark-style view (packet list, layered detail tree, hex).
    /// </summary>
    public partial class PacketMonitorWindow : Window
    {
        /// <summary>
        /// Creates the packet-analyzer window.
        /// </summary>
        public PacketMonitorWindow()
        {
            InitializeComponent();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is PacketMonitorViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
