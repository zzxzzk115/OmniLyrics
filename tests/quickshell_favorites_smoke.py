"""Run the real QML service with a fake Cider helper; never change real music.

Usage: python3 tests/quickshell_favorites_smoke.py --qs /path/to/qs
Use the shell's wrapper when Quickshell requires a bundled Qt environment.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--qs", default=shutil.which("qs") or "quickshell")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]

with tempfile.TemporaryDirectory(prefix="omnilyrics-qml-test-") as folder:
    root = Path(folder)
    (root / "services").mkdir()
    (root / "scripts").mkdir()
    shutil.copy2(repo / "integrations/quickshell/CiderFavorites.qml", root / "services/CiderFavorites.qml")
    (root / "services/MprisController.qml").write_text('''pragma Singleton
import QtQuick
import Quickshell
Singleton {
    property QtObject player: QtObject {
        property string dbusName: "org.mpris.MediaPlayer2.cider.Mock"
        property string trackTitle: "Test song"
        property string trackArtist: "Test artist"
    }
    property var players: [player]
}
''')
    (root / "scripts/cider_favorites.py").write_text('''import json,sys,time
from pathlib import Path
p=Path(__file__).with_name("state.json")
state=json.loads(p.read_text()) if p.exists() else {"favorite":False,"writes":[]}
time.sleep(.25)
if sys.argv[1]=="set":
    assert sys.argv[2:4]==["test-id","song"]
    state["favorite"]=sys.argv[4]=="1"
    state["writes"].append(sys.argv[4])
    p.write_text(json.dumps(state))
print(json.dumps({"available":True,"trackId":"test-id","kind":"song",
    "title":"Test song","artist":"Test artist","favorite":state["favorite"]}))
''')
    (root / "shell.qml").write_text('''import QtQuick
import Quickshell
import qs.services
Scope {
    id: root
    property int step: 0
    Timer {
        interval: 10
        running: root.step < 6
        repeat: true
        onTriggered: {
            const service = CiderFavorites;
            const player = MprisController.player;
            if (root.step === 0 && service.matches(player) && !service.working) {
                root.step = 1; // Wait for the actual four-second polling timer.
            } else if (root.step === 1 && service.working) {
                if (service.busy || !service.matches(player)) {
                    console.log("FAIL background polling changed button readiness");
                    root.step = 6;
                    return;
                }
                console.log("PASS background polling keeps favorite button ready");
                service.toggle(player);
                service.toggle(player); // A rapid duplicate must not queue another write.
                root.step = 2;
            } else if (root.step === 2 && !service.busy && !service.working) {
                if (!service.isFavorite(player)) {
                    console.log("FAIL queued favorite was lost"); root.step = 6; return;
                }
                console.log("PASS click during polling favorites once");
                service.refresh();
                service.toggle(player);
                root.step = 3;
            } else if (root.step === 3 && !service.busy && !service.working) {
                if (service.isFavorite(player)) {
                    console.log("FAIL queued unfavorite was lost"); root.step = 6; return;
                }
                console.log("PASS click during polling unfavorites once");
                service.refresh();
                service.toggle(player);
                player.trackTitle = "Different song";
                root.step = 4;
            } else if (root.step === 4 && !service.working && !service.busy) {
                if (service.matches(player)) {
                    console.log("FAIL old snapshot matches changed song"); root.step = 6; return;
                }
                console.log("PASS queued action cancelled when the song changes");
                console.log("DONE");
                root.step = 6;
            }
        }
    }
}
''')
    log = root / "quickshell.log"
    with log.open("w") as output:
        process = subprocess.Popen([args.qs, "-p", str(root / "shell.qml")], stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 18
            while time.monotonic() < deadline:
                text = log.read_text()
                if "FAIL " in text or "DONE" in text or process.poll() is not None:
                    break
                time.sleep(.1)
            text = log.read_text()
            if "DONE" not in text or "FAIL " in text:
                raise SystemExit("Quickshell favorite regression failed:\n" + text)
            state = json.loads((root / "scripts/state.json").read_text())
            assert state["writes"] == ["1", "0"], "Unexpected duplicate or stale write"
            for line in text.splitlines():
                if "PASS " in line:
                    print(line[line.index("PASS "):])
            print("PASS no duplicate or stale library writes")
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
