//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    /// The Desktop app's PAD window places a call over XOT, exchanges text and clears, driven by real
    /// key presses on the real window, against a small fake router on loopback.
    /// </summary>
    /// <remarks>
    /// Found by hand on 2026-11-11: the window printed COM for a call and nothing left the PC,
    /// because the app could not load a configuration file at all and always ran on its defaults.
    /// These tests load the real sample file that sends 2350... and 2360... calls to the switch, so a
    /// call that is not routed out fails here and not at a person's keyboard.
    /// </remarks>
    [TestFixture]
    public sealed class PadWindowCallTests
    {
        private const string SampleFile = "x25config-xot-to-switch.json";

        /// <summary>
        /// Finds the sample file by walking up from the test's folder to the repository's docs folder.
        /// </summary>
        private static string FindSampleFile() => FindSampleFile(SampleFile);

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

        /// <summary>
        /// Lets the UI thread work and waits until a condition is true, or fails after the time.
        /// </summary>
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
        /// A router at the far end of the XOT link: answers a call, says R1>, answers "show clock",
        /// and confirms a clear. It records what the app sent so the test can check it.
        /// </summary>
        private sealed class FakeRouter : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly object _lock = new object();
            private readonly List<string> _dataSeen = new List<string>();
            private int _sendSeq;
            private volatile bool _stop;

            public FakeRouter()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(Run) { IsBackground = true, Name = "fake-router" };
                _thread.Start();
            }

            public int Port { get; }

            public string? CalledAddress { get; private set; }

            /// <summary>The call user data of the Call Request the router received (null before a call).</summary>
            public byte[]? CallUserData { get; private set; }

            public volatile bool ClearSeen;

            /// <summary>When set, a call is refused with a Clear Indication carrying this cause.</summary>
            public byte? RefuseCause { get; set; }

            /// <summary>Diagnostic octet of the refusing Clear Indication.</summary>
            public byte RefuseDiagnostic { get; set; }

            public volatile bool QBitDataSeen;

            public bool HasData(string text)
            {
                lock (_lock)
                {
                    for (int i = 0; i < _dataSeen.Count; i++)
                    {
                        if (_dataSeen[i].Contains(text))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            private void Run()
            {
                // A switch opens a spare connection first and carries a call on another, so every
                // connection gets its own thread.
                try
                {
                    while (!_stop)
                    {
                        TcpClient accepted = _listener.AcceptTcpClient();
                        Thread worker = new Thread(() => Serve(accepted)) { IsBackground = true, Name = "fake-router-conn" };
                        worker.Start();
                    }
                }
                catch (Exception)
                {
                    // The listener was closed at the end of the test.
                }
            }

            private void Serve(TcpClient client)
            {
                try
                {
                    using TcpClient owned = client;
                    using NetworkStream stream = owned.GetStream();
                    byte[] header = new byte[4];
                    while (!_stop)
                    {
                        if (!ReadExactly(stream, header, 4))
                        {
                            return;
                        }

                        int length = (header[2] << 8) | header[3];
                        byte[] packet = new byte[length];
                        if (!ReadExactly(stream, packet, length))
                        {
                            return;
                        }

                        Handle(stream, packet);
                    }
                }
                catch (Exception)
                {
                    // The listener was closed at the end of the test.
                }
            }

            private static bool ReadExactly(NetworkStream stream, byte[] buffer, int count)
            {
                int got = 0;
                while (got < count)
                {
                    int n = stream.Read(buffer, got, count - got);
                    if (n <= 0)
                    {
                        return false;
                    }

                    got += n;
                }

                return true;
            }

            private void Handle(NetworkStream stream, byte[] packet)
            {
                byte lci = packet[1];
                byte type = packet[2];

                if (type == 0xFB)
                {
                    // Restart Request, the first packet on a new XOT connection (X.25 4.2): a DCE
                    // answers with Restart Confirmation, and the app holds its calls until it arrives.
                    Send(stream, new byte[] { 0x10, 0x00, 0xFF });
                }
                else if (type == 0x0B)
                {
                    // Call Request: the address lengths are in the next octet (calling high nibble,
                    // called low nibble), then the called digits, two to an octet.
                    int calledLength = packet[3] & 0x0F;
                    StringBuilder digits = new StringBuilder();
                    for (int i = 0; i < calledLength; i++)
                    {
                        byte b = packet[4 + (i / 2)];
                        int digit = (i % 2 == 0) ? (b >> 4) : (b & 0x0F);
                        digits.Append((char)('0' + digit));
                    }

                    CallUserData = RetroIO.Protocols.X25.Models.X25PacketParser.ParsePacket(packet).CallUserData;
                    CalledAddress = digits.ToString();
                    if (RefuseCause.HasValue)
                    {
                        // Refuse the call: Clear Indication with this cause and diagnostic.
                        Send(stream, new byte[] { 0x10, lci, 0x13, RefuseCause.Value, RefuseDiagnostic });
                        return;
                    }

                    Send(stream, new byte[] { 0x10, lci, 0x0F });
                    SendData(stream, lci, 0, "R1>");
                }
                else if (type == 0x13)
                {
                    ClearSeen = true;
                    Send(stream, new byte[] { 0x10, lci, 0x17 });
                }
                else if ((type & 0x01) == 0)
                {
                    int ps = (type >> 1) & 0x07;
                    int pr = (ps + 1) & 0x07;
                    string text = Encoding.ASCII.GetString(packet, 3, packet.Length - 3);
                    if ((packet[0] & 0x80) != 0)
                    {
                        QBitDataSeen = true;
                    }

                    lock (_lock)
                    {
                        _dataSeen.Add(text);
                    }

                    if (text.Contains("show clock"))
                    {
                        SendData(stream, lci, pr, "CLOCK-OK\r\nR1>");
                    }
                    else
                    {
                        // Receive Ready: acknowledges the packet, so the app's send window opens again.
                        Send(stream, new byte[] { 0x10, lci, (byte)((pr << 5) | 0x01) });
                    }
                }
            }

            private void SendData(NetworkStream stream, byte lci, int pr, string text)
            {
                byte[] body = Encoding.ASCII.GetBytes(text);
                byte[] packet = new byte[3 + body.Length];
                packet[0] = 0x10;
                packet[1] = lci;
                packet[2] = (byte)((pr << 5) | (_sendSeq << 1));
                _sendSeq = (_sendSeq + 1) & 0x07;
                Buffer.BlockCopy(body, 0, packet, 3, body.Length);
                Send(stream, packet);
            }

            private static void Send(NetworkStream stream, byte[] packet)
            {
                byte[] frame = new byte[4 + packet.Length];
                frame[2] = (byte)(packet.Length >> 8);
                frame[3] = (byte)(packet.Length & 0xFF);
                Buffer.BlockCopy(packet, 0, frame, 4, packet.Length);
                stream.Write(frame, 0, frame.Length);
            }

            public void Dispose()
            {
                _stop = true;
                _listener.Stop();
            }
        }

        [Test]
        public void TheSampleFile_IsLoaded_WithTheXotLinkAndBothRoutes()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());

            Assert.That(config.Routing.XotInterfaces.Count, Is.EqualTo(1));
            Assert.That(config.Routing.XotInterfaces[0].RemoteIPAddress, Is.EqualTo("127.0.0.1"));
            Assert.That(config.Routing.XotInterfaces[0].RemotePort, Is.EqualTo(2200));
            Assert.That(config.Routing.RoutingTable.Count, Is.EqualTo(2));
        }

        [Test]
        public void ALoadOfAMissingFile_FailsLoudly_NotOnTheDefaults()
        {
            Assert.Throws<FileNotFoundException>(() => App.LoadConfigFile("no-such-file.json"));
        }

        [AvaloniaTest]
        public void Restart_ReplacesTheEngine_AndTheNewSettingsAreTheOnesInUse()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            object before = runtimeService.Runtime;
            int restarted = 0;
            runtimeService.RuntimeRestarted += (s, e) => restarted++;

            RuntimeConfig fromFile = App.LoadConfigFile(FindSampleFile());
            runtimeService.Restart(fromFile);

            Assert.That(restarted, Is.EqualTo(1), "no notice that the engine was replaced");
            Assert.That(runtimeService.Runtime, Is.Not.SameAs(before), "the old engine is still in use");
            Assert.That(runtimeService.Runtime.Config.Routing.XotInterfaces.Count, Is.EqualTo(1),
                "the engine does not run on the new settings");
        }

        [AvaloniaTest]
        public void PadWindow_CallsThroughTheSwitchLink_ExchangesText_AndClearsWithEscape()
        {
            using FakeRouter router = new FakeRouter();

            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();

            using ServiceProvider provider = BuildServices(config);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();

            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, runtimeService, 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The packet window is open BEFORE the call, so it has to receive the packets as they happen.
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            PacketWindow packetWindow = new PacketWindow(packetVm);
            packetWindow.Show();
            Dispatcher.UIThread.RunJobs();

            // CALL 23600001 and Enter, typed on the window like a person does.
            window.KeyTextInput("CALL 23600001");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            WaitUntil(() => router.CalledAddress != null, "the call to reach the router",
                () => viewModel.ConsoleText + "\n--- app log ---\n" + AppLog(provider));
            Assert.That(router.CalledAddress, Is.EqualTo("23600001"));

            // A PAD call carries the X.29 protocol identifier on its own: 01 and three zero octets. Nothing is typed or
            // switched on for it. The router's PAD refuses a call without it.
            Assert.That(router.CallUserData, Is.EqualTo(new byte[] { 0x01, 0x00, 0x00, 0x00 }), "the Call Request of a PAD call carries the X.29 call user data");
            WaitUntil(() => viewModel.ConsoleText.Contains("R1>"), "the router's prompt in the window");

            // A command in the call goes to the far side and the answer comes back.
            window.KeyTextInput("show clock");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("CLOCK-OK"), "the answer to show clock");

            // The packets of the call are in the counters and in the history the packet window shows.
            // They were empty for every XOT call until 2026-11-11 (only the serial path recorded them).
            RetroIO.Protocols.X25.Models.StatisticsSnapshot stats = runtimeService.Runtime.GetStatistics();
            Assert.That(stats.X25.PacketsTransmitted, Is.GreaterThan(0), "no X.25 packet counted as sent");
            Assert.That(stats.X25.PacketsReceived, Is.GreaterThan(0), "no X.25 packet counted as received");
            Assert.That(stats.X25.CallRequestsTransmitted, Is.EqualTo(1), "the Call Request was not counted");
            Assert.That(runtimeService.Runtime.GetHistory().Count, Is.GreaterThan(0), "the packet history is empty");

            // The counters are read the way the Status window reads them (StatisticsReader), and the main
            // window's own view-model says the XOT link is up.
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            WaitUntil(() => Total(provider, "X.25 packets received") != "0", "X.25 packet count in the statistics",
                () => "rx=" + Total(provider, "X.25 packets received") + " tx=" + Total(provider, "X.25 packets sent"));
            Assert.That(Total(provider, "X.25 packets sent"), Is.Not.EqualTo("0"));
            Assert.That(Total(provider, "X.25 call requests sent"), Is.EqualTo("1"));
            WaitUntil(() => main.Interfaces.Count > 0 && main.Interfaces[0].Status.StartsWith("Connected"),
                "the XOT link shown as connected",
                () => main.Interfaces.Count > 0 ? main.Interfaces[0].Status : "(no interface rows)");

            // The packet window lists the call's packets (the user saw it empty).
            WaitUntil(() => packetVm.Packets.Count >= 4, "packets in the packet window",
                () => "packets listed: " + packetVm.Packets.Count);

            // The Status information window shows the counters (the user saw zeros everywhere).
            StatusWindowViewModel statusVm = provider.GetRequiredService<StatusWindowViewModel>();
            StatusWindow statusWindow = new StatusWindow(statusVm);
            statusWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(statusVm.Table.Cell("X.25 packets received", "Total"), Is.Not.EqualTo("0"), "Status window: X.25 packets received are 0");
            Assert.That(statusVm.Table.Cell("X.25 packets sent", "Total"), Is.Not.EqualTo("0"), "Status window: X.25 packets sent are 0");
            Assert.That(statusVm.Table.Cell("X.25 call requests sent", "Total"), Is.EqualTo("1"), "Status window: the call request is not counted");
            statusWindow.Close();
            packetWindow.Close();

            // Ctrl+P leaves the call for command mode; CLEAR there clears the call. It must not be
            // sent to the far side as text.
            window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, null);
            window.KeyTextInput("CLEAR");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            WaitUntil(() => router.ClearSeen, "the Clear Request to reach the router");
            WaitUntil(() => viewModel.ConsoleText.Contains("CLEARED"), "CLEARED in the window");
            Assert.That(router.HasData("CLEAR"), Is.False, "CLEAR was sent as text instead of being a command");
            Assert.That(router.QBitDataSeen, Is.False,
                "the PAD sent an X.29 message; a PAD must not send the host-to-PAD invitation to clear");

            window.Close();
        }

        /// <summary>
        /// Nothing listens at the far end of the link (the switch is not running). The window must
        /// not say the call is connected, and the prompt must come back.
        /// </summary>
        [AvaloniaTest]
        public void ACallWithNobodyAtTheFarEnd_DoesNotSayCom_AndThePromptComesBack()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = deadPort;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();

            using ServiceProvider provider = BuildServices(config);
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.KeyTextInput("CALL 23600001");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Thread.Sleep(3000);
            Dispatcher.UIThread.RunJobs();

            string shown = viewModel.ConsoleText;
            Assert.That(shown, Does.Not.Contain("COM"), "said connected with nobody at the far end\n" + shown + "\n--- app log ---\n" + AppLog(provider));
            Assert.That(shown, Does.Contain("CLR"), "no word that the call failed\n" + shown);
            Assert.That(shown.TrimEnd(), Does.EndWith("PAD>"), "the PAD> prompt did not come back\n" + shown);
            window.Close();
        }

        /// <summary>
        /// The far end refuses the call with cause 1 (number busy), diagnostic 0. The window shows the X.28
        /// clear signal with the reason, "CLR OCC C:1 D:0", and the PAD> prompt comes back.
        /// </summary>
        [AvaloniaTest]
        public void ARefusedCall_ShowsTheX28ClearSignalWithTheReason()
        {
            using FakeRouter router = new FakeRouter { RefuseCause = 1, RefuseDiagnostic = 0 };
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();

            using ServiceProvider provider = BuildServices(config);
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.KeyTextInput("CALL 23600001");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            WaitUntil(() => viewModel.ConsoleText.Contains("CLR OCC C:1 D:0"), "CLR OCC C:1 D:0 in the window",
                () => viewModel.ConsoleText + "\n--- app log ---\n" + AppLog(provider));
            Assert.That(viewModel.ConsoleText, Does.Not.Contain("COM"), "said connected on a refused call");
            WaitUntil(() => viewModel.ConsoleText.TrimEnd().EndsWith("PAD>"), "the PAD> prompt after the clear",
                () => viewModel.ConsoleText);
            window.Close();
        }

        /// <summary>
        /// LIVE: the real PAD window calls the real Cisco R1 through the real switch (switch-cisco.ini
        /// must be running on this PC and R1 reachable). Skipped unless asked for by name.
        /// </summary>
        [AvaloniaTest]
        [Explicit("needs the real switch (demo/switch-cisco.ini) and the real router R1")]
        public void Live_PadWindowCallsR1_ThroughTheRealSwitch()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            using ServiceProvider provider = BuildServices(config);
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.KeyTextInput("CALL 23600001");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("COM") && viewModel.ConsoleText.Contains("R1>"),
                "COM and the router prompt R1>", () => viewModel.ConsoleText + "\n--- app log ---\n" + AppLog(provider), 20000);

            window.KeyTextInput("show clock");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("UTC"), "the router's clock", () => viewModel.ConsoleText, 15000);

            // Enter must send one line only: the router must not show a second, empty prompt line.
            string afterClock = viewModel.ConsoleText.Substring(viewModel.ConsoleText.LastIndexOf("show clock", StringComparison.Ordinal));
            Assert.That(afterClock, Does.Not.Contain("R1>\r\n\r\nR1>").And.Not.Contain("R1>\n\nR1>"), "Enter was sent twice\n" + afterClock);

            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            WaitUntil(() => Total(provider, "X.25 packets received") != "0" && main.Interfaces[0].Status.StartsWith("Connected"),
                "stats and the connected link",
                () => "rx=" + Total(provider, "X.25 packets received") + " link=" + main.Interfaces[0].Status);

            window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, null);
            window.KeyTextInput("CLEAR");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("CLEARED"), "CLEARED", () => viewModel.ConsoleText);
            WaitUntil(() => viewModel.ConsoleText.TrimEnd().EndsWith("PAD>"), "PAD> again", () => viewModel.ConsoleText);
            window.Close();
        }

        /// <summary>
        /// LIVE: the app listens on the real port 1998 and waits (up to two minutes) for the real
        /// router R1 to call 23430200, then checks the TAD answered and the main window shows it.
        /// The switch must NOT be running (it uses port 1998 too). Skipped unless asked for by name.
        /// </summary>
        [AvaloniaTest]
        [Explicit("needs the real router R1 to call 23430200 while this runs")]
        public void Live_R1CallsTheTad_InTheDesktopRuntime()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-tad-callback.json"));
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();

            // The router's call arrives, the TAD answers it, and the main window counts it.
            WaitUntil(() => Total(provider, "X.25 call requests received") != "0", "R1's call to arrive",
                () => AppLog(provider), 120000);
            WaitUntil(() => main.Interfaces.Count > 0 && main.Interfaces[0].Status.StartsWith("Listening, 1"),
                "the listener shown with R1 connected", () => main.Interfaces[0].Status, 20000);
            // Hold the call open while R1 is typing. R1 keeps the TCP link up after its call ends, so
            // "done" is: data has arrived and nothing more has come for 8 seconds.
            WaitUntil(() => Total(provider, "X.25 data packets received") != "0", "R1's typed data to arrive",
                () => AppLog(provider), 100000);
            string last = "";
            System.Diagnostics.Stopwatch quiet = System.Diagnostics.Stopwatch.StartNew();
            while (quiet.ElapsedMilliseconds < 8000)
            {
                Dispatcher.UIThread.RunJobs();
                string now = Total(provider, "X.25 packets received") + "/" + Total(provider, "X.25 packets sent");
                if (now != last)
                {
                    last = now;
                    quiet.Restart();
                }

                Thread.Sleep(50);
            }

            Assert.That(Total(provider, "X.25 data packets received"), Is.Not.EqualTo("0"), "no data from R1 reached the TAD");
            Assert.That(Total(provider, "X.25 packets sent"), Is.Not.EqualTo("0"));
            Assert.That(provider.GetRequiredService<IRuntimeService>().Runtime.GetHistory().Count, Is.GreaterThan(0));
        }

        /// <summary>
        /// LIVE: the Desktop app alone (built-in switch, the real sample files, the real ports) calls the real
        /// router R1, and then waits for R1 to call the TAD back through the same built-in switch. Nothing else
        /// may run on this PC (no x25switch, no x25echo). Skipped unless asked for by name.
        /// </summary>
        [AvaloniaTest]
        [Explicit("needs the real router R1; R1 must call 23430200 while this runs")]
        public void Live_DesktopAloneWithItsOwnSwitch_CallsR1_AndIsCalledByR1()
        {
            string sample = FindSampleFile("x25config-embedded-switch.json");
            RuntimeConfig config = App.LoadConfigFile(sample);
            EmbeddedSwitchOptions options = EmbeddedSwitchOptions.LoadFrom(sample);
            using ServiceProvider provider = BuildServices(config, options);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            Assert.That(main.SwitchStatus, Does.Contain("running"), main.SwitchStatus);

            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // 1. PAD -> built-in switch -> R1.
            window.KeyTextInput("CALL 23600001");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("COM") && viewModel.ConsoleText.Contains("R1>"),
                "COM and R1> through the built-in switch", () => viewModel.ConsoleText + "\n--- app log ---\n" + AppLog(provider), 20000);
            window.KeyTextInput("show clock");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("UTC"), "the router's clock", () => viewModel.ConsoleText, 15000);
            window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, null);
            window.KeyTextInput("CLEAR");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("CLEARED"), "CLEARED", () => viewModel.ConsoleText);
            window.Close();

            // 2. R1 -> built-in switch -> the app's TAD. Wait for the call, then for the traffic to go quiet.
            string callsBefore = Total(provider, "X.25 call requests received");
            WaitUntil(() => Total(provider, "X.25 call requests received") != callsBefore, "R1's call to the TAD",
                () => AppLog(provider), 110000);
            WaitUntil(() => Total(provider, "X.25 data packets received") != "0", "R1's typed data", () => AppLog(provider), 100000);
            string last = "";
            System.Diagnostics.Stopwatch quiet = System.Diagnostics.Stopwatch.StartNew();
            while (quiet.ElapsedMilliseconds < 8000)
            {
                Dispatcher.UIThread.RunJobs();
                string now = Total(provider, "X.25 packets received") + "/" + Total(provider, "X.25 packets sent");
                if (now != last)
                {
                    last = now;
                    quiet.Restart();
                }

                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Every command given to the PAD is followed by the PAD> prompt again.
        /// </summary>
        [AvaloniaTest]
        public void AfterEveryCommand_ThePadPromptComesBack()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            string[] commands = { "HELP", "STAT", "PAR?", "X", "SET 2:0" };
            for (int i = 0; i < commands.Length; i++)
            {
                window.KeyTextInput(commands[i]);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                WaitUntil(() => viewModel.ConsoleText.TrimEnd().EndsWith("PAD>"),
                    "the prompt after " + commands[i], () => viewModel.ConsoleText);
                // One prompt per command: count the prompts typed so far (the first one is the start-up prompt).
                int prompts = 0;
                int at = 0;
                while ((at = viewModel.ConsoleText.IndexOf("PAD>", at, StringComparison.Ordinal)) >= 0)
                {
                    prompts++;
                    at += 4;
                }

                Assert.That(prompts, Is.EqualTo(i + 2), "wrong number of prompts after " + commands[i] + "\n" + viewModel.ConsoleText);
            }

            window.Close();
        }

        /// <summary>
        /// Text in the console can be marked and copied to the clipboard with Ctrl+C, and the
        /// copy is not sent to the far side as a break.
        /// </summary>
        [AvaloniaTest]
        public void MarkedConsoleText_IsCopiedWithCtrlC()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            SelectableTextBlock box = window.FindControl<SelectableTextBlock>("ConsoleBox")!;
            Assert.That(box, Is.Not.Null, "the console is not a selectable text control");

            box.SelectAll();
            Dispatcher.UIThread.RunJobs();
            Assert.That(box.SelectedText, Does.Contain("PAD Client ready"), "nothing is marked");

            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            Dispatcher.UIThread.RunJobs();

            string? copied = null;
            WaitUntil(() =>
            {
                System.Threading.Tasks.Task<string?> read = window.Clipboard!.GetTextAsync();
                read.Wait(2000);
                copied = read.Result;
                return copied != null && copied.Contains("PAD Client ready");
            }, "the marked text on the clipboard", () => "clipboard: " + (copied ?? "(empty)"));
            window.Close();
        }

        [AvaloniaTest]
        public void Help_ListsTheCommands_AndEnterSendsOneCrNotCrLf()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.KeyTextInput("HELP");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("PAD commands:"), "the help text", () => viewModel.ConsoleText);

            Assert.That(viewModel.ConsoleText, Does.Contain("CALL <address>"));
            Assert.That(viewModel.ConsoleText, Does.Contain("Ctrl+P"));
            window.Close();
        }

        [AvaloniaTest]
        public void TheConsole_FollowsNewOutput_WhenItIsAtTheBottom()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Each unknown command prints a line, so this fills the window several times over.
            for (int i = 0; i < 80; i++)
            {
                window.KeyTextInput("X");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
            }

            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            ScrollViewer scroll = window.FindControl<ScrollViewer>("ConsoleScroll")!;
            Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height), "the text did not outgrow the window");
            Assert.That(scroll.Offset.Y + scroll.Viewport.Height, Is.GreaterThanOrEqualTo(scroll.Extent.Height - 2),
                "the newest line is below the visible area");
            window.Close();
        }

        /// <summary>
        /// The call-back case: a router (here a plain X.25 client, the same wire a Cisco router uses)
        /// calls the TAD address 23430200 on the Desktop app's XOT listener, reads the banner, types a
        /// line and gets it echoed. The app's counters and packet history must show the call.
        /// </summary>
        [AvaloniaTest]
        public void ARouterCallingTheTadAddress_GetsTheBanner_AndTheEcho_AndTheAppCountsIt()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-tad-callback.json"));
            Assert.That(config.Routing.XotInterfaces.Count, Is.EqualTo(1));
            Assert.That(config.Routing.XotInterfaces[0].Mode, Is.EqualTo(RetroIO.Protocols.X25.XotMode.Server));
            Assert.That(config.Routing.XotInterfaces[0].LocalPort, Is.EqualTo(1998));

            // 1998 is the real port; the test uses a free one so it never meets a running switch.
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            config.Routing.XotInterfaces[0].LocalPort = port;

            using ServiceProvider provider = BuildServices(config);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();

            StringBuilder text = new StringBuilder();
            using RetroIO.Protocols.X25.Client.X25Client client =
                new RetroIO.Protocols.X25.Client.X25Client { ServiceName = "fake-r1" };
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                if (!qBit)
                {
                    lock (text)
                    {
                        text.Append(Encoding.ASCII.GetString(data, 0, length));
                    }
                }
            });
            client.Open(new RetroIO.Protocols.X25.Client.X25ClientConfig
            {
                Host = "127.0.0.1",
                Port = (ushort)port,
                LocalAddress = "23600001",
                Framing = RetroIO.Protocols.X25.Client.X25ClientFraming.Xot
            }, NullLogger.Instance);

            RetroIO.Protocols.X25.Client.X25Vc? call = client.CallWait("23430200", 5000);
            Assert.That(call, Is.Not.Null, "the call to the TAD address was not accepted\n" + AppLog(provider));

            WaitUntil(() => { lock (text) { return text.ToString().Contains("TAD> "); } },
                "the TAD banner and prompt", () => { lock (text) { return text.ToString(); } });
            lock (text)
            {
                Assert.That(text.ToString(), Does.StartWith("*** X.25 TAD Loopback Echo Service ***"));
            }

            Assert.That(client.SendText(call!, "hello cisco 42\r"), Is.True);
            WaitUntil(() => { lock (text) { return text.ToString().Contains("Echo: hello cisco 42"); } },
                "the echo of the typed line", () => { lock (text) { return text.ToString(); } });

            RetroIO.Protocols.X25.Models.StatisticsSnapshot stats = runtimeService.Runtime.GetStatistics();
            Assert.That(stats.X25.PacketsReceived, Is.GreaterThan(0), "no X.25 packet counted as received");
            Assert.That(stats.X25.PacketsTransmitted, Is.GreaterThan(0), "no X.25 packet counted as sent");
            Assert.That(runtimeService.Runtime.GetHistory().Count, Is.GreaterThan(0), "the packet history is empty");

            // The main window shows the call: the listener has a connection, the X.25 counters moved.
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            WaitUntil(() => Total(provider, "X.25 packets received") != "0" && Total(provider, "X.25 call requests received") == "1",
                "the call in the main window's X.25 counters",
                () => "rx=" + Total(provider, "X.25 packets received") + " calls=" + Total(provider, "X.25 call requests received"));
            WaitUntil(() => main.Interfaces.Count > 0 && main.Interfaces[0].Status.StartsWith("Listening, 1"),
                "the listener shown with one connection",
                () => main.Interfaces.Count > 0 ? main.Interfaces[0].Status : "(no interface rows)");
        }

        /// <summary>
        /// Reads one figure of the Total column the way the Status window reads it (StatisticsReader), so
        /// a test and the window cannot disagree. The main window shows no counters any more.
        /// </summary>
        private static string Total(ServiceProvider provider, string counter)
        {
            return StatisticsReader.Build(provider.GetRequiredService<IRuntimeService>().Runtime).Cell(counter, "Total") ?? "(no such counter)";
        }

        /// <summary>
        /// The app's own log so far, one line per entry, so a failed wait shows where a call stopped.
        /// </summary>
        private static string AppLog(ServiceProvider provider)
        {
            RetroIO.Protocols.X25.Common.HistoryLoggingProvider history =
                provider.GetRequiredService<RetroIO.Protocols.X25.Common.HistoryLoggingProvider>();
            IReadOnlyList<RetroIO.Protocols.X25.Common.LogEntry> entries = history.GetAllLogs();
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                text.Append(entries[i].Level).Append(' ').Append(entries[i].Message).Append('\n');
            }

            return text.ToString();
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
        /// Writes a switch .ini like demo/switch-desktop.ini but on free ports, so a test never meets a
        /// running switch. The caller deletes the file.
        /// </summary>
        private static string WriteSwitchIni(int callersPort, int routerDialsInPort, int routerPort, int desktopServerPort)
        {
            string path = Path.Combine(Path.GetTempPath(), "x25test-switch-" + Guid.NewGuid().ToString("N") + ".ini");
            string ini =
                "[switch]\nname = TestSwitch\ndnic = 2350\nmgmt_addr = 23500000\n" +
                "[port.0]\ntype = access\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = " + callersPort + "\nlci_count = 64\n" +
                "[port.1]\ntype = trunk\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = " + routerDialsInPort + "\npeer_name = R1\n" +
                "[port.2]\ntype = trunk\ntransport = xot\nmode = dte\nhost = 127.0.0.1\nport = " + routerPort + "\npeer_name = R1\n" +
                "[port.3]\ntype = trunk\ntransport = xot\nmode = dte\nhost = 127.0.0.1\nport = " + desktopServerPort + "\npeer_name = TAD\n" +
                "[route.3]\ntype = prefix\naddress = 2343\nprefix_len = 4\nport = 3\n" +
                "[route.1]\ntype = prefix\naddress = 2360\nprefix_len = 4\nport = 2\n" +
                "[route.0]\ntype = dnic\ndnic = 2350\nport = 0\n" +
                "[user.0]\nname = admin\npassword = admin\nlevel = 1\n";
            File.WriteAllText(path, ini);
            return path;
        }

        /// <summary>
        /// The sample configuration for the built-in switch, with its ports moved to free ones. Returns the
        /// config and the switch options; the ini file path is in the options.
        /// </summary>
        private static RuntimeConfig EmbeddedSwitchConfig(int callersPort, int desktopServerPort, string iniPath, out EmbeddedSwitchOptions options)
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-embedded-switch.json"));
            options = new EmbeddedSwitchOptions { Enabled = true, ConfigFile = iniPath };
            config.Routing.XotInterfaces[0].RemotePort = callersPort;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            config.Routing.XotInterfaces[0].LocalPort = desktopServerPort;
            return config;
        }

        [Test]
        public void TheEmbeddedSwitchSample_NamesTheIniFile_ThatExists()
        {
            EmbeddedSwitchOptions options = EmbeddedSwitchOptions.LoadFrom(FindSampleFile("x25config-embedded-switch.json"));
            Assert.That(options.Enabled, Is.True);
            Assert.That(File.Exists(options.ConfigFile), Is.True, "the sample names a switch file that is not there: " + options.ConfigFile);
            Assert.That(EmbeddedSwitchOptions.LoadFrom(FindSampleFile()).Enabled, Is.False, "a config without the section must mean no built-in switch");
        }

        /// <summary>
        /// With the built-in switch on, the Desktop app alone reaches the router: a PAD call goes
        /// app -> built-in switch -> router, and the main window says the switch is running.
        /// </summary>
        [AvaloniaTest]
        public void EmbeddedSwitch_CarriesAPadCallToTheRouter_WithNoOtherProgram()
        {
            using FakeRouter router = new FakeRouter();
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), router.Port, desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedSwitchConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);

                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                Assert.That(main.SwitchStatus, Does.Contain("running"), main.SwitchStatus);

                PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                    NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
                PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                window.KeyTextInput("CALL 23600001");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                WaitUntil(() => viewModel.ConsoleText.Contains("COM") && viewModel.ConsoleText.Contains("R1>"),
                    "the call through the built-in switch to reach the router", () => viewModel.ConsoleText + "\n--- app log ---\n" + AppLog(provider), 15000);
                Assert.That(router.CalledAddress, Is.EqualTo("23600001"));
                window.Close();
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// With the built-in switch on, the router (here a plain X.25 client) dials the switch and calls
        /// the TAD address: switch -> the app's own XOT listener -> TAD banner and echo.
        /// </summary>
        [AvaloniaTest]
        public void EmbeddedSwitch_CarriesTheRoutersCallToTheTad_WithNoOtherProgram()
        {
            int callers = FreePort();
            int routerDialsIn = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, routerDialsIn, FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedSwitchConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);

                StringBuilder text = new StringBuilder();
                using RetroIO.Protocols.X25.Client.X25Client router =
                    new RetroIO.Protocols.X25.Client.X25Client { ServiceName = "fake-r1" };
                router.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    if (!qBit)
                    {
                        lock (text)
                        {
                            text.Append(Encoding.ASCII.GetString(data, 0, length));
                        }
                    }
                });
                router.Open(new RetroIO.Protocols.X25.Client.X25ClientConfig
                {
                    Host = "127.0.0.1",
                    Port = (ushort)routerDialsIn,
                    LocalAddress = "23600001",
                    Framing = RetroIO.Protocols.X25.Client.X25ClientFraming.Xot
                }, NullLogger.Instance);

                // The switch's dialling port needs a moment to bring its spare connection up; a router
                // retries a call that is cleared as not obtainable, so the test does too.
                RetroIO.Protocols.X25.Client.X25Vc? call = null;
                System.Diagnostics.Stopwatch tries = System.Diagnostics.Stopwatch.StartNew();
                while (call == null && tries.ElapsedMilliseconds < 10000)
                {
                    call = router.CallWait("23430200", 2000);
                }

                Assert.That(call, Is.Not.Null, "the call to the TAD through the built-in switch was not accepted\n" + AppLog(provider));
                WaitUntil(() => { lock (text) { return text.ToString().Contains("TAD> "); } },
                    "the TAD banner", () => { lock (text) { return text.ToString(); } });

                router.SendText(call!, "via the switch\r");
                WaitUntil(() => { lock (text) { return text.ToString().Contains("Echo: via the switch"); } },
                    "the echo", () => { lock (text) { return text.ToString(); } });
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// Apply in the Configuration window replaces the engine while the built-in switch keeps running.
        /// The router must still reach the TAD afterwards.
        /// </summary>
        [AvaloniaTest]
        public void EmbeddedSwitch_StillCarriesTheRoutersCall_AfterTheEngineIsRestarted()
        {
            int callers = FreePort();
            int routerDialsIn = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, routerDialsIn, FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedSwitchConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);
                provider.GetRequiredService<IRuntimeService>().Restart(
                    EmbeddedSwitchConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions unused));

                StringBuilder text = new StringBuilder();
                using RetroIO.Protocols.X25.Client.X25Client router =
                    new RetroIO.Protocols.X25.Client.X25Client { ServiceName = "fake-r1" };
                router.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    if (!qBit)
                    {
                        lock (text)
                        {
                            text.Append(Encoding.ASCII.GetString(data, 0, length));
                        }
                    }
                });
                router.Open(new RetroIO.Protocols.X25.Client.X25ClientConfig
                {
                    Host = "127.0.0.1",
                    Port = (ushort)routerDialsIn,
                    LocalAddress = "23600001",
                    Framing = RetroIO.Protocols.X25.Client.X25ClientFraming.Xot
                }, NullLogger.Instance);

                RetroIO.Protocols.X25.Client.X25Vc? call = null;
                System.Diagnostics.Stopwatch tries = System.Diagnostics.Stopwatch.StartNew();
                while (call == null && tries.ElapsedMilliseconds < 15000)
                {
                    call = router.CallWait("23430200", 2000);
                }

                Assert.That(call, Is.Not.Null, "no call to the TAD after the engine restart\n" + AppLog(provider));
                WaitUntil(() => { lock (text) { return text.ToString().Contains("TAD> "); } },
                    "the TAD banner after the restart", () => { lock (text) { return text.ToString(); } });
                Assert.That(provider.GetRequiredService<MainWindowViewModel>().SwitchStatus, Does.Contain("running"));
            }
            finally
            {
                File.Delete(ini);
            }
        }

        private sealed class QuietDialogs : IDialogService
        {
            public string LastInfo = "";
            public string LastError = "";

            public void ShowInfo(string title, string message) { LastInfo = message; }

            public void ShowWarning(string title, string message) { }

            public void ShowError(string title, string message) { LastError = message; }

            public System.Threading.Tasks.Task<bool> ShowConfirmation(string title, string message) => System.Threading.Tasks.Task.FromResult(true);

            public System.Threading.Tasks.Task<string?> ShowOpenFileDialog(string title, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);

            public System.Threading.Tasks.Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Apply in the Configuration window restarts the engine on the configuration the app is running
        /// (the loaded file, plus the window's edits), not on a fresh default one.
        /// </summary>
        [AvaloniaTest]
        public void ApplyInTheConfigurationWindow_KeepsTheInterfacesAndRoutesFromTheLoadedFile()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-embedded-switch.json"));
            int xot = config.Routing.XotInterfaces.Count;
            int routes = config.Routing.RoutingTable.Count;
            Assert.That(xot, Is.GreaterThan(0));

            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, EmbeddedSwitchOptions.Off);
            QuietDialogs dialogs = new QuietDialogs();
            services.AddSingleton<IDialogService>(dialogs);
            using ServiceProvider provider = services.BuildServiceProvider();
            // The two server ports of the sample are fixed numbers; move them so the test never meets a real program.
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            provider.GetRequiredService<IRuntimeService>().Initialize(config);

            ConfigurationWindowViewModel vm = provider.GetRequiredService<ConfigurationWindowViewModel>();
            Assert.That(vm.X25LocalMachineId, Is.EqualTo("23430200"), "the window does not show the running configuration");
            vm.ApplySettingsCommand.Execute(null);
            Assert.That(dialogs.LastError, Is.Empty, dialogs.LastError);

            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            Assert.That(runtimeService.Runtime.Config.Routing.XotInterfaces.Count, Is.EqualTo(xot), "Apply lost the XOT interfaces");
            Assert.That(runtimeService.Runtime.Config.Routing.RoutingTable.Count, Is.EqualTo(routes), "Apply lost the routes");
            Assert.That(runtimeService.Runtime.Config.X25.LocalMachineId, Is.EqualTo("23430200"));
        }

        /// <summary>
        /// A client link whose far end is not there is shown as NOT REACHABLE, with the reason, before
        /// any call is made; with something listening it is shown as answering.
        /// </summary>
        [AvaloniaTest]
        public void AnXotLinkToAFarEnd_IsShownAsReachableOrNot_BeforeAnyCall()
        {
            int port = FreePort();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();

            WaitUntil(() => main.Interfaces[0].Status.StartsWith("NOT REACHABLE"),
                "a dead far end to be shown as not reachable", () => main.Interfaces[0].Status);
            Assert.That(main.Interfaces[0].Status, Does.Contain("NOT REACHABLE: "), "the reason is missing");

            // Now something starts listening: the next check shows it. (A check runs at each statistics update.)
            TcpListener listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            try
            {
                main.RefreshLinksNow();
                WaitUntil(() => main.Interfaces[0].Status.StartsWith("Far end answers"),
                    "a live far end to be shown as answering", () => main.Interfaces[0].Status);
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// A built-in switch that cannot start (its port is taken) says so in the main window.
        /// </summary>
        [AvaloniaTest]
        public void AnEmbeddedSwitchThatCannotStart_SaysSoInTheMainWindow()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            TcpListener taken = new TcpListener(IPAddress.Any, callers) { ExclusiveAddressUse = true };
            taken.Start();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedSwitchConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                options = new EmbeddedSwitchOptions { Enabled = true, ConfigFile = ini };
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                Assert.That(main.SwitchStatus, Does.Contain("NOT running"), main.SwitchStatus);
            }
            finally
            {
                taken.Stop();
                File.Delete(ini);
            }
        }

        private static ServiceProvider BuildServices(RuntimeConfig config, EmbeddedSwitchOptions? switchOptions = null)
        {
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, switchOptions);
            ServiceProvider provider = services.BuildServiceProvider();
            // Same order as the app: runtime first, then the built-in switch (it dials the runtime's listener).
            provider.GetRequiredService<IRuntimeService>().Initialize(config);
            provider.GetRequiredService<EmbeddedSwitchService>().StartIfEnabled();
            return provider;
        }
    }
}
