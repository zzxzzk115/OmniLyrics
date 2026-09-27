# Linux packages

English | [简体中文](./README.zh-CN.md)

OmniLyrics provides two independent native packages:

| Package | Command | Contents |
| --- | --- | --- |
| `omnilyrics` | `omnilyrics` | Desktop lyrics, application-menu entry and icons |
| `omnilyrics-cli` | `omnilyrics-cli` | Terminal lyrics, TUI configuration and widget output |

Both include the .NET runtime and share the user's existing OmniLyrics configuration. Neither package depends on the other. Package removal preserves user settings and never enables autostart or changes compositor configuration.

## Install

For now, download the `omnilyrics-linux-packages-linux-x64` or `omnilyrics-linux-packages-linux-arm64` artifact from a successful [Linux packages](https://github.com/zzxzzk115/OmniLyrics/actions/workflows/linux-packages.yaml) GitHub Actions run and extract it. The existing **0.4.2 release does not contain these native packages**. Published releases can include them in the future. Check the included `SHA256SUMS`, then select the package format and CPU architecture for your system.

| System | x64 architecture | ARM64 architecture |
| --- | --- | --- |
| Debian / Ubuntu | `amd64` | `arm64` |
| Fedora / Arch | `x86_64` | `aarch64` |

Run commands from the extracted directory containing only the version and architecture you intend to install. Choose either GUI or CLI, or run both commands to install both.

```bash
# Debian / Ubuntu
sudo apt install ./omnilyrics_*.deb
sudo apt install ./omnilyrics-cli_*.deb

# Fedora
sudo dnf install ./omnilyrics-[0-9]*.rpm
sudo dnf install ./omnilyrics-cli-[0-9]*.rpm

# Arch / CachyOS
sudo pacman -U ./omnilyrics-[0-9]*.pkg.tar.zst
sudo pacman -U ./omnilyrics-cli-[0-9]*.pkg.tar.zst
```

Open **OmniLyrics** from the application menu or run `omnilyrics`. For the terminal tool, run `omnilyrics-cli`, `omnilyrics-cli config`, or `omnilyrics-cli --mode json`.

To update, download newer packages and repeat the same installation command. These are local packages, **not a hosted APT/DNF repository**, so normal system updates do not fetch new OmniLyrics releases automatically. An older user-local `~/.local/bin/omnilyrics-cli` may take precedence over `/usr/bin/omnilyrics-cli`; use `command -v omnilyrics-cli` to check which one runs.

Remove either component with `sudo apt remove omnilyrics`, `sudo dnf remove omnilyrics` or `sudo pacman -R omnilyrics`; replace the package name with `omnilyrics-cli` to remove the terminal tool.

## Build packages

Requires the .NET 10 SDK, Python 3.11+ and nFPM. Run from the repository root; `install-nfpm.sh` downloads nFPM 2.47.0 with a pinned SHA-256 checksum into the specified directory.

```bash
dotnet restore src/OmniLyrics.Gui
dotnet restore src/OmniLyrics.Cli
dotnet publish src/OmniLyrics.Gui -c Release -r linux-x64 -o publish/linux-x64/gui
dotnet publish src/OmniLyrics.Cli -c Release -r linux-x64 -o publish/linux-x64/cli
bash build/linux/install-nfpm.sh /tmp/omnilyrics-tools
python3 build/linux/package.py --rid linux-x64 \
  --gui publish/linux-x64/gui/OmniLyrics.Gui \
  --cli publish/linux-x64/cli/OmniLyrics.Cli \
  --nfpm /tmp/omnilyrics-tools/nfpm --output packages/linux-x64
```

Replace `linux-x64` with `linux-arm64` for ARM64. Version comes from `Directory.Build.props`. Use `--format deb`, `--format rpm` or `--format archlinux` to select a format; omit `--gui` or `--cli` to build only one component. `--revision 2` builds an update to the packaging of the same application version. Use a fresh output directory for each release. The script does not strip or change the single-file executables.

## AUR recipes

The generator produces independent `omnilyrics-bin` and `omnilyrics-cli-bin` directories, each containing `PKGBUILD`, `.SRCINFO` and its own resources. The `-bin` suffix follows the AUR convention; installed commands remain `omnilyrics` and `omnilyrics-cli`. **Public AUR entries have not been submitted yet.** A maintainer can generate recipes for a published release using that release's actual portable-ZIP checksums:

```bash
gh release download v0.4.2 --repo zzxzzk115/OmniLyrics \
  --pattern SHA256SUMS --dir /tmp/omnilyrics-release-checksums
python3 build/linux/make-aur.py --version 0.4.2 \
  --checksums /tmp/omnilyrics-release-checksums/SHA256SUMS \
  --output /tmp/omnilyrics-aur-recipes
cd /tmp/omnilyrics-aur-recipes/omnilyrics-bin
makepkg -si
```

Use the `omnilyrics-cli-bin` directory for the CLI. When preparing a new release, regenerate from its ZIP checksums, validate with `makepkg --printsrcinfo`, and submit the two recipes to their respective AUR repositories. Do not use CI-native-package checksums for portable ZIP sources. AUR recipes disable stripping, which would damage bundled .NET executables.

For a release, use packages from a successful run of the exact release commit. Upload the six packages for each architecture and merge their checksum entries into the release's `SHA256SUMS`, preserving the portable-ZIP entries used by AUR. Upload only revision 1 outputs; revision 2 packages are CI upgrade fixtures. These scripts do not create releases, publish repositories, or submit AUR changes automatically.
