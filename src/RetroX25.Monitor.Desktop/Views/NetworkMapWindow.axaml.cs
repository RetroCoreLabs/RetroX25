//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using RetroX25.Monitor.Desktop.Services;
using RetroX25.Monitor.Desktop.ViewModels;

namespace RetroX25.Monitor.Desktop.Views
{
    /// <summary>
    /// The network-map window and the application's entry point: it seeds in-band NMC discovery, draws the
    /// discovered switches as coloured nodes and their trunks as edges, opens the monitor tool windows from a
    /// node's right-click menu, and shows a trunk's routing from the trunk's right-click menu.
    /// </summary>
    public partial class NetworkMapWindow : Window
    {
        // The tools that can be opened for an endpoint.
        private enum Tool
        {
            PacketAnalyzer,
            LinkStatus,
            LogViewer
        }

        /// <summary>
        /// Creates the network-map window.
        /// </summary>
        public NetworkMapWindow()
        {
            InitializeComponent();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is NetworkMapViewModel vm)
            {
                // Remember where the user dragged the nodes before tearing the map down.
                vm.SavePositions();
                vm.Dispose();
            }
        }

        // ----- File / View / Help menu -----

        private void OnExit(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnOpenPreferences(object? sender, RoutedEventArgs e)
        {
            PreferencesWindow dialog = new PreferencesWindow
            {
                DataContext = new PreferencesViewModel(PreferencesStore.Load())
            };
            _ = dialog.ShowDialog(this);
        }

        private void OnAbout(object? sender, RoutedEventArgs e)
        {
            TextBlock text = new TextBlock
            {
                Text = "X.25 Network Monitor\n\nAuto-discovers the network over the in-band X.25 management\n"
                     + "API (NMC), then offers packet, link and log tools per node and\n"
                     + "routing per trunk.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(20)
            };

            Window about = new Window
            {
                Title = "About X.25 Network Monitor",
                Width = 400,
                Height = 200,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = text
            };
            _ = about.ShowDialog(this);
        }

        // ----- Node context menu: open a tool for the right-clicked node -----

        private void OnOpenPacketAnalyzer(object? sender, RoutedEventArgs e)
        {
            OpenToolForNode(sender, Tool.PacketAnalyzer);
        }

        private void OnOpenLinkStatus(object? sender, RoutedEventArgs e)
        {
            OpenToolForNode(sender, Tool.LinkStatus);
        }

        private void OnOpenLogViewer(object? sender, RoutedEventArgs e)
        {
            OpenToolForNode(sender, Tool.LogViewer);
        }

        // Resolves the node under the clicked menu item to its monitor endpoint and opens the tool. When the
        // node has no matching monitor bookmark, the tool opens with the full bookmark list so the user can
        // pick or edit an endpoint.
        private void OpenToolForNode(object? sender, Tool tool)
        {
            NetworkMapViewModel? map = DataContext as NetworkMapViewModel;
            if (map == null || sender is not Control control || control.DataContext is not MapNodeViewModel node)
            {
                return;
            }

            ObservableCollection<SwitchEndpoint> list;
            SwitchEndpoint? endpoint = map.EndpointForNode(node);
            if (endpoint != null)
            {
                list = new ObservableCollection<SwitchEndpoint> { endpoint };
            }
            else
            {
                list = map.Switches;
            }

            OpenTool(tool, list);
        }

        // Opens the given tool window bound to the supplied endpoint list.
        private static void OpenTool(Tool tool, ObservableCollection<SwitchEndpoint> list)
        {
            if (tool == Tool.PacketAnalyzer)
            {
                new PacketMonitorWindow { DataContext = new PacketMonitorViewModel(list) }.Show();
            }
            else if (tool == Tool.LinkStatus)
            {
                new LinkStatusWindow { DataContext = new LinkStatusViewModel(list) }.Show();
            }
            else
            {
                new LogWindow { DataContext = new LogViewModel(list) }.Show();
            }
        }

        // ----- Trunk context menu: show the routing each end sends across the link -----

        private void OnShowTrunkRouting(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control control || control.DataContext is not MapConnectionViewModel edge)
            {
                return;
            }

            TextBox body = new TextBox
            {
                Text = edge.RoutingText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
                Margin = new Avalonia.Thickness(12)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(body, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(body, ScrollBarVisibility.Auto);

            Window routing = new Window
            {
                Title = "Trunk routing — " + edge.TrunkLabel,
                Width = 460,
                Height = 340,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = body
            };
            _ = routing.ShowDialog(this);
        }
    }
}
