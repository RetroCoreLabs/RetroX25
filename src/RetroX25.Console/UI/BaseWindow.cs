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
using Terminal.Gui;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// Base class for all X.25 Emulator GUI windows providing consistent behavior
    /// </summary>
    public abstract class BaseWindow : Window
    {
        /// <summary>
        /// The protocol runtime this window reads its state from and sends its commands to.
        /// </summary>
        /// <remarks>
        /// Set once by the constructor and never null.
        /// </remarks>
        protected readonly ProtocolRuntime _runtime;

        /// <summary>
        /// The logger this window writes its messages to, including errors caught by <see cref="SafeInvoke"/>.
        /// </summary>
        /// <remarks>
        /// Set once by the constructor and never null.
        /// </remarks>
        protected readonly ILogger _logger;

        /// <summary>
        /// Event fired when window is closed, allowing parent to remove from tracking
        /// </summary>
        public new event EventHandler? Closed;

        /// <summary>
        /// Create a new base window with common settings
        /// </summary>
        protected BaseWindow(string title, ProtocolRuntime runtime, ILogger logger) : base(title)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Common window properties
            X = 2;
            Y = 2;
            Width = Dim.Fill() - 4;  // Resizable width
            Height = Dim.Fill() - 4; // Resizable height
            ColorScheme = Colors.Dialog;
            Modal = false; // Non-modal by default
        }

        /// <summary>
        /// Standard window close behavior - override for custom cleanup
        /// </summary>
        protected virtual void CloseWindow()
        {
            // Perform any custom cleanup first
            OnClosing();

            // Fire closed event so parent can remove from tracking list
            Closed?.Invoke(this, EventArgs.Empty);

            // Hide window and remove from Application.Top
            this.Visible = false;
            Application.Top.Remove(this);
        }

        /// <summary>
        /// Called before window closes - override for cleanup logic
        /// </summary>
        protected virtual void OnClosing()
        {
            // Base implementation does nothing
        }

        /// <summary>
        /// Standard exit button click handler
        /// </summary>
        protected virtual void OnExitClicked()
        {
            CloseWindow();
        }

        /// <summary>
        /// Helper to create a standard exit button
        /// </summary>
        protected Button CreateExitButton(int x, int y, string text = "Exit")
        {
            var button = new Button(text) { X = x, Y = y };
            button.Clicked += OnExitClicked;
            return button;
        }

        /// <summary>
        /// Helper to safely invoke UI updates on the main thread
        /// </summary>
        protected void SafeInvoke(Action action)
        {
            try
            {
                Application.MainLoop.Invoke(action);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UI thread invocation");
            }
        }
    }
}
