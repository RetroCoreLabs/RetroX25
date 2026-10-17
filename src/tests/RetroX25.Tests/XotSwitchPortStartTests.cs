//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Switch;
using RetroX25.Switch;

namespace RetroX25.Tests
{
    /// <summary>
    /// Checks that a switch port which cannot open reports it, rather than looking healthy.
    /// </summary>
    /// <remarks>
    /// Found running the demo on a machine where Windows had reserved TCP 2180-2279: every listening
    /// port failed to bind, the log still said "listening", and the switch ran on with no X.25 ports
    /// at all. The program now exits on a <see langword="false"/> from Start, so that result has to
    /// be right.
    /// </remarks>
    [TestFixture]
    public class XotSwitchPortStartTests
    {
        /// <summary>
        /// A listening port whose TCP port is already taken returns false from Start and stays inactive.
        /// </summary>
        [Test]
        public void ListeningPort_WhenTheTcpPortIsTaken_DoesNotStart()
        {
            // Hold a port so the switch port cannot bind it.
            TcpListener holder = new TcpListener(IPAddress.Loopback, 0);
            holder.Start();
            int taken = ((IPEndPoint)holder.LocalEndpoint).Port;

            XotSwitchPort port = new XotSwitchPort(
                new PortConfig { Id = 0, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = (ushort)taken, LciBase = 1 },
                NullLogger.Instance);
            try
            {
                Assert.That(port.Start(), Is.False, "Start must report a port it could not bind");
                Assert.That(port.Active, Is.False, "a port that never bound is not active");
            }
            finally
            {
                port.Stop();
                holder.Stop();
            }
        }

        /// <summary>
        /// The same listening port on a free TCP port starts and is active, so the test above fails
        /// for the reason it names and not because a listening port can never start.
        /// </summary>
        [Test]
        public void ListeningPort_OnAFreeTcpPort_Starts()
        {
            XotSwitchPort port = new XotSwitchPort(
                new PortConfig { Id = 0, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            try
            {
                Assert.That(port.Start(), Is.True);
                Assert.That(port.Active, Is.True);
                Assert.That(port.ListenPort, Is.GreaterThan(0), "the operating system should have picked a port");
            }
            finally
            {
                port.Stop();
            }
        }
    }
}
