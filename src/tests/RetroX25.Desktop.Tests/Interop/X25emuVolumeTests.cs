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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Headless.NUnit;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RetroIO.Protocols.X25.Monitor;
using RetroX25.Desktop.Services;
using RetroX25.Tests;
using OurApp = RetroX25.Desktop.Tests.X25emuPeerTests.OurApp;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Volume: x25traffic from x25emu and x25traffic from RetroX25 at the same time, both through x25emu's Switch A, and
    /// every counter compared on both sides.
    /// </summary>
    /// <remarks>
    /// <para>What runs at once (all started together):</para>
    /// <list type="number">
    /// <item><description>x25emu's x25traffic stream (one call, 128-byte packets) and call storm (calls started 250 ms apart,
    /// 3 packets each), dialling 23520001. x25emu's Switch A routes them to its HDLC-over-TCP trunk (port 6), which dials OUR listening port and runs a LAPB link; our
    /// built-in TAD answers and echoes.</description></item>
    /// <item><description>Our x25traffic.exe stream and call storm, dialling 23430001 on x25emu's access port 2001. Switch A routes
    /// them to the endpoint port 3 (XOT 2007), where our <see cref="X25emuEchoPool"/> answers and echoes. x25emu's own x25echo
    /// takes ONE call, so it cannot answer a storm; the pool keeps a fresh idle connection ready for every call.</description></item>
    /// </list>
    /// <para>The storm calls are 250 ms apart, not all in one instant, because x25emu's XOT trunk (a different path, used in X25emuConnectionTests) loses calls it dials in the same
    /// millisecond (measured in <see cref="X25emuConnectionTests"/>).</para>
    /// <para>The time limit is stated in <see cref="LimitSeconds"/>: it is a hang detector, not a speed target.</para>
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    public sealed class X25emuVolumeTests
    {
        private const int LimitSeconds = 150;
        private const int StreamPackets = 2000;
        private const int StormCalls = 8;
        private const int StormPackets = 3;

        private static long Figure(string output, string pattern)
        {
            int block = output.LastIndexOf("=== ", StringComparison.Ordinal);
            string finalBlock = block >= 0 ? output.Substring(block) : output;
            Match match = Regex.Match(finalBlock, pattern);
            Assert.That(match.Success, Is.True, "figure not found: " + pattern + "\n" + output);
            return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static string FindOurTool()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string bin = Path.Combine(dir.FullName, "RetroX25.Traffic", "bin");
                if (Directory.Exists(bin))
                {
                    string[] found = Directory.GetFiles(bin, "x25traffic.exe", SearchOption.AllDirectories);
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

            throw new FileNotFoundException("x25traffic.exe is not built (RetroX25.Traffic/bin)");
        }

        /// <summary>One of our tool processes: output collected, stopped by its recorded process number only.</summary>
        private sealed class WindowsTool : IDisposable
        {
            private readonly Process _process;
            private readonly StringBuilder _output = new StringBuilder();

            internal WindowsTool(string exe, string arguments)
            {
                ProcessStartInfo start = new ProcessStartInfo(exe, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                _process = new Process { StartInfo = start };
                _process.OutputDataReceived += (s, e) => Add(e.Data);
                _process.ErrorDataReceived += (s, e) => Add(e.Data);
                _process.Start();
                Pid = _process.Id;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            internal int Pid { get; }

            internal bool HasExited => _process.HasExited;

            internal int ExitCode => _process.ExitCode;

            internal string Output
            {
                get
                {
                    lock (_output)
                    {
                        return _output.ToString();
                    }
                }
            }

            private void Add(string? line)
            {
                if (line != null)
                {
                    lock (_output)
                    {
                        _output.AppendLine(line);
                    }
                }
            }

            public void Dispose()
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(true);
                        _process.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }

                _process.Dispose();
            }
        }

        private static long Stat(OurApp app, string counter)
        {
            string? cell = StatisticsReader.Build(app.Service.Runtime).Cell(counter, "Total");
            return long.Parse(cell ?? "-1", CultureInfo.InvariantCulture);
        }

        [AvaloniaTest]
        public void TrafficFromBothImplementations_AtOnce_EveryCounterAddsUp_AndNothingIsLeftOpen()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            string ourTool = FindOurTool();
            Stopwatch total = Stopwatch.StartNew();
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen, null, "23520001", hdlcListener: true);

            // A pool big enough for our stream (1 call) and our storm (StormCalls calls), with a spare. The limit is 16.
            using X25emuEchoPool pool = new X25emuEchoPool(X25emuEnvironment.WslHost!, X25emuEnvironment.CSharpAccessA, "23430001", StormCalls + 2);
            // x25emu's HDLC trunk (Switch A port 6) dials our listening port by itself; the calls go in only once it has.
            XotLinkHealthTests.WaitUntil(() => X25emuPeerTests.OurTcpConnections(string.Empty, 0, X25emuEnvironment.OurHdlcPort) >= 1, "x25emu's HDLC trunk to dial our port", () => app.Log(), 60000);
            Thread.Sleep(1500);
            Dictionary<int, PortStatus> before = X25emuMonitor.Snapshot(X25emuEnvironment.MonitorA);

            // Our storm is StormCalls separate x25traffic.exe processes with one call each. A single "-C" run would put all its calls on
            // ONE TCP connection (RetroIO's client does that), and x25emu's access port answers only the first call on a connection
            // (RFC 1613 section 5 asks for one connection per call; measured 2026-11-12: 1 of 8 answered, see docs/crossenv-testing.md and
            // PLAN.md). Going through our app does not help: the app does not forward a call from one interface to another.
            string ours = "-x " + X25emuEnvironment.WslHost + ":" + X25emuEnvironment.AccessA + " -d 23430001 ";
            string x25emuTool = X25emuEnvironment.ToolPath("x25traffic");
            List<WindowsTool> windows = new List<WindowsTool>();
            List<X25emuEnvironment.LinuxProcess> linux = new List<X25emuEnvironment.LinuxProcess>();
            try
            {
                // windows[0] is our stream, windows[1..StormCalls] are our one-call storm processes, 250 ms apart (the same pace as the
                // x25emu storm below).
                windows.Add(new WindowsTool(ourTool, ours + "-n " + StreamPackets + " -s 128 -r 0"));
                linux.Add(new X25emuEnvironment.LinuxProcess(string.Empty, x25emuTool, "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-d", "23520001", "-n", StreamPackets.ToString(CultureInfo.InvariantCulture), "-s", "128", "-r", "0"));
                linux.Add(new X25emuEnvironment.LinuxProcess(string.Empty, x25emuTool, "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-d", "23520001", "-C", "-n", StormCalls.ToString(CultureInfo.InvariantCulture), "-r", "4", "-p", StormPackets.ToString(CultureInfo.InvariantCulture)));
                for (int i = 0; i < StormCalls; i++)
                {
                    windows.Add(new WindowsTool(ourTool, ours + "-C -n 1 -r 0 -p " + StormPackets));
                    Thread.Sleep(250);
                }

                long deadline = Environment.TickCount64 + LimitSeconds * 1000L;
                long lastReport = Environment.TickCount64;
                while (true)
                {
                    bool done = true;
                    for (int i = 0; i < windows.Count; i++)
                    {
                        done &= windows[i].HasExited;
                    }

                    for (int i = 0; i < linux.Count; i++)
                    {
                        done &= linux[i].HasExited;
                    }

                    if (done)
                    {
                        break;
                    }

                    if (Environment.TickCount64 > deadline)
                    {
                        Assert.Fail("a tool did not finish within " + LimitSeconds + " s\nours stream:\n" + windows[0].Output
                            + "\nx25emu stream:\n" + linux[0].Output + "\nx25emu storm:\n" + linux[1].Output + "\napp log:\n" + app.Log());
                    }

                    if (Environment.TickCount64 - lastReport > 10000)
                    {
                        lastReport = Environment.TickCount64;
                        int running = 0;
                        for (int i = 0; i < windows.Count; i++)
                        {
                            running += windows[i].HasExited ? 0 : 1;
                        }

                        TestContext.Progress.WriteLine("volume: " + total.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s, our tools still running " + running
                            + ", x25emu stream " + (linux[0].HasExited ? "done" : "running") + ", x25emu storm " + (linux[1].HasExited ? "done" : "running"));
                    }

                    Thread.Sleep(100);
                }
                double seconds = total.Elapsed.TotalSeconds;
                StringBuilder facts = new StringBuilder();
                facts.AppendLine("time to finish: " + seconds.ToString("F1", CultureInfo.InvariantCulture) + " s (limit " + LimitSeconds + " s)");
                facts.AppendLine("our stream:\n" + windows[0].Output);
                for (int i = 1; i < windows.Count; i++)
                {
                    facts.AppendLine("our storm call " + i + ":\n" + windows[i].Output);
                }

                facts.AppendLine("x25emu stream:\n" + linux[0].Output);
                facts.AppendLine("x25emu storm:\n" + linux[1].Output);
                TestContext.Progress.WriteLine(facts.ToString());

                for (int i = 0; i < windows.Count; i++)
                {
                    Assert.That(windows[i].ExitCode, Is.EqualTo(0), facts.ToString());
                }

                Assert.That(linux[0].ExitCode, Is.EqualTo(0), facts.ToString());
                Assert.That(linux[1].ExitCode, Is.EqualTo(0), facts.ToString());
                // What each tool says it did.
                long oursStreamSent = Figure(windows[0].Output, @"Sent:\s+(\d+) pkts");
                long oursStormSent = 0;
                long oursStormConnected = 0;
                for (int i = 1; i < windows.Count; i++)
                {
                    oursStormSent += Figure(windows[i].Output, @"Data sent:\s+(\d+) packets");
                    oursStormConnected += Figure(windows[i].Output, @"VCs opened:\s+\d+ \((\d+) connected");
                }

                long theirStreamSent = Figure(linux[0].Output, @"Sent:\s+(\d+) pkts");
                long theirStormSent = Figure(linux[1].Output, @"Data sent:\s+(\d+) packets");
                long theirStormConnected = Figure(linux[1].Output, @"VCs opened:\s+\d+ \((\d+) connected");
                Assert.That(oursStreamSent, Is.EqualTo(StreamPackets), facts.ToString());
                Assert.That(theirStreamSent, Is.EqualTo(StreamPackets), facts.ToString());
                Assert.That(oursStormConnected, Is.EqualTo(StormCalls), facts.ToString());
                Assert.That(theirStormConnected, Is.EqualTo(StormCalls), facts.ToString());
                Assert.That(oursStormSent, Is.EqualTo(StormCalls * StormPackets), facts.ToString());
                Assert.That(theirStormSent, Is.EqualTo(StormCalls * StormPackets), facts.ToString());

                // Direction 1, ours to x25emu: every data packet we sent reached the pool, byte count and call count too.
                long oursSent = oursStreamSent + oursStormSent;
                XotLinkHealthTests.WaitUntil(() => pool.Received().Count >= oursSent, "the pool to hold every packet we sent", () => "pool " + pool.Received().Count + " of " + oursSent, 30000);
                Assert.That(pool.Received().Count, Is.EqualTo(oursSent), "data packets: our tools against the pool");
                Assert.That(pool.Calls, Is.EqualTo(1 + StormCalls), "calls: our tools against the pool");
                // Direction 2, x25emu to us: every packet and call x25emu's tools say they made is in our counters.
                long theirSent = theirStreamSent + theirStormSent;
                XotLinkHealthTests.WaitUntil(() => Stat(app, "X.25 data packets received") >= theirSent, "our app to count every packet", () => "app " + Stat(app, "X.25 data packets received") + " of " + theirSent + "\n" + app.Log(), 30000);
                Assert.That(Stat(app, "X.25 data packets received"), Is.EqualTo(theirSent), "data packets: x25emu's tools against our app");
                Assert.That(Stat(app, "X.25 call requests received"), Is.EqualTo(1 + StormCalls), "calls: x25emu's tools against our app");

                // x25emu's own counters (Switch A, per port, risen during the run). It counts one call on the port the call came
                // in on (access 0) and one on the port it left by (trunk 4 towards us, endpoint 3 towards the pool).
                Dictionary<int, PortStatus> after = X25emuMonitor.Snapshot(X25emuEnvironment.MonitorA);
                string delta = X25emuMonitor.Delta(before, after);
                TestContext.Progress.WriteLine("x25emu monitor, Switch A, risen during the run:\n" + delta);
                int calls = 1 + StormCalls;
                Assert.That(X25emuMonitor.Rise(before, after, 0, p => p.CallsIn), Is.EqualTo(2 * calls), "x25emu port 0: calls in (ours + its own)\n" + delta);
                Assert.That(X25emuMonitor.Rise(before, after, 3, p => p.CallsOut), Is.EqualTo(calls), "x25emu port 3: calls out to the pool\n" + delta);
                Assert.That(X25emuMonitor.Rise(before, after, 6, p => p.CallsOut), Is.EqualTo(calls), "x25emu port 6 (HDLC trunk to us): calls out to our app\n" + delta);
                Assert.That(X25emuMonitor.Rise(before, after, 4, p => p.CallsOut), Is.EqualTo(0), "x25emu port 4 (XOT trunk) is not used here\n" + delta);

                // Nothing left open: our app's calls are gone, x25emu's trunk may keep idle spare connections, and no connection of
                // our tools to the access port is left.
                XotLinkHealthTests.WaitUntil(() => Stat(app, "X.25 active virtual circuits") == 0, "no call left open in our app", () => app.Log(), 30000);
                // x25emu's XOT trunk dials spare connections to us for the next call and keeps them (measured 0 to 5 after a burst, and
                // it decides how many; see docs/crossenv-testing.md). They carry no circuit, which the wait above has just proved, so
                // the number is reported and not limited. (An earlier version waited for "at most 1" and failed in 2 of 11 repeated runs on 2026-11-13.)
                TestContext.Progress.WriteLine("spare connections x25emu keeps open to us, no circuit on them: " + app.Peer.GetPeerReport().AcceptedOpenNow);
                XotLinkHealthTests.WaitUntil(() => X25emuPeerTests.OurTcpConnections(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessA) == 0, "no TCP connection of ours left on the access port", () => "open: " + X25emuPeerTests.OurTcpConnections(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessA), 30000);
                Assert.That(app.Errors(), Is.Empty, "error lines in our log");
                Assert.That(seconds, Is.LessThan(LimitSeconds));
            }
            finally
            {
                for (int i = 0; i < windows.Count; i++)
                {
                    windows[i].Dispose();
                }

                for (int i = 0; i < linux.Count; i++)
                {
                    linux[i].Dispose();
                }
            }
        }
    }
}
