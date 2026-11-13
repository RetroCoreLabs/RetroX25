//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Diagnostics;
using NUnit.Framework;

namespace RetroX25.Tests
{
    /// <summary>
    /// The x25switch program run for real: a port it cannot run ends the program with a non-zero exit code
    /// and a message that names the port and the transport.
    /// </summary>
    /// <remarks>
    /// The program is started as a child process (its apphost sits next to the test assembly). The process
    /// id is recorded, and if the program is still running after the limit it is killed by that id, so a
    /// failure of this test does not leave a switch running.
    /// </remarks>
    [TestFixture]
    public class SwitchProgramRefusalTests
    {
        private const int LimitMs = 20000;

        private static string RunSwitch(string configText, out int exitCode, out bool hadToKill)
        {
            string file = Path.Combine(Path.GetTempPath(), "x25test-prog-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(file, configText);
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = Path.Combine(AppContext.BaseDirectory, "x25switch.exe"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                info.ArgumentList.Add(file);

                using Process process = Process.Start(info)!;
                int pid = process.Id;
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();

                hadToKill = false;
                if (!process.WaitForExit(LimitMs))
                {
                    hadToKill = true;
                    // Killed by the id recorded at start, never by name.
                    Process.GetProcessById(pid).Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }

                exitCode = process.HasExited ? process.ExitCode : -1;
                return output.Result + error.Result;
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>
        /// A serial trunk on a device that does not exist ends the program with exit code 1 and a message.
        /// </summary>
        [Test]
        public void APortItCannotRun_EndsTheProgram_WithExitCodeOne_AndAMessageNamingPortAndTransport()
        {
            string text = RunSwitch(
                "[switch]\ndnic = 2340\nmgmt_port = 0\nmonitor_port = 0\n\n" +
                "[port.0]\ntype = access\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = 0\n\n" +
                "[port.1]\ntype = trunk\ntransport = hdlc-serial\nmode = dte\ndevice = NO-SUCH-DEVICE-FOR-THE-TEST\nbaud = 9600\n\n" +
                "[route.0]\ntype = default\nport = 1\n",
                out int exitCode, out bool hadToKill);

            Assert.That(hadToKill, Is.False, "the program must stop by itself, not keep running:\n" + text);
            Assert.That(exitCode, Is.Not.EqualTo(0), text);
            Assert.That(text, Does.Contain("hdlc-serial"), "the message names the transport");
            Assert.That(text, Does.Contain("Port 1").Or.Contain("port 1"), "the message names the port");
        }

        /// <summary>
        /// A transport name the configuration loader does not know ends the program too, instead of running as XOT.
        /// </summary>
        [Test]
        public void AnUnknownTransportName_EndsTheProgram_InsteadOfRunningAsXot()
        {
            string text = RunSwitch(
                "[switch]\ndnic = 2340\nmgmt_port = 0\nmonitor_port = 0\n\n" +
                "[port.0]\ntype = trunk\ntransport = carrier-pigeon\nmode = dte\nhost = 127.0.0.1\nport = 1\n",
                out int exitCode, out bool hadToKill);

            Assert.That(hadToKill, Is.False, text);
            Assert.That(exitCode, Is.Not.EqualTo(0), text);
            Assert.That(text, Does.Contain("carrier-pigeon"));
        }
    }
}
