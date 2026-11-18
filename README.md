# RetroX25

**An X.25 network you can run.** Ten applications — a packet switch, two desktop front ends, six console
tools and a service host — built on the `RetroIO.Protocols.X25.*` packages.

[![build](https://github.com/RetroCoreLabs/RetroX25/actions/workflows/build.yml/badge.svg)](https://github.com/RetroCoreLabs/RetroX25/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/RetroCoreLabs/RetroX25)](https://github.com/RetroCoreLabs/RetroX25/releases)
[![licence](https://img.shields.io/github/license/RetroCoreLabs/RetroX25)](LICENSE)
[![downloads](https://img.shields.io/github/downloads/RetroCoreLabs/RetroX25/total)](https://github.com/RetroCoreLabs/RetroX25/releases)
[Applications](#the-ten-applications) · [Prerequisites](#prerequisites) · [The demo network](#the-demo-network) · [About X.25](#about-x25) · [API reference](https://retrocorelabs.github.io/RetroX25/)

<!-- A 5-15 second recording: the switch running, a call placed from x25pad, the packets
     appearing in x25monitor. Under 10 MB. Uncomment when the capture exists.
![The x25switch console, a call placed from x25pad, and the packets appearing live in x25monitor.](src/docs/images/readme/demo.gif)
-->
## How this code was written

Some of the code in this repository was written with the help of AI, mostly Claude (Anthropic).
Ronny Hansen directs the work, tests it against real hardware and emulators, and is responsible for
what is in the repository. We say so plainly so you know what you are using.

## Why this repo exists

X.25 is how computers talked to each other before the internet won, and a great deal of surviving equipment still
speaks it: Norsk Data machines, Cisco routers, terminal servers, banking and telex gear. To reach any of it you
need a switch, something to call with, something to answer, and a way to see what actually crossed the wire.

This repository is those things. The protocol work lives in [RetroIO](https://github.com/RetroCoreLabs/RetroIO)
and is consumed here as NuGet packages — so the same packet layer runs in the switch, the desktop app and every
tool, and a fix reaches all of them at once.

It is also one half of a **two-implementation** arrangement. A C emulator (`x25emu`) implements the same
protocols, the two are trunked together, and both read the same X.25 conformance vectors. A disagreement between
them is a failing test rather than an argument.

## The ten applications

| Application | What it is | README |
|---|---|---|
| `x25switch` | The packet switch. Everything else talks to one | [README](src/RetroX25.Switch/README.md) |
| `RetroX25.exe` | Desktop app: PAD client, packet view, routing, configuration | [README](src/RetroX25.Desktop/README.md) |
| `x25monitor` | Desktop network monitor: map, packet analyser, link status | [README](src/RetroX25.Monitor.Desktop/README.md) |
| `x25pad` | Interactive terminal client — places calls | [README](src/RetroX25.Pad/README.md) |
| `x25echo` | Answers calls and echoes data back | [README](src/RetroX25.Echo/README.md) |
| `x25trace` | Wire tracer: connect or listen, decode, hex dump | [README](src/RetroX25.Trace/README.md) |
| `x25traffic` | Load generator: streams and call storms | [README](src/RetroX25.Traffic/README.md) |
| `x25nmc` | Network management console, in-band over X.25 | [README](src/RetroX25.Nmc/README.md) |
| `x25xot` | XOT client and server, interactive | [README](src/RetroX25.Xot/README.md) |
| `x25service` | The X.25 runtime with no window: runs a service (the Altos chat) from a JSON configuration | [README](src/RetroX25.Service/README.md) |

The guides that cover more than one program (trunks, XOT peers, Cisco testing, the telnet PAD) are listed in [src/docs/README.md](src/docs/README.md).

---

## Status

No tagged release yet, so the release and download badges above are empty until the first
`v*` tag is pushed. The ten applications are built and run.

- **The whole solution builds in Release with no errors**, and four test projects cover it (see [Tests](#tests)).
- **Two implementations, one set of vectors.** The C emulator `x25emu` implements the same
  protocols, the two are trunked together, and both read the same X.25 conformance vectors.
- **The six-switch demonstration network runs**, and a trunk whose peer does not answer is
  dialled again every 2 seconds (see [trunk-links.md](src/docs/trunk-links.md)). No start-up time is claimed.
- **Not yet measured:** call setup rate and throughput under load. `x25traffic` exists to
  measure them; no number is claimed here because none has been taken.

---

## Tested against real Cisco software

The switch, the PAD and the echo and TAD services have been tested against **Cisco routers over XOT** (X.25
carried inside TCP, RFC 1613). Calls were placed in both directions, through our switch, and the data came back
unchanged.

**Read this carefully: what was and was not tested.**

- **Tested:** two Cisco 7200 routers running **real Cisco IOS 12.4(24)T5**, started in the GNS3 router emulator
  (Dynamips). Their X.25 and XOT behaviour is the real IOS code.
- **Not tested yet:** a physical Cisco box (a 2500 series, for example). The router hardware here is emulated.
- A Cisco `pad` call to our echo service works. A Cisco `pad 23430200` call to our **TAD server** (banner, `HELP`,
  echo, `QUIT`) works too, checked on the real IOS image and again with the Desktop app running alone. Calls from `x25pad` and from
  the Desktop app to a Cisco router, and through it to a second router on a serial cable, work. A call storm of
  30 calls and a stream of 100 000 data packets (128 bytes each, all returned) ran through the switch.

```mermaid
flowchart LR
    subgraph PC["Windows PC: RetroX25"]
        PAD["x25pad or RetroX25.exe"]
        SW["x25switch"]
        ECHO["x25echo"]
        TAD["TAD server<br/>(in RetroX25.exe)"]
        PAD --> SW
        ECHO --> SW
        TAD --> SW
    end
    subgraph LAP["Linux laptop: GNS3"]
        R1["R1 Cisco 7200<br/>real IOS"]
        R2["R2 Cisco 7200<br/>real IOS"]
        R1 ---|"serial cable"| R2
    end
    SW <-->|"XOT, TCP port 1998"| R1

    classDef app fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    classDef core fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef svc fill:#9C27B0,stroke:#7B1FA2,stroke-width:2px,color:#fff
    classDef ext fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff
    class PAD app
    class SW core
    class ECHO,TAD svc
    class R1,R2 ext
```

**Build the same bench yourself:** [Testing RetroX25 against real Cisco routers](src/docs/cisco-gns3-xot-test.md)
starts with five pictures (the machines and cables, a call in each direction, the Cisco calling our TAD server, and the Desktop app alone), then gives
the GNS3 project, both complete router configurations, the switch file, every test step with its expected output,
and a troubleshooting list. The Desktop-only variant uses the sample configuration
[x25config-embedded-switch.json](src/docs/config-samples/x25config-embedded-switch.json).

---
## Prerequisites

| # | Requirement | Needed for | How to check |
|---|---|---|---|
| P1 | .NET SDK as pinned in `src/global.json` | Everything | `dotnet --info` |
| P2 | PowerShell 7+ | `build.ps1`, `demo.ps1` | `$PSVersionTable.PSVersion` |
| P3 | **RetroIO built and packed to the local feed** (`../localfeed`, beside the repositories) | Restore | `dir ..\localfeed` shows `0.1.0-local.*` |
| P4 | `.\src\build.ps1` run | Testing the published exes | `dir src\publish\win-x64\*.exe` lists ten |
| P5 | TCP ports 2200-2203, 2210, 9101-9103, 4001-4003 free, and not reserved by Windows | The demo network | `netstat -ano \| findstr "2200 2201 9101 4001"`; reserved ranges: see [the demo README](src/demo/README.md) |
| P6 | A Windows desktop session | `RetroX25.exe`, `x25monitor` | Two of the ten are Avalonia apps |
| P7 | A terminal | The console tools | Any modern terminal |
| P8 | WSL with `x25emu` built | Cross-environment tests only | `wsl -e bash -c 'ls <x25emu folder>/tools/x25switch'` |
| P9 | Inbound TCP 2210 allowed | Cross-environment tests only | The WSL side dials **in** |
| P10 | A real ND machine and the retroterm MCP | Hardware tests only | `terminal_connlist` |

Until the RetroIO packages are on nuget.org they come from the local feed, so **a fresh CI run is red until the
first publish**. That is expected, not a broken build.

```powershell
# from the repository root
.\src\build.ps1                 # build, test, publish all ten as single self-contained files
.\src\demo\demo.ps1 start       # three chained switches and an echo service
.\src\demo\demo.ps1 pad         # a PAD client; at the * prompt:  CALL 23520001
.\src\demo\demo.ps1 stop        # always stop with the script, never by process name
```

Git Bash or WSL: the same verbs on `./src/demo/demo.sh`.

---

## How the applications reach the network

Every one of them is a client of a switch. None has a private channel into one — which is why a routing fault
breaks monitoring and management too, and why that is a feature: the monitoring path is itself a proof that
routing works.

```mermaid
flowchart TB
    subgraph GUI["Avalonia desktop"]
        RX["RetroX25.exe"]
        MON["x25monitor"]
    end

    subgraph CLI["Console tools"]
        PAD["x25pad"]
        TRAF["x25traffic"]
        TRACE["x25trace"]
        NMC["x25nmc"]
        XOT["x25xot"]
    end

    SW["x25switch"]
    ECHO["x25echo"]
    TELNET["Management port<br/>telnet 9101-9103"]
    MONPORT["Monitor port<br/>4001-4003"]
    REF["The C reference network<br/>over a trunk"]

    PAD --> SW
    TRAF --> SW
    TRACE --> SW
    XOT --> SW
    RX --> SW
    NMC -->|"X.25 call to the mgmt address"| SW
    MON --> MONPORT
    MONPORT --- SW
    TELNET --- SW
    SW --> ECHO
    SW <--> REF

    classDef gui  fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    classDef cli  fill:#3F51B5,stroke:#283593,stroke-width:2px,color:#fff
    classDef core fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef svc  fill:#9C27B0,stroke:#7B1FA2,stroke-width:2px,color:#fff
    classDef port fill:#FFA726,stroke:#F57C00,stroke-width:2px,color:#000
    classDef ext  fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff

    class RX,MON gui
    class PAD,TRAF,TRACE,NMC,XOT cli
    class SW core
    class ECHO svc
    class TELNET,MONPORT port
    class REF ext
```

**Two different management paths, and confusing them wastes time.** `x25nmc` goes **in-band** — a real X.25 call
to the switch's management address, routed like any other call. `demo.ps1 cli-a` opens a **telnet** session on
port 9101. The interesting test is `x25nmc` reaching switches B and C *through* A.

---

## The demo network

`src\demo\demo.ps1` starts three chained switches and an echo service.

```mermaid
flowchart LR
    PAD["x25pad"]
    A["Switch A<br/>DNIC 2350<br/>access 2200, trunk-in 2210<br/>mgmt 9101, mon 4001"]
    B["Switch B<br/>DNIC 2351<br/>trunk-in 2201<br/>mgmt 9102, mon 4002"]
    C["Switch C<br/>DNIC 2352<br/>trunk-in 2202, access 2203<br/>mgmt 9103, mon 4003"]
    ECHO["x25echo<br/>answers 23520001"]

    PAD -->|"XOT 2200"| A
    A -->|"trunk 2201"| B
    B -->|"trunk 2202"| C
    C --> ECHO

    classDef access fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    classDef core   fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef svc    fill:#9C27B0,stroke:#7B1FA2,stroke-width:2px,color:#fff

    class PAD access
    class A,B,C core
    class ECHO svc
```

| Switch | DNIC | Access | Trunk-in | Management (telnet) | Monitor | Management address |
|---|---|---|---|---|---|---|
| A | 2350 | 2200 | 2210 (from WSL) | 9101 | 4001 | 23500000 |
| B | 2351 | — | 2201 | 9102 | 4002 | 23510000 |
| C | 2352 | 2203 | 2202 | 9103 | 4003 | 23520000 |

Echo answers on **23520001**. Management login on every switch: `admin` / `admin`.

**A call from the PAD to `23520001` must cross all three switches**, so `show x25 vc` on *every* switch is the
proof it routed rather than short-circuited.

### Verbs

| Command | What it does |
|---|---|
| `.\src\demo\demo.ps1 start` | Build and start three switches plus the echo |
| `.\src\demo\demo.ps1 pad` | A PAD client |
| `.\src\demo\demo.ps1 gui` | The monitor GUI |
| `.\src\demo\demo.ps1 cli-a` / `cli-b` / `cli-c` | Telnet to a switch's management console |
| `.\src\demo\demo.ps1 stop` | Stop everything |
| `.\src\demo\crossenv-test.ps1` | The automated interop check against the C reference |

`stop` reads and writes `src\demo\.pids`. **Always stop with the script**; if you must kill something, kill the PID
in that file — never by process name.

---

## The three-country network (X.75)

`src\demo\x75-countries\x75.ps1` starts three national networks joined by X.75 international links, as
1980s public data networks were: **Norway** (DNIC 2422, the PAD users at the University of Tromsø), **England**
(PSS, 2342) and **Germany** (Datex-P, 2624, with the Altos CHAT conference at `026245890040004`). Everything is
in ini and JSON files in that folder; the script only starts and stops the published programs, so the network
can stay up for testing by hand.

```mermaid
flowchart LR
    PAD["x25pad / RetroX25.exe<br/>PAD users, NUI required"]
    NO["Norway 2422<br/>PAD line 25001<br/>mgmt 9201"]
    EN["England 2342<br/>mgmt 9202"]
    DE["Germany 2624<br/>mgmt 9203"]
    CHAT["x25service<br/>Altos CHAT 26245890040004"]
    E1["echo 24229001"]
    E2["echo 23429001"]
    E3["echo 26249001"]

    PAD -->|"XOT"| NO
    NO <-->|"X.75"| EN
    EN <-->|"X.75"| DE
    DE --> CHAT
    NO --> E1
    EN --> E2
    DE --> E3

    classDef access fill:#2196F3,stroke:#1565C0,stroke-width:2px,color:#fff
    classDef core   fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef svc    fill:#9C27B0,stroke:#7B1FA2,stroke-width:2px,color:#fff
    class PAD access
    class NO,EN,DE core
    class CHAT,E1,E2,E3 svc
```

```powershell
# from the repository root, after .\src\build.ps1
cd src\demo\x75-countries
./x75.ps1 start          # the three switches, the chat and the echos
./x75.ps1 pad 1          # a PAD as test user uit-1 (NUI 242226500000101); then: CALL 026245890040004
./x75.ps1 status         # what runs, by PID
./x75.ps1 stop
```

- Calls abroad take the international prefix `0` (X.121): `026245890040004` is the chat in Germany.
- Every call from Norway must carry a NUI, the account that pays: four test users, `uit-1` to `uit-4`.
  Norway's console shows each account's calls (`show x25 account`, `show x25 invoice uit-1`).
- The desktop app can be one of the PAD users too: `RetroX25.exe desktop-norway.json`, then `ID<nui>` and the
  address in its PAD window.
- `x25service` is the X.25 runtime as a console program, with no window; here it carries the Altos chat.

[The folder's README](src/demo/x75-countries/README.md) has every address (NUA), every NUI, the management
commands, the call records, the ports, and a list of checks to do by hand. `X75CountriesScriptTests` start the
same files with the same script.

**Coming next: fake machines.** `x25host` (planned) answers calls as a described machine: a banner, a login, a
command line or a menu, from ini and text files and never from code. The format is
[x25host-format.md](src/docs/x25host-format.md); the texts of real systems of the time (VMS, TOPS-20, Unix, TSO,
VM/370, SINTRAN III, PRIMOS, ARPANET hosts, PADs, BBSes, the Altos chat) are collected with their sources in
[research-os.md](src/docs/research-os.md).

---

## Against the C reference implementation

The C emulator (the `x25emu` repository, checked out under WSL) runs on DNICs **2340/2341/2342** — chosen *not* to
collide with this network's 2350/2351/2352, so the two can be trunked.

```mermaid
flowchart LR
    subgraph CS["RetroX25 (Windows)"]
        CA["Switch A<br/>2350, listens on 2210"]
        CB["Switch B 2351"]
        CC["Switch C 2352"]
        CE["x25echo 23520001"]
        CA --- CB --- CC --> CE
    end

    subgraph REF["x25emu (WSL)"]
        RA["Switch A 2340"]
        RB["Switch B 2341"]
        RC["Switch C 2342"]
        RE["x25echo 23420001"]
        RA --- RB --- RC --> RE
    end

    RA -->|"dials in, TCP 2210"| CA

    classDef core fill:#009688,stroke:#00695C,stroke-width:2px,color:#fff
    classDef ext  fill:#E91E63,stroke:#AD1457,stroke-width:2px,color:#fff
    classDef svc  fill:#9C27B0,stroke:#7B1FA2,stroke-width:2px,color:#fff

    class CA,CB,CC core
    class RA,RB,RC ext
    class CE,RE svc
```

**The WSL side dials in**, so nothing changes on this side. Start this demo first and allow inbound TCP 2210.

```powershell
# the argument is the x25emu folder INSIDE WSL; nothing in it is changed
.\src\demo\crossenv-test.ps1 -X25emu <x25emu folder in WSL>
.\src\demo\crossenv-test.ps1 -X25emu <x25emu folder in WSL> -WslHost <address>   # if auto-detect fails
```

It finds the WSL address with `hostname -I` (which changes across reboots), starts the x25emu test network in WSL, runs
every test of category `Interop` against it (calls both ways over XOT and `hdlc-tcp`, data details, connection counts,
volume), and stops what it started. The guide, with the address table and what was measured, is
[crossenv-testing.md](src/docs/crossenv-testing.md).

Both directions matter: from our PAD, `CALL 23420001` should reach the reference echo; from the reference PAD,
`23520001` should reach ours. **A failure in only one direction is the interesting result** — record which.

### One file, two implementations

The X.25 conformance vectors in `RetroIO/src/tests/x25-vectors/x25-vectors.json` are read by **this stack and the C
one**. A disagreement about bytes on the wire is therefore a failing test on both sides rather than a mystery on
the trunk.

---

## Tests

| Project | Covers |
|---|---|
| `src/tests/RetroX25.Desktop.Tests` | The desktop app |
| `src/tests/RetroX25.Monitor.Desktop.Tests` | The monitor app |
| `src/tests/RetroX25.Tests` | The switch, trunks (XOT and HDLC), the console tools, and interop tests against the C reference `x25emu` (these need WSL, see above), and the serial-line tests (`SerialHdlcLoopbackTests`, which need two COM ports joined by an HDLC line and skip themselves without them) |
| `src/tests/RetroX25.Xot.Tests` | The `x25xot` tool: its predefined packets and an XOT loopback |

The protocol itself is tested in RetroIO — `src/tests/RetroIO.Protocols.X25.Tests`, including the shared vectors.
That is the right place for it: these are applications, and a protocol bug found here should become a test there.

Manual test procedures, including the hardware cases, are in
the migration notes, as `Manual tests - RetroX25.md` and `Manual tests - X25 multi-network.md`.

## About X.25

X.25 is how computers talked to each other over public networks before the internet won.
The ITU-T standardised it in 1976 and carriers ran it worldwide - Telenor, Transpac,
Datex-P, Tymnet - into the 2000s. A call is placed to an address, a virtual circuit is set
up across the network, data flows in numbered packets with flow control and error recovery,
and the circuit is cleared. Conceptually it is a telephone call for data.

It matters now because a great deal of surviving equipment still speaks nothing else: Norsk
Data minicomputers, Cisco routers, terminal servers, banking and telex gear. The carriers are
gone, so to reach any of that you have to provide the network yourself - a switch, something
to call with, something to answer, and a way to see what crossed the wire. That is what this
repository is.

The layers, briefly: **LAPB** is the link layer over a serial line; **XOT** (RFC 1613)
carries X.25 over TCP so two sites can be joined across the modern internet; **PAD** (X.3,
X.28, X.29) is the terminal interface a human uses to place a call. The specifications are
ITU-T X.25, X.75 and X.121.

---
## Conventions

- Names follow the workspace naming standard; diagrams follow the workspace Mermaid palette. Both live one level
  up in the workspace root, **outside this repository**, so they are paths rather than links: `NAMING.md` and
  `MERMAID_COLOR_STANDARDS.md`.
- `python src/eng/check-docs.py` checks every link and diagram in this repo.
- The org is **RetroCoreLabs**. `HackerCorpLabs` is retired and must not appear in anything new.

## Contributing

See [CONTRIBUTING.md](.github/CONTRIBUTING.md) for how to get a build running, what the code is
expected to look like, and what will get a change sent back. A security problem goes through
[SECURITY.md](.github/SECURITY.md) and never a public issue.

## Licence

MIT. See [LICENSE](LICENSE).