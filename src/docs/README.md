# The guides

One line for each guide in this folder. Each program also has its own page next to its code (see the
main `README.md` at the top of the repository); these guides cover things that are bigger than one program.

## How the programs fit together

| Guide | What it says |
|---|---|
| [tools.md](tools.md) | What each of the eleven programs is for. |
| [x25-network.md](x25-network.md) | How a country X.25 network works and which program plays each part of it. |

## Machines to call

| Guide | What it says |
|---|---|
| [x25host-format.md](x25host-format.md) | The host folder format of `x25host`: banner, logins, commands, menus, scripts, users on one machine, the operator console. |
| [research-os.md](research-os.md) | What the machines, networks and BBSes of the 1970s-1990s showed a caller, with sources; the host library is built from it. |
| [atlas/README.md](atlas/README.md) | The X.25 Network Atlas: the old public X.25 networks and their hosts with real numbers and sources; the large demo network takes its host numbers from it. |

## Links between switches and routers

| Guide | What it says |
|---|---|
| [trunk-links.md](trunk-links.md) | Trunks between switches: `hdlc-tcp` and `hdlc-serial`, with XOT as the extra, and every key that sets them up. |
| [xot-peers.md](xot-peers.md) | How XOT peers work: dialling out, accepting in, the source address, and what NAT does. |
| [modulo-128.md](modulo-128.md) | Why modulo 128 does not work at either layer, and how Cisco uses it. |

## Billing

| Guide | What it says |
|---|---|
| [billing.md](billing.md) | Accounts, NUIs and call records in both switches: what X.25 says, the settings, the PAD login, segments, and the record file column by column. |

## Using the Desktop app

| Guide | What it says |
|---|---|
| [telnet-pad.md](telnet-pad.md) | How to use a PAD from a normal telnet program through the Desktop app. |
| [altos-chat.md](altos-chat.md) | The Altos CHAT 3.1 conference program as a built-in service: its address and settings, and running it behind a switch port. |

## Testing against other equipment

| Guide | What it says |
|---|---|
| [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) | A full bench of two real Cisco IOS routers in GNS3 against our switch, with every step and expected output. |
| [cisco-ios-x25-options.md](cisco-ios-x25-options.md) | Ways to set up real Cisco routers (25xx and similar) against RetroX25, and how much of each was tested. |
| [cisco-learnings.md](cisco-learnings.md) | One page with everything measured about the Cisco router: facts, cause codes, commands. |
| [crossenv-testing.md](crossenv-testing.md) | Testing against the C emulator `x25emu` in WSL: the network, the addresses, and what the tests prove. |

## Other files

- [config-samples](https://github.com/RetroCoreLabs/RetroX25/tree/main/src/docs/config-samples): sample configuration files for the Desktop app and the routing sample.
- [images](https://github.com/RetroCoreLabs/RetroX25/tree/main/src/docs/images): pictures used by these guides.
