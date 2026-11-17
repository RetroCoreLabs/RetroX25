//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using NUnit.Framework;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The accounts of the built-in switch in the Status window: a column for each account, the switch's own totals in
    /// it, so the window says what <c>show x25 account</c>, <c>GET_ACCOUNTS</c> and the record file say.
    /// </summary>
    [TestFixture]
    public class StatusAccountsTests
    {
        [Test]
        public void EachAccountIsAColumn_WithHowItIsReached_AndItsTotals()
        {
            SwitchAccount line = new SwitchAccount(new AccountConfig { Name = "lineA", AddrStart = "23420000", AddrEnd = "23420099" })
            {
                CallsActive = 1,
                CallsMade = 3,
                CallsReceived = 2,
                PacketsSent = 20,
                PacketsReceived = 19,
                BytesSent = 2560,
                BytesReceived = 2432,
                SegmentsSent = 40,
                SegmentsReceived = 38,
                TotalDurationMs = 95_000,
            };
            SwitchAccount card = new SwitchAccount(new AccountConfig { Name = "card7", Nui = "CARD7" });

            StatisticsTable table = StatisticsReader.BuildAccounts(new List<SwitchAccount> { line, card });

            Assert.That(table.Headers, Is.EqualTo(new[] { "lineA", "card7" }));
            Assert.That(table.Kinds, Is.EqualTo(new[] { "23420000 to 23420099", "NUI CARD7" }));
            Assert.That(table.Cell("Calls up now", "lineA"), Is.EqualTo("1"));
            Assert.That(table.Cell("Calls made", "lineA"), Is.EqualTo("3"));
            Assert.That(table.Cell("Calls received", "lineA"), Is.EqualTo("2"));
            Assert.That(table.Cell("Packets sent", "lineA"), Is.EqualTo("20"));
            Assert.That(table.Cell("Packets received", "lineA"), Is.EqualTo("19"));
            Assert.That(table.Cell("Bytes sent", "lineA"), Is.EqualTo("2560"));
            Assert.That(table.Cell("Bytes received", "lineA"), Is.EqualTo("2432"));
            Assert.That(table.Cell("Segments sent", "lineA"), Is.EqualTo("40"));
            Assert.That(table.Cell("Segments received", "lineA"), Is.EqualTo("38"));
            Assert.That(table.Cell("Call time", "lineA"), Is.EqualTo("1m 35s"));
            Assert.That(table.Cell("Calls made", "card7"), Is.EqualTo("0"));
        }

        [Test]
        public void NoAccounts_NoColumns()
        {
            StatisticsTable table = StatisticsReader.BuildAccounts(Array.Empty<SwitchAccount>());
            Assert.That(table.Headers, Is.Empty, "the window shows nothing rather than an empty grid");
        }
    }
}
