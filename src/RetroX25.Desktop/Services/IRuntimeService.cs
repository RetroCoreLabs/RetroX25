//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Holds the one <see cref="ProtocolRuntime"/> the desktop application uses, so view-models reach it
/// through this service instead of creating their own.
/// </summary>
public interface IRuntimeService
{
    /// <summary>
    /// Gets the protocol runtime.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when <see cref="Initialize"/> has not created a runtime, or the runtime has been disposed.
    /// </exception>
    ProtocolRuntime Runtime { get; }

    /// <summary>
    /// Gets whether a runtime exists: <see cref="Initialize"/> has created one and
    /// <see cref="Dispose"/> has not disposed it since.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Creates the runtime from a configuration. A runtime that already exists is disposed first.
    /// </summary>
    /// <param name="config">
    /// The runtime configuration.
    /// </param>
    void Initialize(RuntimeConfig config);

    /// <summary>
    /// Raised after <see cref="Restart"/> has replaced the runtime with a new one.
    /// </summary>
    /// <remarks>
    /// Everything that holds the old runtime (status views, PAD clients, server windows) is holding
    /// a disposed engine at this point and must let go of it and, if it still wants one, take the
    /// new <see cref="Runtime"/>.
    /// </remarks>
    event EventHandler? RuntimeRestarted;

    /// <summary>
    /// Stops the running runtime and starts a new one on a configuration, so the new settings take
    /// effect: its interfaces, routes and timers are the ones in <paramref name="config"/>.
    /// </summary>
    /// <param name="config">
    /// The configuration the new runtime runs on.
    /// </param>
    void Restart(RuntimeConfig config);

    /// <summary>
    /// Disposes the runtime, if there is one. Afterwards <see cref="IsRunning"/> is false.
    /// </summary>
    void Dispose();
}
