//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Microsoft.Extensions.Logging;
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
        _logger.LogInformation("ProtocolRuntime initialized successfully");
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
