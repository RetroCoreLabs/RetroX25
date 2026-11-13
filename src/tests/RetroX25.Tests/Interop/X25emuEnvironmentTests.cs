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
    /// The checker: says in plain words what is missing before any cross-environment test runs (WSL not running, x25emu
    /// not built, a port that nobody answers, WSL unable to reach this machine). Skipped unless X25EMU_WSL_HOST is set.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    public class X25emuEnvironmentTests
    {
        [Test]
        public void TheX25emuSetup_IsReady_InBothDirections()
        {
            // RequireReady fails with the list of what is missing, one plain sentence each.
            X25emuEnvironment.RequireReady();
            TestContext.Progress.WriteLine("x25emu network at " + X25emuEnvironment.WslHost + "; WSL reaches this machine at " + X25emuEnvironment.WindowsHost);
        }
    }
}
