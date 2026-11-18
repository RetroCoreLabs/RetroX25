# The programs in this repository and what each one is for

RetroX25 builds nine programs. Together they let you build a small X.25 network on one computer (or
several), put traffic through it, watch it and take it apart again. This page says what each program is.
How they fit together as a network is in [A country X.25 network and how these programs simulate it](x25-network.md).

Every program has its own page with every setting and a screenshot slot. This page only says what the
program is for, so it stays true when a setting changes.

## The programs

| Program | Project folder | What it is | Role in an X.25 network |
|---|---|---|---|
| `x25switch` | [RetroX25.Switch](../RetroX25.Switch/README.md) | A packet switch. It takes calls in on its ports, looks at the called address, picks the port the address routes to and joins the two calls. It also has a telnet management console and a monitor port. | The **switch** (PSE). One program for each switch in the network. Its ports play the DCE role towards subscribers and the trunk role towards other switches. |
| `x25echo` | [RetroX25.Echo](../RetroX25.Echo/README.md) | A tiny service. It accepts every call routed to its address and sends every byte it receives straight back. | A **DTE**: a host with something to talk to. The simplest subscriber. |
| `x25traffic` | [RetroX25.Traffic](../RetroX25.Traffic/README.md) | A load generator. It places calls and sends numbered data, then reports what came back. It can send one long stream or open and clear calls as fast as you ask. | A **DTE** that sends a lot of data. It is a data generator and nothing else; it is not a PAD. |
| `x25pad` | [RetroX25.Pad](../RetroX25.Pad/README.md) | A PAD (Packet Assembler/Disassembler) on the console. You type `CALL <address>`, then what you type goes to the remote host and its answer is printed. It follows X.3 (the PAD settings), X.28 (the commands you type) and X.29 (the host changing those settings). It can also open a telnet port so an ordinary terminal program reaches the PAD. | The **PAD** with the **terminal** in front of it. |
| `RetroX25` (the desktop application) | [RetroX25.Desktop](../RetroX25.Desktop/README.md) | A window application that puts the stack behind a screen: a PAD window, a live packet view that decodes every packet, the routing table, the configuration, link health for the trunks and an XOT peer list. It has its own built-in server (TAD) to call and can run a built-in switch. | A **PAD** (the PAD window and the telnet PAD) and a **DTE** (the built-in TAD server); optionally a **switch**. |
| `x25monitor` | [RetroX25.Monitor.Desktop](../RetroX25.Monitor.Desktop/README.md) | A window that watches a whole network. It connects to the monitor port of each switch and shows a map, a live decoded packet view, the state of each port and the logs. | The **network operator's screen**. It is not a device in the drawing. |
| `x25nmc` | [RetroX25.Nmc](../RetroX25.Nmc/README.md) | A management console that reaches the switches through X.25 itself: it places a call to a switch's management address, signs in, asks for status and can move on to the neighbours it learns about. | The **network management centre**. |
| `x25trace` | [RetroX25.Trace](../RetroX25.Trace/README.md) | A passive decoder. It sits on an XOT connection, reads what crosses and prints every X.25 packet in readable fields (type, logical channel, addresses, facilities, causes). | A **line analyser** clipped onto a link. |
| `x25xot` | [RetroX25.Xot](../RetroX25.Xot/README.md) | A debug tool for XOT (X.25 over TCP, RFC 1613). It opens one XOT connection as client or server; you send single packets by hand (function keys or typed hex) and it shows each packet that comes back, decoded. Not a PAD; nothing depends on it. | A **protocol tester** clipped onto an XOT link, for hunting a fault. |

The demo network that starts three switches and an echo service is described in
[demo/README.md](../demo/README.md).

## Programs from outside this repository that it works with

| Program | What it is | Where it is described |
|---|---|---|
| `x25emu` tools (`x25switch`, `x25pad`, `x25echo`, `x25traffic`, `x25trace`, `x25nmc`) | A second, independent X.25 emulator written in C that runs in WSL. It is the other implementation that RetroX25 is tested against. | [crossenv-testing.md](crossenv-testing.md) |
| A Cisco router (IOS) | A real X.25 implementation. It acts as a DTE or a DCE, runs a PAD service and speaks XOT. | [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md), [xot-peers.md](xot-peers.md); what was learned, on one page: [cisco-learnings.md](cisco-learnings.md) |

## How the programs find each other

- A switch is the centre. Every other program connects **to** a switch, never to each other.
- A program reaches a switch over TCP. Two framings are used: **XOT** (RFC 1613, one TCP connection for each
  call, what a router speaks) and **HDLC/LAPB over TCP** (one connection carrying all calls, like a leased line).
  [xot-peers.md](xot-peers.md) and [trunk-links.md](trunk-links.md) say when each is used.
- The switch's ports, routes and addresses are in its configuration file. The demo files are in
  [demo/](../demo/README.md).
