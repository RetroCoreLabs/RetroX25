//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

// The number plan of the three-country demo network (demo/x75-countries), read from the real ini files: which port
// each kind of number leaves on in each country.

using NUnit.Framework;
using RetroIO.Protocols.X25.Common;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;

namespace RetroX25.Tests
{
    /// <summary>
    /// Every country's routes, from the files x75.ps1 starts the switches with. Numbers are given as they arrive at
    /// that switch: dialled by a PAD user in Norway, the full international number on the X.75 links.
    /// </summary>
    [TestFixture]
    public class X75CountriesRoutesTests
    {
        private static X121RoutingTable Load(string country)
        {
            DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                string file = Path.Combine(dir.FullName, "demo", "x75-countries", country + ".ini");
                if (File.Exists(file))
                {
                    return SwitchConfigLoader.Load(IniConfig.Parse(File.ReadAllLines(file))).Routing;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("demo/x75-countries/" + country + ".ini not found above the test folder");
        }

        // Norway: 0 PAD users, 1 echo, 2 X.75 to England, 3 host line. Norway's users dial as a PAD user would: a
        // national number without the DNIC, and 0 + the full number abroad (international_prefix = 0).
        [TestCase("9001", 1, TestName = "Norway: its echo service, 9001")]
        // The hosts have real numbers from the X.25 Network Atlas, each routed exactly to its country's host line.
        [TestCase("211000074", 3, TestName = "Norway: OSLOCLOCK, 211000074, goes to the host line")]
        [TestCase("4501340", 3, TestName = "Norway: NORDBBS, 4501340, goes to the host line")]
        [TestCase("2110000020", 3, TestName = "Norway: NDOSLO, 2110000020, goes to the host line")]
        [TestCase("0234223519191", 2, TestName = "Norway: a host in England, 0234223519191, goes to England")]
        [TestCase("026244422190371", 2, TestName = "Norway: a host in Germany, 026244422190371, goes via England")]
        [TestCase("026245890040004", 2, TestName = "Norway: the Altos chat goes via England")]
        // England: 0 X.75 from Norway, 1 X.75 to Germany, 2 echo, 3 host line.
        [TestCase("23429001", 2, TestName = "England: its echo service", Category = "england")]
        [TestCase("234223519191", 3, TestName = "England: LONDONGAMES goes to the host line", Category = "england")]
        [TestCase("2422211000074", 0, TestName = "England: a host in Norway goes to Norway", Category = "england")]
        [TestCase("26244422190371", 1, TestName = "England: a host in Germany goes to Germany", Category = "england")]
        // Germany: 0 X.75 from England, 1 Altos line, 2 echo, 3 host line.
        [TestCase("26249001", 2, TestName = "Germany: its echo service", Category = "germany")]
        [TestCase("26245890040004", 1, TestName = "Germany: the Altos chat", Category = "germany")]
        [TestCase("26244422190371", 3, TestName = "Germany: MUNICHBOARD goes to the host line, not to the default", Category = "germany")]
        // ucbvax's real number lies inside the Altos block 2624589004...; its exact route wins over the block.
        [TestCase("26245890040262", 3, TestName = "Germany: ucbvax, inside the Altos block, goes to the host line", Category = "germany")]
        [TestCase("2422211000074", 0, TestName = "Germany: a host in Norway goes back to England", Category = "germany")]
        public void EachNumberLeavesOnItsPort(string called, int port)
        {
            // The country is the start of the test name ("Norway: ...").
            string country = TestContext.CurrentContext.Test.Name.Split(':')[0].ToLowerInvariant();

            X121Route? route = Load(country).FindWinner(called, inPort: -1);

            Assert.That(route, Is.Not.Null, country + " has no route for " + called);
            Assert.That(route!.DestPort, Is.EqualTo(port), country + ": " + called);
        }

        /// <summary>
        /// A number in no block of its own country is refused there, not sent to a host line or a neighbour. 1001 was
        /// a host number before the hosts got real ones; nothing answers it now.
        /// </summary>
        [TestCase("norway", "5555")]
        [TestCase("norway", "1001")]
        [TestCase("england", "23421001")]
        [TestCase("england", "23425555")]
        public void AnUnknownNumberInTheCountry_HasNoRoute(string country, string called)
        {
            Assert.That(Load(country).FindWinner(called, inPort: -1), Is.Null);
        }
    }
}
