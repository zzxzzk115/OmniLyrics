# 使用指南

[English](./user-guide.md) | 简体中文 · [README](../README.zh-CN.md)

安装与编译步骤见 [README](../README.zh-CN.md#编译运行)。点击歌词窗口的齿轮按钮或使用托盘菜单打开设置。

## 平台与播放器

| 平台 | 连接方式 | 播放器 |
| --- | --- | --- |
| Windows | 系统媒体控制 SMTC | Spotify 等接入系统媒体会话的播放器 |
| macOS | `media-control` | Apple Music、Spotify、Cider 等提供 Now Playing 信息的播放器 |
| Linux | MPRIS | Spotify 和其他 MPRIS 播放器 |
| 三个平台 | 播放器专用 API | Cider V3+、YesPlayMusic |

macOS 需先运行 `brew install media-control` 安装依赖。
播放控制、队列与收藏取决于播放器提供的能力。支持收藏时才会显示收藏按钮，目前收藏适配使用 Cider V4 的资料库 API。

## 五种界面预设

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

默认只监听本机：HTTP `127.0.0.1:27270`，UDP 端口 `32651`。
允许可信局域网中的其他设备连接：

```bash
OmniLyrics.Cli config server lan        # 监听所有 IPv4 网卡
# 修改监听设置后，重启正在运行的服务。
OmniLyrics.Cli --mode line
# 在另一台设备上，填写服务所在电脑的实际局域网地址：
OmniLyrics.Cli config server target 192.168.1.50
# 恢复仅本机设置：
OmniLyrics.Cli config server local
OmniLyrics.Cli config server target 127.0.0.1
```

`config server listen ADDRESS` 可指定网卡地址，`config server ports HTTP_PORT UDP_PORT` 可修改端口。
指定远程主机后，只连接该主机；它离线时等待重连，不会在本机另开服务。

单次运行可使用 `--listen`、`--host`、`--http-port`、`--udp-port` 覆盖设置。
对应环境变量为 `OMNILYRICS_LISTEN_ADDRESS`、`OMNILYRICS_CONTROL_HOST`、`OMNILYRICS_HTTP_PORT`、
`OMNILYRICS_UDP_PORT`，命令行参数优先。
这些 HTTP／UDP 接口没有认证，只适用于可信网络；Cider 令牌不保护 OmniLyrics 自身的服务。

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
