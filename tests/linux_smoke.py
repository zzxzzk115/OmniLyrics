"""Linux runtime checks; requires Python 3, PyGObject, iproute2 and util-linux.

Run after a Release build: python3 tests/linux_smoke.py
Requires dbus-run-session and unprivileged user/network namespaces.
Only an isolated mock MPRIS player is controlled. Logs are saved under /tmp.
"""

import json
import os
from pathlib import Path
import signal
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse
from gi.repository import Gio, GLib

if '--isolated-network' not in sys.argv:
    raise SystemExit(subprocess.call([
        'unshare', '--user', '--map-current-user', '--keep-caps', '--net', '--',
        'sh', '-c', 'ip link set lo up && exec "$@"', 'omnilyrics-smoke',
        sys.executable, str(Path(__file__).resolve()), '--isolated-network', *[x for x in sys.argv[1:] if x == '--no-desktop-actions']]))

if '--isolated-bus' not in sys.argv:
    # Never send playback controls to the user's desktop session.
    raise SystemExit(subprocess.call(['dbus-run-session', '--', sys.executable,
                                     str(Path(__file__).resolve()), '--isolated-network', '--isolated-bus', *[x for x in sys.argv[1:] if x == '--no-desktop-actions']]))

ROOT = Path(__file__).resolve().parents[1]
# Never inherit the user's Cider credentials into a test instance.
config_directory = tempfile.TemporaryDirectory(prefix='omnilyrics-smoke-config-')
os.environ['OMNILYRICS_CONFIG_DIR'] = config_directory.name
for key in ('CIDER_API_TOKEN', 'CIDER_TOKEN_FILE', 'CIDER_AUTH_MODE', 'OMNILYRICS_LISTEN_ADDRESS',
            'OMNILYRICS_HTTP_PORT', 'OMNILYRICS_UDP_PORT', 'OMNILYRICS_CONTROL_HOST'):
    os.environ.pop(key, None)
CLI = ROOT / 'src/OmniLyrics.Cli/bin/Release/net10.0/linux-x64/OmniLyrics.Cli'
GUI = ROOT / 'src/OmniLyrics.Gui/bin/Release/net10.0/linux-x64/OmniLyrics.Gui'
BASE = 'http://127.0.0.1:27270'
NAME = 'org.mpris.MediaPlayer2.cider.OmniLyricsSmokeTest'
PATH = '/org/mpris/MediaPlayer2'
IFACE = 'org.mpris.MediaPlayer2.Player'
TRACK = '/org/mpris/MediaPlayer2/track/test'
results = []

# Fail before launching if existing services could interfere with the test.
for port, kind in ((27270, socket.SOCK_STREAM), (32651, socket.SOCK_DGRAM)):
    with socket.socket(socket.AF_INET, kind) as probe:
        probe.bind(('127.0.0.1', port))
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
    probe.settimeout(.2)
    if probe.connect_ex(('127.0.0.1', 10767)) == 0:
        raise SystemExit('Stop Cider before running the isolated mock-player test.')

def check(name, action):
    try:
        detail = action()
        results.append((name, True, detail))
        print('PASS', name, detail or '', flush=True)
    except Exception as exc:
        results.append((name, False, str(exc)))
        print('FAIL', name, str(exc), flush=True)

def require(value, message):
    if not value:
        raise AssertionError(message)

def eventually(predicate, timeout=8):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            last = predicate()
            if last:
                return last
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(.1)
    raise AssertionError(f'Condition not reached; last={last!r}')

def api(path='/playback/state', method='GET', data=None):
    body = None if data is None else json.dumps(data).encode()
    request = urllib.request.Request(BASE + path, body, {'Content-Type':'application/json'}, method=method)
    with urllib.request.urlopen(request, timeout=3) as response:
        raw = response.read()
        return json.loads(raw) if raw else None

def state_is(**expected):
    state = api()
    return state is not None and all(state.get(k) == v for k,v in expected.items())

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
loop = GLib.MainLoop()
threading.Thread(target=loop.run, daemon=True).start()
status = 'Playing'
position = 5_000_000
title = 'OmniLyrics integration test'
artist = ''
calls = []
capable_mpris = False
can_navigate = True

