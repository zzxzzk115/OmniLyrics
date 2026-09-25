using System.Globalization;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core;

/// <summary>Shared English/Chinese catalog; player names and protocol values stay unchanged.</summary>
public static class Localization
{
    public static readonly IReadOnlyDictionary<string, (string English, string Chinese)> Catalog =
        new Dictionary<string, (string, string)>
    {
        ["Settings"] = ("Settings", "设置"),
        ["SettingsTitle"] = ("Settings", "设置"),
        ["ApplySettings"] = ("Apply", "应用"),
        ["SettingsApplied"] = ("Changes applied", "更改已应用"),
        ["GeneralSubtitle"] = ("Your language and listening preferences.", "语言与聆听偏好。"),
        ["AppearanceSubtitle"] = ("Make room for your music.", "让歌词以你喜欢的方式呈现。"),
        ["ThemeColors"] = ("Theme & colors", "主题与配色"),
        ["ThemeColorsSubtitle"] = ("Your music, in your colors.", "为音乐选择属于你的色彩。"),
        ["ThemePreset"] = ("Color preset", "配色预设"),
        ["ThemePresetExplanation"] = ("Colors apply across the app. Layout and type sizes stay as you set them.", "配色应用于整个应用，布局和字号保持各自的设置。"),
        ["ThemeMode"] = ("Interface", "界面风格"),
        ["DarkTheme"] = ("Dark", "深色"),
        ["LightTheme"] = ("Light", "浅色"),
        ["AccentColor"] = ("Accent color", "主题色"),
        ["CustomTheme"] = ("Custom", "自定义"),
        ["DarkRed"] = ("Dark + Red", "深色 + 红色"),
        ["DarkBlue"] = ("Dark + Blue", "深色 + 蓝色"),
        ["LightBlue"] = ("Light + Blue", "浅色 + 蓝色"),
        ["LightRed"] = ("Light + Red", "浅色 + 红色"),
        ["SaveThemePreset"] = ("Save your palette", "保存自定义配色"),
        ["ThemeName"] = ("Preset name", "预设名称"),
        ["SavePreset"] = ("Save preset", "保存预设"),
        ["DeleteThemePreset"] = ("Delete this preset", "删除此预设"),
        ["ThemeSaveHint"] = ("An existing name updates that preset. Apply activates your colors; saving a preset keeps them for later.", "同名保存会更新预设。点击「应用」启用当前配色；保存预设可供以后选择。"),
        ["ThemeNameRequired"] = ("Enter a name for this palette.", "请为配色预设填写名称。"),
        ["ThemePresetSaved"] = ("Palette saved. Apply to use these colors.", "配色已保存，点击「应用」即可启用。"),
        ["ThemePresetDeleted"] = ("Preset deleted. Current colors are kept.", "预设已删除，当前配色仍保留。"),
        ["ThemePresetSaveError"] = ("Could not save. Use 1–64 characters and at most 32 custom presets; check the configuration file.", "无法保存：名称限 1–64 个字符，自定义预设最多 32 个；请检查配置文件。"),
        ["PlayerConnectionsSubtitle"] = ("Connect your player, keep your music in sync.", "连接播放器，让音乐与歌词同步。"),
        ["AboutSubtitle"] = ("A companion for every song.", "陪伴每一首歌。"),
        ["Typography"] = ("Text & color", "文字与色彩"),
        ["ColorsLabel"] = ("Text · highlight · background", "文字 · 高亮 · 背景"),
        ["Background"] = ("Background", "背景"),
        ["DisplayOptions"] = ("Details & behavior", "显示与交互"),
        ["PresetExplanation"] = ("Reading layouts adapt to your window. Playback controls stay with the song.", "阅读布局随窗口调整，播放操作始终位于歌曲信息区。"),
        ["AdvancedMenu"] = ("Advanced ···", "高级 ···"),
        ["EditConfiguration"] = ("Edit configuration…", "编辑配置文件…"),
        ["ReloadConfiguration"] = ("Reload", "重新载入"),
        ["ConfigurationEditorHint"] = ("Edit shared preferences as JSON. Valid changes sync automatically. API tokens are stored separately.", "以 JSON 编辑共享偏好。有效更改会自动同步；API 令牌单独存储。"),
        ["ConfigurationReloaded"] = ("Updated from configuration file", "已同步配置文件的修改"),
        ["ConfigurationInvalid"] = ("Invalid configuration. Check JSON, values and ranges; current settings are kept.", "配置无效，请检查 JSON、设置值与范围；当前设置已保留。"),
        ["ConfigurationConflict"] = ("The file changed elsewhere. Reload before applying your edits.", "文件已被外部修改，请重新载入后再应用编辑。"),
        ["SharedPreferences"] = ("Shared preferences for all players and frontends.", "所有播放器和前端共用的偏好设置。"),
        ["Language"] = ("Language", "语言"),
        ["SystemLanguage"] = ("Follow system", "跟随系统"),
        ["KaraokeFirst"] = ("Karaoke lyrics first", "优先使用逐字歌词"),
        ["KaraokeExplanation"] = ("Look for word-timed lyrics across available sources before falling back to line-synced lyrics.", "先从可用来源查找逐字歌词，均不可用时再使用逐行歌词。"),
        ["LyricsMatching"] = ("Lyrics & matching", "歌词与匹配"),
        ["LyricsMatchingSubtitle"] = ("Find the right words for every song.", "为每首歌找到准确的歌词。"),
        ["SearchStrategy"] = ("Search strategy", "搜索策略"),
        ["FallbackSearch"] = ("Search with fallback", "补充搜索"),
        ["QuickSearch"] = ("Basic search only", "仅基础搜索"),
        ["SearchStrategyExplanation"] = ("Fallback adds a broader search if the first results do not match. Basic search uses fewer requests. Both keep karaoke lyrics first.", "补充搜索会在初次结果不匹配时扩大搜索范围；基础搜索减少请求次数。两种策略均优先逐字歌词。"),
        ["MatchMode"] = ("Match accuracy", "匹配严格程度"),
        ["BalancedMatch"] = ("Standard", "标准匹配"),
        ["StrictMatch"] = ("Strict", "严格匹配"),
        ["MatchModeExplanation"] = ("Both check title, lead artist and recording version. Standard allows 3–6 seconds of duration difference when known. Strict also requires the same album and known durations within 2 seconds; it may find fewer lyrics.", "两种模式均校验歌名、主唱和录音版本。标准匹配允许已知时长相差 3–6 秒；严格匹配还要求专辑一致、时长已知且相差不超过 2 秒，可能降低命中率。"),
        ["LyricsSources"] = ("Lyric sources", "歌词来源"),
        ["PlayerLyrics"] = ("Lyrics from the player", "播放器提供的歌词"),
        ["PlayerLyricsExplanation"] = ("Use direct track lyrics when the connection supports them. Currently available for YesPlayMusic.", "连接支持时直接获取当前曲目的歌词。目前支持 YesPlayMusic。"),
        ["QQMusic"] = ("QQ Music", "QQ 音乐"),
        ["Netease"] = ("NetEase Cloud Music", "网易云音乐"),
        ["PreferredLyricSource"] = ("Preferred online source", "优先在线来源"),
        ["LyricsSourcesExplanation"] = ("Player lyrics are checked first, then enabled online sources in this order. Genuine word timing takes priority over any line-only result. Applying changes searches the current song again with matching cache entries.", "先检查播放器歌词，再按优先顺序查询已启用的在线来源。真实逐字歌词优先于任何逐行结果。应用后会按新规则重新匹配当前歌曲，并仅复用符合规则的缓存。"),
        ["LyricSourceRequired"] = ("Enable at least one lyric source.", "请至少启用一个歌词来源。"),
        ["LyricPolicySummary"] = ("Search: {0}; matching: {1}; sources: {2}; preferred: {3}.", "搜索：{0}；匹配：{1}；来源：{2}；优先：{3}。"),
        ["LyricStrategyPrompt"] = ("Search [fallback/quick, Enter keeps current]: ", "搜索策略 [fallback/quick，回车保留]："),
        ["LyricMatchPrompt"] = ("Matching [balanced/strict, Enter keeps current]: ", "匹配程度 [balanced/strict，回车保留]："),
        ["LyricSourcesPrompt"] = ("Sources [player,qq,netease; Enter keeps current]: ", "歌词来源 [player,qq,netease；回车保留]："),
        ["LyricPriorityPrompt"] = ("Preferred online source [qq/netease, Enter keeps current]: ", "优先在线来源 [qq/netease，回车保留]："),
        ["LyricPolicyInvalid"] = ("Unknown lyric search strategy, match mode or preferred source.", "未知的歌词搜索策略、匹配模式或优先来源。"),
        ["Prefetch"] = ("Cache upcoming tracks in advance", "提前缓存待播歌曲的歌词"),
        ["PrefetchExplanation"] = ("Used when the player provides its upcoming queue. Cached lyrics are reused across sessions.", "播放器提供待播队列时生效。缓存可在下次运行时继续使用。"),
        ["UpcomingTracks"] = ("Upcoming tracks", "预取歌曲数量"),
        ["SavePreferences"] = ("Save preferences", "保存偏好"),
        ["PlayerConnections"] = ("Player connections", "播放器连接"),
        ["PlayerConnectionExplanation"] = ("Playback follows the active player through the system media connection. Some players offer additional connection options.", "通过系统媒体接口跟随当前播放器。部分播放器提供额外的连接选项。"),
        ["CiderOptions"] = ("Cider · additional connection options", "Cider · 额外连接选项"),
        ["Integration"] = ("Playback integration", "播放连接方式"),
        ["AutoIntegration"] = ("Automatic · prefer Web API, then system connection", "自动 · 优先 Web API，其次系统接口"),
        ["WebApiIntegration"] = ("Web API · Cider V3+", "Web API · Cider V3+"),
        ["MprisIntegration"] = ("MPRIS · for Cider V4 on Linux", "MPRIS · 适用于 Linux 上的 Cider V4"),
        ["TokenMode"] = ("Use an API token", "使用 API 令牌"),
        ["NoTokenMode"] = ("No token — authentication is disabled in Cider", "不使用令牌 · 已在 Cider 中关闭接口认证"),
        ["EnterToken"] = ("Enter API token", "输入 API 令牌"),
        ["TokenSaved"] = ("Token saved · leave blank to keep it", "令牌已保存 · 留空以保留"),
        ["FavoritesAccess"] = ("For favorites, allow playback and library access in Cider.", "使用收藏功能时，请在 Cider 中授予播放和资料库权限。"),
        ["EnvironmentOverride"] = ("Environment overrides are active. Remove them to use only saved settings.", "环境变量正在覆盖设置。移除后将仅使用保存的设置。"),
        ["TestCider"] = ("Test Cider connection", "测试 Cider 连接"),
        ["SaveCider"] = ("Save Cider connection", "保存 Cider 连接"),
        ["Close"] = ("Close", "关闭"),
        ["Previous"] = ("Previous track", "上一首"),
        ["Next"] = ("Next track", "下一首"),
        ["PlayPause"] = ("Play / pause", "播放／暂停"),
        ["ShowLyrics"] = ("Show Lyrics", "显示歌词"),
        ["Quit"] = ("Quit", "退出"),
        ["Waiting"] = ("Waiting for player", "等待播放器"),
        ["MusicPlayer"] = ("Music player", "音乐播放器"),
        ["Connecting"] = ("Connecting…", "正在连接…"),
        ["ServiceUnavailable"] = ("Waiting for the lyric service. Check the configured host and ports.", "正在等待歌词服务，请检查配置的主机和端口。"),
        ["SharedPlayback"] = ("Playback shared with OmniLyrics service", "通过 OmniLyrics 服务共享播放状态"),
        ["DirectPlayback"] = ("Playback from {0}", "播放来源：{0}"),
        ["ControlFailed"] = ("Could not send playback command. Please try again.", "播放指令发送失败，请重试。"),
        ["PreferencesReadError"] = ("Cannot read preferences. Check the configuration file.", "无法读取偏好，请检查配置文件。"),
        ["ConfigReadError"] = ("Cannot read configuration. Check the file and permissions before saving.", "无法读取配置，请先检查文件和权限。"),
        ["TokenInvalid"] = ("Enter a valid single-line token, or choose No token.", "请输入有效的单行令牌，或选择不使用令牌。"),
        ["ConnectionSaved"] = ("Saved. All frontends use the updated settings automatically.", "已保存。所有前端会自动使用新设置。"),
        ["ConfigSaveError"] = ("Could not save. Check the configuration file and permissions.", "保存失败，请检查配置文件和权限。"),
        ["CiderConnecting"] = ("Connecting to Cider…", "正在连接 Cider…"),
        ["CiderConnected"] = ("Connected with these settings. No library changes were made.", "连接成功。此次测试没有修改资料库。"),
        ["CiderConnectionError"] = ("Cannot connect. Check Cider, Require API Tokens and the token's playback permission.", "无法连接，请检查 Cider、接口认证开关和令牌的播放权限。"),
        ["TokenReadError"] = ("Could not read the token. Check the configuration and permissions.", "无法读取令牌，请检查配置和权限。"),
        ["PreferencesSaved"] = ("Saved for all players and frontends.", "已保存，适用于所有播放器和前端。"),
        ["PreferencesSaveError"] = ("Could not save preferences. Check the configuration file and permissions.", "无法保存偏好，请检查配置文件和权限。"),
        ["NowPlaying"] = ("Now Playing:", "正在播放："),
        ["UnknownArtist"] = ("Unknown Artist", "未知艺人"),
        ["SearchingLyrics"] = ("Searching lyrics...", "正在查找歌词…"),
        ["NoLyrics"] = ("(No lyrics found)", "（未找到歌词）"),
        ["ConfigMenu"] = ("OmniLyrics preferences\n  1. Lyrics and caching\n  2. Player connection: Cider\n  3. Language\n  Enter. Cancel", "OmniLyrics 偏好\n  1. 歌词与缓存\n  2. 播放器连接：Cider\n  3. 语言\n  回车：取消"),
        ["ChoosePreferences"] = ("Choose [1/2/3]: ", "请选择 [1/2/3]："),
        ["LanguagePrompt"] = ("Language [auto / en / zh-CN]: ", "语言 [auto / en / zh-CN]："),
        ["ConfigUsage"] = ("Usage: config [show|language auto|en|zh-CN|lyrics prefetch on|off|lyrics count 1..20|cider|server]", "用法：config [show|language auto|en|zh-CN|lyrics prefetch on|off|lyrics count 1..20|cider|server]"),
        ["LyricsUsage"] = ("Usage: config lyrics prefetch on|off | count 1..20 | strategy fallback|quick | match balanced|strict | sources player,qq,netease | prefer qq|netease", "用法：config lyrics prefetch on|off | count 1..20 | strategy fallback|quick | match balanced|strict | sources player,qq,netease | prefer qq|netease"),
        ["ServerUsage"] = ("Usage: config server local|lan|listen ADDRESS|target HOST|ports HTTP UDP", "用法：config server local|lan|listen ADDRESS|target HOST|ports HTTP UDP"),
        ["NonInteractiveConfig"] = ("Use config show, config lyrics prefetch on|off or config cider for connection settings.", "请使用 config show、config lyrics prefetch on|off，或 config cider 配置播放器连接。"),
        ["NonInteractiveCider"] = ("Use config cider token --stdin or config cider none for non-interactive setup.", "非交互配置请使用 config cider token --stdin 或 config cider none。"),
        ["ListenAddressError"] = ("The listen address must be an IPv4 or IPv6 address.", "监听地址必须为 IPv4 或 IPv6 地址。"),
        ["PortRangeError"] = ("Ports must be between 1 and 65535.", "端口必须介于 1 与 65535 之间。"),
        ["ControlHostError"] = ("The control host must be a destination address or hostname, not a wildcard.", "控制主机必须为目标地址或主机名，不能使用通配地址。"),
        ["EnvironmentPortError"] = ("The port environment variable must contain an integer.", "端口环境变量必须为整数。"),
        ["Appearance"] = ("Appearance", "外观"),
        ["General"] = ("General", "常规"),
        ["About"] = ("About", "关于"),
        ["Author"] = ("Author", "作者"),
        ["Repository"] = ("GitHub repository", "GitHub 仓库"),
        ["SearchSettings"] = ("Search settings", "搜索设置"),
        ["NoSettingsFound"] = ("No matching settings", "未找到匹配的设置"),
        ["Version"] = ("Version {0}", "版本 {0}"),
        ["Typography"] = ("Text and colors", "文字与颜色"),
        ["FontSize"] = ("Lyric size", "原文字号"),
        ["TranslationFontSize"] = ("Translation size", "译文字号"),
        ["TextColor"] = ("Text color", "文字颜色"),
        ["HighlightColor"] = ("Highlight color", "高亮颜色"),
        ["BackgroundColor"] = ("Background color", "背景颜色"),
        ["BackgroundOpacity"] = ("Background opacity", "背景不透明度"),
        ["UseBlur"] = ("Frosted glass background", "毛玻璃背景"),
        ["BlurExplanation"] = ("Uses system blur when available; otherwise falls back to the selected background color and opacity.", "系统支持时使用背景模糊；否则使用所选背景颜色与透明度。"),
        ["ColorHint"] = ("Colors use #RRGGBB, for example #F3C879.", "颜色格式为 #RRGGBB，例如 #F3C879。"),
        ["ShowTranslation"] = ("Show translation when available", "有译文时显示双语歌词"),
        ["TranslationExplanation"] = ("Translations come from the lyric source. Missing translations leave only the original text.", "译文由歌词来源提供；缺少译文时只显示原文。"),
        ["ResetAppearance"] = ("Restore appearance defaults", "恢复默认外观"),
        ["Favorite"] = ("Add to favorites", "收藏歌曲"),
        ["Unfavorite"] = ("Remove from favorites", "取消收藏"),
        ["FavoriteBusy"] = ("Updating favorite…", "正在更新收藏…"),
        ["FavoriteError"] = ("Favorite was not confirmed. Please try again.", "收藏操作未确认，请重试。"),
        ["Preset"] = ("Layout preset", "布局预设"),
        ["ClassicPreset"] = ("Classic · two lines", "经典 · 双行歌词"),
        ["CompactPreset"] = ("Compact · one line", "紧凑 · 单行歌词"),
        ["FocusPreset"] = ("Focus · cover and lyrics", "专注 · 封面与歌词"),
        ["PortraitPreset"] = ("Portrait · stacked lyrics", "竖向 · 多行歌词"),
        ["FullscreenPreset"] = ("Fullscreen · centered lyrics", "全屏 · 居中歌词"),
        ["ApproximateHighlight"] = ("Simulate highlighting for line-synced lyrics", "为逐行歌词模拟高亮"),
        ["ApproximateExplanation"] = ("An estimated sweep between line timestamps. It cannot reproduce the singer's word timing.", "按相邻行时间估算扫过效果，不能还原真实演唱的逐字时间。"),
        ["WordTiming"] = ("Word-synced", "逐字同步"),
        ["LineTiming"] = ("Line-synced", "逐行同步"),
        ["ApproximateTiming"] = ("Simulated highlight", "模拟高亮"),
        ["ShowLogo"] = ("Show OmniLyrics logo beside song information", "在歌曲信息左侧显示 OmniLyrics Logo"),
        ["ShowPlayerInfo"] = ("Show player information", "显示播放器信息"),
        ["LockWindow"] = ("Lock window", "锁定窗口"),
        ["UnlockWindow"] = ("Unlock window", "解锁窗口"),
        ["LockExplanation"] = ("Prevent moving the window. Resizing, playback and settings remain available. Unlock with the lock button, tray, Settings, or Ctrl+L.", "仅禁止拖动窗口位置；仍可缩放窗口、控制播放和打开设置。可通过锁按钮、托盘、设置或 Ctrl+L 解锁。"),
        ["SaveAppearance"] = ("Save appearance", "保存外观"),
        ["AppearanceSaved"] = ("Appearance saved.", "外观已保存。"),
        ["AppearanceError"] = ("Could not save appearance settings.", "无法保存外观设置。") ,
        ["CiderMenu"] = ("Cider connection\n  1. Use API token\n  2. No token (Require API Tokens is disabled in Cider)\n  Enter. Cancel", "Cider 连接\n  1. 使用 API 令牌\n  2. 不使用令牌（已在 Cider 中关闭接口认证）\n  回车：取消"),
        ["Choose"] = ("Choose [1/2]: ", "请选择 [1/2]："),
        ["KaraokeCli"] = ("Word-timed karaoke lyrics have highest priority for every player.", "所有播放器均优先使用逐字歌词。"),
        ["PrefetchPrompt"] = ("Prefetch upcoming tracks where available? [Y/n]: ", "队列可用时预取待播歌词？[Y/n]："),
        ["SavedLyricsCli"] = ("Saved shared lyric preferences.", "已保存共享歌词偏好。"),
        ["SavedIntegration"] = ("Saved Cider playback integration.", "已保存 Cider 播放连接方式。"),
        ["CiderTestOk"] = ("Cider API connected.", "Cider API 连接成功。"),
        ["CiderTestError"] = ("Cannot connect. Check Cider, its authentication setting, and your token.", "无法连接，请检查 Cider、接口认证设置及令牌。"),
        ["ServerSaved"] = ("Saved server settings. Restart a running control server to apply listening changes.", "服务设置已保存。监听地址或端口变更需要重启控制服务。"),
        ["Cancelled"] = ("Configuration cancelled.", "已取消配置。"),
        ["ConfigError"] = ("Unable to read or save configuration. Check the configuration file and permissions.", "无法读取或保存配置，请检查配置文件和权限。"),
        ["HiddenTokenPrompt"] = ("API token (hidden; blank keeps the saved token): ", "API 令牌（输入不回显，留空保留已保存令牌）："),
        ["NoTokenEntered"] = ("No token entered and no saved token exists. Nothing was changed.", "没有输入令牌，也没有已保存令牌。配置未更改。"),
        ["NoTokenArgument"] = ("Use --stdin for piped token input. Tokens are not accepted as command arguments.", "管道输入令牌请使用 --stdin；令牌不能作为命令参数。"),
        ["PrefetchRange"] = ("Prefetch count must be between 1 and 20.", "预取数量必须介于 1 与 20 之间。"),
        ["LanguageRange"] = ("Language must be auto, en or zh-CN.", "语言必须为 auto、en 或 zh-CN。"),
        ["LanguageSaved"] = ("Language saved.", "语言已保存。"),
        ["ConfigLocation"] = ("Configuration: {0}", "配置文件：{0}"),
        ["LyricSummary"] = ("Lyrics: karaoke first; queue prefetch {0}, up to {1} tracks.", "歌词：逐字优先；队列预取 {0}，最多 {1} 首。"),
        ["LanguageSummary"] = ("Language: {0}", "语言：{0}"),
        ["AuthSummary"] = ("Cider authentication: {0}", "Cider 认证方式：{0}"),
        ["IntegrationSummary"] = ("Cider integration: {0}", "Cider 连接方式：{0}"),
        ["TokenSummary"] = ("Token: {0}", "令牌：{0}"),
        ["TokenUnused"] = ("not used / not configured", "未使用／未配置"),
        ["TokenHidden"] = ("configured (hidden)", "已配置（隐藏）"),
        ["ServerSummary"] = ("Listen: {0}; HTTP {1}; UDP {2}; control host {3}", "监听：{0}；HTTP {1}；UDP {2}；控制主机 {3}"),
        ["AuthSaved"] = ("Saved Cider authentication: {0}. All frontends share this configuration.", "已保存 Cider 认证方式：{0}。所有前端共用此配置。"),
        ["CiderEnvironment"] = ("Cider environment overrides are present.", "Cider 环境变量正在覆盖设置。"),
        ["CiderEnvironmentSaved"] = ("Cider environment overrides are present; remove them to use only saved settings.", "Cider 环境变量正在覆盖设置；移除后将仅使用保存的设置。"),
        ["StartServiceError"] = ("Could not start the control service. Check the configured address and whether its ports are already in use.", "无法启动控制服务，请检查监听地址及端口是否已被占用。")
    };

    private static string _language = ResolveLanguage();
    public static event Action? Changed;
    public static string Language => _language;
    public static string Get(string key) => Catalog.TryGetValue(key, out var value)
        ? (_language == "zh-CN" ? value.Chinese : value.English) : key;
    public static string Text(string english)
    {
        if (_language != "zh-CN") return english;
        foreach (var pair in Catalog.Values) if (pair.English == english) return pair.Chinese;
        return english;
    }
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
    private static long _refreshAt;
    public static void Refresh()
    {
        var now = Environment.TickCount64;
        if (now < Interlocked.Read(ref _refreshAt)) return;
        Interlocked.Exchange(ref _refreshAt, now + 2000);
        Reload();
    }
    public static void Reload()
    {
        var language = ResolveLanguage(_language);
        if (_language == language) return;
        _language = language;
        Changed?.Invoke();
    }
    private static string ResolveLanguage(string fallback = "auto")
    {
        string? configured;
        try { configured = Environment.GetEnvironmentVariable("OMNILYRICS_LANGUAGE") ?? UserConfiguration.LoadLanguage(); }
        catch { return fallback == "auto" ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en") : fallback; }
        if (configured == "zh-CN" || configured == "en") return configured;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en";
    }
}
