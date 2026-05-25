using System.IO;
using SubtitleMatcher.Helpers;
using SubtitleMatcher.Models;

namespace SubtitleMatcher.Services;

/// <summary>
/// 视频-字幕匹配服务。
/// 使用评分系统（100+ 分制）进行智能匹配，支持季数/剧名/目录上下文/同集多剧歧义检测。
/// </summary>
public class MatchingService
{
    /// <summary>
    /// 执行自动匹配，返回按 (剧名 → 季数排序 → 集数) 排序的结果。
    /// </summary>
    public List<MatchItem> Match(
        List<MediaFileInfo> videos,
        List<MediaFileInfo> subtitles,
        string videoRootPath,
        string subtitleRootPath)
    {
        var matched = MatchInternal(videos, subtitles);

        var items = new List<MatchItem>(videos.Count);
        foreach (var video in videos)
        {
            var item = new MatchItem
            {
                VideoFile = GetDisplayPath(videoRootPath, video.FilePath),
                VideoEpisode = video.EpisodeLabel,
                VideoPath = video.FilePath,
                VideoSeriesKey = video.SeriesKey,
                VideoSeason = video.Season,
                VideoEpisodeNumber = video.Episode,
                FolderSortOrder = video.FolderSortOrder,
                AllSubtitles = subtitles.Select(s => s.DisplayName).ToList().AsReadOnly(),
                SubtitleRootPath = subtitleRootPath,
            };

            if (matched.TryGetValue(video.FilePath, out var sub))
            {
                item.SubtitleFile = sub.DisplayName;
                item.SubtitleEpisode = sub.EpisodeLabel;
                item.SubtitlePath = sub.FilePath;
            }

            items.Add(item);
        }

        // 排序
        items.Sort((a, b) =>
        {
            int cmp = (string.IsNullOrEmpty(a.VideoSeriesKey) ? 1 : 0)
                .CompareTo(string.IsNullOrEmpty(b.VideoSeriesKey) ? 1 : 0);
            if (cmp != 0) return cmp;

            cmp = string.Compare(a.VideoSeriesKey, b.VideoSeriesKey, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;

            var sA = a.VideoSeason ?? a.FolderSortOrder ?? 0;
            var sB = b.VideoSeason ?? b.FolderSortOrder ?? 0;
            cmp = sA.CompareTo(sB);
            if (cmp != 0) return cmp;

            cmp = (a.VideoEpisodeNumber.HasValue ? 0 : 1).CompareTo(b.VideoEpisodeNumber.HasValue ? 0 : 1);
            if (cmp != 0) return cmp;

            return (a.VideoEpisodeNumber ?? 0).CompareTo(b.VideoEpisodeNumber ?? 0);
        });

        for (int i = 0; i < items.Count; i++)
            items[i].RowNumber = i + 1;

        return items;
    }

    /// <summary>
    /// 仅返回匹配逻辑结果（不包装成 MatchItem），供外部批量调用。
    /// </summary>
    private static Dictionary<string, MediaFileInfo> MatchInternal(
        List<MediaFileInfo> videos, List<MediaFileInfo> subtitles)
    {
        var subtitlesByEp = subtitles
            .Where(s => s.HasEpisode)
            .GroupBy(s => s.Episode!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var videosByEpCount = new Dictionary<int, int>();
        foreach (var v in videos)
        {
            if (v.HasEpisode)
                videosByEpCount[v.Episode!.Value] =
                    videosByEpCount.TryGetValue(v.Episode!.Value, out var c) ? c + 1 : 1;
        }

        var result = new Dictionary<string, MediaFileInfo>(StringComparer.OrdinalIgnoreCase);
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<(MediaFileInfo Video, List<SubScore> Candidates)>();

        foreach (var video in videos)
        {
            if (!video.HasEpisode) continue;
            if (!subtitlesByEp.TryGetValue(video.Episode!.Value, out var epSubs)) continue;

            var candidates = epSubs
                .Select(sub => Score(video, sub, videos))
                .Where(s => s != null)
                .Cast<SubScore>()
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Sub.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count > 0)
                plans.Add((video, candidates));
        }

        // 按最佳分数降序处理，分数相同时有剧名优先
        plans.Sort((a, b) =>
        {
            var cmp = b.Candidates[0].Score.CompareTo(a.Candidates[0].Score);
            if (cmp != 0) return cmp;
            return (string.IsNullOrEmpty(a.Video.SeriesKey) ? 1 : 0)
                .CompareTo(string.IsNullOrEmpty(b.Video.SeriesKey) ? 1 : 0);
        });

        foreach (var (video, candidates) in plans)
        {
            var available = candidates.Where(c => !usedPaths.Contains(c.Sub.FilePath)).ToList();
            if (available.Count == 0) continue;

            var best = available[0];

            // 纯集数匹配时，检查同集是否有多部剧
            if (best.Score < 120)
            {
                if (videosByEpCount.TryGetValue(video.Episode!.Value, out var cnt) && cnt > 1)
                    continue;
            }

            // 同分场景：多部字幕分数相同且剧名不同时不自动匹配
            if (available.Count > 1 && available[1].Score == best.Score)
            {
                var tied = available.TakeWhile(c => c.Score == best.Score)
                    .Select(c => c.Sub.SeriesKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (tied > 1) continue;
            }

            result[video.FilePath] = best.Sub;
            usedPaths.Add(best.Sub.FilePath);
        }

        return result;
    }

    private static SubScore? Score(MediaFileInfo video, MediaFileInfo sub, List<MediaFileInfo> allVideos)
    {
        if (!video.HasEpisode || !sub.HasEpisode || video.Episode != sub.Episode)
            return null;

        // 都有季数但不匹配
        if (video.Season.HasValue && sub.Season.HasValue && video.Season != sub.Season)
            return null;

        // 视频有季数但字幕没有，且该集在不同季中有歧义
        if (video.Season.HasValue && !sub.Season.HasValue && IsSeasonAmbiguous(video, allVideos))
            return null;

        // ── 计分 ──
        var score = 100; // 基础分

        // 季数加分
        if (video.Season.HasValue && sub.Season.HasValue) score += 35;
        else if (video.Season.HasValue || sub.Season.HasValue) score += 10;
        else score += 5;

        // 剧名相关性
        var seriesScore = ScoreSeries(video, sub, allVideos);
        if (seriesScore == null) return null;

        return new SubScore(sub, score + seriesScore.Value);
    }

    private static int? ScoreSeries(MediaFileInfo video, MediaFileInfo sub, List<MediaFileInfo> allVideos)
    {
        // 双方都有文件名剧名 → 精确匹配
        if (video.HasFileSeries && sub.HasFileSeries)
        {
            if (KeyMatch(video.FileSeriesKey, sub.FileSeriesKey) && !IsJustEpisodeKey(video.FileSeriesKey)) return 120;

            // 文件名不匹配时尝试目录关联匹配
            if (video.HasDirectorySeries && KeyContains(sub.FileSeriesKey, video.DirectorySeriesKeys))
                return 85;
            if (sub.HasDirectorySeries && KeyContains(video.FileSeriesKey, sub.DirectorySeriesKeys))
                return 85;
            if (video.HasDirectorySeries && sub.HasDirectorySeries && video.DirectorySeriesKeys.Overlaps(sub.DirectorySeriesKeys))
                return 90;

            // 不同剧名但有翻译/简写：仅当同集无其他剧名冲突才匹配
            var conflict = allVideos
                .Where(v => v.Episode == video.Episode &&
                    (!v.Season.HasValue || !video.Season.HasValue || v.Season == video.Season))
                .Select(v => v.FileSeriesKey)
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1;
            return conflict ? null : 8;
        }

        // 只有视频有文件名剧名 → 匹配字幕的目录剧名
        if (video.HasFileSeries)
        {
            if (sub.HasDirectorySeries && KeyContains(video.FileSeriesKey, sub.DirectorySeriesKeys))
                return 85;
            return IsEpisodeAmbiguous(video, allVideos) ? null : 8;
        }

        // 只有字幕有文件名剧名 → 匹配视频的目录剧名
        if (sub.HasFileSeries)
        {
            if (video.HasDirectorySeries && KeyContains(sub.FileSeriesKey, video.DirectorySeriesKeys))
                return 85;
            return IsEpisodeAmbiguous(video, allVideos) ? null : 8;
        }

        // 双方都有目录剧名
        if (video.HasDirectorySeries && sub.HasDirectorySeries)
        {
            if (video.DirectorySeriesKeys.Overlaps(sub.DirectorySeriesKeys)) return 90;
            return IsEpisodeAmbiguous(video, allVideos) ? null : 8;
        }

        // 单方有剧名
        if (video.HasSeries || sub.HasSeries)
            return IsEpisodeAmbiguous(video, allVideos) ? null : 8;

        // 双方都无剧名
        return 15;
    }

    /// <summary>判断 key 是否仅为集号（如 e01、01），无有意义的剧名信息</summary>
    private static bool IsJustEpisodeKey(string key) =>
        !string.IsNullOrEmpty(key) && System.Text.RegularExpressions.Regex.IsMatch(key, @"^[es]?\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool KeyMatch(string key, HashSet<string> keys) =>
        keys.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static bool KeyMatch(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>软匹配：key 包含于或包含 keys 中的任一值，要求至少匹配3个字符避免误匹配</summary>
    private static bool KeyContains(string key, HashSet<string> keys) =>
        keys.Any(k => (key.StartsWith(k, StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    && k.Length >= 2 && key.Length >= 2);

    /// <summary>同集同季中是否有多个不同剧名的视频</summary>
    private static bool IsEpisodeAmbiguous(MediaFileInfo video, List<MediaFileInfo> allVideos) =>
        allVideos.Count(v =>
            v.Episode == video.Episode &&
            (!v.Season.HasValue || !video.Season.HasValue || v.Season == video.Season) &&
            v.HasFileSeries &&
            !string.Equals(v.FileSeriesKey, video.FileSeriesKey, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>同集中，视频有季数但字幕无季数时，是否有多个视频在不同季</summary>
    private static bool IsSeasonAmbiguous(MediaFileInfo video, List<MediaFileInfo> allVideos) =>
        allVideos.Count(v =>
            v.Episode == video.Episode &&
            v.Season.HasValue &&
            v.Season != video.Season) > 0;

    private static string GetDisplayPath(string rootPath, string filePath)
    {
        try
        {
            var rel = Path.GetRelativePath(rootPath, filePath);
            return rel.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(filePath) : rel;
        }
        catch
        {
            return Path.GetFileName(filePath);
        }
    }

    private sealed record SubScore(MediaFileInfo Sub, int Score);
}
