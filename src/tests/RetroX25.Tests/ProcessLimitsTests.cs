//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using NUnit.Framework;
using RetroX25.Host;

namespace RetroX25.Tests
{
    /// <summary>
    /// The job object of <c>--max-memory-mb</c> and <c>--max-cpu-percent</c>: the limits are read back from Windows.
    /// The test process itself is never put into the job (that would cap the test run).
    /// </summary>
    [TestFixture]
    [Platform("Win")]
    public sealed class ProcessLimitsTests
    {
        [Test]
        public void TheJob_HoldsTheMemoryAndCpuCaps_AsWindowsReadsThemBack()
        {
            using ProcessLimits l = new ProcessLimits(256, 25);
            (ulong mem, uint rate) = l.Query();
            Assert.That(mem, Is.EqualTo(256UL * 1024 * 1024));
            Assert.That(rate, Is.EqualTo(2500u), "1/100 of a percent");
        }

        [Test]
        public void ACapOfZero_IsNoCap()
        {
            using ProcessLimits l = new ProcessLimits(0, 0);
            Assert.That(l.Query(), Is.EqualTo((0UL, 0u)));
        }

        [Test]
        public void ACpuCapAbove100_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessLimits(0, 101));
        }
    }
}
