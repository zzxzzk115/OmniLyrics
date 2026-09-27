# Linux packages / Linux 软件包

OmniLyrics provides two independent native packages:

| Package / 软件包 | Command / 命令 | Contents / 内容 |
| --- | --- | --- |
| `omnilyrics` | `omnilyrics` | Desktop lyrics, application-menu entry and icons / 桌面歌词、应用菜单入口及图标 |
| `omnilyrics-cli` | `omnilyrics-cli` | Terminal lyrics, TUI configuration and widget output / 终端歌词、TUI 配置与组件输出 |

Both include the .NET runtime and share the user's existing OmniLyrics configuration. Neither package depends on the other. Package removal preserves user settings and never enables autostart or changes compositor configuration.

两者均内含 .NET 运行时，并共享用户已有的 OmniLyrics 配置，互不依赖。卸载软件包会保留用户设置；安装不会启用自启动或修改合成器配置。

## Install / 安装

For now, download the `omnilyrics-linux-packages-linux-x64` or `omnilyrics-linux-packages-linux-arm64` artifact from a successful [Linux packages](https://github.com/zzxzzk115/OmniLyrics/actions/workflows/linux-packages.yaml) GitHub Actions run and extract it. The existing **0.4.2 release does not contain these native packages**. Published releases can include them in the future. Check the included `SHA256SUMS`, then select the package format and CPU architecture for your system.

目前请从成功的 [Linux packages](https://github.com/zzxzzk115/OmniLyrics/actions/workflows/linux-packages.yaml) GitHub Actions 构建中下载 `omnilyrics-linux-packages-linux-x64` 或 `omnilyrics-linux-packages-linux-arm64` 产物并解压。现有 **0.4.2 Release 尚未包含这些原生包**，后续发布可附带它们。核对包内 `SHA256SUMS` 后，选择系统对应的包格式与处理器架构。

| System / 系统 | x64 architecture / x64 架构名 | ARM64 architecture / ARM64 架构名 |
| --- | --- | --- |
| Debian / Ubuntu | `amd64` | `arm64` |
| Fedora / Arch | `x86_64` | `aarch64` |

Run commands from the extracted directory containing only the version and architecture you intend to install. Choose either GUI or CLI, or run both commands to install both.

在只包含所需版本和架构的解压目录运行命令。GUI 与 CLI 可任选其一，也可都安装。

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

从应用菜单打开 **OmniLyrics**，或运行 `omnilyrics`。终端工具使用 `omnilyrics-cli`、`omnilyrics-cli config` 或 `omnilyrics-cli --mode json`。

To update, download newer packages and repeat the same installation command. These are local packages, **not a hosted APT/DNF repository**, so normal system updates do not fetch new OmniLyrics releases automatically. An older user-local `~/.local/bin/omnilyrics-cli` may take precedence over `/usr/bin/omnilyrics-cli`; use `command -v omnilyrics-cli` to check which one runs.

更新时下载新包并重复对应安装命令。这些是本地软件包，**不是托管的 APT/DNF 软件源**，系统更新不会自动下载 OmniLyrics 新版本。旧的用户级 `~/.local/bin/omnilyrics-cli` 可能优先于 `/usr/bin/omnilyrics-cli`，可用 `command -v omnilyrics-cli` 检查实际启动位置。

Remove either component with `sudo apt remove omnilyrics`, `sudo dnf remove omnilyrics` or `sudo pacman -R omnilyrics`; replace the package name with `omnilyrics-cli` to remove the terminal tool.

卸载 GUI 使用 `sudo apt remove omnilyrics`、`sudo dnf remove omnilyrics` 或 `sudo pacman -R omnilyrics`；将包名换成 `omnilyrics-cli` 即可卸载终端工具。

## Build packages / 构建软件包

Requires the .NET 10 SDK, Python 3.11+ and nFPM. Run from the repository root; `install-nfpm.sh` downloads nFPM 2.47.0 with a pinned SHA-256 checksum into the specified directory.

需要 .NET 10 SDK、Python 3.11+ 和 nFPM。在仓库根目录执行；`install-nfpm.sh` 会将经过固定 SHA-256 校验的 nFPM 2.47.0 下载到指定目录。

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

ARM64 构建将 `linux-x64` 换成 `linux-arm64`。版本号来自 `Directory.Build.props`。使用 `--format deb`、`--format rpm` 或 `--format archlinux` 指定格式；省略 `--gui` 或 `--cli` 可只构建一种组件。`--revision 2` 用于同一应用版本的软件包修订。每次发布应使用新的输出目录。脚本不会 strip 或修改单文件可执行程序。

## AUR recipes / AUR 构建文件

The generator produces independent `omnilyrics-bin` and `omnilyrics-cli-bin` directories, each containing `PKGBUILD`, `.SRCINFO` and its own resources. The `-bin` suffix follows the AUR convention; installed commands remain `omnilyrics` and `omnilyrics-cli`. **Public AUR entries have not been submitted yet.** A maintainer can generate recipes for a published release using that release's actual portable-ZIP checksums:

生成器分别输出 `omnilyrics-bin` 和 `omnilyrics-cli-bin` 目录，包含各自的 `PKGBUILD`、`.SRCINFO` 与资源。`-bin` 后缀遵循 AUR 惯例，安装后的命令仍是 `omnilyrics` 与 `omnilyrics-cli`。**目前尚未提交公开 AUR 条目。** 维护者可使用已发布版本的实际便携 ZIP 校验和生成构建文件：

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

CLI 使用 `omnilyrics-cli-bin` 目录。准备新版本时，应根据该版本 ZIP 的校验和重新生成，用 `makepkg --printsrcinfo` 验证，再分别提交到对应 AUR 仓库。便携 ZIP 来源不能使用 CI 原生软件包的校验和。构建文件禁用了 strip，避免破坏 .NET 捆绑可执行程序。

For a release, use packages from a successful run of the exact release commit. Upload the six packages for each architecture and merge their checksum entries into the release's `SHA256SUMS`, preserving the portable-ZIP entries used by AUR. Upload only revision 1 outputs; revision 2 packages are CI upgrade fixtures. These scripts do not create releases, publish repositories, or submit AUR changes automatically.

正式发布应使用与发布提交完全一致且检查通过的构建产物。上传每种架构的六个包，将其校验和合并到 Release 的 `SHA256SUMS`，保留 AUR 使用的便携 ZIP 校验和。只上传修订 1 的产物；修订 2 仅为 CI 升级测试准备。脚本不会自动创建 Release、发布软件源或提交 AUR 修改。
