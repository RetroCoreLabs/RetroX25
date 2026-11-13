//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Threading;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Remembers, for the whole app, which packets the user has cleared from view.
/// </summary>
/// <remarks>
/// <para>
/// The protocol engine keeps its packet history in a ring buffer and has no call to empty it. So
/// "clear packet history" cannot remove anything from the engine. What it can do, and what this
/// class makes true, is hide: every Packet window shows only packets newer than the mark kept here,
/// and a Packet window opened later starts at the same mark. The engine's own history, its
/// counters and its exported history are not changed.
/// </para>
/// <para>
/// The mark is a history item number. After the engine is restarted its numbers start again from
/// one, so the mark is reset then.
/// </para>
/// </remarks>
public sealed class PacketViewState : IDisposable
{
    private readonly IRuntimeService _runtimeService;
    private long _clearedUpToId;

    /// <summary>
    /// Creates the state and starts watching for an engine restart.
    /// </summary>
    /// <param name="runtimeService">
    /// The service that owns the engine; its restart notice resets the mark.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="runtimeService"/> is null.
    /// </exception>
    public PacketViewState(IRuntimeService runtimeService)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _runtimeService.RuntimeRestarted += OnRuntimeRestarted;
    }

    /// <summary>
    /// Gets the history item number up to and including which packets are hidden; 0 when nothing is cleared.
    /// </summary>
    public long ClearedUpToId => Interlocked.Read(ref _clearedUpToId);

    /// <summary>
    /// Raised on the thread that called <see cref="ClearUpTo"/> after the mark changed.
    /// </summary>
    public event EventHandler? Cleared;

    /// <summary>
    /// Hides every packet recorded so far from every Packet window.
    /// </summary>
    /// <remarks>
    /// It reads the newest history item number from the running engine. With an empty history the
    /// mark is left as it was.
    /// </remarks>
    public void ClearAll()
    {
        if (!_runtimeService.IsRunning)
        {
            return;
        }

        long newest = 0;
        System.Collections.Generic.IReadOnlyList<RetroIO.Protocols.X25.Models.HistoryItem> history =
            _runtimeService.Runtime.GetHistory();
        for (int i = 0; i < history.Count; i++)
        {
            if (history[i].Id > newest)
            {
                newest = history[i].Id;
            }
        }

        if (newest > 0)
        {
            ClearUpTo(newest);
        }
    }

    /// <summary>
    /// Hides every packet with a history item number up to and including <paramref name="id"/>.
    /// </summary>
    /// <param name="id">
    /// The newest history item number to hide. A number lower than the present mark does nothing.
    /// </param>
    public void ClearUpTo(long id)
    {
        long current = Interlocked.Read(ref _clearedUpToId);
        if (id <= current)
        {
            return;
        }

        Interlocked.Exchange(ref _clearedUpToId, id);
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    private void OnRuntimeRestarted(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref _clearedUpToId, 0);
    }

    /// <summary>
    /// Stops watching for an engine restart.
    /// </summary>
    public void Dispose()
    {
        _runtimeService.RuntimeRestarted -= OnRuntimeRestarted;
    }
}
