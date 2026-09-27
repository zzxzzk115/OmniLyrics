#!/usr/bin/env python3
"""Package existing Linux single-file publishes; never build or modify the inputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
APP_ID = 'io.github.zzxzzk115.OmniLyrics'
ARCHES = {'linux-x64': ('amd64', 'x86_64', 62), 'linux-arm64': ('arm64', 'aarch64', 183)}
COMMON = {
    'deb': ['ca-certificates', 'libc6 (>= 2.35)', 'libgcc-s1', 'libstdc++6', 'libgssapi-krb5-2',
            'libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70',
            'libssl3t64 | libssl3', 'zlib1g', 'libbrotli1', 'dbus'],
    'rpm': ['ca-certificates', 'glibc >= 2.35', 'libgcc', 'libstdc++', 'krb5-libs', 'libicu',
            'openssl-libs', 'zlib', 'libbrotli', 'dbus'],
    'archlinux': ['ca-certificates', 'glibc>=2.35', 'gcc-libs', 'krb5', 'icu', 'openssl', 'zlib', 'brotli', 'dbus'],
}
GUI = {
    'deb': ['libx11-6', 'libice6', 'libsm6', 'libxcursor1', 'libxext6', 'libxi6', 'libxrandr2',
            'libxrender1', 'libfontconfig1', 'libfreetype6', 'libglib2.0-0t64 | libglib2.0-0'],
    'rpm': ['libX11', 'libICE', 'libSM', 'libXcursor', 'libXext', 'libXi', 'libXrandr', 'libXrender',
            'fontconfig', 'freetype', 'glib2'],
    'archlinux': ['libx11', 'libice', 'libsm', 'libxcursor', 'libxext', 'libxi', 'libxrandr', 'libxrender',
                 'fontconfig', 'freetype2', 'glib2'],
}


def contents(kind):
    """Shared native/AUR resource list: source relative to repo, destination absolute."""
    name = 'omnilyrics' if kind == 'gui' else 'omnilyrics-cli'
    files = [('LICENSE', f'/usr/share/licenses/{name}/LICENSE')]
    if kind == 'gui':
        files += [(f'build/linux/{APP_ID}.desktop', f'/usr/share/applications/{APP_ID}.desktop'),
                  (f'build/linux/{APP_ID}.metainfo.xml', f'/usr/share/metainfo/{APP_ID}.metainfo.xml')]
        for size in (16, 32, 128, 256, 512):
            files.append((f'src/OmniLyrics.Gui/Assets/AppIcon.iconset/icon_{size}x{size}.png',
                          f'/usr/share/icons/hicolor/{size}x{size}/apps/{APP_ID}.png'))
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', required=True, choices=ARCHES)
    parser.add_argument('--gui', type=Path)
    parser.add_argument('--cli', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--format', action='append', choices=COMMON)
    parser.add_argument('--version', default=ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version'))
    parser.add_argument('--revision', type=int, default=1)
    parser.add_argument('--nfpm', default=os.environ.get('NFPM', 'nfpm'))
    args = parser.parse_args()
    if not (args.gui or args.cli):
        parser.error('provide --gui, --cli or both')
    if not re.fullmatch(r'\d+\.\d+\.\d+', args.version) or args.revision < 1:
        parser.error('version must be X.Y.Z and revision must be positive')
    args.output.mkdir(parents=True, exist_ok=True)
    arch, native_arch, machine = ARCHES[args.rid]
    built = []
    for kind in ('gui', 'cli'):
        source = getattr(args, kind)
        if source is None:
            continue
        source = source.resolve(strict=True)
        with source.open('rb') as f:
            header = f.read(64)
        if len(header) != 64 or header[:6] != b'\x7fELF\x02\x01' or struct.unpack_from('<H', header, 18)[0] != machine:
            parser.error(f'{source} is not a 64-bit ELF for {args.rid}')
        name = 'omnilyrics' if kind == 'gui' else 'omnilyrics-cli'
        binary = f'OmniLyrics.{kind.title()}'
        dest = f'/usr/lib/{name}/{binary}'
        entries = [{'src': str(source), 'dst': dest, 'file_info': {'mode': 0o755}},
                   {'src': dest, 'dst': f'/usr/bin/{name}', 'type': 'symlink'},
                   {'dst': f'/usr/lib/{name}', 'type': 'dir', 'file_info': {'mode': 0o755}},
                   {'dst': f'/usr/share/licenses/{name}', 'type': 'dir', 'file_info': {'mode': 0o755}}]
        entries += [{'src': str(ROOT / src), 'dst': dst, 'file_info': {'mode': 0o644}}
                    for src, dst in contents(kind)]
        for fmt in args.format or COMMON:
            config = {
                'name': name, 'arch': arch, 'platform': 'linux', 'version': args.version,
                'release': str(args.revision), 'section': 'sound', 'priority': 'optional',
                'maintainer': 'Lazy_V <33739170+zzxzzk115@users.noreply.github.com>',
                'homepage': 'https://github.com/zzxzzk115/OmniLyrics', 'license': 'MIT',
                'description': ('Desktop lyrics with bilingual karaoke highlighting' if kind == 'gui'
                                else 'Terminal lyrics, interactive configuration and desktop widget integration'),
                'depends': COMMON[fmt] + (GUI[fmt] if kind == 'gui' else []), 'contents': entries,
                'archlinux': {'packager': 'Lazy_V <33739170+zzxzzk115@users.noreply.github.com>'},
            }
            if kind == 'gui' and fmt == 'deb':
                config['recommends'] = ['fonts-dejavu-core']
            if kind == 'gui' and fmt == 'rpm':
                config['recommends'] = ['dejavu-sans-fonts']
            if fmt == 'deb':
                filename = f'{name}_{args.version}-{args.revision}_{arch}.deb'
            elif fmt == 'rpm':
                filename = f'{name}-{args.version}-{args.revision}.{native_arch}.rpm'
            else:
                filename = f'{name}-{args.version}-{args.revision}-{native_arch}.pkg.tar.zst'
            target = args.output.resolve() / filename
            if target.exists():
                parser.error(f'refusing to overwrite {target}')
            with tempfile.TemporaryDirectory(prefix='omnilyrics-nfpm-') as tmp:
                cfg = Path(tmp) / 'nfpm.json'
                cfg.write_text(json.dumps(config))  # JSON is valid YAML; no extra Python dependencies.
                subprocess.run([args.nfpm, 'package', '--config', str(cfg), '--packager', fmt,
                                '--target', str(target)], check=True)
            built.append(target)
    # Each output directory belongs to one RID. Include every package, including other invocation's component.
    packages = sorted(p for p in args.output.iterdir() if p.name.endswith(('.deb', '.rpm', '.pkg.tar.zst')))
    def digest(p):
        with p.open('rb') as f:
            return hashlib.file_digest(f, 'sha256').hexdigest()
    (args.output / 'SHA256SUMS').write_text(''.join(f'{digest(p)}  {p.name}\n' for p in packages))
    print(f'Packaged {len(built)} files for {args.rid} ({args.version}-{args.revision})')


if __name__ == '__main__':
    main()