def metadata():
    return {'mpris:trackid': GLib.Variant('o', TRACK),
            'mpris:length': GLib.Variant('x', 180_000_000),
            'xesam:title': GLib.Variant('s', title),
            # A blank artist exercises music events without calling lyric providers.
            'xesam:artist': GLib.Variant('as', [artist]),
            'xesam:album': GLib.Variant('s', 'Test album')}

def properties_changed():
    bus.emit_signal(None, PATH, 'org.freedesktop.DBus.Properties', 'PropertiesChanged',
        GLib.Variant('(sa{sv}as)', (IFACE, {'PlaybackStatus': GLib.Variant('s',status),
        'Metadata': GLib.Variant('a{sv}', metadata())}, [])))

def method_call(conn, sender, path, interface, method, parameters, invocation):
    global status, position
    args = parameters.unpack()
    calls.append((method, args))
    if method == 'GetTracksMetadata':
        # Empty artist lists avoid online providers while exercising real D-Bus
        # serialization for a generic (not Cider-specific) queue capability.
        invocation.return_value(GLib.Variant('(aa{sv})', ([{
            'mpris:trackid': GLib.Variant('o', item),
            'xesam:title': GLib.Variant('s', 'Upcoming test'),
            'xesam:artist': GLib.Variant('as', [])} for item in args[0]],)))
        return
    if method == 'Play': status = 'Playing'
    elif method == 'Pause': status = 'Paused'
    elif method == 'PlayPause': status = 'Paused' if status == 'Playing' else 'Playing'
    elif method == 'SetPosition':
        if args[0] == TRACK: position = args[1]
    elif method == 'Seek': position += args[0]
    invocation.return_value(GLib.Variant('()', ()))
    properties_changed()

def get_property(conn, sender, path, interface, prop):
    if interface == 'org.mpris.MediaPlayer2.TrackList':
        return GLib.Variant('ao', [TRACK, TRACK + '_next', TRACK + '_later'])
    if interface == 'org.mpris.MediaPlayer2':
        return GLib.Variant('s', 'OmniLyrics mock player')
    if prop.startswith('Can'):
        return GLib.Variant('b', capable_mpris and (can_navigate or prop not in ('CanGoNext','CanGoPrevious')))
    return {'PlaybackStatus': lambda: GLib.Variant('s',status),
            'Position': lambda: GLib.Variant('x',position),
            'Metadata': lambda: GLib.Variant('a{sv}',metadata())}[prop]()

xml = '''<node><interface name="org.mpris.MediaPlayer2.Player">
<method name="Play"/><method name="Pause"/><method name="PlayPause"/>
<method name="Next"/><method name="Previous"/>
<method name="Seek"><arg type="x" direction="in"/></method>
<method name="SetPosition"><arg type="o" direction="in"/><arg type="x" direction="in"/></method>
<property name="PlaybackStatus" type="s" access="read"/>
<property name="Position" type="x" access="read"/>
<property name="Metadata" type="a{sv}" access="read"/>
<property name="CanControl" type="b" access="read"/>
<property name="CanPlay" type="b" access="read"/>
<property name="CanPause" type="b" access="read"/>
<property name="CanGoNext" type="b" access="read"/>
<property name="CanGoPrevious" type="b" access="read"/>
<property name="CanSeek" type="b" access="read"/>
</interface></node>'''
bus.register_object(PATH, Gio.DBusNodeInfo.new_for_xml(xml).interfaces[0], method_call, get_property, None)
root_xml = '''<node><interface name="org.mpris.MediaPlayer2">
<property name="Identity" type="s" access="read"/>
</interface></node>'''
bus.register_object(PATH, Gio.DBusNodeInfo.new_for_xml(root_xml).interfaces[0], None, get_property, None)
queue_xml = '''<node><interface name="org.mpris.MediaPlayer2.TrackList">
<property name="Tracks" type="ao" access="read"/>
<method name="GetTracksMetadata"><arg type="ao" direction="in"/><arg type="aa{sv}" direction="out"/></method>
</interface></node>'''
bus.register_object(PATH, Gio.DBusNodeInfo.new_for_xml(queue_xml).interfaces[0], method_call, get_property, None)

