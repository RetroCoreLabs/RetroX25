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
    /// x25traffic is a data generator. The options that bent it into other things (call user data, Q, M and D bits) are gone;
    /// a program that is given one prints its usage and does not try to connect. (The call user data of a PAD call is
    /// sent by the PAD itself, and the bits are tested through the client library in <see cref="QmdBitsTests"/>.)
    /// </summary>
    [TestFixture]
    public class TrafficOptionsTests
    {
        [TestCase("-u", "01000000")]
        [TestCase("--qbit", "")]
        [TestCase("--dbit", "")]
        [TestCase("--mbit-chain", "4")]
        public void RemovedOption_IsRefused_WithTheUsage(string option, string value)
        {
            string exe = Path.Combine(AppContext.BaseDirectory, "x25traffic.exe");
            Assert.That(File.Exists(exe), Is.True, exe);
            ProcessStartInfo start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            string[] args = { "-x", "127.0.0.1:1", "-d", "23410001", option, value };
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Length > 0)
                {
                    start.ArgumentList.Add(args[i]);
                }
            }

            using Process process = Process.Start(start)!;
            int pid = process.Id;
            try
            {
                Task<string> error = process.StandardError.ReadToEndAsync();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Assert.That(process.WaitForExit(15000), Is.True, "x25traffic (process " + pid + ") did not end");
                string text = error.Result + output.Result;
                Assert.That(process.ExitCode, Is.EqualTo(1));
                Assert.That(text, Does.Contain("Usage"), text);
                Assert.That(text, Does.Not.Contain("Calling"), "it must refuse the option before it calls: " + text);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
        }
    }
}
