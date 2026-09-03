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

    public ProtocolRuntime Runtime => _runtime ?? throw new InvalidOperationException("Runtime not initialized. Call Initialize() first.");

    public bool IsRunning => _runtime != null;

    public RuntimeService(ILogger<RuntimeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
