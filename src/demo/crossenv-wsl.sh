#!/bin/bash
#
# crossenv-wsl.sh - starts the x25emu (C emulator) test network inside WSL for the cross-environment tests.
#
# Not meant to be run by hand: demo/crossenv-test.ps1 starts it (through wsl.exe), waits for the file
# "ready" in the state folder, runs the tests, and then stops it by sending TERM to the process number
# written in the file "supervisor.pid". Everything this script starts is written to the file "pids", one
# "name number" per line, and everything is stopped by number - never by name.
#
# It must stay in the foreground: WSL ends every background process of a session when its wsl.exe ends,
# so this script is what keeps the network alive for as long as wsl.exe runs.
#
# Usage:  crossenv-wsl.sh <x25emu-checkout> <windows-address-as-WSL-sees-it> <xot-port> <hdlc-port> <state-folder>
#
#   x25emu-checkout   the folder in WSL that holds the built x25emu (its tools folder has x25switch, x25echo, ...).
#                     Only READ: nothing in it is changed. The three sample switch configs of its demo folder are the
#                     starting point for Switch B and Switch C; Switch A gets the extra ports written below.
#   windows address   the address WSL uses to reach the Windows host (the WSL default gateway, or the LAN address).
#   xot-port          Windows port that x25emu's XOT trunk (Switch A port 4) dials, for calls to DNIC 2351.
#   hdlc-port         Windows port that x25emu's hdlc-tcp trunk (Switch A port 6) dials, for calls to DNIC 2352.
#   state-folder      a new folder (in /tmp) for the scratch configs, logs and the files named above. Deleted at the end.
#
# The network (all addresses are X.121; "ours" is the RetroX25 side on Windows):
#
#   Switch A  DNIC 2340  mgmt 9001  monitor 4101
#     port 0  access  xot   dce  listen 2001   callers (x25pad, x25traffic) connect here
#     port 1  trunk   hdlc  dte  -> Switch B 2003
#     port 3  access  xot   dce  listen 2007   an endpoint of ours connects in; calls to 2343 are sent to it
#     port 4  trunk   xot   dte  -> windows:<xot-port>    calls to 2351 go out here (x25emu dials us)
#     port 5  trunk   hdlc  dce  listen 2010   our hdlc-tcp trunk dials in; calls to 2350 go out here
#     port 6  trunk   hdlc  dte  -> windows:<hdlc-port>   calls to 2352 go out here (x25emu dials our hdlc port)
#     port 7  trunk   xot   dce  listen 2011   our XOT peer dials in; calls to 2353 go out here
#   Switch B  DNIC 2341  (the demo config as it is)       Switch C  DNIC 2342  (the demo config as it is)
#
# No echo service is started here. (Until 2026-11-13 x25emu's x25echo took ONE call: after it, Switch C offered the next call to the
# connection the first call closed ("nothing to deliver ... number busy") until the echo was restarted.) Every test
# that calls 23420001 still starts a fresh x25echo of its own (attached to Switch C port 2006) and stops it by its
# number, so no test depends on what another test left behind.
#

set -u

CHECKOUT="${1:?x25emu checkout}"
WINHOST="${2:?windows address}"
XOTPORT="${3:?xot port}"
HDLCPORT="${4:?hdlc port}"
STATE="${5:?state folder}"

TOOLS="$CHECKOUT/tools"
DEMO="$CHECKOUT/demo"

for tool in x25switch x25echo x25traffic x25pad; do
    if [ ! -x "$TOOLS/$tool" ]; then
        echo "ERROR: $TOOLS/$tool is missing or not executable - build x25emu first (make)" >&2
        exit 2
    fi
done
for conf in switch_b.conf switch_c.conf; do
    if [ ! -r "$DEMO/$conf" ]; then
        echo "ERROR: $DEMO/$conf not found" >&2
        exit 2
    fi
done

if [ -e "$STATE" ]; then
    echo "ERROR: $STATE already exists" >&2
    exit 2
fi
mkdir -p "$STATE" || exit 2
echo $$ > "$STATE/supervisor.pid"
: > "$STATE/pids"

cp "$DEMO/switch_b.conf" "$STATE/switch_b.conf"
cp "$DEMO/switch_c.conf" "$STATE/switch_c.conf"

