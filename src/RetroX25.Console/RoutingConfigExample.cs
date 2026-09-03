using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;
using System;
using System.IO.Ports;

namespace RetroX25.Console
{
    /// <summary>
    /// Example showing how to configure X.25 centralized routing programmatically
    /// Demonstrates routing to X.121 address "100.*" via serial port and "300.*" via XOT
    /// </summary>
    public static class RoutingConfigExample
    {
        /// <summary>
        /// Create a sample routing configuration with centralized Cisco-style routing
        /// - Route "100.*" via Serial0 interface
        /// - Route "300.*" via XOT0 interface
        /// - Route "200" to LocalTAD
        /// - Default gateway "100" for unmatched routes
        /// </summary>
        public static RuntimeConfig CreateSampleRoutingConfig()
        {
            var config = new RuntimeConfig();
            
            // Configure X.25 settings
            config.X25.LocalMachineId = "200";
            
            // Standard LAPB configuration for peer-to-peer DTE mode
            config.Lapb.IsDCE = false;  // DTE mode for peer-to-peer communication
            config.Lapb.T1Timeout = TimeSpan.FromSeconds(3);
            config.Lapb.T2Timeout = TimeSpan.FromMilliseconds(200);
            config.Lapb.T3Timeout = TimeSpan.FromMilliseconds(0);  // Disabled per ITU-T X.25 
            config.Lapb.MaxRetries = 5;
            config.Lapb.WindowSize = 7;
            
            // Enable centralized routing
            config.Routing.Enabled = true;
            config.Routing.EnableFailover = true;
            config.Routing.FailoverTimeout = 5000;
            config.Routing.DefaultGateway = "100";
            
            // Configure Serial0 interface
            config.Routing.SerialInterfaces.Add(new SerialInterfaceConfig
            {
                Name = "Serial0",
                Enabled = true,
                PortName = "COM7",
                BaudRate = 19200,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                RemoteMachineId = "100",
                Description = "Serial connection to X.25 switch 100"
            });
            
            // Configure XOT0 interface  
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig
            {
                Name = "XOT0",
                Enabled = false, // Disabled by default
                Mode = XotMode.Client,
                RemoteIPAddress = "192.168.1.240",
                RemotePort = 1998,
                RemoteMachineId = "300",
                Description = "XOT connection to remote X.25 network 300"
            });
            
            // Configure routing table
            
            // Route 1: Local TAD server
            config.Routing.RoutingTable.Add(new X25RouteEntry
            {
                Name = "LocalTAD",
                X121AddressPattern = "200",
                Type = RouteType.Local,
                Destination = "LocalTAD",
                Priority = 200,
                Enabled = true,
                Description = "Route to built-in TAD server"
            });
            
            // Route 2: Serial network (addresses starting with 100)
            config.Routing.RoutingTable.Add(new X25RouteEntry
            {
                Name = "SerialNetwork",
                X121AddressPattern = "100.*",
                Type = RouteType.Interface,
                Destination = "Serial0",
                Priority = 100,
                Enabled = true,
                Description = "Route 100.* addresses to Serial0"
            });
            
            // Route 3: XOT network (addresses starting with 300)
            config.Routing.RoutingTable.Add(new X25RouteEntry
            {
                Name = "XotNetwork",
                X121AddressPattern = "300.*",
                Type = RouteType.Interface,
                Destination = "XOT0",
                Priority = 100,
                Enabled = false, // Disabled since XOT interface is disabled
                Description = "Route 300.* addresses to XOT0"
            });
            
            // Route 4: Gateway route for indirect access
            config.Routing.RoutingTable.Add(new X25RouteEntry
            {
                Name = "GatewayRoute",
                X121AddressPattern = "101.*",
                Type = RouteType.Gateway,
                Destination = "100", // Route through gateway machine 100
                Priority = 50,
                Enabled = true,
                Description = "Route 101.* through gateway 100"
            });
            
            // Configure known hosts for multi-hop routing
            config.Routing.KnownHosts.Add(new KnownHostEntry
            {
                Name = "SerialGateway",
                MachineId = "100",
                Reachability = HostReachability.Direct,
                Via = "Serial0",
                Description = "Gateway machine 100 for remote network access",
                Enabled = true
            });
            
            return config;
        }
        
        /// <summary>
        /// Create a minimal routing configuration for testing
        /// Only includes LocalTAD route
        /// </summary>
        public static RuntimeConfig CreateMinimalRoutingConfig()
        {
            var config = new RuntimeConfig();
            
            // Configure X.25 settings
            config.X25.LocalMachineId = "200";
            
            // Enable routing with just LocalTAD
            config.Routing.Enabled = true;
            
            // Route to LocalTAD only
            config.Routing.RoutingTable.Add(new X25RouteEntry
            {
                Name = "LocalTAD",
                X121AddressPattern = "200",
                Type = RouteType.Local,
                Destination = "LocalTAD",
                Priority = 200,
                Enabled = true,
                Description = "Route to built-in TAD server"
            });
            
            return config;
        }
        
        /// <summary>
        /// Create comprehensive routing configuration for production use
        /// Includes multiple interfaces and complex routing scenarios
        /// </summary>
        public static RuntimeConfig CreateComprehensiveRoutingConfig()
        {
            return RuntimeConfig.CreateComprehensiveExample();
        }
    }
}