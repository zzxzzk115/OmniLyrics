// JXA is part of macOS. Application sends public Apple Events to the player;
// AppKit's process check avoids launching players (including an absent Spotify).
ObjC.import('AppKit');
function run(argv) {
    var id = argv[0], action = argv[1], app = null;
    function running() { return $.NSRunningApplication.runningApplicationsWithBundleIdentifier(id).count > 0; }
    function write(value) {
        var data = $(JSON.stringify(value) + '\n').dataUsingEncoding($.NSUTF8StringEncoding);
        $.NSFileHandle.fileHandleWithStandardOutput.writeData(data);
    }
    if (action !== 'stream') {
        if (!running()) throw new Error('Player is not running');
        app = Application(id);
        if (action === 'favorite-get' || action === 'favorite-set') {
            try {
                if (id !== 'com.apple.Music') return 'null';
                var expected = JSON.parse(argv[2]), current = app.currentTrack();
                function norm(value) { return String(value || '').trim().toUpperCase(); }
                function identity(track) { return String(track.persistentID()); }
                var stableId = identity(current);
                if (!stableId || norm(current.name()) !== norm(expected.title) || norm(current.artist()) !== norm(expected.artist)
                    || expected.album && norm(current.album()) !== norm(expected.album)
                    || expected.duration > 0 && Math.abs(current.duration() - expected.duration) >= 2) return 'null';
                var property = 'favorited';
                try { if (typeof current.favorited() !== 'boolean') return 'null'; }
                catch (_) { property = 'loved'; if (typeof current.loved() !== 'boolean') return 'null'; }
                if (action === 'favorite-set') {
                    if (expected.targetId !== stableId || typeof expected.favorite !== 'boolean') return 'null';
                    // Track IDs are scoped to their container, including streaming sources.
                    var fixedTrack = current.container().tracks.byId(current.id());
                    if (identity(fixedTrack) !== stableId || identity(app.currentTrack()) !== stableId) return 'null';
                    fixedTrack[property] = expected.favorite;
                    if (identity(app.currentTrack()) !== stableId || fixedTrack[property]() !== expected.favorite) return 'null';
                }
                var result = {id: stableId, favorite: current[property]()};
                return identity(app.currentTrack()) === stableId ? JSON.stringify(result) : 'null';
            } catch (_) { return 'null'; }
        }
        switch (action) {
            case 'play': app.play(); break;
            case 'pause': app.pause(); break;
            case 'toggle': app.playpause(); break;
            case 'next': app.nextTrack(); break;
            case 'previous': app.previousTrack(); break;
            case 'seek': app.playerPosition = Number(argv[2]); break;
            default: throw new Error('Unknown playback command');
        }
        return;
    }
    while (true) {
        var delay = 0.5;
        try {
            if (!running()) { app = null; write(null); delay = 1; }
            else {
                if (app === null) { write({waiting: true}); app = Application(id); }
                var state = app.playerState();
                if (state === 'stopped') write(null);
                else {
                    var track = app.currentTrack();
                    var duration = track.duration() / (id === 'com.spotify.client' ? 1000 : 1);
                    var value = {title: track.name(), artist: track.artist(), album: track.album(),
                        duration: duration, position: app.playerPosition(), playing: state === 'playing'};
                    if (id === 'com.spotify.client') {
                        try { value.artworkUrl = track.artworkUrl(); } catch (_) { }
                    }
                    write(value);
                }
            }
        } catch (error) {
            write({error: Number(error.errorNumber || error.number || 0), message: String(error)});
            delay = 5;
        }
        $.NSThread.sleepForTimeInterval(delay);
    }
}
