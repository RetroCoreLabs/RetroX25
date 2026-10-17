//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Generic;
using RetroX25.Monitor.Desktop.Theming;

namespace RetroX25.Monitor.Desktop.Services
{
    /// <summary>
    /// A persisted switch endpoint (JSON-friendly: mutable auto-properties so System.Text.Json can round-trip
    /// it). Mirrors the runtime <see cref="RetroX25.Monitor.Desktop.ViewModels.SwitchEndpoint"/>.
    /// </summary>
    public sealed class SwitchEndpointSetting
    {
        /// <summary>
        /// The friendly name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The monitor host or IP.
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// The monitor TCP port.
        /// </summary>
        public int Port { get; set; }
    }

    /// <summary>
    /// A persisted network-map node position, keyed by the node's stable key (its monitor endpoint or a
    /// synthetic key). Lets the map restore where the user dragged each node.
    /// </summary>
    public sealed class NodePositionSetting
    {
        /// <summary>
        /// The node's stable key.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// The node's X position on the map canvas.
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// The node's Y position on the map canvas.
        /// </summary>
        public double Y { get; set; }
    }

    /// <summary>
    /// The user's persisted application preferences. Kept deliberately small; grow it as new persisted
    /// settings are added (colour thresholds, …).
    /// </summary>
    public sealed class AppPreferences
    {
        /// <summary>
        /// The colour theme to apply on startup.
        /// </summary>
        public AppTheme Theme { get; set; } = AppTheme.X25;

        /// <summary>
        /// The remembered seed switch endpoints. Empty means "seed the demo switches on next launch".
        /// </summary>
        public List<SwitchEndpointSetting> Switches { get; set; } = new List<SwitchEndpointSetting>();

        /// <summary>
        /// The remembered network-map node positions, keyed by node key.
        /// </summary>
        public List<NodePositionSetting> NodePositions { get; set; } = new List<NodePositionSetting>();
    }
}
