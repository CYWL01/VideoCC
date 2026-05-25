using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using SubtitleMatcher.Models;

namespace SubtitleMatcher.Helpers;

public partial class FileNameCleaner
{
    // ── 常量 ──────────────────────────────────────────────
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".flv", ".webm" };
    private static readonly HashSet<string> SubExts = new(StringComparer.OrdinalIgnoreCase)
        { ".srt", ".ass", ".ssa", ".sub", ".sup", ".txt" };
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "字幕", "subtitle", "subtitles", "subs", "sub", "video", "videos", "downloads", "download", "media" };
    private static readonly HashSet<string> NonEpisodeTags = new(StringComparer.OrdinalIgnoreCase)
        { "nced", "ncop", "pv", "pved", "menu", "cm", "special", "trailer", "preview", "sample",
          "op", "ed", "opening", "ending", "nc", "creditless",
          "interview", "making", "behind", "teaser", "recap",
          "extra", "bonus", "ova", "oad", "sp" };

    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "1080p","720p","2160p","480p","4k","8k",
        "x264","x265","h264","h265","hevc","avc","xvid",
        "web","webdl","dl","webrip","bluray","bdrip","dvdrip","hdtv",
        "hdr","hdr10","10bit","8bit","aac","flac","dts","ma10p",
        "chs","cht","sc","tc","zh","chs","cht","jp","en","eng",
        "sub","subs","subtitle","subtitles",
        "v2","v3","repack","proper",
        "vcb","reinforce","ai-raws","ank","lolihouse","loli","snow","jsum",
        "hkg","x2","kamigami","dmx","caso",
        "完","完结","连载","续",
    };

    // ── 正则模式 ──────────────────────────────────────────
    private const RegexOptions Opt = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly Regex BracketRegex = BracketPattern();
    private static readonly Regex TrailingSuffixRegex = TrailingSuffixPattern();
    private static readonly Regex NonEpisodeRegex = NonEpisodePattern();

    private static readonly (Regex Regex, bool HasSeason)[] EpisodePatterns =
    {
        (new Regex(@"S(\d{1,2})[\s._-]*E(\d{1,3})", Opt), true),
        (new Regex(@"Season\s*(\d{1,2})\s*E(?:p(?:isode)?)?\.?\s*(\d{1,3})", Opt), true),
        (new Regex(@"E(?:p(?:isode)?)?\.?\s*(\d{1,3})", Opt), false),
        (new Regex(@"[第]?(\d{1,3})[集话]", Opt), false),
        (new Regex(@"\[(\d{1,3})\]", Opt), false),
        (new Regex(@"-(\d{1,3})[-\.\s]", Opt), false),
        (new Regex(@"_(\d{1,3})[_\.]", Opt), false),
        (new Regex(@"\s(\d{1,3})\.", Opt), false),
        (new Regex(@"\.(\d{1,3})\.", Opt), false),
        (new Regex(@"\.(\d{1,3})(?:[^\d]|$)", Opt), false),
        (new Regex(@"^(\d{1,3})[^\d]", Opt), false),
        (new Regex(@"[^\d](\d{1,3})$", Opt), false),
        (new Regex(@"^(\d{1,3})$", Opt), false),
    };

    private static readonly ChineseNumberParser _chineseParser = new();
    private static readonly ConcurrentDictionary<string, EpisodeInfo?> _episodeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> _titleCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> _keyCache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsVideoFile(string path) => VideoExts.Contains(Path.GetExtension(path));
    public bool IsSubtitleFile(string path) => SubExts.Contains(Path.GetExtension(path));
    public bool IsSupportedFile(string path) => VideoExts.Contains(Path.GetExtension(path)) || SubExts.Contains(Path.GetExtension(path));
    public static IEnumerable<string> SupportedVideoExts => VideoExts;
    public static IEnumerable<string> SupportedSubExts => SubExts;

    // ── 公共解析入口 ───────────────────────────────────────
    public MediaFileInfo Parse(string rootPath, string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var epInfo = ExtractEpisode(name);
        var season = epInfo?.Season ?? ExtractSeasonFromText(name) ?? ExtractSeasonFromDirs(rootPath, filePath);
        var folderSort = ExtractFolderSort(rootPath, filePath);
        var epLabel = epInfo is null ? "" : FormatLabel(season, epInfo.Episode);

        var cleanedTitle = CleanTitle(name);
        var fileKey = NormalizeKey(cleanedTitle);
        var dirKeys = InferDirKeys(rootPath, filePath);
        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(fileKey)) allKeys.Add(fileKey);
        foreach (var k in dirKeys) allKeys.Add(k);

        var seriesKey = !string.IsNullOrEmpty(fileKey) ? fileKey : dirKeys.FirstOrDefault() ?? "";

        return new MediaFileInfo
        {
            FilePath = filePath,
            CleanedTitle = cleanedTitle,
            Season = season,
            Episode = epInfo?.Episode,
            EpisodeLabel = epLabel,
            SeriesTitle = seriesKey,
            SeriesKey = seriesKey,
            FileSeriesKey = fileKey,
            DirectorySeriesKeys = new HashSet<string>(dirKeys, StringComparer.OrdinalIgnoreCase),
            SeriesKeys = allKeys,
            FolderSortOrder = folderSort,
        };
    }

    public void ClearCaches()
    {
        _episodeCache.Clear();
        _titleCache.Clear();
        _keyCache.Clear();
    }

    // ── 剧名清理 ───────────────────────────────────────────
    private string CleanTitle(string name)
    {
        return _titleCache.GetOrAdd(name, n =>
        {
            var t = n;
            t = BracketRegex.Replace(t, " ");
            t = TrailingSuffixRegex.Replace(t, " ");
            // 移除通用技术标记
            foreach (var token in GenericTokens)
                t = Regex.Replace(t, $@"\b{Regex.Escape(token)}\b", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t;
        });
    }

    public static string NormalizeKey(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        return _keyCache.GetOrAdd(title, t =>
            Regex.Replace(t.ToLowerInvariant(), @"[\s\-_\.]+", ""));
    }

    // ── 集数提取 ───────────────────────────────────────────
    private static EpisodeInfo? ExtractEpisode(string name)
    {
        return _episodeCache.GetOrAdd(name, n =>
        {
            // 先排除非剧集（正则 + 标签集合双重检查）
            if (NonEpisodeRegex.IsMatch(n)) return null;
            if (NonEpisodeTags.Any(t => n.Contains(t, StringComparison.OrdinalIgnoreCase))) return null;

            // 先尝试中文
            var zhSeason = TryExtractChineseSeason(n);
            var zhEpisode = TryExtractChineseEpisode(n);
            if (zhEpisode.HasValue)
            {
                return new EpisodeInfo(zhSeason, zhEpisode.Value,
                    FormatLabel(zhSeason, zhEpisode.Value));
            }

            foreach (var (regex, hasSeason) in EpisodePatterns)
            {
                var m = regex.Match(n);
                if (!m.Success) continue;

                if (hasSeason && m.Groups.Count > 2)
                {
                    var s = int.Parse(m.Groups[1].Value);
                    var e = int.Parse(m.Groups[2].Value);
                    return new EpisodeInfo(s, e, FormatLabel(s, e));
                }

                var ep = int.Parse(m.Groups[1].Value);
                return new EpisodeInfo(null, ep, FormatLabel(null, ep));
            }

            return null;
        });
    }

    private static int? TryExtractChineseSeason(string name)
    {
        var m = Regex.Match(name, @"第(.+?)季");
        if (m.Success) return _chineseParser.Parse(m.Groups[1].Value);
        return null;
    }

    private static int? TryExtractChineseEpisode(string name)
    {
        var m = Regex.Match(name, @"第(.+?)(?:话|集)");
        if (m.Success)
        {
            var ep = _chineseParser.Parse(m.Groups[1].Value);
            return ep > 0 ? ep : null;
        }
        return null;
    }

    private static int? ExtractSeasonFromText(string name)
    {
        var m = Regex.Match(name, @"[Ss](\d{1,2})");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var s)) return s;
        return null;
    }

    private static int? ExtractSeasonFromDirs(string rootPath, string filePath)
    {
        var rel = Path.GetRelativePath(rootPath, filePath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            var m = Regex.Match(part, @"[Ss](?:eason)?[\s._-]*(\d{1,2})", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var s)) return s;

            var cn = Regex.Match(part, @"第(.+?)季");
            if (cn.Success)
            {
                var v = _chineseParser.Parse(cn.Groups[1].Value);
                if (v > 0) return v;
            }
        }
        return null;
    }

    private static int? ExtractFolderSort(string rootPath, string filePath)
    {
        var rel = Path.GetRelativePath(rootPath, filePath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length > 1)
        {
            var dir = parts[^2];
            var m = Regex.Match(dir, @"^(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;
        }
        return null;
    }

    // ── 目录推断剧名 ───────────────────────────────────────
    private List<string> InferDirKeys(string rootPath, string filePath)
    {
        var keys = new List<string>();
        var rel = Path.GetRelativePath(rootPath, filePath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            if (SkipDirs.Contains(part)) continue;
            if (Regex.IsMatch(part, @"^[Ss](?:eason)?[\s._-]*\d", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(part, @"^第.+季$")) continue;
            if (Regex.IsMatch(part, @"^\d+[-_]")) continue;
            if (Regex.IsMatch(part, @"^\d+$")) continue;
            if (Regex.IsMatch(part, @"^[Ee]\d+", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(part, @"^[Ss]\d+[Ee]\d+$", RegexOptions.IgnoreCase)) continue;

            var cleaned = CleanTitle(part);
            var key = NormalizeKey(cleaned);
            if (!string.IsNullOrEmpty(key))
                keys.Add(key);
        }

        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string FormatLabel(int? season, int episode)
    {
        return season.HasValue ? $"S{season.Value:D2}E{episode:D2}" : $"E{episode:D2}";
    }

    // ── 正则生成器 ─────────────────────────────────────────
    [GeneratedRegex(@"\[.*?\]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketPattern();
    [GeneratedRegex(@"\.([A-Za-z0-9&\-_]+(?:-sc|完|完结|连载|续))$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingSuffixPattern();
    [GeneratedRegex(@"(?:^|[\s._\-\[\]\(\)])(NC(?:OP|ED)|PV(?:\s*\d+|ED)?|MENU|CM|SPECIAL|TRAILER|PREVIEW|SAMPLE)[\s._\-\[\]\(\)\d]?", RegexOptions.IgnoreCase)]
    private static partial Regex NonEpisodePattern();
}
