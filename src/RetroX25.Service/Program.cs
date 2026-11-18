//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Service
{
    /// <summary>
    /// x25service - runs the X.25 services of the protocol runtime (the Altos chat) as a console program, with no
    /// window.
    /// </summary>
    /// <remarks>
    /// Usage: x25service &lt;config.json&gt; [-v]
    ///
    /// The file is the same <c>RuntimeConfig</c> JSON the Desktop app reads (samples in <c>docs/config-samples</c>).
    /// The runtime dials the switch named in its XOT interfaces and answers the calls its routing table lets in.
    /// It runs until Ctrl+C or until its process is stopped (the demo scripts stop it by process ID).
    /// </remarks>
    internal static class Program
    {
        private static readonly ManualResetEventSlim Shutdown = new ManualResetEventSlim(false);

        private static int Main(string[] args)
        {
            string? configPath = null;
            bool verbose = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-v")
                {
                    verbose = true;
                }
                else if (configPath == null)
                {
                    configPath = args[i];
                }
            }

            if (configPath == null)
            {
                Console.Error.WriteLine("Usage: x25service <config.json> [-v]");
                return 1;
            }

            using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
                b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
            });
            ILogger logger = loggerFactory.CreateLogger("x25service");

            RuntimeConfig config;
            try
            {
                config = Load(configPath);
            }
            catch (Exception ex)
            {
                // Fails loudly: a service started on defaults after a bad file would look alive and answer nothing.
                logger.LogError("x25service: {Reason}. Exiting.", ex.Message);
                return 1;
            }

            ProtocolRuntime runtime;
            try
            {
                runtime = new ProtocolRuntime(logger, config);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "x25service: the runtime did not start. Exiting.");
                return 1;
            }

            logger.LogInformation("x25service started from {Path}; chat address {Chat}", configPath,
                string.IsNullOrEmpty(config.Chat.Address) ? "(none)" : config.Chat.Address);

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Shutdown.Set();
            };

            Shutdown.Wait();
            runtime.Dispose();
            logger.LogInformation("x25service stopped");
            return 0;
        }

        // The same reading rules as the Desktop app (RetroX25.Desktop App.LoadConfigFile), so one file works in both.
        private static RuntimeConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("configuration file not found: " + path, path);
            }

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                Converters = { new JsonStringEnumConverter() }
            };

            RuntimeConfig? config = JsonSerializer.Deserialize<RuntimeConfig>(File.ReadAllText(path), options);
            if (config == null)
            {
                throw new InvalidDataException("configuration file is empty: " + path);
            }

            return config;
        }
    }
}
