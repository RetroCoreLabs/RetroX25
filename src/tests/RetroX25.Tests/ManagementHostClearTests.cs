//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Text;
using NUnit.Framework;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Management;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;
using RetroX25.Switch;

namespace RetroX25.Tests
{
    /// <summary>
    /// A management console session must end when the caller clears its call.
    /// </summary>
    /// <remarks>
    /// Found 2026-11-17 with SINTRAN's X.29 PAD calling the switch: SINTRAN cleared a console call on channel 1031,
    /// the session stayed in the console's table, and the next call SINTRAN made on channel 1031 (to another host)
    /// was joined by the old console. It answered "Password:" and "Login incorrect." into the new call, with its own
    /// packet numbers. A console call has no stitch, so the switch's general clear handling never told the console;
    /// only a QUIT typed in the console removed a session.
    /// </remarks>
    [TestFixture]
    public class ManagementHostClearTests
    {
        private static (ManagementHost Host, LoopbackSwitchPort Port) Build()
        {
            SwitchConfig cfg = new SwitchConfig { Dnic = "2342", Name = "T" };
            cfg.Users.Add(new UserConfig { Name = "admin", Password = "admin" });
            SwitchNode node = new SwitchNode(cfg);
            ManagementHost host = new ManagementHost(node, new SwitchCli(node), new SwitchApiProcessor(node));
            LoopbackSwitchPort port = new LoopbackSwitchPort(new PortConfig { Id = 0 });

            // Wired the same way as Program.cs.
            port.DataReceived += (p, lci, data, length, q, m, d) => host.OnPortData(p, lci, data, length, q, m, d);
            port.CallCleared += host.OnPortCallCleared;
            return (host, port);
        }

        [Test]
        public void CallerClears_TheSessionEnds_AndLaterDataOnTheSameChannelIsNotAnswered()
        {
            (ManagementHost host, LoopbackSwitchPort port) = Build();
            const ushort Lci = 1031;

            host.OnManagementCall(port, Lci, "23420000", "234200000006", System.Array.Empty<byte>());
            Assert.That(port.SentData.Count, Is.GreaterThan(0), "the console sent no banner");

            port.InjectClear(Lci);
            int sentBefore = port.SentData.Count;

            // A new call on the same channel, carrying data for some other host.
            port.InjectData(Lci, Encoding.ASCII.GetBytes("HELP\r"));

            Assert.That(port.SentData.Count, Is.EqualTo(sentBefore),
                "the cleared console session answered data on a later call on the same channel");
            Assert.That(host.OnPortData(port, Lci, Encoding.ASCII.GetBytes("x\r"), 2, false, false, false), Is.False,
                "the cleared console session still claims the channel");
        }

        [Test]
        public void CallStillUp_TheSessionAnswers()
        {
            (ManagementHost host, LoopbackSwitchPort port) = Build();
            const ushort Lci = 5;

            host.OnManagementCall(port, Lci, "23420000", "234200000006", System.Array.Empty<byte>());
            int sentBefore = port.SentData.Count;
            port.InjectData(Lci, Encoding.ASCII.GetBytes("admin\r"));

            Assert.That(port.SentData.Count, Is.GreaterThan(sentBefore), "a live console session did not answer");
        }
    }
}
