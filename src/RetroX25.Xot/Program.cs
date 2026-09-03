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
            Console.WriteLine("  X25Emulator.XOT.exe [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --mode, -m <client|server>    Operating mode");
            Console.WriteLine("  --host, -h <hostname>         Remote host (client mode only)");
            Console.WriteLine("  --port, -p <port>             Port number");
            Console.WriteLine("  --help, -?                    Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  X25Emulator.XOT.exe --mode client --host 192.168.1.100 --port 1998");
            Console.WriteLine("  X25Emulator.XOT.exe --mode server --port 1998");
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
            Console.WriteLine("  3) SINTRAN HOST - Full SINTRAN node (routing + PAD server)");
            Console.WriteLine("  4) SINTRAN PAD - Connect to SINTRAN PAD service");
            Console.WriteLine("  5) X.25 PAD - Pure X.25 PAD client (no LAPB)");
            Console.WriteLine("  6) Help");
            Console.WriteLine("  7) Quit");
            Console.WriteLine();
            Console.Write("Enter choice [1-7]: ");

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
                    SetupSintranClient();
                    break;

                case "4":
                    SetupSintranPad();
                    break;

                case "5":
                    SetupX25Pad();
                    break;

                case "6":
                    ShowHelp();
                    Console.WriteLine();
                    Console.WriteLine("Press any key to return to menu...");
                    Console.ReadKey(true);
                    ShowInteractiveMenu();
                    break;

                case "7":
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

        static void SetupSintranClient()
        {
            Console.WriteLine("═══ SINTRAN HOST Setup ═══");
            Console.WriteLine();

            Console.WriteLine("Select TCP mode:");
            Console.WriteLine("  1) TCP Client - Connect to remote host");
            Console.WriteLine("  2) TCP Server - Listen for incoming connections");
            Console.WriteLine();
            Console.Write("Enter choice [1-2]: ");
            string? modeChoice = Console.ReadLine();
            Console.WriteLine();

            bool isServer = false;
            string? host = null;
            int port = 1998;

            if (modeChoice?.Trim() == "2")
            {
                // TCP Server mode
                isServer = true;

                Console.Write("Listen port [default: 1998]: ");
                string? portInput = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(portInput) && !int.TryParse(portInput, out port))
                {
                    Console.WriteLine("Error: Invalid port number");
                    Console.WriteLine("Press any key to return to menu...");
                    Console.ReadKey(true);
                    ShowInteractiveMenu();
                    return;
                }
            }
            else if (modeChoice?.Trim() == "1")
            {
                // TCP Client mode - ask for remote host and port
                Console.Write("Remote host [default: localhost]: ");
                host = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(host))
                {
                    host = "localhost";
                }

                Console.Write("Remote port [default: 1998]: ");
                string? portInput = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(portInput) && !int.TryParse(portInput, out port))
                {
                    Console.WriteLine("Error: Invalid port number");
                    Console.WriteLine("Press any key to return to menu...");
                    Console.ReadKey(true);
                    ShowInteractiveMenu();
                    return;
                }
            }
            else
            {
                Console.WriteLine("Invalid choice.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            Console.Write("Local Machine ID [default: 200]: ");
            string? machineIdInput = Console.ReadLine();

            ushort machineId = 200;
            if (!string.IsNullOrWhiteSpace(machineIdInput) && !ushort.TryParse(machineIdInput, out machineId))
            {
                Console.WriteLine("Error: Invalid machine ID");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            LaunchSintranClient(machineId, isServer, host, port);
        }

        static void SetupSintranPad()
        {
            Console.WriteLine("═══ SINTRAN PAD Setup ═══");
            Console.WriteLine();

            Console.WriteLine("Select TCP mode:");
            Console.WriteLine("  1) TCP Client - Connect to remote host");
            Console.WriteLine("  2) TCP Server - Listen for incoming connections (not yet implemented)");
            Console.WriteLine();
            Console.Write("Enter choice [1-2]: ");
            string? modeChoice = Console.ReadLine();
            Console.WriteLine();

            if (modeChoice?.Trim() == "2")
            {
                Console.WriteLine("TCP Server mode not yet implemented.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }
            else if (modeChoice?.Trim() != "1")
            {
                Console.WriteLine("Invalid choice.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            // TCP Client mode - ask for remote host and port
            Console.Write("Remote host [default: localhost]: ");
            string? host = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(host))
            {
                host = "localhost";
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

            Console.Write("Local Machine ID [default: 200]: ");
            string? machineIdInput = Console.ReadLine();

            ushort machineId = 200;
            if (!string.IsNullOrWhiteSpace(machineIdInput) && !ushort.TryParse(machineIdInput, out machineId))
            {
                Console.WriteLine("Error: Invalid machine ID");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            LaunchSintranPad(machineId);
        }

        static void SetupX25Pad()
        {
            Console.WriteLine("═══ X.25 PAD Setup ═══");
            Console.WriteLine();

            Console.WriteLine("Select TCP mode:");
            Console.WriteLine("  1) TCP Client - Connect to remote host");
            Console.WriteLine("  2) TCP Server - Listen for incoming connections (not yet implemented)");
            Console.WriteLine();
            Console.Write("Enter choice [1-2]: ");
            string? modeChoice = Console.ReadLine();
            Console.WriteLine();

            if (modeChoice?.Trim() == "2")
            {
                Console.WriteLine("TCP Server mode not yet implemented.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }
            else if (modeChoice?.Trim() != "1")
            {
                Console.WriteLine("Invalid choice.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                ShowInteractiveMenu();
                return;
            }

            // TCP Client mode - ask for remote host and port
            Console.Write("Remote host [default: localhost]: ");
            string? host = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(host))
            {
                host = "localhost";
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

            Console.Write("Local X.121 Address [default: 12345]: ");
            string? addressInput = Console.ReadLine();

            string localAddress = "12345";
            if (!string.IsNullOrWhiteSpace(addressInput))
            {
                localAddress = addressInput;
            }

            LaunchX25Pad(localAddress);
        }

        static void LaunchSintranClient(ushort machineId, bool isServer, string? remoteHost, int port)
        {
            try
            {
                // Initialize Terminal.Gui
                Application.Init();

                try
                {
                    // SINTRAN HOST window retired with Stack A (see _retired/XotWindows/SintranClientWindow.cs).
                    // Migrate it onto LapbInstance (Stack B) to restore this feature.
                    Console.WriteLine($"SINTRAN HOST window retired (Stack A). machineId={machineId}, server={isServer}, host={remoteHost}, port={port}");
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

        static void LaunchSintranPad(ushort machineId)
        {
            try
            {
                // Initialize Terminal.Gui
                Application.Init();

                try
                {
                    // SINTRAN PAD window retired with Stack A (see _retired/XotWindows/SintranPadClientWindow.cs).
                    // Migrate it onto LapbInstance (Stack B) to restore this feature.
                    Console.WriteLine($"SINTRAN PAD window retired (Stack A). machineId={machineId}");
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

        static void LaunchX25Pad(string localAddress)
        {
            try
            {
                // Initialize Terminal.Gui
                Application.Init();

                try
                {
                    // Create logger (simplified - no output for now)
                    var logger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
                    {
                        // No console logger configured - logs are discarded
                    }).CreateLogger("X25Pad");

                    // Create and run X.25 PAD window
                    var window = new X25PadClientWindow(logger, localAddress);
                    Application.Top.Add(window);
                    Application.Run();
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
