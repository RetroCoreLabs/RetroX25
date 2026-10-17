# X.25 C# demo network

A self-contained demo of three chained C# switches with an echo service, so you can
place a call end-to-end, watch it in the Avalonia monitor GUI, and drive the
management CLIs.

## Topology

```
  PAD ──▶ Switch A ──trunk──▶ Switch B ──trunk──▶ Switch C ──▶ echo (23520001)
          DNIC 2350           DNIC 2351           DNIC 2352
          access 2200         trunk-in 2201       trunk-in 2202
          mgmt  9101          mgmt  9102          access  2203
          mon   4001          mon   4002          mgmt  9103 / mon 4003
```

A call to `23520001` placed at the PAD routes A → B → C and out to the echo
service, which sends the data straight back.

## Run it

From the repo root:

**Windows / PowerShell**
```powershell
./src/demo/demo.ps1 start      # build + start all three switches and the echo
./src/demo/demo.ps1 pad        # PAD client -> at the * prompt type:  CALL 23520001
./src/demo/demo.ps1 gui        # Avalonia monitor GUI
./src/demo/demo.ps1 stop
```

**Git Bash / WSL (Windows dotnet on PATH)**
```bash
./src/demo/demo.sh start
./src/demo/demo.sh pad
./src/demo/demo.sh gui
./src/demo/demo.sh stop
```

If `start` stops with `could NOT listen on 0.0.0.0:22xx` and "An attempt was made to access
a socket in a way forbidden by its access permissions", Windows has reserved that port for
something else - Hyper-V and WSL take blocks of ports, and which blocks changes between
reboots. List the reserved ranges with
`netsh interface ipv4 show excludedportrange protocol=tcp`; if 2200-2210 is inside one, move
the demo's ports in the three `switch-*.ini` files and the echo/PAD lines of the script.

In the GUI, add/select a switch endpoint — `127.0.0.1` port `4001` (A), `4002` (B),
or `4003` (C) — and Connect. The **Packet Analyzer** shows live decoded packets;
**Link Status** shows per-port counters. Place a call from the PAD and watch it
flow across the switches.

Management consoles (telnet): `demo.ps1 cli-a` / `cli-b` / `cli-c`
(ports 9101 / 9102 / 9103). Useful commands: `show x25 route`, `show x25 vc`,
`show x25 port`, `show x25 stats`.

## Cross-environment trunk to the reference (WSL) network

The C# switches use DNICs `2350/2351/2352`, deliberately distinct from the reference
network's `2340/2341/2342`, so the two can be trunked without address collision.

Switch A already **listens** for the reference network's trunk on `0.0.0.0:2210`
(`switch-a.ini` `[port.2]`) and routes the reference DNIC block `234x` out it. The
reference (WSL) side **dials in** to this host. Its trunk targets this machine's LAN
address, whose **default is `192.168.1.180`** — because the C# side binds `0.0.0.0`,
there is nothing to change here; the address is configured on the reference side (see
that repo's `demo/CROSSENV-CSHARP.md`). Just start the C# demo first and allow inbound
TCP `2210` through the firewall.

The routes that carry calls across that trunk are the `[route.0]` comments in
`switch-a.ini`. The live interop tests that drive this trunk from the C# side are the
`CrossEnvLive` tests in the RetroIO repository (`RetroIO.Protocols.X25.Tests`), started by
the `crossenv-test.ps1` script kept beside them in its `src/tests/demo` folder.
