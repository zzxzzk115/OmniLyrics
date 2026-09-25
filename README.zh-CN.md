<p align="center">
  <img src="./src/OmniLyrics.Gui/Assets/logo.png" alt="OmniLyrics" width="96" height="96">
</p>

# OmniLyrics

[English](./README.md) | 简体中文

OmniLyrics：一次个人尝试，打造一直想要的歌词工具——提供 CLI、TUI、GUI，并支持跨平台。

版本 **0.4.0** 新增五种 GUI 布局、双语逐字歌词、共享偏好与队列缓存。
配置与集成方式见 [使用指南](./docs/user-guide.zh-CN.md)。

## 效果展示

当前 GUI（专注布局）：

![GUI 专注布局](./media/images/gui_preset_focus.png)

早期 Windows GUI（上方：Cider 迷你播放器，下方：OmniLyrics）：

![Windows GUI](./media/images/gui_windows.png)

Windows 终端（默认模式）：

![CLI（Windows 终端）](./media/images/cli_windows_terminal.png)

macOS 终端（默认模式）：

![CLI（macOS 终端）](./media/images/cli_macos_terminal.png)

Linux Waybar（单行模式，--mode line）：

![CLI（Linux Waybar）](./media/images/cli_linux_waybar.jpg)

## 编译运行

下载并安装 [.NET 10 LTS SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

> macOS 上需要安装 `media-control`：
> ```bash
> brew install media-control
> ```

克隆仓库：

```bash
git clone https://github.com/zzxzzk115/OmniLyrics.git
```

编译并运行：

```bash
cd OmniLyrics

# 编译整个解决方案
dotnet build

# 启动 GUI
dotnet run --project src/OmniLyrics.Gui

# 启动 CLI
dotnet run --project src/OmniLyrics.Cli

# 以单行输出模式启动 CLI
# （适用于状态栏）
dotnet run --project src/OmniLyrics.Cli -- --mode line

# -------------------------------------------------------------------
# 远程控制命令
# 这些命令需要已有一个以歌词／守护进程模式运行的 OmniLyrics 实例。
# -------------------------------------------------------------------

# 播放控制
dotnet run --project src/OmniLyrics.Cli -- --control play
dotnet run --project src/OmniLyrics.Cli -- --control pause
dotnet run --project src/OmniLyrics.Cli -- --control toggle

# 切换歌曲
dotnet run --project src/OmniLyrics.Cli -- --control prev
dotnet run --project src/OmniLyrics.Cli -- --control next

# 跳转到指定位置（单位：秒）
dotnet run --project src/OmniLyrics.Cli -- --control seek 10
```

### Waybar 模块配置

```json
// OmniLyrics
"custom/OmniLyrics": {
  "format": "  {text}",
  "exec": "/path/to/OmniLyrics.Cli --mode line",
  "return-type": "text",
  "escape": true
},
```

---

## Web API 接口

默认模式和单行模式在 `http://127.0.0.1:27270` 提供 HTTP 服务。
GUI、TUI 和 CLI 会复用已有服务；服务停止后，其余本机实例按 GUI → TUI → CLI 的顺序接管。快照、收藏与可信局域网的配置见 [协议指南](./docs/user-guide.zh-CN.md#web-api)。

### 歌词 API

#### **GET /lyrics**

以 JSON 格式返回当前歌曲解析后的 LRC 歌词。

**响应**

```json
[
  { "timestamp": "00:00:12.4500000", "text": "We're no strangers to love" },
  { "timestamp": "00:00:16.8000000", "text": "You know the rules and so do I" }
]
```

没有可用歌词时：

```json
null
```

---

### 播放控制 API

所有控制接口在成功时返回 `200 OK`。

#### **POST /playback/play**

开始播放。

#### **POST /playback/pause**

暂停播放。

#### **POST /playback/toggle**

切换播放／暂停状态。

#### **POST /playback/next**

切换到下一首。

#### **POST /playback/prev**

切换到上一首。

#### **POST /playback/seek**

跳转到指定位置。

**请求体：**

```json
{ "position": 42.5 }
```

（单位：秒）

---

### 元数据 API

#### **GET /playback/state**

以 JSON 格式返回当前播放器状态：

```json
{
  "title": "Song Title",
  "artists": ["Artist A", "Artist B"],
  "album": "Best Album",
  "position": "00:00:12.3400000",
  "duration": "00:03:00",
  "playing": true,
  "sourceApp": "Cider",
  "artworkUrl": "https://example.com/art.jpg",
  "artworkWidth": 640,
  "artworkHeight": 640
}
```

---

## Cider V3+ 设置

Cider V4 是当前商用版本，V3 仍为兼容目标。不支持 V2 和 V1。

- **启用认证：** 在 Cider 的 Settings → Connectivity → Manage External Application Access 中创建应用令牌，然后在 OmniLyrics 的「设置 → 播放器连接」中填写，或在 CLI 中运行 `config cider token`。
- **关闭认证：** 在 Cider 中关闭 **Require API Tokens**，然后在 OmniLyrics 中选择「无令牌」，或运行 `config cider none`。此模式下 API 无需令牌即可使用，包括播放器支持的收藏功能。

GUI、交互终端和 CLI 共享这些设置。令牌输入框留空会保留已保存的令牌。参见 [连接详情](./docs/user-guide.zh-CN.md#cider-v3-连接与认证)。

## 待办清单

通用后端：

- [x] Windows SMTC
- [x] Linux MPRIS
- [x] macOS media-control

播放器专用后端：

- [x] [Cider V3+](https://cider.sh/)（V4 已测试，V3 为兼容目标）
- [x] [YesPlayMusic](https://github.com/qier222/YesPlayMusic)

服务与 API

- [x] UDP 客户端与服务端（localhost:32651）
- [x] Web API（http://localhost:27270）

CLI：

- [x] 多行模式（默认）
- [x] 单行模式（适用于 Waybar）
- [x] 远程控制（通过 UDP 命令）

TUI：

- [x] 交互式共享配置

GUI：

- [x] 五种布局、双语逐字歌词、窗口锁定和可选收藏功能
- [x] 支持搜索的设置、外观调整和中英界面

## 致谢

- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)
- [WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [Tmds.DBus](https://github.com/tmds/Tmds.DBus)
- [media-control](https://github.com/ungive/media-control)

## 许可

本项目采用 [MIT 许可证](./LICENSE)。
