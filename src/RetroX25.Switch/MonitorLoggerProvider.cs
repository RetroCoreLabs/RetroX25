//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Monitor;

using RetroIO.Protocols.X25.Switch;
namespace RetroX25.Switch
{
    /// <summary>
    /// An <see cref="ILoggerProvider"/> that forwards every log record (stamped at the source) to a
    /// <see cref="MonitorServer"/>, so remote viewers, tests, or an alerting app receive the switch's logs
    /// live over the monitoring stream.
    /// </summary>
    public sealed class MonitorLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, ForwardingLogger> _loggers = new ConcurrentDictionary<string, ForwardingLogger>();
        private readonly LogLevel _minLevel;

        /// <summary>
        /// The server to forward records to. Set after the server is created (loggers may exist before it).
        /// </summary>
        public MonitorServer? Server { get; set; }

        /// <summary>
        /// Creates the provider.
        /// </summary>
        /// <param name="minLevel">
        /// Minimum level to forward (default Information, to avoid flooding with debug noise).
        /// </param>
        public MonitorLoggerProvider(LogLevel minLevel = LogLevel.Information)
        {
            _minLevel = minLevel;
        }

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new ForwardingLogger(this, name));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _loggers.Clear();
        }

        private bool IsEnabled(LogLevel level)
        {
            return level >= _minLevel && level != LogLevel.None && Server != null;
        }

        private void Publish(LogLevel level, string category, string message)
        {
            MonitorServer? server = Server;
            if (server == null)
            {
                return;
            }

            server.PublishLog(new LogRecord
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Level = level.ToString(),
                Category = category,
                Message = message
            });
        }

        private sealed class ForwardingLogger : ILogger
        {
            private readonly MonitorLoggerProvider _provider;
            private readonly string _category;

            public ForwardingLogger(MonitorLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return _provider.IsEnabled(logLevel);
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string message = formatter(state, exception);
                if (exception != null)
                {
                    message = message + " | " + exception.Message;
                }

                _provider.Publish(logLevel, _category, message);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            private NullScope()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
