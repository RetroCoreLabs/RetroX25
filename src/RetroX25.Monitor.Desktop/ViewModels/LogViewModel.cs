//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Protocols.X25.Monitor;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// One log row: a source-stamped record rendered for the log grid.
    /// </summary>
    public sealed class LogRowViewModel
    {
        /// <summary>
        /// Local time string (from the source UTC timestamp).
        /// </summary>
        public string Time { get; }

        /// <summary>
        /// Severity level.
        /// </summary>
        public string Level { get; }

        /// <summary>
        /// Logger category.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Message text.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Creates a log row from a record.
        /// </summary>
        /// <param name="record">
        /// The received log record.
        /// </param>
        public LogRowViewModel(LogRecord record)
        {
            Time = DateTimeOffset.FromUnixTimeMilliseconds(record.TimestampMs).ToLocalTime().ToString("HH:mm:ss.fff");
            Level = record.Level;
            Category = ShortCategory(record.Category);
            Message = record.Message;
        }

        private static string ShortCategory(string category)
        {
            int dot = category.LastIndexOf('.');
            return dot >= 0 && dot < category.Length - 1 ? category.Substring(dot + 1) : category;
        }
    }

    /// <summary>
    /// The log-viewer window view-model: connects to a switch and shows its live log stream with a minimum
    /// level filter and a free-text search. Records are timestamped at the source.
    /// </summary>
    public sealed partial class LogViewModel : ObservableObject, IDisposable
    {
        private const int MaxLogs = 5000;

        private readonly MonitorClient _client = new MonitorClient();
        private readonly Action<Action> _uiInvoke;
        private readonly List<LogRecord> _all = new List<LogRecord>();

        [ObservableProperty]
        private SwitchEndpoint? _selectedSwitch;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _status = "Disconnected";

        [ObservableProperty]
        private int _minLevelIndex;

        [ObservableProperty]
        private string _search = string.Empty;

        /// <summary>
        /// The known switch endpoints, shared with the shell.
        /// </summary>
        public ObservableCollection<SwitchEndpoint> Switches { get; }

        /// <summary>
        /// The minimum-level filter choices (index maps to a severity floor).
        /// </summary>
        public ObservableCollection<string> Levels { get; } = new ObservableCollection<string>(
            new[] { "All", "Debug", "Information", "Warning", "Error" });

        /// <summary>
        /// The filtered log rows shown in the grid.
        /// </summary>
        public ObservableCollection<LogRowViewModel> Logs { get; } = new ObservableCollection<LogRowViewModel>();

        /// <summary>
        /// Creates the log view-model.
        /// </summary>
        /// <param name="switches">
        /// The shared switch endpoints.
        /// </param>
        /// <param name="uiInvoke">
        /// Marshals work onto the UI thread; defaults to the Avalonia dispatcher.
        /// </param>
        public LogViewModel(ObservableCollection<SwitchEndpoint> switches, Action<Action>? uiInvoke = null)
        {
            Switches = switches;
            _selectedSwitch = switches.Count > 0 ? switches[0] : null;
            _uiInvoke = uiInvoke ?? (action => Dispatcher.UIThread.Post(action));
            _client.LogReceived += OnLogReceived;
            _client.ConnectionChanged += OnConnectionChanged;
        }

        /// <summary>
        /// Connects to the selected switch's monitoring server.
        /// </summary>
        [RelayCommand]
        private void Connect()
        {
            SwitchEndpoint? endpoint = SelectedSwitch;
            if (endpoint == null)
            {
                Status = "No switch selected";
                return;
            }

            Status = _client.Connect(endpoint.Host, endpoint.Port)
                ? $"Connected to {endpoint.Display}"
                : $"Failed to connect to {endpoint.Display}";
        }

        /// <summary>
        /// Disconnects from the monitoring server.
        /// </summary>
        [RelayCommand]
        private void Disconnect()
        {
            _client.Disconnect();
        }

        /// <summary>
        /// Clears the log view (and the backing buffer).
        /// </summary>
        [RelayCommand]
        private void ClearLogs()
        {
            _all.Clear();
            Logs.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _client.Dispose();
        }

        partial void OnMinLevelIndexChanged(int value)
        {
            Rebuild();
        }

        partial void OnSearchChanged(string value)
        {
            Rebuild();
        }

        private void OnLogReceived(LogRecord record)
        {
            _uiInvoke(() =>
            {
                _all.Add(record);
                while (_all.Count > MaxLogs)
                {
                    _all.RemoveAt(0);
                }

                if (Passes(record))
                {
                    Logs.Add(new LogRowViewModel(record));
                    while (Logs.Count > MaxLogs)
                    {
                        Logs.RemoveAt(0);
                    }
                }
            });
        }

        private void OnConnectionChanged(bool connected)
        {
            _uiInvoke(() =>
            {
                IsConnected = connected;
                if (!connected)
                {
                    Status = "Disconnected";
                }
            });
        }

        private void Rebuild()
        {
            Logs.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                if (Passes(_all[i]))
                {
                    Logs.Add(new LogRowViewModel(_all[i]));
                }
            }
        }

        private bool Passes(LogRecord record)
        {
            // MinLevelIndex: 0=All, 1=Debug, 2=Information, 3=Warning, 4=Error; it equals the severity floor
            // (Trace=0, Debug=1, Information=2, Warning=3, Error=4), so include when severity >= the floor.
            if (Severity(record.Level) < MinLevelIndex)
            {
                return false;
            }

            string search = Search;
            if (!string.IsNullOrEmpty(search)
                && record.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && record.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }

        private static int Severity(string level)
        {
            switch (level)
            {
                case "Trace": return 0;
                case "Debug": return 1;
                case "Information": return 2;
                case "Warning": return 3;
                case "Error": return 4;
                case "Critical": return 5;
                default: return 2;
            }
        }
    }
}
