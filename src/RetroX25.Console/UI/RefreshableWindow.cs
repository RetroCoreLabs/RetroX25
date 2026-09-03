using System;
using System.Threading;
using Terminal.Gui;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Base class for windows that need periodic refresh capabilities
    /// </summary>
    public abstract class RefreshableWindow : BaseWindow
    {
        private Timer? _refreshTimer;
        
        /// <summary>
        /// Refresh interval in milliseconds (default: 2000ms)
        /// </summary>
        protected virtual int RefreshIntervalMs => 2000;
        
        /// <summary>
        /// Initial delay before first refresh in milliseconds (default: 1000ms)
        /// </summary>
        protected virtual int RefreshDelayMs => 1000;
        
        /// <summary>
        /// Create a new refreshable window
        /// </summary>
        protected RefreshableWindow(string title, ProtocolRuntime runtime, ILogger logger) : base(title, runtime, logger)
        {
        }
        
        /// <summary>
        /// Start the refresh timer
        /// </summary>
        protected void StartRefreshTimer()
        {
            _refreshTimer = new Timer(
                callback: _ => SafeInvoke(() =>
                {
                    try
                    {
                        OnRefresh();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during window refresh");
                    }
                }),
                state: null,
                dueTime: TimeSpan.FromMilliseconds(RefreshDelayMs),
                period: TimeSpan.FromMilliseconds(RefreshIntervalMs)
            );
        }
        
        /// <summary>
        /// Stop the refresh timer
        /// </summary>
        protected void StopRefreshTimer()
        {
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }
        
        /// <summary>
        /// Override this method to implement refresh logic
        /// </summary>
        protected abstract void OnRefresh();
        
        /// <summary>
        /// Helper to create a standard refresh button
        /// </summary>
        protected Button CreateRefreshButton(int x, int y, string text = "Refresh")
        {
            var button = new Button(text) { X = x, Y = y };
            button.Clicked += () => SafeInvoke(OnRefresh);
            return button;
        }
        
        /// <summary>
        /// Override to stop timer before closing
        /// </summary>
        protected override void OnClosing()
        {
            StopRefreshTimer();
            base.OnClosing();
        }
        
        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopRefreshTimer();
            }
            base.Dispose(disposing);
        }
    }
}