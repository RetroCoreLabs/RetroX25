//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using NUnit.Framework;

namespace RetroX25.Tests
{
    /// <summary>
    /// End-to-end interop across the real socket-based switch: two <see cref="RetroIO.Protocols.X25.Client.X25Client"/>
    /// instances connect to a switch over XOT/TCP, one places a call that the switch routes and stitches to
    /// the other, and data is echoed back. Exercises the actual XOT transport, framing, and packet handling
    /// against the clean client stack.
    /// </summary>
    [TestFixture]
    public class SwitchXotInteropTests
    {
        /// <summary>
        /// A caller connected on one switch port reaches an echo endpoint connected on another, with the
        /// switch routing by X.121 address, and the echoed data returns to the caller.
        /// </summary>
        [Test]
        public void ClientCallsThroughSwitch_ToEchoClient_EchoRoundTrips()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            SwitchXotHarness.RunCallerEchoScenario(sw, "PING");
        }
    }
}
