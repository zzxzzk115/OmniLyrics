# macOS 签名发布

[English](macos-release.md) | [简体中文](macos-release.zh-CN.md)

OmniLyrics 要求 macOS 14 或更高版本。发布包包含 .NET 运行时，用户无需安装 SDK。GUI 为 Apple Silicon 和 Intel 分别提供 `OmniLyrics.app`，便携可执行文件和未正式签名的 CI 预览包另行提供。

## 签名凭据

使用本机钥匙串中包含私钥的 **Developer ID Application** 证书。通过以下命令查找签名身份及团队：

```bash
security find-identity -v -p codesigning
```

Team ID 是证书名称末尾的十位标识。用于公证的 Apple 账号必须属于该团队。

在 [Apple 账户](https://account.apple.com/)的 **登录与安全 → App 专用密码** 中创建应用专用密码，然后将凭据保存到本机：

```bash
xcrun notarytool store-credentials "omnilyrics-notary" \
  --apple-id "YOUR_APPLE_ACCOUNT_EMAIL" \
  --team-id "YOUR_TEAM_ID"
```

仅在安全提示中输入应用专用密码，不要将其放入命令、仓库、Issue 或聊天。`notarytool` 会在保存到钥匙串之前验证凭据。这些脚本不会导出签名私钥，也不会将凭据上传到 GitHub。

如果验证返回 HTTP 403，提示 “A required agreement is missing or has expired”，团队的 Account Holder 需要在 [Apple Developer 账户](https://developer.apple.com/account/)中检查协议通知和会员状态。解决后再次运行 `store-credentials`。不要跳过凭据验证，也不要将未经公证的包作为正式签名版本发布。

## 构建与签名

从干净工作区中的发布标签开始，使用仓库之外的输出目录。需要 Xcode Command Line Tools、Python 3 和 .NET 10 SDK。

```bash
export MACOS_SIGNING_IDENTITY="Developer ID Application: YOUR_NAME (YOUR_TEAM_ID)"
bash build/macos/prepare-release.sh /absolute/path/to/release-work
```

此命令构建两种架构；可选的第二个参数可指定 `osx-arm64` 或 `osx-x64`。已存在的输出目录会被拒绝，以免混用不同版本。

应用版本从项目读取。macOS 的 .NET 单文件宿主包含运行时；Avalonia、Skia 和 HarfBuzz 原生库保留在应用包内，不会在启动时解压到缓存。先分别签名原生库，再使用安全时间戳和 Hardened Runtime 为主程序及应用包签名。主程序仅请求 JIT 和 Apple Events 权限。

如需打包已有的 publish 目录：

```bash
bash build/macos/package.sh /path/to/publish /path/to/package-output 0.4.3
```

正式签名时，发布参数需包含 `-p:IncludeNativeLibrariesForSelfExtract=false`，并设置 `MACOS_SIGNING_IDENTITY`。未设置该变量时，脚本生成 **ad-hoc 预览包**，不适用于 Cask。

## 公证与附加票据

分别提交两个签名应用：

```bash
bash build/macos/notarize.sh submit /absolute/path/to/release-work/osx-arm64/package/OmniLyrics.app omnilyrics-notary
bash build/macos/notarize.sh submit /absolute/path/to/release-work/osx-x64/package/OmniLyrics.app omnilyrics-notary
```

提交 ID 会保存在应用旁边。通过以下命令检查各自的结果：

```bash
bash build/macos/notarize.sh finish /absolute/path/to/release-work/osx-arm64/package/OmniLyrics.app omnilyrics-notary
bash build/macos/notarize.sh finish /absolute/path/to/release-work/osx-x64/package/OmniLyrics.app omnilyrics-notary
```

仍在处理中的提交以状态码 2 退出，等待后再检查即可。提交失败时，脚本下载 Apple 的诊断日志并停止。Apple 接受提交后，脚本将公证票据附加到应用的临时副本，通过 `stapler`、`codesign` 和 Gatekeeper 验证，再用该副本重新生成归档。

原应用包保持不变，包括 macOS 在测试启动后保护该应用包的情况。Cask 生成器会解压并验证最终归档。仅分发**附加公证票据之后**创建的归档，以支持离线验证。

发布前应在 Mac 上运行签名应用，验证启动、Automation 权限下的 Apple Music 访问和歌词功能。Intel 版本可在 Intel Mac 或 Rosetta 下测试；Rosetta 测试不能替代真实 Intel 硬件测试。

## Release 资产与 Homebrew Cask

只有两个提交都被接受后，才生成最终归档、校验和及 Cask：

```bash
python3 build/homebrew/make-cask.py /absolute/path/to/release-work /absolute/path/to/release-assets
```

生成器会再次验证已附加公证票据的应用包，然后生成：

- `omnilyrics-gui-osx-arm64-signed.zip`
- `omnilyrics-gui-osx-x64-signed.zip`
- `SHA256SUMS-macos-signed`
- `omnilyrics.rb`

将归档与校验和清单上传到 `zzxzzk115/OmniLyrics` 中对应的 `vVERSION` Release。不要替换已经发布的资产：新增签名版本可以保留现有便携下载及其校验和。

将生成的 Cask 复制到 [`zzxzzk115/homebrew-tap`](https://github.com/zzxzzk115/homebrew-tap) 的 `Casks/omnilyrics.rb`，检查格式并进行 audit 后发布。Tap 必须引用已上传的资产及对应的精确校验和。

发布后，用户可通过以下命令安装：

```bash
brew install --cask zzxzzk115/tap/omnilyrics
```

Cask 会选择正确的架构并安装 `OmniLyrics.app`。不需要 `media-control`；原生 Apple Music 和 Spotify 使用 Apple Events。安装测试应保持 Homebrew 正常的 quarantine 与 Gatekeeper 检查。`brew uninstall --cask omnilyrics` 会移除应用；即使使用 `--zap`，也会保留可能同时被 CLI 使用的 OmniLyrics 共享配置目录。

## GitHub 托管的签名与公证

发布稳定版 GitHub Release 时，[Sign and publish macOS release](../.github/workflows/macos-release.yaml) 会在 GitHub 托管的 macOS runner 上运行。也可以从 `master` 手动触发，指定已发布的 `vMAJOR.MINOR.PATCH` Release。工作流拒绝未合入 `master` 的标签、草稿、预发布版以及与项目版本不同的标签。标签指向的源码必须包含发布脚本，因此自动上传从 0.4.3 开始可用。仅合并分支或推送标签不会发布签名包。

各个任务分别负责：

1. **Build：** 从标签对应的精确提交编译 Apple Silicon 和 Intel 应用包，不接触签名凭据。
2. **Sign：** 仅下载该构建产生的不可变 artifact ID，逐个签名原生库和应用，提交 Apple，等待接受，附加公证票据，并通过 Gatekeeper 验证最终归档。凭据仅对该任务可用；此处不会启动应用。
3. **Publish：** 仅下载签名任务产生的不可变 artifact ID，将两个签名 ZIP、`SHA256SUMS-macos-signed` 和 `omnilyrics.rb` 上传到已有 Release。只有该任务拥有 `contents: write`，且不会接触 Apple 凭据。

上传前会检查两个归档的哈希、内部应用版本和生成的 Cask。相同的已有资产会跳过；同名但内容不同的文件会在任何上传开始前被拒绝。便携 ZIP、Linux 原生包及其独立校验和清单保持不变。生成的 Cask 仍需提交到 `zzxzzk115/homebrew-tap`；本仓库的 `GITHUB_TOKEN` 不能写入另一个仓库。

### 一次性仓库配置

在 **Settings → Environments** 创建 `macos-release`。在 **Deployment branches and tags → Selected branches and tags** 中，添加 **branch** 规则 `master` 用于手动运行，以及 **tag** 规则 `v*` 用于 Release 触发。仅配置分支规则会阻止自动 Release 签名。Required reviewers 可选；若启用，每次签名任务都会等待审批。独立维护者若要批准自己触发的运行，应保持 “Prevent self-review” 未勾选。

配置以下 **Environment Secrets**，而非普通仓库级 Secrets：

| Secret | 值 |
| --- | --- |
| `MACOS_CERTIFICATE_P12_BASE64` | 加密 `.p12` 的 Base64，包含 Developer ID Application 证书**及其私钥** |
| `MACOS_CERTIFICATE_PASSWORD` | 导出 `.p12` 时设置的密码 |
| `APPLE_ID` | 属于证书团队的 Apple 账号邮箱 |
| `APPLE_APP_SPECIFIC_PASSWORD` | 专用于公证的应用专用密码 |

配置以下 **Environment Variables**：

| Variable | 值 |
| --- | --- |
| `APPLE_TEAM_ID` | 证书中十位的开发者 Team ID |
| `MACOS_SIGNING_IDENTITY` | 完整的 `Developer ID Application: NAME (TEAM_ID)` 签名身份，或其 SHA-1 指纹 |

在 **钥匙串访问 → 我的证书** 中自行导出证书和私钥，保存为有密码保护的 `.p12`。Base64 并非加密。以下命令可将文件直接传入 Environment Secret，而不打印内容：

```bash
base64 -i /path/to/DeveloperID.p12 | gh secret set MACOS_CERTIFICATE_P12_BASE64 \
  --repo zzxzzk115/OmniLyrics --env macos-release
```

其他 Secrets 可在 GitHub 的 Environment 设置中填写，也可使用不带 `--body` 的 `gh secret set` 交互提示。不要将密码、`.p12` 或私钥放入聊天、提交、Release 资产或命令参数。配置完成后，按照自己的备份策略安全处理导出的文件。本机 `omnilyrics-notary` 钥匙串配置不会自动传到 GitHub runner。

工作流目前使用 Apple ID 凭据；App Store Connect API key 也是一种可选认证方式，但需要修改凭据导入步骤。

工作流合入 `master` 后，发布对应的稳定版 Release 即可自动启动。如需为已发布版本手动触发：

```bash
gh workflow run macos-release.yaml --repo zzxzzk115/OmniLyrics \
  --ref master -f tag=v0.4.3
```

若配置了环境审批人，在 Actions 中批准 `macos-release` 部署。Apple 提交超时或拒绝会停止发布；失败时会将诊断 JSON 保存为短期 Actions artifact。签名失败不会降级发布未签名包。

如果只有 **Publish** 失败，选择 **Re-run failed jobs**，复用原签名产物（保留 30 天）。不要重新构建并替换已有签名归档：安全时间戳可能导致每次签名产生不同字节。如果签名在发布前失败，重新运行失败任务即可创建临时钥匙串并再次提交。这些步骤都不需要 Mac mini 在线。

证书和公证配置位于 `RUNNER_TEMP` 下的临时钥匙串中；凭据文件仅当前用户可访问，钥匙串密码随机生成。Secrets 通过环境变量传入，未启用 shell 命令追踪。脚本退出以及工作流的 `always()` 步骤都会清理凭据；GitHub 还会在任务结束后销毁托管 runner。Release Actions 固定到完整提交哈希。后续发布不需要自托管 runner、长期访问 Mac，也不需要再次在维护者机器上导出私钥。

环境审批不能使任意工作流代码变得安全：被批准的步骤能够访问其 Secrets。不要为签名任务添加 `pull_request_target`、不受信任的 PR checkout 或任意外部 artifact 输入。

参考 [GitHub 证书导入指南](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications)、[Environment 保护](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)和[公开仓库安全指南](https://docs.github.com/en/actions/reference/security/secure-use#hardening-for-self-hosted-runners)。

相关资料：[Apple Developer ID](https://developer.apple.com/developer-id/)、[Avalonia macOS 部署](https://docs.avaloniaui.net/docs/deployment/macos)、[.NET 单文件宿主设计](https://github.com/dotnet/designs/blob/main/accepted/2020/single-file/design.md)。
