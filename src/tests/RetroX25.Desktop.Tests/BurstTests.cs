//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Controls;
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
    /// Burst tests: the command-line tools x25traffic and x25echo (real processes, started by the test)
    /// push a large volume through the Desktop app's own runtime, and the test reads the result from the
    /// real windows and view models.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How it runs: the Desktop runtime listens as an XOT server on a free loopback port (the sample
    /// x25config-tad-callback.json, the same configuration a Cisco router calls). The tools are the
    /// published programs of the RetroX25.Traffic and RetroX25.Echo projects, started from their build
    /// folders. Each tool process is recorded by its process number and stopped by that number in a
    /// finally block; nothing is ever stopped by name.
    /// </para>
    /// <para>
    /// "The UI keeps working" is measured, not assumed: while a tool runs, the test thread (which is the
    /// UI thread in headless Avalonia) runs the queued UI jobs in a loop and measures the longest single
    /// run of the queue. The limit is 2 seconds, from the request that a posted job must run within a
    /// stated time; it is a hang detector, not a speed target.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class BurstTests
    {
        /// <summary>The longest the UI thread may be away from its queue, in milliseconds (a hang detector).</summary>
        private const int UiStallLimitMs = 2000;

        // ------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------

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
        /// Finds a tool program in the build folder of its project, next to the Desktop project, by walking
        /// up from the test's folder to the folder that holds the tool's project.
        /// </summary>
        private static string FindTool(string projectFolder, string exeName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string bin = Path.Combine(dir.FullName, projectFolder, "bin");
                if (Directory.Exists(bin))
                {
                    string[] found = Directory.GetFiles(bin, exeName, SearchOption.AllDirectories);
                    string? newest = null;
                    for (int i = 0; i < found.Length; i++)
                    {
                        if (newest == null || File.GetLastWriteTimeUtc(found[i]) > File.GetLastWriteTimeUtc(newest))
                        {
                            newest = found[i];
                        }
                    }

                    if (newest != null)
                    {
                        return newest;
                    }
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("The tool " + exeName + " is not built (looked in " + projectFolder + "/bin above " + AppContext.BaseDirectory + ")");
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static void WaitUntil(Func<bool> condition, string what, Func<string>? detail = null, int timeoutMs = 10000)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                Dispatcher.UIThread.RunJobs();
                if (condition())
                {
                    return;
                }

                Thread.Sleep(10);
            }

            Dispatcher.UIThread.RunJobs();
            string window = detail != null ? "\n--- detail ---\n" + detail() : "";
            Assert.That(condition(), Is.True, "gave up waiting for: " + what + window);
        }

        /// <summary>
        /// One tool process: started by the test, its output collected, stopped by its own process number.
        /// </summary>
        private sealed class ToolRun : IDisposable
        {
            private readonly Process _process;
            private readonly StringBuilder _output = new StringBuilder();

            public ToolRun(string exe, string arguments)
            {
                ProcessStartInfo start = new ProcessStartInfo(exe, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _process = new Process { StartInfo = start };
                _process.OutputDataReceived += (s, e) => Append(e.Data);
                _process.ErrorDataReceived += (s, e) => Append(e.Data);
                _process.Start();
                Pid = _process.Id;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            /// <summary>The process number recorded at start; the only thing this test ever stops.</summary>
            public int Pid { get; }

            public bool HasExited => _process.HasExited;

            public int ExitCode => _process.ExitCode;

            public string Output
            {
                get
                {
                    lock (_output)
                    {
                        return _output.ToString();
                    }
                }
            }

            private void Append(string? line)
            {
                if (line != null)
                {
                    lock (_output)
                    {
                        _output.AppendLine(line);
                    }
                }
            }

            /// <summary>
            /// Stops this process and the processes it started, by the recorded process number.
            /// </summary>
            public void Stop()
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
            }

            public void Dispose()
            {
                Stop();
                _process.Dispose();
            }
        }

        /// <summary>
        /// Runs the UI queue in a loop until all the given tools have exited, and returns the longest time
        /// the UI thread was away from its queue between two runs (milliseconds).
        /// </summary>
        private static long PumpUntilExited(ToolRun[] tools, int timeoutMs, out long longestQueueRunMs)
        {
            long worstGap = 0;
            longestQueueRunMs = 0;

            // The heartbeat: every 50 ms a background timer posts a small job to the UI thread, the way the
            // view models post theirs, and the job records how long it waited. This is "post a job and see
            // it run": the longest wait is how long a person's click would have waited behind the queue.
            long worstWait = 0;
            using System.Threading.Timer heartbeat = new System.Threading.Timer(_ =>
            {
                long posted = Stopwatch.GetTimestamp();
                Dispatcher.UIThread.Post(() =>
                {
                    long waited = (Stopwatch.GetTimestamp() - posted) * 1000 / Stopwatch.Frequency;
                    long seen;
                    do
                    {
                        seen = Interlocked.Read(ref worstWait);
                    }
                    while (waited > seen && Interlocked.CompareExchange(ref worstWait, waited, seen) != seen);
                });
            }, null, 50, 50);
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch gap = Stopwatch.StartNew();
            while (true)
            {
                bool allDone = true;
                for (int i = 0; i < tools.Length; i++)
                {
                    if (!tools[i].HasExited)
                    {
                        allDone = false;
                    }
                }

                if (allDone)
                {
                    break;
                }

                Assert.That(total.ElapsedMilliseconds, Is.LessThan(timeoutMs),
                    "a tool did not finish within " + timeoutMs + " ms:\n" + string.Join("\n---\n", Outputs(tools)));
                long sinceLast = gap.ElapsedMilliseconds;
                if (sinceLast > worstGap)
                {
                    worstGap = sinceLast;
                }

                Dispatcher.UIThread.RunJobs();
                gap.Restart();
                Thread.Sleep(5);
            }

            Dispatcher.UIThread.RunJobs();
            longestQueueRunMs = Interlocked.Read(ref worstWait);
            return worstGap;
        }

        private static string[] Outputs(ToolRun[] tools)
        {
            string[] text = new string[tools.Length];
            for (int i = 0; i < tools.Length; i++)
            {
                text[i] = tools[i].Output;
            }

            return text;
        }

        /// <summary>
        /// Reads a whole number that follows a text in the tool's output, for example "Sent:      2000 pkts".
        /// </summary>
        private static long Figure(string output, string pattern)
        {
            // The tool prints live progress lines ("Sent: 1739 pkts  Recv: ...") while it runs and the final
            // figures under a "=== ... Results ===" header. Read the final block only.
            int block = output.LastIndexOf("=== ", StringComparison.Ordinal);
            string finalBlock = block >= 0 ? output.Substring(block) : output;
            Match match = Regex.Match(finalBlock, pattern);
            Assert.That(match.Success, Is.True, "figure not found in the tool output: " + pattern + "\n" + output);
            return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static string Total(ServiceProvider provider, string counter)
        {
            return StatisticsReader.Build(provider.GetRequiredService<IRuntimeService>().Runtime).Cell(counter, "Total") ?? "(no such counter)";
        }

        private static long TotalNumber(ServiceProvider provider, string counter)
        {
            return long.Parse(Total(provider, counter), CultureInfo.InvariantCulture);
        }

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

        /// <summary>
        /// Starts the Desktop runtime as an XOT server on a free port (the TAD call-back sample) and returns
        /// the service graph. The caller disposes it.
        /// </summary>
        private static ServiceProvider StartDesktop(out int port)
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-tad-callback.json"));
            port = FreePort();
            config.Routing.XotInterfaces[0].LocalPort = port;
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IRuntimeService>().Initialize(config);
            return provider;
        }

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

        // ------------------------------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Waits until the app has no open call left and its XOT listener shows no connection, then
        /// checks the log has no error line. The "clean shutdown" half of every burst test.
        /// </summary>
        private static void AssertQuietAndClean(ServiceProvider provider, string what)
        {
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            WaitUntil(() => TotalNumber(provider, "X.25 active virtual circuits") == 0,
                what + ": no call left open", () => "active circuits " + Total(provider, "X.25 active virtual circuits") + "\n" + AppLog(provider));
            WaitUntil(() => main.Interfaces.Count > 0 && main.Interfaces[0].Status.StartsWith("Listening, nobody connected", StringComparison.Ordinal),
                what + ": the listener shows no connection", () => main.Interfaces.Count > 0 ? main.Interfaces[0].Status : "(no line)");

            IReadOnlyList<RetroIO.Protocols.X25.Common.LogEntry> entries =
                provider.GetRequiredService<RetroIO.Protocols.X25.Common.HistoryLoggingProvider>().GetAllLogs();
            StringBuilder errors = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Level == RetroIO.Protocols.X25.Common.LogLevel.Error)
                {
                    errors.Append(entries[i].Message).Append('\n');
                }
            }

            Assert.That(errors.ToString(), Is.Empty, what + ": error lines in the app log");
        }

        /// <summary>
        /// The Packet window and the Log window hold what the engine and the log keep, no more.
        /// </summary>
        private static void AssertWindowsBounded(ServiceProvider provider, PacketWindowViewModel packetVm, LogWindowViewModel logVm)
        {
            int capacity = provider.GetRequiredService<IRuntimeService>().Runtime.HistoryCapacity;
            int logMax = provider.GetRequiredService<RetroIO.Protocols.X25.Common.HistoryLoggingProvider>().MaxEntries;
            WaitUntil(() => packetVm.Packets.Count <= capacity && logVm.LogEntries.Count <= logMax,
                "the windows to hold no more than the engine keeps",
                () => "packet rows " + packetVm.Packets.Count + " (engine keeps " + capacity + "), log rows " + logVm.LogEntries.Count + " (log keeps " + logMax + ")");
            Console.WriteLine("packet window rows " + packetVm.Packets.Count + " of " + capacity + ", log window rows " + logVm.LogEntries.Count + " of " + logMax);
        }

        /// <summary>
        /// One x25traffic stream of full 128-byte data packets into the built-in TAD, with the Packet,
        /// Log and Status windows open. The count the tool sent equals the count the app received, the
        /// Packet window is exactly as full as the engine's history, the UI thread never stalls.
        /// </summary>
        [AvaloniaTest]
        public void Stream_OfFullPackets_IntoTheTad_IsCountedExactly_AndTheUiKeepsRunning()
        {
            string traffic = FindTool("RetroX25.Traffic", "x25traffic.exe");
            using ServiceProvider provider = StartDesktop(out int port);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();

            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            packetVm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(packetVm);
            ShowAndSettle(packetWindow);
            LogWindowViewModel logVm = provider.GetRequiredService<LogWindowViewModel>();
            LogWindow logWindow = new LogWindow(logVm);
            ShowAndSettle(logWindow);

            ToolRun? tool = null;
            try
            {
                const int packets = 2000;
                tool = new ToolRun(traffic, "-x 127.0.0.1:" + port + " -d 23430200 -n " + packets + " -s 128 -r 0");
                long gap = PumpUntilExited(new[] { tool }, 60000, out long longestRun);
                Console.WriteLine("tool output:\n" + tool.Output);
                Console.WriteLine("longest gap between queue runs " + gap + " ms, longest wait of a posted job " + longestRun + " ms");

                Assert.That(tool.ExitCode, Is.EqualTo(0), tool.Output);
                long sent = Figure(tool.Output, @"Sent:\s+(\d+) pkts");
                Assert.That(sent, Is.EqualTo(packets));
                Assert.That(Figure(tool.Output, @"Sent:\s+\d+ pkts, (\d+) bytes"), Is.EqualTo(packets * 128L));

                WaitUntil(() => TotalNumber(provider, "X.25 data packets received") >= sent, "all data packets counted",
                    () => "app counted " + Total(provider, "X.25 data packets received") + ", tool sent " + sent);
                Assert.That(TotalNumber(provider, "X.25 data packets received"), Is.EqualTo(sent), "data packets: app against tool");
                Assert.That(TotalNumber(provider, "X.25 call requests received"), Is.EqualTo(1), "one call");
                Assert.That(gap, Is.LessThan(UiStallLimitMs), "the UI thread was away from its queue too long");
                Assert.That(longestRun, Is.LessThan(UiStallLimitMs), "a job posted to the UI thread waited too long");

                // The tool counts the echoes it got back (the TAD answers); what the app says it sent must
                // be at least what the tool says it received.
                long toolReceived = Figure(tool.Output, @"Received:\s+(\d+) pkts");
                Assert.That(TotalNumber(provider, "X.25 data packets sent"), Is.GreaterThanOrEqualTo(toolReceived));

                // Per-link bytes grow (the XOT links section): at least the 128 data bytes of every packet.
                StatisticsTable table = StatisticsReader.Build(runtimeService.Runtime);
                long bytesIn = long.Parse(table.Cell("Bytes received", "Total") ?? "-1", CultureInfo.InvariantCulture);
                Console.WriteLine("link bytes received " + bytesIn + ", sent " + table.Cell("Bytes sent", "Total"));
                Assert.That(bytesIn, Is.GreaterThanOrEqualTo(packets * 128L), "link bytes received");
                // The bytes the app wrote back are counted too (they were not: "Bytes sent" stayed 0 for an XOT
                // listener). The tool counts the data bytes it got back; the app's figure adds the 3 header bytes per packet.
                long bytesOut = long.Parse(table.Cell("Bytes sent", "Total") ?? "-1", CultureInfo.InvariantCulture);
                long toolReceivedBytes = Figure(tool.Output, @"Received:\s+\d+ pkts, (\d+) bytes");
                Assert.That(bytesOut, Is.GreaterThanOrEqualTo(toolReceivedBytes), "link bytes sent");

                AssertWindowsBounded(provider, packetVm, logVm);
                int capacity = runtimeService.Runtime.HistoryCapacity;
                Assert.That(packetVm.Packets.Count, Is.EqualTo(capacity), "the Packet window shows exactly what the engine keeps");
                IReadOnlyList<RetroIO.Protocols.X25.Models.HistoryItem> history = runtimeService.Runtime.GetHistory();
                Assert.That(packetVm.Packets[packetVm.Packets.Count - 1].Id, Is.EqualTo(history[history.Count - 1].Id), "the newest line is the engine's newest");

                AssertQuietAndClean(provider, "stream");
                Assert.That(tool.HasExited, Is.True);
            }
            finally
            {
                tool?.Dispose();
                logWindow.Close();
                packetWindow.Close();
            }
        }

        /// <summary>
        /// A call storm: x25traffic opens 100 calls into the TAD, sends 3 data packets on each and clears
        /// them. The app counts the same calls and packets, and no circuit is left behind.
        /// </summary>
        [AvaloniaTest]
        public void CallStorm_IntoTheTad_IsCountedExactly_AndLeavesNoCircuit()
        {
            string traffic = FindTool("RetroX25.Traffic", "x25traffic.exe");
            using ServiceProvider provider = StartDesktop(out int port);
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            packetVm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(packetVm);
            ShowAndSettle(packetWindow);
            LogWindowViewModel logVm = provider.GetRequiredService<LogWindowViewModel>();

            ToolRun? tool = null;
            try
            {
                const int calls = 100;
                tool = new ToolRun(traffic, "-x 127.0.0.1:" + port + " -d 23430200 -C -n " + calls + " -r 0 -p 3");
                long gap = PumpUntilExited(new[] { tool }, 90000, out long longestRun);
                Console.WriteLine("tool output:\n" + tool.Output);
                Console.WriteLine("longest gap " + gap + " ms, longest wait of a posted job " + longestRun + " ms");
                Assert.That(tool.ExitCode, Is.EqualTo(0), tool.Output);

                long connected = Figure(tool.Output, @"VCs opened:\s+\d+ \((\d+) connected");
                long dataPackets = Figure(tool.Output, @"Data sent:\s+(\d+) packets");
                Assert.That(connected, Is.EqualTo(calls), "every call was accepted: " + tool.Output);

                WaitUntil(() => TotalNumber(provider, "X.25 data packets received") >= dataPackets, "all data packets counted",
                    () => "app counted " + Total(provider, "X.25 data packets received") + ", tool sent " + dataPackets);
                Assert.That(TotalNumber(provider, "X.25 call requests received"), Is.EqualTo(calls), "calls: app against tool");
                Assert.That(TotalNumber(provider, "X.25 data packets received"), Is.EqualTo(dataPackets), "data packets: app against tool");
                Assert.That(gap, Is.LessThan(UiStallLimitMs));
                Assert.That(longestRun, Is.LessThan(UiStallLimitMs));
                AssertWindowsBounded(provider, packetVm, logVm);
                AssertQuietAndClean(provider, "storm");
            }
            finally
            {
                tool?.Dispose();
                packetWindow.Close();
            }
        }

        /// <summary>
        /// Three tool processes at the same time (two streams with different packet sizes and one call
        /// storm) into the same app. The totals are the sums of what the three tools sent.
        /// </summary>
        [AvaloniaTest]
        public void SeveralTools_AtOnce_AreAddedUpExactly()
        {
            string traffic = FindTool("RetroX25.Traffic", "x25traffic.exe");
            using ServiceProvider provider = StartDesktop(out int port);
            PacketWindowViewModel packetVm = provider.GetRequiredService<PacketWindowViewModel>();
            packetVm.IsCollapsed = false;
            PacketWindow packetWindow = new PacketWindow(packetVm);
            ShowAndSettle(packetWindow);
            LogWindowViewModel logVm = provider.GetRequiredService<LogWindowViewModel>();

            List<ToolRun> started = new List<ToolRun>();
            try
            {
                string target = "-x 127.0.0.1:" + port + " -d 23430200 ";
                started.Add(new ToolRun(traffic, target + "-n 1500 -s 128 -r 0"));
                started.Add(new ToolRun(traffic, target + "-n 1500 -s 16 -r 0"));
                started.Add(new ToolRun(traffic, target + "-C -n 40 -r 0 -p 4"));
                ToolRun[] tools = started.ToArray();
                long gap = PumpUntilExited(tools, 120000, out long longestRun);
                for (int i = 0; i < tools.Length; i++)
                {
                    Console.WriteLine("tool " + i + " output:\n" + tools[i].Output);
                    Assert.That(tools[i].ExitCode, Is.EqualTo(0), tools[i].Output);
                }

                long expectedData = Figure(tools[0].Output, @"Sent:\s+(\d+) pkts") + Figure(tools[1].Output, @"Sent:\s+(\d+) pkts")
                    + Figure(tools[2].Output, @"Data sent:\s+(\d+) packets");
                long expectedCalls = 2 + Figure(tools[2].Output, @"VCs opened:\s+\d+ \((\d+) connected");
                WaitUntil(() => TotalNumber(provider, "X.25 data packets received") >= expectedData, "all data packets counted",
                    () => "app counted " + Total(provider, "X.25 data packets received") + ", tools sent " + expectedData);
                Console.WriteLine("longest gap " + gap + " ms, longest wait of a posted job " + longestRun + " ms");
                Assert.That(TotalNumber(provider, "X.25 data packets received"), Is.EqualTo(expectedData), "data packets: app against the three tools");
                Assert.That(TotalNumber(provider, "X.25 call requests received"), Is.EqualTo(expectedCalls), "calls: app against the three tools");
                Assert.That(gap, Is.LessThan(UiStallLimitMs));
                Assert.That(longestRun, Is.LessThan(UiStallLimitMs));
                AssertWindowsBounded(provider, packetVm, logVm);
                AssertQuietAndClean(provider, "three tools");
            }
            finally
            {
                for (int i = 0; i < started.Count; i++)
                {
                    started[i].Dispose();
                }

                packetWindow.Close();
            }
        }

        /// <summary>
        /// A burst of Q-bit data (X.29 control messages) from a client: 300 READ PARAMETERS messages,
        /// each of which the TAD must answer with a PARAMETER INDICATION that also has the Q bit. Both
        /// ends count the same number. (x25traffic cannot set the Q bit, so this uses the client library
        /// that x25traffic is built on.)
        /// </summary>
        [AvaloniaTest]
        public void QBitBurst_EveryReadIsAnswered()
        {
            using ServiceProvider provider = StartDesktop(out int port);
            int answers = 0;
            using RetroIO.Protocols.X25.Client.X25Client client = new RetroIO.Protocols.X25.Client.X25Client { ServiceName = "q-burst" };
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                // A PARAMETER INDICATION is X.29 message type 0 with the Q bit set.
                if (qBit && length > 0 && data[0] == 0x00)
                {
                    Interlocked.Increment(ref answers);
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
            Assert.That(call, Is.Not.Null, AppLog(provider));

            const int reads = 300;
            byte[] read = new byte[] { 0x04, 2, 3, 4 };
            int accepted = 0;
            Stopwatch watch = Stopwatch.StartNew();
            while (accepted < reads && watch.ElapsedMilliseconds < 30000)
            {
                if (client.Send(call!, read, read.Length, true))
                {
                    accepted++;
                }
                else
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(1);
                }
            }

            Assert.That(accepted, Is.EqualTo(reads), "the client could not send all the reads");
            WaitUntil(() => Volatile.Read(ref answers) >= reads, "an answer to every read",
                () => "answers " + Volatile.Read(ref answers) + " of " + reads + "\n" + AppLog(provider), 30000);
            Assert.That(Volatile.Read(ref answers), Is.EqualTo(reads), "PARAMETER INDICATION answers: client against sent reads");
            client.Clear(call!);
            // The client closes its connection to the listener, so the listener shows nobody connected.
            client.Dispose();
            AssertQuietAndClean(provider, "Q burst");
        }

        /// <summary>
        /// The PAD window is the receiver of a burst. It calls x25echo (a real process) through the app's
        /// own XOT listener, and 300 long lines are typed at it; the echo comes back as many packets. Every
        /// line must be in the console, and the console must show the last line.
        /// </summary>
        [AvaloniaTest]
        public void PadWindow_ReceivingABurst_FollowsTheOutput()
        {
            string echo = FindTool("RetroX25.Echo", "x25echo.exe");

            // The app's own built-in switch carries the call: the PAD dials the switch's caller port, x25echo
            // dials the switch's own echo port (the switch joins a call from one port to a service on
            // ANOTHER port; the ports are numbered 0 and 1 without a gap, the switch numbers them in order), and the exact route sends 23500001 to the echo port.
            int callers = FreePort();
            int echoPort = FreePort();
            string ini = Path.Combine(Path.GetTempPath(), "x25burst-switch-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(ini,
                "[switch]\n" +
                "name = BurstSwitch\n" +
                "dnic = 2350\n" +
                "mgmt_addr = 23500000\n" +
                "[port.0]\n" +
                "type = access\n" +
                "transport = xot\n" +
                "mode = dce\n" +
                "host = 127.0.0.1\n" +
                "port = " + callers + "\n" +
                "lci_count = 64\n" +
                "[port.1]\n" +
                "type = access\n" +
                "transport = xot\n" +
                "mode = dce\n" +
                "host = 127.0.0.1\n" +
                "port = " + echoPort + "\n" +
                "lci_count = 64\n" +
                "[route.2]\n" +
                "type = exact\n" +
                "address = 23500001\n" +
                "port = 1\n" +
                "[route.0]\n" +
                "type = dnic\n" +
                "dnic = 2350\n" +
                "port = 0\n" +
                "[user.0]\n" +
                "name = admin\n" +
                "password = admin\n" +
                "level = 1\n");
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-embedded-switch.json"));
            EmbeddedSwitchOptions options = new EmbeddedSwitchOptions { Enabled = true, ConfigFile = ini };
            config.Routing.XotInterfaces[0].RemotePort = callers;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, options);
            using ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IRuntimeService>().Initialize(config);
            provider.GetRequiredService<EmbeddedSwitchService>().StartIfEnabled();

            ToolRun? tool = null;
            PADClientWindow? window = null;
            try
            {
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                Assert.That(main.SwitchStatus, Does.Contain("running"), main.SwitchStatus);
                tool = new ToolRun(echo, "-x 127.0.0.1:" + echoPort + " -a 23500001");
                Thread.Sleep(1500);

                PADClientWindowViewModel vm = new PADClientWindowViewModel(
                    NullLogger<PADClientWindowViewModel>.Instance, provider.GetRequiredService<IRuntimeService>(), 1);
                window = new PADClientWindow(vm) { Width = 900, Height = 400 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                // Local echo off (X.3 parameter 2 = 0): the far end's echo is then the only copy of each line, so
                // the typed text and the received text cannot be mixed together letter by letter.
                window.KeyTextInput("SET 2:0");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                WaitUntil(() => vm.ConsoleText.TrimEnd().EndsWith("PAD>"), "the prompt after SET", () => vm.ConsoleText);
                window.KeyTextInput("CALL 23500001");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                WaitUntil(() => vm.ConsoleText.Contains("COM"), "COM from the echo program", () => vm.ConsoleText + "\n" + AppLog(provider) + "\n" + tool.Output, 15000);

                const int lines = 300;
                Stopwatch watch = Stopwatch.StartNew();
                long worstRun = 0;
                for (int i = 0; i < lines; i++)
                {
                    // One turn of the UI: type a line, press Enter, let the queued jobs run. The longest turn is the stall.
                    Stopwatch run = Stopwatch.StartNew();
                    window.KeyTextInput("LINE" + i.ToString("D4", CultureInfo.InvariantCulture) + " " + new string('x', 90));
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Dispatcher.UIThread.RunJobs();
                    if (run.ElapsedMilliseconds > worstRun)
                    {
                        worstRun = run.ElapsedMilliseconds;
                    }
                }

                string last = "LINE" + (lines - 1).ToString("D4", CultureInfo.InvariantCulture);
                WaitUntil(() => CountOf(vm.ConsoleText, last) >= 1, "the echo of the last line", () => vm.ConsoleText.Substring(Math.Max(0, vm.ConsoleText.Length - 400)), 60000);
                Console.WriteLine("console " + vm.ConsoleText.Length + " characters; keyed " + lines + " lines in " + watch.ElapsedMilliseconds + " ms; worst UI queue run " + worstRun + " ms");
                int previousAt = -1;
                for (int i = 0; i < lines; i++)
                {
                    string marker = "LINE" + i.ToString("D4", CultureInfo.InvariantCulture);
                    Assert.That(CountOf(vm.ConsoleText, marker), Is.EqualTo(1), marker + " must be shown exactly once (the far end's echo)" + "\n" + Around(vm.ConsoleText, marker));
                    int at = vm.ConsoleText.IndexOf(marker, StringComparison.Ordinal);
                    Assert.That(at, Is.GreaterThan(previousAt), marker + " is out of order");
                    previousAt = at;
                }

                Assert.That(worstRun, Is.LessThan(UiStallLimitMs));
                ScrollViewer scroll = window.FindControl<ScrollViewer>("ConsoleScroll")!;
                // The layout catches up with the text in a few passes; wait for it, then check the last line.
                WaitUntil(() =>
                {
                    window.UpdateLayout();
                    return scroll.Extent.Height > scroll.Viewport.Height
                        && scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 1.0;
                }, "the console to scroll to the last line",
                    () => "offset " + scroll.Offset.Y + ", viewport " + scroll.Viewport.Height + ", extent " + scroll.Extent.Height
                        + ", box text " + window.FindControl<SelectableTextBlock>("ConsoleBox")!.Text?.Length + ", vm text " + vm.ConsoleText.Length, 10000);
                Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height));
                Assert.That(scroll.Offset.Y + scroll.Viewport.Height, Is.GreaterThanOrEqualTo(scroll.Extent.Height - 1.0),
                    "the last line is out of view: offset " + scroll.Offset.Y + ", viewport " + scroll.Viewport.Height + ", extent " + scroll.Extent.Height);

                window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, null);
                window.KeyTextInput("CLEAR");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                WaitUntil(() => vm.ConsoleText.Contains("CLEARED"), "CLEARED", () => vm.ConsoleText);
                tool.Stop();
                WaitUntil(() => TotalNumber(provider, "X.25 active virtual circuits") == 0, "no call left",
                    () => "active " + Total(provider, "X.25 active virtual circuits") + ", in total " + Total(provider, "X.25 virtual circuits in total")
                        + ", clear requests seen: " + (AppLog(provider).Split("Clear").Length - 1));
            }
            finally
            {
                tool?.Dispose();
                window?.Close();
                File.Delete(ini);
            }
        }

        /// <summary>
        /// The text around the first place a marker occurs, for a failure message.
        /// </summary>
        private static string Around(string text, string marker)
        {
            int at = Math.Max(0, text.IndexOf(marker, StringComparison.Ordinal) - 200);
            return text.Substring(at, Math.Min(900, text.Length - at)).Replace("\r", "<CR>");
        }

        private static int CountOf(string text, string part)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(part, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += part.Length;
            }

            return count;
        }
    }
}
