const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const script = readFileSync('src/OmniLyrics.Backends.Mac/Scripts/AppleEvents.js', 'utf8');
let current = 'one', writes = [], denied = false, switchOnResolve = false, rejectWrite = false;
const tracks = {};
for (const id of ['one', 'two']) {
  let favorite = false;
  tracks[id] = { name: () => 'Song', artist: () => 'Artist', album: () => 'Album', duration: () => 120, persistentID: () => id, id: () => id };
  Object.defineProperty(tracks[id], 'favorited', { get: () => () => { if (denied) throw Error('Denied'); return favorite; },
    set: value => { writes.push(id); if (!rejectWrite) favorite = value; } });
}
const app = { currentTrack: () => tracks[current], tracks: { byId: id => { if (switchOnResolve) current = 'two'; return tracks[id]; } } };
for (const track of Object.values(tracks)) track.container = () => ({ tracks: app.tracks });
const context = { ObjC: { import() {} }, $: { NSRunningApplication: { runningApplicationsWithBundleIdentifier: () => ({ count: 1 }) } }, Application: () => app };
vm.createContext(context); vm.runInContext(script, context);
const expected = { title: 'Song', artist: 'Artist', album: 'Album', duration: 120, targetId: 'one', favorite: true };
function run(action, payload = expected, bundle = 'com.apple.Music') { return JSON.parse(context.run([bundle, action, JSON.stringify(payload)])); }
assert.deepEqual(run('favorite-get'), { id: 'one', favorite: false }); assert.equal(writes.length, 0);
assert.deepEqual(run('favorite-set'), { id: 'one', favorite: true }); assert.deepEqual(writes, ['one']);
assert.equal(run('favorite-set', { ...expected, targetId: 'stale' }), null); assert.equal(writes.length, 1);
assert.equal(run('favorite-set', { ...expected, title: 'Other' }), null); assert.equal(writes.length, 1);
switchOnResolve = true;
assert.equal(run('favorite-set', { ...expected, favorite: false }), null); assert.equal(writes.length, 1);
switchOnResolve = false; current = 'one'; rejectWrite = true;
assert.equal(run('favorite-set', { ...expected, favorite: false }), null); assert.equal(writes.length, 2);
denied = true;
assert.equal(run('favorite-get'), null);
assert.equal(run('favorite-get', expected, 'com.spotify.client'), null);
console.log('PASS Apple Events favorites: read, explicit-ID write, stale metadata, switched track, rejected write, permission denial, unsupported player');
