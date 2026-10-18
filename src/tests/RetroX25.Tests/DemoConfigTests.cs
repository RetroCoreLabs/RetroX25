//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using NUnit.Framework;
using RetroIO.Protocols.X25.Switch;

namespace RetroX25.Tests
{
    /// <summary>
    /// Checks the demo network's configuration against the tools the documentation runs against it.
    /// </summary>
    /// <remarks>
    /// A port carries as many calls at once as it has logical channels. <c>x25traffic</c> in
    /// call-storm mode holds up to 2048 calls open together (<c>NumVcs</c> in its Program.cs), so a
    /// demo access port with the default of 16 channels can only carry 16 of them and the storm
    /// measures refusals instead of the switch.
    /// </remarks>
    [TestFixture]
    public class DemoConfigTests
    {
        /// <summary>
        /// The most calls <c>x25traffic</c> holds open together in call-storm mode.
        /// </summary>
        private const int TrafficToolChannels = 2048;

        private static string FindDemoFile(string name)
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "demo", name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("the demo file " + name + " was not found above " + AppContext.BaseDirectory);
        }

        /// <summary>
        /// The demo's access port has at least as many channels as the load tool's storm has calls.
        /// </summary>
        [Test]
        public void DemoAccessPort_HasAChannelForEachCallOfTheLoadToolsStorm()
        {
            SwitchConfig config = SwitchConfigLoader.LoadFile(FindDemoFile("switch-a.ini"));

            PortConfig? access = null;
            for (int i = 0; i < config.Ports.Count; i++)
            {
                if (config.Ports[i].Id == 0)
                {
                    access = config.Ports[i];
                }
            }

            Assert.That(access, Is.Not.Null, "port 0 is the access port the documentation points the tools at");
            Assert.That(access!.Type, Is.EqualTo(PortType.Access));
            Assert.That(access.LciBase + access.LciCount - 1, Is.GreaterThanOrEqualTo(TrafficToolChannels),
                "x25traffic holds up to " + TrafficToolChannels + " calls open together; a port with fewer channels refuses the rest");
        }
    }
}
