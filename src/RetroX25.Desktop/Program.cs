//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Avalonia;

namespace RetroX25.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        App.ConfigFilePath = ChooseConfigFile(args, System.IO.File.Exists);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Picks the configuration file to start on: the first command-line argument that ends in .json,
    /// else x25config.json in the working folder when that file exists, else none (built-in defaults).
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="fileExists">Tells whether a file exists; the real one is File.Exists.</param>
    /// <returns>
    /// The file name, or null. A file that is NAMED but missing is returned as named, so the loader
    /// fails loudly instead of the program quietly starting on the defaults.
    /// </returns>
    public static string? ChooseConfigFile(string[] args, Func<string, bool> fileExists)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return args[i];
            }
        }

        return fileExists("x25config.json") ? "x25config.json" : null;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
