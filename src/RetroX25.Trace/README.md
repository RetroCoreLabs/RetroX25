# x25trace — the wire tracer

<!-- A still screenshot of RetroX25.Trace doing its job. PNG, under 10 MB. Uncomment when
     the capture exists; keep the alt text, it is all a screen reader gets.
![RetroX25.Trace in use.](../../docs/images/readme/RetroX25.Trace.png)
-->

## What it is

A passive packet decoder. It sits on an XOT connection, reads whatever crosses it, and prints each X.25 packet
decoded into readable fields — packet type, logical channel, addresses, facilities, sequence numbers, causes and
diagnostics. With more verbosity it prints the hex as well.

## Why you need it

When a call fails, the interesting information is in a packet nobody looked at. A clear carries a **cause** and a
**diagnostic**; a refused call says why; a circuit that resets says what reset it. Those bytes are the difference
between "it does not work" and "the called address is not obtainable, diagnostic 67".

It is also the only honest way to settle an interoperability argument. If a Cisco router, a SINTRAN machine or the
C reference implementation disagrees with this one, the question is which octets went past — not which program's
log is more convincing.

## What you can use it for

- **Diagnosing a refused or cleared call** — read the cause and diagnostic rather than guessing.
- Checking XOT interop: confirm the header and the packet are what RFC 1613 asks for.
- Watching a handshake in order: restart, call set-up, accept, data, clear.
- **Verifying facility negotiation** — packet and window size, reverse charging and the rest are in the call
  packets and this prints them.
- Confirming whether a stall is a **window** problem or a lost packet, from the sequence numbers and RR/RNR.
- Capturing evidence for a bug report that is not an opinion.

## Prerequisites

| # | Requirement | Why | How to check |
|---|---|---|---|
| P1 | .NET SDK as pinned in `global.json`, or the published exe | To build or run | `dotnet --info` |
| P2 | **Traffic to look at** | It decodes; it does not generate | Run `x25pad` or `x25traffic` alongside |
| P3 | Either a port to dial or one to listen on | It must be in the path | `-x` to dial, `-l` to be dialled |
| P4 | A spare switch port, or a peer willing to dial it | It is an **endpoint**, not a tap | Add a port to the switch INI |

```
x25trace [-x host:port] [-l port] [-v]
```

| Option | Meaning |
|---|---|
| `-x host:port` | Dial an XOT peer and decode what arrives |
| `-l port` | Listen for an incoming XOT connection instead |
| `-v` | More verbosity; **repeat for hex dumps** |

```powershell
x25trace -l 2299                        # point a trunk at it
x25trace -x 127.0.0.1:2200 -v -v        # dial, with hex
```

> **An important limit, said plainly: this is an endpoint, not a packet sniffer.** It decodes traffic on a
> connection it is party to. To see traffic between two *other* programs, put it between them — give the switch a
> port that dials it, or have one side dial it. For genuine passive capture of somebody else's socket, use
> Wireshark on the TCP port and decode the XOT payload.

## Where to put it

```mermaid
flowchart LR
    subgraph A["Option 1: trace dials the switch"]
        T1["x25trace -x host:2200"]
        S1["x25switch"]
        T1 <--> S1
    end

    subgraph B["Option 2: trace is dialled"]
        S2["x25switch<br/>a port configured as DTE"]
        T2["x25trace -l 2299"]
        S2 <--> T2
    end

    classDef core fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef tool fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff

    class S1,S2 core
    class T1,T2 tool
```

## What it does with each packet

```mermaid
flowchart TB
    SOCK["Octets arrive on the TCP connection"]
    FRAME["XOT framing:<br/>strip version and length"]
    PKT["One X.25 packet"]
    TYPE{"Packet type?"}
    CALL["Decode addresses<br/>and facilities"]
    DATA["Decode P(S), P(R),<br/>M and Q bits"]
    CTRL["Decode cause<br/>and diagnostic"]
    PRINT["Print the decoded line<br/>(and hex, with -v -v)"]

    SOCK --> FRAME --> PKT --> TYPE
    TYPE -->|"CALL / ACCEPT"| CALL
    TYPE -->|"DATA"| DATA
    TYPE -->|"CLEAR / RESET / RESTART"| CTRL
    CALL --> PRINT
    DATA --> PRINT
    CTRL --> PRINT

    classDef sock  fill:#3F51B5,stroke:#283593,stroke-width:2px,color:#fff
    classDef frame fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    classDef core  fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef ok    fill:#4CAF50,stroke:#2E7D32,stroke-width:2px,color:#fff
    classDef bad   fill:#F44336,stroke:#C62828,stroke-width:2px,color:#fff

    class SOCK sock
    class FRAME frame
    class PKT,TYPE,PRINT core
    class CALL,DATA ok
    class CTRL bad
```

## Related

- `x25monitor` ([README](../RetroX25.Monitor.Desktop/README.md)) is the graphical equivalent, and it works
  differently in a way that matters: it connects to a switch's **monitor port** and receives frames the switch
  decoded, so it can watch a switch without being in the data path. `x25trace` must be in the path.
- The decode itself reuses the same parser as the stack, in
  `RetroIO.X25` (`RetroIO/src/RetroIO.X25/README.md`), so what it prints cannot drift from what the stack
  parsed.
