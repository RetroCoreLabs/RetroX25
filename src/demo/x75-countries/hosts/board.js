// A notice board: every caller sees the last notices and can pin one. Notices are kept while x25host runs and are
// shared by all callers of this machine; callers on line at the same time see a new notice at once.
// x25host machine script (API: README of the RetroIO package RetroIO.Protocols.X25.Host).

var notices = [];
var lines = {};
var MAX = 10;

function onConnect(s) {
    lines[s.id] = '';
    s.send(machine.settings.hostname + ' notice board. ' + notices.length + ' notice(s).\n');
    for (var i = 0; i < notices.length; i++) {
        s.send('  ' + notices[i] + '\n');
    }
    s.send('Type a line to pin it, or q to leave.\n> ');
}

function onInput(s, text) {
    for (var i = 0; i < text.length; i++) {
        var c = text.charAt(i);
        if (c === '\r' || c === '\n') {
            var line = lines[s.id].trim();
            lines[s.id] = '';
            if (line === 'q' || line === 'Q') {
                s.send('Goodbye.\n');
                s.clear();
                return;
            }
            if (line.length > 0) {
                var notice = (s.calling || 'unknown') + ': ' + line;
                notices.push(notice);
                if (notices.length > MAX) {
                    notices.shift();
                }
                s.send('Pinned.\n> ');
                machine.broadcast('\nNew notice ' + notice + '\n> ', s.id);
            } else {
                s.send('> ');
            }
        } else if (lines[s.id].length < 70) {
            lines[s.id] += c;
        }
    }
}

function onDisconnect(s) {
    delete lines[s.id];
}
