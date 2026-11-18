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
    /// demo access port with the old default of 16 channels (the default is every channel up to 4095 since 2026-11-18) can only carry 16 of them and the storm
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

        private static PortConfig Port(string file, int id)
        {
            SwitchConfig config = SwitchConfigLoader.LoadFile(FindDemoFile(file));
            for (int i = 0; i < config.Ports.Count; i++)
            {
                if (config.Ports[i].Id == id)
                {
                    return config.Ports[i];
                }
            }

            throw new InvalidOperationException("no port " + id + " in " + file);
        }

        /// <summary>
        /// The three demo switches are chained by HDLC leased lines: the end that dials is the DTE, the end that
        /// listens is the DCE, and both name the same port. Every channel the storm needs is available on them.
        /// </summary>
        [Test]
        public void DemoSwitches_AreChainedByHdlcTrunks_ThatMeetOnTheSamePort()
        {
            PortConfig aToB = Port("switch-a.ini", 1);
            PortConfig bFromA = Port("switch-b.ini", 0);
            PortConfig bToC = Port("switch-b.ini", 1);
            PortConfig cFromB = Port("switch-c.ini", 0);

            Assert.That(aToB.Transport, Is.EqualTo(PortTransport.HdlcTcp));
            Assert.That(bFromA.Transport, Is.EqualTo(PortTransport.HdlcTcp));
            Assert.That(bToC.Transport, Is.EqualTo(PortTransport.HdlcTcp));
            Assert.That(cFromB.Transport, Is.EqualTo(PortTransport.HdlcTcp));

            Assert.That(aToB.Mode, Is.EqualTo(PortMode.Dte));
            Assert.That(bFromA.Mode, Is.EqualTo(PortMode.Dce));
            Assert.That(aToB.Port, Is.EqualTo(bFromA.Port), "A dials the port B listens on");

            Assert.That(bToC.Mode, Is.EqualTo(PortMode.Dte));
            Assert.That(cFromB.Mode, Is.EqualTo(PortMode.Dce));
            Assert.That(bToC.Port, Is.EqualTo(cFromB.Port), "B dials the port C listens on");

            PortConfig[] trunks = { aToB, bFromA, bToC, cFromB };
            for (int i = 0; i < trunks.Length; i++)
            {
                Assert.That(trunks[i].LciBase + trunks[i].LciCount - 1, Is.GreaterThanOrEqualTo(TrafficToolChannels),
                    "trunk port " + trunks[i].Id + " must carry as many calls as the load tool's storm holds");
            }

            Assert.That(Port("switch-c.ini", 1).LciCount, Is.GreaterThanOrEqualTo(TrafficToolChannels),
                "the echo's access port must hold the storm too, or the storm measures refusals");
        }

        /// <summary>
        /// The reference-network trunk and the Cisco and Desktop samples stay XOT: their peers speak XOT.
        /// </summary>
        [Test]
        public void TheReferenceTrunkAndTheCiscoAndDesktopSamples_StayXot()
        {
            Assert.That(Port("switch-a.ini", 2).Transport, Is.EqualTo(PortTransport.Xot));
            SwitchConfig cisco = SwitchConfigLoader.LoadFile(FindDemoFile("switch-cisco.ini"));
            SwitchConfig desktop = SwitchConfigLoader.LoadFile(FindDemoFile("switch-desktop.ini"));
            for (int i = 0; i < cisco.Ports.Count; i++)
            {
                Assert.That(cisco.Ports[i].Transport, Is.EqualTo(PortTransport.Xot));
            }

            for (int i = 0; i < desktop.Ports.Count; i++)
            {
                Assert.That(desktop.Ports[i].Transport, Is.EqualTo(PortTransport.Xot));
            }
        }
    }
}
