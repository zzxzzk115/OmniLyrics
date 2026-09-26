# 使用指南

[English](./user-guide.md) | 简体中文 · [README](../README.zh-CN.md)

安装与编译步骤见 [README](../README.zh-CN.md#编译运行)。点击歌词窗口的齿轮按钮或使用托盘菜单打开设置。

## 平台与播放器

| 平台 | 连接方式 | 播放器 |
| --- | --- | --- |
| Windows | 系统媒体控制 SMTC | Spotify 等接入系统媒体会话的播放器 |
| macOS | 原生 Apple Events | Apple Music 和 Spotify 桌面端 |
| macOS | 可选 `media-control` | 其他提供 Now Playing 信息的播放器 |
| Linux | MPRIS | Spotify 和其他 MPRIS 播放器 |
| 三个平台 | 播放器专用 API | Cider V3+、YesPlayMusic |

macOS 上 Apple Music 和 Spotify 通过系统自带的 `/usr/bin/osascript`（JavaScript for Automation）发送公开 Apple Events，无需额外安装。只读取已运行的播放器，不会自动启动播放器。首次使用时，请允许启动 OmniLyrics 的应用（例如终端）控制播放器。若拒绝过授权，请在「系统设置 → 隐私与安全性 → 自动化」中启用相应播放器，然后重启 OmniLyrics。当原生访问失败且没有其他可用连接时，歌词窗口会显示处理提示。

macOS 的「设置 → macOS 环境」可检查原生访问、Homebrew 和 media-control 兼容性。每次启动 CLI/GUI 都会重新检查；两种连接均不可用时，交互界面会询问是否安装兜底工具，选择「暂不安装」不会阻止下次启动再次提示。line/JSON 模式及输入输出重定向时仅向标准错误输出恢复指引，不等待输入。只有用户确认后才执行安装，并显示 Homebrew 输出；支持取消并显示失败原因。没有 Homebrew 时提供官网安装入口。等待自动化授权不视为连接失败。显式设置 `OMNILYRICS_MEDIA_CONTROL=off` 时不会提示安装，因为安装不能启用已被关闭的后端。

Apple Events 并非新 macOS 才支持；这里使用的 JXA 桥接从 [OS X 10.10](https://developer.apple.com/library/archive/documentation/LanguagesUtilities/Conceptual/MacAutomationScriptingGuide/) 起就已提供。当前 .NET 10 应用要求 [macOS 14 或更新版本](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)，安装 media-control 不能让不受支持的旧系统变为受支持。

macOS 启用毛玻璃时，歌词背景层会自动使用 0% 不透明度，避免遮住 Avalonia 原有的原生模糊材质。文字提供局部描边/阴影保护，控件使用局部底色，工具栏其余区域保持透明；对比度过低的文字颜色会在显示时自动校正，但不改变已保存的配色。关闭毛玻璃后可重新调整背景不透明度。Apple Events 进度采用连续插值，小幅采样误差逐渐校正，避免逐字高亮突然前跳。

Cider 继续使用 Web API。其他播放器可选运行 `brew install media-control` 安装兜底工具，程序会检查 PATH 和标准 Homebrew 位置。设置 `OMNILYRICS_MEDIA_CONTROL=off` 可关闭兜底，或将该变量设为工具的绝对路径。Apple Music / Spotify 原生连接及 Cider Web API 优先于同一播放器的系统媒体连接；可选连接缺失或失败不会阻止其他播放器运行。

原生 Apple Music / Spotify 支持曲目信息、进度、播放／暂停、上一首／下一首与跳转，尚未接入原生队列。

## 收藏与账号授权

| 播放器 | 收藏接入 | 设置方式 |
| --- | --- | --- |
| macOS Apple Music | 原生 Apple Events | 允许自动化控制“音乐”，无需另行登录 |
| Spotify 桌面端 | Spotify Web API | 设置 → 播放器连接 → Spotify：填写开发者应用 Client ID，并在浏览器授权 |
| YesPlayMusic 桌面端 | 内置网易云本地 API | 优先直接使用本地 API；接口要求登录时，可用网易云音乐手机 App 扫码授权相同账号 |
| Cider | V4 资料库 API | 开启 API 令牌认证时，需授予 library 权限 |

仅在歌曲匹配且能读取收藏状态时显示按钮。支持固定歌曲 ID 的接口会按 ID 写入，切歌后拒绝旧状态，并回读确认结果。接口不可用或拒绝请求时，不会显示收藏成功。共享 `/favorites` 接口使用同一套适配。

Spotify 使用 [PKCE 授权流程](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow)，无需 Client Secret。在开发者后台登记 **`http://127.0.0.1/spotify/callback`**（不填端口）；每次授权选择临时本地端口，符合 Spotify 的[回调地址规则](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)。仅请求 `user-library-read`、`user-library-modify`、`user-read-currently-playing` 权限。开发模式仍受账号条件限制，详见[当前开发者要求](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)。授权账号的当前歌曲必须与桌面播放器匹配。收藏使用现行 `/me/library` 接口，不依赖桌面脚本提供资料库控制。

YesPlayMusic 优先直接使用本地 Web API 的可用登录态，不要求手填 Token 或预先保存 Cookie。需要本地接口 27232、10754 端口可用，可先在设置中“检测本地 API”。官方服务仅转发 API 请求，不会自动带上播放器浏览器窗口的 Cookie；如果接口返回未登录，扫码可为 OmniLyrics 单独授权。取消授权或关闭设置会停止登录。“移除已保存的授权”仅清除 OmniLyrics 保存的凭据，不会退出播放器账号。Spotify 刷新令牌与网易云登录 Cookie 保存在配置目录中的独立私密文件，macOS/Linux 仅允许当前用户读写，不包含在导出的设置中；请勿分享凭据文件。

## 五种界面预设

在**设置 → 常规 → 界面缩放**中，默认跟随当前显示器。可选择 100–200%，或自定义 75–300% 的比例，立即覆盖所有窗口的缩放。这不会改变已保存的歌词字号。配置文件中的 `appearance.uiScale` 为 `null` 时跟随系统，为 `1.5` 等数值时表示 150% 等手动比例。

原生托盘菜单提供缩放、布局、深浅主题、毛玻璃、翻译和字号快捷设置。若大比例导致设置窗口超出屏幕，选择“恢复 100% 缩放并找回设置窗口”，即可重置缩放并把完整设置窗口移回显示器。托盘菜单不受 OmniLyrics 界面缩放影响。

在「设置 → 外观」中选择布局，然后点击「应用」。

| 预设 | 显示方式 |
| --- | --- |
| 经典 | 悬浮显示当前行与下一行，可附加译文 |
| 紧凑 | 小窗口显示当前歌词与可选译文 |
| 专注 | 封面与歌曲信息旁显示多行歌词 |
| 竖向 | 顶部歌曲信息，下方竖向排列歌词 |
| 全屏 | 居中歌词与独立歌曲信息区；按 Esc 返回专注模式 |

外观设置支持原文／译文字号、背景透明度、毛玻璃，以及译文、OmniLyrics Logo 和播放器信息的显示开关。
毛玻璃效果取决于操作系统支持；不可用时使用所选的背景色与透明度。

「主题与配色」提供深色 + 红色、深色 + 蓝色、浅色 + 蓝色、浅色 + 红色预设。
也可以自定义深浅主题、主题色、文字色、高亮色与歌词背景色，命名保存后重复使用；点击「应用」启用。
配色预设不会覆盖布局、字号、透明度和毛玻璃设置。

上锁只锁定窗口位置，仍可调整大小、控制播放、收藏和打开设置。
点击锁按钮、使用托盘菜单，或在窗口获得焦点时按 **Ctrl+L** 解锁。
关闭歌词窗口会收起到托盘；要结束程序，请使用托盘菜单的「退出」。

## 歌词与匹配

OmniLyrics 优先在已启用来源中寻找真实逐字歌词，再回退到逐行歌词。
普通 LRC 默认模拟高亮，可关闭「为逐行歌词模拟高亮」改用整行高亮。
模拟只根据相邻行时间估算进度，不能还原真实逐字时间。

在外观设置中开启译文，即可显示来源提供的双语歌词。没有译文时留空，不自动生成翻译。

「设置 → 歌词与匹配」提供以下选项：

| 设置 | 说明 |
| --- | --- |
| 搜索策略 | 补充搜索在初次结果不匹配时扩大搜索；基础搜索减少请求。两者都优先逐字歌词。 |
| 匹配严格程度 | 标准匹配检查歌名、主唱、录音版本和已知时长；严格匹配还要求专辑一致、双方时长已知且相差不超过 2 秒。 |
| 歌词来源 | 分别启用播放器歌词（目前支持 YesPlayMusic）、QQ 音乐、网易云音乐，并选择优先使用的在线来源。 |
| 提前缓存 | 默认预取后续 5 首，可关闭或调整为 1–20 首。 |

修改搜索或匹配规则后，当前歌曲会按新规则重新加载。
如果歌词对应了其他录音版本，可以尝试严格匹配；它有助于减少错配，也可能让更多歌曲匹配不到歌词。
歌曲信息匹配不能保证音频时间完全对齐。

预取需要 Cider Web API 或 MPRIS TrackList 提供待播队列，尚未接入 Spotify Web API 队列或 macOS Apple Music 原生队列。
没有受支持的队列时，仍会获取和缓存当前歌曲。缓存可在重启后继续使用。

## CLI 与共享配置

以下命令假设 `OmniLyrics.Cli` 已在命令搜索路径中。从源码运行时，在仓库根目录将它替换为
`dotnet run --project src/OmniLyrics.Cli --`。

```bash
OmniLyrics.Cli                         # 终端歌词界面（TUI）
OmniLyrics.Cli --mode line             # 状态栏单行输出
OmniLyrics.Cli --mode json             # 组件 JSON 输出
OmniLyrics.Cli config                  # 交互式配置
OmniLyrics.Cli config show
OmniLyrics.Cli config language auto    # auto、en 或 zh-CN
OmniLyrics.Cli config lyrics strategy fallback  # fallback 或 quick
OmniLyrics.Cli config lyrics match balanced     # balanced 或 strict
OmniLyrics.Cli config lyrics sources player,qq,netease
OmniLyrics.Cli config lyrics prefer qq          # qq 或 netease
OmniLyrics.Cli config lyrics prefetch on        # on 或 off
OmniLyrics.Cli config lyrics count 5
```

GUI、交互终端和 CLI 共用偏好。在常规设置中选择「跟随系统」、English 或简体中文，GUI 语言会立即切换。

| 平台 | 配置目录 |
| --- | --- |
| Windows | `%APPDATA%\OmniLyrics` |
| macOS | `~/Library/Application Support/OmniLyrics` |
| Linux | `~/.config/omnilyrics` |

偏好保存在 `config.json`，Cider 令牌单独保存在 `cider-token`。
「高级 → 编辑配置文件」打开 JSON 编辑器；外部有效修改也会自动同步到设置和歌词窗口，无效修改则保留上次有效设置。
`OMNILYRICS_CONFIG_DIR` 可指定配置目录，`OMNILYRICS_LANGUAGE` 可覆盖单个进程的语言。

## Cider V3+ 连接与认证

接入目标为 Cider V3+，不支持 V2、V1；收藏使用 V4 的资料库 API。
在「设置 → 播放器连接 → Cider」选择播放连接：

| 模式 | 行为 |
| --- | --- |
| `auto` | 优先使用可用的 Cider Web API，再尝试系统连接。 |
| `webapi` | 使用 Cider Web API，适合 MPRIS 支持不完整的 V3。 |
| `mpris` | Linux 上优先使用具备所需控制能力的 Cider MPRIS，否则回退到 Web API。 |

对应的 CLI 命令为 `OmniLyrics.Cli config cider integration webapi`，也可选择 `auto` 或 `mpris`。
收藏始终通过 Web API，与播放连接的选择无关。

### 使用令牌

在 Cider 的 **Settings → Connectivity → Manage External Application Access** 创建应用令牌，
授予 `playback` 权限；需要收藏时同时授予 `library` 权限。
在 OmniLyrics 的 Cider 设置中填写令牌，或使用终端的隐藏输入提示：

```bash
OmniLyrics.Cli config cider token
OmniLyrics.Cli config cider test
```

令牌框留空会保留已保存的令牌，`config show` 不显示令牌内容。
自动化配置可使用 `config cider token --stdin` 从标准输入读取。

### 不使用令牌

在 Cider 中关闭 **Require API Tokens**，然后在 OmniLyrics 选择「无令牌」，或运行：

```bash
OmniLyrics.Cli config cider none
```

此时 API（包括 V4 收藏与取消收藏）无需令牌即可使用，此模式会忽略旧令牌文件。
OmniLyrics 不会替你切换 Cider 的认证开关。
Linux 上的 MPRIS 播放控制可以在 Cider API 仍需认证时免令牌使用；收藏仍遵循 API 的认证设置。

### 环境变量覆盖

`CIDER_AUTH_MODE=token` 或 `none` 覆盖保存的模式。令牌模式依次读取 `CIDER_API_TOKEN`、
`CIDER_TOKEN_FILE`、已保存的 `cider-token` 文件；无令牌模式忽略全部令牌来源。
修改环境变量后需重新启动应用。

## 共享服务与局域网

GUI、TUI 和 CLI（含 JSON 组件）会复用已有 OmniLyrics 服务。
没有服务时，本机实例按 **GUI → TUI → CLI** 优先级接管；健康服务不会因打开其他界面而被打断，退出后由剩余实例接管。
这一协调适用于使用相同配置目录与端口的实例。其他程序占用端口时，OmniLyrics 会等待重试。

本机 HTTP（`127.0.0.1:27270`）和 UDP 控制（`32651`）在同一台电脑的应用之间无需认证。这两个接口仅限回环地址，并拒绝来自浏览器页面的 API 请求。其他局域网设备必须通过 HTTPS 配对。

在来源设备的 **设置 → 局域网设备** 中启用共享并保存。选择本机局域网地址，生成邀请并私下传给接收设备，在其 **配对设备** 中粘贴邀请。选择已配对设备后，点击 **显示此设备的歌词**。共享默认关闭；配对默认只有歌词读取权限，只有生成邀请时勾选 **同时允许控制播放和收藏** 才会授权这些操作。

邀请五分钟有效，仅可使用一次。其中包含来源设备的证书指纹，发送凭据前会先验证；发现设备不代表信任。**取消邀请** 会使未使用的邀请失效。在来源设备上 **撤销授权** 会立即阻止该设备后续请求；**忘记远端设备** 仅删除接收设备保存的连接。

歌词来源设置适用于使用同一配置目录的 GUI、TUI 和 CLI，也适用于 CLI 的 `--control` 命令。本机服务接管独立运行，始终只发布本机播放内容，避免循环转发。来源离线或授权被撤销时会清空歌词，不会悄悄将播放控制切到本机。点击 **使用本机播放器** 可以切回。

CLI 和交互式终端配置使用相同设置：

```bash
OmniLyrics.Cli config lan                        # 交互式配置
OmniLyrics.Cli config lan on                     # 在来源设备开启共享
OmniLyrics.Cli config lan invite 192.168.1.50     # 私密、只读邀请
# 只有希望允许控制播放与收藏时，才在 invite 命令后加 --control。
OmniLyrics.Cli config lan discover               # 发现本地网络中的来源设备
OmniLyrics.Cli config lan pair                   # 在隐藏输入中粘贴邀请
# 管道输入使用 config lan pair --stdin，不要把邀请放到命令参数中。
OmniLyrics.Cli config lan peers                  # 显示 ID 和权限，不输出凭据
OmniLyrics.Cli config lan source DEVICE_ID
OmniLyrics.Cli config lan source local
OmniLyrics.Cli config lan revoke ACCESS_ID        # 在来源设备执行
OmniLyrics.Cli config lan off
```

来源设备需要保持新版 GUI、TUI 或 CLI 运行。HTTPS 使用 TCP **27271**，发现使用 UDP **32652**；防火墙只需在私有网络放行这些端口。发现采用 IPv4 广播，访客 Wi-Fi 或 VLAN 可能阻止广播，此时仍可通过邀请直接连接。直接配对支持私有 IPv4、IPv6 地址，不支持互联网地址。`config lan ports HTTPS_PORT UDP_PORT` 修改局域网端口，`config lan name NAME` 设置设备名称。发现设备要求双方使用相同发现端口。重新发现 IP 变化的设备时，仍会保持原来的证书验证。

配对凭据和设备证书独立保存在私有配置目录的 `lan-trust.json` 中，不要公开或复制到其他设备。发现与配对不包含 Cider、Spotify 或网易云凭据。包括自定义客户端在内，局域网 API 请求均须通过 HTTPS，验证已配对证书并携带 bearer token。只读授权不能读取或修改收藏，不会降级为明文或跳过证书验证。

`config server lan` 现在等同于启用安全共享。旧的 HTTP/UDP 全网卡监听会被限制为回环地址，请把远端 `server target` 配置迁移为配对。`config server listen ADDRESS` 和 `config server target HOST` 仅接受回环地址。`config server ports HTTP_PORT UDP_PORT` 以及已有 `--listen`、`--host`、`--http-port`、`--udp-port` / `OMNILYRICS_*` 覆盖项只配置本机通信。

## Web API

请求发送到服务地址，默认为 `http://127.0.0.1:27270`。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/snapshot` | 同一首歌曲的播放状态与歌词，包括来源提供的逐字时间和译文 |
| GET | `/playback/state` | 歌曲信息、播放器名称、播放位置和时长 |
| GET | `/lyrics` | 解析后的歌词；歌词不可用时为 `null`，无播放状态时返回 404 |
| POST | `/playback/play`、`/playback/pause`、`/playback/toggle` | 播放控制 |
| POST | `/playback/next`、`/playback/prev` | 切换歌曲 |
| POST | `/playback/seek` | 跳转位置，JSON 请求体如 `{"position":42.5}`，单位为秒 |
| GET | `/favorites` | 当前收藏状态；不支持时返回 404 |
| POST | `/favorites` | 使用上次 GET 结果修改收藏状态 |

`/snapshot` 包含 `service`、`protocolVersion`、`state`、`lyrics`、`loading`、`lyricsVersion`。
收藏 GET 返回 `{"trackId":"…","mediaKey":"…","isFavorite":false}`。
POST 请求体为 `{"previous":<上次 GET 结果>,"favorite":true}`，成功时返回确认后的状态。
歌曲已变化或无法确认修改时返回 409，不支持的连接返回 404。

```bash
curl http://127.0.0.1:27270/snapshot
curl -X POST http://127.0.0.1:27270/playback/seek \
  -H 'Content-Type: application/json' -d '{"position":42.5}'
OmniLyrics.Cli --control pause
```

桌面组件可使用 `--mode json`，其中包含当前／下一行歌词以及 `currentTranslation`、`nextTranslation`。
Quickshell 安装方式见 [集成说明](../integrations/quickshell/README.md)。
