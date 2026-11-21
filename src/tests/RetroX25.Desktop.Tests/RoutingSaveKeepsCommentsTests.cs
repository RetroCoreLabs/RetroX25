//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using NUnit.Framework;

using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Runtime;

using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The routing screen's Save writes the routing values into the configuration file's text and keeps every comment
    /// and every other section (added 2026-11-21, RetroIO BUGS.md "Save drops comments"; before, the first Save lost
    /// every comment in the file).
    /// </summary>
    [TestFixture]
    public sealed class RoutingSaveKeepsCommentsTests
    {
        private const string File1 =
            "// top comment\n" +
            "{\n" +
            "  // the switch section is not the routing screen's\n" +
            "  \"EmbeddedSwitch\": { \"Enabled\": false /* off for now */ },\n" +
            "  \"routing\": {\n" +
            "    // our own network\n" +
            "    \"OwnDnics\": [ \"2422\" ],\n" +
            "    \"XotInterfaces\": [\n" +
            "      { \"Name\": \"far\", \"LciCount\": 8 }, // the far line\n" +
            "      { \"Name\": \"near\" }\n" +
            "    ],\n" +
            "    \"Routes\": []\n" +
            "  }\n" +
            "}\n";

        private string _path = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "retrox25-routing-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(_path, File1);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static X25RoutingConfig Routing()
        {
            X25RoutingConfig r = new X25RoutingConfig();
            r.OwnDnics = new List<string> { "2422", "2423" };
            r.InternationalPrefix = "0";
            r.Routes = new List<X121RouteConfig> { new X121RouteConfig { Name = "out", Kind = "default", Port = "far" } };
            r.XotInterfaces.Add(new XotInterfaceConfig { Name = "far" });
            r.XotInterfaces.Add(new XotInterfaceConfig { Name = "near", LciCount = 16 });
            return r;
        }

        [Test]
        public void Save_KeepsEveryComment_AndTheOtherSections()
        {
            RoutingViewModel.WriteRoutingToFile(_path, Routing());
            string text = File.ReadAllText(_path);

            Assert.That(text, Does.StartWith("// top comment\n{\n  // the switch section is not the routing screen's\n"));
            Assert.That(text, Does.Contain("\"EmbeddedSwitch\": { \"Enabled\": false /* off for now */ },"));
            Assert.That(text, Does.Contain("    // our own network\n"));
            Assert.That(text, Does.Contain("// the far line"));
        }

        [Test]
        public void Save_WritesTheValues_AndTheFileStillReadsBack()
        {
            RoutingViewModel.WriteRoutingToFile(_path, Routing());
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            JsonElement r = doc.RootElement.GetProperty("routing");

            Assert.That(r.GetProperty("OwnDnics").GetArrayLength(), Is.EqualTo(2));
            Assert.That(r.GetProperty("InternationalPrefix").GetString(), Is.EqualTo("0"));
            Assert.That(r.GetProperty("Routes")[0].GetProperty("Name").GetString(), Is.EqualTo("out"));
            Assert.That(r.GetProperty("XotInterfaces")[0].TryGetProperty("LciCount", out _), Is.False, "a count not set is taken out");
            Assert.That(r.GetProperty("XotInterfaces")[1].GetProperty("LciCount").GetInt32(), Is.EqualTo(16));
            Assert.That(r.GetProperty("DefaultGateway").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(doc.RootElement.GetProperty("EmbeddedSwitch").GetProperty("Enabled").GetBoolean(), Is.False);
        }

        [Test]
        public void AFileWithoutARoutingSection_GetsOne()
        {
            File.WriteAllText(_path, "{\n  // nothing yet\n  \"Logging\": {}\n}\n");
            RoutingViewModel.WriteRoutingToFile(_path, Routing());
            string text = File.ReadAllText(_path);

            Assert.That(text, Does.Contain("// nothing yet"));
            using JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            Assert.That(doc.RootElement.GetProperty("Routing").GetProperty("Routes").GetArrayLength(), Is.EqualTo(1));
        }

        [Test]
        public void TheEditor_RemovesAMemberAndItsComma()
        {
            string t = JsonTextEditor.Remove("{ \"a\": 1, \"b\": 2 }", "a");
            Assert.That(t, Is.EqualTo("{ \"b\": 2 }"));
            Assert.That(JsonTextEditor.Remove("{ \"a\": 1, \"b\": 2 }", "b"), Is.EqualTo("{ \"a\": 1 }"));
        }
    }
}
