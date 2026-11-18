//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Common;
using RetroIO.Protocols.X25.PacketLayer;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Switch;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// The one routing screen: this node's own network, every route in the order the lookup tries them, an
/// add/edit dialog, and a box that shows where a number would go.
/// </summary>
/// <remarks>
/// <para>
/// The screen edits a working copy of the routing section of the configuration (the written form of the one
/// routing table, <see cref="X121NetworkConfig"/>). The old route list (<c>RoutingTable</c>) and
/// <c>DefaultGateway</c> are read through <see cref="RoutingConfigConverter"/>, the same conversion the engine
/// uses, so the screen shows what the engine does with them. Every list, preview and test is made by building
/// a real <see cref="X121RoutingTable"/> from the working copy and asking it - the screen has no lookup of its
/// own. Port states come from the running engine's router (<c>X25Router.PortStatusOf</c>).
/// </para>
/// <para>
/// Save writes the working copy into the configuration in the new form (<c>OwnDnics</c>,
/// <c>InternationalPrefix</c>, <c>Routes</c>), empties the old list and <c>DefaultGateway</c> (they are now
/// in <c>Routes</c>), reloads the engine's table, and writes the routing section of the file the app was
/// started on, leaving the rest of that file as it was.
/// </para>
/// </remarks>
public partial class RoutingViewModel : ViewModelWithLogger
{
    private readonly RuntimeConfig _config;
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;
    private readonly List<string> _ownDnics = new List<string>();
    private List<X121RouteConfig> _routes = new List<X121RouteConfig>();
    private Avalonia.Controls.Window? _ownerWindow;
    private bool _loading;

    [ObservableProperty]
    private string _ownDnicsText = string.Empty;

    [ObservableProperty]
    private string _internationalPrefix = string.Empty;

    [ObservableProperty]
    private string _ownNetworkText = string.Empty;

    [ObservableProperty]
    private string _ownNetworkError = string.Empty;

    [ObservableProperty]
    private RouteRow? _selectedRoute;

    [ObservableProperty]
    private string _testNumber = string.Empty;

    [ObservableProperty]
    private string _testArrivalPort = PlacedHere;

    [ObservableProperty]
    private string _testResult = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// The switch-wide call limit as typed: digits, 0 = no limit. Empty is read as 0.
    /// </summary>
    [ObservableProperty]
    private string _maxCallsText = "0";

    [ObservableProperty]
    private string _maxCallsError = string.Empty;

    [ObservableProperty]
    private string _switchSettingsText = string.Empty;

    [ObservableProperty]
    private bool _hasSwitchFile;

    /// <summary>
    /// The arrival-port choice that means "a call placed on this node".
    /// </summary>
    public const string PlacedHere = "(placed on this node)";

    /// <summary>
    /// The routes, in the order the lookup tries them.
    /// </summary>
    public ObservableCollection<RouteRow> Routes { get; } = new ObservableCollection<RouteRow>();

    /// <summary>
    /// The ports a route may use: the configured lines, and <c>local</c> (this node).
    /// </summary>
    public ObservableCollection<string> PortNames { get; } = new ObservableCollection<string>();

    /// <summary>
    /// The choices for the port a test call arrives on: <see cref="PlacedHere"/> and every line.
    /// </summary>
    public ObservableCollection<string> ArrivalPorts { get; } = new ObservableCollection<string>();

    /// <summary>
    /// Every line with its channel count: the built-in switch's ports (from its .ini file) and this app's own lines.
    /// </summary>
    public ObservableCollection<LineRow> Lines { get; } = new ObservableCollection<LineRow>();

    /// <summary>
    /// The file Save writes; null when the app was started without one. Set from the command line by the app.
    /// </summary>
    public string? ConfigFilePath { get; set; }

    /// <summary>
    /// The built-in switch's .ini file (the <c>EmbeddedSwitch</c> section's <c>ConfigFile</c>): where the max calls
    /// setting and the switch ports' channel counts are read from and saved to. Null when the app names none. Set
    /// from the configuration by the app; a test sets its own and then calls <see cref="LoadFromConfig"/>.
    /// </summary>
    public string? SwitchFilePath { get; set; }

    /// <summary>
    /// Where Save writes, in words.
    /// </summary>
    public string SaveTargetText => ConfigFilePath == null
        ? "Save changes the running engine only: the app was started without a configuration file. Use Save in the Configuration window to write a file."
        : "Save writes the routing part of " + Path.GetFileName(ConfigFilePath) + " and reloads the engine's routes.";

    /// <summary>
    /// Shows the add/edit dialog and says whether OK was pressed. The view sets it; a test sets its own.
    /// </summary>
    public Func<RouteEditDialogViewModel, Task<bool>>? ShowEditor { get; set; }

