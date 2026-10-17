//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodifyM.Avalonia.ViewModelBase;
using RetroIO.Protocols.X25.Nmc;
using RetroX25.Monitor.Desktop.Services;
using RetroX25.Monitor.Desktop.Theming;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// The network-map view-model: it auto-discovers the network by walking it over the in-band X.25
    /// management API (the NMC). From a single seed — one switch's access endpoint plus its management
    /// address — it connects, logs in, and follows each switch's neighbours across real X.25 (including
    /// cross-environment trunks). The discovered switches and trunks are rendered in an interactive NodifyM
    /// graph (pan, zoom, drag, select); node positions are remembered between runs. A trunk can be
    /// right-clicked to see the routing each end sends across it (see docs/NMC_API_V1.md).
    /// </summary>
    public sealed partial class NetworkMapViewModel : NodifyEditorViewModelBase, IDisposable
    {
        /// <summary>
        /// The logical width of the initial radial layout area.
        /// </summary>
        public const double CanvasWidth = 900;

        /// <summary>
        /// The logical height of the initial radial layout area.
        /// </summary>
        public const double CanvasHeight = 560;

        // Node/link colours come from the ACTIVE palette (ThemeManager), resolved at use-time so a live
        // theme swap recolours the map. Keys must exist in every palette (Styles/Palettes/*.axaml).
        private static IBrush BrushReachable => ThemeManager.Brush("NodeReachableBrush");
        private static IBrush BrushDegraded => ThemeManager.Brush("NodeDegradedBrush");
        private static IBrush BrushDown => ThemeManager.Brush("NodeDownBrush");
        private static IBrush BrushUnknown => ThemeManager.Brush("NodeUnknownBrush");
        private static IBrush BrushLinkUp => ThemeManager.Brush("LinkUpBrush");
        private static IBrush BrushLinkDown => ThemeManager.Brush("LinkDownBrush");

        private readonly Action<Action> _uiInvoke;

        private readonly Dictionary<string, MapNodeViewModel> _nodesByKey = new Dictionary<string, MapNodeViewModel>();
        private readonly List<MapNodeViewModel> _nodeList = new List<MapNodeViewModel>();
        private readonly Dictionary<string, MapConnectionViewModel> _edgesByKey = new Dictionary<string, MapConnectionViewModel>();
        private readonly List<MapConnectionViewModel> _edgeList = new List<MapConnectionViewModel>();

        // The latest NMC data for each discovered node, keyed by node key (its management address). Used to
        // compute the routing shown when a trunk is right-clicked.
        private readonly Dictionary<string, NmcNode> _nmcByKey = new Dictionary<string, NmcNode>();

        // Remembered node positions (key -> canvas point), restored from preferences and re-applied on layout.
        private readonly Dictionary<string, Point> _savedPositions = new Dictionary<string, Point>();

        private NmcDiscovery? _discovery;

        [ObservableProperty]
        private string _status = "Idle";

        [ObservableProperty]
        private bool _isDiscovering;

        // The seed: the local switch access endpoint the NMC connects to (XOT), and the management address it
        // first calls. Discovery walks outward from there.
        [ObservableProperty]
        private string _seedHost = "127.0.0.1";

        [ObservableProperty]
        private int _seedPort = 2200;

        [ObservableProperty]
        private string _seedMgmtAddr = "23500000";

        [ObservableProperty]
        private string _username = "admin";

        [ObservableProperty]
        private string _password = "admin";

        /// <summary>
        /// Monitor-plane endpoints for the per-node packet/link/log tool windows. These are separate from
        /// NMC discovery (which walks the management plane); a node's tools resolve to the bookmark whose
        /// name matches the node, so a discovered switch can still be opened in the X25MON tools.
        /// </summary>
        public ObservableCollection<SwitchEndpoint> Switches { get; } = new ObservableCollection<SwitchEndpoint>();

        /// <summary>
        /// Creates the network-map view-model.
        /// </summary>
        /// <param name="uiInvoke">
        /// Marshals work onto the UI thread; defaults to the Avalonia dispatcher.
        /// </param>
        public NetworkMapViewModel(Action<Action>? uiInvoke = null)
        {
            _uiInvoke = uiInvoke ?? (action => Dispatcher.UIThread.Post(action));

            // Default monitor-plane bookmarks for the demo switches' tool windows.
            Switches.Add(new SwitchEndpoint("Switch-A", "127.0.0.1", 4001));
            Switches.Add(new SwitchEndpoint("Switch-B", "127.0.0.1", 4002));
            Switches.Add(new SwitchEndpoint("Switch-C", "127.0.0.1", 4003));

            // Restore remembered node positions so the layout survives restarts.
            AppPreferences prefs = PreferencesStore.Load();
            if (prefs.NodePositions != null)
            {
                for (int i = 0; i < prefs.NodePositions.Count; i++)
                {
                    NodePositionSetting p = prefs.NodePositions[i];
                    _savedPositions[p.Key] = new Point(p.X, p.Y);
                }
            }

            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>
        /// Resolves the monitor endpoint for a node's tool windows by matching the node's name to a bookmark,
        /// or null when none matches (the node has no known monitor endpoint).
        /// </summary>
        /// <param name="node">
        /// The node whose monitor endpoint is wanted.
        /// </param>
        /// <returns>
        /// The matching endpoint, or null.
        /// </returns>
        public SwitchEndpoint? EndpointForNode(MapNodeViewModel node)
        {
            if (node == null)
            {
                return null;
            }

            for (int i = 0; i < Switches.Count; i++)
            {
                if (string.Equals(Switches[i].Name, node.Name, StringComparison.Ordinal))
                {
                    return Switches[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Clears the map and starts NMC discovery from the seed.
        /// </summary>
        [RelayCommand]
        private void Discover()
        {
            Clear();

            NmcDiscoveryOptions options = new NmcDiscoveryOptions
            {
                Host = SeedHost.Trim(),
                Port = SeedPort,
                LocalAddress = "99990002",
                Username = Username,
                Password = Password,
                MaxDepth = 8
            };

            NmcDiscovery discovery = new NmcDiscovery(options);
            discovery.NodeChanged += OnNmcNodeChanged;
            _discovery = discovery;

            IsDiscovering = true;
            Status = "Discovering from " + SeedHost + ":" + SeedPort + " (" + SeedMgmtAddr + ")";

            if (!discovery.Start(SeedMgmtAddr.Trim()))
            {
                IsDiscovering = false;
                Status = "Failed to connect to " + SeedHost + ":" + SeedPort;
            }
        }

        /// <summary>
        /// Tears down discovery and clears the map.
        /// </summary>
        [RelayCommand]
        private void Clear()
        {
            NmcDiscovery? discovery = _discovery;
            _discovery = null;
            if (discovery != null)
            {
                discovery.NodeChanged -= OnNmcNodeChanged;
                discovery.Dispose();
            }

            _nodesByKey.Clear();
            _nodeList.Clear();
            _edgesByKey.Clear();
            _edgeList.Clear();
            _nmcByKey.Clear();
            Nodes.Clear();
            Connections.Clear();
            IsDiscovering = false;
            Status = "Idle";
        }

        /// <summary>
        /// Persists the current node positions (called when the map window closes). Read-modify-write so the
        /// theme and other settings are preserved.
        /// </summary>
        public void SavePositions()
        {
            AppPreferences prefs = PreferencesStore.Load();
            prefs.NodePositions = new List<NodePositionSetting>();
            for (int i = 0; i < _nodeList.Count; i++)
            {
                MapNodeViewModel node = _nodeList[i];
                prefs.NodePositions.Add(new NodePositionSetting
                {
                    Key = node.Key,
                    X = node.Location.X,
                    Y = node.Location.Y
                });
            }

            PreferencesStore.Save(prefs);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            Clear();
        }

        // A node's NMC state changed: refresh its VM, its neighbour edges, and any routing, on the UI thread.
        private void OnNmcNodeChanged(NmcNode node)
        {
            _uiInvoke(() =>
            {
                _nmcByKey[node.Key] = node;

                MapNodeViewModel vm = UpsertNode(node.Key, node.DisplayName, node.Depth);
                if (node.Info != null)
                {
                    vm.Name = node.Info.Name.Length > 0 ? node.Info.Name : node.Key;
                    vm.Dnic = node.Info.Dnic;
                }
                vm.Reachable = node.Reachable;
                vm.HasMonitor = true; // an NMC-reachable node; drives colouring (reachable vs down)
                vm.StatusText = BuildNodeStatus(node);

                for (int i = 0; i < node.Neighbours.Count; i++)
                {
                    NmcNeighbour nb = node.Neighbours[i];
                    string nbKey = nb.MgmtAddr.Length > 0 ? nb.MgmtAddr : "name:" + nb.Name;

                    // A neighbour with no management address is a stub the walk cannot enter.
                    MapNodeViewModel nbVm = UpsertNode(nbKey, nb.Name, node.Depth + 1);
                    if (nb.MgmtAddr.Length == 0)
                    {
                        nbVm.HasMonitor = false;
                    }

                    UpsertEdge(node.Key, nbKey, nb.Up);
                }

                RefreshAllRouting();
                Relayout();
                RecolorAll();
                UpdateDiscoveryStatus();
            });
        }

        private MapNodeViewModel UpsertNode(string key, string name, int depth)
        {
            if (_nodesByKey.TryGetValue(key, out MapNodeViewModel? existing))
            {
                if (depth < existing.Depth)
                {
                    existing.Depth = depth;
                }
                if ((existing.Name.Length == 0 || existing.Name == existing.Key) && name.Length > 0)
                {
                    existing.Name = name;
                }
                return existing;
            }

            MapNodeViewModel node = new MapNodeViewModel(key, name, string.Empty, 0) { Depth = depth };
            node.Fill = BrushUnknown;

            // Restore a remembered position if we have one, otherwise the radial layout will place it.
            if (_savedPositions.TryGetValue(key, out Point saved))
            {
                node.Location = saved;
            }

            _nodesByKey[key] = node;
            _nodeList.Add(node);
            Nodes.Add(node);
            return node;
        }

        private void UpsertEdge(string a, string b, bool up)
        {
            string key = string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
            if (_edgesByKey.TryGetValue(key, out MapConnectionViewModel? existing))
            {
                existing.Up = up;
                existing.Stroke = up ? BrushLinkUp : BrushLinkDown;
                return;
            }

            if (!_nodesByKey.TryGetValue(a, out MapNodeViewModel? from)
                || !_nodesByKey.TryGetValue(b, out MapNodeViewModel? to))
            {
                return;
            }

            MapConnectionViewModel edge = new MapConnectionViewModel(this, from, to) { Up = up };
            edge.Stroke = up ? BrushLinkUp : BrushLinkDown;
            from.Connector.IsConnected = true;
            to.Connector.IsConnected = true;

            _edgesByKey[key] = edge;
            _edgeList.Add(edge);
            Connections.Add(edge);
        }

        // Rebuilds the routing text for every edge from the two switches' routing tables and neighbour lists.
        private void RefreshAllRouting()
        {
            for (int i = 0; i < _edgeList.Count; i++)
            {
                MapConnectionViewModel edge = _edgeList[i];
                string fromName = NodeName(edge.FromKey);
                string toName = NodeName(edge.ToKey);
                edge.TrunkLabel = fromName + " ↔ " + toName;

                StringBuilder sb = new StringBuilder();
                AppendDirection(sb, edge.FromKey, edge.ToKey, fromName, toName);
                AppendDirection(sb, edge.ToKey, edge.FromKey, toName, fromName);
                edge.RoutingText = sb.Length > 0 ? sb.ToString() : "(no routing information)";
            }
        }

        // Appends "from -> to" with the routes 'from' forwards across the trunk toward 'to'.
        private void AppendDirection(StringBuilder sb, string fromKey, string toKey, string fromName, string toName)
        {
            if (!_nmcByKey.TryGetValue(fromKey, out NmcNode? from))
            {
                return;
            }

            // Find the trunk port on 'from' that reaches 'to', then list the routes that exit that port.
            int via = -1;
            for (int i = 0; i < from.Neighbours.Count; i++)
            {
                NmcNeighbour nb = from.Neighbours[i];
                string nbKey = nb.MgmtAddr.Length > 0 ? nb.MgmtAddr : "name:" + nb.Name;
                if (nbKey == toKey)
                {
                    via = nb.Via;
                    break;
                }
            }

            if (via < 0)
            {
                return;
            }

            sb.Append(fromName).Append(" → ").Append(toName).Append(" (port ").Append(via).Append("):\n");
            bool any = false;
            for (int i = 0; i < from.Routes.Count; i++)
            {
                NmcRoute r = from.Routes[i];
                if (r.Port != via)
                {
                    continue;
                }
                any = true;
                sb.Append("    ").Append(FormatRoute(r)).Append('\n');
            }

            if (!any)
            {
                sb.Append("    (no routes over this trunk)\n");
            }
        }

        private static string FormatRoute(NmcRoute r)
        {
            if (r.Type == "default")
            {
                return "default";
            }
            if (r.Type == "prefix")
            {
                return "prefix " + r.Address + "/" + r.PrefixLen;
            }
            return r.Type + " " + r.Address;
        }

        private string NodeName(string key)
        {
            return _nodesByKey.TryGetValue(key, out MapNodeViewModel? vm) && vm.Name.Length > 0 ? vm.Name : key;
        }

        private static string BuildNodeStatus(NmcNode node)
        {
            if (!node.Reachable)
            {
                return "unreachable";
            }
            if (node.Stats != null)
            {
                return node.Stats.Stitches + " circ · up " + FormatUptime(node.Stats.UptimeSeconds);
            }
            return "up";
        }

        // Re-applies node/link colours from the (new) active palette, on the UI thread.
        private void OnThemeChanged()
        {
            _uiInvoke(() =>
            {
                RecolorAll();
                for (int i = 0; i < _edgeList.Count; i++)
                {
                    MapConnectionViewModel edge = _edgeList[i];
                    edge.Stroke = edge.Up ? BrushLinkUp : BrushLinkDown;
                }
            });
        }

        // Places un-pinned nodes on concentric rings by discovery depth. Nodes with a remembered position keep
        // it; edges follow their nodes' connector anchors automatically, so no edge maths are needed here.
        private void Relayout()
        {
            List<List<MapNodeViewModel>> byDepth = new List<List<MapNodeViewModel>>();
            for (int i = 0; i < _nodeList.Count; i++)
            {
                int d = _nodeList[i].Depth;
                while (byDepth.Count <= d)
                {
                    byDepth.Add(new List<MapNodeViewModel>());
                }
                byDepth[d].Add(_nodeList[i]);
            }

            double cx = CanvasWidth / 2;
            double cy = CanvasHeight / 2;

            for (int d = 0; d < byDepth.Count; d++)
            {
                List<MapNodeViewModel> ring = byDepth[d];
                if (d == 0)
                {
                    for (int i = 0; i < ring.Count; i++)
                    {
                        if (!_savedPositions.ContainsKey(ring[i].Key))
                        {
                            ring[i].Location = new Point(cx, cy);
                        }
                    }
                    continue;
                }

                double radius = 120 + (d - 1) * 150;
                for (int i = 0; i < ring.Count; i++)
                {
                    if (_savedPositions.ContainsKey(ring[i].Key))
                    {
                        continue;
                    }

                    double angle = (2 * Math.PI * i) / ring.Count - Math.PI / 2;
                    double x = cx + radius * Math.Cos(angle);
                    double y = cy + radius * Math.Sin(angle);
                    ring[i].Location = new Point(x, y);
                }
            }
        }

        private void RecolorAll()
        {
            for (int i = 0; i < _nodeList.Count; i++)
            {
                RecolorNode(_nodeList[i]);
            }
        }

        // Green = reachable with all its trunks up; orange = reachable but a trunk is down; red = an
        // NMC-reachable node that is not answering; grey = a stub peer with no management address to walk.
        private void RecolorNode(MapNodeViewModel node)
        {
            if (!node.HasMonitor)
            {
                node.Fill = BrushUnknown;
                return;
            }

            if (!node.Reachable)
            {
                node.Fill = BrushDown;
                return;
            }

            bool anyDown = false;
            for (int i = 0; i < _edgeList.Count; i++)
            {
                MapConnectionViewModel edge = _edgeList[i];
                if ((edge.FromKey == node.Key || edge.ToKey == node.Key) && !edge.Up)
                {
                    anyDown = true;
                    break;
                }
            }

            node.Fill = anyDown ? BrushDegraded : BrushReachable;
        }

        private void UpdateDiscoveryStatus()
        {
            int reachable = 0;
            for (int i = 0; i < _nodeList.Count; i++)
            {
                if (_nodeList[i].Reachable)
                {
                    reachable++;
                }
            }

            Status = "Nodes: " + _nodeList.Count + " · reachable: " + reachable;
        }

        private static string FormatUptime(long seconds)
        {
            long hours = seconds / 3600;
            long minutes = (seconds % 3600) / 60;
            long secs = seconds % 60;
            if (hours > 0)
            {
                return hours + "h " + minutes + "m";
            }
            if (minutes > 0)
            {
                return minutes + "m " + secs + "s";
            }
            return secs + "s";
        }
    }
}