# Switch A: written out in full so the ports and routes above are exactly what runs.
cat > "$STATE/switch_a.conf" <<EOF
[switch]
dnic = 2340
max_hops = 15
mgmt_port = 9001
monitor_port = 4101
name = Switch Alpha
location = Local Lab
mgmt_addr = 23400000

[user.0]
name = admin
password = admin
level = 1

[port.0]
type = access
transport = xot
mode = dce
port = 2001
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 64

[port.1]
type = trunk
transport = hdlc-tcp
mode = dte
host = 127.0.0.1
port = 2003
peer_name = Switch-B
peer_monitor_host = 127.0.0.1
peer_monitor_port = 4102
peer_mgmt_addr = 23410000
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 64

[port.2]
type = access
transport = xot
mode = dce
port = 2002
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 32

[port.3]
type = access
transport = xot
mode = dce
port = 2007
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 64

[port.4]
type = trunk
transport = xot
mode = dte
host = $WINHOST
port = $XOTPORT
peer_name = RetroX25
peer_monitor_host = $WINHOST
peer_monitor_port = 4001
peer_mgmt_addr = 23510000
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 64

[port.5]
type = trunk
transport = hdlc-tcp
mode = dce
host = 0.0.0.0
port = 2010
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 64

[port.6]
type = trunk
transport = hdlc-tcp
mode = dte
host = $WINHOST
port = $HDLCPORT
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 64

[port.7]
type = trunk
transport = xot
mode = dce
host = 0.0.0.0
port = 2011
packet_size = 128
window_size = 7
max_packet_size = 1024
max_window_size = 7
lci_base = 1
lci_count = 64

[route.0]
type = prefix
address = 2340
prefix_len = 4
port = 0

[route.1]
type = default
port = 1

[route.2]
type = prefix
address = 2343
prefix_len = 4
port = 3

[route.3]
type = prefix
address = 2350
prefix_len = 4
port = 5

[route.4]
type = prefix
address = 2351
prefix_len = 4
port = 4

[route.5]
type = prefix
address = 2352
prefix_len = 4
port = 6

[route.6]
type = prefix
address = 2353
prefix_len = 4
port = 7

# Calls for the Cisco side (2360...) also go out of the XOT trunk to us (port 4). Our switch
# (demo/switch-cisco-x25emu.ini) passes them on to the router. Without this route they would
# follow the default route into Switch B and C, which know no 2360 and clear them.
[route.7]
type = prefix
address = 2360
prefix_len = 4
port = 4

[account.0]
name = pad_users
addr_start = 23400000
addr_end = 23409999
EOF

cd "$STATE" || exit 2

start() {
    # start <name> <command...>: runs it in the background with its output in <name>.log and records its number.
    name="$1"
    shift
    "$@" > "$STATE/$name.log" 2>&1 &
    echo "$name $!" >> "$STATE/pids"
}

stop_all() {
    # Stop everything this script started, by the numbers it recorded; then remove the state folder.
    if [ -r "$STATE/pids" ]; then
        while read -r name pid; do
            kill "$pid" 2>/dev/null
        done < "$STATE/pids"
        sleep 1
        while read -r name pid; do
            kill -0 "$pid" 2>/dev/null && kill -9 "$pid" 2>/dev/null
        done < "$STATE/pids"
    fi
    cd / && rm -rf "$STATE"
    exit 0
}
trap stop_all TERM INT HUP

# Listeners first (C, then B, then A), as the demo does.
start switch_c "$TOOLS/x25switch" -c "$STATE/switch_c.conf"
sleep 1
start switch_b "$TOOLS/x25switch" -c "$STATE/switch_b.conf"
sleep 1
start switch_a "$TOOLS/x25switch" -c "$STATE/switch_a.conf"
sleep 1

# Ready when every port we depend on accepts a connection.
deadline=$((SECONDS + 30))
missing=""
for port in 2001 2006 2007 2009 2010 2011 9001 4101; do
    while ! (echo > "/dev/tcp/127.0.0.1/$port") 2>/dev/null; do
        if [ $SECONDS -ge $deadline ]; then
            missing="$missing $port"
            break
        fi
        sleep 0.3
    done
done
if [ -n "$missing" ]; then
    echo "NOT READY: nothing listens on:$missing" > "$STATE/not-ready"
else
    echo ready > "$STATE/ready"
fi

# Stay in the foreground until told to stop (a TERM runs stop_all).
while true; do
    sleep 1
done