# A tray host on the isolated bus exercises the real Avalonia export without
# registering test icons with the user's desktop tray.
tray_items = []
watcher_xml = '''<node><interface name="org.kde.StatusNotifierWatcher">
<method name="RegisterStatusNotifierItem"><arg type="s" direction="in"/></method>
<property name="IsStatusNotifierHostRegistered" type="b" access="read"/>
<property name="ProtocolVersion" type="i" access="read"/>
<property name="RegisteredStatusNotifierItems" type="as" access="read"/>
</interface></node>'''
def register_tray(conn, sender, path, interface, method, parameters, invocation):
    tray_items.append(parameters.unpack()[0])
    invocation.return_value(GLib.Variant('()', ()))

def watcher_property(conn, sender, path, interface, prop):
    return {'IsStatusNotifierHostRegistered': GLib.Variant('b', True),
            'ProtocolVersion': GLib.Variant('i', 0),
            'RegisteredStatusNotifierItems': GLib.Variant('as', tray_items)}[prop]

bus.register_object('/StatusNotifierWatcher', Gio.DBusNodeInfo.new_for_xml(watcher_xml).interfaces[0],
                    register_tray, watcher_property, None)
bus.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus',
              'RequestName', GLib.Variant('(su)', ('org.kde.StatusNotifierWatcher', 0)),
              None, Gio.DBusCallFlags.NONE, 3000, None)

def tray_call(path, interface, method, args):
    return bus.call_sync(tray_items[-1], path, interface, method, args, None,
                         Gio.DBusCallFlags.NONE, 3000, None).unpack()

def tray_property(prop):
    return tray_call('/StatusNotifierItem', 'org.freedesktop.DBus.Properties', 'Get',
                     GLib.Variant('(ss)', ('org.kde.StatusNotifierItem', prop)))[0]

def name_call(method):
    args = GLib.Variant('(su)', (NAME,0)) if method == 'RequestName' else GLib.Variant('(s)',(NAME,))
    return bus.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus',method,args,None,Gio.DBusCallFlags.NONE,3000,None)

def stop(proc):
    if proc.poll() is None:
        proc.send_signal(signal.SIGINT)
        try: proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.terminate()
            try: proc.wait(timeout=3)
            except subprocess.TimeoutExpired: proc.kill(); proc.wait()