    /// <summary>
    /// Creates the screen and reads the routing section of <paramref name="config"/>.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model.
    /// </param>
    /// <param name="config">
    /// The shared runtime configuration (the one the engine was started from).
    /// </param>
    /// <param name="runtimeService">
    /// The engine, for the port states, the hit counts and the reload after Save.
    /// </param>
    /// <param name="dialogService">
    /// For the delete confirmation and errors.
    /// </param>
    /// <param name="switchOptions">
    /// The built-in switch setting; its .ini file holds the max calls setting and the switch ports.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an argument is null.
    /// </exception>
    public RoutingViewModel(
        ILogger<RoutingViewModel> logger,
        RuntimeConfig config,
        IRuntimeService runtimeService,
        IDialogService dialogService,
        EmbeddedSwitchOptions switchOptions) : base(logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        if (switchOptions == null)
        {
            throw new ArgumentNullException(nameof(switchOptions));
        }

        ConfigFilePath = App.ConfigFilePath;
        SwitchFilePath = switchOptions.ConfigFile.Length > 0 ? switchOptions.ConfigFile : null;
        LoadFromConfig();
    }

    /// <summary>
    /// Sets the window the add/edit dialog opens over.
    /// </summary>
    /// <param name="ownerWindow">
    /// The window.
    /// </param>
    public void SetOwnerWindow(Avalonia.Controls.Window ownerWindow)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
        ShowEditor ??= ShowEditorWindow;
    }

    private async Task<bool> ShowEditorWindow(RouteEditDialogViewModel vm)
    {
        if (_ownerWindow == null)
        {
            return false;
        }

        Views.RouteEditDialog dialog = new Views.RouteEditDialog { DataContext = vm };
        return await dialog.ShowDialog<bool>(_ownerWindow);
    }

    /// <summary>
    /// The routes of the working copy, in the order they are written (not the lookup order).
    /// </summary>
    public IReadOnlyList<X121RouteConfig> WorkingRoutes => _routes;

    /// <summary>
    /// Reads the routing section of the configuration again, dropping unsaved changes.
    /// </summary>
    public void LoadFromConfig()
    {
        _loading = true;
        try
        {
            X121NetworkConfig net = RoutingConfigConverter.ToNetworkConfig(_config.Routing);
            _ownDnics.Clear();
            _ownDnics.AddRange(net.OwnDnics);
            _routes = CopyAll(net.Routes);
            OwnDnicsText = string.Join(", ", _ownDnics);
            InternationalPrefix = net.InternationalPrefix ?? string.Empty;
            OwnNetworkError = string.Empty;

            PortNames.Clear();
            ArrivalPorts.Clear();
            ArrivalPorts.Add(PlacedHere);
            AddLines(PortNames);
            AddLines(ArrivalPorts);
            PortNames.Add(X121RoutingTable.LocalPortName);
            TestArrivalPort = PlacedHere;
        }
        finally
        {
            _loading = false;
        }

        RebuildRows();
        UpdateOwnNetworkText();
        string switchProblem = LoadLinesAndSwitchSettings();
        HasUnsavedChanges = false;
        StatusText = Routes.Count + " route(s)" + (switchProblem.Length > 0 ? ". " + switchProblem : string.Empty);
    }

    // The Lines list and the max calls field: the built-in switch's ports and [switch] max_calls from its .ini
    // file, then this app's own lines. Returns why the switch file could not be read, or empty.
    private string LoadLinesAndSwitchSettings()
    {
        _loading = true;
        string problem = string.Empty;
        try
        {
            Lines.Clear();
            MaxCallsText = "0";
            MaxCallsError = string.Empty;
            HasSwitchFile = false;
            if (SwitchFilePath != null)
            {
                try
                {
                    SwitchConfig sw = SwitchConfigLoader.LoadFile(SwitchFilePath);
                    HasSwitchFile = true;
                    MaxCallsText = sw.MaxCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    for (int i = 0; i < sw.Ports.Count; i++)
                    {
                        PortConfig pc = sw.Ports[i];
                        string name = pc.Name.Length > 0 ? pc.Name : "port." + pc.Id;
                        string kind = (pc.Type == PortType.Trunk ? "trunk" : "access") + ", " + TransportWord(pc.Transport);
                        Lines.Add(new LineRow(this, LineSource.SwitchPort, i, "port." + pc.Id, name, kind, pc.LciBase,
                            pc.LciCountIsSet ? pc.LciCount : (ushort?)null, editable: true));
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    problem = "The built-in switch's file " + Path.GetFileName(SwitchFilePath) + " could not be read: " + ex.Message;
                }
            }

            X25RoutingConfig r = _config.Routing;
            for (int i = 0; i < r.XotInterfaces.Count; i++)
            {
                XotInterfaceConfig x = r.XotInterfaces[i];
                Lines.Add(new LineRow(this, LineSource.AppXot, i, x.Name, x.Name, "this app, XOT", x.LciBase,
                    x.LciCountIsSet ? x.LciCount : (ushort?)null, editable: true));
            }

            // Serial and TCP LAPB lines have no channel setting: their packet layer always gets the default range.
            for (int i = 0; i < r.SerialInterfaces.Count; i++)
            {
                Lines.Add(new LineRow(this, LineSource.AppFixed, i, r.SerialInterfaces[i].Name, r.SerialInterfaces[i].Name,
                    "this app, serial", 1, null, editable: false));
            }

            for (int i = 0; i < r.TcpLapbInterfaces.Count; i++)
            {
                Lines.Add(new LineRow(this, LineSource.AppFixed, i, r.TcpLapbInterfaces[i].Name, r.TcpLapbInterfaces[i].Name,
                    "this app, TCP LAPB", 1, null, editable: false));
            }

            SwitchSettingsText = HasSwitchFile
                ? "Saved to the built-in switch's file " + Path.GetFileName(SwitchFilePath) + "; the switch reads it when the app starts."
                : "No built-in switch file is named, so there is no switch-wide limit to set here.";
        }
        finally
        {
            _loading = false;
        }

        return problem;
    }

    private static string TransportWord(PortTransport transport)
    {
        switch (transport)
        {
            case PortTransport.Xot: return "XOT";
            case PortTransport.HdlcTcp: return "HDLC over TCP";
            case PortTransport.HdlcSerial: return "serial";
            case PortTransport.HdlcPpp: return "HDLC (RFC 1662) over TCP";
            default: return transport.ToString();
        }
    }

    partial void OnMaxCallsTextChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        MaxCallsError = TryReadMaxCalls(value, out _, out string error) ? string.Empty : error;
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Reads the max calls field: digits only, empty is 0, 0 is no limit.
    /// </summary>
    /// <param name="text">
    /// The field.
    /// </param>
    /// <param name="value">
    /// The number.
    /// </param>
    /// <param name="error">
    /// Why it was refused, in words.
    /// </param>
    /// <returns>
    /// True when it is a number of calls.
    /// </returns>
    public static bool TryReadMaxCalls(string text, out int value, out string error)
    {
        string t = (text ?? string.Empty).Trim();
        value = 0;
        error = string.Empty;
        if (t.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] < '0' || t[i] > '9')
            {
                error = "Max calls: digits only (0 = no limit).";
                return false;
            }
        }

        if (!int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            error = "Max calls: the number is too large.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Called by a line when its channel count is edited.
    /// </summary>
    internal void LineEdited()
    {
        if (!_loading)
        {
            HasUnsavedChanges = true;
        }
    }

    private string FirstLineError()
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Error.Length > 0)
            {
                return Lines[i].Error;
            }
        }

        return string.Empty;
    }

    private void AddLines(ObservableCollection<string> into)
    {
        X25RoutingConfig r = _config.Routing;
        for (int i = 0; i < r.SerialInterfaces.Count; i++)
        {
            AddUnique(into, r.SerialInterfaces[i].Name);
        }

        for (int i = 0; i < r.XotInterfaces.Count; i++)
        {
            AddUnique(into, r.XotInterfaces[i].Name);
        }

        for (int i = 0; i < r.TcpLapbInterfaces.Count; i++)
        {
            AddUnique(into, r.TcpLapbInterfaces[i].Name);
        }
    }

    private static void AddUnique(ObservableCollection<string> into, string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !into.Contains(name))
        {
            into.Add(name);
        }
    }

    // A real table built from a working copy; every list, preview and test asks one of these.
    private X121RoutingTable BuildTable(IReadOnlyList<string> ownDnics, string prefix, List<X121RouteConfig> routes)
    {
        X121RoutingTable table = new X121RoutingTable();
        Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        X121NetworkConfig net = new X121NetworkConfig { InternationalPrefix = prefix };
        net.OwnDnics.AddRange(ownDnics);
        net.Routes.AddRange(routes);
        X121TableBuilder.Fill(table, net, name =>
        {
            if (string.Equals(name, X121RoutingTable.LocalPortName, StringComparison.OrdinalIgnoreCase))
            {
                return X121RoutingTable.LocalPort;
            }

            if (!ids.TryGetValue(name, out int id))
            {
                id = ids.Count;
                ids[name] = id;
                table.SetPortName(id, name);
            }

            return id;
        });
        return table;
    }

    private X121PortStatus PortState(X121RoutingTable table, int id)
    {
        string name = table.GetPortName(id);
        return _runtimeService.IsRunning ? _runtimeService.Runtime.Router.PortStatusOf(name) : X121PortStatus.Missing;
    }

    private long HitsOf(string routeName)
    {
        if (!_runtimeService.IsRunning)
        {
            return 0;
        }

        IReadOnlyList<X121Route> live = _runtimeService.Runtime.Router.Table.Routes;
        for (int i = 0; i < live.Count; i++)
        {
            if (string.Equals(live[i].Name, routeName, StringComparison.Ordinal))
            {
                return live[i].Hits;
            }
        }

        return 0;
    }

    private void RebuildRows()
    {
        string? keep = SelectedRoute?.Name;
        Routes.Clear();
        X121RoutingTable table;
        try
        {
            table = BuildTable(_ownDnics, InternationalPrefix, _routes);
        }
        catch (InvalidDataException ex)
        {
            StatusText = "The routes cannot be used: " + ex.Message;
            return;
        }

        List<X121Route> order = table.RoutesInLookupOrder();
        for (int i = 0; i < order.Count; i++)
        {
            X121Route r = order[i];
            int at = -1;
            for (int j = 0; j < table.Routes.Count; j++)
            {
                if (ReferenceEquals(table.Routes[j], r))
                {
                    at = j;
                    break;
                }
            }

            RouteRow row = new RouteRow(this, at, i + 1, r.Type, r.Name, X121RouteText.Describe(r),
                X121RouteText.Target(r, table), r.Note, HitsOf(r.Name), r.Enabled);
            Routes.Add(row);
            if (keep != null && string.Equals(keep, r.Name, StringComparison.Ordinal))
            {
                SelectedRoute = row;
            }
        }
    }

    partial void OnOwnDnicsTextChanged(string value) => OwnNetworkEdited();

    partial void OnInternationalPrefixChanged(string value) => OwnNetworkEdited();

    private void OwnNetworkEdited()
    {
        if (_loading)
        {
            return;
        }

        List<string> dnics = new List<string>();
        string[] parts = OwnDnicsText.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!X121RouteRules.CheckDnic(parts[i], out string error))
            {
                OwnNetworkError = "Own network " + parts[i] + ": " + error + ".";
                return;
            }

            dnics.Add(parts[i]);
        }

        string prefix = InternationalPrefix.Trim();
        if (!X121RouteRules.CheckInternationalPrefix(prefix, out string prefixError))
        {
            OwnNetworkError = "International prefix: " + prefixError + ".";
            return;
        }

        OwnNetworkError = string.Empty;
        _ownDnics.Clear();
        _ownDnics.AddRange(dnics);
        HasUnsavedChanges = true;
        UpdateOwnNetworkText();
        RebuildRows();
    }

    private void UpdateOwnNetworkText()
    {
        if (_ownDnics.Count == 0)
        {
            OwnNetworkText = "No own network set: every number is taken as it is dialled, and an exact route matches only the number as written.";
            return;
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < _ownDnics.Count; i++)
        {
            sb.Append(i == 0 ? "This node is in network " : ", also ");
            sb.Append(_ownDnics[i]).Append(X121RouteText.CountrySuffix(_ownDnics[i]));
        }

        sb.Append(". ");
        string prefix = InternationalPrefix.Trim();
        string first = _ownDnics[0].Length > 4 ? _ownDnics[0].Substring(0, 4) : _ownDnics[0];
        if (prefix.Length == 0)
        {
            sb.Append("No international prefix: numbers are taken as dialled; a number route also matches ")
              .Append(first).Append(" in front of it.");
        }
        else
        {
            sb.Append("A number that starts with ").Append(prefix).Append(" is international and the ").Append(prefix)
              .Append(" is taken off; any other number is local and gets ").Append(first).Append(" put in front (X.121 7.6, 7.8).");
        }

        OwnNetworkText = sb.ToString();
    }

    /// <summary>
    /// Where a number would go with the working copy, with <paramref name="replacement"/> put in place of the
    /// route at <paramref name="index"/> (or added, when <paramref name="index"/> is -1).
    /// </summary>
    /// <param name="replacement">
    /// The route being edited.
    /// </param>
    /// <param name="index">
    /// Its place in the working copy, or -1 for a new route.
    /// </param>
    /// <param name="number">
    /// The called number.
    /// </param>
    /// <returns>
    /// The lookup's answer.
    /// </returns>
    public X121RouteResult Preview(X121RouteConfig replacement, int index, string number)
    {
        List<X121RouteConfig> routes = new List<X121RouteConfig>(_routes);
        if (index >= 0 && index < routes.Count)
        {
            routes[index] = replacement;
        }
        else
        {
            routes.Add(replacement);
        }

        X121RoutingTable table = BuildTable(_ownDnics, InternationalPrefix, routes);
        return table.Resolve(number, -1, id => PortState(table, id));
    }

    [RelayCommand]
    private void TestAddress()
    {
        string number = TestNumber.Trim();
        if (number.Length == 0)
        {
            TestResult = "Type a number to test.";
            return;
        }

        X121RoutingTable table;
        try
        {
            table = BuildTable(_ownDnics, InternationalPrefix, _routes);
        }
        catch (InvalidDataException ex)
        {
            TestResult = "The routes cannot be used: " + ex.Message;
            return;
        }

        int inPort = -1;
        if (TestArrivalPort != PlacedHere && !table.TryFindPort(TestArrivalPort, out inPort))
        {
            // A line no route names has no number in the table yet; it cannot be chosen, so nothing is skipped.
            inPort = -1;
        }

        X121RouteResult r = table.Resolve(number, inPort, id => PortState(table, id));
        StringBuilder sb = new StringBuilder();
        sb.Append(r.Explanation);
        if (r.InternationalAddress.Length > 0 && r.InternationalAddress != number)
        {
            sb.Append("\nThe full international number is ").Append(r.InternationalAddress).Append('.');
        }

        if (r.IsRouted && r.CalledAddress.Length > 0 && r.CalledAddress != number)
        {
            sb.Append("\nThe number sent on is ").Append(r.CalledAddress).Append('.');
        }

        if (r.Route != null && r.Route.Type != X121RouteType.Hunt)
        {
            sb.Append("\nOrder tried: ").Append(X121RouteText.Target(r.Route, table)).Append('.');
        }

        if (HasUnsavedChanges)
        {
            sb.Append("\n(Tested on the routes shown here, which are not saved yet.)");
        }

        TestResult = sb.ToString();
    }

    private List<string> NamesExcept(int index)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < _routes.Count; i++)
        {
            if (i != index)
            {
                names.Add(_routes[i].Name);
            }
        }

        return names;
    }

    [RelayCommand]
    private async Task AddRoute()
    {
        RouteEditDialogViewModel vm = new RouteEditDialogViewModel(Logger, null, true,
            (rc, number) => Preview(rc, -1, number), PortNames, NamesExcept(-1));
        if (ShowEditor == null || !await ShowEditor(vm) || vm.Result == null)
        {
            return;
        }

        _routes.Add(vm.Result);
        HasUnsavedChanges = true;
        RebuildRows();
        SelectByName(vm.Result.Name);
        StatusText = "Added route '" + vm.Result.Name + "' (not saved yet)";
    }

    [RelayCommand]
    private async Task EditRoute()
    {
        RouteRow? row = SelectedRoute;
        if (row == null || row.Index < 0 || row.Index >= _routes.Count)
        {
            StatusText = "Pick a route to edit.";
            return;
        }

        int index = row.Index;
        RouteEditDialogViewModel vm = new RouteEditDialogViewModel(Logger, _routes[index], false,
            (rc, number) => Preview(rc, index, number), PortNames, NamesExcept(index));
        if (ShowEditor == null || !await ShowEditor(vm) || vm.Result == null)
        {
            return;
        }

        _routes[index] = vm.Result;
        HasUnsavedChanges = true;
        RebuildRows();
        SelectByName(vm.Result.Name);
        StatusText = "Changed route '" + vm.Result.Name + "' (not saved yet)";
    }

    [RelayCommand]
    private async Task DeleteRoute()
    {
        RouteRow? row = SelectedRoute;
        if (row == null || row.Index < 0 || row.Index >= _routes.Count)
        {
            StatusText = "Pick a route to delete.";
            return;
        }

        bool sure = await _dialogService.ShowConfirmation("Delete route",
            "Delete the route '" + row.Name + "'?\n\n" + row.Match + "\n" + row.Target);
        if (!sure)
        {
            return;
        }

        _routes.RemoveAt(row.Index);
        HasUnsavedChanges = true;
        RebuildRows();
        StatusText = "Deleted route '" + row.Name + "' (not saved yet)";
    }

    /// <summary>
    /// Turns a route on or off in the working copy; the list's switch calls this.
    /// </summary>
    /// <param name="index">
    /// The route's place in the working copy.
    /// </param>
    /// <param name="enabled">
    /// The new state.
    /// </param>
    internal void SetEnabled(int index, bool enabled)
    {
        if (index < 0 || index >= _routes.Count || _routes[index].Enabled == enabled)
        {
            return;
        }

        _routes[index].Enabled = enabled;
        HasUnsavedChanges = true;
        StatusText = "Route '" + _routes[index].Name + "' " + (enabled ? "turned on" : "turned off") + " (not saved yet)";
    }

    private void SelectByName(string name)
    {
        for (int i = 0; i < Routes.Count; i++)
        {
            if (string.Equals(Routes[i].Name, name, StringComparison.Ordinal))
            {
                SelectedRoute = Routes[i];
                return;
            }
        }
    }

    [RelayCommand]
    private void RefreshHits()
    {
        RebuildRows();
        StatusText = "Hit counts read from the engine";
    }

    [RelayCommand]
    private void Refresh() => LoadFromConfig();

    /// <summary>
    /// Writes the working copy into the shared configuration in the new form. The Configuration window calls
    /// this before it writes a whole file, so its Save carries the routes too.
    /// </summary>
    public void CommitToConfig()
    {
        X25RoutingConfig r = _config.Routing;
        r.OwnDnics = new List<string>(_ownDnics);
        r.InternationalPrefix = InternationalPrefix.Trim();
        r.Routes = CopyAll(_routes);

        // The old list and DefaultGateway were converted into Routes when the screen read them; keeping them
        // would add each route a second time on the next start.
        r.RoutingTable.Clear();
        r.DefaultGateway = null;

        // The channel counts typed for this app's XOT lines (a line with a typing error keeps what it had).
        for (int i = 0; i < Lines.Count; i++)
        {
            LineRow line = Lines[i];
            if (line.Source == LineSource.AppXot && line.Error.Length == 0 && line.Index < r.XotInterfaces.Count)
            {
                r.XotInterfaces[line.Index].LciCountSetting = line.TryRead(out ushort? count, out _) ? count : r.XotInterfaces[line.Index].LciCountSetting;
            }
        }
    }

    /// <summary>
    /// Writes the max calls setting and the switch ports' channel counts into the built-in switch's .ini file and
    /// leaves every other line of it (comments too) as it was. A port whose count is empty loses its
    /// <c>lci_count</c> line, so it gets the default, every channel up to 4095. Max calls 0 is written as 0 when the
    /// file has the key, and not added when it does not (absent means the same: no limit).
    /// </summary>
    /// <param name="path">
    /// The switch file.
    /// </param>
    /// <param name="maxCalls">
    /// The limit; 0 = none.
    /// </param>
    /// <param name="lines">
    /// The screen's lines; only the switch ports are written.
    /// </param>
    public static void WriteSwitchSettingsToFile(string path, int maxCalls, IReadOnlyList<LineRow> lines)
    {
        List<string> text = new List<string>(File.ReadAllLines(path));
        bool hasKey = IniConfig.Parse(text, int.MaxValue).Get("switch", "max_calls") != null;
        if (maxCalls > 0 || hasKey)
        {
            IniConfig.SetValue(text, "switch", "max_calls", maxCalls.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < lines.Count; i++)
        {
            LineRow line = lines[i];
            if (line.Source != LineSource.SwitchPort || !line.TryRead(out ushort? count, out _))
            {
                continue;
            }

            IniConfig.SetValue(text, line.Section, "lci_count",
                count.HasValue ? count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }

        File.WriteAllLines(path, text);
    }

    [RelayCommand]
    private void Save()
    {
        if (OwnNetworkError.Length > 0)
        {
            StatusText = "Not saved: " + OwnNetworkError;
            return;
        }

        if (!TryReadMaxCalls(MaxCallsText, out int maxCalls, out string maxCallsError))
        {
            StatusText = "Not saved: " + maxCallsError;
            return;
        }

        string lineError = FirstLineError();
        if (lineError.Length > 0)
        {
            StatusText = "Not saved: " + lineError;
            return;
        }

        try
        {
            // Proves the routes load before anything is changed.
            BuildTable(_ownDnics, InternationalPrefix, _routes);
        }
        catch (InvalidDataException ex)
        {
            StatusText = "Not saved: " + ex.Message;
            return;
        }

        CommitToConfig();
        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.ReloadRouting();
        }

        string where = "the running engine";
        if (ConfigFilePath != null)
        {
            try
            {
                WriteRoutingToFile(ConfigFilePath, _config.Routing);
                where = Path.GetFileName(ConfigFilePath) + " and the running engine";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                StatusText = "The engine has the new routes, but the file was not written: " + ex.Message;
                _dialogService.ShowError("Routes not written to the file", ex.Message);
                return;
            }
        }

        if (HasSwitchFile && SwitchFilePath != null)
        {
            try
            {
                WriteSwitchSettingsToFile(SwitchFilePath, maxCalls, Lines);
                where += " and " + Path.GetFileName(SwitchFilePath) + " (the built-in switch uses it from its next start)";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                StatusText = "The routes are saved, but the switch file was not written: " + ex.Message;
                _dialogService.ShowError("Switch settings not written", ex.Message);
                return;
            }
        }

        HasUnsavedChanges = false;
        RebuildRows();
        StatusText = "Saved to " + where;
        Logger.LogInformation("Routing saved: {Count} routes to {Where}", _routes.Count, where);
    }

    /// <summary>
    /// Replaces the routing keys of a configuration file and leaves everything else in it as it was (other
    /// sections, such as the built-in switch, are not part of <see cref="RuntimeConfig"/>, so the file cannot
    /// simply be written again from it). Comments in the file are lost, because the JSON reader does not keep
    /// them.
    /// </summary>
    /// <param name="path">
    /// The file.
    /// </param>
    /// <param name="routing">
    /// The routing section to write.
    /// </param>
    public static void WriteRoutingToFile(string path, X25RoutingConfig routing)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(path), null,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (root is not JsonObject top)
        {
            throw new InvalidDataException(path + " is not a configuration file");
        }

        JsonObject section = FindOrAdd(top, "Routing");
        SetKey(section, "OwnDnics", JsonSerializer.SerializeToNode(routing.OwnDnics));
        SetKey(section, "InternationalPrefix", JsonValue.Create(routing.InternationalPrefix));
        SetKey(section, "Routes", JsonSerializer.SerializeToNode(routing.Routes));
        SetKey(section, "RoutingTable", new JsonArray());
        SetKey(section, "DefaultGateway", null);

        // The channel count of each XOT line, matched by name; a count that is not set is taken out of the file so
        // the line keeps the default (every channel up to 4095). Nothing else of a line is touched.
        string? xotKey = FindKey(section, "XotInterfaces");
        if (xotKey != null && section[xotKey] is JsonArray xotLines)
        {
            for (int i = 0; i < xotLines.Count; i++)
            {
                if (xotLines[i] is not JsonObject line)
                {
                    continue;
                }

                string? nameKey = FindKey(line, "Name");
                string name = nameKey != null ? line[nameKey]?.GetValue<string>() ?? string.Empty : string.Empty;
                for (int j = 0; j < routing.XotInterfaces.Count; j++)
                {
                    if (!string.Equals(routing.XotInterfaces[j].Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ushort? count = routing.XotInterfaces[j].LciCountSetting;
                    string? countKey = FindKey(line, "LciCount");
                    if (count.HasValue)
                    {
                        line[countKey ?? "LciCount"] = count.Value;
                    }
                    else if (countKey != null)
                    {
                        line.Remove(countKey);
                    }

                    break;
                }
            }
        }

        string text = top.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, text);
    }

    // The file is read without regard to case, so a key may be written in any case; the one there is reused.
    private static JsonObject FindOrAdd(JsonObject parent, string key)
    {
        string? existing = FindKey(parent, key);
        if (existing != null && parent[existing] is JsonObject o)
        {
            return o;
        }

        JsonObject added = new JsonObject();
        parent[existing ?? key] = added;
        return added;
    }

    private static void SetKey(JsonObject obj, string key, JsonNode? value)
    {
        obj[FindKey(obj, key) ?? key] = value;
    }

    private static string? FindKey(JsonObject obj, string key)
    {
        for (int i = 0; i < obj.Count; i++)
        {
            string k = obj.GetAt(i).Key;
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return k;
            }
        }

        return null;
    }

    /// <summary>
    /// A copy of a route that shares nothing with it, so the working copy never changes the live configuration
    /// before Save.
    /// </summary>
    /// <param name="rc">
    /// The route.
    /// </param>
    /// <returns>
    /// The copy.
    /// </returns>
    public static X121RouteConfig Copy(X121RouteConfig rc)
    {
        return new X121RouteConfig
        {
            Name = rc.Name,
            Kind = rc.Kind,
            Match = rc.Match,
            Port = rc.Port,
            BackupPorts = new List<string>(rc.BackupPorts ?? new List<string>()),
            HuntPorts = new List<string>(rc.HuntPorts ?? new List<string>()),
            Enabled = rc.Enabled,
            Note = rc.Note,
            Direction = rc.Direction,
            CallUserDataPattern = rc.CallUserDataPattern,
            Priority = rc.Priority,
        };
    }

    private static List<X121RouteConfig> CopyAll(List<X121RouteConfig> routes)
    {
        List<X121RouteConfig> copy = new List<X121RouteConfig>(routes.Count);
        for (int i = 0; i < routes.Count; i++)
        {
            copy.Add(Copy(routes[i]));
        }

        return copy;
    }
}

