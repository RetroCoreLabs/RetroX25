//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Microsoft.Extensions.Logging;

namespace RetroX25.Host
{
    /// <summary>
    /// One machine's log: everything goes to the program's log, but only at or above <see cref="Minimum"/>. The
    /// console's <c>log &lt;hostname|*&gt; &lt;level&gt;</c> sets it, so one busy machine can be quietened, or one
    /// looked at closely, without touching the rest. The program's own level (<c>-v</c>) still applies on top.
    /// </summary>
    internal sealed class MachineLog : ILogger
    {
        private readonly ILogger _inner;
        private volatile int _minimum = (int)LogLevel.Trace;

        /// <summary>Makes a machine log on top of the program's.</summary>
        /// <param name="inner">The program's log.</param>
        public MachineLog(ILogger inner)
        {
            _inner = inner;
        }

        /// <summary>The lowest level let through; <see cref="LogLevel.Trace"/> (everything) at start.</summary>
        public LogLevel Minimum
        {
            get => (LogLevel)_minimum;
            set => _minimum = (int)value;
        }

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return _inner.BeginScope(state);
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None && (int)logLevel >= _minimum && _inner.IsEnabled(logLevel);
        }

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
