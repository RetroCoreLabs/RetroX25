//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace RetroX25.Tests
{
    /// <summary>
    /// What the cross-environment tests need to know about the x25emu network (the C emulator in WSL), and a check that
    /// says in plain words what is missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything comes from environment variables, so no machine path is in the repository. The runner
    /// <c>demo/crossenv-test.ps1</c> sets them and starts the network; to run by hand, set them yourself.
    /// </para>
    /// <list type="table">
    /// <listheader><term>Variable</term><description>Meaning (default)</description></listheader>
    /// <item><term>X25EMU_WSL_HOST</term><description>The address Windows uses to reach the x25emu listeners. Its absence
    /// skips every Interop test.</description></item>
    /// <item><term>X25EMU_TOOLS</term><description>The folder IN WSL that holds the built x25emu programs (x25traffic,
    /// x25echo, x25pad, x25trace).</description></item>
    /// <item><term>X25EMU_WINDOWS_HOST</term><description>The address WSL uses to reach this Windows machine (needed by the
    /// tests where x25emu dials us).</description></item>
    /// <item><term>X25EMU_WSL_DISTRO</term><description>The WSL distribution name (the default distribution).</description></item>
    /// <item><term>X25EMU_ACCESS_PORT</term><description>Switch A's access port for callers (2001).</description></item>
    /// <item><term>X25EMU_OUR_XOT_PORT / X25EMU_OUR_HDLC_PORT</term><description>The Windows ports Switch A dials for calls to
    /// DNIC 2351 / 2352 (2210 / 2211).</description></item>
    /// </list>
    /// </remarks>
    internal static class X25emuEnvironment
    {
        /// <summary>The x25emu network's fixed ports and addresses (see demo/crossenv-wsl.sh).</summary>
        internal const int AccessA = 2001;
        internal const int EchoAccessC = 2006;
        internal const int CSharpAccessA = 2007;
        internal const int CSharpAccessC = 2009;
        internal const int HdlcTrunkListen = 2010;
        internal const int XotTrunkListen = 2011;
        internal const int MonitorA = 4101;
        internal const string EchoAddress = "23420001";

        internal static string? WslHost => Environment.GetEnvironmentVariable("X25EMU_WSL_HOST");

        internal static string? Tools => Environment.GetEnvironmentVariable("X25EMU_TOOLS");

        internal static string? WindowsHost => Environment.GetEnvironmentVariable("X25EMU_WINDOWS_HOST");

        private static string? Distro => Environment.GetEnvironmentVariable("X25EMU_WSL_DISTRO");

        internal static int OurXotPort => IntVariable("X25EMU_OUR_XOT_PORT", 2210);

        internal static int OurHdlcPort => IntVariable("X25EMU_OUR_HDLC_PORT", 2211);

        internal static int AccessPort => IntVariable("X25EMU_ACCESS_PORT", AccessA);

        private static int IntVariable(string name, int fallback)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;
        }

        /// <summary>
        /// Skips the test when the variable is not set; fails it, saying what is missing, when the network is set up wrongly.
        /// </summary>
        internal static void RequireReady(bool needInbound = false)
        {
            if (string.IsNullOrWhiteSpace(WslHost))
            {
                Assert.Ignore("Set X25EMU_WSL_HOST (the address of the running x25emu network) to run this test; demo/crossenv-test.ps1 does it all.");
            }

            string problems = Check(needInbound);
            Assert.That(problems, Is.Empty, "the x25emu test setup is not ready:" + Environment.NewLine + problems);
        }

        private static string? _checkedOutbound;
        private static string? _checkedInbound;

        /// <summary>
        /// Looks at the setup and describes, in plain words, everything that is missing. Empty when all is well. Done once
        /// per kind of check.
        /// </summary>
        /// <param name="needInbound">
        /// True to also try that WSL can open a connection to THIS test program (the Windows firewall decides, program by
        /// program). The tests where x25emu dials us need it; the others only connect out.
        /// </param>
        internal static string Check(bool needInbound = false)
        {
            lock (typeof(X25emuEnvironment))
            {
                if (needInbound)
                {
                    _checkedInbound ??= RunCheck(true);
                    return _checkedInbound;
                }

                _checkedOutbound ??= RunCheck(false);
                return _checkedOutbound;
            }
        }

        private static string RunCheck(bool needInbound)
        {
            StringBuilder problems = new StringBuilder();
            if (string.IsNullOrWhiteSpace(WslHost))
            {
                return "- X25EMU_WSL_HOST is not set, so nothing is known about the x25emu network.\n";
            }

            // 1. WSL itself.
            string running = RunWsl("echo ok", 15000, out int exit);
            if (exit != 0 || !running.Contains("ok"))
            {
                problems.Append("- WSL does not answer (wsl.exe returned ").Append(exit).Append(": ").Append(running.Trim()).Append("). Start it, or set X25EMU_WSL_DISTRO.\n");
                return problems.ToString();
            }

            // 2. The x25emu programs.
            if (string.IsNullOrWhiteSpace(Tools))
            {
                problems.Append("- X25EMU_TOOLS is not set (it is the folder in WSL that holds x25emu's built programs).\n");
            }
            else
            {
                string[] programs = { "x25switch", "x25echo", "x25traffic", "x25pad", "x25trace" };
                for (int i = 0; i < programs.Length; i++)
                {
                    RunWsl("test -x '" + Tools!.TrimEnd('/') + "/" + programs[i] + "'", 15000, out int found);
                    if (found != 0)
                    {
                        problems.Append("- x25emu is not built: ").Append(Tools.TrimEnd('/')).Append('/').Append(programs[i]).Append(" is missing or cannot run in WSL. Run make in the x25emu checkout.\n");
                    }
                }
            }

            // 3. The network's ports, reached from Windows.
            int[] ports = { AccessPort, EchoAccessC, CSharpAccessA, CSharpAccessC, HdlcTrunkListen, XotTrunkListen, MonitorA };
            for (int i = 0; i < ports.Length; i++)
            {
                if (!CanConnect(WslHost!, ports[i]))
                {
                    problems.Append("- Nothing answers at ").Append(WslHost).Append(':').Append(ports[i])
                        .Append(": the x25emu network is not running, or Windows cannot reach it at that address (try the WSL address from 'wsl hostname -I'; 127.0.0.1 may not be forwarded).\n");
                }
            }

            // 4. The other way: WSL reaching a listener on this machine (the tests where x25emu dials us).
            if (!needInbound)
            {
                return problems.ToString();
            }

            if (string.IsNullOrWhiteSpace(WindowsHost))
            {
                problems.Append("- X25EMU_WINDOWS_HOST is not set (the address WSL uses to reach this machine), so the tests where x25emu dials us cannot run.\n");
            }
            else
            {
                TcpListener probe = new TcpListener(IPAddress.Any, 0);
                probe.Start();
                try
                {
                    int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                    RunWsl("timeout 5 bash -c 'echo > /dev/tcp/" + WindowsHost + "/" + port + "'", 15000, out int reached);
                    if (reached != 0)
                    {
                        problems.Append("- WSL cannot open a TCP connection to this machine at ").Append(WindowsHost)
                            .Append(": the Windows firewall probably blocks inbound TCP to the test program (allow it for the private network), or the address is wrong.\n");
                    }
                }
                finally
                {
                    probe.Stop();
                }
            }

            return problems.ToString();
        }

        private static bool CanConnect(string host, int port)
        {
            try
            {
                using TcpClient client = new TcpClient();
                Task connect = client.ConnectAsync(host, port);
                return connect.Wait(3000) && client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void AddDistro(ProcessStartInfo start)
        {
            if (!string.IsNullOrWhiteSpace(Distro))
            {
                start.ArgumentList.Add("-d");
                start.ArgumentList.Add(Distro!);
            }
        }

        /// <summary>
        /// Runs one short shell command in WSL and returns its output. The command is a bash script text.
        /// </summary>
        internal static string RunWsl(string bashCommand, int timeoutMs, out int exitCode)
        {
            ProcessStartInfo start = new ProcessStartInfo("wsl.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            AddDistro(start);
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("bash");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(bashCommand);
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                // Only the process this call started is stopped, by its own number.
                process.Kill(true);
                exitCode = -1;
                return "timed out";
            }

            exitCode = process.ExitCode;
            return output.Result + error.Result;
        }

        /// <summary>
        /// A program running in WSL, started by a test, stopped by its Linux process number.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Started as <c>wsl.exe -e bash -c 'echo $$; exec "$@"'</c>: the shell prints its own number and then BECOMES the
        /// program, so that number is the program's. The Windows side (<c>wsl.exe</c>) is also recorded and stopped.
        /// WSL ends every Linux process when its wsl.exe ends, so the program lives exactly as long as this object.
        /// </para>
        /// <para>
        /// Nothing is ever stopped by name.
        /// </para>
        /// </remarks>
        internal sealed class LinuxProcess : IDisposable
        {
            private readonly Process _wsl;
            private readonly StringBuilder _output = new StringBuilder();
            private readonly ManualResetEventSlim _gotPid = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim _exited = new ManualResetEventSlim(false);
            private bool _disposed;

            internal LinuxProcess(string stdin, params string[] commandAndArguments)
            {
                ProcessStartInfo start = new ProcessStartInfo("wsl.exe")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                AddDistro(start);
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add("bash");
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add("echo $$; exec \"$@\"");
                start.ArgumentList.Add("bash");
                for (int i = 0; i < commandAndArguments.Length; i++)
                {
                    start.ArgumentList.Add(commandAndArguments[i]);
                }

                _wsl = new Process { StartInfo = start, EnableRaisingEvents = true };
                _wsl.OutputDataReceived += (s, e) => OnLine(e.Data);
                _wsl.ErrorDataReceived += (s, e) => Append(e.Data);
                _wsl.Exited += (s, e) => _exited.Set();
                _wsl.Start();
                WindowsPid = _wsl.Id;
                _wsl.BeginOutputReadLine();
                _wsl.BeginErrorReadLine();
                if (!_gotPid.Wait(15000))
                {
                    Dispose();
                    throw new InvalidOperationException("WSL did not report the process number of " + commandAndArguments[0] + ": " + Output);
                }

                if (!string.IsNullOrEmpty(stdin))
                {
                    _wsl.StandardInput.Write(stdin);
                    _wsl.StandardInput.Flush();
                }
            }

            /// <summary>The process number of the program inside WSL (the number to stop it by).</summary>
            internal int LinuxPid { get; private set; }

            /// <summary>The process number of the wsl.exe that holds it on the Windows side.</summary>
            internal int WindowsPid { get; }

            internal bool HasExited => _exited.IsSet;

            internal int ExitCode => _wsl.ExitCode;

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

            private void OnLine(string? line)
            {
                if (line == null)
                {
                    return;
                }

                if (!_gotPid.IsSet && int.TryParse(line.Trim(), out int pid))
                {
                    LinuxPid = pid;
                    _gotPid.Set();
                    return;
                }

                Append(line);
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

            /// <summary>Sends text to the program's standard input.</summary>
            internal void Write(string text)
            {
                _wsl.StandardInput.Write(text);
                _wsl.StandardInput.Flush();
            }

            /// <summary>Waits for the program to end on its own.</summary>
            internal bool WaitForExit(int timeoutMs)
            {
                return _exited.Wait(timeoutMs);
            }

            /// <summary>
            /// Stops the program by its Linux process number (TERM), then makes sure the wsl.exe that held it is gone.
            /// </summary>
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (LinuxPid != 0 && !_exited.IsSet)
                {
                    RunWsl("kill -TERM " + LinuxPid + " 2>/dev/null; sleep 0.3; kill -0 " + LinuxPid + " 2>/dev/null && kill -KILL " + LinuxPid, 10000, out _);
                    _exited.Wait(5000);
                }

                try
                {
                    if (!_wsl.HasExited)
                    {
                        _wsl.Kill(true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }

                _wsl.Dispose();
            }
        }

        /// <summary>
        /// Starts a fresh x25emu echo service on Switch C. x25emu's echo takes one call; start one per call (see
        /// demo/crossenv-wsl.sh).
        /// </summary>
        internal static LinuxProcess StartEcho()
        {
            LinuxProcess echo = new LinuxProcess(string.Empty, ToolPath("x25echo"), "-x", "127.0.0.1:" + EchoAccessC);
            // Give it a moment to attach to Switch C before a call is routed to it.
            Thread.Sleep(1500);
            return echo;
        }

        internal static string ToolPath(string program)
        {
            return (Tools ?? string.Empty).TrimEnd('/') + "/" + program;
        }
    }
}
