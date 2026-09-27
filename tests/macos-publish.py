#!/usr/bin/env python3
"""Check macOS publication integrity and retries without Apple or GitHub credentials."""
import importlib.util
import json
from pathlib import Path
import plistlib
import shutil
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('macos_publish', ROOT / 'build/macos/publish.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class PublishingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='omnilyrics-macos-test-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for name in release.ARCHIVES:
            self.bundle(name)
        self.manifest()

    def bundle(self, name, version='1.2.3'):
        with zipfile.ZipFile(self.root / name, 'w') as archive:
            archive.writestr('OmniLyrics.app/Contents/Info.plist', plistlib.dumps({
                'CFBundleShortVersionString': version, 'CFBundleVersion': version,
                'CFBundleIdentifier': 'io.github.zzxzzk115.OmniLyrics'}))

    def manifest(self):
        hashes = [release.digest(self.root / name) for name in release.ARCHIVES]
        (self.root / release.MANIFEST).write_text(''.join(
            f'{sha}  {name}\n' for name, sha in zip(release.ARCHIVES, hashes)))
        (self.root / 'omnilyrics.rb').write_text('  version "1.2.3"\n' + '\n'.join(hashes))

    def invoke(self, dry_run=False):
        release.publish(self.root, 'v1.2.3', 'example/omnilyrics', dry_run)

    def test_valid_assets_and_dry_run_do_not_need_credentials(self):
        with patch.object(release, 'run', side_effect=AssertionError('Network attempted')):
            self.invoke(True)
        self.assertEqual(len(release.release_assets(self.root, 'v1.2.3')), 4)

    def test_damaged_archive_is_rejected_before_network(self):
        (self.root / release.ARCHIVES[0]).write_bytes(b'damaged')
        with patch.object(release, 'run', side_effect=AssertionError('Network attempted')):
            with self.assertRaisesRegex(ValueError, 'Checksum mismatch'):
                self.invoke()

    def test_missing_architecture_is_rejected(self):
        (self.root / release.ARCHIVES[0]).unlink()
        with self.assertRaises(ValueError):
            self.invoke(True)

    def test_duplicate_or_traversing_manifest_entries_are_rejected(self):
        manifest = self.root / release.MANIFEST
        original = manifest.read_text()
        for text in (original + original.splitlines()[0] + '\n',
                     original.replace(release.ARCHIVES[0], '../outside.zip')):
            manifest.write_text(text)
            with self.assertRaisesRegex(ValueError, 'Invalid macOS checksum'):
                self.invoke(True)

    def test_wrong_bundle_version_is_rejected_even_with_valid_hash(self):
        self.bundle(release.ARCHIVES[0], '1.2.2')
        self.manifest()
        with self.assertRaisesRegex(ValueError, 'does not match release'):
            self.invoke(True)

    def test_wrong_cask_is_rejected(self):
        (self.root / 'omnilyrics.rb').write_text('  version "1.2.2"')
        with self.assertRaisesRegex(ValueError, 'Cask version or checksums'):
            self.invoke(True)

    def test_unstable_tags_are_rejected(self):
        for tag in ('1.2.3', 'v1.2.3-rc1', '../v1.2.3'):
            with self.assertRaises(ValueError):
                release.release_assets(self.root, tag)

    def test_draft_or_prerelease_is_not_updated(self):
        for draft, prerelease in ((True, False), (False, True)):
            value = json.dumps({'assets': [], 'isDraft': draft, 'isPrerelease': prerelease})
            with patch.object(release, 'run', return_value=value) as command:
                with self.assertRaisesRegex(ValueError, 'published stable release'):
                    self.invoke()
                self.assertEqual(command.call_count, 1)

    def test_retry_reuses_identical_assets_and_leaves_portable_manifest_alone(self):
        def command(*args, capture=False):
            if args[2] == 'view':
                return json.dumps({'assets': [{'name': release.ARCHIVES[0]}, {'name': 'SHA256SUMS'}],
                                   'isDraft': False, 'isPrerelease': False})
            if args[2] == 'download':
                shutil.copyfile(self.root / release.ARCHIVES[0], Path(args[-1]) / release.ARCHIVES[0])
        with patch.object(release, 'run', side_effect=command) as calls:
            self.invoke()
        uploads = [c.args for c in calls.call_args_list if c.args[2] == 'upload']
        self.assertEqual({Path(c[4]).name for c in uploads},
                         {release.ARCHIVES[1], release.MANIFEST, 'omnilyrics.rb'})
        self.assertTrue(all('--clobber' not in c for c in uploads))

    def test_conflict_in_last_asset_prevents_all_uploads(self):
        def command(*args, capture=False):
            if args[2] == 'view':
                return json.dumps({'assets': [{'name': 'omnilyrics.rb'}],
                                   'isDraft': False, 'isPrerelease': False})
            if args[2] == 'download':
                (Path(args[-1]) / 'omnilyrics.rb').write_text('previous release')
                return
            self.fail('Must check every collision before uploading anything')
        with patch.object(release, 'run', side_effect=command):
            with self.assertRaisesRegex(ValueError, 'Refusing to replace'):
                self.invoke()


if __name__ == '__main__':
    unittest.main()
