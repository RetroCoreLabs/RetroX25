//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Microsoft.Extensions.Logging;
using RetroIO.Link;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Implementation of IRuntimeService providing access to ProtocolRuntime.
/// </summary>
public class RuntimeService : IRuntimeService
{
    private readonly ILogger<RuntimeService> _logger;
    private ProtocolRuntime? _runtime;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Initialize"/> has not been called, or the runtime has been
    /// disposed.
    /// </exception>
    public ProtocolRuntime Runtime => _runtime ?? throw new InvalidOperationException("Runtime not initialized. Call Initialize() first.");

    /// <summary>
    /// Gets whether a runtime exists, that is, <see cref="Initialize"/> has been called and
    /// <see cref="Dispose"/> has not been called since.
    /// </summary>
    public bool IsRunning => _runtime != null;

    /// <summary>
    /// Creates the service with no runtime yet; call <see cref="Initialize"/> to create one.
    /// </summary>
    /// <param name="logger">
    /// Logger for the service, also passed to the runtime it creates.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    public RuntimeService(ILogger<RuntimeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When a runtime already exists, it is disposed first and a new one is created.
    /// </remarks>
    public void Initialize(RuntimeConfig config)
    {
        if (_runtime != null)
        {
            _logger.LogWarning("Runtime already initialized. Disposing existing instance.");
            Dispose();
        }

        _logger.LogInformation("Initializing ProtocolRuntime...");
        _runtime = new ProtocolRuntime(_logger, config);
        StartTcpLapbLinks(_runtime, config, _logger);
        _logger.LogInformation("ProtocolRuntime initialized successfully");
    }

    /// <summary>
    /// Creates and starts a link for every enabled TCP-LAPB entry in the configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine builds its serial and XOT interfaces by itself when it is created, but it never
    /// builds the TCP-LAPB ones: those are links of the engine's link manager and only exist when
    /// someone creates them. The engine has a call that creates links for ALL interface kinds from the
    /// configuration, but using it here would make a second copy of every serial and XOT interface
    /// (two owners of one serial port and one XOT listener). So only the TCP-LAPB entries are created
    /// here, with the engine's public calls, the same way that call does it.
    /// </para>
    /// <para>
    /// An entry that cannot be created or started is logged as an error and the others still go on;
    /// the entry then shows as missing or in error in the Link Status window.
    /// </para>
    /// </remarks>
    /// <param name="runtime">
    /// The engine to add the links to.
    /// </param>
    /// <param name="config">
    /// The configuration whose TCP-LAPB entries are used.
    /// </param>
    /// <param name="logger">
    /// Where failures are written.
    /// </param>
    /// <returns>
    /// The number of links created.
    /// </returns>
    internal static int StartTcpLapbLinks(ProtocolRuntime runtime, RuntimeConfig config, ILogger logger)
    {
        System.Collections.Generic.List<TcpLapbInterfaceConfig>? entries = config.Routing?.TcpLapbInterfaces;
        if (entries == null)
        {
            return 0;
        }

        int created = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            TcpLapbInterfaceConfig entry = entries[i];
            if (!entry.Enabled)
            {
                continue;
            }

            try
            {
                if (entry.Framing == FramingType.Slip)
                {
                    // SLIP framing runs on the engine's codec link, which needs its own TCP byte carrier.
                    bool isServer = entry.Mode == TcpMode.Server;
                    string host = isServer ? "0.0.0.0" : entry.RemoteAddress;
                    var transport = new TcpLinkTransport(logger, entry.Name, isServer, host, entry.Port);
                    var codecConfig = new CodecLinkConfig
                    {
                        Name = entry.Name,
                        Framing = FramingType.Slip,
                        RemoteMachineId = entry.RemoteMachineId,
                        Description = entry.Description,
                        WindowSize = entry.LapbWindowSize,
                        MaxRetries = entry.LapbMaxRetries,
                        T1TimeoutMs = entry.LapbT1TimeoutMs,
                        T2TimeoutMs = entry.LapbT2TimeoutMs,
                        T3TimeoutMs = entry.LapbT3TimeoutMs,
                        IsPrimary = entry.IsPrimary,
                        LapbAutoConnect = entry.LapbAutoConnect,
                        ProtocolMode = new ProtocolModeConfig { Mode = entry.ProtocolMode }
                    };
                    runtime.Links.CreateCodecLink(codecConfig, transport);
                }
                else
                {
                    var linkConfig = new TcpLapbLinkConfig
                    {
                        Name = entry.Name,
                        Mode = entry.Mode,
                        RemoteAddress = entry.RemoteAddress,
                        Port = entry.Port,
                        RemoteMachineId = entry.RemoteMachineId,
                        Description = entry.Description,
                        ConnectTimeoutMs = entry.ConnectTimeoutMs,
                        ReconnectDelayMs = entry.ReconnectDelayMs,
                        WindowSize = entry.LapbWindowSize,
                        MaxRetries = entry.LapbMaxRetries,
                        T1TimeoutMs = entry.LapbT1TimeoutMs,
                        T2TimeoutMs = entry.LapbT2TimeoutMs,
                        T3TimeoutMs = entry.LapbT3TimeoutMs,
                        IsPrimary = entry.IsPrimary,
                        ProtocolMode = new ProtocolModeConfig { Mode = entry.ProtocolMode }
                    };
                    runtime.CreateTcpLapbLink(linkConfig);
                }

                created++;
                if (!runtime.StartLink(entry.Name))
                {
                    logger.LogError("TCP-LAPB link '{Name}' was created but did not start", entry.Name);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                // The link manager refuses a duplicate name and the link refuses an empty one.
                logger.LogError(ex, "TCP-LAPB link '{Name}' could not be created", entry.Name);
            }
        }

        return created;
    }

    /// <inheritdoc/>
    public event EventHandler? RuntimeRestarted;

    /// <inheritdoc/>
    public void Restart(RuntimeConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _logger.LogInformation("Restarting ProtocolRuntime on a new configuration");
        Dispose();
        Initialize(config);
        RuntimeRestarted?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Does nothing when there is no runtime. Afterwards <see cref="IsRunning"/> is false.
    /// </remarks>
    public void Dispose()
    {
        if (_runtime != null)
        {
            _logger.LogInformation("Disposing ProtocolRuntime...");
            _runtime.Dispose();
            _runtime = null;
            _logger.LogInformation("ProtocolRuntime disposed");
        }
    }
}
