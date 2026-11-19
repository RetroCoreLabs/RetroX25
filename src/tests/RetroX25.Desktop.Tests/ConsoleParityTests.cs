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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Tests for the features the old text-mode console had and the Desktop app now has too: the
    /// packet window's decoded columns and filters, the Link Status window, the Status window's one
    /// statistics table (bad counts, Reset Statistics), the main window's up/down view,
    /// the TCP-LAPB configuration tab, the main window's function keys, the
    /// CSV export and the route test.
    /// </summary>
    /// <remarks>
    /// Every test here drives the real windows and view-models, headless. A test that needs a far end
    /// uses a small fake router (an XOT peer on loopback) or a real TCP-LAPB peer made with the same
    /// library, never a stand-in for the thing under test.
    /// </remarks>
    [TestFixture]
    public sealed class ConsoleParityTests
    {
        private const string SampleFile = "x25config-xot-to-switch.json";

        // ----------------------------------------------------------------------------------------
        // Small helpers (copied from PadWindowCallTests, which is not edited by this work).
        // ----------------------------------------------------------------------------------------


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

            public volatile bool ClearSeen;

            /// <summary>When set, a call is refused with a Clear Indication carrying this cause.</summary>
            public byte? RefuseCause { get; set; }

            /// <summary>Diagnostic octet of the refusing Clear Indication.</summary>
            public byte RefuseDiagnostic { get; set; }

            public volatile bool QBitDataSeen;

            // The connection that carried the call, so the test can push packets to the app later.
            private NetworkStream? _callStream;
            private byte _callLci;
            private int _lastPr;
            private readonly object _writeLock = new object();

            /// <summary>
            /// Sends a data packet to the app on the call's channel: this is data from the far end that
            /// the app did not ask for, with the M, D and Q bits as given.
            /// </summary>
            public void PushData(string text, bool mBit, bool dBit, bool qBit)
            {
                byte[] body = Encoding.ASCII.GetBytes(text);
                byte[] packet = new byte[3 + body.Length];
                packet[0] = (byte)(0x10 | (qBit ? 0x80 : 0) | (dBit ? 0x40 : 0));
                packet[1] = _callLci;
                lock (_writeLock)
                {
                    packet[2] = (byte)((_lastPr << 5) | (mBit ? 0x10 : 0) | (_sendSeq << 1));
                    _sendSeq = (_sendSeq + 1) & 0x07;
                    Buffer.BlockCopy(body, 0, packet, 3, body.Length);
                    Send(_callStream!, packet);
                }
            }

            /// <summary>
            /// Sends an X.25 packet to the app on the call's channel: the general format octet and the
            /// channel are put in front of <paramref name="rest"/>, which starts with the packet type.
            /// </summary>
            public void PushRaw(byte[] rest)
            {
                byte[] packet = new byte[2 + rest.Length];
                packet[0] = 0x10;
                packet[1] = _callLci;
                Buffer.BlockCopy(rest, 0, packet, 2, rest.Length);
                lock (_writeLock)
                {
                    Send(_callStream!, packet);
                }
            }

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

                    CalledAddress = digits.ToString();
                    _callStream = stream;
                    _callLci = lci;
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
                    _lastPr = pr;
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
        /// A dialog service that answers by itself and remembers what it was asked, so a test can see
        /// that a key press or a button reached the dialog without a window opening.
        /// </summary>
        private sealed class RecordingDialogs : IDialogService
        {
            public string LastInfoTitle = "";
            public string LastInfo = "";
            public int InfoCount;
            public string LastError = "";
            public int ErrorCount;
            public string LastConfirmTitle = "";
            public int ConfirmCount;
            public bool ConfirmAnswer = true;

            public void ShowInfo(string title, string message) { LastInfoTitle = title; LastInfo = message; InfoCount++; }

            public void ShowWarning(string title, string message) { }

            public void ShowError(string title, string message) { LastError = message; ErrorCount++; }

            public System.Threading.Tasks.Task<bool> ShowConfirmation(string title, string message)
            {
                LastConfirmTitle = title;
                ConfirmCount++;
                return System.Threading.Tasks.Task.FromResult(ConfirmAnswer);
            }

            public System.Threading.Tasks.Task<string?> ShowOpenFileDialog(string title, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);

            public System.Threading.Tasks.Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Builds the service graph the app starts on, with the recording dialogs in place of the
        /// real ones, starts the engine, and returns the provider (the caller disposes it).
        /// </summary>
        private static ServiceProvider BuildServices(RuntimeConfig config, RecordingDialogs? dialogs = null)
        {
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            if (dialogs != null)
            {
                services.AddSingleton<IDialogService>(dialogs);
            }

            ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IRuntimeService>().Initialize(config);
            return provider;
        }

        /// <summary>
        /// Shows a window and lets the layout settle.
        /// </summary>
        private static void ShowAndSettle(Window window)
        {
            window.Width = 1000;
            window.Height = 700;
            window.Show();
            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
        }

        /// <summary>
        /// Finds the first control of a type with a name anywhere below a root, or null.
        /// </summary>
        private static T? FindNamed<T>(Visual root, string name) where T : Control
        {
            List<Visual> all = new List<Visual>(root.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is T found && found.Name == name)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Starts a call from a PAD window through a fake router, with the packet window already open,
        /// and sends one command so there is data in both directions. Returns when the answer is in.
        /// </summary>
        private static void PlaceCallAndExchangeText(
            FakeRouter router, ServiceProvider provider, PacketWindowViewModel packetVm, out PADClientWindow padWindow)
        {
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, runtimeService, 1);
            padWindow = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            padWindow.Show();
            Dispatcher.UIThread.RunJobs();

            padWindow.KeyTextInput("CALL 23600001");
            padWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => router.CalledAddress != null, "the call to reach the router",
                () => viewModel.ConsoleText + "\n--- app log ---\n" + AppLog(provider));
            WaitUntil(() => viewModel.ConsoleText.Contains("R1>"), "the router's prompt in the window");

            padWindow.KeyTextInput("show clock");
            padWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => viewModel.ConsoleText.Contains("CLOCK-OK"), "the answer to show clock");
        }

        /// <summary>
        /// Returns the first line of the packet window with this X.25 type and direction, or null.
        /// </summary>
        private static PacketRowViewModel? FindRow(PacketWindowViewModel vm, string l3Type, Direction direction)
        {
            for (int i = 0; i < vm.Packets.Count; i++)
            {
                PacketRowViewModel row = vm.Packets[i];
                if (row.L3Type == l3Type && row.Direction == direction)
                {
                    return row;
                }
            }

            return null;
        }

        private static int CountRows(PacketWindowViewModel vm, Func<PacketRowViewModel, bool> test)
        {
            int count = 0;
            for (int i = 0; i < vm.Packets.Count; i++)
            {
                if (test(vm.Packets[i]))
                {
                    count++;
                }
            }

            return count;
        }

        // ----------------------------------------------------------------------------------------
        // Items 2, 3 and 13 (counts): the packet window decodes, filters, counts.
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// A real call over XOT: the Packet window shows each packet's X.25 type, channel, P(S)/P(R),
        /// flags (Q, D, M) in their own columns, and the detail pane gives the decoded fields, the
        /// facilities in hex and the M bit.
        /// </summary>
        [AvaloniaTest]
        public void PacketWindow_DecodesACall_IntoColumnsAndDetail()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);

            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            packetVm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(packetVm);
            ShowAndSettle(packetWindow);

            PlaceCallAndExchangeText(router, provider, packetVm, out PADClientWindow pad);

            // The far end sends a message in two parts: the first with the M bit set.
            router.PushData("part one ", mBit: true, dBit: false, qBit: false);
            WaitUntil(() => CountRows(packetVm, r => r.L3Type == "DATA" && r.Flags.Contains("M")) == 1,
                "a data packet with the M bit set to show an M in the Flags column",
                () => DumpRows(packetVm));

            PacketRowViewModel? call = null;
            WaitUntil(() => (call = FindRow(packetVm, "CALL-REQ", Direction.TX)) != null,
                "the Call Request line", () => DumpRows(packetVm));
            Assert.That(call!.Lcn, Is.Not.Empty, "no channel number on the Call Request");
            Assert.That(call.LcnValue, Is.Not.Null);
            Assert.That(call.Layer, Is.EqualTo(LayerType.X25));

            packetVm.SelectedPacket = call;
            string callDetail = packetVm.SelectedPacketDetail;
            Assert.That(callDetail, Does.Contain("X.25 packet: "), callDetail);
            Assert.That(callDetail, Does.Contain("Logical channel (LCN): " + call.Lcn), callDetail);
            Assert.That(callDetail, Does.Contain("Facilities (hex): "), callDetail);

            PacketRowViewModel? more = null;
            WaitUntil(() => (more = FindRowWithFlag(packetVm, "M")) != null, "the M row");
            Assert.That(more!.Direction, Is.EqualTo(Direction.RX));
            Assert.That(more.Sequence, Does.Match(@"^\d/\d$"), "P(S)/P(R) column: " + more.Sequence);
            packetVm.SelectedPacket = more;
            string dataDetail = packetVm.SelectedPacketDetail;
            Assert.That(dataDetail, Does.Contain("M bit: 1"), dataDetail);
            Assert.That(dataDetail, Does.Contain("P(S): "), dataDetail);
            Assert.That(dataDetail, Does.Contain("P(R): "), dataDetail);
            Assert.That(dataDetail, Does.Contain("D bit: 0"), dataDetail);

            // The columns are in the grid: read the headers of the real DataGrid.
            DataGrid? grid = null;
            List<Visual> all = new List<Visual>(packetWindow.GetVisualDescendants());
            for (int i = 0; i < all.Count && grid == null; i++)
            {
                grid = all[i] as DataGrid;
            }

            Assert.That(grid, Is.Not.Null, "no DataGrid in the Packet window");
            List<string> headers = new List<string>();
            for (int i = 0; i < grid!.Columns.Count; i++)
            {
                headers.Add(grid.Columns[i].Header?.ToString() ?? "");
            }

            string[] wanted = { "L2", "L3", "LCN", "N(S)/N(R) P(S)/P(R)", "Flags", "FCS" };
            for (int i = 0; i < wanted.Length; i++)
            {
                Assert.That(headers, Does.Contain(wanted[i]), "column missing: " + wanted[i] + " (have " + string.Join(" | ", headers) + ")");
            }

            pad.Close();
            packetWindow.Close();
        }

        private static PacketRowViewModel? FindRowWithFlag(PacketWindowViewModel vm, string flag)
        {
            for (int i = 0; i < vm.Packets.Count; i++)
            {
                if (vm.Packets[i].Flags.Contains(flag))
                {
                    return vm.Packets[i];
                }
            }

            return null;
        }

        private static string DumpRows(PacketWindowViewModel vm)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < vm.Packets.Count; i++)
            {
                PacketRowViewModel r = vm.Packets[i];
                text.Append(r.Id).Append(' ').Append(r.Layer).Append(' ').Append(r.Direction).Append(" L2=").Append(r.L2Name)
                    .Append(" L3=").Append(r.L3Type).Append(" LCN=").Append(r.Lcn).Append(" seq=").Append(r.Sequence)
                    .Append(" flags=").Append(r.Flags).Append(" fcs=").Append(r.Fcs).Append(" err=").Append(r.ErrorText).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// The far end refuses a call with cause 1 (number busy), diagnostic 0. The Clear line shows the
        /// code and its words for both.
        /// </summary>
        [AvaloniaTest]
        public void PacketWindow_ShowsCauseAndDiagnosticWithText_ForARefusedCall()
        {
            using FakeRouter router = new FakeRouter { RefuseCause = 1, RefuseDiagnostic = 0 };
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);

            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            packetVm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(packetVm);
            ShowAndSettle(packetWindow);

            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
            PADClientWindow pad = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            pad.Show();
            Dispatcher.UIThread.RunJobs();
            pad.KeyTextInput("CALL 23600001");
            pad.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            PacketRowViewModel? clear = null;
            WaitUntil(() => (clear = FindRow(packetVm, "CLEAR-REQ", Direction.RX)) != null,
                "the Clear line from the far end", () => DumpRows(packetVm) + "\n" + AppLog(provider));
            packetVm.SelectedPacket = clear;
            string detail = packetVm.SelectedPacketDetail;
            Assert.That(detail, Does.Contain("Cause: 0x01 (number busy)"), detail);
            Assert.That(detail, Does.Contain("Diagnostic: 0x00 (no additional information)"), detail);
            Assert.That(clear!.L3Group, Is.EqualTo("CLEAR"));

            pad.Close();
            packetWindow.Close();
        }

        /// <summary>
        /// LAPB frames (made by the engine's own simulate-receive call) show their class (I, S, U), their
        /// name, N(S)/N(R) and the frame check in the columns, and the I, S and U boxes hide and show them.
        /// </summary>
        [AvaloniaTest]
        public void PacketWindow_ShowsLapbFrames_AndTheIsuFiltersWork()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            PacketWindowViewModel vm = provider.GetRequiredService<PacketWindowViewModel>();
            vm.IsCollapsed = false;
            PacketWindow window = new PacketWindow(vm);
            ShowAndSettle(window);

            // SABM (U frame), RR (S frame) and an I frame with N(S)=2, N(R)=3.
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x3F }, 2);
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x21 }, 2);
            byte iControl = (byte)((3 << 5) | (2 << 1));
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, iControl, 0x10, 0x01, 0x0B }, 5);

            WaitUntil(() => vm.Packets.Count == 3, "three LAPB lines", () => DumpRows(vm));
            PacketRowViewModel? u = null;
            PacketRowViewModel? s = null;
            PacketRowViewModel? iRow = null;
            for (int i = 0; i < vm.Packets.Count; i++)
            {
                PacketRowViewModel r = vm.Packets[i];
                if (r.L2Token == "U") u = r;
                if (r.L2Token == "S") s = r;
                if (r.L2Token == "I") iRow = r;
            }

            Assert.That(u, Is.Not.Null, DumpRows(vm));
            Assert.That(s, Is.Not.Null, DumpRows(vm));
            Assert.That(iRow, Is.Not.Null, DumpRows(vm));
            Assert.That(u!.L2Name, Is.EqualTo("SABM"));
            Assert.That(s!.L2Name, Is.EqualTo("RR"));
            Assert.That(s.Sequence, Is.EqualTo("-/1"), "an RR with N(R)=1");
            Assert.That(iRow!.L2Name, Is.EqualTo("I"));
            Assert.That(iRow.Sequence, Is.EqualTo("2/3"), "N(S)/N(R) column of the I frame");
            Assert.That(iRow.Fcs, Is.EqualTo("OK"));

            vm.ShowUFrames = false;
            Assert.That(vm.Packets.Count, Is.EqualTo(2), "U off");
            Assert.That(CountRows(vm, r => r.L2Token == "U"), Is.EqualTo(0));
            Assert.That(vm.StatisticsText, Does.Contain("Hidden: 1"), vm.StatisticsText);
            vm.ShowSFrames = false;
            Assert.That(vm.Packets.Count, Is.EqualTo(1), "U and S off");
            vm.ShowIFrames = false;
            Assert.That(vm.Packets.Count, Is.EqualTo(0), "all three off");
            Assert.That(vm.StatisticsText, Does.Contain("Hidden: 3"), vm.StatisticsText);
            vm.ShowUFrames = true;
            vm.ShowSFrames = true;
            vm.ShowIFrames = true;
            Assert.That(vm.Packets.Count, Is.EqualTo(3), "all three on again");
            Assert.That(vm.StatisticsText, Does.Contain("Hidden: 0"), vm.StatisticsText);

            // The boxes in the window itself are tied to the same properties.
            CheckBox? boxI = FindNamed<CheckBox>(window, "FilterI");
            Assert.That(boxI, Is.Not.Null, "the I box is not in the window");
            boxI!.IsChecked = false;
            Assert.That(vm.ShowIFrames, Is.False, "the I box does not reach the view-model");
            Assert.That(vm.Packets.Count, Is.EqualTo(2));

            window.Close();
        }

        /// <summary>
        /// X.25 filters on a real call: the packet groups (DATA, CALL, CLEAR), the channel number, and
        /// errors only. The Hidden count follows.
        /// </summary>
        [AvaloniaTest]
        public void PacketWindow_FiltersByGroupChannelAndErrors()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);

            PacketWindowViewModel vm = provider.GetRequiredService<PacketWindowViewModel>();
            vm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(vm);
            ShowAndSettle(packetWindow);
            PlaceCallAndExchangeText(router, provider, vm, out PADClientWindow pad);

            WaitUntil(() => FindRow(vm, "CALL-REQ", Direction.TX) != null && CountRows(vm, r => r.L3Group == "DATA") >= 2,
                "call and data lines", () => DumpRows(vm));
            int total = vm.Packets.Count;
            int dataRows = CountRows(vm, r => r.L3Group == "DATA");
            int callRows = CountRows(vm, r => r.L3Group == "CALL");
            Assert.That(dataRows, Is.GreaterThanOrEqualTo(2));
            Assert.That(callRows, Is.GreaterThanOrEqualTo(2), "Call Request and Call Connected: " + DumpRows(vm));

            vm.ShowDataPackets = false;
            Assert.That(CountRows(vm, r => r.L3Group == "DATA"), Is.EqualTo(0), "DATA off");
            Assert.That(vm.Packets.Count, Is.EqualTo(total - dataRows));
            Assert.That(vm.StatisticsText, Does.Contain("Hidden: " + dataRows), vm.StatisticsText);
            vm.ShowDataPackets = true;

            vm.ShowCallPackets = false;
            Assert.That(CountRows(vm, r => r.L3Group == "CALL"), Is.EqualTo(0), "CALL off");
            vm.ShowCallPackets = true;
            Assert.That(vm.Packets.Count, Is.EqualTo(total));

            // Channel filter: the real channel keeps the X.25 lines, a wrong one hides everything.
            PacketRowViewModel call = FindRow(vm, "CALL-REQ", Direction.TX)!;
            vm.LcnFilterText = call.Lcn;
            Assert.That(vm.LcnFilterValue, Is.EqualTo(call.LcnValue));
            Assert.That(vm.Packets.Count, Is.GreaterThan(0), "the real channel hides everything");
            Assert.That(CountRows(vm, r => r.Lcn != call.Lcn), Is.EqualTo(0), "a line from another channel is shown");
            int wrong = (call.LcnValue!.Value + 7) % 4096;
            vm.LcnFilterText = wrong.ToString();
            Assert.That(vm.Packets.Count, Is.EqualTo(0), "a wrong channel should hide every line");
            vm.LcnFilterText = "";
            Assert.That(vm.Packets.Count, Is.EqualTo(total));

            // Errors only: a clean call has no error lines.
            vm.ErrorsOnly = true;
            Assert.That(vm.Packets.Count, Is.EqualTo(0), "a clean call shows error lines: " + DumpRows(vm));
            vm.ErrorsOnly = false;
            Assert.That(vm.Packets.Count, Is.EqualTo(total));

            pad.Close();
            packetWindow.Close();
        }

        /// <summary>
        /// A packet that cannot be read is an error line: it shows in the Errors column text, the
        /// Errors-only filter finds it, the count in the status line counts it, and the detail says why.
        /// </summary>
        [AvaloniaTest]
        public void PacketWindow_MarksAnUnreadablePacketAsAnError_AndErrorsOnlyFindsIt()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);

            PacketWindowViewModel vm = provider.GetRequiredService<PacketWindowViewModel>();
            vm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(vm);
            ShowAndSettle(packetWindow);
            PlaceCallAndExchangeText(router, provider, vm, out PADClientWindow pad);

            // A Clear Indication with no clearing cause: X.25 needs the cause octet, so the parser
            // reports it as unreadable (X.25 Figure 5-6, octet 5).
            router.PushRaw(new byte[] { 0x13 });
            WaitUntil(() => CountRows(vm, r => r.IsError) >= 1, "an error line for the unreadable packet",
                () => DumpRows(vm) + "\n" + AppLog(provider));

            vm.ErrorsOnly = true;
            Assert.That(vm.Packets.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(CountRows(vm, r => !r.IsError), Is.EqualTo(0), "Errors-only shows a clean line");
            Assert.That(vm.StatisticsText, Does.Contain("Errors: " + vm.Packets.Count), vm.StatisticsText);
            vm.SelectedPacket = vm.Packets[0];
            Assert.That(vm.SelectedPacketDetail, Does.Contain("ERROR: "), vm.SelectedPacketDetail);

            pad.Close();
            packetWindow.Close();
        }

        /// <summary>
        /// A frame with a bad frame check shows FCS BAD, is an error line, and the detail says so.
        /// (The runtime never makes such a line on a TCP link, so the line is built directly.)
        /// </summary>
        [Test]
        public void PacketRow_WithABadFrameCheck_ShowsBadAndIsAnError()
        {
            HdlcHistoryItem bad = new HdlcHistoryItem(Direction.RX, new byte[] { 0x01, 0x3F }, false, 7);
            bad.Id = 7;
            PacketRowViewModel row = new PacketRowViewModel(bad, false);

            Assert.That(row.Fcs, Is.EqualTo("BAD"));
            Assert.That(row.IsError, Is.True);
            Assert.That(row.BuildDetailText(), Does.Contain("BAD"), row.BuildDetailText());

            HdlcHistoryItem good = new HdlcHistoryItem(Direction.RX, new byte[] { 0x01, 0x3F }, true, 8);
            PacketRowViewModel okRow = new PacketRowViewModel(good, false);
            Assert.That(okRow.Fcs, Is.EqualTo("OK"));
            Assert.That(okRow.IsError, Is.False);
        }


        // ----------------------------------------------------------------------------------------
        // Item 6: Clear Packet History, Reset Statistics.
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Clear Packet History hides the packets so far in every Packet window, open or opened later;
        /// new packets show again; the engine's own history is NOT emptied (the text says "hides");
        /// after an engine restart the mark starts again.
        /// </summary>
        [AvaloniaTest]
        public void ClearPacketHistory_HidesInOpenAndNewWindows_AndTheEngineKeepsItsHistory()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();

            PacketWindowViewModel first = provider.GetRequiredService<PacketWindowViewModel>();
            first.IsCollapsed = false;
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x3F }, 2);
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x21 }, 2);
            WaitUntil(() => first.Packets.Count == 2, "two lines before the clear", () => DumpRows(first));

            main.ClearPacketHistoryCommand.Execute(null);
            WaitUntil(() => first.Packets.Count == 0, "the open window to hide its lines", () => DumpRows(first));
            Assert.That(runtimeService.Runtime.GetHistory().Count, Is.EqualTo(2), "the engine's history must be untouched");

            PacketWindowViewModel second = provider.GetRequiredService<PacketWindowViewModel>();
            second.IsCollapsed = false;
            Assert.That(second.Packets.Count, Is.EqualTo(0), "a window opened later shows the cleared lines");

            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x3F }, 2);
            WaitUntil(() => first.Packets.Count == 1 && second.Packets.Count == 1, "the new line in both windows",
                () => first.Packets.Count + " / " + second.Packets.Count);

            // Engine restart: the numbers start again, so the mark is reset and a new line shows.
            runtimeService.Restart(App.CreateDefaultConfig());
            PacketWindowViewModel third = provider.GetRequiredService<PacketWindowViewModel>();
            third.IsCollapsed = false;
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x3F }, 2);
            WaitUntil(() => third.Packets.Count == 1, "a line after the restart", () => DumpRows(third));
        }

        // ----------------------------------------------------------------------------------------
        // Item 10: packet export, TXT and CSV, honoring the filters.
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Exports to a .csv file: the header, one line per shown packet only (a filter that hides a
        /// frame class keeps it out of the file), commas inside text quoted. A .txt file gets the
        /// readable form with the same filter applied.
        /// </summary>
        [AvaloniaTest]
        public void PacketExport_WritesCsvAndText_WithTheFiltersApplied()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            PacketWindowViewModel vm = provider.GetRequiredService<PacketWindowViewModel>();
            vm.IsCollapsed = false;
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x3F }, 2);
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x21 }, 2);
            byte iControl = (byte)((3 << 5) | (2 << 1));
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, iControl, 0x41, 0x42 }, 4);
            WaitUntil(() => vm.Packets.Count == 3, "three lines", () => DumpRows(vm));

            string folder = Path.Combine(Path.GetTempPath(), "RetroX25-parity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string csvAll = Path.Combine(folder, "all.csv");
                vm.ExportToFile(csvAll);
                string[] lines = File.ReadAllLines(csvAll);
                Assert.That(lines.Length, Is.EqualTo(4), "header plus three lines: " + string.Join("\n", lines));
                Assert.That(lines[0], Is.EqualTo("Id,Time,Layer,Direction,L2,L3,LCN,Seq,Flags,FCS,Error,Length,Description,Hex,Ascii"));
                Assert.That(string.Join("\n", lines), Does.Contain(",\"SABM\","));
                Assert.That(string.Join("\n", lines), Does.Contain(",\"RR\","));
                Assert.That(string.Join("\n", lines), Does.Contain(",\"2/3\","), "N(S)/N(R) of the I frame");

                // A filter keeps a frame class out of the file.
                vm.ShowSFrames = false;
                string csvFiltered = Path.Combine(folder, "filtered.csv");
                vm.ExportToFile(csvFiltered);
                string[] filtered = File.ReadAllLines(csvFiltered);
                Assert.That(filtered.Length, Is.EqualTo(3), string.Join("\n", filtered));
                Assert.That(string.Join("\n", filtered), Does.Not.Contain(",\"RR\","), "the hidden RR frame is in the file");
                Assert.That(string.Join("\n", filtered), Does.Contain(",\"SABM\","));

                // The text file: the same two lines and the figures line.
                string txt = Path.Combine(folder, "some.txt");
                vm.ExportToFile(txt);
                string text = File.ReadAllText(txt);
                Assert.That(text, Does.Contain("Total Packets: 2"), text);
                Assert.That(text, Does.Contain("Hidden: 1"), text);
                Assert.That(text, Does.Not.Contain("RR"), "the hidden frame is in the text file");

                // Text fields are always written in quotes, so a comma inside one cannot split a line.
                Assert.That(vm.BuildExport(true).Split('\n')[0], Does.StartWith("Id,"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Item 5: bad frame and packet counts.
        // ----------------------------------------------------------------------------------------

        private static StatisticsSnapshot SnapshotWithBadCounts()
        {
            StatisticsSnapshot snap = new StatisticsSnapshot();
            snap.HDLC.BadFramesReceived = 11;
            snap.HDLC.BadFramesTransmitted = 12;
            snap.LAPB.BadFramesReceived = 13;
            snap.LAPB.BadFramesTransmitted = 14;
            snap.X25.BadPacketsReceived = 15;
            snap.X25.BadPacketsTransmitted = 16;
            return snap;
        }

        // ----------------------------------------------------------------------------------------
        // Statistics have ONE place: the Status window (F1). The main window says "is it up?", the
        // Link Status window controls links, the Packet window counts its own capture.
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// All text a window shows in text blocks (the text of the block and of every run in it), so a
        /// test can say what is on the screen and what is not.
        /// </summary>
        private static string AllText(Visual root)
        {
            StringBuilder text = new StringBuilder();
            List<Visual> all = new List<Visual>(root.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is TextBlock block)
                {
                    if (!string.IsNullOrEmpty(block.Text))
                    {
                        text.Append(block.Text).Append('\n');
                    }

                    if (block.Inlines != null)
                    {
                        for (int j = 0; j < block.Inlines.Count; j++)
                        {
                            if (block.Inlines[j] is Run run && run.Text != null)
                            {
                                text.Append(run.Text);
                            }
                        }

                        text.Append('\n');
                    }
                }
            }

            return text.ToString();
        }

        private static int RowIndex(StatisticsTable table, string label)
        {
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (table.Rows[i].Label == label)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The text of a cell as it is on the Status window's screen, found by the cell's control name.
        /// </summary>
        private static string? CellOnScreen(Window window, StatisticsTable table, string label, string header)
        {
            int row = RowIndex(table, label);
            int column = table.ColumnIndex(header);
            if (row < 0 || column < 0)
            {
                return null;
            }

            return FindNamed<TextBlock>(window, "Cell_" + row + "_" + column)?.Text;
        }

        /// <summary>
        /// Finds a menu item by its header text anywhere in the window's logical tree. (The items of a
        /// closed menu are not in the visual tree, so the logical tree is searched.)
        /// </summary>
        private static MenuItem? FindMenuItem(Window window, string header)
        {
            List<Avalonia.LogicalTree.ILogical> all = new List<Avalonia.LogicalTree.ILogical>(
                Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window));
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is MenuItem item && item.Header?.ToString() == header)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// Presses a button the way a person does: a mouse press and release in the middle of it.
        /// </summary>
        private static void ClickButton(Window window, Button button)
        {
            Point? centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.That(centre, Is.Not.Null, "the button is not on the screen: " + button.Name);
            window.MouseDown(centre!.Value, MouseButton.Left);
            window.MouseUp(centre.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// A config with the sample XOT client link and one TCP-LAPB server link, so the Status table has
        /// two kinds of column besides the total.
        /// </summary>
        private static RuntimeConfig ConfigWithXotAndTcpLapb(int xotPort)
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = xotPort;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            TcpLapbInterfaceConfig lapb = new TcpLapbInterfaceConfig
            {
                Name = "LAPB1",
                Enabled = true,
                Mode = RetroIO.Link.TcpMode.Server,
                Port = FreePort(),
                RemoteMachineId = "201"
            };
            config.Routing.TcpLapbInterfaces.Add(lapb);
            return config;
        }

        /// <summary>
        /// Reset Statistics, from the Status window's button or from the View menu: a real call makes the
        /// engine's counters non-zero, the reset zeroes them in the ENGINE (not just on the screen), and
        /// a new call counts again from 1.
        /// </summary>
        private static void RunResetFlow(bool fromMenu)
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            string xot = config.Routing.XotInterfaces[0].Name;
            using ServiceProvider provider = BuildServices(config);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();

            StatusWindowViewModel statusVm = provider.GetRequiredService<StatusWindowViewModel>();
            StatusWindow statusWindow = new StatusWindow(statusVm);
            ShowAndSettle(statusWindow);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow mainWindow = new MainWindow { DataContext = main };
            ShowAndSettle(mainWindow);

            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            PlaceCallAndExchangeText(router, provider, packetVm, out PADClientWindow pad);

            // 1. The counters are not zero: one Call Request went out, packets went both ways, and the
            //    XOT interface has moved bytes. Read from the engine and from the table on the screen.
            WaitUntil(() => runtimeService.Runtime.GetStatistics().X25.PacketsReceived > 0, "packets counted by the engine");
            statusVm.Refresh();
            Assert.That(runtimeService.Runtime.GetStatistics().X25.CallRequestsTransmitted, Is.EqualTo(1));
            Assert.That(statusVm.Table.Cell("X.25 call requests sent", "Total"), Is.EqualTo("1"));
            Assert.That(CellOnScreen(statusWindow, statusVm.Table, "X.25 call requests sent", "Total"), Is.EqualTo("1"));
            Assert.That(statusVm.Table.Cell("X.25 packets sent", "Total"), Is.Not.EqualTo("0"));
            Assert.That(statusVm.Table.Cell("Bytes sent", xot), Is.Not.EqualTo("0"), "the XOT interface counted no bytes");

            // 2. Reset.
            if (fromMenu)
            {
                MenuItem? item = FindMenuItem(mainWindow, "_Reset Statistics");
                Assert.That(item, Is.Not.Null, "the View menu has no Reset Statistics item");
                Assert.That(item!.Command, Is.Not.Null, "the menu item has no command");
                Assert.That(item.Command!.CanExecute(null), Is.True);
                item.Command.Execute(null);
            }
            else
            {
                Button? reset = FindNamed<Button>(statusWindow, "ResetButton");
                Assert.That(reset, Is.Not.Null, "the Status window has no Reset Statistics button");
                Assert.That(reset!.Content?.ToString(), Is.EqualTo("Reset Statistics"));
                ClickButton(statusWindow, reset);
                Assert.That(statusVm.ActionText, Does.StartWith("Counters reset"), "the window does not say it reset");
            }

            // 3. The ENGINE's counters are zero (asked from the engine itself), and so are the interface's.
            RetroIO.Protocols.X25.Models.StatisticsSnapshot after = runtimeService.Runtime.GetStatistics();
            Assert.That(after.X25.CallRequestsTransmitted, Is.EqualTo(0), "the engine still counts the call request");
            Assert.That(after.X25.PacketsTransmitted, Is.EqualTo(0), "the engine still counts sent packets");
            Assert.That(after.X25.PacketsReceived, Is.EqualTo(0), "the engine still counts received packets");
            Assert.That(after.X25.DataPacketsTransmitted + after.X25.DataPacketsReceived, Is.EqualTo(0));
            Assert.That(runtimeService.Runtime.TransportInterfaces[xot].GetStatistics().BytesTransmitted, Is.EqualTo(0),
                "the interface still counts bytes");
            Assert.That(runtimeService.Runtime.TransportInterfaces[xot].GetStatistics().BytesReceived, Is.EqualTo(0));

            // And the Status window shows the zeros.
            statusVm.Refresh();
            Assert.That(CellOnScreen(statusWindow, statusVm.Table, "X.25 call requests sent", "Total"), Is.EqualTo("0"));
            Assert.That(CellOnScreen(statusWindow, statusVm.Table, "X.25 packets sent", "Total"), Is.EqualTo("0"));
            Assert.That(CellOnScreen(statusWindow, statusVm.Table, "Bytes sent", xot), Is.EqualTo("0"));

            // 4. A new call counts again from 1 (not 2: the old count is gone, not hidden).
            PADClientWindowViewModel secondVm = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, runtimeService, 2);
            PADClientWindow secondPad = new PADClientWindow(secondVm) { Width = 900, Height = 600 };
            secondPad.Show();
            // Another window is open, so this one has to be made the active one for the keys to reach it.
            secondPad.Activate();
            // A mouse click on the Status window's button leaves the keyboard focus on that button, and
            // keys go to the focused control, so the focus is cleared and given to this window.
            statusWindow.FocusManager?.ClearFocus();
            secondPad.Focus();
            Dispatcher.UIThread.RunJobs();
            secondPad.KeyTextInput("CALL 23600001");
            secondPad.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => runtimeService.Runtime.GetStatistics().X25.CallRequestsTransmitted >= 1,
                "the new call to be counted", () => secondVm.ConsoleText + "\n" + AppLog(provider));
            Assert.That(runtimeService.Runtime.GetStatistics().X25.CallRequestsTransmitted, Is.EqualTo(1), "the new call did not count from 1");
            statusVm.Refresh();
            Assert.That(CellOnScreen(statusWindow, statusVm.Table, "X.25 call requests sent", "Total"), Is.EqualTo("1"));

            secondPad.Close();
            pad.Close();
            mainWindow.Close();
            statusWindow.Close();
        }

        /// <summary>
        /// The Reset Statistics button on the Status window, pressed with the mouse, zeroes the engine's
        /// counters; a new call counts from 1.
        /// </summary>
        [AvaloniaTest]
        public void ResetStatistics_Button_ZeroesTheEngineCounters_AndANewCallCountsFromOne()
        {
            RunResetFlow(false);
        }

        /// <summary>
        /// The View > Reset Statistics menu item of the main window zeroes the engine's counters; a new
        /// call counts from 1.
        /// </summary>
        [AvaloniaTest]
        public void ResetStatistics_MenuItem_ZeroesTheEngineCounters_AndANewCallCountsFromOne()
        {
            RunResetFlow(true);
        }

        /// <summary>
        /// The View menu offers Reset Statistics.
        /// </summary>
        [AvaloniaTest]
        public void ViewMenu_OffersResetStatistics()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);

            Assert.That(FindMenuItem(window, "_View"), Is.Not.Null, "no View menu");
            Assert.That(FindMenuItem(window, "_Reset Statistics"), Is.Not.Null, "no Reset Statistics item");
            Assert.That(main.GetType().GetProperty("ResetStatisticsCommand"), Is.Not.Null);
            window.Close();
        }

        /// <summary>
        /// The Status table has a column for the total, one for each link and one for each transport
        /// interface, on the screen and in the table; a link's LAPB figures and an interface's call figures
        /// sit in their own columns, and a counter a column does not have shows "-". The byte total is not
        /// counted twice.
        /// </summary>
        [AvaloniaTest]
        public void StatusTable_HasAColumnForEachLinkAndInterface()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = ConfigWithXotAndTcpLapb(router.Port);
            string xot = config.Routing.XotInterfaces[0].Name;
            using ServiceProvider provider = BuildServices(config);

            StatusWindowViewModel vm = provider.GetRequiredService<StatusWindowViewModel>();
            StatusWindow window = new StatusWindow(vm);
            ShowAndSettle(window);
            PlaceCallAndExchangeText(router, provider, provider.GetRequiredService<PacketWindowViewModel>(), out PADClientWindow pad);
            vm.Refresh();

            StatisticsTable table = vm.Table;
            Assert.That(table.Headers, Is.EqualTo(new List<string> { "Total", "LAPB1", xot }), string.Join(" | ", table.Headers));

            // The headers are on the screen too.
            Assert.That(FindNamed<TextBlock>(window, "Header_0")?.Text, Is.EqualTo("Total"));
            Assert.That(FindNamed<TextBlock>(window, "Header_1")?.Text, Is.EqualTo("LAPB1"));
            Assert.That(FindNamed<TextBlock>(window, "Header_2")?.Text, Is.EqualTo(xot));

            // The link (LAPB1) has the LAPB figures and its state; the XOT interface has none of them.
            Assert.That(table.Cell("LAPB I frames received", "LAPB1"), Does.Match(@"^\d+$"));
            Assert.That(table.Cell("LAPB retransmissions", "LAPB1"), Does.Match(@"^\d+$"));
            Assert.That(table.Cell("LAPB state", "LAPB1"), Is.EqualTo("Disconnected"));
            Assert.That(table.Cell("LAPB I frames received", xot), Is.EqualTo("-"));
            Assert.That(table.Cell("LAPB state", xot), Is.EqualTo("-"));

            // The interface has the call and connection figures; the link has none of them.
            Assert.That(table.Cell("Outbound calls tried", xot), Does.Match(@"^\d+$"));
            Assert.That(table.Cell("Open connections", xot), Does.Match(@"^\d+$"));
            Assert.That(table.Cell("Outbound calls tried", "LAPB1"), Is.EqualTo("-"));
            Assert.That(table.Cell("Link frames received", "LAPB1"), Does.Match(@"^\d+$"));

            // There is no runtime-wide LAPB state: the Total cell of that row is not a state word.
            Assert.That(table.Cell("LAPB state", "Total"), Is.EqualTo("-"));

            // The total comes from the engine; the same figures are on the screen.
            RetroIO.Protocols.X25.Models.StatisticsSnapshot engine = provider.GetRequiredService<IRuntimeService>().Runtime.GetStatistics();
            Assert.That(table.Cell("X.25 call requests sent", "Total"), Is.EqualTo(engine.X25.CallRequestsTransmitted.ToString()));
            Assert.That(CellOnScreen(window, table, "X.25 call requests sent", "Total"), Is.EqualTo("1"));
            Assert.That(CellOnScreen(window, table, "LAPB state", "LAPB1"), Is.EqualTo("Disconnected"));

            // No double counting: the byte total is the link's bytes plus the interface's bytes, each once.
            long linkBytes = long.Parse(table.Cell("Bytes sent", "LAPB1") ?? "x");
            long xotBytes = long.Parse(table.Cell("Bytes sent", xot) ?? "x");
            long totalBytes = long.Parse(table.Cell("Bytes sent", "Total") ?? "x");
            Assert.That(xotBytes, Is.GreaterThan(0), "the call moved no bytes on the XOT interface");
            Assert.That(totalBytes, Is.EqualTo(linkBytes + xotBytes), "a byte is counted twice (or not at all) in the total");

            pad.Close();
            window.Close();
        }

        /// <summary>
        /// The Status window shows the bad counts of HDLC, LAPB and X.25, received and sent, in its table,
        /// and calls LAPB retransmissions by that name (not "errors"). (The engine can hardly be made to
        /// count a bad frame, so the figures are put into the table the way the engine's statistics hold them.)
        /// </summary>
        [AvaloniaTest]
        public void StatusWindow_ShowsTheBadCounts_ForEveryLayerAndDirection()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            StatusWindowViewModel vm = provider.GetRequiredService<StatusWindowViewModel>();
            StatusWindow window = new StatusWindow(vm);
            ShowAndSettle(window);

            vm.Table = StatisticsReader.Build(SnapshotWithBadCounts(), new RetroIO.Link.ILink[0], new List<RetroIO.Protocols.X25.Transport.ITransportInterface>());
            Dispatcher.UIThread.RunJobs();

            string[] labels =
            {
                "HDLC bad frames received", "HDLC bad frames sent",
                "LAPB bad frames received", "LAPB bad frames sent",
                "X.25 bad packets received", "X.25 bad packets sent"
            };
            string[] expected = { "11", "12", "13", "14", "15", "16" };
            for (int i = 0; i < labels.Length; i++)
            {
                Assert.That(CellOnScreen(window, vm.Table, labels[i], "Total"), Is.EqualTo(expected[i]), labels[i]);
            }

            // The misleading name is gone: retransmissions are called retransmissions, and no row says "errors" for them.
            Assert.That(vm.Table.FindRow("LAPB retransmissions"), Is.Not.Null);
            string screen = AllText(window);
            Assert.That(screen, Does.Not.Contain("LAPB errors"));
            Assert.That(screen, Does.Contain("LAPB retransmissions"));
            window.Close();
        }

        /// <summary>
        /// The table builder on its own: figures in the engine's snapshot come out in the Total column, a
        /// table without links or interfaces has just the Total column, and a counter that the Total column
        /// does not have a runtime-wide figure for ("LAPB state") is "-".
        /// </summary>
        [Test]
        public void StatisticsReader_PutsTheEngineFigures_InTheTotalColumn()
        {
            StatisticsSnapshot snap = new StatisticsSnapshot();
            snap.Uptime = TimeSpan.FromSeconds(125);
            snap.HDLC.FramesReceived = 21;
            snap.HDLC.FramesTransmitted = 22;
            snap.HDLC.BytesReceived = 23;
            snap.HDLC.BytesTransmitted = 24;
            snap.HDLC.FramesInvalidFcs = 25;
            snap.LAPB.IFramesReceived = 31;
            snap.LAPB.SFramesTransmitted = 32;
            snap.LAPB.UFramesReceived = 33;
            snap.LAPB.CurrentState = "DataTransfer";
            snap.X25.PacketsReceived = 41;
            snap.X25.PacketsTransmitted = 42;
            snap.X25.CallRequestsReceived = 43;
            snap.X25.CallRequestsTransmitted = 44;
            snap.X25.DataPacketsReceived = 45;
            snap.X25.DataPacketsTransmitted = 46;
            snap.X25.ActiveVirtualCircuits = 3;
            snap.X25.TotalVirtualCircuits = 47;

            StatisticsTable table = StatisticsReader.Build(snap, new RetroIO.Link.ILink[0], new List<RetroIO.Protocols.X25.Transport.ITransportInterface>());

            Assert.That(table.Headers, Is.EqualTo(new List<string> { "Total" }));
            Assert.That(table.UptimeText, Is.EqualTo("2m 5s"));
            string[] labels =
            {
                "HDLC frames received", "HDLC frames sent", "HDLC bytes received", "HDLC bytes sent", "HDLC frames with bad FCS",
                "LAPB I frames received", "LAPB S frames sent", "LAPB U frames received",
                "X.25 packets received", "X.25 packets sent", "X.25 call requests received", "X.25 call requests sent",
                "X.25 data packets received", "X.25 data packets sent", "X.25 active virtual circuits", "X.25 virtual circuits in total"
            };
            string[] expected = { "21", "22", "23", "24", "25", "31", "32", "33", "41", "42", "43", "44", "45", "46", "3", "47" };
            for (int i = 0; i < labels.Length; i++)
            {
                Assert.That(table.Cell(labels[i], "Total"), Is.EqualTo(expected[i]), labels[i]);
            }

            // The engine's own LAPB state word is NOT shown: each link has its own state.
            Assert.That(table.Cell("LAPB state", "Total"), Is.EqualTo("-"));
            Assert.That(table.Cell("no such counter", "Total"), Is.Null);
            Assert.That(table.Cell("HDLC frames received", "no such column"), Is.Null);
        }

        /// <summary>
        /// The main window answers "is it up?" and shows no counters: no byte or frame totals, no protocol
        /// statistics, no routing summary. A link whose far end answers shows up and "All links up".
        /// </summary>
        [AvaloniaTest]
        public void MainWindow_ShowsNoCounters_ButShowsUp()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);

            WaitUntil(() => main.LinkSummary == "All links up", "the main window to say all links are up",
                () => main.LinkSummary + " / " + (main.Interfaces.Count > 0 ? main.Interfaces[0].Status : "(no lines)"));
            Assert.That(main.AllLinksUp, Is.True);
            Assert.That(main.Interfaces[0].Health, Is.EqualTo(LinkHealth.Up));

            string screen = AllText(window);
            Assert.That(screen, Does.Contain("Uptime"));
            Assert.That(screen, Does.Contain("All links up"));
            Assert.That(screen, Does.Contain(config.Routing.XotInterfaces[0].Name));
            Assert.That(screen, Does.Contain("Host Information"));

            // Nothing that is a counter. (The words below are the labels the old counter panels used.)
            string[] counterWords = { "Protocol Statistics", "HDLC", "I-Frames", "Retx", "Timeouts", "Bad frames", "Bad packets",
                "Active VCs", "RX:", "TX:", "FCS", "All Interfaces:", "Routing:", "Active Routes", "frames)" };
            for (int i = 0; i < counterWords.Length; i++)
            {
                Assert.That(screen, Does.Not.Contain(counterWords[i]), "the main window shows a counter: " + counterWords[i] + "\n" + screen);
            }

            // And the view-model no longer carries counter properties that no screen uses.
            string[] gone = { "X25PacketsReceived", "HdlcFramesReceived", "LapbIFramesSent", "AggregateRxBytes", "AggregateFcsErrors",
                "ComPortStatus", "HdlcStatus", "LapbStatus", "X25Status", "RoutingSummary", "ActiveRoutes", "TotalRoutes", "LapbCurrentState" };
            for (int i = 0; i < gone.Length; i++)
            {
                Assert.That(main.GetType().GetProperty(gone[i]), Is.Null, "a counter property is still there: " + gone[i]);
            }

            window.Close();
        }

        /// <summary>
        /// A link whose far end does not answer shows down, and the main window says how many links are down.
        /// </summary>
        [AvaloniaTest]
        public void MainWindow_SaysHowManyLinksAreDown()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = FreePort();
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);

            WaitUntil(() => main.LinkSummary == "1 link(s) down", "the main window to say one link is down",
                () => main.LinkSummary + " / " + (main.Interfaces.Count > 0 ? main.Interfaces[0].Status : "(no lines)"));
            Assert.That(main.AllLinksUp, Is.False);
            Assert.That(main.Interfaces[0].Health, Is.EqualTo(LinkHealth.Down));
            Assert.That(main.Interfaces[0].Status, Does.StartWith("NOT REACHABLE"));
            Assert.That(AllText(window), Does.Contain("1 link(s) down"));
            window.Close();
        }

        /// <summary>
        /// A TCP-LAPB link has a line in the main window with its name, its state and its LAPB link state,
        /// and counts as down while its LAPB link is not in data transfer.
        /// </summary>
        [AvaloniaTest]
        public void MainWindow_ListsATcpLapbLink_WithItsLapbState()
        {
            RuntimeConfig config = ConfigWithXotAndTcpLapb(FreePort());
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);

            InterfaceStatusViewModel? line = null;
            for (int i = 0; i < main.Interfaces.Count; i++)
            {
                if (main.Interfaces[i].Name == "LAPB1")
                {
                    line = main.Interfaces[i];
                }
            }

            Assert.That(line, Is.Not.Null, "no line for the TCP-LAPB link");
            Assert.That(line!.Type, Is.EqualTo("TCP-LAPB"));
            Assert.That(line.RemoteMachineId, Is.EqualTo("201"));
            Assert.That(line.Health, Is.EqualTo(LinkHealth.Down));
            Assert.That(line.LapbState, Does.StartWith("Down"));
            Assert.That(AllText(window), Does.Contain("LAPB1"));
            Assert.That(AllText(window), Does.Contain("LAPB link: Down"));
            window.Close();
        }

        /// <summary>
        /// The Link Status window is for control and state only: the list, the buttons and the details
        /// (state, LAPB state, V(S) V(R) V(A), remote machine), and NO counter at all, after a call that
        /// made every counter non-zero. There is no byte total in the top bar.
        /// </summary>
        [AvaloniaTest]
        public void LinkStatusWindow_ShowsNoCounters_OnlyControlAndState()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = ConfigWithXotAndTcpLapb(router.Port);
            using ServiceProvider provider = BuildServices(config);
            PlaceCallAndExchangeText(router, provider, provider.GetRequiredService<PacketWindowViewModel>(), out PADClientWindow pad);

            LinkStatusWindowViewModel vm = provider.GetRequiredService<LinkStatusWindowViewModel>();
            LinkStatusWindow window = new LinkStatusWindow(vm);
            ShowAndSettle(window);

            Assert.That(FindNamed<TextBlock>(window, "BytesText"), Is.Null, "the byte total is still in the top bar");
            Assert.That(FindNamed<Button>(window, "StartButton"), Is.Not.Null);
            Assert.That(FindNamed<Button>(window, "StopButton"), Is.Not.Null);
            Assert.That(FindNamed<Button>(window, "ConnectButton"), Is.Not.Null);
            Assert.That(FindNamed<Button>(window, "DisconnectButton"), Is.Not.Null);
            Assert.That(FindNamed<Button>(window, "RefreshButton"), Is.Not.Null);

            string[] counterWords = { "Bytes", "bytes", "Frames", "frames", "Errors", "errors", "calls", "Calls", "Retransmissions",
                "retransmissions", "timeouts", "Timeouts", "Total:", "connections", "Window K", "unacknowledged" };

            for (int row = 0; row < vm.Rows.Count; row++)
            {
                vm.SelectedRow = vm.Rows[row];
                Dispatcher.UIThread.RunJobs();
                string detail = vm.DetailText;
                Assert.That(detail, Does.Contain("Name: " + vm.Rows[row].Name), detail);
                Assert.That(detail, Does.Contain("Status: "), detail);
                for (int i = 0; i < counterWords.Length; i++)
                {
                    Assert.That(detail, Does.Not.Contain(counterWords[i]), "a counter in the details of " + vm.Rows[row].Name + ": " + counterWords[i] + "\n" + detail);
                }

                if (vm.Rows[row].Name == "LAPB1")
                {
                    Assert.That(detail, Does.Contain("LAPB state: "), detail);
                    Assert.That(detail, Does.Contain("V(S)="), detail);
                    Assert.That(detail, Does.Contain("V(R)="), detail);
                    Assert.That(detail, Does.Contain("V(A)="), detail);
                    Assert.That(detail, Does.Contain("Remote machine: 201"), detail);
                }
            }

            Assert.That(vm.SummaryText, Does.Contain("Active"));
            string screen = AllText(window);
            Assert.That(screen, Does.Not.Contain("Total:"), screen);
            Assert.That(screen, Does.Not.Contain("received |"), screen);

            pad.Close();
            window.Close();
        }

        /// <summary>
        /// The Packet window's bottom bar says its counts are for the packets captured in this window, so
        /// nobody reads them as the engine's totals (those are in the Status window).
        /// </summary>
        [AvaloniaTest]
        public void PacketWindow_BottomBar_SaysTheCountsAreForThisCapture()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            PacketWindowViewModel vm = provider.GetRequiredService<PacketWindowViewModel>();
            vm.IsCollapsed = false;
            PacketWindow window = new PacketWindow(vm);
            ShowAndSettle(window);

            Assert.That(vm.StatisticsText, Does.StartWith("In this capture:"), vm.StatisticsText);
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x3F }, 2);
            WaitUntil(() => vm.Packets.Count == 1, "a captured line", () => DumpRows(vm));
            Assert.That(vm.StatisticsText, Does.StartWith("In this capture:"), vm.StatisticsText);
            Assert.That(vm.StatusText, Does.Contain("in this capture"), vm.StatusText);

            // The words are on the screen.
            string screen = AllText(window);
            Assert.That(screen, Does.Contain("In this capture:"), screen);
            Assert.That(screen, Does.Contain("packets in this capture"), screen);
            window.Close();
        }

        // ----------------------------------------------------------------------------------------
        // Item 8: the main window's function keys.
        // ----------------------------------------------------------------------------------------

        private static void CloseAllButMain(INavigationService navigation)
        {
            List<Window> open = navigation.GetOpenWindows();
            for (int i = 0; i < open.Count; i++)
            {
                navigation.CloseWindow(open[i]);
            }

            Dispatcher.UIThread.RunJobs();
        }

        private static int CountOpen(INavigationService navigation, Type type)
        {
            int count = 0;
            List<Window> open = navigation.GetOpenWindows();
            for (int i = 0; i < open.Count; i++)
            {
                if (open[i].GetType() == type)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Each shortcut opens its window, found through the navigation service's list of open
        /// windows: Ctrl+1 Status, Ctrl+2 Packets, Ctrl+3 Log, Ctrl+4 Routes, Ctrl+, Configuration,
        /// Ctrl+5 PAD, Ctrl+6 TAD server, Ctrl+L Link Status.
        /// </summary>
        [AvaloniaTest]
        public void WindowShortcuts_OpenTheirWindows()
        {
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig());
            INavigationService navigation = provider.GetRequiredService<INavigationService>();
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);
            window.Focus();

            Key[] keys = { Key.D1, Key.D2, Key.D3, Key.D4, Key.OemComma, Key.D5, Key.D6, Key.L };
            PhysicalKey[] physical = { PhysicalKey.Digit1, PhysicalKey.Digit2, PhysicalKey.Digit3, PhysicalKey.Digit4, PhysicalKey.Comma, PhysicalKey.Digit5, PhysicalKey.Digit6, PhysicalKey.L };
            RawInputModifiers[] modifiers = new RawInputModifiers[keys.Length];
            for (int m = 0; m < modifiers.Length; m++)
            {
                modifiers[m] = RawInputModifiers.Control;
            }
            Type[] expected =
            {
                typeof(StatusWindow), typeof(PacketWindow), typeof(LogWindow),
                typeof(RoutesWindow), typeof(ConfigurationWindow), typeof(PADClientWindow), typeof(TADServerWindow),
                typeof(LinkStatusWindow)
            };
            string[] names = { "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4", "Ctrl+,", "Ctrl+5", "Ctrl+6", "Ctrl+L" };

            for (int i = 0; i < keys.Length; i++)
            {
                CloseAllButMain(navigation);
                Assert.That(navigation.GetOpenWindows().Count, Is.EqualTo(0), "windows left over before " + names[i]);
                window.KeyPress(keys[i], modifiers[i], physical[i], null);
                Dispatcher.UIThread.RunJobs();
                Assert.That(CountOpen(navigation, expected[i]), Is.EqualTo(1),
                    names[i] + " did not open a " + expected[i].Name);
                Assert.That(navigation.GetOpenWindows().Count, Is.EqualTo(1), names[i] + " opened more than one window");
            }

            CloseAllButMain(navigation);
            window.Close();
        }

        /// <summary>
        /// Ctrl+Shift+Delete clears the packet history (hides what the Packet windows show), F1 shows the
        /// help. About and Exit are reached from the menu commands (Exit asks first; answered "no" here,
        /// so the test run goes on). Alt+F4 is the system's own window-close key and is not tested here.
        /// </summary>
        [AvaloniaTest]
        public void HistoryHelpAboutAndExit_ReachTheirActions()
        {
            RecordingDialogs dialogs = new RecordingDialogs { ConfirmAnswer = false };
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig(), dialogs);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            PacketViewState viewState = provider.GetRequiredService<PacketViewState>();
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);
            window.Focus();

            PacketWindowViewModel packets = provider.GetRequiredService<PacketWindowViewModel>();
            packets.IsCollapsed = false;
            runtimeService.Runtime.SimulateLapbFrame(new byte[] { 0x01, 0x3F }, 2);
            WaitUntil(() => packets.Packets.Count == 1, "a line to clear", () => DumpRows(packets));
            Assert.That(viewState.ClearedUpToId, Is.EqualTo(0));

            window.KeyPress(Key.Delete, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Delete, null);
            WaitUntil(() => packets.Packets.Count == 0, "Ctrl+Shift+Delete to hide the line", () => DumpRows(packets));
            Assert.That(viewState.ClearedUpToId, Is.GreaterThan(0));

            window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
            Assert.That(dialogs.InfoCount, Is.EqualTo(1), "F1 did not show the help");
            Assert.That(dialogs.LastInfoTitle, Is.EqualTo("X.25 Emulator - Help"));
            string help = dialogs.LastInfo;

            main.ShowAboutCommand.Execute(null);
            Assert.That(dialogs.InfoCount, Is.EqualTo(2), "About did not show");
            Assert.That(dialogs.LastInfoTitle, Is.EqualTo("About X.25 Emulator"));

            Assert.That(dialogs.ConfirmCount, Is.EqualTo(0));
            main.ExitCommand.Execute(null);
            WaitUntil(() => dialogs.ConfirmCount == 1, "Exit to ask for confirmation");
            Assert.That(dialogs.LastConfirmTitle, Is.EqualTo("Exit Application"));

            // The help lists exactly the keys that exist: each of these has a line, and F3 and F5 do not.
            string[] listed = { "F1 - ", "Ctrl+1 - ", "Ctrl+2 - ", "Ctrl+3 - ", "Ctrl+4 - ", "Ctrl+5 - ", "Ctrl+6 - ", "Ctrl+, - ", "Ctrl+L - ", "Ctrl+Shift+Delete - ", "Alt+F4 - " };
            for (int i = 0; i < listed.Length; i++)
            {
                Assert.That(help, Does.Contain("\n" + listed[i]), "the help does not list " + listed[i]);
            }

            Assert.That(help, Does.Not.Contain("\nF3 - "));
            Assert.That(help, Does.Not.Contain("\nF5 - "));
            Assert.That(help, Does.Not.Contain("\nF2 - "), "the old F2 line is still in the help");
            Assert.That(help, Does.Not.Contain("Ctrl+H"), "the old Ctrl+H is still in the help");
            Assert.That(help, Does.Contain("Reset Statistics"), "the help does not name Reset Statistics");
            Assert.That(help, Does.Contain("hides"), "the help should say that F6 hides, not deletes");

            window.Close();
        }

        /// <summary>
        /// The test box of the routing screen asks the one routing table: a number in a configured range is
        /// taken by its route, a number nobody routes is refused with diagnostic 67, an empty box asks for a
        /// number, and a route back out of the line the call came in on is never used.
        /// </summary>
        [AvaloniaTest]
        public void RoutingScreen_TestBox_AsksTheOneTable()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            using ServiceProvider provider = BuildServices(config);
            RoutingViewModel routing = provider.GetRequiredService<RoutingViewModel>();

            routing.TestNumber = "23500001";
            routing.TestAddressCommand.Execute(null);
            Assert.That(routing.TestResult, Does.Contain("route 'ToSwitch_2350' wins"), routing.TestResult);

            routing.TestNumber = "99999999";
            routing.TestAddressCommand.Execute(null);
            Assert.That(routing.TestResult, Does.Contain("No route takes 99999999").And.Contain("diagnostic 67"), routing.TestResult);

            routing.TestNumber = "  ";
            routing.TestAddressCommand.Execute(null);
            Assert.That(routing.TestResult, Does.Contain("Type a number"));

            routing.TestNumber = "23500001";
            routing.TestArrivalPort = "XOT_ToSwitch";
            routing.TestAddressCommand.Execute(null);
            Assert.That(routing.TestResult, Does.Contain("lead back out of the port the call came in on"), routing.TestResult);
        }

        /// <summary>
        /// Which file the program starts on: a .json argument wins (named missing files are kept, so
        /// the loader fails loudly), else x25config.json in the working folder if it is there, else
        /// none, which means the built-in defaults.
        /// </summary>
        [Test]
        public void StartupFile_IsTheArgument_ThenX25configJson_ThenNone()
        {
            Assert.That(Program.ChooseConfigFile(new[] { "my.json" }, f => true), Is.EqualTo("my.json"));
            Assert.That(Program.ChooseConfigFile(new[] { "-x", "MY.JSON" }, f => false), Is.EqualTo("MY.JSON"), "a named file is never replaced, even when missing");
            Assert.That(Program.ChooseConfigFile(new[] { "my.json" }, f => f == "x25config.json"), Is.EqualTo("my.json"), "the argument beats x25config.json");
            Assert.That(Program.ChooseConfigFile(Array.Empty<string>(), f => f == "x25config.json"), Is.EqualTo("x25config.json"));
            Assert.That(Program.ChooseConfigFile(new[] { "notes.txt" }, f => f == "x25config.json"), Is.EqualTo("x25config.json"), "only .json arguments count");
            Assert.That(Program.ChooseConfigFile(Array.Empty<string>(), f => false), Is.Null);
        }

        /// <summary>
        /// The TCP-LAPB interface dialog refuses wrong fields with a plain sentence, and a right set of
        /// fields gives an interface that carries X.25 only (no SINTRAN protocol detection).
        /// </summary>
        [Test]
        public void TcpLapbDialog_RefusesBadFields_AndBuildsAnX25OnlyInterface()
        {
            TcpLapbInterfaceEditDialogViewModel dialog = new TcpLapbInterfaceEditDialogViewModel(NullLogger.Instance, null, true)
            {
                Name = "L1",
                RemoteMachineId = "201",
                IsServer = true,
                Port = 5555
            };

            dialog.Port = 0;
            Assert.That(dialog.TryAccept(), Is.False);
            Assert.That(dialog.ValidationError, Does.Contain("port"));
            dialog.Port = 5555;

            dialog.RemoteMachineId = " ";
            Assert.That(dialog.TryAccept(), Is.False);
            Assert.That(dialog.ValidationError, Does.Contain("Remote machine ID"));
            dialog.RemoteMachineId = "201";

            dialog.WindowSize = 8;
            Assert.That(dialog.TryAccept(), Is.False, "a window above 7 must be refused: modulo 128 is not implemented");
            Assert.That(dialog.ValidationError, Does.Contain("1-7"));
            dialog.WindowSize = 7;

            dialog.T2TimeoutMs = 2000;
            Assert.That(dialog.TryAccept(), Is.False, "T2 above T1/2 must be refused");
            dialog.T2TimeoutMs = 1500;

            Assert.That(dialog.TryAccept(), Is.True, dialog.ValidationError);
            Assert.That(dialog.Result, Is.Not.Null);
            Assert.That(dialog.Result!.Name, Is.EqualTo("L1"));
            Assert.That(dialog.Result.Mode, Is.EqualTo(RetroIO.Link.TcpMode.Server));
            Assert.That(dialog.Result.Port, Is.EqualTo(5555));
            Assert.That(dialog.Result.RemoteMachineId, Is.EqualTo("201"));
            Assert.That(dialog.Result.ProtocolMode, Is.EqualTo(RetroIO.Link.ProtocolDetectionMode.X25Only));
        }

        /// <summary>
        /// The TCP-LAPB tab changes the shared configuration in place: add puts an interface in the list
        /// and in the configuration, edit replaces it, delete asks first and removes it only on "yes".
        /// </summary>
        [AvaloniaTest]
        public void TcpLapbTab_AddsReplacesAndDeletes_InTheConfiguration()
        {
            RecordingDialogs dialogs = new RecordingDialogs { ConfirmAnswer = false };
            RuntimeConfig config = App.CreateDefaultConfig();
            int before = config.Routing.TcpLapbInterfaces.Count;
            using ServiceProvider provider = BuildServices(config, dialogs);
            TcpLapbInterfacesTabViewModel tab = provider.GetRequiredService<TcpLapbInterfacesTabViewModel>();

            tab.AddEntry(new TcpLapbInterfaceConfig { Name = "A1", Mode = RetroIO.Link.TcpMode.Server, Port = 6001, RemoteMachineId = "301" });
            Assert.That(config.Routing.TcpLapbInterfaces.Count, Is.EqualTo(before + 1));
            Assert.That(tab.TcpLapbInterfaces.Count, Is.EqualTo(before + 1));
            Assert.That(tab.TcpLapbInterfaces[before].Name, Is.EqualTo("A1"));
            Assert.That(tab.TcpLapbInterfaces[before].Port, Is.EqualTo(6001));

            tab.ReplaceEntry(before, new TcpLapbInterfaceConfig { Name = "A2", Mode = RetroIO.Link.TcpMode.Client, RemoteAddress = "10.0.0.5", Port = 6002, RemoteMachineId = "302" });
            Assert.That(config.Routing.TcpLapbInterfaces[before].Name, Is.EqualTo("A2"));
            Assert.That(tab.TcpLapbInterfaces[before].Mode, Is.EqualTo("Client"));
            Assert.That(tab.TcpLapbInterfaces[before].RemoteAddress, Is.EqualTo("10.0.0.5"));
            Assert.Throws<ArgumentOutOfRangeException>(() => tab.ReplaceEntry(before + 5, new TcpLapbInterfaceConfig()));

            tab.SelectedInterface = tab.TcpLapbInterfaces[before];
            tab.DeleteInterfaceCommand.Execute(null);
            WaitUntil(() => dialogs.ConfirmCount == 1, "the delete confirmation");
            Assert.That(config.Routing.TcpLapbInterfaces.Count, Is.EqualTo(before + 1), "answered no, but the interface was deleted");

            dialogs.ConfirmAnswer = true;
            tab.DeleteInterfaceCommand.Execute(null);
            WaitUntil(() => config.Routing.TcpLapbInterfaces.Count == before, "the delete after yes");
            Assert.That(tab.TcpLapbInterfaces.Count, Is.EqualTo(before));
        }

        /// <summary>
        /// Link Status Stop and Start act on the selected link of the running engine: a stop brings the
        /// row to Stopped (and the engine's own link with it), a start brings it back to Active, and
        /// with nothing selected the buttons cannot be used.
        /// </summary>
        [AvaloniaTest]
        public void LinkStatus_StopAndStart_ActOnTheSelectedLink()
        {
            RuntimeConfig config = ConfigWithXotAndTcpLapb(FreePort());
            using ServiceProvider provider = BuildServices(config);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            LinkStatusWindowViewModel vm = provider.GetRequiredService<LinkStatusWindowViewModel>();
            vm.Refresh();

            LinkRowViewModel? row = null;
            for (int i = 0; i < vm.Rows.Count; i++)
            {
                if (vm.Rows[i].Name == "LAPB1")
                {
                    row = vm.Rows[i];
                }
            }

            Assert.That(row, Is.Not.Null, "no row for the TCP-LAPB link");
            vm.SelectedRow = null;
            Assert.That(vm.StopSelectedCommand.CanExecute(null), Is.False);
            Assert.That(vm.StartSelectedCommand.CanExecute(null), Is.False);

            vm.SelectedRow = row;
            WaitUntil(() => row!.State == LinkRowViewModel.RowState.Active, "the link to be active");
            Assert.That(vm.StopSelectedCommand.CanExecute(null), Is.True);

            vm.StopSelectedCommand.Execute(null);
            Assert.That(vm.ActionText, Is.EqualTo("Stopped LAPB1"));
            WaitUntil(() => FindRow(vm, "LAPB1")!.State == LinkRowViewModel.RowState.Stopped, "the row to say Stopped");
            Assert.That(runtimeService.Runtime.GetLink("LAPB1")!.Status, Is.EqualTo(RetroIO.Link.LinkStatus.Stopped), "the engine link was not stopped");

            vm.SelectedRow = FindRow(vm, "LAPB1");
            vm.StartSelectedCommand.Execute(null);
            Assert.That(vm.ActionText, Is.EqualTo("Started LAPB1"));
            WaitUntil(() => FindRow(vm, "LAPB1")!.State == LinkRowViewModel.RowState.Active, "the row to say Active again");
        }

        private static LinkRowViewModel? FindRow(LinkStatusWindowViewModel vm, string name)
        {
            for (int i = 0; i < vm.Rows.Count; i++)
            {
                if (vm.Rows[i].Name == name)
                {
                    return vm.Rows[i];
                }
            }

            return null;
        }

        private static PacketTreeNode? FindNode(System.Collections.Generic.IList<PacketTreeNode> nodes, string startsWith)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Text.StartsWith(startsWith, StringComparison.Ordinal))
                {
                    return nodes[i];
                }

                PacketTreeNode? inner = FindNode(nodes[i].Children, startsWith);
                if (inner != null)
                {
                    return inner;
                }
            }

            return null;
        }

        private static byte[] BytesOf(PacketRowViewModel row, PacketTreeNode node)
        {
            byte[] bytes = new byte[node.ByteLength];
            Array.Copy(row.Item.Payload, node.ByteStart, bytes, 0, node.ByteLength);
            return bytes;
        }

        /// <summary>
        /// The detail tree of a real call: each field names the bytes it came from, and those bytes really
        /// hold that value. Read back from the payload here, by hand, for the logical channel number, the
        /// called address, the sequence octet and the user data; clicking a line lights exactly its bytes.
        /// </summary>
        [AvaloniaTest]
        public void PacketTree_PointsEachFieldAtItsOwnBytes()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            packetVm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(packetVm);
            ShowAndSettle(packetWindow);
            PlaceCallAndExchangeText(router, provider, packetVm, out PADClientWindow pad);
            router.PushData("part one ", mBit: true, dBit: false, qBit: false);
            PacketRowViewModel? more = null;
            WaitUntil(() => (more = FindRowWithFlag(packetVm, "M")) != null, "the M row");
            PacketRowViewModel? call = null;
            WaitUntil(() => (call = FindRow(packetVm, "CALL-REQ", Direction.TX)) != null, "the Call Request line");

            // The Call Request.
            packetVm.SelectedPacket = call;
            Assert.That(packetVm.DetailTree.Count, Is.GreaterThan(0));
            Assert.That(packetVm.HexBytes.Count, Is.EqualTo(call!.Item.Payload.Length));
            PacketTreeNode? lcn = FindNode(packetVm.DetailTree, "Logical channel (LCN): ");
            Assert.That(lcn, Is.Not.Null);
            byte[] lcnBytes = BytesOf(call, lcn!);
            Assert.That(lcnBytes.Length, Is.EqualTo(2));
            Assert.That(((lcnBytes[0] & 0x0F) << 8) | lcnBytes[1], Is.EqualTo(call.LcnValue));

            PacketTreeNode? called = FindNode(packetVm.DetailTree, "Called address: ");
            Assert.That(called, Is.Not.Null);
            string digits = called!.Text.Substring("Called address: ".Length);
            byte[] calledBytes = BytesOf(call, called);
            StringBuilder packed = new StringBuilder();
            for (int i = 0; i < calledBytes.Length; i++)
            {
                packed.Append(calledBytes[i].ToString("X2"));
            }

            Assert.That(packed.ToString(), Does.StartWith(digits), "the marked bytes do not hold the called address " + digits);

            // Clicking the line lights exactly its bytes.
            packetVm.SelectedNode = called;
            int lit = 0;
            for (int i = 0; i < packetVm.HexBytes.Count; i++)
            {
                bool inside = i >= called.ByteStart && i < called.ByteStart + called.ByteLength;
                Assert.That(packetVm.HexBytes[i].IsHighlighted, Is.EqualTo(inside), "byte " + i);
                lit += packetVm.HexBytes[i].IsHighlighted ? 1 : 0;
            }

            Assert.That(lit, Is.EqualTo(called.ByteLength));
            packetVm.SelectedNode = FindNode(packetVm.DetailTree, "Packet #");
            Assert.That(packetVm.HexBytes[0].IsHighlighted, Is.False, "a line without bytes must light nothing");

            // The data packet with the M bit.
            packetVm.SelectedPacket = more;
            Assert.That(packetVm.SelectedNode, Is.Null, "a new packet starts with no line selected");
            PacketTreeNode? seq = FindNode(packetVm.DetailTree, "Sequence octet");
            Assert.That(seq, Is.Not.Null);
            byte octet = BytesOf(more!, seq!)[0];
            Assert.That(octet >> 5, Is.EqualTo(int.Parse(more!.Sequence.Split('/')[1])), "P(R) bits of the sequence octet");
            Assert.That((octet >> 1) & 7, Is.EqualTo(int.Parse(more.Sequence.Split('/')[0])), "P(S) bits of the sequence octet");
            Assert.That((octet >> 4) & 1, Is.EqualTo(1), "M bit of the sequence octet");
            PacketTreeNode? user = FindNode(packetVm.DetailTree, "User data: ");
            Assert.That(user, Is.Not.Null);
            Assert.That(Encoding.ASCII.GetString(BytesOf(more, user!)), Is.EqualTo("part one "));

            pad.Close();
            packetWindow.Close();
        }

        /// <summary>
        /// A LAPB information frame shows the X.25 packet it carries under its information field, with the
        /// positions counted from the start of the frame, so the packet's lines mark the right bytes of the
        /// whole frame.
        /// </summary>
        [AvaloniaTest]
        public void PacketTree_ShowsTheX25PacketInsideALapbFrame_WithPositionsInTheFrame()
        {
            // Address 01, control 00 (an information frame, N(S) 0, N(R) 0), then a Call Request on
            // channel 1: called 2343 and calling 0200, no facilities. A frame from a real link is a
            // plain LAPB item, which is what is built here.
            byte[] frame = { 0x01, 0x00, 0x10, 0x01, 0x0B, 0x44, 0x23, 0x43, 0x02, 0x00, 0x00 };
            PacketRowViewModel row = new PacketRowViewModel(
                new RetroIO.Protocols.X25.Models.LapbHistoryItem(
                    Direction.RX, frame, RetroIO.Link.Models.LapbFrameType.Information, 0x01, 0x00, 0, 0, false, 7),
                false);
            System.Collections.Generic.List<PacketTreeNode> tree = row.BuildDetailTree();
            PacketTreeNode? lapbNode = FindNode(tree, "LAPB frame");
            Assert.That(lapbNode, Is.Not.Null);
            Assert.That(lapbNode!.ByteLength, Is.EqualTo(frame.Length));
            PacketTreeNode? control = FindNode(tree, "Control: ");
            Assert.That(control!.ByteStart, Is.EqualTo(1));
            PacketTreeNode? information = FindNode(tree, "Information field");
            Assert.That(information!.ByteStart, Is.EqualTo(2));
            Assert.That(information.ByteLength, Is.EqualTo(frame.Length - 2));

            PacketTreeNode? x25 = FindNode(information.Children, "X.25 packet: ");
            Assert.That(x25, Is.Not.Null, "no X.25 packet under the information field");
            Assert.That(x25!.ByteStart, Is.EqualTo(2));
            PacketTreeNode? lcn = FindNode(x25.Children, "Logical channel (LCN): 1");
            Assert.That(lcn, Is.Not.Null);
            Assert.That(lcn!.ByteStart, Is.EqualTo(2), "the LCN starts at the first byte of the packet, which is byte 2 of the frame");
            PacketTreeNode? called = FindNode(x25.Children, "Called address: 2343");
            Assert.That(called, Is.Not.Null, "called address not found");
            Assert.That(called!.ByteStart, Is.EqualTo(6));
            Assert.That(called.ByteLength, Is.EqualTo(2));
            Assert.That(row.Item.Payload[called.ByteStart], Is.EqualTo(0x23));
            Assert.That(row.Item.Payload[called.ByteStart + 1], Is.EqualTo(0x43));
        }

        // ---- the PAD console must keep the last line in view ----

        /// <summary>
        /// True if the scroll view shows its very last line (allows one pixel of rounding).
        /// </summary>
        private static bool ShowsLastLine(ScrollViewer scroll)
        {
            return scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 1.0;
        }

        /// <summary>
        /// Sends text from the fake router in packets of at most 100 bytes (the packet size is 128), the way
        /// a long answer reaches the PAD.
        /// </summary>
        private static void PushInPackets(FakeRouter router, string text)
        {
            for (int at = 0; at < text.Length; at += 100)
            {
                router.PushData(text.Substring(at, Math.Min(100, text.Length - at)), mBit: false, dBit: false, qBit: false);
            }
        }

        private static void SettleLayout(Window window)
        {
            for (int i = 0; i < 8; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }

        /// <summary>
        /// Text that arrives from the far end keeps the last line in view, whether it comes one line at a
        /// time, as a burst of many lines at once, or as a last line with no line end (a prompt). The
        /// person must never have to scroll or press Enter to find the text they just got.
        /// </summary>
        [AvaloniaTest]
        public void PadConsole_KeepsTheLastLineInView_WhenTextArrives()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            PlaceCallAndExchangeText(router, provider, packetVm, out PADClientWindow pad);
            PADClientWindowViewModel vm = (PADClientWindowViewModel)pad.DataContext!;
            ScrollViewer scroll = FindNamed<ScrollViewer>(pad, "ConsoleScroll")!;

            // One line at a time, until the text is much taller than the window.
            for (int i = 0; i < 80; i++)
            {
                string marker = "single-" + i;
                router.PushData(marker + "\r\n", mBit: false, dBit: false, qBit: false);
                WaitUntil(() => vm.ConsoleText.Contains(marker), "line " + marker);
                SettleLayout(pad);
                Assert.That(ShowsLastLine(scroll), Is.True,
                    "after " + marker + " the last line is out of view: offset " + scroll.Offset.Y + ", viewport "
                    + scroll.Viewport.Height + ", extent " + scroll.Extent.Height);
            }

            // A burst of many lines in one packet.
            System.Text.StringBuilder burst = new System.Text.StringBuilder();
            for (int i = 0; i < 40; i++)
            {
                burst.Append("burst-").Append(i).Append("\r\n");
            }

            PushInPackets(router, burst.ToString());
            WaitUntil(() => vm.ConsoleText.Contains("burst-39"), "the burst");
            SettleLayout(pad);
            Assert.That(ShowsLastLine(scroll), Is.True, "after a burst the last line is out of view: offset "
                + scroll.Offset.Y + ", viewport " + scroll.Viewport.Height + ", extent " + scroll.Extent.Height);

            // A last line with no line end, as a prompt is.
            router.PushData("R1#", mBit: false, dBit: false, qBit: false);
            WaitUntil(() => vm.ConsoleText.EndsWith("R1#"), "the prompt");
            SettleLayout(pad);
            Assert.That(ShowsLastLine(scroll), Is.True, "after a prompt without line end the last line is out of view");

            // A very wide line makes the horizontal bar appear, which takes room from the bottom.
            PushInPackets(router, new string('w', 400) + "\r\n");
            WaitUntil(() => vm.ConsoleText.Contains(new string('w', 400)), "the wide line");
            SettleLayout(pad);
            router.PushData("after-wide\r\n", mBit: false, dBit: false, qBit: false);
            WaitUntil(() => vm.ConsoleText.Contains("after-wide"), "the line after the wide one");
            SettleLayout(pad);
            Assert.That(ShowsLastLine(scroll), Is.True, "with the horizontal bar showing the last line is out of view: offset "
                + scroll.Offset.Y + ", viewport " + scroll.Viewport.Height + ", extent " + scroll.Extent.Height);

            pad.Close();
        }

        /// <summary>
        /// A person who has scrolled up to read is not pulled back down by new output; when they scroll
        /// to the bottom again, following resumes.
        /// </summary>
        [AvaloniaTest]
        public void PadConsole_DoesNotPullBackAReader_ButFollowsAgainAtTheBottom()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            PlaceCallAndExchangeText(router, provider, packetVm, out PADClientWindow pad);
            PADClientWindowViewModel vm = (PADClientWindowViewModel)pad.DataContext!;
            ScrollViewer scroll = FindNamed<ScrollViewer>(pad, "ConsoleScroll")!;

            for (int i = 0; i < 60; i++)
            {
                router.PushData("fill-" + i + "\r\n", mBit: false, dBit: false, qBit: false);
            }

            WaitUntil(() => vm.ConsoleText.Contains("fill-59"), "the filler");
            SettleLayout(pad);
            Assert.That(ShowsLastLine(scroll), Is.True);

            scroll.Offset = new Avalonia.Vector(0, 0);
            SettleLayout(pad);
            router.PushData("while-reading\r\n", mBit: false, dBit: false, qBit: false);
            WaitUntil(() => vm.ConsoleText.Contains("while-reading"), "text while reading");
            SettleLayout(pad);
            Assert.That(scroll.Offset.Y, Is.EqualTo(0).Within(1.0), "a reader at the top was pulled down");

            scroll.ScrollToEnd();
            SettleLayout(pad);
            router.PushData("back-at-bottom\r\n", mBit: false, dBit: false, qBit: false);
            WaitUntil(() => vm.ConsoleText.Contains("back-at-bottom"), "text after returning to the bottom");
            SettleLayout(pad);
            Assert.That(ShowsLastLine(scroll), Is.True, "following did not resume at the bottom");

            pad.Close();
        }

        /// <summary>
        /// Output that arrives fast, without waiting for the window between packets, still ends with the
        /// last line in view. The window decides "am I following?" while text keeps arriving and layouts
        /// keep growing; a decision taken on a stale position leaves the newest lines below the edge.
        /// </summary>
        [AvaloniaTest]
        public void PadConsole_KeepsTheLastLineInView_WhenOutputArrivesFast()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            PlaceCallAndExchangeText(router, provider, packetVm, out PADClientWindow pad);
            PADClientWindowViewModel vm = (PADClientWindowViewModel)pad.DataContext!;
            ScrollViewer scroll = FindNamed<ScrollViewer>(pad, "ConsoleScroll")!;

            for (int round = 0; round < 5; round++)
            {
                for (int i = 0; i < 300; i++)
                {
                    router.PushData("fast-" + round + "-" + i + "\r\n", mBit: false, dBit: false, qBit: false);
                    if (i % 7 == 0)
                    {
                        // The window gets a turn now and then, but not after every packet.
                        Dispatcher.UIThread.RunJobs();
                    }
                }

                string last = "fast-" + round + "-299";
                WaitUntil(() => vm.ConsoleText.Contains(last), "the last line of round " + round, null, 20000);
                SettleLayout(pad);
                Assert.That(ShowsLastLine(scroll), Is.True, "round " + round + ": last line out of view: offset "
                    + scroll.Offset.Y + ", viewport " + scroll.Viewport.Height + ", extent " + scroll.Extent.Height);
            }

            pad.Close();
        }

        /// <summary>
        /// The whole path against the built-in TAD server (address 23430200), which answers every typed
        /// character with an echo of its own and every line with a longer reply. Each command is typed,
        /// sent with Enter, and after its reply the last line must be in view.
        /// </summary>
        [AvaloniaTest]
        public void PadConsole_KeepsTheLastLineInView_AgainstTheBuiltInTadServer()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-tad-callback.json"));
            using ServiceProvider provider = BuildServices(config);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            PADClientWindowViewModel vm = new PADClientWindowViewModel(
                NullLogger<PADClientWindowViewModel>.Instance, runtimeService, 1);
            PADClientWindow pad = new PADClientWindow(vm) { Width = 900, Height = 600 };
            pad.Show();
            SettleLayout(pad);
            ScrollViewer scroll = FindNamed<ScrollViewer>(pad, "ConsoleScroll")!;

            pad.KeyTextInput("CALL 23430200");
            pad.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitUntil(() => vm.ConsoleText.Contains("TAD"), "the greeting of the TAD server", () => vm.ConsoleText);
            SettleLayout(pad);
            Assert.That(ShowsLastLine(scroll), Is.True, "after the greeting");

            for (int i = 0; i < 40; i++)
            {
                string typed = "STATUS";
                int before = vm.ConsoleText.Length;
                pad.KeyTextInput(typed);
                SettleLayout(pad);
                Assert.That(ShowsLastLine(scroll), Is.True, "round " + i + ", after typing: offset " + scroll.Offset.Y
                    + ", viewport " + scroll.Viewport.Height + ", extent " + scroll.Extent.Height);
                pad.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                WaitUntil(() => vm.ConsoleText.Length > before + typed.Length + 20, "a reply in round " + i, () => vm.ConsoleText);
                Thread.Sleep(150);
                SettleLayout(pad);
                Assert.That(ShowsLastLine(scroll), Is.True, "round " + i + ", after the reply: offset " + scroll.Offset.Y
                    + ", viewport " + scroll.Viewport.Height + ", extent " + scroll.Extent.Height);
            }

            pad.Close();
        }

        // ---------------------------------------------------------------------------------------
        // PAD window against the built-in TAD server: connect, X.3, Clear Console, Disconnect.
        // ---------------------------------------------------------------------------------------

        /// <summary>Finds a button by the text on it.</summary>
        private static Button FindButtonByText(Window window, string text)
        {
            List<Visual> all = new List<Visual>(window.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Button b && b.Content?.ToString() == text)
                {
                    return b;
                }
            }

            Assert.Fail("no button with the text: " + text);
            return null!;
        }

        /// <summary>Types a line in the PAD window as a person does: the text, then Enter.</summary>
        private static void TypeLine(PADClientWindow pad, string line)
        {
            pad.KeyTextInput(line);
            pad.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>Waits until the console text has stayed the same for a short while.</summary>
        private static void WaitQuiet(PADClientWindowViewModel vm)
        {
            string last = vm.ConsoleText;
            int stable = 0;
            while (stable < 8)
            {
                Thread.Sleep(50);
                Dispatcher.UIThread.RunJobs();
                if (vm.ConsoleText == last)
                {
                    stable++;
                }
                else
                {
                    last = vm.ConsoleText;
                    stable = 0;
                }
            }
        }

        private static string Dump(string label, PADClientWindowViewModel vm)
        {
            return "[" + label + "] status=" + vm.StatusText + "\n" + vm.ConsoleText.Replace("\r", "\\r").Replace("\n", "\\n\n") + "<END>";
        }

        /// <summary>
        /// The PAD window and the "Local TAD server" window on one runtime that has the built-in TAD
        /// server behind address 23430200, as the app starts them.
        /// </summary>
        private sealed class PadRig : IDisposable
        {
            public readonly ServiceProvider Provider;
            public readonly PADClientWindowViewModel Vm;
            public readonly PADClientWindow Pad;
            public readonly TADServerWindowViewModel TadVm;
            public readonly TADServerWindow TadWindow;

            public PadRig()
            {
                RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-tad-callback.json"));
                Provider = BuildServices(config);
                IRuntimeService runtimeService = Provider.GetRequiredService<IRuntimeService>();
                Vm = new PADClientWindowViewModel(NullLogger<PADClientWindowViewModel>.Instance, runtimeService, 1);
                Pad = new PADClientWindow(Vm) { Width = 900, Height = 600 };
                Pad.Show();
                SettleLayout(Pad);
                TadVm = new TADServerWindowViewModel(NullLogger<TADServerWindowViewModel>.Instance, runtimeService);
                TadWindow = new TADServerWindow(TadVm) { Width = 900, Height = 600 };
                TadWindow.Show();
                SettleLayout(TadWindow);
                // The TAD window took the keyboard focus when it opened; the person works in the PAD window.
                Pad.Activate();
                Pad.Focus();
                SettleLayout(Pad);
            }

            public void Dispose()
            {
                TadWindow.Close();
                Pad.Close();
                TadVm.Dispose();
                Vm.Dispose();
                Provider.Dispose();
            }

            /// <summary>Types CALL 23430200 and waits until the host's prompt is on the screen.</summary>
            public void Call()
            {
                int greetings = CountOf(Vm.ConsoleText, "TAD> ");
                TypeLine(Pad, "CALL 23430200");
                WaitUntil(() => CountOf(Vm.ConsoleText, "TAD> ") > greetings && Vm.StatusText.Contains("Connected"),
                    "the call to be connected and the host prompt on the screen", () => Dump("call", Vm));
                WaitQuiet(Vm);
            }

            /// <summary>Presses a button of the PAD window with the mouse.</summary>
            public void Press(string text)
            {
                ClickButton(Pad, FindButtonByText(Pad, text));
                WaitQuiet(Vm);
            }

            /// <summary>Types a line and checks that the host answers with the wanted text.</summary>
            public void AskHost(string line, string answerPart)
            {
                int before = Vm.ConsoleText.Length;
                TypeLine(Pad, line);
                WaitUntil(() => Vm.ConsoleText.Length > before && Vm.ConsoleText.Substring(before).Contains(answerPart),
                    "the host to answer '" + line + "' with '" + answerPart + "'", () => Dump("ask " + line, Vm));
                WaitQuiet(Vm);
            }

            /// <summary>Types a line in command mode and checks that the PAD answers and shows the prompt again.</summary>
            public void AskPad(string line, string answerPart)
            {
                int before = Vm.ConsoleText.Length;
                TypeLine(Pad, line);
                WaitUntil(() => Vm.ConsoleText.Length > before && Vm.ConsoleText.Substring(before).Contains(answerPart),
                    "the PAD to answer '" + line + "' with '" + answerPart + "'", () => Dump("pad " + line, Vm));
                WaitQuiet(Vm);
                Assert.That(Vm.ConsoleText, Does.EndWith("PAD> "), Dump("after " + line, Vm));
            }
        }

        private static int CountOf(string text, string part)
        {
            int count = 0;
            int at = text.IndexOf(part, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>The person leaves the data state with the PAD escape (Ctrl+P) and types a PAD command.</summary>
        private static void EscapeAndType(PADClientWindow pad, string command)
        {
            pad.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, null);
            Dispatcher.UIThread.RunJobs();
            TypeLine(pad, command);
        }

        /// <summary>
        /// The person escapes to the PAD during a call, types one SET line per entry, and then types
        /// DATA to go back to the host (the PAD stays in command mode until then).
        /// </summary>
        private static void EscapeSetAndResume(PADClientWindowViewModel vm, PADClientWindow pad, params string[] setLines)
        {
            pad.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, null);
            Dispatcher.UIThread.RunJobs();
            // X.28 3.5.23: the escape shows the PAD's prompt only (it printed a "PAD Command Mode" banner until 2026-11-19).
            WaitUntil(() => vm.ConsoleText.EndsWith("PAD> ", StringComparison.Ordinal), "the PAD to enter command mode", () => Dump("escape", vm));
            WaitQuiet(vm);
            for (int i = 0; i < setLines.Length; i++)
            {
                TypeLine(pad, setLines[i]);
                WaitQuiet(vm);
            }

            int before = vm.ConsoleText.Length;
            TypeLine(pad, "DATA");
            WaitUntil(() => vm.ConsoleText.Substring(before).Contains("Resuming Data Transfer"), "the PAD to go back to data transfer", () => Dump("resume", vm));
            WaitQuiet(vm);
        }

        [AvaloniaTest]
        public void PadTad_Call_ConnectsAndTheHostAnswers()
        {
            using PadRig rig = new PadRig();
            Assert.That(rig.Vm.ConsoleText, Does.EndWith("PAD> "), "the window starts at the PAD prompt");
            rig.Call();
            Assert.That(rig.Vm.StatusText, Does.Contain("Connected"));
            Assert.That(rig.Vm.ConsoleText, Does.Contain("COM"));
            Assert.That(rig.Vm.ConsoleText, Does.Contain("X.25 TAD Loopback Echo Service"));
            rig.AskHost("STATUS", "Echo service active");
            WaitUntil(() => rig.TadVm.Sessions.Count == 1, "one session in the Local TAD server window", () => rig.TadVm.ConnectionStatsText, 9000);
            Assert.That(rig.TadVm.Sessions[0].CalledAddress, Does.Contain("23430200"));
        }

        [AvaloniaTest]
        public void PadTad_X3Change_ShowsInPadWindowAndInLocalTadWindow()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            WaitUntil(() => rig.TadVm.Sessions.Count == 1, "one session in the Local TAD server window", () => rig.TadVm.ConnectionStatsText, 9000);
            rig.TadVm.SelectedSession = rig.TadVm.Sessions[0];
            Assert.That(rig.TadVm.X3Parameters, Does.Contain("P02:"), "the X.3 text of the selected session");
            Assert.That(rig.TadVm.X3Parameters, Does.Not.Contain("P04:  20 "), "before the change");

            // Leave the data state with the escape, change two parameters, and return to the host.
            EscapeSetAndResume(rig.Vm, rig.Pad, "SET 4:20", "SET 2:0");

            WaitUntil(() => rig.Vm.X3Parameters.Contains("P04:  20 - Idle Timer"), "P4 = 20 in the PAD window", () => string.Join("\n", rig.Vm.X3Parameters));
            WaitUntil(() => rig.Vm.X3Parameters.Contains("P02:   0 - Echo"), "P2 = 0 in the PAD window", () => string.Join("\n", rig.Vm.X3Parameters));

            // The Local TAD window refreshes every 2 seconds and re-selects the session.
            WaitUntil(() => rig.TadVm.X3Parameters.Contains("P04:  20 - IDLE_TIMER") && rig.TadVm.X3Parameters.Contains("P02:   0 - ECHO"),
                "the new values in the Local TAD server window", () => rig.TadVm.X3Parameters, 9000);
            Assert.That(rig.TadVm.SelectedSession, Is.Not.Null, "the session stays selected after the refresh");

            // Still the same call, and the host still answers.
            rig.AskHost("STATUS", "Echo service active");
        }

        [AvaloniaTest]
        public void PadTad_ClearConsole_WhileConnected_ClearsAndTypingStillReachesTheHost()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            rig.Press("Clear Console");
            Assert.That(rig.Vm.ConsoleText, Is.EqualTo(""), "the console is empty after Clear Console while connected (the host prompt is the host's)");
            Assert.That(rig.Vm.StatusText, Does.Contain("Connected"));

            // Typing goes to the host, not to the button that was just pressed.
            rig.AskHost("STATUS", "Echo service active");
            Assert.That(rig.Vm.ConsoleText, Does.Not.Contain("X.25 TAD Loopback Echo Service"), "the old text did not come back");
            rig.AskHost("HELP", "QUIT");
        }

        [AvaloniaTest]
        public void PadTad_ClearConsole_WhileDisconnected_ShowsThePadPrompt()
        {
            using PadRig rig = new PadRig();
            rig.Press("Clear Console");
            Assert.That(rig.Vm.ConsoleText, Is.EqualTo("PAD> "), "an empty console with the PAD prompt");
            rig.AskPad("STAT", "FREE"); // X.28 3.5.11: no call
            rig.AskPad("PAR?", "PAR 1:1, 2:1"); // X.28 3.5.14
            rig.Call();
            Assert.That(rig.Vm.StatusText, Does.Contain("Connected"));
        }

        [AvaloniaTest]
        public void PadTad_Disconnect_ReturnsToCommandModeWithPrompt_AndANewCallWorks()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            rig.AskHost("STATUS", "Echo service active");
            rig.Press("Disconnect");
            Assert.That(rig.Vm.StatusText, Does.Contain("Disconnected"));
            Assert.That(rig.Vm.ConsoleText, Does.EndWith("PAD> "), Dump("after Disconnect", rig.Vm));
            Assert.That(CountOf(rig.Vm.ConsoleText, "CLR"), Is.EqualTo(1), "one clear message, not two: " + Dump("after Disconnect", rig.Vm));
            string afterClear = rig.Vm.ConsoleText.Substring(rig.Vm.ConsoleText.LastIndexOf("CLR", StringComparison.Ordinal));
            Assert.That(CountOf(afterClear, "PAD> "), Is.EqualTo(1), "one prompt after the clear: " + Dump("after Disconnect", rig.Vm));

            // Command mode works, and the PAD is back on its DEFAULT parameters: the built-in TAD set echo
            // (P2) to 0 during the call, and after the call it is 1 again (the rule: after a call the PAD
            // always goes back to the defaults, never to what the host set).
            rig.AskPad("PAR?", "PAR 1:1, 2:1"); // X.28 3.5.14
            WaitUntil(() => rig.TadVm.Sessions.Count == 0, "the session gone from the Local TAD server window", () => rig.TadVm.ConnectionStatsText, 9000);

            // And a new call works.
            rig.Call();
            rig.AskHost("STATUS", "Echo service active");
        }

        // ------------------------------------------------------------------ one prompt after a clear, defaults after a call

        /// <summary>The text from the last occurrence of the part to the end.</summary>
        private static string After(string text, string part)
        {
            int at = text.LastIndexOf(part, StringComparison.Ordinal);
            return at < 0 ? string.Empty : text.Substring(at);
        }

        /// <summary>
        /// Every X.3 parameter shown in the PAD window is its default value (P2, echo, is 1).
        /// </summary>
        private static void AssertWindowShowsDefaults(PADClientWindowViewModel vm, string when)
        {
            Dictionary<byte, byte> defaults = new Dictionary<byte, byte>();
            RetroIO.Protocols.X25.Protocols.X3.FillDefaultValues(defaults);
            WaitUntil(() => HasParameterLine(vm, "P02:   1"), "P2 to be back to its default " + when, () => string.Join("\n", vm.X3Parameters));
            foreach (KeyValuePair<byte, byte> kv in defaults)
            {
                string expected = "P" + kv.Key.ToString("D2") + ": " + kv.Value.ToString().PadLeft(3);
                Assert.That(HasParameterLine(vm, expected), Is.True, "parameter list is not at its default '" + expected + "' " + when + ":\n" + string.Join("\n", vm.X3Parameters));
            }
        }

        private static bool HasParameterLine(PADClientWindowViewModel vm, string start)
        {
            for (int i = 0; i < vm.X3Parameters.Count; i++)
            {
                if (vm.X3Parameters[i].StartsWith(start, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// During the call the host (the built-in TAD) has set P2 to 0 in the PAD, and the Local TAD
        /// window shows the session's X.3 values.
        /// </summary>
        private static void AssertHostChangedTheEchoDuringTheCall(PadRig rig)
        {
            WaitUntil(() => HasParameterLine(rig.Vm, "P02:   0"), "the host's SET (echo off) to show in the PAD window", () => string.Join("\n", rig.Vm.X3Parameters));
            WaitUntil(() => rig.TadVm.Sessions.Count == 1, "one session in the Local TAD server window", () => rig.TadVm.ConnectionStatsText, 9000);
            rig.TadVm.SelectedSession = rig.TadVm.Sessions[0];
            Assert.That(rig.TadVm.X3Parameters, Does.Contain("P02:"), "the Local TAD window shows the session's X.3 values during the call");
        }

        [AvaloniaTest]
        public void PadTad_HostClear_ByTypingQuit_GivesExactlyOnePrompt()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            int before = rig.Vm.ConsoleText.Length;
            TypeLine(rig.Pad, "QUIT");
            WaitUntil(() => rig.Vm.ConsoleText.Substring(before).Contains("CLR") && rig.Vm.ConsoleText.EndsWith("PAD> ", StringComparison.Ordinal),
                "the clear message and the prompt after a host clear", () => Dump("QUIT", rig.Vm));
            WaitQuiet(rig.Vm);
            Thread.Sleep(300);
            WaitQuiet(rig.Vm);
            string afterClear = After(rig.Vm.ConsoleText, "CLR");
            Assert.That(CountOf(afterClear, "PAD> "), Is.EqualTo(1), "one prompt after a host clear: " + Dump("QUIT", rig.Vm));
        }

        [AvaloniaTest]
        public void PadTad_UserClear_GivesExactlyOnePrompt()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            int before = rig.Vm.ConsoleText.Length;
            EscapeAndType(rig.Pad, "CLR");
            WaitUntil(() => rig.Vm.ConsoleText.Substring(before).Contains("CLR CONF") && rig.Vm.ConsoleText.EndsWith("PAD> ", StringComparison.Ordinal),
                "CLR CONF and the prompt after the user's own clear", () => Dump("CLR", rig.Vm));
            WaitQuiet(rig.Vm);
            string afterClear = After(rig.Vm.ConsoleText, "CLR CONF");
            Assert.That(CountOf(afterClear, "PAD> "), Is.EqualTo(1), "one prompt after the user's clear: " + Dump("CLR", rig.Vm));
        }

        [AvaloniaTest]
        public void PadTad_AfterAHostClear_ParametersAreTheDefaults_AndTypingIsEchoedAgain()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            AssertHostChangedTheEchoDuringTheCall(rig);
            TypeLine(rig.Pad, "QUIT");
            WaitUntil(() => rig.Vm.StatusText.Contains("Disconnected") && rig.Vm.ConsoleText.EndsWith("PAD> ", StringComparison.Ordinal), "the host clear", () => Dump("QUIT", rig.Vm));
            WaitQuiet(rig.Vm);
            AssertWindowShowsDefaults(rig.Vm, "after a host clear");

            // The PAD echoes the typed command again, and a new call starts from the defaults.
            rig.AskPad("PAR?", "PAR 1:1, 2:1"); // X.28 3.5.14
            Assert.That(rig.Vm.ConsoleText, Does.Contain("PAD> PAR?"), "the typed command is echoed once P2 is back: " + Dump("echo", rig.Vm));
            rig.Call();
            rig.AskHost("STATUS", "Echo service active");
        }

        [AvaloniaTest]
        public void PadTad_AfterTheUsersOwnClear_ParametersAreTheDefaults()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            AssertHostChangedTheEchoDuringTheCall(rig);
            EscapeAndType(rig.Pad, "CLR");
            WaitUntil(() => rig.Vm.StatusText.Contains("Disconnected") && rig.Vm.ConsoleText.EndsWith("PAD> ", StringComparison.Ordinal), "the user's clear", () => Dump("CLR", rig.Vm));
            WaitQuiet(rig.Vm);
            AssertWindowShowsDefaults(rig.Vm, "after the user's CLR");
            rig.AskPad("PAR?", "PAR 1:1, 2:1"); // X.28 3.5.14
        }

        [AvaloniaTest]
        public void PadTad_AfterTheDisconnectButton_ParametersAreTheDefaults()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            AssertHostChangedTheEchoDuringTheCall(rig);
            rig.Press("Disconnect");
            WaitUntil(() => rig.Vm.StatusText.Contains("Disconnected"), "Disconnect", () => Dump("Disconnect", rig.Vm));
            AssertWindowShowsDefaults(rig.Vm, "after the Disconnect button");
        }

        [AvaloniaTest]
        public void PadTad_ClearConsoleThenDisconnect_GivesPromptAndNewCallWorks()
        {
            using PadRig rig = new PadRig();
            rig.Call();
            rig.Press("Clear Console");
            rig.Press("Disconnect");
            Assert.That(rig.Vm.StatusText, Does.Contain("Disconnected"));
            Assert.That(rig.Vm.ConsoleText, Does.EndWith("PAD> "), Dump("clear then disconnect", rig.Vm));
            Assert.That(CountOf(rig.Vm.ConsoleText, "PAD> "), Is.EqualTo(1), "one prompt: " + Dump("clear then disconnect", rig.Vm));
            rig.Press("Clear Console");
            Assert.That(rig.Vm.ConsoleText, Is.EqualTo("PAD> "));
            rig.Call();
            rig.AskHost("STATUS", "Echo service active");
        }

        [AvaloniaTest]
        public void PadTad_WholeSequenceTwice_StaysNormal()
        {
            using PadRig rig = new PadRig();
            for (int round = 0; round < 2; round++)
            {
                rig.Call();
                rig.AskHost("STATUS", "Echo service active");
                rig.Press("Clear Console");
                rig.AskHost("STATUS", "Echo service active");
                int idle = 10 + round;
                EscapeSetAndResume(rig.Vm, rig.Pad, "SET 4:" + idle);
                WaitUntil(() => rig.Vm.X3Parameters.Contains("P04:  " + idle + " - Idle Timer"), "P4 changed in round " + round, () => string.Join("\n", rig.Vm.X3Parameters));
                rig.AskHost("STATUS", "Echo service active");
                rig.Press("Clear Console");
                rig.Press("Disconnect");
                Assert.That(rig.Vm.ConsoleText, Does.EndWith("PAD> "), "round " + round + ": " + Dump("end", rig.Vm));
                rig.Press("Clear Console");
                Assert.That(rig.Vm.ConsoleText, Is.EqualTo("PAD> "), "round " + round);
                rig.AskPad("STAT", "FREE"); // X.28 3.5.11: no call
            }
        }

        /// <summary>
        /// The decoded fields as plain text are always shown in the right box of the packet window, below
        /// a splitter: no drop-down to open, and the text has real size once a packet is selected.
        /// </summary>
        [AvaloniaTest]
        public void PacketWindow_ShowsThePlainTextAlways_NoDropDown()
        {
            using FakeRouter router = new FakeRouter();
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile());
            config.Routing.XotInterfaces[0].RemotePort = router.Port;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            using ServiceProvider provider = BuildServices(config);
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            packetVm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(packetVm);
            ShowAndSettle(packetWindow);
            PlaceCallAndExchangeText(router, provider, packetVm, out PADClientWindow pad);
            PacketRowViewModel? call = null;
            WaitUntil(() => (call = FindRow(packetVm, "CALL-REQ", Direction.TX)) != null, "the call request row");
            packetVm.SelectedPacket = call;
            SettleLayout(packetWindow);

            Assert.That(new List<Visual>(packetWindow.GetVisualDescendants()).Exists(v => v is Expander), Is.False,
                "the packet window still has a drop-down");
            SelectableTextBlock detail = FindNamed<SelectableTextBlock>(packetWindow, "DetailText")!;
            Assert.That(detail.IsEffectivelyVisible, Is.True);
            Assert.That(detail.Bounds.Height, Is.GreaterThan(10), "the plain text has no room");
            Assert.That(detail.Text, Is.Not.Empty);
            Assert.That(detail.Text, Is.EqualTo(packetVm.SelectedPacketDetail));

            pad.Close();
            packetWindow.Close();
        }
    }
}
