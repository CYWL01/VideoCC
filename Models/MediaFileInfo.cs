using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace SubtitleMatcher.Models;

/// <summary>
/// 媒体文件信息，包含文件名解析结果、剧名归一化、季/集信息。
/// </summary>
public class MediaFileInfo
{
    public required string FilePath { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public string CleanedTitle { get; init; } = string.Empty;
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string EpisodeLabel { get; init; } = string.Empty;
    public string SeriesTitle { get; init; } = string.Empty;
    public string SeriesKey { get; init; } = string.Empty;
    public string FileSeriesKey { get; init; } = string.Empty;
    public HashSet<string> DirectorySeriesKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SeriesKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int? FolderSortOrder { get; init; }

    public bool HasEpisode => Episode.HasValue;
    public bool HasFileSeries => !string.IsNullOrEmpty(FileSeriesKey);
    public bool HasDirectorySeries => DirectorySeriesKeys.Count > 0;
    public bool HasSeries => HasFileSeries || HasDirectorySeries;
}

/// <summary>
/// 集数信息
/// </summary>
public sealed record EpisodeInfo(int? Season, int Episode, string Label);
