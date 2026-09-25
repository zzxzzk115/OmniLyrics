# Lyricify 调研与本轮实现

调研与验收日期：2026-09-25。开发基线为 `dev`（`9155915`）；改动尚未提交。

## 结论

Lyricify Lyrics Helper 提供搜索、下载、解析能力，但不包含 Lyricify 完整应用或云端歌词关联服务。
Lyricify 4 的公开说明还涉及服务器歌词、用户标记和人工纠错。因此，引用同一个 Helper 包，并不能保证相同的歌词覆盖率和匹配质量。
这是依据公开流程作出的判断，未取得其服务器实现或完整 GUI 源码。
参见 [Lyricify 4 歌词来源说明](https://github.com/WXRIW/Lyricify-App/blob/2aad11e7404fc3770a90da4fa4c4403e3b7ad0f1/docs/Lyricify%204/README.md#歌词来源)。

## 已修复的获取与匹配问题

| 原问题 | 当前实现 |
| --- | --- |
| QQ 只有 LRC 时提前结束搜索，错过其他来源的逐字歌词 | 保留 LRC 回退，继续查找真正带有效词时长的歌词；所有播放器、GUI、CLI 使用相同优先级 |
| 网易云直接取第一条搜索结果 | 使用 Helper 统一搜索；另行检查歌名、主要艺人、Live／Remix／Acoustic 等版本标记和时长；拒绝明显不相容的录音 |
| 一个来源异常阻断全部流程 | 来源分别隔离失败，再尝试其他来源 |
| 临时失败被永久缓存 | 失败仅保留 1 分钟；普通歌词回退 10 分钟后可重新查找逐字歌词 |
| 切歌时才下载 | 播放队列能力接口、Cider API 与可选 MPRIS TrackList 适配；默认预取后续 5 首；缓存可跨进程复用 |
| QQ 下载格式被写死 | 自动识别格式；对 Helper 0.2.0 无法识别的短 LRC／译文增加时间标签检测回退 |
| 旧歌请求晚到，覆盖新歌 | 曲目标识与请求修订号同时校验；CLI、GUI 和快照服务拒绝旧歌结果 |
| 翻译、行结束时间在转换时丢失 | 模型、缓存、HTTP 快照保留译文和行结束；接入 QQ、网易云、YesPlayMusic 的译文 |

匹配仍是基于元数据的判断，无法从音频确认录音版本。更保守的条件可能拒绝部分有效候选，但不会为了显示 Karaoke 而放宽到明显不同的歌曲。
译文按时间一对一对齐，容许 350 毫秒的时间舍入差；缺行或不明确的译文留空，不能按行号强行拼接。没有自动翻译服务。

## 版本核对与网络抽样

生产依赖已从 **Helper 0.1.4 升级至 0.2.0**。NuGet 0.2.0 包声明的源码提交为 `a139e38`，
不等于调研时 Helper 仓库的 [`cabe0b7`](https://github.com/WXRIW/Lyricify-Lyrics-Helper/tree/cabe0b71d443a9b882a35c60c4f7f21addcbe751)。
不要把未发布的 master 改动算作已安装版本的能力。

在本机，用独立临时项目请求《海陆风》／李荣浩（无专辑与时长）和 “Shape of You”／Ed Sheeran（专辑 `÷`，233 秒）。
探针使用统一搜索、Medium 阈值，无账户凭据；单请求超时 6 秒，搜索等待上限 25 秒。

| 检查 | 观察 |
| --- | --- |
| 0.1.4 首轮，两首 × QQ／网易云 | 未返回达到阈值的搜索匹配 |
| 0.2.0 首轮 | 网易云两首匹配，但无 YRC；QQ 未返回匹配 |
| 0.2.0 复查 | 网易云两首返回非空 LRC，无 YRC；QQ 的 “Shape of You” 此次取得 92 行、711 个词时间 |

这是小样本探针，不是应用端到端覆盖率。结果随网络和提供方响应变化，不能把全部差异归因于升级。
本轮自动化回归采用模拟数据，验证匹配、回退、同步与显示行为，不声称所有歌曲都能获得逐字歌词。

## UI 参考与模拟高亮

以 [Lyricify App 当前 README](https://github.com/WXRIW/Lyricify-App/blob/2aad11e7404fc3770a90da4fa4c4403e3b7ad0f1/README.md)
实际引用的 `func-lyrics-display.png`、`func-lyrics-desktop.png`、`func-lyrics-vertical.png`、
`func-lyrics-fulscreen.png`、`func-lyrics-am-multiline.png` 为参考。早先看的 `02.png` 是旧图，已不作为当前设计依据。

OmniLyrics 本轮提供五种独立布局：经典双行、紧凑、封面分栏、竖向多行、全屏居中。
参考了信息层级、歌词留白、前后文明暗和封面区域的分工；没有复制 Lyricify 的专有界面代码、图片或品牌元素。
设置页提供左侧分类与中英文搜索、字号、颜色选择器、背景透明度、毛玻璃开关、译文、锁定、Logo 与播放器信息。

Lyricify 的公开介绍提过真／假 Karaoke，但公开应用仓库不足以核实具体模拟算法。
OmniLyrics 的可选模拟高亮是独立实现：在已知行起止时间之间按文本宽度扫过，并明确标为“模拟高亮”。
它不会生成或缓存虚假的词时间，也不会改变 API 中的原始歌词。真实 QRC／YRC 词时间优先使用；默认不开启模拟。

锁定固定窗口位置并隐藏悬浮工具栏；通过托盘、设置或 Ctrl+L 解锁。**当前不是跨平台鼠标穿透**。
毛玻璃依赖系统或合成器的支持；不支持时使用配置的背景色与透明度。

## 仍有差距的部分

- 尚无 Lyricify 云端的人工曲目关联、用户校正库、单曲候选选择与单曲时间偏移编辑。不能宣称错配已被完全消除。
- Helper 有更多来源与格式能力，但本次在线取词仍以 QQ、网易云及 YesPlayMusic 为主；未把每个提供方都接入。
- Apple Music Media User Token 与 Cider 外部应用 token 不同；未读取账户令牌，也未接入需要该凭据的 Apple Music 取词流程。
- 合唱／背景人声、音译、多演唱者布局尚未完整保留和呈现。
- Spotify Web API 队列和 macOS Apple Music 原生队列尚无适配。能获取播放状态不代表能读取待播队列。
- 收藏通过可选能力接口显示。当前具体适配为 Cider V4 library API；MPRIS／SMTC 本身不能据此宣称支持收藏。GUI 接入 CLI 时，经 CLI 的收藏协议完成。
- Cider 的收藏端点针对当前播放歌曲，只能在提交前后检查曲目并拒绝过期结果，无法在客户端实现服务器端的原子“按 ID 写入”。不会自动重试收藏写入。
- 本机实际运行验证为 Linux；macOS、Windows 和 Cider V3 尚需对应环境验收。

## 其他仓库

[Lyricify Backgrounds](https://github.com/WXRIW/Lyricify-Backgrounds/tree/bdd31b5c5f678bbdf8946c6258883291f6b58657)
可参考封面与背景的资源管理，但 WPF／WinUI、DirectComposition 和 HLSL 不能直接当作跨平台 Avalonia 控件使用。许可证为 Apache-2.0。
[Lyricify Lines Creator](https://github.com/WXRIW/Lyricify-Lines-Creator/tree/a30d26191592916d4e4df8f57b00c0b21b988f31)
是 C++／HiEasyX 人工打轴工具，不是在线逐字歌词获取器，许可证为 LGPL-2.1。
[Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper) 采用 Apache-2.0；使用和移植时应分别保留对应许可。

## Apple Music Sing／人声衰减

核对 [Cider 官方 RPC 文档](https://github.com/ciderapp/docs/blob/main/docs/1.client/rpc.md) 后，没有找到可供 OmniLyrics 调用的人声衰减控制端点。目录对象中的 `isVocalAttenuationAllowed` 是歌曲元数据，不能证明播放器暴露了可控制的 K 歌模式。Cider 团队在 [2024 年回复](https://itch.io/t/3489681/apple-music-sing) 中也区分了逐字歌词和人声衰减；该历史回复本身不能证明所有 V4 版本的能力。

[Apple 的 Sing 说明](https://support.apple.com/en-ie/127940) 列出了受支持的 iPhone、iPad 和 Apple TV，并未列出 Mac。当前未加入无法验证的「开启 Cider K 歌模式」按钮，也没有改变播放器的声音设置；OmniLyrics 的 Karaoke 功能指逐字歌词显示。后续有明确控制协议时，应按播放器能力显示该选项。
