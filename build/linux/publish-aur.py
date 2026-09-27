#!/usr/bin/env python3
"""Push the two generated recipes to AUR; never force-push or downgrade a package."""
import argparse
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import tempfile


def git(folder, *args):
    return subprocess.run(['git', '-C', str(folder), *args], check=True, text=True,
                          stdout=subprocess.PIPE).stdout.strip()


def version(folder):
    value = re.search(r'^\s*pkgver = (\d+\.\d+\.\d+)\s*$',
                      (folder / '.SRCINFO').read_text(), re.M)
    if not value:
        raise ValueError('Missing or invalid .SRCINFO version')
    info = (folder / '.SRCINFO').read_text()
    revision = re.search(r'^\s*pkgrel = ([1-9][0-9]*)\s*$', info, re.M)
    if not revision or re.search(r'^\s*epoch = [1-9]', info, re.M):
        raise ValueError('Unsupported package revision or epoch; inspect AUR before updating')
    return (*map(int, value[1].split('.')), int(revision[1]))


def publish(recipe, remote, dry_run):
    incoming = version(recipe)
    if dry_run:
        print(f'Would publish {recipe.name} {".".join(map(str, incoming[:3]))} to {remote}')
        return
    with tempfile.TemporaryDirectory(prefix='omnilyrics-aur-git-') as tmp:
        checkout = Path(tmp)
        subprocess.run(['git', 'clone', remote, tmp], check=True)
        if (checkout / '.SRCINFO').exists() and version(checkout) > incoming:
            raise ValueError(f'Refusing to downgrade {recipe.name}')
        # Only replace files owned by the generated recipe. Preserve unrelated maintainer files.
        for source in recipe.iterdir():
            if not source.is_file() or source.is_symlink() or source.name == '.git':
                raise ValueError(f'Invalid recipe resource: {source.name}')
            target = checkout / source.name
            if target.is_symlink():
                target.unlink()
            shutil.copyfile(source, target)
        git(checkout, 'add', '--', *(p.name for p in recipe.iterdir()))
        if not git(checkout, 'diff', '--cached', '--name-only'):
            print(f'Already published: {recipe.name}')
            return
        git(checkout, '-c', 'user.name=Lazy_V', '-c',
            'user.email=33739170+zzxzzk115@users.noreply.github.com',
            'commit', '-m', f'Update to {".".join(map(str, incoming[:3]))}')
        git(checkout, 'push', 'origin', 'HEAD:master')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--recipes', type=Path, required=True)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    recipes = [args.recipes / name for name in ('omnilyrics-bin', 'omnilyrics-cli-bin')]
    for recipe in recipes:
        version(recipe)
        if not (recipe / 'PKGBUILD').is_file():
            parser.error(f'Missing {recipe.name}/PKGBUILD')
    if args.dry_run:
        for recipe in recipes:
            publish(recipe, f'ssh://aur@aur.archlinux.org/{recipe.name}.git', True)
        return
    key = os.environ.get('AUR_SSH_PRIVATE_KEY', '')
    hosts = os.environ.get('AUR_KNOWN_HOSTS', '')
    if not key or not hosts:
        parser.error('Set AUR_SSH_PRIVATE_KEY and AUR_KNOWN_HOSTS in the linux-release environment')
    with tempfile.TemporaryDirectory(prefix='omnilyrics-aur-ssh-') as tmp:
        keyfile, known_hosts = Path(tmp) / 'key', Path(tmp) / 'known_hosts'
        keyfile.write_text(key.rstrip() + '\n')
        keyfile.chmod(0o600)
        known_hosts.write_text(hosts.rstrip() + '\n')
        os.environ['GIT_SSH_COMMAND'] = (
            f'ssh -i {shlex.quote(str(keyfile))} -o IdentitiesOnly=yes -o BatchMode=yes '
            f'-o StrictHostKeyChecking=yes -o UserKnownHostsFile={shlex.quote(str(known_hosts))}')
        for recipe in recipes:
            publish(recipe, f'ssh://aur@aur.archlinux.org/{recipe.name}.git', False)


if __name__ == '__main__':
    main()
