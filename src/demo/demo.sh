#!/usr/bin/env bash
#
# X.25 C# demo network: three chained switches (A -> B -> C) with distinct DNICs
# plus an echo service on Switch C. Mirrors the reference demo's UX.
#
#   Switch A  DNIC 2350  access 2200   mgmt 9101   monitor 4001
#   Switch B  DNIC 2351  trunk  2201   mgmt 9102   monitor 4002
#   Switch C  DNIC 2352  trunk  2202   mgmt 9103   monitor 4003
#   echo      answers on 23520001, connected to Switch C access port 2203
#
# The PAD connects to Switch A; CALL 23520001 routes A -> B -> C -> echo.
#
# Usage (from the repo root): ./src/demo/demo.sh {start|stop|status|pad|gui|cli-a|cli-b|cli-c}

set -u

# ROOT is src: the solution and every project folder sit one level above this demo folder.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEMO="$ROOT/demo"
LOGS="$DEMO/logs"
PIDS="$DEMO/.pids"
CFG="net10.0"
BIN="bin/Debug/$CFG"

SWITCH="$ROOT/RetroX25.Switch/$BIN/x25switch.dll"
ECHO="$ROOT/RetroX25.Echo/$BIN/x25echo.dll"
PAD="$ROOT/RetroX25.Pad/$BIN/x25pad.dll"
GUI="$ROOT/RetroX25.Monitor.Desktop/$BIN/x25monitor.dll"

build() {
    echo "Building..."
    # -nodeReuse:false so the build leaves no MSBuild worker processes running behind it.
    dotnet build "$ROOT/RetroX25.slnx" -c Debug --nologo -v quiet -nodeReuse:false || exit 1
}

launch() {
    # launch <logfile> <settle-seconds> <dll> [args...]
    local log="$1"; shift
    local settle="$1"; shift
    dotnet "$@" > "$LOGS/$log" 2>&1 &
    local pid=$!
    echo "$pid" >> "$PIDS"
    sleep "$settle"

    # A program that cannot open its ports exits at once. Say so and stop, rather than go on
    # to print "Network ready!" over a network that is not there.
    if ! kill -0 "$pid" 2>/dev/null; then
        echo "  $log: the program exited. Its output:"
        sed 's/^/    /' "$LOGS/$log"
        stop
        exit 1
    fi
}

start() {
    if [ -f "$PIDS" ]; then
        echo "Demo already running (see $PIDS). Run './src/demo/demo.sh stop' first."
        exit 1
    fi
    build
    mkdir -p "$LOGS"
    : > "$PIDS"

    echo "Starting Switch C (DNIC=2352, mgmt=9103)..."
    launch switch-c.log 2 "$SWITCH" "$DEMO/switch-c.ini"

    echo "Starting Switch B (DNIC=2351, mgmt=9102)..."
    launch switch-b.log 2 "$SWITCH" "$DEMO/switch-b.ini"

    echo "Starting Switch A (DNIC=2350, mgmt=9101)..."
    launch switch-a.log 2 "$SWITCH" "$DEMO/switch-a.ini"

    echo "Starting echo service (address 23520001, via Switch C port 2203)..."
    launch echo.log 1 "$ECHO" -x 127.0.0.1:2203 -a 23520001

    cat <<'EOF'

Network ready!

To test:
  ./src/demo/demo.sh pad     - Launch PAD client (connects to Switch A)
  Then at the * prompt:      CALL 23520001
  Type anything - it echoes back!

  ./src/demo/demo.sh gui     - Launch the Avalonia monitor GUI
  In the GUI add/select a switch (127.0.0.1 : 4001/4002/4003) and Connect.

Management consoles (telnet):
  ./src/demo/demo.sh cli-a   - Switch A (telnet localhost 9101)
  ./src/demo/demo.sh cli-b   - Switch B (telnet localhost 9102)
  ./src/demo/demo.sh cli-c   - Switch C (telnet localhost 9103)

  ./src/demo/demo.sh stop    - Stop the whole network
EOF
}

stop() {
    if [ ! -f "$PIDS" ]; then
        echo "Demo not running."
        return
    fi
    echo "Stopping demo..."
    # A PID file outlives its processes (a reboot, a crashed demo) and the numbers are handed out
    # again, so a PID is only killed while it still belongs to a dotnet process. Git Bash's ps
    # shows no arguments, so this cannot also check WHICH dll it runs - only that it is dotnet.
    while read -r pid; do
        [ -n "$pid" ] || continue
        if ps -p "$pid" 2>/dev/null | grep -q dotnet; then
            kill "$pid" 2>/dev/null && echo "  stopped PID $pid"
        else
            echo "  PID $pid is gone or is not a dotnet process - left alone."
        fi
    done < "$PIDS"
    rm -f "$PIDS"
    echo "Stopped."
}

status() {
    if [ ! -f "$PIDS" ]; then
        echo "Demo not running."
        return
    fi
    echo "Demo PIDs:"
    cat "$PIDS"
}

case "${1:-}" in
    start)  start ;;
    stop)   stop ;;
    status) status ;;
    pad)    dotnet "$PAD" -x 127.0.0.1:2200 ;;
    gui)    dotnet "$GUI" ;;
    cli-a)  telnet localhost 9101 ;;
    cli-b)  telnet localhost 9102 ;;
    cli-c)  telnet localhost 9103 ;;
    *)      echo "Usage: ./src/demo/demo.sh {start|stop|status|pad|gui|cli-a|cli-b|cli-c}" ;;
esac
