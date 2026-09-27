#!/usr/bin/env python3
"""Validate native release assets, then publish to GitHub or a maintainer's Cloudsmith repository."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def run(*args, capture=False):
    result = subprocess.run([str(a) for a in args], check=True, text=True,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout if capture else None


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def package_names(version):
    return {name for package in ('omnilyrics', 'omnilyrics-cli')
            for deb, native in (('amd64', 'x86_64'), ('arm64', 'aarch64'))
            for name in (f'{package}_{version}-1_{deb}.deb',
                         f'{package}-{version}-1.{native}.rpm',
                         f'{package}-{version}-1-{native}.pkg.tar.zst')}


def prepare(source, output, version):
    expected = package_names(version)
    found = {}
    manifests = sorted(source.glob('*/SHA256SUMS'))
    if len(manifests) != 2:
        raise ValueError('Expected exactly two architecture artifacts with SHA256SUMS')
    for manifest in manifests:
        for line in manifest.read_text().splitlines():
            match = re.fullmatch(r'([a-f0-9]{64})  ([^/\\\s]+)', line)
            if not match:
                raise ValueError('Invalid package checksum entry')
            sha, name = match.groups()
            path = manifest.parent / name
            if name not in expected or name in found or path.is_symlink() or digest(path) != sha:
                raise ValueError(f'Unexpected, duplicated or damaged package: {name}')
            found[name] = path
    if found.keys() != expected:
        raise ValueError('Missing GUI/CLI packages or architectures')
    if output.exists():
        raise ValueError('Use a fresh output directory')
    output.mkdir(parents=True)
    for name, path in found.items():
        shutil.copyfile(path, output / name)
    (output / 'SHA256SUMS-linux-native').write_text(''.join(
        f'{digest(output / name)}  {name}\n' for name in sorted(found)))
    print(f'Validated {len(found)} native release assets')


def release_assets(path, version):
    names = package_names(version)
    manifest = path / 'SHA256SUMS-linux-native'
    expected = ''.join(f'{digest(path / name)}  {name}\n' for name in sorted(names))
    if manifest.read_text() != expected:
        raise ValueError('Native release checksum mismatch')
    return [path / name for name in sorted(names)] + [manifest]


def github_upload(paths, repository, tag, dry_run):
    if dry_run:
        print(f'Would upload {len(paths)} assets to {repository} release {tag}; never overwrite assets')
        return
    release = json.loads(run('gh', 'release', 'view', tag, '--repo', repository,
                             '--json', 'assets', capture=True))
    existing = {a['name'] for a in release['assets']}
    with tempfile.TemporaryDirectory(prefix='omnilyrics-release-') as tmp:
        for path in paths:
            if path.name in existing:
                run('gh', 'release', 'download', tag, '--repo', repository, '--pattern', path.name, '--dir', tmp)
                if digest(Path(tmp) / path.name) != digest(path):
                    raise ValueError(f'Refusing to replace published asset: {path.name}')
                print(f'Already published: {path.name}')
            else:
                run('gh', 'release', 'upload', tag, path, '--repo', repository)


def cloudsmith_upload(paths, repository, dry_run):
    if not re.fullmatch(r'[a-z0-9][a-z0-9_-]*/[a-z0-9][a-z0-9_-]*', repository):
        raise ValueError('CLOUDSMITH_REPOSITORY must be owner/repository')
    if not dry_run and not os.environ.get('CLOUDSMITH_API_KEY'):
        raise ValueError('Set CLOUDSMITH_API_KEY in the linux-release environment')
    for path in paths:
        fmt = path.suffix[1:]
        if fmt not in ('deb', 'rpm'):
            continue
        # Cloudsmith signs RPMs, so their served checksum can differ from our input.
        # Retain the original digest as a tag for retry checks.
        source_tag = 'upstream-sha256-' + digest(path)
        target = repository + '/any-distro/any-version'
        if dry_run:
            print(f'Would publish {path.name} to {target} ({fmt}, no replacement)')
            continue
        query = f'filename:^{path.name}$'
        existing = json.loads(run('cloudsmith', 'list', 'packages', repository, '--query', query,
                                  '--page-all', '--output-format', 'json', capture=True))['data']
        if existing:
            matching = json.loads(run('cloudsmith', 'list', 'packages', repository,
                                      '--query', query + ' tag:^' + source_tag + '$', '--page-all',
                                      '--output-format', 'json', capture=True))['data']
            if len(existing) != 1 or len(matching) != 1 or not matching[0].get('is_sync_completed'):
                raise ValueError(f'Existing package differs or has not finished syncing: {path.name}')
            print(f'Already published: {path.name}')
            continue
        run('cloudsmith', 'push', fmt, target, path, '--no-republish', '--tags', source_tag)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('prepare', 'github', 'cloudsmith'))
    parser.add_argument('--version', required=True)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--repository', default='zzxzzk115/OmniLyrics')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+', args.version):
        parser.error('version must be X.Y.Z')
    try:
        if args.action == 'prepare':
            if args.output is None:
                parser.error('prepare requires --output')
            prepare(args.input, args.output, args.version)
        else:
            paths = release_assets(args.input, args.version)
            if args.action == 'github':
                github_upload(paths, args.repository, 'v' + args.version, args.dry_run)
            else:
                cloudsmith_upload(paths, args.repository, args.dry_run)
    except (ValueError, OSError) as error:
        parser.exit(1, str(error) + '\n')


if __name__ == '__main__':
    main()
