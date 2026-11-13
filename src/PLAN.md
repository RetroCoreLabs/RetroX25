# RetroX25 plan - outstanding work only

**Next:** push the commits (A6), after Ronny has looked at them. Everything that needs the router waits for the router (it was stopped).

Rules for this file: finished items are deleted, not ticked. Each item says what to build, how it is proved,
and what it waits for. What was measured is in `docs/xot-peers.md`, `docs/trunk-links.md` and
`docs/crossenv-testing.md`.

---

## A. Things that are not right or not done

### A0. The real serial device is not tested
`hdlc-serial` is tested over an in-memory cable only. The development machine has no virtual serial pair and WSL has
no `socat` (checked 2026-11-13).
- Build: nothing. Needs a cable or a virtual pair (com0com on Windows, or `socat` in WSL).
- Prove: two switches linked over it, a call, data, unplugging and plugging back, and the baud/parity/flow settings.
  Then x25emu's `hdlc-serial` trunk against ours.
- Waits for: a cable or a virtual serial pair.

### A1. The Client mode is built; one proof is open
Client entries now open one TCP connection per call (RFC 1613 section 5), proved against x25emu (1, 2 and 10 calls give
1, 2 and 10 connections in `Get-NetTCPConnection`, in `ss` and in the transport counters). The client library
(`X25Client`) does the same, so `x25traffic -C -n 8` against x25emu's access port gets 8 of 8 answered (it was 0 of 8
with one connection). Neither was run against the real router: the router stopped before this was built.
- Prove (waits for the router): `ClientEntry_SimultaneousCalls_OpenOneConnectionEach` in `CiscoR1PeerTests`
  (explicit, needs the router) - both calls answered.

### A5. Cisco: what the router does with a connection from an unknown address
Everything else about the router is measured (see `docs/xot-peers.md`). This one is not: the connection must come
from an address other than this PC's, and no second address or second machine with a route to the router exists.
- Prove: run `CiscoR1MeasurementTests` from a second machine on the router's network and write the result into
  `docs/xot-peers.md`; decode the bytes in order.
- Waits for: the router and a second machine (or a second address) that reaches it. No router change is needed.

### A6. Housekeeping
- The work is committed by topic in both repositories and not pushed. RetroX25 pushes from WSL (Windows has no
  GitHub SSH key).

### A7. x25emu answers only some calls that start in the same instant (report to its owner)
Seen from our side, nothing to fix here. Ten calls started at the same instant through its XOT trunk toward us:
6 and 10 answered in two runs (it gives two connections the same channel number); against one fresh x25emu echo, 2 of 2,
3 of 4 and 5 of 8 answered. Calls 2 seconds apart all work.
- Prove (when it is fixed there): the same tests report 10 of 10 and 8 of 8.

---

## C. Router in the middle

### C2. x25emu, our switch and the router together
x25emu PAD to our switch to the router and back; the router calling x25emu through us. Same checks as the volume and
detail tests. Our app does not forward a call from one interface to another (a call that arrives on a peer interface
for an address routed back out of the same interface is cleared as "not routed"), so the path goes through
`x25switch`, not through the Desktop app.
- Waits for: the router (it was stopped; ask again before a new kind of test traffic to it).
