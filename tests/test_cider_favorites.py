"""Exercise the Cider adapter against a local mock; never modify a real library."""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("cider_favorites", Path(__file__).resolve().parents[1]
    / "integrations/quickshell/cider_favorites.py")
cider = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cider)


class FavoritesTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                if cls.require_token and self.headers.get("apptoken") != "test-token":
                    self.send_response(403)
                    self.end_headers()
                    return
                if self.path.endswith("/playback/now-playing"):
                    cls.track_reads += 1
                    if cls.change_at == cls.track_reads:
                        cls.track_id = "new-song"
                    data = {"name": "Test song", "artistName": "Test artist",
                            "playParams": {"id": cls.track_id, "kind": "song"}}
                elif self.path.endswith("/library/now-playing/status"):
                    data = {"rating": cls.rating, "inLibrary": False}
                else:
                    self.send_response(404)
                    self.end_headers()
                    return
                self.send_json(data)

            def do_PUT(self):
                if cls.require_token and self.headers.get("apptoken") != "test-token":
                    self.send_response(403)
                    self.end_headers()
                    return
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                cls.writes.append((self.path, body))
                if cls.accept_changes:
                    cls.rating = body["rating"]
                self.send_json(body)

            def send_json(self, data):
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(json.dumps({"data": data}).encode())

        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join()

    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.config_folder = Path(folder.name)
        environment = patch.dict(os.environ, {"OMNILYRICS_CONFIG_DIR": folder.name,
            "CIDER_AUTH_MODE": "", "CIDER_API_TOKEN": "", "CIDER_TOKEN_FILE": ""})
        environment.start()
        self.addCleanup(environment.stop)
        cls = type(self)
        cls.track_id = "test-song"
        cls.track_reads = 0
        cls.change_at = 0
        cls.rating = 0
        cls.writes = []
        cls.accept_changes = True
        cls.require_token = True
        self.client = cider.CiderClient("test-token", self.server.server_port)

    def test_status_and_auth_header(self):
        result = self.client.status()
        self.assertTrue(result["available"])
        self.assertFalse(result["favorite"])
        self.assertEqual(result["trackId"], "test-song")
        self.assertEqual(self.writes, [])

    def test_favorite_and_unfavorite(self):
        self.assertTrue(self.client.set_favorite("test-song", "song", True)["favorite"])
        self.assertFalse(self.client.set_favorite("test-song", "song", False)["favorite"])
        self.assertEqual(self.writes, [
            ("/api/v2/library/now-playing/rating", {"rating": 1}),
            ("/api/v2/library/now-playing/rating", {"rating": 0})])

    def test_disliked_song_can_be_favorited(self):
        type(self).rating = -1
        self.assertFalse(self.client.status()["favorite"])
        self.assertTrue(self.client.set_favorite("test-song", "song", True)["favorite"])

    def test_no_token_when_cider_allows_it(self):
        type(self).require_token = False
        self.client.token = ""
        self.assertFalse(self.client.status()["favorite"])
        self.assertTrue(self.client.set_favorite("test-song", "song", True)["favorite"])

    def test_stale_click_does_not_write(self):
        with self.assertRaisesRegex(cider.FavoriteError, "track_changed"):
            self.client.set_favorite("old-song", "song", True)
        self.assertEqual(self.writes, [])

    def test_switch_during_read_does_not_mix_rating_and_song(self):
        type(self).change_at = 2
        with self.assertRaisesRegex(cider.FavoriteError, "track_changed"):
            self.client.status()

    def test_switch_after_write_never_retries_write_on_next_song(self):
        type(self).change_at = 2
        with self.assertRaisesRegex(cider.FavoriteError, "track_changed"):
            self.client.set_favorite("test-song", "song", True)
        self.assertEqual(len(self.writes), 1)

    def test_failed_library_update_is_not_reported_as_success(self):
        type(self).accept_changes = False
        with patch.object(cider.time, "sleep"), self.assertRaisesRegex(cider.FavoriteError, "not_confirmed"):
            self.client.set_favorite("test-song", "song", True)
        self.assertEqual(len(self.writes), 1)

    def test_auth_failure_does_not_expose_token(self):
        self.client.token = "invalid-secret"
        with self.assertRaisesRegex(cider.FavoriteError, "^auth_required$"):
            self.client.status()

    def test_invalid_rating_is_not_treated_as_unfavorited(self):
        type(self).rating = "1"
        with self.assertRaisesRegex(cider.FavoriteError, "invalid_response"):
            self.client.status()

    def test_token_file_and_environment_precedence(self):
        with tempfile.TemporaryDirectory() as folder:
            token_path = Path(folder) / "token"
            token_path.write_text("file-token\n")
            with patch.dict(os.environ, {"CIDER_API_TOKEN": "", "CIDER_TOKEN_FILE": str(token_path)}):
                self.assertEqual(cider.read_token(), "file-token")
                with patch.dict(os.environ, {"CIDER_API_TOKEN": "env-token"}):
                    self.assertEqual(cider.read_token(), "env-token")
                token_path.unlink()
                self.assertEqual(cider.read_token(), "")

    def test_multiline_token_rejected(self):
        with patch.dict(os.environ, {"CIDER_API_TOKEN": "token\ninjected"}):
            with self.assertRaisesRegex(cider.FavoriteError, "auth_required"):
                cider.read_token()

    def test_shared_none_mode_ignores_saved_and_environment_token(self):
        (self.config_folder / "cider-token").write_text("saved-token")
        (self.config_folder / "config.json").write_text('{"version":1,"cider":{"authentication":"none"}}')
        with patch.dict(os.environ, {"CIDER_API_TOKEN": "environment-token"}):
            self.assertEqual(cider.read_token(), "")

    def test_shared_token_mode_and_live_reload(self):
        (self.config_folder / "cider-token").write_text("saved-token")
        with patch.dict(os.environ, {"CIDER_TOKEN_FILE": str(self.config_folder / "cider-token")}):
            self.assertEqual(cider.read_token(), "saved-token")
            (self.config_folder / "config.json").write_text('{"cider":{"authentication":"none"}}')
            self.assertEqual(cider.read_token(), "")
            with patch.dict(os.environ, {"CIDER_AUTH_MODE": "token"}):
                self.assertEqual(cider.read_token(), "saved-token")

    def test_invalid_shared_config_is_not_ignored(self):
        (self.config_folder / "config.json").write_text('not-json')
        with self.assertRaisesRegex(cider.FavoriteError, "invalid_config"):
            cider.read_token()


if __name__ == "__main__":
    unittest.main()
