//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Service for accessing the X.25 Protocol Runtime.
/// Provides centralized access to Core functionality.
/// </summary>
public interface IRuntimeService
{
    /// <summary>
    /// Gets the ProtocolRuntime instance.
    /// </summary>
    ProtocolRuntime Runtime { get; }

    /// <summary>
    /// Gets whether the runtime is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Initializes the runtime with the specified configuration.
    /// </summary>
    /// <param name="config">Runtime configuration</param>
    void Initialize(RuntimeConfig config);

    /// <summary>
    /// Disposes the runtime and cleans up resources.
    /// </summary>
    void Dispose();
}
