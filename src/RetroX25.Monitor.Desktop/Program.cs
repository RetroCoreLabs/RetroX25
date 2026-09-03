using System;
using Avalonia;

namespace RetroX25.Monitor.Desktop
{
    /// <summary>
    /// Application entry point for the X.25 monitor GUI.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Process entry point. Avalonia requires a single-threaded apartment on Windows.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments passed to the desktop lifetime.
        /// </param>
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        /// <summary>
        /// Builds and configures the Avalonia application.
        /// </summary>
        /// <returns>
        /// The configured application builder.
        /// </returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}
