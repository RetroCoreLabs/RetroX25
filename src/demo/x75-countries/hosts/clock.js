// A speaking clock: tells the time, then clears the call.
// x25host machine script (API: README of the RetroIO package RetroIO.Protocols.X25.Host).

function two(v) {
    return v < 10 ? '0' + v : String(v);
}

function onConnect(s) {
    var t = machine.now();
    s.send(machine.settings.hostname + ' speaking clock\n');
    s.send('The time is ' + two(t.hour) + ':' + two(t.minute) + ':' + two(t.second) +
           ' on ' + t.year + '-' + two(t.month) + '-' + two(t.day) + '.\n');
    s.send('Goodbye.\n');
    s.clear();
}
