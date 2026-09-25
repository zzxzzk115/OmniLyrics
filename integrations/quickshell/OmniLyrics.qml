pragma Singleton

import QtQuick
import Quickshell
import Quickshell.Io

Singleton {
    id: root
    property var snapshot: ({})
    property bool connected: false

    function normalize(text) {
        return String(text ?? "").trim().toLowerCase().replace(/\s+/g, " ")
            .replace(/\s*[,，、;]\s*/g, ",");
    }

    function matchesPlayer(player) {
        if (!connected || !snapshot.available || !player)
            return false;
        if (normalize(snapshot.title) !== normalize(player.trackTitle))
            return false;
        const source = normalize(snapshot.sourceApp);
        const bus = normalize(player.dbusName);
        if (source.startsWith("org.mpris.") && source !== bus)
            return false;
        if (source === "cider" && !bus.includes("cider"))
            return false;
        const artist = normalize(player.trackArtist);
        return !artist || !snapshot.artist || artist === normalize(snapshot.artist);
    }

    function currentFor(player) {
        return matchesPlayer(player) ? (snapshot.currentLine ?? "") : "";
    }

    function nextFor(player) {
        return matchesPlayer(player) ? (snapshot.nextLine ?? "") : "";
    }

    function placeholderFor(player) {
        if (!player)
            return "等待播放";
        if (!matchesPlayer(player) || snapshot.loading)
            return "正在加载歌词…";
        return snapshot.hasLyrics ? "♪" : "暂无歌词";
    }

    Process {
        id: lyricProcess
        command: [Quickshell.env("OMNILYRICS_EXECUTABLE") ||
            Quickshell.env("HOME") + "/GitHub/OmniLyrics/src/OmniLyrics.Cli/bin/Release/net10.0/linux-x64/OmniLyrics.Cli",
            "--mode", "json"]
        // The shell's bundled Qt libraries must not override .NET's native libraries.
        environment: ({ "LD_LIBRARY_PATH": null })
        running: true
        stdout: SplitParser {
            onRead: line => {
                try {
                    const value = JSON.parse(line);
                    if (typeof value.available !== "boolean")
                        return;
                    root.snapshot = value;
                    root.connected = true;
                } catch (error) {
                    // Ignore non-JSON diagnostic output.
                }
            }
        }
        stderr: SplitParser {
            onRead: line => console.warn("[OmniLyrics] " + line)
        }
        onExited: {
            root.connected = false;
            root.snapshot = ({});
        }
    }

    Timer {
        interval: 5000
        running: !lyricProcess.running
        repeat: true
        onTriggered: lyricProcess.running = true
    }

    IpcHandler {
        target: "lyrics"
        function status(): string {
            return JSON.stringify({connected: root.connected, snapshot: root.snapshot});
        }
    }
}