/// <summary>
/// Where a line of the Lines list comes from, which says where its channel count is saved.
/// </summary>
public enum LineSource
{
    /// <summary>A port of the built-in switch: <c>lci_count</c> in its .ini file.</summary>
    SwitchPort,

    /// <summary>One of this app's XOT lines: <c>LciCount</c> in the app's configuration file.</summary>
    AppXot,

    /// <summary>One of this app's serial or TCP LAPB lines: no channel setting, always the default.</summary>
    AppFixed,
}

/// <summary>
/// One line of the Lines list: a line, what kind it is, and how many logical channels it has.
/// </summary>
/// <remarks>
/// The count is typed as digits; empty means "not set", which gives every channel from the line's first channel
/// up to 4095 (X.25 3.1; <see cref="X25Channels.DefaultCount"/>). A set count must keep the last channel at or
/// below 4095.
/// </remarks>
public sealed partial class LineRow : ObservableObject
{
    private readonly RoutingViewModel _owner;

    [ObservableProperty]
    private string _channelCountText;

    [ObservableProperty]
    private string _error = string.Empty;

    /// <summary>
    /// Creates a line.
    /// </summary>
    /// <param name="owner">The screen, told when the count is edited.</param>
    /// <param name="source">Where the line comes from.</param>
    /// <param name="index">Its place in its own list (switch ports, or the app's lines of its kind).</param>
    /// <param name="section">For a switch port its .ini section (<c>port.N</c>); otherwise the line's name.</param>
    /// <param name="name">The name shown.</param>
    /// <param name="kind">What kind of line it is, in words.</param>
    /// <param name="lciBase">The line's first channel.</param>
    /// <param name="count">The configured count; null when not set.</param>
    /// <param name="editable">Whether the count can be changed here.</param>
    public LineRow(RoutingViewModel owner, LineSource source, int index, string section, string name, string kind,
        ushort lciBase, ushort? count, bool editable)
    {
        _owner = owner;
        Source = source;
        Index = index;
        Section = section;
        Name = name;
        Kind = kind;
        LciBase = lciBase == 0 ? (ushort)1 : lciBase;
        IsEditable = editable;
        _channelCountText = count.HasValue ? count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
    }

