// Guess the number: each caller gets their own number from 1 to 100. The best score so far is kept for every
// caller of this machine, which shows that a machine's variables are shared by all its calls.
// x25host machine script (API: README of the RetroIO package RetroIO.Protocols.X25.Host).

var best = 0;        // fewest guesses so far, 0 = nobody has won yet
var games = {};      // per call: { secret, tries, line }

function newGame(s) {
    var t = machine.now();
    games[s.id] = { secret: (t.ticks % 100) + 1, tries: 0, line: '' };
    s.send('I am thinking of a number from 1 to 100. Your guess? ');
}

function onConnect(s) {
    s.send('Welcome to ' + machine.settings.hostname + '. Type q to leave.\n');
    s.send(best > 0 ? 'Best so far: ' + best + ' guesses.\n' : 'Nobody has won yet.\n');
    newGame(s);
}

function onInput(s, text) {
    var g = games[s.id];
    for (var i = 0; i < text.length; i++) {
        var c = text.charAt(i);
        if (c === '\r' || c === '\n') {
            var line = g.line.trim();
            g.line = '';
            if (line.length > 0) {
                answer(s, g, line);
            }
        } else if (g.line.length < 40) {
            g.line += c;
        }
    }
}

function answer(s, g, line) {
    if (line === 'q' || line === 'Q') {
        s.send('Goodbye.\n');
        s.clear();
        return;
    }

    var n = parseInt(line, 10);
    if (isNaN(n) || n < 1 || n > 100) {
        s.send('A number from 1 to 100, please: ');
        return;
    }

    g.tries++;
    if (n < g.secret) {
        s.send('Higher: ');
    } else if (n > g.secret) {
        s.send('Lower: ');
    } else {
        s.send('Right, in ' + g.tries + ' guesses!\n');
        if (best === 0 || g.tries < best) {
            best = g.tries;
            s.send('That is the new best.\n');
        }
        newGame(s);
    }
}

function onDisconnect(s) {
    delete games[s.id];
}
