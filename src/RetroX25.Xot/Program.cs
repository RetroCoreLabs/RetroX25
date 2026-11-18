//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Terminal.Gui;

namespace RetroX25.Xot
{
    class Program
    {
        static void Main(string[] args)
        {
            // Parse command-line arguments
            if (args.Length > 0 && TryParseCommandLine(args, out var mode, out var host, out var port))
            {
                LaunchApplication(mode, host, port);
            }
            else
            {
                // Show interactive menu
                ShowInteractiveMenu();
            }
        }

        static bool TryParseCommandLine(string[] args, out string mode, out string host, out int port)
        {
            mode = string.Empty;
            host = string.Empty;
            port = 0;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--mode":
                    case "-m":
                        if (i + 1 < args.Length)
                        {
                            mode = args[++i].ToLower();
                        }
                        break;

                    case "--host":
                    case "-h":
                        if (i + 1 < args.Length)
                        {
                            host = args[++i];
                        }
                        break;

                    case "--port":
                    case "-p":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int p))
                        {
                            port = p;
                        }
                        break;

                    case "--help":
                    case "-?":
                        ShowHelp();
                        return false;
                }
            }

            // Validate
            if (mode == "client")
            {
                if (string.IsNullOrEmpty(host) || port == 0)
                {
                    Console.WriteLine("Error: Client mode requires --host and --port");
                    ShowHelp();
                    return false;
                }
                return true;
            }
            else if (mode == "server")
            {
                if (port == 0)
                {
                    Console.WriteLine("Error: Server mode requires --port");
                    ShowHelp();
                    return false;
                }
                return true;
            }

            return false;
        }

        static void ShowHelp()
        {
            Console.WriteLine("XOT Test Console Application");
            Console.WriteLine("============================");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  x25xot [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --mode, -m <client|server>    Operating mode");
            Console.WriteLine("  --host, -h <hostname>         Remote host (client mode only)");
            Console.WriteLine("  --port, -p <port>             Port number");
            Console.WriteLine("  --help, -?                    Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  x25xot --mode client --host 192.168.1.100 --port 1998");
            Console.WriteLine("  x25xot --mode server --port 1998");
            Console.WriteLine();
            Console.WriteLine("Interactive Mode:");
            Console.WriteLine("  Run without arguments for interactive menu");
            Console.WriteLine();
            Console.WriteLine("Function Keys:");
            Console.WriteLine("  F1 - Send Call Request packet");
            Console.WriteLine("  F2 - Send Call Accept packet");
            Console.WriteLine("  F3 - Send Data packet");
            Console.WriteLine("  F4 - Send Clear Request packet");
            Console.WriteLine("  F5 - Send custom hex packet");
            Console.WriteLine("  F6 - Send RR (Receiver Ready)");
            Console.WriteLine("  F7 - Send RNR (Receiver Not Ready)");
            Console.WriteLine("  F8 - Send Reset Request");
            Console.WriteLine("  F9 - Quit application");
        }

        static void ShowInteractiveMenu()
        {
            Console.Clear();
            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║   XOT Test Console Application (RFC 1613)   ║");
            Console.WriteLine("║         X.25 over TCP Test Tool              ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("Select mode:");
            Console.WriteLine("  1) Raw XOT Client - Send/receive raw packets");
            Console.WriteLine("  2) Raw XOT Server - Accept connections");
            Console.WriteLine("  3) Help");
            Console.WriteLine("  4) Quit");
            Console.WriteLine();
            Console.Write("Enter choice [1-4]: ");

            string? choice = Console.ReadLine();
            Console.WriteLine();

            switch (choice?.Trim())
            {
                case "1":
                    SetupClient();
                    break;

                case "2":
                    SetupServer();
                    break;

                case "3":
                    ShowHelp();
                    Console.WriteLine();
                    Console.WriteLine("Press any key to return to menu...");
                    Console.ReadKey(true);
                    ShowInteractiveMenu();
                    break;

                case "4":
                    return;

                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    Console.WriteLine();
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey(true);
                    ShowInteractiveMenu();
                    break;
            }
        }

        static void SetupClient()
        {
            Console.WriteLine("═══ XOT Client Setup ═══");
            Console.WriteLine();

            Console.Write("Remote host (IP address or hostname): ");
            string? host = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(host))
            {
                Console.WriteLine("Error: Host is required");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            Console.Write("Remote port [default: 1998]: ");
            string? portInput = Console.ReadLine();

            int port = 1998;
            if (!string.IsNullOrWhiteSpace(portInput) && !int.TryParse(portInput, out port))
            {
                Console.WriteLine("Error: Invalid port number");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            LaunchApplication("client", host, port);
        }

        static void SetupServer()
        {
            Console.WriteLine("═══ XOT Server Setup ═══");
            Console.WriteLine();

            Console.Write("Listen port [default: 1998]: ");
            string? portInput = Console.ReadLine();

            int port = 1998;
            if (!string.IsNullOrWhiteSpace(portInput) && !int.TryParse(portInput, out port))
            {
                Console.WriteLine("Error: Invalid port number");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            LaunchApplication("server", string.Empty, port);
        }

        static void LaunchApplication(string mode, string host, int port)
        {
            try
            {
                // Initialize Terminal.Gui
                Application.Init();

                try
                {
                    if (mode == "client")
                    {
                        // Create and connect client
                        var client = new XotTestClient(host, port);
                        if (!client.Connect())
                        {
                            Application.Shutdown();
                            Console.WriteLine($"Failed to connect to {host}:{port}");
                            Console.WriteLine("Press any key to exit...");
                            Console.ReadKey(true);
                            return;
                        }

                        // Create and run window
                        var window = new XotTestWindow(client);
                        Application.Top.Add(window);
                        Application.Run();

                        // Cleanup
                        client.Dispose();
                    }
                    else if (mode == "server")
                    {
                        // Create and start server
                        var server = new XotTestServer(port);
                        if (!server.Start())
                        {
                            Application.Shutdown();
                            Console.WriteLine($"Failed to start server on port {port}");
                            Console.WriteLine("Press any key to exit...");
                            Console.ReadKey(true);
                            return;
                        }

                        // Create and run window
                        var window = new XotTestWindow(server);
                        Application.Top.Add(window);
                        Application.Run();

                        // Cleanup
                        server.Stop();
                        server.Dispose();
                    }
                }
                finally
                {
                    Application.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(true);
            }
        }
    }
}
