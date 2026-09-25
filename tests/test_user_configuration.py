"""Check the built CLI and the Quickshell helper against the same private config."""
import importlib.util
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "src/OmniLyrics.Cli/bin/Release/net10.0/linux-x64/OmniLyrics.Cli"
SPEC = importlib.util.spec_from_file_location("cider_config_helper", ROOT / "integrations/quickshell/cider_favorites.py")
helper = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(helper)


class ConfigurationTest(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix="omnilyrics-cli-config-")
        self.addCleanup(directory.cleanup)
        self.folder = Path(directory.name)
        self.env = {**os.environ, "OMNILYRICS_CONFIG_DIR": directory.name,
                    "OMNILYRICS_LANGUAGE": "",
                    "CIDER_API_TOKEN": "", "CIDER_TOKEN_FILE": "", "CIDER_AUTH_MODE": ""}

    def cli(self, *args, input=""):
        return subprocess.run([str(CLI), "config", *args], input=input, text=True,
                              env=self.env, capture_output=True, timeout=8)

    def test_token_via_stdin_is_private_and_never_echoed(self):
        result = self.cli("cider", "token", "--stdin", input="fake-configuration-secret\n")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn("fake-configuration-secret", result.stdout + result.stderr)
        token_file = self.folder / "cider-token"
        self.assertEqual(stat.S_IMODE(token_file.stat().st_mode), 0o600)
        config = (self.folder / "config.json").read_text()
        self.assertNotIn("fake-configuration-secret", config)
        self.assertEqual(json.loads(config)["cider"]["authentication"], "token")
        result = self.cli("show")
        self.assertIn("configured (hidden)", result.stdout)
        self.assertNotIn("fake-configuration-secret", result.stdout + result.stderr)
        with patch.dict(os.environ, self.env):
            self.assertEqual(helper.read_token(), "fake-configuration-secret")

    def test_none_mode_is_shared_and_retains_unused_token(self):
        self.assertEqual(self.cli("cider", "token", "--stdin", input="saved-test").returncode, 0)
        self.assertEqual(self.cli("cider", "none").returncode, 0)
        self.assertTrue((self.folder / "cider-token").exists())
        self.assertIn("Cider authentication: none", self.cli("show").stdout)
        with patch.dict(os.environ, self.env):
            self.assertEqual(helper.read_token(), "")

    def test_blank_keeps_saved_token(self):
        self.cli("cider", "token", "--stdin", input="saved-test")
        self.assertEqual(self.cli("cider", "token", "--stdin", input="\n").returncode, 0)
        self.assertEqual((self.folder / "cider-token").read_text().strip(), "saved-test")

    def test_empty_new_token_does_not_save(self):
        self.assertNotEqual(self.cli("cider", "token", "--stdin").returncode, 0)
        self.assertFalse((self.folder / "config.json").exists())

    def test_rejects_multiline_token_without_echoing(self):
        result = self.cli("cider", "token", "--stdin", input="private-first\nprivate-second")
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("private-first", result.stdout + result.stderr)
        self.assertFalse((self.folder / "cider-token").exists())

    def test_no_token_as_argument(self):
        result = self.cli("cider", "token", "must-not-echo-this")
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("must-not-echo-this", result.stdout + result.stderr)

    def test_corrupt_config_is_not_overwritten(self):
        p = self.folder / "config.json"
        p.write_text("broken-json")
        self.assertNotEqual(self.cli("cider", "none").returncode, 0)
        self.assertEqual(p.read_text(), "broken-json")

    def test_cider_integration_preserves_authentication(self):
        self.cli('cider','token','--stdin',input='saved-test')
        self.assertEqual(self.cli('cider','integration','webapi').returncode,0)
        config=json.loads((self.folder/'config.json').read_text())
        self.assertEqual(config['cider'],{'authentication':'token','integration':'webapi'})
        self.assertNotEqual(self.cli('cider','integration','unknown').returncode,0)

    def test_server_settings_preserve_cider_and_validate_inputs(self):
        self.cli('cider','none')
        self.assertEqual(self.cli('server','lan').returncode,0)
        self.assertEqual(self.cli('server','ports','28000','33000').returncode,0)
        self.assertEqual(self.cli('server','target','192.0.2.2').returncode,0)
        config=json.loads((self.folder/'config.json').read_text())
        self.assertEqual(config['cider']['authentication'],'none')
        self.assertEqual(config['server'],{'listenAddress':'0.0.0.0','httpPort':28000,'udpPort':33000,'controlHost':'192.0.2.2'})
        self.assertNotEqual(self.cli('server','target','0.0.0.0').returncode,0)
        self.assertNotEqual(self.cli('server','ports','0','70000').returncode,0)

    def test_global_lyrics_preferences_need_no_player_configuration(self):
        self.assertEqual(self.cli('lyrics','prefetch','off').returncode,0)
        self.assertEqual(self.cli('lyrics','count','8').returncode,0)
        saved=json.loads((self.folder/'config.json').read_text())
        self.assertEqual(saved['lyrics']['prefetch'],False)
        self.assertEqual(saved['lyrics']['prefetchCount'],8)
        self.assertEqual(saved['lyrics']['searchStrategy'],'fallback')
        self.assertNotIn('cider',saved)
        self.assertIn('karaoke first',self.cli('show').stdout)
        self.assertNotEqual(self.cli('lyrics','count','0').returncode,0)
        self.assertNotEqual(self.cli('lyrics','count','21').returncode,0)
        self.cli('cider','none')
        self.assertEqual(json.loads((self.folder/'config.json').read_text())['lyrics'],saved['lyrics'])

    def test_matching_policy_is_shared_and_validated(self):
        for key,value in [('strategy','quick'),('match','strict'),('sources','qq,netease'),('prefer','netease')]:
            self.assertEqual(self.cli('lyrics',key,value).returncode,0)
        path=self.folder/'config.json'
        before=path.read_text()
        settings=json.loads(before)['lyrics']
        self.assertEqual(settings['searchStrategy'],'quick')
        self.assertEqual(settings['matchMode'],'strict')
        self.assertFalse(settings['playerSource'])
        self.assertTrue(settings['qqMusicSource'])
        self.assertEqual(settings['preferredSource'],'netease')
        self.assertIn('matching: strict',self.cli('show').stdout)
        for key,value in [('strategy','unknown'),('match','guess'),('sources',''),('sources','unknown'),('prefer','unknown')]:
            self.assertNotEqual(self.cli('lyrics',key,value).returncode,0)
            self.assertEqual(path.read_text(),before)

    def test_language_is_shared_and_configuration_remains_intact(self):
        self.env.pop('OMNILYRICS_LANGUAGE', None)
        self.cli('lyrics','count','7')
        result=self.cli('language','zh-CN')
        self.assertEqual(result.returncode,0)
        self.assertIn('语言已保存',result.stdout)
        self.assertIn('配置文件',self.cli('show').stdout)
        self.assertIn('预取数量',self.cli('lyrics','count','30').stderr)
        self.assertEqual(self.cli('language','en').returncode,0)
        self.assertIn('Configuration:',self.cli('show').stdout)
        saved=json.loads((self.folder/'config.json').read_text())
        self.assertEqual(saved['lyrics']['prefetchCount'],7)
        self.assertNotEqual(self.cli('language','unsupported').returncode,0)


if __name__ == "__main__":
    unittest.main()
