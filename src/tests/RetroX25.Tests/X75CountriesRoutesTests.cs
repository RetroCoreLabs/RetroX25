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
        [TestCase("1001", 3, TestName = "Norway: a host 1xxx goes to the host line")]
        [TestCase("1999", 3, TestName = "Norway: the last host number, 1999")]
        [TestCase("023421001", 2, TestName = "Norway: a host in England, 023421001, goes to England")]
        [TestCase("026241001", 2, TestName = "Norway: a host in Germany, 026241001, goes via England")]
        [TestCase("026245890040004", 2, TestName = "Norway: the Altos chat goes via England")]
        // England: 0 X.75 from Norway, 1 X.75 to Germany, 2 echo, 3 host line.
        [TestCase("23429001", 2, TestName = "England: its echo service", Category = "england")]
        [TestCase("23421001", 3, TestName = "England: a host 23421xxx goes to the host line", Category = "england")]
        [TestCase("24221001", 0, TestName = "England: a host in Norway goes to Norway", Category = "england")]
        [TestCase("26241001", 1, TestName = "England: a host in Germany goes to Germany", Category = "england")]
        // Germany: 0 X.75 from England, 1 Altos line, 2 echo, 3 host line.
        [TestCase("26249001", 2, TestName = "Germany: its echo service", Category = "germany")]
        [TestCase("26245890040004", 1, TestName = "Germany: the Altos chat", Category = "germany")]
        [TestCase("26241001", 3, TestName = "Germany: a host 26241xxx goes to the host line, not to the default", Category = "germany")]
        [TestCase("24221001", 0, TestName = "Germany: a host in Norway goes back to England", Category = "germany")]
        public void EachNumberLeavesOnItsPort(string called, int port)
        {
            // The country is the start of the test name ("Norway: ...").
            string country = TestContext.CurrentContext.Test.Name.Split(':')[0].ToLowerInvariant();

            X121Route? route = Load(country).FindWinner(called, inPort: -1);

            Assert.That(route, Is.Not.Null, country + " has no route for " + called);
            Assert.That(route!.DestPort, Is.EqualTo(port), country + ": " + called);
        }

        /// <summary>
        /// A number in no block of its own country is refused there, not sent to a host line or a neighbour.
        /// </summary>
        [TestCase("norway", "5555")]
        [TestCase("england", "23425555")]
        public void AnUnknownNumberInTheCountry_HasNoRoute(string country, string called)
        {
            Assert.That(Load(country).FindWinner(called, inPort: -1), Is.Null);
        }
    }
}
