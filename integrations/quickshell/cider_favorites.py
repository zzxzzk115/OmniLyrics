#!/usr/bin/env python3
"""Small, local-only Cider V4 favorite adapter. Never emit the app token."""

import argparse
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.request


class FavoriteError(Exception):
    pass


class CiderClient:
    def __init__(self, token, port=10767):
        self.token = token
        self.base = f"http://127.0.0.1:{port}/api/v2"
        # Local Cider traffic must not pass through an environment HTTP proxy.
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def request(self, path, method="GET", body=None):
        headers = {"Accept": "application/json"}
        if self.token:
            headers["apptoken"] = self.token
        payload = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            payload = json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=payload,
                                     headers=headers, method=method)
        try:
            with self.http.open(req, timeout=3) as response:
                envelope = json.load(response)
        except urllib.error.HTTPError as error:
            error.close()
            if error.code in (401, 403):
                raise FavoriteError("auth_required") from None
            raise FavoriteError("request_failed") from None
        except (urllib.error.URLError, TimeoutError, OSError):
            raise FavoriteError("unavailable") from None
        except (ValueError, UnicodeError):
            raise FavoriteError("invalid_response") from None
        if not isinstance(envelope, dict) or not isinstance(envelope.get("data"), dict):
            raise FavoriteError("invalid_response")
        return envelope["data"]

    def track(self):
        data = self.request("/playback/now-playing")
        params = data.get("playParams") or {}
        if not isinstance(params, dict) or not params.get("id") or not data.get("name"):
            raise FavoriteError("no_track")
        return {
            "trackId": str(params["id"]),
            "kind": str(params.get("kind", "song")),
            "title": str(data["name"]),
            "artist": str(data.get("artistName", "")),
        }

    @staticmethod
    def identity(track):
        return track["kind"], track["trackId"]

    def status(self):
        before = self.track()
        status = self.request("/library/now-playing/status")
        after = self.track()
        if self.identity(before) != self.identity(after):
            raise FavoriteError("track_changed")
        rating = status.get("rating")
        if type(rating) is not int or rating not in (-1, 0, 1):
            raise FavoriteError("invalid_response")
        return {"available": True, **after, "favorite": rating == 1, "rating": rating}

    def set_favorite(self, track_id, kind, favorite):
        # The Cider endpoint acts on now-playing, so recheck immediately before
        # writing. A stale widget must never change a newly selected song.
        current = self.track()
        if self.identity(current) != (kind, track_id):
            raise FavoriteError("track_changed")
        rating = 1 if favorite else 0
        self.request("/library/now-playing/rating", "PUT", {"rating": rating})
        # Cider acknowledges dispatch before Apple Music finishes the operation.
        # Only report success once the actual rating has been read back.
        for attempt in range(7):
            if attempt:
                time.sleep(0.5)
            snapshot = self.status()
            if self.identity(snapshot) != (kind, track_id):
                raise FavoriteError("track_changed")
            if snapshot["rating"] == rating:
                return snapshot
        raise FavoriteError("not_confirmed")


def read_token():
    folder = Path(os.environ.get("OMNILYRICS_CONFIG_DIR") or str(Path.home() / ".config/omnilyrics"))
    path = Path(os.environ.get("CIDER_TOKEN_FILE") or str(folder / "cider-token"))
    direct = os.environ.get("CIDER_API_TOKEN", "").strip()
    mode = os.environ.get("CIDER_AUTH_MODE", "").strip()
    if not mode:
        try:
            settings = json.loads((folder / "config.json").read_text())
            mode = settings.get("cider", {}).get("authentication")
        except FileNotFoundError:
            pass
        except (OSError, ValueError, AttributeError, TypeError):
            raise FavoriteError("invalid_config") from None
    if mode is None or mode == "":
        mode = "token" if direct or os.environ.get("CIDER_TOKEN_FILE") or path.exists() else "none"
    if mode == "none":
        return ""
    if mode != "token":
        raise FavoriteError("invalid_config")
    if direct:
        if "\n" in direct or "\r" in direct:
            raise FavoriteError("auth_required")
        return direct
    try:
        token = path.read_text().strip()
    except FileNotFoundError:
        # Cider may explicitly allow local clients without API tokens.
        return ""
    except OSError:
        raise FavoriteError("auth_required") from None
    if not token or "\n" in token or "\r" in token:
        raise FavoriteError("auth_required")
    return token


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("status")
    change = commands.add_parser("set")
    change.add_argument("track_id")
    change.add_argument("kind")
    change.add_argument("favorite", choices=("0", "1"))
    args = parser.parse_args()
    try:
        client = CiderClient(read_token())
        result = (client.status() if args.command == "status" else
                  client.set_favorite(args.track_id, args.kind, args.favorite == "1"))
    except FavoriteError as error:
        result = {"available": False, "error": str(error)}
    print(json.dumps(result, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
