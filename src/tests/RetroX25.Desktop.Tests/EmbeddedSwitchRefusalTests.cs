//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// A built-in switch whose configuration asks for a port it cannot run must stop and say so, naming the
    /// port and the transport. It must not run that port as a different kind of port.
    /// </summary>
    /// <remarks>
    /// Until 2026-11-12 the built-in switch made an XOT port for every configured port, so a port written as
    /// a serial trunk came up as an XOT port, "worked", and failed against a real peer with no word in the log.
    /// The serial device here does not exist, so the port can never open, whatever the build supports.
    /// </remarks>
    [TestFixture]
    public sealed class EmbeddedSwitchRefusalTests
    {
        private string _file = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _file = Path.Combine(Path.GetTempPath(), "x25test-refusal-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(_file,
                "[switch]\ndnic = 2340\nmgmt_port = 0\nmonitor_port = 0\n\n" +
                "[port.0]\ntype = access\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = 0\n\n" +
                "[port.1]\ntype = trunk\ntransport = hdlc-serial\nmode = dte\ndevice = NO-SUCH-DEVICE-FOR-THE-TEST\nbaud = 9600\n\n" +
                "[route.0]\ntype = default\nport = 1\n");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_file))
            {
                File.Delete(_file);
            }
        }

        /// <summary>
        /// The switch does not run, and the failure names port 1 and the transport.
        /// </summary>
        [Test]
        public void APortItCannotRun_StopsTheBuiltInSwitch_AndNamesThePortAndTheTransport()
        {
            using EmbeddedSwitchService service = new EmbeddedSwitchService(
                new EmbeddedSwitchOptions { Enabled = true, ConfigFile = _file },
                NullLogger<EmbeddedSwitchService>.Instance);

            bool started = service.StartIfEnabled();

            Assert.That(started, Is.False, "the switch must not start with a port it cannot run");
            Assert.That(service.IsRunning, Is.False);
            Assert.That(service.Failure, Does.Contain("1"), "the failure names the port");
            Assert.That(service.Failure, Does.Contain("hdlc-serial"), "the failure names the transport");
            Assert.That(service.Describe(), Does.Contain("NOT running"));
        }
    }
}
