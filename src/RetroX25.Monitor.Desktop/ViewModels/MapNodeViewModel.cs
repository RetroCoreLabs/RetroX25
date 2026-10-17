//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// One discovered X.25 switch rendered as a NodifyM graph node. It extends the node-graph base
    /// (<see cref="NodeViewModelBase"/> — position, dragging, selection) with the live X.25 metadata the map
    /// shows and a status colour. Trunk links are undirected, so a single <see cref="Connector"/> anchors
    /// every edge that touches this node.
    /// </summary>
    public sealed partial class MapNodeViewModel : NodeViewModelBase
    {
        /// <summary>
        /// The stable key identifying this node (its monitor endpoint, or a synthetic key for a node with
        /// no reachable monitor).
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The monitor host used to reach this node, or empty when it has no reachable monitor.
        /// </summary>
        public string Host { get; }

        /// <summary>
        /// The monitor TCP port used to reach this node, or 0 when it has no reachable monitor.
        /// </summary>
        public int MonitorPort { get; }

        /// <summary>
        /// True when this node is an NMC-reachable switch (drives reachable/down colouring); false for a stub
        /// peer with no management address to walk. Seeded from the constructor, then set by the discovery walk.
        /// </summary>
        public bool HasMonitor { get; set; }

        /// <summary>
        /// The discovery depth (0 for the seed), used by the radial layout.
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// The single connector every trunk edge attaches to. Connecting is disabled (<c>CanConnect = false</c>)
        /// because the topology is discovered, not hand-edited.
        /// </summary>
        public ConnectorViewModelBase Connector { get; }

        [ObservableProperty]
        private string _dnic = string.Empty;

        [ObservableProperty]
        private bool _reachable;

        [ObservableProperty]
        private string _statusText = "discovering…";

        [ObservableProperty]
        private IBrush _fill = Brushes.Gray;

        /// <summary>
        /// The node's display name. Maps onto the base <see cref="NodeViewModelBase.Title"/> so discovery code
        /// can keep using <c>Name</c>.
        /// </summary>
        public string Name
        {
            get => Title;
            set => Title = value;
        }

        /// <summary>
        /// The endpoint label shown on the node.
        /// </summary>
        public string EndpointText => HasMonitor ? Host + ":" + MonitorPort : "no monitor";

        /// <summary>
        /// Creates a map node.
        /// </summary>
        /// <param name="key">
        /// The stable key (monitor endpoint or synthetic key).
        /// </param>
        /// <param name="name">
        /// The initial display name.
        /// </param>
        /// <param name="host">
        /// The monitor host, or empty when unreachable.
        /// </param>
        /// <param name="monitorPort">
        /// The monitor TCP port, or 0 when unreachable.
        /// </param>
        public MapNodeViewModel(string key, string name, string host, int monitorPort)
        {
            Key = key;
            Host = host;
            MonitorPort = monitorPort;
            HasMonitor = monitorPort > 0;
            Title = name;

            // One connector, rendered by the node so its Anchor is tracked; connecting is disabled.
            Connector = new ConnectorViewModelBase
            {
                Title = name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                CanConnect = false
            };
            Input = new ObservableCollection<object> { Connector };
        }
    }
}
