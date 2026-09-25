pragma Singleton

import QtQuick
import Quickshell
import Quickshell.Io
import qs.services

Singleton {
    id: root
    property var snapshot: ({})
    property string lastError: ""
    property bool receivedResponse: false
    property var pendingChange: null
    readonly property bool busy: (worker.running && worker.changing) || pendingChange !== null
    readonly property bool working: worker.running
    readonly property var ciderPlayer: MprisController.players.find(p => isCider(p)) ?? null
    readonly property string trackKey: (ciderPlayer?.trackTitle ?? "") + "\n" + (ciderPlayer?.trackArtist ?? "")
    readonly property string helper: decodeURIComponent(Qt.resolvedUrl("../scripts/cider_favorites.py").toString().replace(/^file:\/\//, ""))

    function isCider(player) {
        return Boolean(player?.dbusName?.toLowerCase().includes("cider"));
    }

    function normalize(text) {
        return String(text ?? "").trim().toLowerCase().replace(/\s+/g, " ")
            .replace(/\s*[,，、;]\s*/g, ",");
    }

    function matches(player) {
        return isCider(player) && snapshot.available === true
            && normalize(snapshot.title) === normalize(player.trackTitle)
            && normalize(snapshot.artist) === normalize(player.trackArtist);
    }

    function isFavorite(player) {
        return matches(player) && snapshot.favorite === true;
    }

    function refresh() {
        if (!ciderPlayer || worker.running || pendingChange !== null)
            return;
        receivedResponse = false;
        worker.changing = false;
        worker.command = ["python3", helper, "status"];
        worker.running = true;
    }

    function toggle(player) {
        if (!matches(player) || busy)
            return;
        lastError = "";
        const change = {trackId: snapshot.trackId, kind: snapshot.kind,
            favorite: !snapshot.favorite};
        if (worker.running) {
            // Finish the background read, then apply the user's intended value.
            // A second click is guarded by busy while the action is queued.
            pendingChange = change;
            return;
        }
        startChange(change);
    }

    function startChange(change) {
        if (!snapshot.available || snapshot.trackId !== change.trackId || snapshot.kind !== change.kind) {
            lastError = "track_changed";
            return;
        }
        receivedResponse = false;
        worker.changing = true;
        worker.command = ["python3", helper, "set", change.trackId,
            change.kind, change.favorite ? "1" : "0"];
        worker.running = true;
    }

    function label(player) {
        if (busy)
            return "正在更新收藏…";
        const error = lastError || snapshot.error;
        if (error === "auth_required")
            return "请授权 Cider 的播放与音乐库访问";
        if (error === "invalid_config")
            return "请检查 OmniLyrics 的 Cider 配置";
        if (error === "not_confirmed" || error === "request_failed")
            return "收藏未能确认，请稍后重试";
        if (error === "track_changed")
            return "歌曲已切换，正在刷新";
        if (!matches(player))
            return error ? "暂时无法连接 Cider" : "正在读取收藏状态…";
        return isFavorite(player) ? "取消收藏" : "收藏歌曲";
    }

    onTrackKeyChanged: {
        pendingChange = null;
        snapshot = ({});
        lastError = "";
        refresh();
    }
    Component.onCompleted: refresh()

    Process {
        id: worker
        property bool changing: false
        environment: ({ "LD_LIBRARY_PATH": null })
        stdout: SplitParser {
            onRead: line => {
                try {
                    const value = JSON.parse(line);
                    if (typeof value.available !== "boolean")
                        return;
                    root.receivedResponse = true;
                    root.snapshot = value;
                    if (worker.changing)
                        root.lastError = value.error ?? "";
                } catch (error) {}
            }
        }
        onExited: {
            if (!root.receivedResponse) {
                root.snapshot = ({available: false, error: "unavailable"});
                if (changing)
                    root.lastError = "unavailable";
            }
            if (root.pendingChange !== null) {
                Qt.callLater(() => {
                    const change = root.pendingChange;
                    if (change === null)
                        return;
                    // Start before clearing the queue so busy never briefly resets.
                    root.startChange(change);
                    root.pendingChange = null;
                });
            }
        }
    }

    Timer {
        interval: 4000
        running: root.ciderPlayer !== null
        repeat: true
        onTriggered: root.refresh()
    }

    IpcHandler {
        target: "ciderFavorites"
        function status(): string {
            return JSON.stringify({busy: root.busy, error: root.lastError, snapshot: root.snapshot});
        }
    }
}
