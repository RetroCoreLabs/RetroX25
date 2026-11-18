//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
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
    /// Two Desktop PAD windows talk through the built-in Altos chat (docs/altos-chat-port-plan.md steps 3 and 4). The
    /// chat sets the PAD with X.29: echo, forward on CR, LF after CR in the echo, editing with DEL, CAN and DC2. The
    /// window's Delete key must send DEL (the chat's character delete), and the PAD edits the line before the chat
    /// sees it. The chat monitor window's view-model shows both users.
    /// </summary>
    [TestFixture]
    public sealed class PadWindowChatTests
    {
        private const string ChatAddress = "26245890040004";

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

        /// <summary>
        /// The chat sample configuration with no XOT line (nothing to dial in a test) and no join pause.
        /// </summary>
        private static ServiceProvider StartChatRuntime()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-altos-chat.json"));
            config.Routing.XotInterfaces.Clear();
            config.Chat.WaitPauseMs = 0;
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IRuntimeService>().Initialize(config);
            return provider;
        }

        private static (PADClientWindowViewModel Vm, PADClientWindow Window) OpenPad(ServiceProvider provider, int number)
        {
            PADClientWindowViewModel vm = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), number);
            PADClientWindow window = new PADClientWindow(vm) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("CALL " + ChatAddress);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => vm.ConsoleText.Contains("Get help with:"), "the chat's welcome", () => vm.ConsoleText);
            return (vm, window);
        }

        /// <summary>
        /// Window A types "helo", Delete, "lo", Enter. Window B shows "guest says: hello"; the chat never saw "helo".
        /// A's own echo shows the DEL as "\" (X.28 3.5.24, parameter 19 = 0 printer style) and the line ends with CR
        /// LF (parameter 13 = 4), so B's answer does not overwrite it.
        /// </summary>
        [AvaloniaTest]
        public void TwoPadWindows_TalkThroughTheChat_DeleteKeyEditsInThePad()
        {
            using ServiceProvider provider = StartChatRuntime();
            (PADClientWindowViewModel a, PADClientWindow aWindow) = OpenPad(provider, 1);
            (PADClientWindowViewModel b, PADClientWindow bWindow) = OpenPad(provider, 2);
            Thread.Sleep(200);
            Dispatcher.UIThread.RunJobs();

            int aBefore = a.ConsoleText.Length;
            aWindow.KeyTextInput("helo");
            aWindow.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            aWindow.KeyTextInput("lo");
            aWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            WaitUntil(() => b.ConsoleText.Contains("guest says: hello"), "B gets A's line", () => b.ConsoleText);
            Assert.That(b.ConsoleText, Does.Not.Contain("helo"), "an edited-away character reached the chat");
            string aEcho = a.ConsoleText.Substring(aBefore);
            Assert.That(aEcho, Does.StartWith("helo\\lo" + Environment.NewLine),
                "A's echo: the DEL shown as \\ and a new line after Enter\n" + a.ConsoleText);

            // The chat monitor shows both callers.
            ChatServerWindowViewModel monitor = new ChatServerWindowViewModel(
                NullLogger<ChatServerWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>());
            Assert.That(monitor.Users.Count, Is.EqualTo(2));
            Assert.That(monitor.Users[0].Nickname, Is.EqualTo("guest"));
            Assert.That(monitor.Users[0].From, Is.Not.Empty);
            Assert.That(monitor.Channels.Count, Is.EqualTo(1));
            Assert.That(monitor.Channels[0].Users, Is.EqualTo(2));

            // Live: a nickname change shows after the chat's Changed notice reaches the UI thread.
            bWindow.KeyTextInput(".n bob");
            bWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => monitor.Users.Count == 2 && monitor.Users[1].Nickname == "bob", "the monitor shows the new nickname");

            // The window itself, from the container as the menu opens it: both grids hold rows.
            ChatServerWindow chatWindow = new ChatServerWindow(provider.GetRequiredService<ChatServerWindowViewModel>())
            {
                Width = 800,
                Height = 500,
            };
            chatWindow.Show();
            Dispatcher.UIThread.RunJobs();
            List<Avalonia.Controls.DataGrid> grids = new List<Avalonia.Controls.DataGrid>();
            System.Collections.Generic.IEnumerable<Avalonia.Visual> all = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(chatWindow);
            using (IEnumerator<Avalonia.Visual> e = all.GetEnumerator())
            {
                while (e.MoveNext())
                {
                    if (e.Current is Avalonia.Controls.DataGrid g)
                    {
                        grids.Add(g);
                    }
                }
            }

            Assert.That(grids.Count, Is.EqualTo(2), "users grid and channels grid");
            Assert.That(((ChatServerWindowViewModel)chatWindow.DataContext!).Users.Count, Is.EqualTo(2));
            chatWindow.Close();
            monitor.Dispose();

            aWindow.Close();
            bWindow.Close();
        }
    }
}