    /// <summary>Where the line comes from.</summary>
    public LineSource Source { get; }

    /// <summary>Its place in its own list.</summary>
    public int Index { get; }

    /// <summary>For a switch port its .ini section; otherwise the line's name.</summary>
    public string Section { get; }

    /// <summary>The name shown.</summary>
    public string Name { get; }

    /// <summary>What kind of line it is, in words (access or trunk, and the transport; or "this app, ...").</summary>
    public string Kind { get; }

    /// <summary>The line's first channel.</summary>
    public ushort LciBase { get; }

    /// <summary>Whether the count can be changed here.</summary>
    public bool IsEditable { get; }

    /// <summary>The most channels this line can have: from its first channel up to 4095.</summary>
    public int Highest => X25Channels.Highest + 1 - LciBase;

    /// <summary>The count the line runs with, in words: the number, or "4095 (default)" when it is not set.</summary>
    public string ChannelsText
    {
        get
        {
            if (TryRead(out ushort? count, out _) && count.HasValue)
            {
                return count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return X25Channels.DefaultCount(LciBase).ToString(System.Globalization.CultureInfo.InvariantCulture) + " (default)";
        }
    }

    /// <summary>A hint for the field: the allowed range.</summary>
    public string Hint => "1-" + Highest + ", empty = default (" + X25Channels.DefaultCount(LciBase) + ")";

    /// <summary>
    /// Reads the field.
    /// </summary>
    /// <param name="count">The count; null for "not set" (empty field).</param>
    /// <param name="error">Why it was refused, in words.</param>
    /// <returns>True when it can be saved.</returns>
    public bool TryRead(out ushort? count, out string error)
    {
        count = null;
        error = string.Empty;
        string t = (ChannelCountText ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] < '0' || t[i] > '9')
            {
                error = Name + ": channels must be digits (" + Hint + ").";
                return false;
            }
        }

