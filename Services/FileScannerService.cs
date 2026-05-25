using System.Collections.Concurrent;
using System.IO;
using SubtitleMatcher.Helpers;
using SubtitleMatcher.Models;

namespace SubtitleMatcher.Services;

public class FileScannerService
{
    private readonly FileNameCleaner _cleaner = new();
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
    };

    /// <summary>
    /// 并行扫描视频和字幕目录，返回解析后的文件列表。
    /// </summary>
    public (List<MediaFileInfo> Videos, List<MediaFileInfo> Subtitles) Scan(string videoPath, string subtitlePath)
    {
        _cleaner.ClearCaches();

        var videoTask = Task.Run(() => ScanDir(videoPath, _cleaner.IsVideoFile));
        var subtitleTask = Task.Run(() => ScanDir(subtitlePath, _cleaner.IsSubtitleFile));
        Task.WaitAll(videoTask, subtitleTask);

        var videos = videoTask.Result;
        var subtitles = subtitleTask.Result;

        // 为字幕分配显示名
        AssignDisplayNames(subtitles, subtitlePath);

        return (videos, subtitles);
    }

    private List<MediaFileInfo> ScanDir(string folderPath, Func<string, bool> filter)
    {
        if (!Directory.Exists(folderPath))
            return new List<MediaFileInfo>();

        var paths = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath, "*", Recursive))
            {
                var ext = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(ext) && filter(file))
                    paths.Add(file);
            }
        }
        catch { }

        var results = new MediaFileInfo[paths.Count];
        try
        {
            Parallel.For(0, paths.Count, i =>
            {
                try { results[i] = _cleaner.Parse(folderPath, paths[i]); }
                catch { }
            });
        }
        catch
        {
            for (int i = 0; i < paths.Count; i++)
            {
                try { results[i] = _cleaner.Parse(folderPath, paths[i]); }
                catch { }
            }
        }

        return results.Where(r => r != null)
            .OrderBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 为字幕文件分配显示名（优先显示相对于根目录的路径）。
    /// </summary>
    private static void AssignDisplayNames(List<MediaFileInfo> files, string rootPath)
    {
        var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var name = Path.GetFileName(f.FilePath);
            nameCount.TryGetValue(name, out var c);
            nameCount[name] = c + 1;
        }

        foreach (var f in files)
        {
            var name = Path.GetFileName(f.FilePath);
            if (nameCount[name] > 1)
            {
                try
                {
                    var rel = Path.GetRelativePath(rootPath, f.FilePath);
                    f.DisplayName = rel.StartsWith("..", StringComparison.Ordinal) ? name : rel;
                }
                catch { f.DisplayName = name; }
            }
            else
            {
                f.DisplayName = name;
            }
        }
    }
}
