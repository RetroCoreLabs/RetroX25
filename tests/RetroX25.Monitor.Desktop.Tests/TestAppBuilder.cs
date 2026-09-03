using Avalonia;
using Avalonia.Headless;
using RetroX25.Monitor.Desktop;
using RetroX25.Monitor.Desktop.Tests;

// Registers the Avalonia application used by all [AvaloniaTest] cases in this assembly.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace RetroX25.Monitor.Desktop.Tests
{
    /// <summary>
    /// Builds the headless Avalonia application for UI tests. It uses the real <see cref="App"/> (so the
    /// Fluent theme, NodifyM control styles and our AppStyles are all loaded) and enables Skia rendering with
    /// <c>UseHeadlessDrawing = false</c> so tests can capture actual rendered pixels.
    /// </summary>
    public static class TestAppBuilder
    {
        /// <summary>
        /// Configures the headless Avalonia application.
        /// </summary>
        /// <returns>
        /// The configured application builder.
        /// </returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false
                });
        }
    }
}
