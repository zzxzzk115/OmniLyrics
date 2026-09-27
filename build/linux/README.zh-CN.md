# Linux 软件包

[English](./README.md) | 简体中文

OmniLyrics 提供两个独立的原生软件包：

| 软件包 | 命令 | 内容 |
| --- | --- | --- |
| `omnilyrics` | `omnilyrics` | 桌面歌词、应用菜单入口及图标 |
| `omnilyrics-cli` | `omnilyrics-cli` | 终端歌词、TUI 配置与组件输出 |

两者均内含 .NET 运行时，并共享用户已有的 OmniLyrics 配置，互不依赖。卸载软件包会保留用户设置；安装不会启用自启动或修改合成器配置。

## 安装

### 维护者软件源

APT/DNF 发布已配置为维护者的 Cloudsmith 仓库 `zzxzzk115/omnilyrics`。**目前尚不能从此源安装；仍需确认公共访问并完成首次发布。** 源可用后，按下方命令添加一次，后续即可通过包管理器获取新版本。用户无需 Cloudsmith 账号或 API key。

#### Debian / Ubuntu

```bash
repository='zzxzzk115/omnilyrics'
curl -fsSL "https://dl.cloudsmith.io/public/$repository/cfg/setup/bash.deb.sh" -o /tmp/omnilyrics-repository.sh
sudo bash /tmp/omnilyrics-repository.sh
sudo apt update
sudo apt install omnilyrics
sudo apt install omnilyrics-cli
```

#### Fedora

```bash
repository='zzxzzk115/omnilyrics'
curl -fsSL "https://dl.cloudsmith.io/public/$repository/cfg/setup/bash.rpm.sh" -o /tmp/omnilyrics-repository.sh
sudo bash /tmp/omnilyrics-repository.sh
sudo dnf install omnilyrics
sudo dnf install omnilyrics-cli
```

配置脚本会添加软件源及其签名公钥，请保持签名验证开启。两个安装命令可任选其一，也可都执行。后续通过 `sudo apt update && sudo apt upgrade` 或 `sudo dnf upgrade` 更新。原生包已在 x64 和 ARM64 的 Debian 12、Ubuntu 22.04/24.04、Fedora 44 上测试，需要 glibc 2.35 或更新版本。

#### Arch / CachyOS

**目前暂未启用 AUR 发布。** 请先安装下方说明中的本地 Arch 软件包。两个 AUR 条目均发布后，也可使用你已有的 AUR 助手安装：

```bash
yay -S omnilyrics-bin
yay -S omnilyrics-cli-bin
```

使用 `yay -Syu` 更新。AUR 包名带有 `-bin` 后缀，命令仍是 `omnilyrics` 与 `omnilyrics-cli`。AUR 与 pacman 官方仓库不同，因此直接执行 `pacman -S omnilyrics` 无法找到这些条目。构建文件支持 x64 和 ARM64，目前 AUR 自动安装测试运行于 x64。

### 本地软件包文件

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

## 发布版本

[Publish Linux repositories](../../.github/workflows/linux-release.yaml) 工作流会在稳定版 GitHub Release 发布时运行。它构建并测试两种架构，上传十二个原生包及独立的 `SHA256SUMS-linux-native`，然后更新已配置的 Cloudsmith 和 AUR 渠道。已有便携 ZIP 及其 `SHA256SUMS` 会保留。Cloudsmith 管理 APT 索引、RPM 软件包和索引的签名；AUR 构建文件校验上游 ZIP 的校验和。参见 [Cloudsmith 官方签名文档](https://docs.cloudsmith.com/supply-chain-security/signing-keys)。

### 维护者一次性配置

1. 在你的 Cloudsmith 账号或组织下创建一个**公共**仓库。这就是你维护的软件源，用户订阅时无需发布者凭据。保持仓库和软件包签名开启。将 GitHub **仓库变量** `CLOUDSMITH_REPOSITORY` 设为其 `owner/repository` 标识（本项目为 `zzxzzk115/omnilyrics`）。选择托管方式时，请查看 [Cloudsmith 开源项目政策](https://docs.cloudsmith.com/resources/open-source-hosting-policy)及账号额度。
2. 创建 GitHub Actions 环境 `linux-release`，按发布流程限制可部署的分支和标签，添加有权向该仓库发布的账号对应的**环境 secret** `CLOUDSMITH_API_KEY`。Cloudsmith 提供签名密钥，无需将 GPG 私钥提交到项目中。
3. 对于 AUR，注册维护者账号，确认 `omnilyrics-bin` 和 `omnilyrics-cli-bin` 名称可用或已由你的账号维护，并登记一个专用 SSH 公钥。将对应私钥设为**环境 secret** `AUR_SSH_PRIVATE_KEY`。将**仓库变量** `AUR_PUBLISH_ENABLED` 设为 `true`，并将**环境变量** `AUR_KNOWN_HOSTS` 设为已核验的 `aur.archlinux.org` known-hosts 条目。请根据 [Arch 公布的指纹](https://wiki.archlinux.org/title/AUR_submission_guidelines#Authentication)核对主机密钥；发布脚本会严格校验主机密钥。
4. 确认公共访问、首次发布和公共源安装检查均通过后，公布签名公钥指纹和链接，并移除两份指南中暂不可安装的提示。如仓库地址变化，需同步更新两份指南。AUR 的可用性提示也需在两个条目都存在后再更新。

Cloudsmith 与 AUR 分别通过各自的仓库变量启用。未设置的渠道会明确标记为跳过；已启用却缺少凭据的渠道会失败。可以先启用其中一个。凭据保存在 GitHub 设置中，不要写入 Markdown 或 Git。

### 每次发布

准备稳定版标签 `vX.Y.Z`，其提交应已合入 `master`，且 `Directory.Build.props` 中的版本号一致。标签指向的提交必须包含这些发布脚本，因此本流程适用于 0.4.2 之后的版本。发布 Release 前，先上传四个 Linux 便携 ZIP（`omnilyrics-{gui,cli}-{linux-x64,linux-arm64}.zip`）及其在 `SHA256SUMS` 中的校验和；AUR 构建检查使用的正是这些发布产物。发布 Release 后工作流会自动启动。

如需重试或手动发布已准备好的版本，在 **Publish Linux repositories → Run workflow → master** 中输入标签，或执行：

```bash
gh workflow run linux-release.yaml --ref master -f tag=vX.Y.Z
```

将 `vX.Y.Z` 替换为实际稳定版标签。只发布通过测试的修订 1 软件包，修订 2 仍仅用作 CI 升级测试。两种架构的构建均通过后才会开始发布，凭据只在发布阶段提供。Cloudsmith 同步完成后，独立容器会在两种架构上从公共源安装 GUI 和 CLI，不使用发布者凭据。

某一渠道失败时，使用 **Re-run failed jobs** 复用已测试的产物。内容相同的已有上传会跳过，冲突文件不会被覆盖，AUR 不会强制推送或降级。因为签名可能改变 RPM 下载文件的校验和，Cloudsmith 包会添加上游校验和标签。软件包若仍在同步或上游内容不同，需先在 Cloudsmith 中检查后再重试。重新构建已发布版本可能产生不同字节，流程会明确拒绝替换；二进制内容变化应发布新版本。
