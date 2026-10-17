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
    /// A port only accepts calls on the logical channels it declares, as the C emulator's switch does.
    /// A caller starts from the highest channel it has, and <c>x25traffic</c> declares 2048 of them
    /// (<c>NumVcs</c> in its Program.cs). With the default of 16 channels the demo's access port refused
    /// the tool's first call, on channel 2048, with a diagnostic and the call failed. The old port
    /// copy accepted any channel, so nothing had ever said the demo was too narrow.
    /// </remarks>
    [TestFixture]
    public class DemoConfigTests
    {
        /// <summary>
        /// The channels <c>x25traffic</c> declares; its first call arrives on the highest of them.
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
        /// The demo's access port declares at least the channels the load tool uses.
        /// </summary>
        [Test]
        public void DemoAccessPort_DeclaresTheChannelsTheLoadToolUses()
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
                "x25traffic's first call is on channel " + TrafficToolChannels + "; a port that does not own it refuses the call");
        }
    }
}
