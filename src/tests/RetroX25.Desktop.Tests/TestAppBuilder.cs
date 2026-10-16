using Avalonia;
using Avalonia.Headless;
using RetroX25.Desktop;
using RetroX25.Desktop.Tests;

// Registers the Avalonia application used by all [AvaloniaTest] cases in this assembly.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Builds the headless Avalonia application for the UI smoke tests.
    /// </summary>
    /// <remarks>
    /// It uses the REAL <see cref="App"/> so the Fluent theme and the app's own styles load exactly
    /// as they do on a normal launch, and Skia rendering is on
    /// (<c>UseHeadlessDrawing = false</c>) because a missing <c>avares://</c> resource or an
    /// unresolvable <c>DynamicResource</c> brush only fails when something is actually drawn.
    /// </remarks>
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