        if (!int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
            || n < 1 || n > Highest)
        {
            error = Name + ": channels must be 1 to " + Highest + " (the last channel may be at most 4095, X.25 3.1).";
            return false;
        }

        count = (ushort)n;
        return true;
    }

    partial void OnChannelCountTextChanged(string value)
    {
        Error = TryRead(out _, out string error) ? string.Empty : error;
        OnPropertyChanged(nameof(ChannelsText));
        _owner.LineEdited();
    }
}

/// <summary>
/// One line of the route list.
/// </summary>
public sealed partial class RouteRow : ObservableObject
{
    private readonly RoutingViewModel _owner;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>
    /// Creates a line.
    /// </summary>
    /// <param name="owner">
    /// The screen; turning the switch changes its working copy.
    /// </param>
    /// <param name="index">
    /// The route's place in the working copy.
    /// </param>
    /// <param name="order">
    /// Its place in the lookup order, from 1.
    /// </param>
    /// <param name="type">
    /// The kind of route.
    /// </param>
    /// <param name="name">
    /// The route name.
    /// </param>
    /// <param name="match">
    /// What it matches, in words.
    /// </param>
    /// <param name="target">
    /// Where it sends calls, backups in order.
    /// </param>
    /// <param name="note">
    /// The note.
    /// </param>
    /// <param name="hits">
    /// How many calls the engine sent by it.
    /// </param>
    /// <param name="enabled">
    /// Whether it is on.
    /// </param>
    public RouteRow(RoutingViewModel owner, int index, int order, X121RouteType type, string name, string match,
        string target, string note, long hits, bool enabled)
    {
        _owner = owner;
        Index = index;
        Order = order;
        Type = type;
        Name = name;
        Match = match;
        Target = target;
        Note = note;
        Hits = hits;
        _enabled = enabled;
    }

