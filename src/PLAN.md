# RetroX25 plan - outstanding work only

**Next:** push the commits (A6) after Ronny has looked at them.

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

### A6. Housekeeping
- The work is committed by topic in both repositories and not pushed. RetroX25 pushes from WSL (Windows has no
  GitHub SSH key).

### A7. x25emu answers only some calls that start in the same instant (report to its owner)
Seen from our side, nothing to fix here. Ten calls started at the same instant through its XOT trunk toward us:
6 and 10 answered in two runs (it gives two connections the same channel number); against one fresh x25emu echo, 2 of 2,
3 of 4 and 5 of 8 answered. Calls 2 seconds apart all work.
- Prove (when it is fixed there): the same tests report 10 of 10 and 8 of 8.
