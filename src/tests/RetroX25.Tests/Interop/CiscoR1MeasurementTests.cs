//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace RetroX25.Tests
{
    /// <summary>
    /// Measurements against the real Cisco router R1 (address 23600001), bytes read in the order they arrive. The router is
    /// reached over XOT on TCP port 1998. These need the real router, so they run only when named.
    /// </summary>
    /// <remarks>
    /// <para>Environment variables: <c>CISCO_R1_HOST</c> (the router's LAN address; the tests are skipped without it).</para>
    /// <para>Nothing is changed on the router. Each test clears every call it makes.</para>
    /// </remarks>
    [TestFixture]
    [Category("CiscoLive")]
    [Explicit("needs the real Cisco router R1; set CISCO_R1_HOST")]
    public class CiscoR1MeasurementTests
    {
        private const byte CallAccepted = 0x0F;
        private const byte ClearRequest = 0x13;
        private static readonly byte[] PadMarker = { 0x01, 0x00, 0x00, 0x00 };

        private static string RouterHost
        {
            get
            {
                string? host = Environment.GetEnvironmentVariable("CISCO_R1_HOST");
                if (string.IsNullOrEmpty(host))
                {
                    Assert.Ignore("CISCO_R1_HOST is not set");
                }

                return host!;
            }
        }

        /// <summary>
        /// A call to the router's own PAD with and without the X.29 marker in the call user data. Without it the router clears the call; with it
        /// the call is accepted and the router's login prompt arrives as data.
        /// </summary>
        [Test]
        public void CallToTheRouterPad_NeedsTheX29Marker_AndTheBytesInOrderAreDecoded()
        {
            using (RawXotCaller without = new RawXotCaller(RouterHost, 1998, 1))
            {
                without.SendCall(1, "23600001", "23400077", null);
                byte[]? answer = without.WaitFor(p => p.Length >= 3 && (p[2] == CallAccepted || p[2] == ClearRequest), 8000);
                TestContext.Progress.WriteLine("WITHOUT call user data:\n" + without.Dump());
                Assert.That(answer, Is.Not.Null, "no answer at all");
                Assert.That(answer![2], Is.EqualTo(ClearRequest), "the router did not clear a call without the X.29 marker\n" + without.Dump());
                Assert.That(answer.Length, Is.GreaterThanOrEqualTo(5));
                TestContext.Progress.WriteLine("  clear cause " + answer[3].ToString("X2") + " diagnostic " + answer[4].ToString("X2"));
                Assert.That(answer[3], Is.EqualTo((byte)0x0D), "cause 13 = not obtainable");
                Assert.That(answer[4], Is.EqualTo((byte)0x40), "diagnostic 64");
            }

            using (RawXotCaller with = new RawXotCaller(RouterHost, 1998, 1))
            {
                with.SendCall(1, "23600001", "23400077", PadMarker);
                byte[]? accepted = with.WaitForType(1, CallAccepted, 8000);
                Assert.That(accepted, Is.Not.Null, "the router did not accept the call with the X.29 marker\n" + with.Dump());
                Thread.Sleep(2500);
                with.Clear(0, 0);
                with.WaitFor(p => p.Length >= 3 && p[2] == 0x17, 5000);
                TestContext.Progress.WriteLine("WITH call user data 01000000:\n" + with.Dump());
            }
        }

        /// <summary>
        /// Two calls on ONE TCP connection (channels 1 and 2). RFC 1613 section 5 asks for one connection per call; what the router does
        /// with the second Call Request is measured here.
        /// </summary>
        [Test]
        public void TwoCallsOnOneConnection_AreMeasured()
        {
            using RawXotCaller caller = new RawXotCaller(RouterHost, 1998, 1);
            caller.SendCall(1, "23600001", "23400077", PadMarker);
            Assert.That(caller.WaitForType(1, CallAccepted, 8000), Is.Not.Null, "the first call was not accepted\n" + caller.Dump());
            caller.SendCall(2, "23600001", "23400077", PadMarker);
            byte[]? second = caller.WaitFor(p => p.Length >= 3 && (((p[0] & 0x0F) << 8) | p[1]) == 2 && (p[2] == CallAccepted || p[2] == ClearRequest), 8000);
            Thread.Sleep(1000);
            TestContext.Progress.WriteLine("TWO CALLS on one connection:\n" + caller.Dump());
            string result = second == null ? "no answer to the second Call Request within 8 s"
                : second[2] == CallAccepted ? "the second call was ACCEPTED on the same connection"
                : "the second call was CLEARED, cause " + second[3].ToString("X2") + " diagnostic " + second[4].ToString("X2");
            TestContext.Progress.WriteLine("RESULT: " + result);
            // Measured 2026-11-13 on R1: the router never answers the second Call Request; it clears the FIRST call
            // (channel 1) with cause 0x13 diagnostic 0x17 and says nothing about channel 2. So the router does not
            // carry two calls on one XOT connection - one connection per call, as RFC 1613 section 5 says.
            Assert.That(second, Is.Null, "the router answered the second call: " + result);
            byte[]? firstCleared = caller.WaitForType(1, ClearRequest, 1000);
            Assert.That(firstCleared, Is.Not.Null, "the router did not clear the first call\n" + caller.Dump());
            Assert.That(firstCleared![3], Is.EqualTo((byte)0x13));
            Assert.That(firstCleared[4], Is.EqualTo((byte)0x17));
        }
    }
}