    /// <summary>
    /// The route's place in the working copy.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Its place in the lookup order, from 1.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// The kind of route.
    /// </summary>
    public X121RouteType Type { get; }

    /// <summary>
    /// The kind, in a word, for the badge.
    /// </summary>
    public string Kind => X121RouteText.KindName(Type);

    /// <summary>
    /// The badge colour of the kind (the WCAG 2.1 AA palette of the documentation diagrams).
    /// </summary>
    public IBrush KindBrush => new SolidColorBrush(Color.Parse(KindColour(Type)));

    /// <summary>
    /// The badge text colour: dark on the amber badge, white on the others, so the word stays readable.
    /// </summary>
    public IBrush KindTextBrush => Type == X121RouteType.Dcc ? Brushes.Black : Brushes.White;

    /// <summary>
    /// The route name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// What it matches, in words.
    /// </summary>
    public string Match { get; }

    /// <summary>
    /// Where it sends calls, backups in order.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// The note.
    /// </summary>
    public string Note { get; }

    /// <summary>
    /// How many calls the engine sent by it since it started or loaded its table.
    /// </summary>
    public long Hits { get; }

    partial void OnEnabledChanged(bool value) => _owner.SetEnabled(Index, value);

    /// <summary>
    /// The badge colour of a kind.
    /// </summary>
    /// <param name="type">
    /// The kind.
    /// </param>
    /// <returns>
    /// A colour as <c>#RRGGBB</c>.
    /// </returns>
    public static string KindColour(X121RouteType type)
    {
        switch (type)
        {
            case X121RouteType.Exact: return "#2196F3";
            case X121RouteType.Hunt: return "#3F51B5";
            case X121RouteType.Regex: return "#9C27B0";
            case X121RouteType.Prefix: return "#009688";
            case X121RouteType.Dnic: return "#4CAF50";
            case X121RouteType.Dcc: return "#FFA726";
            case X121RouteType.Default: return "#E91E63";
            default: return "#757575";
        }
    }
}
