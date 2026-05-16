using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SubtitleMatcher
{
    public partial class MainWindow : Window
    {
        private readonly List<MatchItem> _matchResults = new();
        private readonly List<(string CreatedPath, string OriginalPath, bool WasMove)> _operations = new();
        private readonly List<MediaFileInfo> _allSubtitleFiles = new();
        private readonly Dictionary<string, MediaFileInfo> _subtitleFilesByDisplayName = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".flv", ".webm"
        };

        private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".ass", ".ssa", ".sub", ".sup", ".txt"
        };

        private static readonly EnumerationOptions RecursiveEnumerationOptions = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
        };

        private const RegexOptions EpisodeRegexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        private static readonly (Regex Regex, bool HasSeason)[] EpisodePatterns =
        {
            (new Regex(@"S(\d{1,2})[\s._-]*E(\d{1,3})", EpisodeRegexOptions), true),
            (new Regex(@"Season\s*(\d{1,2})\s*(?:E(?:p(?:isode)?|pisode)?\.?)\s*(\d{1,3})", EpisodeRegexOptions), true),
            (new Regex(@"E(?:p(?:isode)?|pisode)?\.?\s*(\d{1,3})", EpisodeRegexOptions), false),
            (new Regex(@"[第]?(\d{1,3})[集话]", EpisodeRegexOptions), false),
            (new Regex(@"\[(\d{1,3})\]", EpisodeRegexOptions), false),
            (new Regex(@"-(\d{1,3})[-\.]", EpisodeRegexOptions), false),
            (new Regex(@"_(\d{1,3})[_\.]", EpisodeRegexOptions), false),
            (new Regex(@"\s(\d{1,3})\.", EpisodeRegexOptions), false),
            (new Regex(@"\.(\d{1,3})\.", EpisodeRegexOptions), false),
            (new Regex(@"\.(\d{1,3})[^\d]", EpisodeRegexOptions), false),
            (new Regex(@"^(\d{1,3})[^\d]", EpisodeRegexOptions), false),
            (new Regex(@"[^\d](\d{1,3})$", EpisodeRegexOptions), false),
            (new Regex(@"^(\d{1,3})$", EpisodeRegexOptions), false)
        };

        private static readonly HashSet<string> GenericTitleTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "1080p", "720p", "2160p", "480p", "4k", "8k",
            "x264", "x265", "h264", "h265", "hevc", "avc", "xvid",
            "web", "webdl", "dl", "webrip", "bluray", "bdrip", "dvdrip", "hdtv",
            "hdr", "hdr10", "10bit", "8bit", "aac", "flac", "dts", "ma10p",
            "chs", "cht", "sc", "tc", "gb", "big5", "cn", "zh", "zhs", "zht",
            "jp", "ja", "en", "eng", "chi", "chinese",
            "sub", "subs", "subtitle", "subtitles", "video", "videos",
            "season", "episode", "ep", "srt", "ass", "ssa", "sup", "subrip",
            "v2", "v3", "repack", "proper",
            "nced", "ncop", "pv", "menu", "cm", "special", "trailer", "preview", "sample",
            "字幕", "视频", "简体", "繁体", "简中", "繁中", "中文", "中字",
            "中英", "内封", "外挂", "双语", "国配", "日语", "英语",
            // 常见压制组名标记
            "vcb", "vcbs", "vcb-s", "reinforce", "ai-raws", "ank", "beatrice",
            "mawen1250", "lolihouse", "loli", "snow", "sekkise", "jsum",
            // 常见字幕组标记
            "hkg", "x2", "kamigami", "dmx", "caso",
        };

        private static readonly HashSet<string> GenericDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "字幕", "字幕文件", "subs", "sub", "subtitles", "subtitle",
            "视频", "视频文件", "video", "videos", "media", "download", "downloads"
        };

        private static readonly Regex NonEpisodePattern = new(
            @"(?:^|[\s._\-\[\]\(\)])(NC(?:OP|ED)|PV(?:\s*\d+|ED)?|MENU|CM|SPECIAL|TRAILER|PREVIEW|SAMPLE)[\s._\-\[\]\(\)\d]",
            EpisodeRegexOptions);

        private static readonly ConcurrentDictionary<string, string> NormalizeCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> CleanTitleCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, EpisodeInfo?> EpisodeInfoCache = new(StringComparer.OrdinalIgnoreCase);
        private static bool _isOnlineTranslationEnabled;
        internal static bool IsOnlineTranslationEnabled => _isOnlineTranslationEnabled;

        private sealed class MediaFileInfo
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

        private sealed record EpisodeInfo(int? Season, int Episode, string Label);
        private sealed record SubtitleScore(MediaFileInfo Subtitle, int Score);

        public MainWindow()
        {
            InitializeComponent();
            // 默认在自动匹配 Tab：只挂载该 Grid 的 ItemsSource，切换到手动 Tab 时按需挂载
            AutoMatchDataGrid.ItemsSource = _matchResults;
            AutoTabButton.Style = (Style)Resources["TabButtonSelected"];
            ManualTabButton.Style = (Style)Resources["TabButtonUnselected"];
            CopyButton.Style = (Style)Resources["OperationButtonSelected"];
            MoveButton.Style = (Style)Resources["OperationButtonUnselected"];
            RenameButton.Style = (Style)Resources["OperationButtonUnselected"];
        }

        private void SelectCopy_Click(object sender, RoutedEventArgs e)
        {
            CopyButton.Style = (Style)Resources["OperationButtonSelected"];
            MoveButton.Style = (Style)Resources["OperationButtonUnselected"];
            RenameButton.Style = (Style)Resources["OperationButtonUnselected"];
        }

        private void SelectMove_Click(object sender, RoutedEventArgs e)
        {
            CopyButton.Style = (Style)Resources["OperationButtonUnselected"];
            MoveButton.Style = (Style)Resources["OperationButtonSelected"];
            RenameButton.Style = (Style)Resources["OperationButtonUnselected"];
        }

        private void SelectRename_Click(object sender, RoutedEventArgs e)
        {
            CopyButton.Style = (Style)Resources["OperationButtonUnselected"];
            MoveButton.Style = (Style)Resources["OperationButtonUnselected"];
            RenameButton.Style = (Style)Resources["OperationButtonSelected"];
        }

        private void SwitchToAuto_Click(object sender, RoutedEventArgs e)
        {
            AutoTabButton.Style = (Style)Resources["TabButtonSelected"];
            ManualTabButton.Style = (Style)Resources["TabButtonUnselected"];
            AutoMatchDataGrid.Visibility = Visibility.Visible;
            ManualMatchDataGrid.Visibility = Visibility.Collapsed;
            // 确保该 Grid 拥有最新数据（首次切换或扫描后未在此标签时无 ItemsSource）
            if (AutoMatchDataGrid.ItemsSource != _matchResults)
                AutoMatchDataGrid.ItemsSource = _matchResults;
        }

        private void SwitchToManual_Click(object sender, RoutedEventArgs e)
        {
            AutoTabButton.Style = (Style)Resources["TabButtonUnselected"];
            ManualTabButton.Style = (Style)Resources["TabButtonSelected"];
            AutoMatchDataGrid.Visibility = Visibility.Collapsed;
            ManualMatchDataGrid.Visibility = Visibility.Visible;
            if (ManualMatchDataGrid.ItemsSource != _matchResults)
                ManualMatchDataGrid.ItemsSource = _matchResults;
        }

        private void BrowseVideo_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "选择视频文件夹" };
            if (dialog.ShowDialog() == true)
            {
                VideoPathTextBox.Text = dialog.FolderName;
            }
        }

        private void BrowseSubtitle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "选择字幕文件夹" };
            if (dialog.ShowDialog() == true)
            {
                SubtitlePathTextBox.Text = dialog.FolderName;
            }
        }

        private async void Scan_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string videoPath = VideoPathTextBox.Text.Trim();
            string subtitlePath = SubtitlePathTextBox.Text.Trim();

            if (string.IsNullOrEmpty(videoPath) || string.IsNullOrEmpty(subtitlePath))
            {
                System.Windows.MessageBox.Show("请先选择视频和字幕文件夹", "提示");
                return;
            }

            if (!Directory.Exists(videoPath))
            {
                System.Windows.MessageBox.Show("视频文件夹不存在", "错误");
                return;
            }

            if (!Directory.Exists(subtitlePath))
            {
                System.Windows.MessageBox.Show("字幕文件夹不存在", "错误");
                return;
            }

            ScanButton.IsEnabled = false;
            ScanButton.Content = "⏳ 扫描中...";

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await Task.Run(() => DoScan(videoPath, subtitlePath));

                _allSubtitleFiles.Clear();
                _allSubtitleFiles.AddRange(result.SubtitleFiles);
                _subtitleFilesByDisplayName.Clear();
                foreach (var sub in result.SubtitleFiles)
                {
                    _subtitleFilesByDisplayName.TryAdd(sub.DisplayName, sub);
                }

                _matchResults.Clear();
                _operations.Clear();
                _matchResults.AddRange(result.MatchItems);

                // 重置 ItemsSource 比 Items.Refresh() 更快：避免增量更新检测，直接重建可见行
                if (AutoMatchDataGrid.Visibility == Visibility.Visible)
                {
                    AutoMatchDataGrid.ItemsSource = null;
                    AutoMatchDataGrid.ItemsSource = _matchResults;
                }
                else
                {
                    ManualMatchDataGrid.ItemsSource = null;
                    ManualMatchDataGrid.ItemsSource = _matchResults;
                }

                stopwatch.Stop();
                var matchedCount = result.MatchItems.Count(m => !string.IsNullOrEmpty(m.SubtitlePath));
                var unmatchedCount = result.MatchItems.Count - matchedCount;
                StatusTextBlock.Text = $"✅ {result.VideoCount}视频 / {result.SubtitleCount}字幕 / 已匹配{matchedCount} / 未匹配{unmatchedCount} ({stopwatch.ElapsedMilliseconds}ms)";
                ScanButton.Content = $"✅ {result.VideoCount}视频/{result.SubtitleCount}字幕 ({stopwatch.ElapsedMilliseconds}ms)";
                _ = ResetScanButtonAfterDelayAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"扫描过程中出现错误: {ex.Message}", "错误");
                ScanButton.Content = "🔍 扫描";
                StatusTextBlock.Text = "";
            }
            finally
            {
                ScanButton.IsEnabled = true;
            }
        }

        private async Task ResetScanButtonAfterDelayAsync()
        {
            await Task.Delay(2500);
            ScanButton.Content = "🔍 扫描";
            // 状态栏永久保留，不自动清空
        }

        private sealed record ScanResult(
            List<MatchItem> MatchItems,
            List<MediaFileInfo> SubtitleFiles,
            int VideoCount,
            int SubtitleCount);

        private ScanResult DoScan(string videoPath, string subtitlePath)
        {
            ClearCaches();

            // 视频和字幕目录的扫描互不依赖，并行执行可以大幅缩短在两个不同磁盘/目录时的等待时间
            var videoTask = Task.Run(() => ScanVideoFiles(videoPath));
            var subtitleTask = Task.Run(() => ScanSubtitleFiles(subtitlePath));
            Task.WaitAll(videoTask, subtitleTask);
            var videoFiles = videoTask.Result;
            var subtitleFiles = subtitleTask.Result;

            AssignDisplayNames(subtitleFiles, subtitlePath);

            var subtitleNames = subtitleFiles.Select(s => s.DisplayName).ToList();
            var matchedSubtitles = MatchSubtitles(videoFiles, subtitleFiles);

            var matchItems = new List<MatchItem>(videoFiles.Count);

            foreach (var video in videoFiles)
            {
                var match = new MatchItem
                {
                    VideoFile = GetDisplayPath(videoPath, video.FilePath),
                    VideoEpisode = video.EpisodeLabel,
                    VideoPath = video.FilePath,
                    VideoSeriesKey = video.SeriesKey,
                    VideoSeason = video.Season,
                    VideoEpisodeNumber = video.Episode,
                    FolderSortOrder = video.FolderSortOrder
                };

                match.AllSubtitles = subtitleNames;
                match.SubtitleRootPath = subtitlePath;

                if (matchedSubtitles.TryGetValue(video.FilePath, out var matched))
                {
                    match.SubtitleFile = matched.DisplayName;
                    match.SubtitleEpisode = matched.EpisodeLabel;
                    match.SubtitlePath = matched.FilePath;
                }

                matchItems.Add(match);
            }

            matchItems.Sort((a, b) =>
            {
                var keyA = string.IsNullOrEmpty(a.VideoSeriesKey) ? 1 : 0;
                var keyB = string.IsNullOrEmpty(b.VideoSeriesKey) ? 1 : 0;
                var cmp = keyA.CompareTo(keyB);
                if (cmp != 0) return cmp;

                cmp = string.Compare(a.VideoSeriesKey, b.VideoSeriesKey, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;

                // 排序季数：有显式季数的排前面，否则用文件夹数字前缀，再否则用 0
                var sortSeasonA = a.VideoSeason ?? a.FolderSortOrder ?? 0;
                var sortSeasonB = b.VideoSeason ?? b.FolderSortOrder ?? 0;
                cmp = sortSeasonA.CompareTo(sortSeasonB);
                if (cmp != 0) return cmp;

                // 有集数的排前面，无集数的排后面；同有/同无时按集数升序
                var hasEpA = a.VideoEpisodeNumber.HasValue ? 0 : 1;
                var hasEpB = b.VideoEpisodeNumber.HasValue ? 0 : 1;
                cmp = hasEpA.CompareTo(hasEpB);
                if (cmp != 0) return cmp;

                var epA = a.VideoEpisodeNumber ?? 0;
                var epB = b.VideoEpisodeNumber ?? 0;
                return epA.CompareTo(epB);
            });

            for (int i = 0; i < matchItems.Count; i++)
            {
                matchItems[i].RowNumber = i + 1;
            }

            return new ScanResult(matchItems, subtitleFiles, videoFiles.Count, subtitleFiles.Count);
        }

        private List<MediaFileInfo> ScanVideoFiles(string folderPath)
        {
            return ScanFiles(folderPath, VideoExtensions);
        }

        private List<MediaFileInfo> ScanSubtitleFiles(string folderPath)
        {
            return ScanFiles(folderPath, SubtitleExtensions);
        }

        private static List<MediaFileInfo> ScanFiles(string folderPath, HashSet<string> extensions)
        {
            // 一次遍历整个目录树，按扩展名过滤；之前每个扩展名都遍历一遍整个目录树
            var paths = new List<string>(1024);
            try
            {
                foreach (var file in Directory.EnumerateFiles(folderPath, "*", RecursiveEnumerationOptions))
                {
                    var ext = Path.GetExtension(file);
                    if (!string.IsNullOrEmpty(ext) && extensions.Contains(ext))
                    {
                        paths.Add(file);
                    }
                }
            }
            catch
            {
            }

            // 并行解析文件（CPU 密集型：正则、剧名归一化）
            var parsed = new MediaFileInfo[paths.Count];
            try
            {
                Parallel.For(0, paths.Count, i =>
                {
                    try
                    {
                        parsed[i] = ParseMediaFile(folderPath, paths[i]);
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
                // Parallel.For 整体失败时退回顺序解析
                for (int i = 0; i < paths.Count; i++)
                {
                    try { parsed[i] = ParseMediaFile(folderPath, paths[i]); } catch { }
                }
            }

            var result = new List<MediaFileInfo>(parsed.Length);
            foreach (var item in parsed)
            {
                if (item != null) result.Add(item);
            }

            result.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static void ClearCaches()
        {
            NormalizeCache.Clear();
            CleanTitleCache.Clear();
            EpisodeInfoCache.Clear();
        }

        private static string ExtractEpisode(string fileName)
        {
            return ExtractEpisodeInfo(Path.GetFileNameWithoutExtension(fileName))?.Label ?? string.Empty;
        }

        private static EpisodeInfo? ExtractEpisodeInfo(string fileNameWithoutExtension)
        {
            if (EpisodeInfoCache.TryGetValue(fileNameWithoutExtension, out var cached))
                return cached;

            if (NonEpisodePattern.IsMatch(fileNameWithoutExtension))
            {
                EpisodeInfoCache.TryAdd(fileNameWithoutExtension, null);
                return null;
            }

            foreach (var (regex, hasSeason) in EpisodePatterns)
            {
                var match = regex.Match(fileNameWithoutExtension);
                if (match.Success)
                {
                    EpisodeInfo result;
                    if (hasSeason && match.Groups.Count > 2)
                    {
                        var season = int.Parse(match.Groups[1].Value);
                        var episode = int.Parse(match.Groups[2].Value);
                        result = new EpisodeInfo(season, episode, FormatEpisodeLabel(season, episode));
                    }
                    else
                    {
                        var episodeNumber = int.Parse(match.Groups[1].Value);
                        result = new EpisodeInfo(null, episodeNumber, FormatEpisodeLabel(null, episodeNumber));
                    }
                    EpisodeInfoCache.TryAdd(fileNameWithoutExtension, result);
                    return result;
                }
            }

            EpisodeInfoCache.TryAdd(fileNameWithoutExtension, null);
            return null;
        }

        private static MediaFileInfo ParseMediaFile(string rootPath, string filePath)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var episodeInfo = ExtractEpisodeInfo(fileNameWithoutExtension);
            var season = episodeInfo?.Season ?? ExtractSeasonFromText(fileNameWithoutExtension) ?? ExtractSeasonFromDirectories(rootPath, filePath);
            var folderSortOrder = ExtractFolderSortOrder(rootPath, filePath);
            var episodeLabel = episodeInfo is null
                ? string.Empty
                : FormatEpisodeLabel(season, episodeInfo.Episode);

            var cleanedTitle = CleanTitleFromFileName(fileNameWithoutExtension);
            var fileSeriesKey = NormalizeTitleKey(cleanedTitle);
            var directorySeriesKeys = InferDirectorySeriesKeys(rootPath, filePath);
            var seriesKeys = new List<string>();

            AddSeriesKey(seriesKeys, fileSeriesKey);
            foreach (var directorySeriesKey in directorySeriesKeys)
            {
                AddSeriesKey(seriesKeys, directorySeriesKey);
            }

            var seriesKey = !string.IsNullOrEmpty(fileSeriesKey)
                ? fileSeriesKey
                : directorySeriesKeys.FirstOrDefault() ?? string.Empty;

             return new MediaFileInfo
            {
                FilePath = filePath,
                CleanedTitle = cleanedTitle,
                Season = season,
                Episode = episodeInfo?.Episode,
                EpisodeLabel = episodeLabel,
                SeriesTitle = seriesKey,
                SeriesKey = seriesKey,
                FileSeriesKey = fileSeriesKey,
                DirectorySeriesKeys = new HashSet<string>(directorySeriesKeys, StringComparer.OrdinalIgnoreCase),
                SeriesKeys = new HashSet<string>(seriesKeys, StringComparer.OrdinalIgnoreCase),
                FolderSortOrder = folderSortOrder
            };
        }

        private static Dictionary<string, MediaFileInfo> MatchSubtitles(List<MediaFileInfo> videos, List<MediaFileInfo> subtitles)
        {
            var subtitlesByEpisode = subtitles
                .Where(s => s.HasEpisode)
                .GroupBy(s => s.Episode!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 预计算每集的视频数量，避免在 plans 循环中重复 O(N) 扫描
            var videosByEpisodeCount = new Dictionary<int, int>();
            foreach (var v in videos)
            {
                if (v.HasEpisode)
                {
                    videosByEpisodeCount[v.Episode!.Value] =
                        videosByEpisodeCount.TryGetValue(v.Episode!.Value, out var c) ? c + 1 : 1;
                }
            }

            var result = new Dictionary<string, MediaFileInfo>(StringComparer.OrdinalIgnoreCase);
            var usedSubtitlePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var plans = new List<(MediaFileInfo Video, List<SubtitleScore> Candidates)>();

            foreach (var video in videos)
            {
                if (!video.HasEpisode) continue;

                if (!subtitlesByEpisode.TryGetValue(video.Episode!.Value, out var episodeSubtitles))
                    continue;

                var candidates = episodeSubtitles
                    .Select(subtitle => ScoreSubtitle(video, subtitle, videos))
                    .Where(score => score is not null)
                    .Select(score => score!)
                    .OrderByDescending(score => score.Score)
                    .ThenBy(score => score.Subtitle.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (candidates.Count > 0)
                {
                    plans.Add((video, candidates));
                }
            }

            // 按最佳候选分数降序排序，分数相同时优先处理有精确剧名匹配的视频
            plans.Sort((a, b) =>
            {
                var cmp = b.Candidates[0].Score.CompareTo(a.Candidates[0].Score);
                if (cmp != 0) return cmp;
                // 分数相同时，剧名不为空的排前面（精确匹配优先）
                var aHasKey = !string.IsNullOrEmpty(a.Video.SeriesKey) ? 0 : 1;
                var bHasKey = !string.IsNullOrEmpty(b.Video.SeriesKey) ? 0 : 1;
                return aHasKey.CompareTo(bHasKey);
            });

            foreach (var (video, candidates) in plans)
            {
                var available = candidates
                    .Where(c => !usedSubtitlePaths.Contains(c.Subtitle.FilePath))
                    .ToList();

                if (available.Count == 0) continue;

                var best = available[0];
                var episodeOnly = best.Score < 120;

                if (episodeOnly)
                {
                    if (videosByEpisodeCount.TryGetValue(video.Episode!.Value, out var sameEpCount) && sameEpCount > 1)
                        continue;
                }

                if (available.Count > 1 && available[1].Score == best.Score)
                {
                    var tied = available.TakeWhile(c => c.Score == best.Score)
                        .Select(c => c.Subtitle.SeriesKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    if (tied > 1) continue;
                }

                result[video.FilePath] = best.Subtitle;
                usedSubtitlePaths.Add(best.Subtitle.FilePath);
            }

            return result;
        }

        private static SubtitleScore? ScoreSubtitle(MediaFileInfo video, MediaFileInfo subtitle, List<MediaFileInfo> videos)
        {
            if (!video.HasEpisode || !subtitle.HasEpisode || video.Episode != subtitle.Episode)
            {
                return null;
            }

            if (video.Season.HasValue && subtitle.Season.HasValue && video.Season != subtitle.Season)
            {
                return null;
            }

            if (video.Season.HasValue && !subtitle.Season.HasValue && IsEpisodeAmbiguousAcrossVideoSeasons(video, videos))
            {
                return null;
            }

            var score = 100;

            if (video.Season.HasValue && subtitle.Season.HasValue)
            {
                score += 35;
            }
            else if (video.Season.HasValue || subtitle.Season.HasValue)
            {
                score += 10;
            }
            else
            {
                score += 5;
            }

            var seriesScore = ScoreSeries(video, subtitle, videos);
            if (!seriesScore.HasValue)
            {
                return null;
            }

            return new SubtitleScore(subtitle, score + seriesScore.Value);
        }

        private static int? ScoreSeries(MediaFileInfo video, MediaFileInfo subtitle, List<MediaFileInfo> videos)
        {
            if (video.HasFileSeries && subtitle.HasFileSeries)
            {
                var exactMatchScore = ScoreTitleKeys(ToKeySet(video.FileSeriesKey), ToKeySet(subtitle.FileSeriesKey), 120);
                if (exactMatchScore.HasValue)
                {
                    return exactMatchScore;
                }

                // Try online translation when local key comparison fails
                if (_isOnlineTranslationEnabled)
                {
                    var translatedScore = TryTranslateScore(video, subtitle);
                    if (translatedScore.HasValue)
                    {
                        return translatedScore;
                    }
                }

                // Both have file series but no key overlap (e.g., same show in different languages).
                // Only match by episode if no other video with a different series key shares this episode.
                var hasSeriesConflict = videos
                    .Where(other => other.Episode == video.Episode &&
                                    (!other.Season.HasValue || !video.Season.HasValue || other.Season == video.Season))
                    .Select(other => other.FileSeriesKey)
                    .Where(key => !string.IsNullOrEmpty(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .Count() > 1;
                return hasSeriesConflict ? null : 8;
            }

            if (video.HasFileSeries)
            {
                var directoryScore = subtitle.HasDirectorySeries
                    ? ScoreTitleKeys(ToKeySet(video.FileSeriesKey), subtitle.DirectorySeriesKeys, 85)
                    : null;

                if (directoryScore.HasValue)
                {
                    return directoryScore;
                }

                return IsEpisodeAmbiguousAcrossVideos(video, videos) ? null : 8;
            }

            if (subtitle.HasFileSeries)
            {
                var directoryScore = video.HasDirectorySeries
                    ? ScoreTitleKeys(video.DirectorySeriesKeys, ToKeySet(subtitle.FileSeriesKey), 85)
                    : null;

                if (directoryScore.HasValue)
                {
                    return directoryScore;
                }

                return IsEpisodeAmbiguousAcrossVideos(video, videos) ? null : 8;
            }

            if (video.HasSeries && !subtitle.HasSeries)
            {
                return IsEpisodeAmbiguousAcrossVideos(video, videos) ? null : 8;
            }

            if (!video.HasSeries && subtitle.HasSeries)
            {
                return IsEpisodeAmbiguousAcrossVideos(video, videos) ? null : 8;
            }

            if (video.HasDirectorySeries && subtitle.HasDirectorySeries)
            {
                return ScoreTitleKeys(video.DirectorySeriesKeys, subtitle.DirectorySeriesKeys, 90);
            }

            return 15;
        }

        private static int? TryTranslateScore(MediaFileInfo video, MediaFileInfo subtitle)
        {
            var videoCleaned = video.CleanedTitle;
            var subtitleCleaned = subtitle.CleanedTitle;

            if (string.IsNullOrWhiteSpace(videoCleaned) || string.IsNullOrWhiteSpace(subtitleCleaned))
                return null;

            var videoLang = TranslationHelper.DetectLanguage(videoCleaned);
            var subtitleLang = TranslationHelper.DetectLanguage(subtitleCleaned);

            if (videoLang == subtitleLang)
                return null;

            string? translatedVideo = null, translatedSubtitle = null;

            if (videoLang != "en")
            {
                translatedVideo = TranslationHelper.TranslateToEnglish(videoCleaned);
            }

            if (subtitleLang != "en")
            {
                translatedSubtitle = TranslationHelper.TranslateToEnglish(subtitleCleaned);
            }

            var videoKey = NormalizeTitleKey(translatedVideo ?? videoCleaned);
            var subtitleKey = NormalizeTitleKey(translatedSubtitle ?? subtitleCleaned);

            if (string.IsNullOrEmpty(videoKey) || string.IsNullOrEmpty(subtitleKey))
                return null;

            return ScoreTitleKeys(ToKeySet(videoKey), ToKeySet(subtitleKey), 100);
        }

        private static HashSet<string> ToKeySet(string key)
        {
            return string.IsNullOrEmpty(key)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(new[] { key }, StringComparer.OrdinalIgnoreCase);
        }

        private static int? ScoreTitleKeys(HashSet<string> videoKeys, HashSet<string> subtitleKeys, int exactMatchScore)
        {
            if (videoKeys.Count == 0 || subtitleKeys.Count == 0)
            {
                return null;
            }

            if (videoKeys.Overlaps(subtitleKeys))
            {
                return exactMatchScore;
            }

            var overlapScore = GetTokenOverlapScore(videoKeys, subtitleKeys);
            return overlapScore > 0 ? overlapScore : null;
        }

        private static bool IsEpisodeAmbiguousAcrossVideos(MediaFileInfo video, List<MediaFileInfo> videos)
        {
            var sameEpisodeCount = videos
                .Where(other => other.Episode == video.Episode &&
                                (!other.Season.HasValue || !video.Season.HasValue || other.Season == video.Season))
                .Take(2)
                .Count();

            return sameEpisodeCount > 1;
        }

        private static bool IsEpisodeAmbiguousAcrossVideoSeasons(MediaFileInfo video, List<MediaFileInfo> videos)
        {
            var sameEpisodeSeasons = videos
                .Where(other => other.Episode == video.Episode &&
                                (!video.HasSeries || !other.HasSeries || other.SeriesKey == video.SeriesKey))
                .Select(other => other.Season)
                .Where(season => season.HasValue)
                .Distinct()
                .Take(2)
                .Count();

            return sameEpisodeSeasons > 1;
        }

        private static int GetTokenOverlapScore(HashSet<string> videoKeys, HashSet<string> subtitleKeys)
        {
            var bestScore = 0;

            foreach (var videoKey in videoKeys)
            {
                var videoTokens = videoKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (videoTokens.Length == 0)
                {
                    continue;
                }

                foreach (var subtitleKey in subtitleKeys)
                {
                    var subtitleTokens = subtitleKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (subtitleTokens.Length == 0)
                    {
                        continue;
                    }

                    var shared = videoTokens.Intersect(subtitleTokens, StringComparer.OrdinalIgnoreCase).Count();
                    if (shared == 0)
                    {
                        continue;
                    }

                    var ratio = (double)shared / Math.Max(videoTokens.Length, subtitleTokens.Length);
                    if (ratio >= 0.75)
                    {
                        bestScore = Math.Max(bestScore, 70);
                    }
                    else if (ratio >= 0.5)
                    {
                        bestScore = Math.Max(bestScore, 50);
                    }
                    else if (shared >= 2)
                    {
                        bestScore = Math.Max(bestScore, 35);
                    }
                }
            }

            return bestScore;
        }

        private static List<string> InferDirectorySeriesKeys(string rootPath, string filePath)
        {
            var keys = new List<string>();

            foreach (var segment in GetContextSegments(rootPath, filePath).Reverse<string>())
            {
                if (IsSeasonSegment(segment) || IsGenericDirectory(segment) || IsLikelyDiscFolder(segment))
                {
                    continue;
                }

                AddSeriesKey(keys, CleanTitleFromFileName(segment));
            }

            return keys;
        }

        private static void AddSeriesKey(List<string> keys, string rawTitle)
        {
            var key = NormalizeTitleKey(rawTitle);
            if (string.IsNullOrEmpty(key) || keys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            keys.Add(key);
        }

        private static readonly Regex[] CleanTitleRegexes =
        {
            new(@"S\d{1,2}[\s._-]*E\d{1,3}", EpisodeRegexOptions),
            new(@"Season\s*\d{1,2}\s*(?:E(?:p(?:isode)?|pisode)?\.?)?\s*\d{0,3}", EpisodeRegexOptions),
            new(@"E(?:p(?:isode)?|pisode)?\.?\s*\d{1,3}", EpisodeRegexOptions),
            new(@"[第]?\d{1,3}[集话]", EpisodeRegexOptions),
            new(@"第[一二三四五六七八九十百\d]{1,4}季", EpisodeRegexOptions),
            new(@"\bS\d{1,2}\b", EpisodeRegexOptions),
            new(@"\[\d{1,3}\]", EpisodeRegexOptions),
            new(@"\[[^\]]*\]", EpisodeRegexOptions),   // 清除所有括号内元数据（组名/画质/编码等）
            new(@"[-_ .]\d{1,3}([-_ .]|$)", EpisodeRegexOptions),
            new(@"^\d{1,3}([^\d]|$)", EpisodeRegexOptions),
            new(@"[^\d]\d{1,3}$", EpisodeRegexOptions),
            new(@"\.[A-Za-z][A-Za-z0-9&_-]*$", EpisodeRegexOptions),  // 清除尾部后缀标签（.HKG&X2-sc 等）
            new(@"^(\d{1,3})$", EpisodeRegexOptions)
        };

        private static readonly Regex NormalizeBracketsRegex = new(@"[\[\]【】()（）{}]", RegexOptions.Compiled);
        private static readonly Regex NormalizeSeparatorsRegex = new(@"[._+\-]+", RegexOptions.Compiled);
        private static readonly Regex NormalizeNonWordRegex = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);
        private static readonly Regex AllDigitsRegex = new(@"^\d+$", RegexOptions.Compiled);

        private static string CleanTitleFromFileName(string text)
        {
            if (CleanTitleCache.TryGetValue(text, out var cached))
                return cached;

            var cleaned = text;
            foreach (var rx in CleanTitleRegexes)
            {
                cleaned = rx.Replace(cleaned, " ");
            }
            CleanTitleCache.TryAdd(text, cleaned);
            return cleaned;
        }

        private static string NormalizeTitleKey(string text)
        {
            if (NormalizeCache.TryGetValue(text, out var cached))
                return cached;

            var normalized = NormalizeBracketsRegex.Replace(text, " ");
            normalized = NormalizeSeparatorsRegex.Replace(normalized, " ");
            normalized = NormalizeNonWordRegex.Replace(normalized, " ");

            var tokens = normalized
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !GenericTitleTokens.Contains(token))
                .Where(token => !AllDigitsRegex.IsMatch(token))
                .ToList();

            var result = string.Join(' ', tokens);
            NormalizeCache.TryAdd(text, result);
            return result;
        }

        private static readonly Regex FallbackSeasonPattern = new(@"^(\d{1,2})(?:[\s._-]|$)", RegexOptions.Compiled);

        /// <summary>从文件夹名提取显式季数关键词（Season、S、季），用于匹配判断</summary>
        private static int? ExtractSeasonFromDirectories(string rootPath, string filePath)
        {
            foreach (var segment in GetContextSegments(rootPath, filePath).Reverse<string>())
            {
                var season = ExtractSeasonFromText(segment);
                if (season.HasValue)
                {
                    return season;
                }
            }

            return null;
        }

        /// <summary>从文件夹名提取数字前缀（如 02-... 中的 02），仅用于排序，不影响匹配</summary>
        private static int? ExtractFolderSortOrder(string rootPath, string filePath)
        {
            foreach (var segment in GetContextSegments(rootPath, filePath).Reverse<string>())
            {
                // 已有显式季数关键词的文件夹，用其实际季数作为排序值
                var explicitSeason = ExtractSeasonFromText(segment);
                if (explicitSeason.HasValue)
                    return explicitSeason;

                // 纯数字前缀文件夹，取数字作为排序值（不限数字大小，因为仅用于排序）
                var fallbackMatch = FallbackSeasonPattern.Match(segment);
                if (fallbackMatch.Success)
                {
                    return int.Parse(fallbackMatch.Groups[1].Value);
                }
            }

            return null;
        }

        private static readonly Regex DiscFolderPattern = new(@"^\d{1,2}[\s._-]", RegexOptions.Compiled);
        private static readonly Regex SeasonKeywordPattern = new(@"(?:S(?:eason)?|第|季)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool IsLikelyDiscFolder(string segment)
        {
            // 匹配以数字开头，后跟分隔符（-、_、空格）的文件夹名，且不包含季数关键词
            // 例如："11-[VCB-Studio]..."、"02 Season"（但 "02 Season" 会被季数关键词拦截，所以这里主要拦截纯数字前缀）
            if (DiscFolderPattern.IsMatch(segment))
            {
                // 如果包含季数关键词，则不是光盘文件夹
                if (SeasonKeywordPattern.IsMatch(segment))
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        private static readonly Regex SeasonWesternRegex = new(@"(?:^|[\s._-])S(?:eason)?\s*(\d{1,2})(?:$|[\s._-])", EpisodeRegexOptions);
        private static readonly Regex SeasonEnglishRegex = new(@"Season\s*(\d{1,2})", EpisodeRegexOptions);
        private static readonly Regex SeasonNumericChineseRegex = new(@"第?\s*(\d{1,2})\s*季", EpisodeRegexOptions);
        private static readonly Regex SeasonChineseCharRegex = new(@"第\s*([一二三四五六七八九十百]{1,4})\s*季", EpisodeRegexOptions);

        private static int? ExtractSeasonFromText(string text)
        {
            var westernMatch = SeasonWesternRegex.Match(text);
            if (westernMatch.Success)
            {
                return int.Parse(westernMatch.Groups[1].Value);
            }

            var englishMatch = SeasonEnglishRegex.Match(text);
            if (englishMatch.Success)
            {
                return int.Parse(englishMatch.Groups[1].Value);
            }

            var numericChineseMatch = SeasonNumericChineseRegex.Match(text);
            if (numericChineseMatch.Success)
            {
                return int.Parse(numericChineseMatch.Groups[1].Value);
            }

            var chineseMatch = SeasonChineseCharRegex.Match(text);
            if (chineseMatch.Success)
            {
                return ParseChineseNumber(chineseMatch.Groups[1].Value);
            }

            return null;
        }

        private static readonly Dictionary<char, int> ChineseDigits = new()
        {
            ['一'] = 1, ['二'] = 2, ['三'] = 3, ['四'] = 4, ['五'] = 5,
            ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9
        };

        private static int? ParseChineseNumber(string text)
        {
            if (text == "十")
            {
                return 10;
            }

            if (text.Contains('十'))
            {
                var parts = text.Split('十');
                var tens = string.IsNullOrEmpty(parts[0]) ? 1 : ChineseDigits.GetValueOrDefault(parts[0][0]);
                var ones = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? ChineseDigits.GetValueOrDefault(parts[1][0]) : 0;
                return tens * 10 + ones;
            }

            return text.Length == 1 && ChineseDigits.TryGetValue(text[0], out var value) ? value : null;
        }

        private static bool IsSeasonSegment(string segment)
        {
            return ExtractSeasonFromText(segment).HasValue;
        }

        private static bool IsGenericDirectory(string segment)
        {
            var key = NormalizeTitleKey(segment);
            return string.IsNullOrEmpty(key) || GenericDirectoryNames.Contains(segment) || GenericDirectoryNames.Contains(key);
        }

        private static IEnumerable<string> GetContextSegments(string rootPath, string filePath)
        {
            var segments = new List<string>();

            try
            {
                var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var rootName = Path.GetFileName(root);
                if (!string.IsNullOrEmpty(rootName))
                {
                    segments.Add(rootName);
                }

                var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                if (string.IsNullOrEmpty(directory))
                {
                    return segments;
                }

                var relativeDirectory = Path.GetRelativePath(root, directory);
                if (relativeDirectory == "." || relativeDirectory.StartsWith("..", StringComparison.Ordinal))
                {
                    return segments;
                }

                segments.AddRange(relativeDirectory.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries));
            }
            catch
            {
            }

            return segments;
        }

        private static string FormatEpisodeLabel(int? season, int episode)
        {
            return season.HasValue
                ? $"S{season.Value:00}E{episode:00}"
                : $"E{episode:00}";
        }

        private static void AssignDisplayNames(List<MediaFileInfo> files, string rootPath)
        {
            var duplicateNames = files
                .GroupBy(file => Path.GetFileName(file.FilePath), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file.FilePath);
                var displayName = duplicateNames.Contains(fileName) ? GetDisplayPath(rootPath, file.FilePath) : fileName;
                var uniqueDisplayName = displayName;
                var suffix = 2;

                while (!usedNames.Add(uniqueDisplayName))
                {
                    uniqueDisplayName = $"{displayName} ({suffix})";
                    suffix++;
                }

                file.DisplayName = uniqueDisplayName;
            }
        }

        private static string GetDisplayPath(string rootPath, string filePath)
        {
            try
            {
                var relativePath = Path.GetRelativePath(rootPath, filePath);
                return relativePath.StartsWith("..", StringComparison.Ordinal)
                    ? Path.GetFileName(filePath)
                    : relativePath;
            }
            catch
            {
                return Path.GetFileName(filePath);
            }
        }

        private void Match_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var selectedItems = _matchResults.Where(m => m.IsSelected && !string.IsNullOrEmpty(m.SubtitlePath)).ToList();

            if (selectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("没有选中的匹配项", "提示");
                return;
            }

            _operations.Clear();
            int successCount = 0;
            bool isCopy = CopyButton.Style == (Style)Resources["OperationButtonSelected"];
            bool isMove = MoveButton.Style == (Style)Resources["OperationButtonSelected"];

            foreach (var item in selectedItems)
            {
                try
                {
                    string videoDir = Path.GetDirectoryName(item.VideoPath)!;
                    string subtitleExt = Path.GetExtension(item.SubtitlePath);
                    string newSubtitlePath;

                    if (isCopy)
                    {
                        newSubtitlePath = Path.Combine(videoDir, Path.GetFileName(item.SubtitlePath));
                        File.Copy(item.SubtitlePath, newSubtitlePath, true);
                    }
                    else if (isMove)
                    {
                        newSubtitlePath = Path.Combine(videoDir, Path.GetFileName(item.SubtitlePath));
                        File.Move(item.SubtitlePath, newSubtitlePath, true);
                    }
                    else
                    {
                        string videoName = Path.GetFileNameWithoutExtension(item.VideoPath);
                        newSubtitlePath = Path.Combine(videoDir, videoName + subtitleExt);
                        File.Copy(item.SubtitlePath, newSubtitlePath, true);
                    }

                    _operations.Add((newSubtitlePath, item.SubtitlePath, isMove));
                    successCount++;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"处理文件时出错: {item.VideoFile}\n{ex.Message}", "错误");
                }
            }

            System.Windows.MessageBox.Show($"匹配完成！成功处理 {successCount} 个文件", "完成");
        }

        private void Undo_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!_operations.Any())
            {
                System.Windows.MessageBox.Show("没有可撤销的操作", "提示");
                return;
            }

            var failedOperations = new List<(string CreatedPath, string OriginalPath, bool WasMove)>();

            foreach (var op in _operations)
            {
                try
                {
                    if (!File.Exists(op.CreatedPath))
                    {
                        continue;
                    }

                    if (op.WasMove)
                    {
                        File.Move(op.CreatedPath, op.OriginalPath, true);
                    }
                    else
                    {
                        File.Delete(op.CreatedPath);
                    }
                }
                catch
                {
                    failedOperations.Add(op);
                }
            }

            _operations.Clear();
            _operations.AddRange(failedOperations);

            var message = failedOperations.Count == 0
                ? "撤销完成"
                : $"撤销完成，但有 {failedOperations.Count} 个文件处理失败";
            System.Windows.MessageBox.Show(message, "提示");
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var helpWindow = new HelpWindow
                {
                    Owner = this
                };
                helpWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开帮助文档：{ex.Message}", "错误");
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var aboutWindow = new AboutWindow
                {
                    Owner = this
                };
                aboutWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开许可信息：{ex.Message}", "错误");
            }
        }

        private void ToggleOnlineTranslation_Click(object sender, RoutedEventArgs e)
        {
            _isOnlineTranslationEnabled = !_isOnlineTranslationEnabled;
            TranslateToggleButton.Content = _isOnlineTranslationEnabled ? "🌐 翻译开" : "🌐 翻译关";
            TranslateToggleButton.Background = _isOnlineTranslationEnabled
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
        }

        private void TranslateConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configWindow = new TranslateConfigWindow
                {
                    Owner = this
                };
                configWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开翻译配置窗口：{ex.Message}", "错误");
            }
        }

        private void SubtitleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.DataContext is MatchItem item)
            {
                UpdateSubtitleForItem(item);
            }
        }

        private void ManualSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MatchItem item)
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择字幕文件",
                    Filter = "字幕文件|*.srt;*.ass;*.ssa;*.sub;*.sup;*.txt|所有文件|*.*",
                    InitialDirectory = SubtitlePathTextBox.Text
                };

                if (dialog.ShowDialog() == true)
                {
                    var subInfo = _allSubtitleFiles.FirstOrDefault(s => s.FilePath == dialog.FileName);
                    var parsedSubtitle = subInfo ?? ParseMediaFile(
                        Directory.Exists(SubtitlePathTextBox.Text) ? SubtitlePathTextBox.Text : Path.GetDirectoryName(dialog.FileName)!,
                        dialog.FileName);

                    item.SubtitleFile = !string.IsNullOrEmpty(subInfo?.DisplayName) ? subInfo.DisplayName : Path.GetFileName(dialog.FileName);
                    item.SubtitlePath = dialog.FileName;
                    item.SubtitleEpisode = parsedSubtitle.EpisodeLabel;
                    item.IsSelected = true;
                    // MatchItem 实现 INotifyPropertyChanged，DataGrid 会自动更新，无需 Refresh
                }
            }
        }

        private void UpdateSubtitleForItem(MatchItem item)
        {
            if (!string.IsNullOrEmpty(item.SubtitleFile))
            {
                if (_subtitleFilesByDisplayName.TryGetValue(item.SubtitleFile, out var subInfo))
                {
                    item.SubtitlePath = subInfo.FilePath;
                    item.SubtitleEpisode = subInfo.EpisodeLabel;
                    item.IsSelected = true;
                }
            }
        }
    }
}
