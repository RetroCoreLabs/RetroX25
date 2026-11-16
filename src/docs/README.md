# The guides

One line for each guide in this folder. Each program also has its own page next to its code (see the
main `README.md` at the top of the repository); these guides cover things that are bigger than one program.

## How the programs fit together

| Guide | What it says |
|---|---|
| [tools.md](tools.md) | What each of the nine programs is for. |
| [x25-network.md](x25-network.md) | How a country X.25 network works and which program plays each part of it. |

## Links between switches and routers

| Guide | What it says |
|---|---|
| [trunk-links.md](trunk-links.md) | Trunks between switches: `hdlc-tcp` and `hdlc-serial`, with XOT as the extra, and every key that sets them up. |
| [xot-peers.md](xot-peers.md) | How XOT peers work: dialling out, accepting in, the source address, and what NAT does. |
| [modulo-128.md](modulo-128.md) | Why modulo 128 does not work at either layer, and how Cisco uses it. |

## Using the Desktop app

| Guide | What it says |
|---|---|
| [telnet-pad.md](telnet-pad.md) | How to use a PAD from a normal telnet program through the Desktop app. |

## Testing against other equipment

| Guide | What it says |
|---|---|
| [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) | A full bench of two real Cisco IOS routers in GNS3 against our switch, with every step and expected output. |
| [cisco-ios-x25-options.md](cisco-ios-x25-options.md) | Ways to set up real Cisco routers (25xx and similar) against RetroX25, and how much of each was tested. |
| [cisco-learnings.md](cisco-learnings.md) | One page with everything measured about the Cisco router: facts, cause codes, commands. |
| [crossenv-testing.md](crossenv-testing.md) | Testing against the C emulator `x25emu` in WSL: the network, the addresses, and what the tests prove. |

## Other files

- [config-samples](config-samples): sample configuration files for the Desktop app and the routing sample.
- [images](images): pictures used by these guides.
