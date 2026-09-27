# Linux packages

English | [简体中文](./README.zh-CN.md)

OmniLyrics provides two independent native packages:

| Package | Command | Contents |
| --- | --- | --- |
| `omnilyrics` | `omnilyrics` | Desktop lyrics, application-menu entry and icons |
| `omnilyrics-cli` | `omnilyrics-cli` | Terminal lyrics, TUI configuration and widget output |

Both include the .NET runtime and share the user's existing OmniLyrics configuration. Neither package depends on the other. Package removal preserves user settings and never enables autostart or changes compositor configuration.

## Install

### Maintainer repositories

After the maintainer completes the setup below and the first publication succeeds, users can add the public repository once and receive subsequent releases through their package manager. **These channels are not live yet.** `OWNER/REPOSITORY` below is a placeholder for the maintainer's Cloudsmith repository, not a usable OmniLyrics source address. Replace it with the published repository slug before running the commands. Users do not need a Cloudsmith account or API key.

#### Debian / Ubuntu

```bash
repository='OWNER/REPOSITORY'
curl -fsSL "https://dl.cloudsmith.io/public/$repository/cfg/setup/bash.deb.sh" -o /tmp/omnilyrics-repository.sh
sudo bash /tmp/omnilyrics-repository.sh
sudo apt update
sudo apt install omnilyrics
sudo apt install omnilyrics-cli
```

#### Fedora

```bash
repository='OWNER/REPOSITORY'
curl -fsSL "https://dl.cloudsmith.io/public/$repository/cfg/setup/bash.rpm.sh" -o /tmp/omnilyrics-repository.sh
sudo bash /tmp/omnilyrics-repository.sh
sudo dnf install omnilyrics
sudo dnf install omnilyrics-cli
```

The setup scripts configure the repository and its signing key. Keep signature verification enabled. Choose either install command, or both. Subsequent updates use `sudo apt update && sudo apt upgrade` or `sudo dnf upgrade`. Native packages are tested on Debian 12, Ubuntu 22.04/24.04 and Fedora 44, on x64 and ARM64; they require glibc 2.35 or later.

#### Arch / CachyOS

Once the two AUR entries are published, install with an AUR helper you already use:

```bash
yay -S omnilyrics-bin
yay -S omnilyrics-cli-bin
```

Use `yay -Syu` for updates. AUR packages use the `-bin` suffix; their commands remain `omnilyrics` and `omnilyrics-cli`. AUR is separate from the official pacman repositories, so `pacman -S omnilyrics` alone will not find these entries. The recipes cover x64 and ARM64; automated AUR installation tests currently run on x64.

### Local package files

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

## Publish a release

The [Publish Linux repositories](../../.github/workflows/linux-release.yaml) workflow runs when a stable GitHub Release is published. It builds and tests both architectures, uploads twelve native packages plus a separate `SHA256SUMS-linux-native`, and updates the configured Cloudsmith and AUR channels. Existing portable ZIPs and their `SHA256SUMS` are preserved. Cloudsmith manages signed APT indexes and RPM packages/indexes; AUR recipes verify upstream ZIP checksums. See the official [Cloudsmith signing documentation](https://docs.cloudsmith.com/supply-chain-security/signing-keys).

### One-time maintainer setup

1. Create a **public** Cloudsmith repository under your account/organization. This is your own software source; users subscribe to it without publisher credentials. Keep repository/package signing enabled. Set the GitHub **repository variable** `CLOUDSMITH_REPOSITORY` to its `owner/repository` slug. Consult [Cloudsmith's open-source policy](https://docs.cloudsmith.com/resources/open-source-hosting-policy) and account limits when choosing hosting.
2. Create the GitHub Actions environment `linux-release`, restrict its deployment branches/tags to your release process, and add the **environment secret** `CLOUDSMITH_API_KEY` for an account with permission to publish to that repository. Cloudsmith supplies the signing key; no private GPG key needs to be checked into this project.
3. For AUR, register a maintainer account, confirm that `omnilyrics-bin` and `omnilyrics-cli-bin` are available or that your account maintains them, and register a dedicated SSH public key. Add the corresponding **environment secret** `AUR_SSH_PRIVATE_KEY`. Set the **repository variable** `AUR_PUBLISH_ENABLED` to `true` and the **environment variable** `AUR_KNOWN_HOSTS` to a verified `aur.archlinux.org` known-hosts entry. Verify the host key against [Arch's published fingerprints](https://wiki.archlinux.org/title/AUR_submission_guidelines#Authentication); the publisher requires strict host-key checking.
4. After the first successful publication and public installation checks, replace `OWNER/REPOSITORY` in both guides with the actual slug, publish the signing-key fingerprint/link, and remove the “not live yet” notices. Do the same for the AUR availability notices only after both entries exist.

Cloudsmith and AUR are independently enabled by their repository variables. An unset channel is explicitly reported as skipped; an enabled channel with missing credentials fails. You may enable either first. Keep credentials in GitHub settings, never in Markdown or Git.

### Each release

Prepare a stable `vX.Y.Z` tag whose commit is on `master` and whose `Directory.Build.props` version matches. The tagged commit must include these publishing scripts, so this workflow is intended for releases after 0.4.2. Before publishing the Release, upload the four Linux portable ZIPs (`omnilyrics-{gui,cli}-{linux-x64,linux-arm64}.zip`) and their entries in `SHA256SUMS`; the AUR build checks consume these exact release assets. Publishing the Release triggers the workflow automatically.

To retry or publish a previously prepared release manually, choose **Publish Linux repositories → Run workflow → master**, then enter its tag, or run:

```bash
gh workflow run linux-release.yaml --ref master -f tag=vX.Y.Z
```

Replace `vX.Y.Z` with the actual stable tag. Only tested revision 1 packages are published; revision 2 files remain CI upgrade fixtures. The publication jobs run after both architecture builds pass and receive credentials only at publication time. After Cloudsmith synchronization, separate containers install both packages from the public source with no publisher credentials, on both architectures.

If a channel fails, use **Re-run failed jobs** to reuse the tested artifacts. Matching existing uploads are skipped; conflicting files are never overwritten, and AUR is never force-pushed or downgraded. Cloudsmith adds an upstream-checksum tag because signing can change an RPM's served checksum. A package still syncing or with different upstream bytes requires inspection in Cloudsmith before retrying. Rebuilding an already published version can produce different bytes and is deliberately rejected; use a new release for changed binaries.
