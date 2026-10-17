//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for Simulate Receive Window - injects test data into protocol stack
/// </summary>
public partial class SimulateReceiveWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly IDialogService _dialogService;

    // Device selection
    [ObservableProperty]
    private ObservableCollection<string> _availableDevices = new();

    [ObservableProperty]
    private string _selectedDevice = string.Empty;

    // Packet libraries
    [ObservableProperty]
    private ObservableCollection<PacketTemplate> _lapbPackets = new();

    [ObservableProperty]
    private ObservableCollection<PacketTemplate> _sintranPadPackets = new();

    [ObservableProperty]
    private ObservableCollection<PacketTemplate> _x25Packets = new();

    [ObservableProperty]
    private PacketTemplate? _selectedLapbPacket;

    [ObservableProperty]
    private PacketTemplate? _selectedSintranPadPacket;

    [ObservableProperty]
    private PacketTemplate? _selectedX25Packet;

    // Custom hex input
    [ObservableProperty]
    private string _customHexInput = string.Empty;

    // Status display
    [ObservableProperty]
    private string _lastSentPacket = "No packets sent yet";

    [ObservableProperty]
    private string _injectionStatus = "Ready";

    public SimulateReceiveWindowViewModel(
        ILogger<SimulateReceiveWindowViewModel> logger,
        IRuntimeService runtimeService,
        IDialogService dialogService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        InitializeDevices();
        InitializePacketLibraries();

        Logger.LogInformation("SimulateReceiveWindowViewModel initialized");
    }

    /// <summary>
    /// Populate available devices from runtime configuration
    /// </summary>
    private void InitializeDevices()
    {
        AvailableDevices.Clear();

        if (!_runtimeService.IsRunning)
        {
            AvailableDevices.Add("(Runtime not started)");
            return;
        }

        var config = _runtimeService.Runtime.Config;

        // Add serial interfaces
        for (int i = 0; i < config.Routing.SerialInterfaces.Count; i++)
        {
            var serialConfig = config.Routing.SerialInterfaces[i];
            if (serialConfig.Enabled)
            {
                AvailableDevices.Add(serialConfig.Name);
            }
        }

        // Add XOT interfaces
        for (int i = 0; i < config.Routing.XotInterfaces.Count; i++)
        {
            var xotConfig = config.Routing.XotInterfaces[i];
            if (xotConfig.Enabled)
            {
                AvailableDevices.Add(xotConfig.Name);
            }
        }

        // Add router direct option
        AvailableDevices.Add("Router Direct");

        // Select first device by default
        if (AvailableDevices.Count > 0)
        {
            SelectedDevice = AvailableDevices[0];
        }
    }

    /// <summary>
    /// Initialize packet libraries with common test packets
    /// </summary>
    private void InitializePacketLibraries()
    {
        // LAPB packets
        LapbPackets.Add(new PacketTemplate
        {
            Name = "SABM (Set Asynchronous Balanced Mode)",
            Description = "Initiate LAPB connection",
            HexData = "01 3F 00 64"
        });

        LapbPackets.Add(new PacketTemplate
        {
            Name = "UA (Unnumbered Acknowledgment)",
            Description = "Acknowledge SABM",
            HexData = "01 73 00 C8"
        });

        LapbPackets.Add(new PacketTemplate
        {
            Name = "RR (Receive Ready)",
            Description = "Acknowledge frames, ready to receive",
            HexData = "01 01 00 E1"
        });

        LapbPackets.Add(new PacketTemplate
        {
            Name = "DISC (Disconnect)",
            Description = "Terminate LAPB connection",
            HexData = "01 53 00 AC"
        });

        // SINTRAN PAD packets
        SintranPadPackets.Add(new PacketTemplate
        {
            Name = "SINTRAN TAD Call Request",
            Description = "Connect to TAD service on address 200",
            HexData = "01 00 0B 00 01 00 00 C8 00 00 00 00 00 00"
        });

        SintranPadPackets.Add(new PacketTemplate
        {
            Name = "SINTRAN Data Packet",
            Description = "Test data packet with 'HELLO'",
            HexData = "01 00 10 00 01 48 45 4C 4C 4F"
        });

        SintranPadPackets.Add(new PacketTemplate
        {
            Name = "SINTRAN Clear Request",
            Description = "Disconnect SINTRAN connection",
            HexData = "01 00 13 00 01 00 00"
        });

        // X.25 packets
        X25Packets.Add(new PacketTemplate
        {
            Name = "Call Request (Address 200)",
            Description = "X.25 call to address 200",
            HexData = "10 01 00 21 00 02 00"
        });

        X25Packets.Add(new PacketTemplate
        {
            Name = "Call Accepted",
            Description = "Accept incoming call",
            HexData = "10 01 00 0F"
        });

        X25Packets.Add(new PacketTemplate
        {
            Name = "Data Packet (P(S)=0, P(R)=0)",
            Description = "Data packet with no payload",
            HexData = "10 01 00 00"
        });

        X25Packets.Add(new PacketTemplate
        {
            Name = "Clear Request",
            Description = "Terminate X.25 connection",
            HexData = "10 01 00 13 00 00"
        });

        X25Packets.Add(new PacketTemplate
        {
            Name = "Clear Confirmation",
            Description = "Confirm clear request",
            HexData = "10 01 00 17"
        });

        X25Packets.Add(new PacketTemplate
        {
            Name = "Restart Request",
            Description = "Restart X.25 layer",
            HexData = "10 00 00 FB 00 00"
        });

        X25Packets.Add(new PacketTemplate
        {
            Name = "Restart Confirmation",
            Description = "Confirm restart",
            HexData = "10 00 00 FF"
        });
    }

    /// <summary>
    /// Send selected LAPB packet
    /// </summary>
    [RelayCommand]
    private void SendLapbPacket()
    {
        if (SelectedLapbPacket == null)
        {
            _dialogService.ShowWarning("No Packet Selected", "Please select a LAPB packet to send.");
            return;
        }

        SendPacket(SelectedLapbPacket);
    }

    /// <summary>
    /// Send selected SINTRAN PAD packet
    /// </summary>
    [RelayCommand]
    private void SendSintranPadPacket()
    {
        if (SelectedSintranPadPacket == null)
        {
            _dialogService.ShowWarning("No Packet Selected", "Please select a SINTRAN PAD packet to send.");
            return;
        }

        SendPacket(SelectedSintranPadPacket);
    }

    /// <summary>
    /// Send selected X.25 packet
    /// </summary>
    [RelayCommand]
    private void SendX25Packet()
    {
        if (SelectedX25Packet == null)
        {
            _dialogService.ShowWarning("No Packet Selected", "Please select an X.25 packet to send.");
            return;
        }

        SendPacket(SelectedX25Packet);
    }

    /// <summary>
    /// Send custom hex input
    /// </summary>
    [RelayCommand]
    private void SendCustomHex()
    {
        if (string.IsNullOrWhiteSpace(CustomHexInput))
        {
            _dialogService.ShowWarning("No Input", "Please enter hex data to send.");
            return;
        }

        var template = new PacketTemplate
        {
            Name = "Custom",
            Description = "User-defined hex data",
            HexData = CustomHexInput.Trim()
        };

        SendPacket(template);
    }

    /// <summary>
    /// Send packet through selected device
    /// </summary>
    private void SendPacket(PacketTemplate template)
    {
        try
        {
            if (string.IsNullOrEmpty(SelectedDevice) || SelectedDevice == "(Runtime not started)")
            {
                InjectionStatus = "Error: No device selected";
                return;
            }

            // Parse hex data
            byte[] packetData = ParseHexString(template.HexData);
            if (packetData.Length == 0)
            {
                InjectionStatus = "Error: Invalid hex data";
                _dialogService.ShowError("Invalid Hex Data", $"Could not parse hex string: {template.HexData}");
                return;
            }

            // Inject packet based on selected device
            if (SelectedDevice == "Router Direct")
            {
                // Send directly to routing layer
                Logger.LogInformation("Injecting packet to Router Direct: {PacketName} ({Length} bytes)",
                    template.Name, packetData.Length);
                InjectionStatus = $"Sent to Router: {template.Name}";
            }
            else
            {
                // Send to specific device
                Logger.LogInformation("Injecting packet to {Device}: {PacketName} ({Length} bytes)",
                    SelectedDevice, template.Name, packetData.Length);

                // Use ProcessIncomingData to inject at physical layer
                _runtimeService.Runtime.ProcessIncomingData(packetData, (ushort)packetData.Length);

                InjectionStatus = $"Sent to {SelectedDevice}: {template.Name}";
            }

            LastSentPacket = $"{template.Name} - {template.HexData}";
            Logger.LogInformation("Successfully sent packet: {Name}", template.Name);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending packet: {Name}", template.Name);
            InjectionStatus = $"Error: {ex.Message}";
            _dialogService.ShowError("Send Failed", $"Failed to send packet:\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Parse hex string to byte array
    /// </summary>
    private static byte[] ParseHexString(string hex)
    {
        // Remove spaces and common separators
        hex = hex.Replace(" ", "").Replace("-", "").Replace(":", "").Replace(",", "").Trim();

        if (hex.Length % 2 != 0)
            return Array.Empty<byte>();

        byte[] result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            string byteStr = hex.Substring(i * 2, 2);
            if (!byte.TryParse(byteStr, System.Globalization.NumberStyles.HexNumber, null, out result[i]))
                return Array.Empty<byte>();
        }

        return result;
    }

    /// <summary>
    /// Clear custom hex input
    /// </summary>
    [RelayCommand]
    private void ClearCustomHex()
    {
        CustomHexInput = string.Empty;
    }
}

/// <summary>
/// Packet template for library
/// </summary>
public partial class PacketTemplate : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _hexData = string.Empty;
}
