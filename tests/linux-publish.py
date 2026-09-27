#!/usr/bin/env python3
"""Exercise release integrity and retry safeguards without credentials or public writes."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'build/linux' / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


release = module('linux_publish', 'publish.py')
aur = module('aur_publish', 'publish-aur.py')


class PublishingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='omnilyrics-publish-test-')
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source, self.output = self.root / 'artifacts', self.root / 'release'
        for arch in ('x64', 'arm64'):
            folder = self.source / arch
            folder.mkdir(parents=True)
            names = [n for n in release.package_names('1.2.3')
                     if ('amd64' in n or 'x86_64' in n) == (arch == 'x64')]
            for name in names:
                (folder / name).write_bytes(name.encode())
            (folder / 'SHA256SUMS').write_text(''.join(
                f'{release.digest(folder / n)}  {n}\n' for n in sorted(names)))

    def prepare(self):
        release.prepare(self.source, self.output, '1.2.3')
        return release.release_assets(self.output, '1.2.3')

    def test_complete_release_and_separate_checksum_manifest(self):
        paths = self.prepare()
        self.assertEqual(len(paths), 13)
        self.assertEqual(paths[-1].name, 'SHA256SUMS-linux-native')
        self.assertFalse((self.output / 'SHA256SUMS').exists())

    def test_tampered_missing_or_wrong_revision_never_publishes(self):
        package = next((self.source / 'x64').glob('*.deb'))
        package.write_bytes(b'tampered')
        with self.assertRaises(ValueError):
            self.prepare()
        self.assertFalse(self.output.exists())
        manifest = self.source / 'x64/SHA256SUMS'
        manifest.write_text(manifest.read_text().replace('-1_', '-2_'))
        with self.assertRaises(ValueError):
            self.prepare()

    def test_missing_architecture(self):
        (self.source / 'arm64/SHA256SUMS').unlink()
        with self.assertRaises(ValueError):
            self.prepare()

    def test_manifest_path_traversal(self):
        (self.source / 'x64/SHA256SUMS').write_text('a' * 64 + '  ../outside.deb\n')
        with self.assertRaises(ValueError):
            self.prepare()

    def test_modified_staged_assets_rejected(self):
        paths = self.prepare()
        paths[0].write_bytes(b'changed after preparation')
        with self.assertRaises(ValueError):
            release.release_assets(self.output, '1.2.3')

    def test_dry_run_has_no_network_or_credentials(self):
        paths = self.prepare()
        with patch.object(release, 'run', side_effect=AssertionError('Network attempted')):
            release.github_upload(paths, 'zzxzzk115/OmniLyrics', 'v1.2.3', True)
            release.cloudsmith_upload(paths, 'example/omnilyrics', True)
        with self.assertRaises(ValueError):
            release.cloudsmith_upload(paths, 'example/repo/extra', True)

    def test_cloudsmith_retry_skips_identical_signed_package(self):
        paths = [p for p in self.prepare() if p.suffix == '.rpm'][:1]
        ready = json.dumps({'data': [{'is_sync_completed': True}]})
        with patch.dict(os.environ, {'CLOUDSMITH_API_KEY': 'test-placeholder'}), \
                patch.object(release, 'run', return_value=ready) as command:
            release.cloudsmith_upload(paths, 'example/omnilyrics', False)
        self.assertEqual(command.call_count, 2)
        self.assertIn(' tag:^upstream-sha256-', command.call_args.args[5])
        self.assertTrue(all(call.args[1] == 'list' for call in command.call_args_list))

    def test_cloudsmith_conflicting_or_unsynced_package_rejected(self):
        paths = [p for p in self.prepare() if p.suffix == '.deb'][:1]
        for response in ({'data': []}, {'data': [{'is_sync_completed': False}]}):
            with patch.dict(os.environ, {'CLOUDSMITH_API_KEY': 'test-placeholder'}), \
                    patch.object(release, 'run', side_effect=[json.dumps({'data': [{}]}), json.dumps(response)]):
                with self.assertRaises(ValueError):
                    release.cloudsmith_upload(paths, 'example/omnilyrics', False)

    def test_cloudsmith_upload_only_deb_and_rpm_without_replacement(self):
        paths = self.prepare()
        with patch.dict(os.environ, {'CLOUDSMITH_API_KEY': 'test-placeholder'}), \
                patch.object(release, 'run', return_value='{"data": []}') as command:
            release.cloudsmith_upload(paths, 'example/omnilyrics', False)
        uploads = [call.args for call in command.call_args_list if call.args[1] == 'push']
        self.assertEqual(len(uploads), 8)
        self.assertTrue(all('--no-republish' in call for call in uploads))

    def test_github_never_replaces_different_existing_asset(self):
        path = self.prepare()[0]
        def response(*args, capture=False):
            if args[2] == 'view':
                return json.dumps({'assets': [{'name': path.name}]})
            if args[2] == 'download':
                (Path(args[-1]) / path.name).write_bytes(b'previous release bytes')
                return
            self.fail('Should not upload over a published asset')
        with patch.object(release, 'run', side_effect=response):
            with self.assertRaises(ValueError):
                release.github_upload([path], 'example/omnilyrics', 'v1.2.3', False)

    def test_aur_real_git_publish_retry_and_no_downgrade(self):
        remote, recipe = self.root / 'aur.git', self.root / 'omnilyrics-bin'
        subprocess.run(['git', 'init', '--bare', '--initial-branch=master', str(remote)],
                       check=True, stdout=subprocess.DEVNULL)
        recipe.mkdir()
        (recipe / '.SRCINFO').write_text('pkgbase = omnilyrics-bin\n\tpkgver = 1.2.3\n\tpkgrel = 1\n')
        (recipe / 'PKGBUILD').write_text('pkgname=omnilyrics-bin\npkgver=1.2.3\n')
        aur.publish(recipe, str(remote), False)
        first = aur.git(remote, 'rev-parse', 'master')
        aur.publish(recipe, str(remote), False)
        self.assertEqual(aur.git(remote, 'rev-parse', 'master'), first)
        (recipe / '.SRCINFO').write_text('pkgver = 1.2.3\n\tpkgrel = 2\n')
        aur.publish(recipe, str(remote), False)
        first = aur.git(remote, 'rev-parse', 'master')
        (recipe / '.SRCINFO').write_text('pkgver = 1.2.3\n\tpkgrel = 1\n')
        with self.assertRaises(ValueError):
            aur.publish(recipe, str(remote), False)
        (recipe / '.SRCINFO').write_text('pkgver = 1.2.2\n\tpkgrel = 1\n')
        with self.assertRaises(ValueError):
            aur.publish(recipe, str(remote), False)
        self.assertEqual(aur.git(remote, 'rev-parse', 'master'), first)


if __name__ == '__main__':
    unittest.main()
