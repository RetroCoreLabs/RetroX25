# RetroX25

An X.25 network you can run. Eleven applications - a packet switch, two desktop front ends and
eight console tools - built on the `RetroIO.Protocols.X25.*` packages from
[RetroIO](https://retrocorelabs.github.io/RetroIO/).

The source, the build instructions and the releases are on
[GitHub](https://github.com/RetroCoreLabs/RetroX25).

## The X.25 Network Atlas

**[Open the X.25 Network Atlas](../docs/atlas/index.html)**: a map of the old public X.25 world, with the networks,
their hosts and their NUAs (network user addresses), and a search. It is one page that needs no server and
nothing from the internet, and it opens full-screen.

## Get started

```powershell
# from the repository root
.\src\build.ps1                  # builds, tests and publishes all eleven to src\publish\win-x64
.\src\demo\demo.ps1 start        # three chained switches and an echo service
.\src\demo\demo.ps1 pad          # at the * prompt:  CALL 23520001
```

## Where to look

 - **`x25switch`** (`RetroX25.Switch`): the packet switch. Everything else talks to one.
 - **`RetroX25.exe`** (`RetroX25.Desktop`): desktop app - PAD client, packet view, routing,
   configuration.
 - **`x25monitor`** (`RetroX25.Monitor.Desktop`): desktop network monitor - map, packet
   analyser, link status.
 - **`x25pad`**, **`x25echo`**, **`x25trace`**, **`x25traffic`** (`RetroX25.Pad`, `.Echo`,
   `.Trace`, `.Traffic`): place calls, answer them, trace the wire, generate load.
 - **`x25nmc`** (`RetroX25.Nmc`): network management console, in-band over X.25.
 - **`x25xot`** (`RetroX25.Xot`): text-mode XOT debug tool: send single packets by hand, as client or server, and see the answer.

These are applications, so this reference covers the types they expose to each other and to
their tests. The X.25 protocol itself - packet layer, switch engine, routing, client - is
documented with the `RetroIO.Protocols.X25.*` packages in the
[RetroIO API reference](https://retrocorelabs.github.io/RetroIO/).

See the [API reference](api/index.md) for every namespace, type and member.
