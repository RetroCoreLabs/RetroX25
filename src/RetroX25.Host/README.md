# x25host — fake machines on the network

<!-- A still screenshot of RetroX25.Host doing its job. PNG, under 10 MB. Uncomment when
     the capture exists; keep the alt text, it is all a screen reader gets.
![RetroX25.Host in use.](../docs/images/readme/RetroX25.Host.png)
-->

## What it is

A program that answers X.25 calls and pretends to be a machine or a service of the time: a VAX with a VMS login, a
TOPS-20, a SINTRAN, a BBS with a menu. What each machine shows is written in a **host folder** (an ini file and some
text files), never in code. Nothing in a host folder runs, and nothing a caller types is run: it is only compared
with the words in the file. The format is described in full in [the host format](../docs/x25host-format.md).

The same host folders are read by the C `x25host` in x25emu. The cross test
`tests/RetroX25.Tests/Interop/X25hostCrossTests.cs` checks that a caller gets the same bytes from both programs on
every folder in `demo/hosts`.

## Why you need it

A network with only an echo service at the far end shows that calls get through, not what using one was like. With
`x25host` a demo network has machines to call, log in to and look around on, and many of them can run in one
process.

## What you can use it for

- **Filling a demo network with machines**: one machine list names every machine, its type folder, its address and
  its switch port (`demo/x75-countries/machines.ini`, `demo/large-network`).
- **A known target for a PAD**: a login with echo turned off for the password (X.29), menus, line ends, idle
  clearing.
- **Several users on one machine**: `who`, `send`, `wall` and chat rooms go between the callers of one machine.
- **JavaScript machines** (C# only): `type = script` in the machine list runs a script under hard limits.

## Prerequisites

| # | Requirement | Why | How to check |
|---|---|---|---|
| P1 | .NET SDK as pinned in `global.json`, or the published exe | To build or run | `dotnet --info` |
| P2 | **A running switch** | It dials **out** to a switch port and waits for calls | `.\src\demo\demo.ps1 start` |
| P3 | **A route to its address** on that switch | Otherwise no call reaches it | A `[route.N]` entry in the switch INI |
| P4 | A host folder, or a machine list | It says what the machine shows | `src/demo/hosts` holds seventeen |

```
x25host --machines <list.ini> [--group <name>] [-v]
x25host <host-folder> -x <switch-host:port> [-a <address>] [-n <hostname>] [-v]
```

| Option | Meaning |
|---|---|
| `--machines file` | The machine list: one `[machine.N]` per machine (type, nua, switch, hostname) |
| `--group name` | Run only the machines with `group = name` |
| `-x host:port` | One host folder as one machine, on this switch line (XOT) |
| `-a address` | The X.121 address it answers (default: `[host] address` in host.ini) |
| `-n hostname` | Its name (default: `[host] hostname`, else the folder name) |
| `--console-port N` | The operator console over telnet on port N; the logins are `[operator.N]` in the list |
| `--console-bind a` | The address the console listens on (default 127.0.0.1) |
| `--max-memory-mb N` | The whole process may use at most N MB (Windows job object) |
| `--max-cpu-percent N` | At most N percent of the computer's CPU (Windows job object) |
| `--stats-log` | Each machine's counters (calls, bytes, packets, wrong logins) to the log once a minute |
| `-v` | More output; repeat for more |

```powershell
x25host src\demo\hosts\vax -x 127.0.0.1:2006 -a 23520005 -n TOMVAX
```

> **Note the direction.** Like `x25echo`, it **dials the switch** and then waits. It keeps redialling until the
> switch answers, and again after the switch restarts.

Built on `RetroIO.Protocols.X25.Host` (the engines `DescribedHost` and `ScriptedHost`) and
`RetroIO.Protocols.X25.Client`.
