//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The PAD window calls the built-in TAD server (address 23430200). The TAD tells the PAD with an
    /// X.29 SET message "echo = off, I echo for you"; the PAD must then stop echoing what is typed, or
    /// every typed line shows twice (found by hand on 2026-11-12).
    /// </summary>
    [TestFixture]
    public sealed class PadWindowTadEchoTests
    {
        private static string FindSampleFile(string fileName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "docs", "config-samples", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Sample configuration not found above " + AppContext.BaseDirectory, fileName);
        }

        private static void WaitUntil(Func<bool> condition, string what, Func<string>? detail = null, int timeoutMs = 8000)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                Dispatcher.UIThread.RunJobs();
                if (condition())
                {
                    return;
                }

                Thread.Sleep(20);
            }

            Dispatcher.UIThread.RunJobs();
            string window = detail != null ? "\n--- window text ---\n" + detail() : "";
            Assert.That(condition(), Is.True, "gave up waiting for: " + what + window);
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>
        /// Types a line in the real PAD window after CALL 23430200: the line is in the console once,
        /// followed by the TAD's own "Echo: ..." answer line. Before the fix the console held the line
        /// twice in a row before the answer (the PAD echo plus the TAD echo).
        /// </summary>
        [AvaloniaTest]
        public void PadWindow_CallingTheTad_ShowsATypedLineOnce()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-tad-callback.json"));
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            using ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IRuntimeService>().Initialize(config);

            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.KeyTextInput("CALL 23430200");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("TAD> "), "the TAD banner and prompt", () => viewModel.ConsoleText);
            Thread.Sleep(300);
            Dispatcher.UIThread.RunJobs();

            window.KeyTextInput("hello once");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("Echo: hello once"), "the TAD answer line", () => viewModel.ConsoleText);

            string text = viewModel.ConsoleText;
            int answer = text.LastIndexOf("Echo: hello once", StringComparison.Ordinal);
            string before = text.Substring(0, answer);
            Console.WriteLine("console before the answer line: " + before.Substring(Math.Max(0, before.Length - 60)).Replace("\r", "\\r").Replace("\n", "\\n"));
            Assert.That(before, Does.EndWith("hello once\r\n").Or.EndWith("hello once\n"),
                "the typed line is not shown right before the answer\n" + text);
            Assert.That(before, Does.Not.Contain("hello oncehello once"), "the typed line is shown twice\n" + text);
            Assert.That(before, Does.Not.EndWith("hello once\r\nhello once\r\n"), "the typed line is shown twice\n" + text);

            window.Close();
        }
    }
}
