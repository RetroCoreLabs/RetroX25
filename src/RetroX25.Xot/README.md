# x25xot — the XOT test application

## What it is

A standalone Terminal.Gui application for testing **XOT** — X.25 over TCP — in either direction, with SINTRAN
routing support. It runs as a client, as a server, or interactively with a menu and function keys.

## Why you need it

Because sometimes you need to send **one packet** and see exactly what comes back, and every other tool here is
busy being a correct X.25 implementation.

`x25pad` places a call and manages a circuit properly. This is the level below: open an XOT connection, push a
call request, watch the bytes. When a peer is rejecting something and the specification does not explain why, being
able to send a single packet by hand — and to be the **server** so you can see what the other side sends first — is
what settles it.

It is also the tool for the **SINTRAN** side of XOT: a full SINTRAN HOST implementation in both roles, a SINTRAN
PAD client over LAPB, and a pure X.25 PAD client with no LAPB. Having all three in one place is how the difference
between them gets established, because the difference is exactly what breaks interoperability.

## What you can use it for

- **Being either end** of an XOT connection: `--mode client` or `--mode server`.
- Sending a call request by hand (**F1** in interactive mode) and reading the answer.
- Testing the **SINTRAN HOST** protocol in client and server roles.
- Comparing a SINTRAN PAD client **over LAPB** with a pure X.25 PAD client **without** it — the two are genuinely
  different on the wire.
- Configuring X.3 parameters interactively and seeing the effect.
- Proving two endpoints connect at all, before anything more complicated is attempted.

## Prerequisites

| # | Requirement | Why | How to check |
|---|---|---|---|
| P1 | .NET SDK as pinned in `global.json`, or the published exe | To build or run | `dotnet --info` |
| P2 | A terminal | Terminal.Gui draws on it; function keys must reach it | A real terminal, not a pipe |
| P3 | A peer, or this tool in the other mode | Anything useful | Two shells: one server, one client |
| P4 | A free TCP port | Server mode | 1998 is the conventional XOT port |
| P5 | A SINTRAN machine | The SINTRAN windows, for a real test | Record as SKIPPED if unavailable |

```powershell
x25xot --mode server --port 1998                          # be the server
x25xot --mode client --host 127.0.0.1 --port 1998         # be the client
x25xot                                                    # interactive menu
x25xot --help                                             # usage
```

In interactive mode, **F1 sends a Call Request**.

## Client and server, both from here

```mermaid
flowchart LR
    S["x25xot --mode server --port 1998"]
    C["x25xot --mode client --host ... --port 1998"]
    SW["or a real x25switch"]
    ND["or a SINTRAN machine"]

    C -->|"XOT over TCP"| S
    C --> SW
    C --> ND
    ND --> S

    classDef tool fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    classDef core fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef ext  fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff

    class S,C tool
    class SW core
    class ND ext
```

Running it against **itself**, in two shells, is the first thing to try: if that does not connect, the problem is
local and not the peer.

## The windows, and which to pick

```mermaid
flowchart TB
    MENU["Interactive menu"]
    XOTW["XOT Test<br/>send and receive raw packets"]
    SINC["SINTRAN Client<br/>full HOST, client AND server"]
    SPAD["SINTRAN PAD Client<br/>over LAPB"]
    XPAD["X.25 PAD Client<br/>pure X.25, NO LAPB"]
    X3W["X.3 Parameters"]

    MENU --> XOTW
    MENU --> SINC
    MENU --> SPAD
    MENU --> XPAD
    MENU --> X3W

    classDef app  fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    classDef core fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef link fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff

    class MENU app
    class XOTW,X3W app
    class SINC core
    class SPAD link
    class XPAD core
```

**The LAPB distinction is the one to get right.** The SINTRAN PAD client runs over LAPB; the X.25 PAD client does
not, because RFC 1613 XOT puts the packet straight onto TCP. Pointing the wrong one at a peer produces a link that
looks dead for no visible reason.

---

The sections below are the detailed reference: every command-line parameter, every window, and the client/server state machines.

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
x25xot --mode server --port <port> --machine-id <id>

# Client Mode (connect to remote host)
x25xot --mode client --host <hostname> --port <port> --machine-id <local-id> --remote-id <remote-id>

# Interactive Mode (show menu)
x25xot
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
x25xot --mode server --port 1998 --machine-id 100

# Connect to SINTRAN server at localhost:1998 as machine ID 200
x25xot --mode client --host localhost --port 1998 --machine-id 200 --remote-id 100

# Start SINTRAN PAD client as machine ID 200
x25xot --mode sintran-pad --machine-id 200

# Start pure X.25 PAD client (no SINTRAN)
x25xot --mode x25-pad

# Show interactive menu
x25xot
```

---

## Usage Instructions

### Running as SINTRAN Server

1. **Start the server**:
   ```bash
   x25xot --mode server --port 1998 --machine-id 100
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
   x25xot --mode client --host localhost --port 1998 --machine-id 200 --remote-id 100
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

    style UI fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    style XOT_C fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff
    style LAPB_C fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff
    style X25_C fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    style ROUT_C fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    style PAD_C fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
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

    style UI_S fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    style XOT_S fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff
    style LAPB_S fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff
    style X25_S fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    style ROUT_S fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    style PAD_S fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
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

    style TCP fill:#3F51B5,stroke:#283593,stroke-width:2px,color:#fff
    style XOT fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff
    style LAPB fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff
    style X25 fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    style SINTRAN fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff
    style APP fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
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

    style M1 fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    style M2 fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    style PID fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    style DATA fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff
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

    style START fill:#3F51B5,stroke:#283593,stroke-width:2px,color:#fff
    style CONNECTED fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff
    style DISCONNECT fill:#F44336,stroke:#C62828,stroke-width:2px,color:#fff
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

    style START_S fill:#3F51B5,stroke:#283593,stroke-width:2px,color:#fff
    style LISTEN fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    style CONNECTED_S fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff
    style STOP fill:#F44336,stroke:#C62828,stroke-width:2px,color:#fff
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
RetroX25.Xot/
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
# The usual way - build.ps1 builds, tests and publishes every app in this repo
# from the repository root
.\build.ps1

# Or just this project
dotnet build src\RetroX25.Xot\RetroX25.Xot.csproj -nodeReuse:false
```

### Run

```bash
# The published single-file executable, after build.ps1:
#   publish\win-x64\x25xot.exe
cd src\RetroX25.Xot\bin\Debug\net9.0
x25xot [arguments]
```

### Development

- **Framework**: .NET 9.0
- **UI Library**: Terminal.Gui
- **Logging**: Microsoft.Extensions.Logging
- **Architecture**: Event-driven, layered protocol stack
