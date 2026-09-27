# Linux 软件包

[English](./README.md) | 简体中文

OmniLyrics 提供两个独立的原生软件包：

| 软件包 | 命令 | 内容 |
| --- | --- | --- |
| `omnilyrics` | `omnilyrics` | 桌面歌词、应用菜单入口及图标 |
| `omnilyrics-cli` | `omnilyrics-cli` | 终端歌词、TUI 配置与组件输出 |

两者均内含 .NET 运行时，并共享用户已有的 OmniLyrics 配置，互不依赖。卸载软件包会保留用户设置；安装不会启用自启动或修改合成器配置。

## 安装

目前请从成功的 [Linux packages](https://github.com/zzxzzk115/OmniLyrics/actions/workflows/linux-packages.yaml) GitHub Actions 构建中下载 `omnilyrics-linux-packages-linux-x64` 或 `omnilyrics-linux-packages-linux-arm64` 产物并解压。现有 **0.4.2 Release 尚未包含这些原生包**，后续发布可附带它们。核对包内 `SHA256SUMS` 后，选择系统对应的包格式与处理器架构。

| 系统 | x64 架构名 | ARM64 架构名 |
| --- | --- | --- |
| Debian / Ubuntu | `amd64` | `arm64` |
| Fedora / Arch | `x86_64` | `aarch64` |

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

从应用菜单打开 **OmniLyrics**，或运行 `omnilyrics`。终端工具使用 `omnilyrics-cli`、`omnilyrics-cli config` 或 `omnilyrics-cli --mode json`。

更新时下载新包并重复对应安装命令。这些是本地软件包，**不是托管的 APT/DNF 软件源**，系统更新不会自动下载 OmniLyrics 新版本。旧的用户级 `~/.local/bin/omnilyrics-cli` 可能优先于 `/usr/bin/omnilyrics-cli`，可用 `command -v omnilyrics-cli` 检查实际启动位置。

卸载 GUI 使用 `sudo apt remove omnilyrics`、`sudo dnf remove omnilyrics` 或 `sudo pacman -R omnilyrics`；将包名换成 `omnilyrics-cli` 即可卸载终端工具。

## 构建软件包

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

ARM64 构建将 `linux-x64` 换成 `linux-arm64`。版本号来自 `Directory.Build.props`。使用 `--format deb`、`--format rpm` 或 `--format archlinux` 指定格式；省略 `--gui` 或 `--cli` 可只构建一种组件。`--revision 2` 用于同一应用版本的软件包修订。每次发布应使用新的输出目录。脚本不会 strip 或修改单文件可执行程序。

## AUR 构建文件

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

CLI 使用 `omnilyrics-cli-bin` 目录。准备新版本时，应根据该版本 ZIP 的校验和重新生成，用 `makepkg --printsrcinfo` 验证，再分别提交到对应 AUR 仓库。便携 ZIP 来源不能使用 CI 原生软件包的校验和。构建文件禁用了 strip，避免破坏 .NET 捆绑可执行程序。

正式发布应使用与发布提交完全一致且检查通过的构建产物。上传每种架构的六个包，将其校验和合并到 Release 的 `SHA256SUMS`，保留 AUR 使用的便携 ZIP 校验和。只上传修订 1 的产物；修订 2 仅为 CI 升级测试准备。脚本不会自动创建 Release、发布软件源或提交 AUR 修改。
