# RetroX25 plan - outstanding work only

**Next:** Ronny decides item A8 (the 64 KB limit on one message); push the commits (A6) after he has looked at them.

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

### A5. Cisco: what the router does with a connection from an unknown address
Everything else about the router is measured (see `docs/xot-peers.md`). This one is not: the connection must come
from an address other than this PC's. Checked 2026-11-13: a connection from WSL reaches the router with this PC's
address (Windows translates it), so WSL does not help.
- Prove: run `CiscoR1MeasurementTests` from a second source address, then decode the bytes in order and write the
  result into `docs/xot-peers.md`.
- Waits for: one of: a second address on this PC's network card, WSL in bridged or mirrored mode, or a second machine
  that reaches the router. No router change is needed.

### A6. Housekeeping
- The work is committed by topic in both repositories and not pushed. RetroX25 pushes from WSL (Windows has no
  GitHub SSH key).

### A7. x25emu answers only some calls that start in the same instant (report to its owner)
Seen from our side, nothing to fix here. Ten calls started at the same instant through its XOT trunk toward us:
6 and 10 answered in two runs (it gives two connections the same channel number); against one fresh x25emu echo, 2 of 2,
3 of 4 and 5 of 8 answered. Calls 2 seconds apart all work.
- Prove (when it is fixed there): the same tests report 10 of 10 and 8 of 8.

### A8. A long run of linked packets is cut at 64 KB
The router's PAD answers a fast stream with an endless run of full packets that all carry the "more data" mark.
`X25Vc.MaxReassembly` (RetroIO, 65536 bytes) makes our layer reset the circuit (cause 0, diagnostic 39) when one run
gets that long, so a long stream into a router PAD ends after about 19 seconds. X.25 sets no limit; this one is ours.
- Decide: keep the limit (and say so), raise it, or hand the data to the program in pieces when the limit is reached.
- Prove: `x25traffic -d 23600002 -u 01000000 -n 3000 -r 100 -s 64` through the Cisco switch file runs to 3000 packets.
