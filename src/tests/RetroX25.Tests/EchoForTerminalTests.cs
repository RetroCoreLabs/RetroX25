//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Text;
using NUnit.Framework;
using RetroX25.Echo;

namespace RetroX25.Tests
{
    /// <summary>
    /// How the echo service sends typed text back to a person at a terminal (the mode without -q).
    /// </summary>
    /// <remarks>
    /// A line-mode PAD, such as a Cisco router's, ends a typed line with a bare CR. Echoed unchanged,
    /// the cursor goes back to the start of the same row and the next echo draws over the line
    /// ("exito cisco" on a router). Every CR goes back as CR LF, and an LF that belongs to the same
    /// line end is not echoed twice.
    /// </remarks>
    [TestFixture]
    public class EchoForTerminalTests
    {
        private static string Echo(string typed, ref bool previousWasCr)
        {
            byte[] data = Encoding.ASCII.GetBytes(typed);
            byte[] echo = Program.EchoForTerminal(data, data.Length, ref previousWasCr);
            return Encoding.ASCII.GetString(echo);
        }

        [Test]
        public void ABareCr_IsEchoedAsCrLf()
        {
            bool afterCr = false;
            Assert.That(Echo("hello\r", ref afterCr), Is.EqualTo("hello\r\n"));
        }

        [Test]
        public void ACrLfPair_IsEchoedOnce()
        {
            bool afterCr = false;
            Assert.That(Echo("hello\r\n", ref afterCr), Is.EqualTo("hello\r\n"));
        }

        [Test]
        public void AnLfThatStartsTheNextPacket_AfterACr_IsNotEchoedAgain()
        {
            bool afterCr = false;
            Assert.That(Echo("hello\r", ref afterCr), Is.EqualTo("hello\r\n"));
            Assert.That(afterCr, Is.True);
            Assert.That(Echo("\nnext", ref afterCr), Is.EqualTo("next"));
            Assert.That(afterCr, Is.False);
        }

        [Test]
        public void ALoneLf_IsEchoedUnchanged()
        {
            bool afterCr = false;
            Assert.That(Echo("a\nb", ref afterCr), Is.EqualTo("a\nb"));
        }

        [Test]
        public void SeveralLines_InOnePacket_EachEndWithCrLf()
        {
            bool afterCr = false;
            Assert.That(Echo("one\rtwo\r", ref afterCr), Is.EqualTo("one\r\ntwo\r\n"));
        }
    }
}
