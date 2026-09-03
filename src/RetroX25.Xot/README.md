# RetroX25.Xot.cs - XOT Test Application

A standalone Terminal.Gui-based testing application for X.25 over TCP (XOT) protocol with SINTRAN routing support.

## Table of Contents
- [Overview](#overview)
- [Command-Line Arguments](#command-line-arguments)
- [Usage Instructions](#usage-instructions)
- [Architecture Overview](#architecture-overview)
- [Client vs Server Modes](#client-vs-server-modes)
- [Window Types](#window-types)

---

## Overview

This application provides multiple testing windows for XOT protocol development:

- **XOT Test Window** - Basic XOT packet send/receive testing
- **SINTRAN Client Window** - Full SINTRAN HOST implementation (client and server modes)
- **SINTRAN PAD Client Window** - SINTRAN PAD client over LAPB
- **X.25 PAD Client Window** - Pure X.25 PAD client (no LAPB)
- **X.3 Parameters Window** - X.3 PAD parameter configuration

---

## Command-Line Arguments

### SINTRAN Client/Server Mode

```bash
# Server Mode (listen for incoming connections)
RetroX25.Xot.cs.exe --mode server --port <port> --machine-id <id>

# Client Mode (connect to remote host)
RetroX25.Xot.cs.exe --mode client --host <hostname> --port <port> --machine-id <local-id> --remote-id <remote-id>

# Interactive Mode (show menu)
RetroX25.Xot.cs.exe
```

### Command-Line Parameters

| Parameter | Description | Default | Example |
|-----------|-------------|---------|---------|
| `--mode` | Operation mode: `client`, `server`, `sintran`, `sintran-pad`, `x25-pad` | (menu) | `--mode server` |
| `--host` | Remote host for client mode | `localhost` | `--host 192.168.1.100` |
| `--port` | TCP port (client or server) | `1998` | `--port 1998` |
| `--machine-id` | Local SINTRAN machine ID | `200` | `--machine-id 100` |
| `--remote-id` | Remote SINTRAN machine ID (client mode) | `0` | `--remote-id 200` |

### Examples

```bash
# Start SINTRAN server on port 1998 with machine ID 100
RetroX25.Xot.cs.exe --mode server --port 1998 --machine-id 100

# Connect to SINTRAN server at localhost:1998 as machine ID 200
RetroX25.Xot.cs.exe --mode client --host localhost --port 1998 --machine-id 200 --remote-id 100

# Start SINTRAN PAD client as machine ID 200
RetroX25.Xot.cs.exe --mode sintran-pad --machine-id 200

# Start pure X.25 PAD client (no SINTRAN)
RetroX25.Xot.cs.exe --mode x25-pad

# Show interactive menu
RetroX25.Xot.cs.exe
```

---

## Usage Instructions

### Running as SINTRAN Server

1. **Start the server**:
   ```bash
   RetroX25.Xot.cs.exe --mode server --port 1998 --machine-id 100
   ```

2. **Wait for connections**:
   - Server listens on `0.0.0.0:1998` (all interfaces)
   - Window title shows: `SINTRAN HOST - ID:100 - TCP Server :1998 - LISTENING`

3. **Client connects**:
   - Server receives SABM frame from client
   - Protocol stack initializes (LAPB + X.25 + SINTRAN routing)
   - Window title updates: `TCP Server :1998 - CLIENT CONNECTED`

4. **Monitor activity**:
   - **SENT PACKETS** section shows outgoing frames
   - **RECEIVED PACKETS** section shows incoming frames
   - **ROUTING INFO** section shows LAPB state and routing requests
   - **PAD SERVER STATUS** section shows PAD service status

5. **Disconnect**:
   - Press **F4** to stop server and disconnect client
   - Press **F9** to quit application

### Running as SINTRAN Client

1. **Start the client**:
   ```bash
   RetroX25.Xot.cs.exe --mode client --host localhost --port 1998 --machine-id 200 --remote-id 100
   ```

2. **Connection process**:
   - Client connects to XOT gateway at `localhost:1998`
   - Sends SABM frame with local machine ID (200)
   - Waits for UA response from server

3. **After connection**:
   - Window title shows: `TCP Client localhost:1998 - CONNECTED`
   - Press **F2** to send routing info request
   - Press **F4** to disconnect

4. **Interactive connection** (via F3 key):
   - Press **F3** to show connection dialog
   - Enter remote host, port, and machine ID
   - Click **Connect** button

### Function Keys

| Key | Function | Description |
|-----|----------|-------------|
| **F2** | Send Routing Request | Send SINTRAN routing info request (0xDE) |
| **F3** | Connect | Show connection dialog (client mode only) |
| **F4** | Disconnect | Disconnect from remote node or stop server |
| **F9** | Quit | Exit application |

---

## Architecture Overview

### SINTRAN Client Mode Architecture

```mermaid
graph TB
    subgraph "SINTRAN Client (Machine ID 200)"
        UI[SINTRAN Client Window]
        XOT_C[XOT Client Transport]
        LAPB_C[LAPB Layer]
        X25_C[X.25 Layer]
        ROUT_C[SINTRAN Routing Handler]
        PAD_C[SINTRAN PAD Handler]

        UI -->|User Input| XOT_C
        XOT_C -->|TCP Connect| XOT_C
        XOT_C -->|Raw Frames| LAPB_C
        LAPB_C -->|I-frames| X25_C
        X25_C -->|X.25 Packets| ROUT_C
        ROUT_C -->|Protocol 0xDA| PAD_C

        PAD_C -->|Response| ROUT_C
        ROUT_C -->|X.25 Packet| X25_C
        X25_C -->|I-frame| LAPB_C
        LAPB_C -->|HDLC Frame| XOT_C
        XOT_C -->|Display| UI
    end

    subgraph "Remote Server (Machine ID 100)"
        SERVER[SINTRAN Server]
    end

    XOT_C <-->|TCP/IP Port 1998| SERVER

    style UI fill:#e1f5ff
    style XOT_C fill:#fff4e1
    style LAPB_C fill:#e8f5e9
    style X25_C fill:#f3e5f5
    style ROUT_C fill:#ffe0b2
    style PAD_C fill:#ffccbc
```

### SINTRAN Server Mode Architecture

```mermaid
graph TB
    subgraph "SINTRAN Server (Machine ID 100)"
        UI_S[SINTRAN Server Window]
        XOT_S[XOT Server Transport]
        LAPB_S[LAPB Layer]
        X25_S[X.25 Layer]
        ROUT_S[SINTRAN Routing Handler]
        PAD_S[SINTRAN PAD Handler]

        XOT_S -->|Listen on Port| XOT_S
        XOT_S -->|Client Data| LAPB_S
        LAPB_S -->|I-frames| X25_S
        X25_S -->|X.25 Packets| ROUT_S
        ROUT_S -->|Protocol 0xDA| PAD_S

        PAD_S -->|Response| ROUT_S
        ROUT_S -->|X.25 Packet| X25_S
        X25_S -->|I-frame| LAPB_S
        LAPB_S -->|HDLC Frame| XOT_S
        XOT_S -->|Display| UI_S
    end

    subgraph "Remote Client (Machine ID 200)"
        CLIENT[SINTRAN Client]
    end

    CLIENT <-->|TCP/IP Port 1998| XOT_S

    style UI_S fill:#e1f5ff
    style XOT_S fill:#fff4e1
    style LAPB_S fill:#e8f5e9
    style X25_S fill:#f3e5f5
    style ROUT_S fill:#ffe0b2
    style PAD_S fill:#ffccbc
```

### Protocol Stack Layering

```mermaid
graph LR
    subgraph "Layer Stack"
        direction TB
        TCP[TCP/IP Layer<br/>Port 1998]
        XOT[XOT Layer<br/>RFC 1613<br/>4-byte header]
        LAPB[LAPB Layer<br/>HDLC Frames<br/>SABM/UA/I-frames]
        X25[X.25 Layer<br/>Packet Layer<br/>Virtual Circuits]
        SINTRAN[SINTRAN Protocol<br/>0x21 0x13 0xXX<br/>Routing/PAD/TAD]
        APP[Application<br/>PAD Service<br/>Routing Tables]

        TCP --> XOT
        XOT --> LAPB
        LAPB --> X25
        X25 --> SINTRAN
        SINTRAN --> APP
    end

    style TCP fill:#ffebee
    style XOT fill:#fff3e0
    style LAPB fill:#e8f5e9
    style X25 fill:#e3f2fd
    style SINTRAN fill:#f3e5f5
    style APP fill:#fce4ec
```

### Data Flow: Client → Server

```mermaid
sequenceDiagram
    participant Client as SINTRAN Client<br/>(ID 200)
    participant XOT_C as XOT Client
    participant Network as TCP/IP Network
    participant XOT_S as XOT Server
    participant Server as SINTRAN Server<br/>(ID 100)

    Note over Client: User presses F3 (Connect)
    Client->>XOT_C: ConnectToXotGateway(host, port)
    XOT_C->>Network: TCP Connect
    Network->>XOT_S: TCP Accept
    XOT_S->>Server: Connection Established

    Note over Client: Initialize LAPB
    Client->>XOT_C: SABM Frame (ID 200)
    XOT_C->>Network: XOT Packet [00 00 + len + SABM]
    Network->>XOT_S: XOT Packet
    XOT_S->>Server: SABM Frame
    Server->>Server: Create LAPB Layer
    Server->>XOT_S: UA Frame
    XOT_S->>Network: XOT Packet [00 00 + len + UA]
    Network->>XOT_C: XOT Packet
    XOT_C->>Client: UA Frame

    Note over Client,Server: LAPB Connection Established

    Note over Client: User presses F2 (Routing Request)
    Client->>XOT_C: I-frame [0x21 0x13 0xDE 0x08]
    XOT_C->>Network: XOT Packet
    Network->>XOT_S: XOT Packet
    XOT_S->>Server: I-frame
    Server->>Server: Process Routing Request
    Server->>XOT_S: I-frame [Routing Response]
    XOT_S->>Network: XOT Packet
    Network->>XOT_C: XOT Packet
    XOT_C->>Client: I-frame

    Note over Client: Display routing table
```

### SINTRAN Protocol Frame Format

```mermaid
graph LR
    subgraph "SINTRAN Frame Structure"
        direction LR
        M1[0x21<br/>SINTRAN<br/>Marker]
        M2[0x13<br/>Protocol<br/>Type]
        PID[Protocol ID<br/>0xDA=PAD<br/>0xDD=TAD<br/>0xDE=Routing]
        DATA[Protocol Data<br/>Variable Length]

        M1 --> M2
        M2 --> PID
        PID --> DATA
    end

    style M1 fill:#ffcdd2
    style M2 fill:#f8bbd0
    style PID fill:#e1bee7
    style DATA fill:#d1c4e9
```

---

## Client vs Server Modes

### Comparison Table

| Feature | Client Mode | Server Mode |
|---------|-------------|-------------|
| **TCP Role** | Connects to remote server | Listens for incoming connections |
| **Initialization** | User triggers via F3 or CLI args | Auto-starts on application launch |
| **XOT Transport** | `XotClientTransport` | `XotServerTransport` |
| **Connection** | `ConnectToXotGateway(host, port)` | `Start()` on port 1998 |
| **Protocol Stack** | Created on connection | Created on first data received |
| **LAPB Initiator** | Client sends SABM | Server responds with UA |
| **Window Title** | Shows remote host:port | Shows listening port |
| **F3 Function** | Show connection dialog | Disabled (already listening) |
| **F4 Function** | Disconnect from server | Stop server and close connection |
| **Typical Use** | Connect to remote SINTRAN node | Provide services to remote clients |
| **Machine ID** | Local ID sent in SABM | Local ID used for routing |

### Client Mode Characteristics

```mermaid
graph TB
    START[Application Start]
    MENU[Show Main Menu]
    INIT[Initialize UI]
    WAIT[Wait for User Action]
    CONNECT[F3: Connect Dialog]
    HOST[Enter Host/Port]
    XOT[Create XOT Client]
    TCP[TCP Connect]
    LAPB[Send SABM]
    CONNECTED[Connected State]

    START --> MENU
    MENU --> INIT
    INIT --> WAIT
    WAIT --> CONNECT
    CONNECT --> HOST
    HOST --> XOT
    XOT --> TCP
    TCP --> LAPB
    LAPB --> CONNECTED

    CONNECTED --> |F2| ROUTING[Send Routing Request]
    CONNECTED --> |F4| DISCONNECT[Disconnect]
    DISCONNECT --> WAIT

    style START fill:#e3f2fd
    style CONNECTED fill:#c8e6c9
    style DISCONNECT fill:#ffccbc
```

### Server Mode Characteristics

```mermaid
graph TB
    START_S[Application Start]
    MENU_S[Main Menu: Select Server]
    INIT_S[Initialize Server UI]
    SERVER[Create XOT Server]
    LISTEN[Listen on Port 1998]
    WAIT_S[Wait for Connection]
    ACCEPT[Client Connects]
    SABM[Receive SABM]
    STACK[Create Protocol Stack]
    UA[Send UA Response]
    CONNECTED_S[Connected State]

    START_S --> MENU_S
    MENU_S --> INIT_S
    INIT_S --> SERVER
    SERVER --> LISTEN
    LISTEN --> WAIT_S
    WAIT_S --> ACCEPT
    ACCEPT --> SABM
    SABM --> STACK
    STACK --> UA
    UA --> CONNECTED_S

    CONNECTED_S --> |Receive Routing Req| RESPOND[Process & Respond]
    CONNECTED_S --> |F4| STOP[Stop Server]
    STOP --> WAIT_S

    style START_S fill:#e3f2fd
    style LISTEN fill:#fff9c4
    style CONNECTED_S fill:#c8e6c9
    style STOP fill:#ffccbc
```

### Key Differences

#### Connection Establishment

**Client Mode:**
1. User action required (F3 or CLI args)
2. Client actively connects to remote host
3. Client sends SABM frame
4. Waits for UA response

**Server Mode:**
1. Auto-starts on application launch
2. Passively listens on configured port
3. Waits for client SABM frame
4. Responds with UA frame

#### Protocol Stack Lifecycle

**Client Mode:**
```
User Action → Create Stack → Connect XOT → Send SABM → Wait UA → Ready
```

**Server Mode:**
```
App Start → Listen → Wait Client → Receive SABM → Create Stack → Send UA → Ready
```

#### Resource Management

**Client Mode:**
- Stack created before connection
- Connection owns resources
- Disconnect cleans up everything

**Server Mode:**
- Stack created on demand (lazy initialization)
- Server keeps listening after disconnect
- Can accept new client after previous disconnect

---

## Window Types

### 1. SINTRAN Client/Server Window

**Purpose**: Full SINTRAN HOST implementation with client and server modes

**Features**:
- Split-screen TX/RX packet display
- LAPB state monitoring
- SINTRAN routing protocol support
- PAD server status display
- Dual-mode operation (client or server)

**Layout**:
```
┌────────────────────────────────────────────────────┐
│ SINTRAN HOST - ID:200                              │
├────────────────────────────────────────────────────┤
│ ═══════════════ SENT PACKETS ═══════════════       │
│ [Outgoing frames display]                          │
│                                                    │
├────────────────────────────────────────────────────┤
│ ════════════ RECEIVED PACKETS ══════════════       │
│ [Incoming frames display]                          │
│                                                    │
├────────────────────────────────────────────────────┤
│ ROUTING INFO & LAPB STATE │ PAD SERVER STATUS     │
│ [Connection info]          │ [Active sessions]     │
│ [Routing table]            │ [Client list]         │
├────────────────────────────────────────────────────┤
│ F2:SendRoutingReq  F3:Connect  F4:Disconnect  F9:Quit
│ Status: LAPB CONNECTED | TX: 5 | RX: 3           │
└────────────────────────────────────────────────────┘
```

### 2. SINTRAN PAD Client Window

**Purpose**: SINTRAN PAD client using SINTRAN routing protocol over LAPB

**Protocol Stack**: XOT → LAPB → X.25 → SINTRAN (0x21 0x13 0xDA)

### 3. X.25 PAD Client Window

**Purpose**: Pure X.25 PAD client (no LAPB, no SINTRAN)

**Protocol Stack**: XOT → X.25 PAD (direct)

### 4. XOT Test Window

**Purpose**: Low-level XOT packet send/receive testing

**Features**:
- Predefined X.25 packets (F1-F8)
- Custom hex packet input (F5)
- Split-screen TX/RX display
- Packet type identification

---

## Implementation Details

### Server Mode Initialization

**File**: `SintranClientWindow.cs:397-434`

```csharp
private void StartTcpServer()
{
    // Create XOT server configuration
    var xotConfig = new XotServerConfig
    {
        Name = $"SINTRAN-HOST-{_localMachineId}",
        ListenAddress = "0.0.0.0",      // All interfaces
        ListenPort = _port,              // Default: 1998
        MaxConnections = 1               // Single client
    };

    _xotServer = new XotServerTransport(_logger, xotConfig);
    _xotServer.DataReceived += OnServerDataReceived;

    if (!_xotServer.Start())
    {
        // Error handling
    }
}
```

### Lazy Protocol Stack Creation

**Server mode** creates the protocol stack only when first data is received:

```csharp
private void OnServerDataReceived(object? sender, TransportDataEventArgs e)
{
    if (_lapbLayer == null)
    {
        // First data - initialize protocol stack
        _lapbLayer = new LapbLayer(_logger, null, false); // DTE mode
        _x25Layer = new X25Layer(_logger);
        WireUpServerComponents();
    }

    // Process data through LAPB
    _lapbLayer.ReceiveHdlcFrame(e.Data, e.Length);
}
```

This ensures the server remains lightweight until a client actually connects.

---

## Troubleshooting

### Server Not Receiving Connections

1. **Check port binding**:
   - Default port: 1998
   - Server listens on `0.0.0.0` (all interfaces)
   - Verify no firewall blocking

2. **Verify window title**:
   - Should show: `TCP Server :1998 - LISTENING`
   - If shows `SERVER STOPPED`, server failed to start

3. **Check logs**:
   - Sent packets section shows startup messages
   - Look for error messages

### Client Cannot Connect

1. **Verify server is running**:
   - Server window should show `LISTENING` status

2. **Check connection parameters**:
   - Host: `localhost` or correct IP address
   - Port: Must match server port (default 1998)
   - Machine IDs: Can be different

3. **Network connectivity**:
   - Test with `telnet localhost 1998`
   - Verify no network issues

### LAPB Connection Fails

1. **Check SABM/UA exchange**:
   - Client sends SABM frame
   - Server responds with UA frame
   - Both should appear in respective windows

2. **Verify LAPB state**:
   - Should transition to `CONNECTED`
   - Check routing info section for state

### No Routing Responses

1. **Ensure LAPB connected**:
   - Routing requires active LAPB connection
   - Check `LAPB State: CONNECTED` in routing info

2. **Verify routing handler**:
   - Initialized on server start
   - Registered with protocol stack

---

## File Structure

```
RetroX25.Xot.cs/
├── Program.cs                      # Entry point, CLI parsing, menu system
├── SintranClientWindow.cs          # SINTRAN HOST window (client/server)
├── SintranPadClientWindow.cs       # SINTRAN PAD client window
├── X25PadClientWindow.cs           # Pure X.25 PAD client window
├── XotTestWindow.cs                # XOT test window (low-level)
├── HexInputDialog.cs               # Custom hex packet input dialog
├── X3ParametersWindow.cs           # X.3 PAD parameters configuration
├── XotTestServer.cs                # Lightweight XOT server
├── XotTestClient.cs                # Lightweight XOT client
└── README.md                       # This file
```

---

## References

- **RFC 1613**: X.25 over TCP (XOT)
- **ITU-T X.25**: Packet Layer Protocol
- **ITU-T X.3**: PAD Parameters
- **ITU-T X.28**: DTE/DCE Interface for PAD
- **ITU-T X.29**: Procedures for PAD

---

## Build and Run

### Build

```bash
cd E:\Dev\Repos\Ronny\X25Emulator
dotnet build RetroX25.Xot.cs\RetroX25.Xot.cs.csproj
```

### Run

```bash
cd RetroX25.Xot.cs\bin\Debug\net9.0
RetroX25.Xot.cs.exe [arguments]
```

### Development

- **Framework**: .NET 9.0
- **UI Library**: Terminal.Gui
- **Logging**: Microsoft.Extensions.Logging
- **Architecture**: Event-driven, layered protocol stack
