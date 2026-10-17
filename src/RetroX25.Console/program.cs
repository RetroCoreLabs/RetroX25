//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Lapb;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Common;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;
using RetroX25.Console;

namespace RetroX25.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            // Initialize logger with history provider
            var historyProvider = new HistoryLoggingProvider(1000); // Keep last 1000 log entries
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(historyProvider);
                // Remove console logging - all logs will go to GUI log window only
                // builder.AddConsole(); // Commented out to prevent console output
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            });
            var logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("X.25 Protocol Emulator Starting...");

            // Determine config file path - check command line or use default
            string configFilePath = "x25config.json";

            // Check command line for config file override
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    configFilePath = args[i];
                    break;
                }
            }

            // Load configuration from JSON file or use defaults
            RuntimeConfig config;

            if (File.Exists(configFilePath))
            {
                try
                {
                    string json = File.ReadAllText(configFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    };
                    var loadedConfig = JsonSerializer.Deserialize<RuntimeConfig>(json, options);
                    if (loadedConfig != null)
                    {
                        config = loadedConfig;
                        logger.LogInformation("Loaded configuration from {ConfigFile}", configFilePath);
                        logger.LogInformation("LocalMachineId: {Id}", config.X25.LocalMachineId);
                    }
                    else
                    {
                        logger.LogWarning("Config file {ConfigFile} was empty, using defaults", configFilePath);
                        config = RoutingConfigExample.CreateSampleRoutingConfig();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load config from {ConfigFile}, using defaults", configFilePath);
                    config = RoutingConfigExample.CreateSampleRoutingConfig();
                }
            }
            else
            {
                logger.LogInformation("Config file {ConfigFile} not found, using sample configuration", configFilePath);
                config = RoutingConfigExample.CreateSampleRoutingConfig();
            }

            // Create protocol runtime
            var runtime = new ProtocolRuntime(logger, config);

            // Start GUI with logger and history provider
            var gui = new X25Gui();
            gui.Run(runtime, logger, historyProvider);

            // Cleanup
            runtime.Dispose();
            loggerFactory.Dispose();

            logger.LogInformation("X.25 Protocol Emulator Shutting down...");
        }
    }
}