cli = None
gui = None
try:
    cli_log = open('/tmp/omnilyrics-cli-smoke.log','w')
    cli = subprocess.Popen([str(CLI),'--mode','line'], stdout=cli_log, stderr=subprocess.STDOUT)
    check('CLI starts without a player', lambda: eventually(lambda: cli.poll() is None and api() is None))
    def service_identity():
        snapshot=api('/snapshot')
        require(snapshot['service']=='OmniLyrics' and snapshot['protocolVersion']==1 and snapshot['state'] is None,str(snapshot))
    check('CLI identifies its shared GUI protocol without a player',service_identity)
    def occupied_port():
        with tempfile.TemporaryFile(mode='w+') as log:
            other=subprocess.Popen([str(CLI),'--mode','line'],stdout=log,stderr=log)
            try:
                def attached():
                    log.seek(0)
                    return other.poll() is None and 'Connected to existing OmniLyrics service.' in log.read()
                eventually(attached)
                require(cli.poll() is None,'Existing CLI was interrupted')
                require(api('/snapshot')['ownerRole']=='cli','Owner unexpectedly changed')
            finally: stop(other)
    check('Another CLI attaches without disturbing the existing service',occupied_port)
    def local_listeners():
        lines = subprocess.check_output(['ss','-H','-lntu'], text=True).splitlines()
        listeners = [line.split()[4] for line in lines if line.split()[4].rsplit(':',1)[-1] in ('27270','32651')]
        require(len(listeners) == 2 and all(x.startswith('127.0.0.1:') for x in listeners),str(listeners))
    check('Control servers listen only on loopback', local_listeners)
    def no_lyrics():
        try: api('/lyrics')
        except urllib.error.HTTPError as exc:
            require(exc.code == 404, str(exc)); return
        raise AssertionError('Expected 404 with no player')
    check('Lyrics endpoint with no player', no_lyrics)
    name_call('RequestName')
    check('MPRIS player discovery and metadata', lambda: eventually(lambda: state_is(title=title, album='Test album', playing=True)))
    check('MPRIS exposes the player Identity in the shared snapshot', lambda: eventually(
        lambda: api('/snapshot')['state'].get('playerName') == 'OmniLyrics mock player'))
    check('Generic MPRIS TrackList reads upcoming metadata after the current track', lambda: eventually(
        lambda: any(method == 'GetTracksMetadata' and args[0] == [TRACK + '_next', TRACK + '_later']
                    for method, args in calls)))
    def http_control(command, playing):
        api('/playback/'+command, 'POST')
        eventually(lambda: state_is(playing=playing))
    check('HTTP pause', lambda: http_control('pause',False))
    check('HTTP play', lambda: http_control('play',True))
    check('HTTP toggle', lambda: http_control('toggle',False))
    def navigation():
        api('/playback/next','POST'); api('/playback/prev','POST')
        require(any(c[0]=='Next' for c in calls) and any(c[0]=='Previous' for c in calls),'Navigation was not received')
    check('HTTP next / previous', navigation)
    def remote_control(command, playing):
        subprocess.run([str(CLI),'--control',command],check=True,timeout=5)
        eventually(lambda: state_is(playing=playing))
    check('CLI UDP play', lambda: remote_control('play',True))
    def seek():
        api('/playback/seek','POST',{'position':42.5})
        eventually(lambda: position == 42_500_000)
        require(('SetPosition',(TRACK,42_500_000)) in calls,'Wrong MPRIS track id or position')
    check('HTTP seek with an MPRIS object path', seek)
    def udp_seek():
        subprocess.run([str(CLI),'--control','seek','12.25'],check=True,timeout=5)
        eventually(lambda: position == 12_250_000)
        require(('SetPosition',(TRACK,12_250_000)) in calls,'UDP seek did not reach the mock player')
    check('CLI UDP seek', udp_seek)
    def paused_seek():
        http_control('pause',False)
        api('/playback/seek','POST',{'position':6.25})
        eventually(lambda:state_is(position='00:00:06.2500000',playing=False))
        http_control('play',True)
    check('MPRIS position updates when seeking while paused',paused_seek)
    title = 'Changed test track'
    properties_changed()
    check('MPRIS metadata change signal', lambda: eventually(lambda: state_is(title=title)))
    name_call('ReleaseName')
    check('MPRIS player removal clears state', lambda: eventually(lambda: api() is None))
    stop(cli)
    check('CLI exits without an exception', lambda: require(cli.returncode == 0, f'exit={cli.returncode}'))
    cli = subprocess.Popen([str(CLI)], stdout=cli_log, stderr=subprocess.STDOUT)
    check('Default CLI mode starts', lambda: eventually(lambda: cli.poll() is None and api() is None))
    stop(cli)
    check('Default CLI exits cleanly', lambda: require(cli.returncode == 0, f'exit={cli.returncode}'))

    # Bind to all interfaces on alternate ports and reach a non-loopback address
    # inside this isolated network. No interface or listener on the host changes.
    subprocess.run(['ip','address','add','192.0.2.2/32','dev','lo'],check=True)
    cli = subprocess.Popen([str(CLI),'--mode','line','--listen','0.0.0.0',
        '--http-port','27271','--udp-port','32653'],stdout=cli_log,stderr=subprocess.STDOUT)
    remote_http = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    def lan_api(path='/playback/state',method='GET'):
        req=urllib.request.Request('http://192.0.2.2:27271'+path,method=method)
        with remote_http.open(req,timeout=2) as response:
            raw=response.read();return json.loads(raw) if raw else None
    name_call('RequestName')
    check('LAN HTTP works on configured address and port',lambda:eventually(lambda:lan_api() is not None))
    def wildcard_listeners():
        lines=subprocess.check_output(['ss','-H','-lntu'],text=True).splitlines()
        endpoints=[line.split()[4] for line in lines if line.split()[4].rsplit(':',1)[-1] in ('27271','32653')]
        require(len(endpoints)==2 and all(x.startswith('0.0.0.0:') for x in endpoints),str(endpoints))
    check('HTTP and UDP honor the configured wildcard binding',wildcard_listeners)
    def lan_control():
        lan_api('/playback/pause','POST')
        eventually(lambda:lan_api()['playing'] is False)
        subprocess.run([str(CLI),'--host','192.0.2.2','--udp-port','32653','--control','play'],check=True,timeout=5)
        eventually(lambda:lan_api()['playing'] is True)
    check('Remote HTTP and CLI UDP control reach the selected host',lan_control)
    name_call('ReleaseName')
    stop(cli)

    # V3 must keep working through Web API even if it publishes an incomplete
    # MPRIS endpoint. V4 can explicitly use a capable MPRIS implementation.
    class CiderHandler(BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def do_GET(self):
            if self.path.endswith('/is-playing'):data={'status':'ok','is_playing':True}
            elif self.path.endswith('/now-playing'):
                data={'status':'ok','info':{'name':'Cider Web API test','artistName':'',
                    'albumName':'Test album','durationInMillis':180000,'currentPlaybackTime':time.monotonic()%180}}
            else:data={'status':'ok'}
            body=json.dumps(data).encode()
            self.send_response(200);self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
    cider_server=ThreadingHTTPServer(('127.0.0.1',10767),CiderHandler)
    threading.Thread(target=cider_server.serve_forever,daemon=True).start()
    settings_path=Path(config_directory.name)/'config.json'
    name_call('RequestName')
    try:
        for mode in ('webapi','auto','mpris'):
            settings_path.write_text(json.dumps({'cider':{'authentication':'none','integration':mode}}))
            cli=subprocess.Popen([str(CLI),'--mode','line'],stdout=cli_log,stderr=subprocess.STDOUT)
            check('Cider '+mode+' preserves Web API with incomplete MPRIS',
                  lambda:eventually(lambda:state_is(sourceApp='Cider',playerName='Cider',title='Cider Web API test')))
            time.sleep(.5)
            check('Cider '+mode+' does not alternate between API and MPRIS',
                  lambda:require(state_is(sourceApp='Cider'),'Source switched unexpectedly'))
            stop(cli)
        capable_mpris = True
        can_navigate = False
        cli=subprocess.Popen([str(CLI),'--mode','line'],stdout=cli_log,stderr=subprocess.STDOUT)
        check('Cider mpris selects a capable V4 player over healthy Web API',
              lambda:eventually(lambda:state_is(sourceApp=NAME,title=title)))
        check('Track-specific unavailable navigation does not reject V4 MPRIS',
              lambda:require(state_is(sourceApp=NAME),'MPRIS should remain selected'))
        check('V4 MPRIS pause stays on MPRIS',lambda:http_control('pause',False))
        check('V4 MPRIS play stays on MPRIS',lambda:http_control('play',True))
        settings_path.write_text(json.dumps({'cider':{'authentication':'none','integration':'webapi'}}))
        check('Changing integration takes effect without restarting',
              lambda:eventually(lambda:state_is(sourceApp='Cider')))
        capable_mpris = False
    finally:
        stop(cli)
        name_call('ReleaseName')
        cider_server.shutdown();cider_server.server_close()
        settings_path.unlink(missing_ok=True)

    # Exercise actual lyric loading and JSON output using local, synthetic LRC data.
    NAME = 'org.mpris.MediaPlayer2.yesplaymusic.OmniLyricsSmokeTest'
    title = 'JSON test song'
    artist = 'Test Artist'
    track_id = 1
    position = 5_000_000
    status = 'Playing'
    slow_request = threading.Event()
    release_request = threading.Event()
    class LyricHandler(BaseHTTPRequestHandler):
        def log_message(self, *args): pass
        def do_GET(self):
            if self.path == '/player':
                result = {'currentTrack': {'id': track_id, 'name': title, 'dt':180000,
                    'al': {'name':'Test album'}, 'ar':[{'name':artist}]}, 'progress':position/1_000_000}
            else:
                requested = int(parse_qs(urlparse(self.path).query)['id'][0])
                if requested == 2:
                    slow_request.set()
                    release_request.wait(5)
                label = {1:'First',2:'Stale',3:'Latest'}[requested]
                result = {'lrc': {'lyric':f'[00:01.00]{label} test line\n[00:08.00]Second test line\n[00:20.00]Final test line'}}
            body = json.dumps(result).encode()
            self.send_response(200)
            self.send_header('Content-Type','application/json')
            self.send_header('Content-Length',str(len(body)))
            self.end_headers()
            self.wfile.write(body)
    servers = [ThreadingHTTPServer(('127.0.0.1',port),LyricHandler) for port in (10754,27232)]
    for server in servers:
        threading.Thread(target=server.serve_forever,daemon=True).start()
    samples = []
    json_errors = []
    cli = subprocess.Popen([str(CLI),'--mode','json'],stdout=subprocess.PIPE,stderr=cli_log,text=True)
    def read_snapshots():
        for line in cli.stdout:
            try: samples.append(json.loads(line))
            except ValueError: json_errors.append(line)
    reader = threading.Thread(target=read_snapshots,daemon=True)
    reader.start()
    def snapshot_is(**expected):
        return bool(samples) and all(samples[-1].get(k)==v for k,v in expected.items())
    try:
        check('JSON mode emits an empty initial state',lambda: eventually(lambda: snapshot_is(available=False,currentLine='')))
        def json_control_ports():
            eventually(lambda: api('/snapshot')['ownerRole']=='cli')
            lines = subprocess.check_output(['ss','-H','-lntu'],text=True).splitlines()
            listeners = [line for line in lines if line.split()[4].rsplit(':',1)[-1] in ('27270','32651')]
            require(len(listeners)==2,str(listeners))
        check('JSON mode owns the shared service when no frontend is running',json_control_ports)
        name_call('RequestName')
        check('JSON current and next lyrics',lambda: eventually(lambda: snapshot_is(currentLine='First test line',nextLine='Second test line',loading=False)))
        position = 12_000_000
        check('JSON follows playback position',lambda: eventually(lambda: snapshot_is(currentLine='Second test line',nextLine='Final test line')))
        status = 'Paused'; properties_changed()
        check('JSON preserves lyrics when paused',lambda: eventually(lambda: snapshot_is(playing=False,currentLine='Second test line')))
        track_id = 2; title = 'Slow test song'; position = 5_000_000
        properties_changed()
        check('JSON clears lyrics while a new song loads',lambda: eventually(lambda: snapshot_is(title=title,loading=True,currentLine='')))
        check('Slow lyric request reached the mock provider',lambda: require(slow_request.wait(3),'No delayed request'))
        track_id = 3; title = 'Latest test song'; properties_changed()
        check('JSON switches to the latest song',lambda: eventually(lambda: snapshot_is(title=title,currentLine='Latest test line')))
        release_request.set(); time.sleep(.5)
        check('Old lyric requests cannot overwrite a new song',lambda: require(snapshot_is(title=title,currentLine='Latest test line'),str(samples[-1:])))
        name_call('ReleaseName')
        check('JSON clears lyrics when the player disappears',lambda: eventually(lambda: snapshot_is(available=False,currentLine='',nextLine='')))
        stop(cli); reader.join(timeout=2)
        check('JSON output contains only valid JSON records',lambda: require(not json_errors,str(json_errors)))
        check('JSON CLI exits cleanly',lambda: require(cli.returncode == 0,f'exit={cli.returncode}'))
    finally:
        release_request.set()
        stop(cli)
        for server in servers: server.shutdown(); server.server_close()
    artist = ''
    cli = subprocess.Popen([str(CLI),'--mode','line'],stdout=cli_log,stderr=subprocess.STDOUT)
    eventually(lambda: api('/snapshot')['service']=='OmniLyrics')
    gui_log = open('/tmp/omnilyrics-gui-smoke.log','w')
    gui = subprocess.Popen([str(GUI)],stdout=gui_log,stderr=subprocess.STDOUT)
    time.sleep(2)
    check('GUI stays running after startup', lambda: require(gui.poll() is None, f'exit={gui.poll()}'))
    check('GUI registers its tray icon with the host', lambda: eventually(lambda: bool(tray_items)))
    check('Tray exports a valid Active status', lambda: eventually(lambda: tray_property('Status') == 'Active'))
    check('Tray exports an icon image', lambda: require(bool(tray_property('IconPixmap')), 'Empty icon'))
    menu_path = tray_property('Menu')
    def menu_layout():
        return tray_call(menu_path, 'com.canonical.dbusmenu', 'GetLayout',
                         GLib.Variant('(iias)', (0, -1, [])))[1][2]
    def menu_item(label):
        return next(item[0] for item in menu_layout() if label in item[1].get('label', ''))
    check('Tray exposes Show Lyrics, Settings and Quit', lambda: require(
        all(menu_item(label) for label in ('Show Lyrics', 'Settings', 'Quit')), 'Missing menu item'))
    def gui_attached():
        return 'Connected to existing OmniLyrics service.' in Path('/tmp/omnilyrics-gui-smoke.log').read_text()
    check('Actual GUI attaches while CLI owns the control ports',lambda:eventually(gui_attached))
    def gui_idle():
        def ticks():
            fields = Path(f'/proc/{gui.pid}/stat').read_text().split(') ',1)[1].split()
            return int(fields[11])+int(fields[12])
        before=ticks(); start=time.monotonic(); time.sleep(3)
        usage=100*(ticks()-before)/os.sysconf('SC_CLK_TCK')/(time.monotonic()-start)
        require(usage < 30, f'Idle CPU usage is {usage:.1f}% of one core')
        return f'{usage:.1f}% of one core'
    check('GUI idle CPU usage', gui_idle)
    if '--no-desktop-actions' not in sys.argv and shutil.which('hyprctl') and os.environ.get('HYPRLAND_INSTANCE_SIGNATURE'):
        def mapped_window():
            windows = json.loads(subprocess.check_output(['hyprctl','-j','clients'],text=True))
            return any(w['pid'] == gui.pid and w.get('mapped') for w in windows)
        check('GUI window is mapped on the desktop', lambda: eventually(mapped_window))
        def tray_activate():
            tray_call('/StatusNotifierItem', 'org.kde.StatusNotifierItem', 'Activate',
                      GLib.Variant('(ii)', (0, 0)))
        def tray_visibility():
            tray_activate()
            eventually(lambda: not mapped_window())
            require(gui.poll() is None, 'Tray activation terminated the GUI')
            tray_activate()
            eventually(mapped_window)
        check('Tray left click hides and restores the lyrics window', tray_visibility)
        def tray_settings():
            tray_call(menu_path, 'com.canonical.dbusmenu', 'Event',
                      GLib.Variant('(isvu)', (menu_item('Settings'), 'clicked', GLib.Variant('i', 0), 0)))
            def settings_visible():
                windows = json.loads(subprocess.check_output(['hyprctl','-j','clients'],text=True))
                return sum(w['pid'] == gui.pid and bool(w.get('mapped')) for w in windows) >= 2
            eventually(settings_visible)
        check('Tray Settings menu opens the existing settings window', tray_settings)
    name_call('RequestName')
    time.sleep(2)
    check('GUI handles a player appearing', lambda: require(gui.poll() is None, f'exit={gui.poll()}'))
    name_call('ReleaseName')
    time.sleep(2)
    check('GUI handles a player disappearing', lambda: require(gui.poll() is None, f'exit={gui.poll()}'))
    stop(cli)
    check('Actual GUI takes service ownership when CLI exits',lambda:eventually(lambda:api('/snapshot')['ownerRole']=='gui'))
    cli = subprocess.Popen([str(CLI),'--mode','line'],stdout=cli_log,stderr=subprocess.STDOUT)
    check('Returning CLI attaches to the healthy GUI owner',lambda:eventually(lambda:api('/snapshot')['ownerRole']=='gui' and 'Connected to existing OmniLyrics service.' in Path('/tmp/omnilyrics-cli-smoke.log').read_text()))
    if '--no-desktop-actions' not in sys.argv and shutil.which('hyprctl') and os.environ.get('HYPRLAND_INSTANCE_SIGNATURE'):
        def close_window():
            help_text = subprocess.run(['hyprctl','--help'],capture_output=True,text=True).stdout
            if 'eval <code>' in help_text:
                args = ['hyprctl','eval',f'hl.dispatch(hl.dsp.window.close({{window="pid:{gui.pid}"}}))']
            else:
                args = ['hyprctl','dispatch','closewindow',f'pid:{gui.pid}']
            # Close both the settings and lyric windows to the tray.
            for _ in range(2):
                result = subprocess.run(args,capture_output=True,text=True)
                require(result.returncode == 0, result.stdout + result.stderr)
                time.sleep(.2)
            eventually(lambda: not mapped_window())
            require(gui.poll() is None, 'Closing the lyrics window should preserve the dev branch tray process')
        check('GUI closes its window to the tray', close_window)
    stop(gui)
    cli_log.close()
    gui_log.close()
    def no_exceptions():
        for name in ('cli','gui'):
            log = Path(f'/tmp/omnilyrics-{name}-smoke.log').read_text()
            require('exception' not in log.lower() and 'fail:' not in log.lower(),log)
    check('No application exceptions in runtime logs', no_exceptions)
finally:
    if cli is not None: stop(cli)
    if gui is not None: stop(gui)
    loop.quit()
print(json.dumps(results,indent=2))
raise SystemExit(0 if all(r[1] for r in results) else 1)
