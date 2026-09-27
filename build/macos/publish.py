#!/usr/bin/env python3
"""Upload verified macOS release artifacts without replacing published files."""
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import re
import subprocess
import tempfile
import zipfile

ARCHIVES = tuple(f'omnilyrics-gui-osx-{arch}-signed.zip' for arch in ('arm64', 'x64'))
MANIFEST = 'SHA256SUMS-macos-signed'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def release_assets(directory, tag):
    if not re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+', tag):
        raise ValueError('Expected a stable vMAJOR.MINOR.PATCH release tag')
    paths = [directory / name for name in (*ARCHIVES, MANIFEST, 'omnilyrics.rb')]
    if any(path.is_symlink() or not path.is_file() for path in paths):
        raise ValueError('Expected both signed ZIPs, their checksum manifest and the Cask')
    checksums = {}
    for line in (directory / MANIFEST).read_text().splitlines():
        match = re.fullmatch(r'([a-f0-9]{64})  ([^/\\\s]+)', line)
        if not match or match[2] not in ARCHIVES or match[2] in checksums:
            raise ValueError('Invalid macOS checksum manifest')
        checksums[match[2]] = match[1]
    if checksums.keys() != set(ARCHIVES):
        raise ValueError('Missing architecture in the macOS checksum manifest')
    for name in ARCHIVES:
        path = directory / name
        if digest(path) != checksums[name]:
            raise ValueError(f'Checksum mismatch: {name}')
        with zipfile.ZipFile(path) as archive:
            plist = 'OmniLyrics.app/Contents/Info.plist'
            if archive.namelist().count(plist) != 1:
                raise ValueError(f'Expected one app bundle in {name}')
            info = plistlib.loads(archive.read(plist))
            if (info.get('CFBundleShortVersionString') != tag[1:]
                    or info.get('CFBundleVersion') != tag[1:]
                    or info.get('CFBundleIdentifier') != 'io.github.zzxzzk115.OmniLyrics'):
                raise ValueError(f'App bundle does not match release {tag}: {name}')
    cask = (directory / 'omnilyrics.rb').read_text()
    if f'  version "{tag[1:]}"' not in cask or any(sha not in cask for sha in checksums.values()):
        raise ValueError('Cask version or checksums do not match the signed archives')
    return paths


def run(*args, capture=False):
    result = subprocess.run([str(arg) for arg in args], check=True, text=True,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout if capture else None


def publish(directory, tag, repository, dry_run=False):
    paths = release_assets(directory, tag)
    if dry_run:
        print(f'Would upload {len(paths)} verified assets to {repository} {tag}')
        return
    release = json.loads(run('gh', 'release', 'view', tag, '--repo', repository,
                             '--json', 'assets,isDraft,isPrerelease', capture=True))
    if release['isDraft'] or release['isPrerelease']:
        raise ValueError('Expected a published stable release')
    existing = {asset['name'] for asset in release['assets']}
    # Check every collision before uploading anything. Signing timestamps change
    # between builds; retry a failed publish job with its original signed artifact.
    with tempfile.TemporaryDirectory(prefix='omnilyrics-macos-release-') as temporary:
        for path in paths:
            if path.name not in existing:
                continue
            run('gh', 'release', 'download', tag, '--repo', repository,
                '--pattern', path.name, '--dir', temporary)
            if digest(Path(temporary) / path.name) != digest(path):
                raise ValueError(f'Refusing to replace published asset: {path.name}. '
                                 'Retry the failed publish job with its original signed artifact.')
    for path in paths:
        if path.name in existing:
            print(f'Already published: {path.name}')
        else:
            run('gh', 'release', 'upload', tag, path, '--repo', repository)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--repository', default='zzxzzk115/OmniLyrics')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    publish(args.input, args.tag, args.repository, args.dry_run)


if __name__ == '__main__':
    main()
