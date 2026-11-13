//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Desktop.Services;

/// <summary>
/// The "EmbeddedSwitch" section of the configuration file: whether the Desktop app runs an X.25
/// switch inside itself, and which switch file (.ini, the same format the x25switch program reads)
/// it runs on.
/// </summary>
public sealed class EmbeddedSwitchOptions
{
    /// <summary>True when the app starts a switch of its own at startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Full path of the switch .ini file. When the file names it with a relative path, the path is
    /// taken relative to the folder of the configuration file.
    /// </summary>
    public string ConfigFile { get; set; } = string.Empty;

    /// <summary>The "no built-in switch" setting; an external switch (or none) is used.</summary>
    public static EmbeddedSwitchOptions Off => new EmbeddedSwitchOptions();

    /// <summary>
    /// Reads the "EmbeddedSwitch" section of a configuration file. A file without the section means
    /// "no built-in switch".
    /// </summary>
    /// <param name="jsonPath">Path of the JSON configuration file.</param>
    /// <returns>The options; <see cref="Off"/> when the section is missing.</returns>
    /// <exception cref="InvalidDataException">The section is there but wrong.</exception>
    public static EmbeddedSwitchOptions LoadFrom(string jsonPath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (!doc.RootElement.TryGetProperty("EmbeddedSwitch", out JsonElement section))
        {
            return Off;
        }

        EmbeddedSwitchOptions options = new EmbeddedSwitchOptions();
        if (section.TryGetProperty("Enabled", out JsonElement enabled))
        {
            options.Enabled = enabled.GetBoolean();
        }

        if (section.TryGetProperty("ConfigFile", out JsonElement file) && file.ValueKind == JsonValueKind.String)
        {
            string path = file.GetString() ?? string.Empty;
            if (path.Length > 0 && !Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? string.Empty, path));
            }

            options.ConfigFile = path;
        }

        if (options.Enabled && options.ConfigFile.Length == 0)
        {
            throw new InvalidDataException("EmbeddedSwitch is enabled in " + jsonPath + " but names no ConfigFile");
        }

        return options;
    }
}

/// <summary>
/// Runs an X.25 switch inside the Desktop app, from the same switch library and the same .ini file
/// as the x25switch program. With it the Desktop app alone is enough to reach a router: the switch
/// listens for the router's calls, dials the router, and routes between the app's own links.
/// </summary>
/// <remarks>
/// It is optional. With the setting off the app works with an external x25switch (or none), and
/// <see cref="Describe"/> says so.
/// </remarks>
public sealed class EmbeddedSwitchService : IDisposable
{
    private readonly EmbeddedSwitchOptions _options;
    private readonly ILogger _logger;
    private SwitchNode? _node;
    private string _failure = string.Empty;
    private IReadOnlyList<PortConfig> _configuredPorts = Array.Empty<PortConfig>();

    /// <summary>
    /// Creates the service. Nothing runs until <see cref="StartIfEnabled"/>.
    /// </summary>
    /// <param name="options">The settings from the configuration file.</param>
    /// <param name="logger">Where the switch's own log lines go.</param>
    public EmbeddedSwitchService(EmbeddedSwitchOptions options, ILogger<EmbeddedSwitchService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True when the setting asks for a built-in switch.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>True while the built-in switch is running with all its ports started.</summary>
    public bool IsRunning => _node != null;

    /// <summary>
    /// Why the switch is not running, in words; empty while it runs or when it was never asked for.
    /// </summary>
    public string Failure => _failure;

    /// <summary>
    /// The ports as the switch file describes them. They are known as soon as the file has been read,
    /// also when a port then failed to start, so the XOT link view can still tell which of the app's
    /// own links lead to the built-in switch. Empty when no switch was asked for or the file could not be read.
    /// </summary>
    public IReadOnlyList<PortConfig> ConfiguredPorts => _configuredPorts;

    /// <summary>
    /// The running port objects (read-only). Empty while the switch is not running. The link view reads
    /// their counters and connection state; it never changes them.
    /// </summary>
    public IReadOnlyList<ISwitchPort> RunningPorts
    {
        get
        {
            SwitchNode? node = _node;
            return node != null ? node.Ports : Array.Empty<ISwitchPort>();
        }
    }

    /// <summary>
    /// Starts the switch when the setting asks for it. Does nothing otherwise, and does nothing when it
    /// is already running.
    /// </summary>
    /// <returns>True when no switch was asked for, or the switch is now running; false when it failed.</returns>
    public bool StartIfEnabled()
    {
        if (!_options.Enabled || _node != null)
        {
            return true;
        }

        try
        {
            SwitchConfig config = SwitchConfigLoader.LoadFile(_options.ConfigFile);
            _configuredPorts = new List<PortConfig>(config.Ports);
            SwitchNode node = new SwitchNode(config) { UptimeStartMs = Environment.TickCount64 };
            for (int i = 0; i < config.Ports.Count; i++)
            {
                // One factory makes every port from its configured transport. A transport this build cannot
                // run throws here, naming the port and the transport; the catch below keeps the failure and the
                // main window shows it. It is never run as some other kind of port.
                node.AddPort(SwitchPortFactory.Create(config.Ports[i], _logger));
            }

            // The core joins an incoming call on one port to an outgoing call on another, by route.
            SwitchCore core = new SwitchCore(node);
            GC.KeepAlive(core);

            // A port that does not start is an error, not something to run on without (the same rule
            // as the x25switch program): the failure is kept and shown in the main window.
            List<string> failed = new List<string>();
            for (int i = 0; i < node.Ports.Count; i++)
            {
                if (!node.Ports[i].Start())
                {
                    failed.Add(config.Ports[i].Id.ToString() + " (" + SwitchPortFactory.Describe(config.Ports[i].Transport) + ")");
                }
            }

            if (failed.Count > 0)
            {
                for (int i = 0; i < node.Ports.Count; i++)
                {
                    node.Ports[i].Stop();
                }

                _failure = "port(s) " + string.Join(", ", failed) + " did not start (for a listening port: is it already in use, for example by an x25switch program? for a serial port: can the device be opened? The log has the reason)";
                _logger.LogError("Built-in switch: {Failure}", _failure);
                return false;
            }

            _node = node;
            _failure = string.Empty;
            _logger.LogInformation("Built-in switch '{Name}' (DNIC {Dnic}) started with {Ports} ports from {File}",
                config.Name, config.Dnic, node.Ports.Count, _options.ConfigFile);
            return true;
        }
        catch (Exception ex)
        {
            _failure = ex.Message;
            _logger.LogError(ex, "Built-in switch failed to start from {File}", _options.ConfigFile);
            return false;
        }
    }

    /// <summary>
    /// One line for the main window that says which switch the app uses and what state it is in.
    /// </summary>
    /// <returns>The status text.</returns>
    public string Describe()
    {
        if (!_options.Enabled)
        {
            return "Switch: none built in (use an external x25switch if you need one)";
        }

        if (_node != null)
        {
            return $"Switch: built in, running ({_node.Ports.Count} ports, {_node.ActiveStitchCount} call(s) connected)";
        }

        return _failure.Length > 0 ? "Switch: built in, NOT running: " + _failure : "Switch: built in, not started";
    }

    /// <summary>Stops the switch and its ports.</summary>
    public void Dispose()
    {
        SwitchNode? node = _node;
        _node = null;
        if (node == null)
        {
            return;
        }

        for (int i = 0; i < node.Ports.Count; i++)
        {
            node.Ports[i].Stop();
        }
    }
}
