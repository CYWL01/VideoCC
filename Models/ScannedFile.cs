namespace SubtitleMatcher.Models;

public enum FileType { Unknown, Video, Subtitle }

public class ScannedFile
{
    public string FullPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
    public FileType Type { get; set; } = FileType.Unknown;
}

public class DramaInfo
{
    public string OriginalName { get; set; } = string.Empty;
    public string CleanedName { get; set; } = string.Empty;
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string EpisodeLabel { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string FileSeriesKey { get; set; } = string.Empty;
    public HashSet<string> DirectorySeriesKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SeriesKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int? FolderSortOrder { get; set; }

    public bool HasSeries => !string.IsNullOrEmpty(FileSeriesKey) || DirectorySeriesKeys.Count > 0;
}
