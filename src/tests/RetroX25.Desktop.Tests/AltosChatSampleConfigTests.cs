//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The Altos chat sample (docs/config-samples/x25config-altos-chat.json, documented in docs/altos-chat.md) is read
    /// by the app's own loader with every chat key in place: a misspelt key would be dropped silently and the chat
    /// would not start, or would start with the defaults.
    /// </summary>
    [TestFixture]
    public sealed class AltosChatSampleConfigTests
    {
        [Test]
        public void TheAltosChatSample_LoadsWithTheChatItsLineAndItsRoute()
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-altos-chat.json"));

            Assert.That(config.Chat.Address, Is.EqualTo("26245890040004"));
            Assert.That(config.Chat.MaxUsers, Is.EqualTo(30));
            Assert.That(config.Chat.OutputLimitBytes, Is.EqualTo(4096));
            Assert.That(config.Chat.WaitPauseMs, Is.EqualTo(3000));
            Assert.That(config.Chat.StartingNickname, Is.EqualTo("guest"));
            Assert.That(config.Chat.Address, Is.Not.EqualTo(config.X25.LocalMachineId), "the chat needs an address of its own");

            Assert.That(config.Routing.XotInterfaces.Count, Is.EqualTo(1));
            XotInterfaceConfig line = config.Routing.XotInterfaces[0];
            Assert.That(line.Mode, Is.EqualTo(XotMode.Client));
            Assert.That(line.KeepConnectionForIncomingCalls, Is.True);

            Assert.That(config.Routing.RoutingTable.Count, Is.EqualTo(1));
            Assert.That(config.Routing.RoutingTable[0].X121AddressPattern, Is.EqualTo("26245890040004"));
            Assert.That(config.Routing.RoutingTable[0].Type, Is.EqualTo(RouteType.Local));
        }

        private static string FindSampleFile(string fileName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "docs", "config-samples", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Sample configuration not found above " + AppContext.BaseDirectory, fileName);
        }
    }
}
