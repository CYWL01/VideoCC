using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SubtitleMatcher.Helpers;
using SubtitleMatcher.Infrastructure;
using SubtitleMatcher.Models;
using SubtitleMatcher.Services;

namespace SubtitleMatcher;

public partial class MainWindow : Window
{
    private readonly FileScannerService _scanner = new();
    private readonly MatchingService _matcher = new();
    private readonly AiMatchingService _aiMatcher = new();
    private readonly OperationHistoryService _history = new();
    private readonly ConfigurationManager _config = new();
    private readonly ObservableCollection<MatchItem> _items = new();

    private List<MediaFileInfo> _allSubtitles = new();
    private bool _aiEnabled;

    // （拖拽功能已移除）

    public MainWindow()
    {
        InitializeComponent();

        // 加载 AI 配置 + 主题
        var aiConfig = _config.LoadAiConfig();
        _aiMatcher.UpdateConfig(aiConfig);
        _aiEnabled = aiConfig.Enabled;
        UpdateAiButton();

        // 初始化 DataGrid 列
        SetupDataGrid();
        MatchGrid.ItemsSource = _items;

        // 事件绑定
        BrowseVideoBtn.Click += (_, _) => BrowseFolder(VideoPathBox);
        BrowseSubBtn.Click += (_, _) => BrowseFolder(SubtitlePathBox);
        ScanBtn.Click += (_, _) => _ = ScanAsync();
        ExecuteBtn.Click += (_, _) => Execute();
        UndoBtn.Click += (_, _) => Undo();
        HelpBtn.Click += (_, _) => { new HelpWindow { Owner = this }.ShowDialog(); };
        AiRulesBtn.Click += (_, _) =>
        {
            var win = new AiRulesWindow(_config) { Owner = this };
            win.ShowDialog();
            if (win.PromptsChanged)
            {
                var cfg = _config.LoadAiConfig();
                _aiMatcher.UpdateConfig(cfg);
            }
        };
        AboutBtn.Click += (_, _) => { new AboutWindow { Owner = this }.ShowDialog(); };
        ConfigBtn.Click += (_, _) => OpenConfig();

        AiToggleBtn.Click += (_, _) => ToggleAi();

        CopyModeBtn.Click += (_, _) => SetOperationMode("Copy");
        MoveModeBtn.Click += (_, _) => SetOperationMode("Move");
        RenameModeBtn.Click += (_, _) => SetOperationMode("Rename");

        AutoTabBtn.Click += (_, _) => SwitchTab(true);
        ManualTabBtn.Click += (_, _) => SwitchTab(false);

        // 清空按钮
        ClearVideoBtn.Click += (_, _) => VideoPathBox.Text = "";
        ClearSubBtn.Click += (_, _) => SubtitlePathBox.Text = "";
        ClearAllBtn.Click += (_, _) => { VideoPathBox.Text = ""; SubtitlePathBox.Text = ""; };
    }

    // ── 初始化 DataGrid 列 ─────────────────────────────────
    private void SetupDataGrid()
    {
        // 通用截断+提示+居中样式
        Style TrimStyle(string bind) => new(typeof(TextBlock))
        {
            Setters = {
                new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis),
                new Setter(TextBlock.ToolTipProperty, new Binding(bind)),
                new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center)
            }
        };

        // 集数列居中+间距样式
        Style EpStyle() => new(typeof(TextBlock))
        {
            Setters = {
                new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center),
                new Setter(TextBlock.MarginProperty, new Thickness(6, 0, 6, 0))
            }
        };

        DataGridColumn[] cols = [
            new DataGridTextColumn { Header = "#", Binding = new Binding("RowNumber"), Width = 28 },
            new DataGridTextColumn
            {
                Header = "📹 视频文件", Binding = new Binding("VideoFile"),
                Width = new DataGridLength(5, DataGridLengthUnitType.Star),
                ElementStyle = TrimStyle("VideoFile")
            },
            new DataGridTextColumn { Header = "集数", Binding = new Binding("VideoEpisode"), Width = DataGridLength.Auto, ElementStyle = EpStyle() },
            new DataGridTextColumn
            {
                Header = "📜 匹配字幕", Binding = new Binding("SubtitleDisplay"),
                Width = new DataGridLength(5, DataGridLengthUnitType.Star),
                ElementStyle = TrimStyle("SubtitleDisplay")
            },
            new DataGridTextColumn { Header = "集数", Binding = new Binding("SubtitleEpisode"), Width = DataGridLength.Auto, ElementStyle = EpStyle() },
            new DataGridTextColumn { Header = "方式", Binding = new Binding("MatchMethod"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 60, ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center) } } },
            new DataGridCheckBoxColumn { Header = "✓", Binding = new Binding("IsSelected") { Mode = BindingMode.TwoWay }, Width = 28 },
        ];

        foreach (var col in cols)
            MatchGrid.Columns.Add(col);
    }

    // ── 文件夹选择 ─────────────────────────────────────────
    private static void BrowseFolder(TextBox target)
    {
        var dialog = new OpenFolderDialog { Title = "选择文件夹" };
        if (dialog.ShowDialog() == true)
            target.Text = dialog.FolderName;
    }

    // ── 扫描 ────────────────────────────────────────────────
    private async Task ScanAsync()
    {
        var videoPath = VideoPathBox.Text.Trim();
        var subPath = SubtitlePathBox.Text.Trim();

        if (string.IsNullOrEmpty(videoPath) || string.IsNullOrEmpty(subPath))
        { MessageBox.Show("请先选择视频和字幕文件夹"); return; }
        if (!Directory.Exists(videoPath))
        { MessageBox.Show("视频文件夹不存在"); return; }
        if (!Directory.Exists(subPath))
        { MessageBox.Show("字幕文件夹不存在"); return; }

        ScanBtn.IsEnabled = false;
        ScanBtn.Content = "⏳ 扫描中...";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var (videos, subtitles) = await Task.Run(() => _scanner.Scan(videoPath, subPath));
            _allSubtitles = subtitles;

            var matchItems = await Task.Run(() =>
                _matcher.Match(videos, subtitles, videoPath, subPath));

            _items.Clear();
            _history.Clear();
            foreach (var item in matchItems)
            {
                if (item.IsMatched) item.MatchMethod = "文件名匹配";
                _items.Add(item);
            }

            // AI 智能匹配（如果开启）
            if (_aiEnabled)
                await RunAiMatching();

            // 为未匹配项标注原因
            var nonEpTags = new[] { "nced", "ncop", "pv", "pved", "menu", "cm", "special", "trailer", "preview", "sample",
                "op", "ed", "opening", "ending", "nc", "creditless", "interview", "making", "behind", "teaser", "recap",
                "extra", "bonus", "ova", "oad", "sp" };
            foreach (var item in _items)
            {
                if (item.IsMatched) continue;
                var fname = Path.GetFileNameWithoutExtension(item.VideoPath).ToLowerInvariant();
                if (nonEpTags.Any(t => fname.Contains(t)))
                    item.UnmatchedReason = "非剧集标记过滤";
                else if (!item.VideoEpisodeNumber.HasValue && string.IsNullOrEmpty(item.VideoSeriesKey))
                    item.UnmatchedReason = "无法识别剧名和集号";
                else if (!item.VideoEpisodeNumber.HasValue)
                    item.UnmatchedReason = "无集号（电影/单文件）";
                else if (_items.Any(i => i != item && i.VideoEpisodeNumber == item.VideoEpisodeNumber
                    && !string.IsNullOrEmpty(i.VideoSeriesKey) && !string.Equals(i.VideoSeriesKey, item.VideoSeriesKey, StringComparison.OrdinalIgnoreCase)))
                    item.UnmatchedReason = "同集多剧歧义跳过";
                else
                    item.UnmatchedReason = "无匹配字幕";
            }

            var matched = _items.Count(i => i.IsMatched);
            var unmatched = _items.Count - matched;
            sw.Stop();

            UpdateStatusPills(_items.Count, _allSubtitles.Count, matched, unmatched, sw.ElapsedMilliseconds);
            UpdateSelectedCount();
            ScanBtn.Content = $"🔍 扫描 ({_items.Count}v/{sw.ElapsedMilliseconds}ms)";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"扫描出错: {ex.Message}", "错误");
        }
        finally
        {
            ScanBtn.IsEnabled = true;
            _ = ResetScanBtnAsync();
        }
    }

    private async Task ResetScanBtnAsync()
    {
        await Task.Delay(3000);
        ScanBtn.Content = "🔍 扫描";
    }

    // ── AI 智能匹配（批量版）────────────────────────────────
    private async Task RunAiMatching()
    {
        var pending = _items.Where(i => !i.IsMatched && i.VideoEpisodeNumber.HasValue && !string.IsNullOrEmpty(i.VideoSeriesKey)).ToList();
        var pendingNoEp = _items.Where(i => !i.IsMatched && !i.VideoEpisodeNumber.HasValue && !string.IsNullOrEmpty(i.VideoSeriesKey)).ToList();

        if (pending.Count == 0 && pendingNoEp.Count == 0) return;

        ProgressBar.Visibility = Visibility.Visible;
        var statusText = StatusPills.Children.OfType<TextBlock>().LastOrDefault();
        if (statusText != null) statusText.Text = "AI 匹配中...";

        // ── 有集号 → 按集分组 → 每批一次 AI 调用 ──
        var subsByEp = _allSubtitles.Where(s => s.HasEpisode && !string.IsNullOrEmpty(s.SeriesKey))
            .GroupBy(s => s.Episode!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var groupedByEp = pending
            .GroupBy(i => i.VideoEpisodeNumber!.Value)
            .ToList();

        int totalBatches = groupedByEp.Count + (pendingNoEp.Count > 0 ? 1 : 0);
        int batchDone = 0;

        foreach (var epGroup in groupedByEp)
        {
            var ep = epGroup.Key;
            if (!subsByEp.TryGetValue(ep, out var epSubs)) continue;

            var vidList = epGroup.Select(i => (i.VideoSeriesKey, i.VideoFile)).ToList();
            var subList = epSubs.Select(s => (s.SeriesKey, s.DisplayName)).ToList();

            var matches = await _aiMatcher.BatchCompareNamesAsync(vidList, subList);

            foreach (var m in matches)
            {
                if (m.VideoIndex < 0 || m.VideoIndex >= epGroup.Count()) continue;
                if (m.SubtitleIndex < 0 || m.SubtitleIndex >= epSubs.Count) continue;
                if (m.Confidence < 0.3) continue;

                var item = epGroup.ElementAt(m.VideoIndex);
                var sub = epSubs[m.SubtitleIndex];
                if (m.Confidence >= 0.5)
                {
                    item.SubtitleFile = sub.DisplayName;
                    item.SubtitlePath = sub.FilePath;
                    item.SubtitleEpisode = sub.EpisodeLabel;
                    item.MatchMethod = "AI 智能匹配";
                }
                else
                {
                    // 疑似匹配：0.3 ≤ confidence < 0.5
                    item.SubtitleFile = sub.DisplayName;
                    item.SubtitlePath = sub.FilePath;
                    item.SubtitleEpisode = sub.EpisodeLabel;
                    item.MatchMethod = "⚠ 疑似匹配";
                    item.IsSuspected = true;
                }
            }

            batchDone++;
            ProgressBar.Value = (double)batchDone / totalBatches * ProgressBar.Maximum;
        }

        // ── 无集号（电影/单文件）→ 一次 AI 调用 ──
        if (pendingNoEp.Count > 0)
        {
            var subsNoEp = _allSubtitles.Where(s => !s.HasEpisode && !string.IsNullOrEmpty(s.SeriesKey)).ToList();
            if (subsNoEp.Count > 0)
            {
                var vidList = pendingNoEp.Select(i => (i.VideoSeriesKey, i.VideoFile)).ToList();
                var subList = subsNoEp.Select(s => (s.SeriesKey, s.DisplayName)).ToList();

                var matches = await _aiMatcher.BatchCompareNamesAsync(vidList, subList);

                foreach (var m in matches)
                {
                    if (m.VideoIndex < 0 || m.VideoIndex >= pendingNoEp.Count) continue;
                    if (m.SubtitleIndex < 0 || m.SubtitleIndex >= subsNoEp.Count) continue;
                    if (m.Confidence < 0.3) continue;

                    var item = pendingNoEp[m.VideoIndex];
                    var sub = subsNoEp[m.SubtitleIndex];
                    item.SubtitleFile = sub.DisplayName;
                    item.SubtitlePath = sub.FilePath;
                    item.SubtitleEpisode = sub.EpisodeLabel;
                    if (m.Confidence >= 0.5)
                    {
                        item.MatchMethod = "AI 智能匹配";
                    }
                    else
                    {
                        item.MatchMethod = "⚠ 疑似匹配";
                        item.IsSuspected = true;
                    }
                }
            }

            batchDone++;
            ProgressBar.Value = (double)batchDone / totalBatches * ProgressBar.Maximum;
        }

        ProgressBar.Visibility = Visibility.Collapsed;
    }

    // ── 执行 ────────────────────────────────────────────────
    private void Execute()
    {
        var selected = _items.Where(i => i.IsSelected && !string.IsNullOrEmpty(i.SubtitlePath)).ToList();
        if (selected.Count == 0)
        { MessageBox.Show("没有选中的匹配项"); return; }

        var mode = GetOperationMode();

        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Maximum = selected.Count;
        ProgressBar.Value = 0;

        for (int i = 0; i < selected.Count; i++)
        {
            var item = selected[i];
            var targetDir = Path.GetDirectoryName(item.VideoPath)!;
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            var targetFile = mode == "Rename"
                ? Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(item.VideoPath)}{Path.GetExtension(item.SubtitlePath)}")
                : Path.Combine(targetDir, Path.GetFileName(item.SubtitlePath));

            try
            {
                if (mode == "Copy" || mode == "Rename")
                {
                    File.Copy(item.SubtitlePath, targetFile, overwrite: true);
                    _history.Record(mode, item.SubtitlePath, targetFile);
                }
                else if (mode == "Move")
                {
                    File.Move(item.SubtitlePath, targetFile, overwrite: true);
                    _history.Record("Move", item.SubtitlePath, targetFile);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"执行{mode}失败", ex);
            }

            ProgressBar.Value = i + 1;
        }

        ProgressBar.Visibility = Visibility.Collapsed;
        UpdateSelectedCount();
        MessageBox.Show($"已处理 {selected.Count} 项", "完成");
    }

    // ── 撤销 ────────────────────────────────────────────────
    private void Undo()
    {
        if (!_history.HasUndo)
        { MessageBox.Show("没有可撤销的操作"); return; }

        ProgressBar.Visibility = Visibility.Visible;
        int count = 0;
        while (_history.HasUndo)
        {
            if (_history.UndoLast()) count++;
            else break;
        }
        ProgressBar.Visibility = Visibility.Collapsed;

        if (count > 0)
            MessageBox.Show($"已撤销 {count} 项操作", "撤销完成");
    }

    // ── 操作模式 ────────────────────────────────────────────
    private string _operationMode = "Copy";

    private string GetOperationMode() => _operationMode;

    private void SetOperationMode(string mode)
    {
        _operationMode = mode;
        var btns = new[] { CopyModeBtn, MoveModeBtn, RenameModeBtn };
        foreach (var b in btns)
            b.Style = (Style)FindResource("ToolBtn");
        var active = mode switch
        {
            "Copy" => CopyModeBtn,
            "Move" => MoveModeBtn,
            _ => RenameModeBtn
        };
        active.Style = (Style)FindResource("ToolBtnActive");
    }

    // ── AI 切换 ─────────────────────────────────────────────
    private void ToggleAi()
    {
        _aiEnabled = !_aiEnabled;
        UpdateAiButton();

        var cfg = _config.LoadAiConfig();
        cfg.Enabled = _aiEnabled;
        _config.SaveAiConfig(cfg);
    }

    private void UpdateAiButton()
    {
        AiToggleBtn.Content = _aiEnabled ? "🤖 AI 开" : "🤖 AI 关";
        AiToggleBtn.Style = (Style)FindResource(_aiEnabled ? "ToolBtnPrimary" : "ToolBtnOff");
    }

    // ── 配置窗口 ────────────────────────────────────────────
    private void OpenConfig()
    {
        var win = new AiConfigWindow(_config.LoadAiConfig()) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _config.SaveAiConfig(win.Config);
            _aiMatcher.UpdateConfig(win.Config);
        }
    }

    // ── Tab 切换 ────────────────────────────────────────────
    private void SwitchTab(bool isAuto)
    {
        var activeBg = System.Windows.Media.Brushes.White;
        var inactiveBg = System.Windows.Media.Brushes.Transparent;
        AutoTabBg.Background = isAuto ? activeBg : inactiveBg;
        ManualTabBg.Background = isAuto ? inactiveBg : activeBg;
        AutoTabBtn.Foreground = (Brush)FindResource(isAuto ? "PrimaryHoverBrush" : "TextLightBrush");
        ManualTabBtn.Foreground = (Brush)FindResource(isAuto ? "TextLightBrush" : "PrimaryHoverBrush");

        // 手动模式：显示下拉选择
        if (!isAuto)
        {
            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(item.SubtitlePath)) continue;
                item.AllSubtitles = _allSubtitles.Select(s => s.DisplayName).ToList();
            }
        }
    }

    // ── 路径框拖拽提示显隐 ───────────────────────────────
    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender == VideoPathBox)
            VideoDragHint.Visibility = string.IsNullOrEmpty(VideoPathBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        else if (sender == SubtitlePathBox)
            SubtitleDragHint.Visibility = string.IsNullOrEmpty(SubtitlePathBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 路径框拖拽（各框独立处理） ────────────────────────────
    private static string GetDirFromDrop(string path)
    {
        var attr = File.GetAttributes(path);
        return attr.HasFlag(FileAttributes.Directory)
            ? path
            : Path.GetDirectoryName(path) ?? "";
    }

    private void PathBox_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void PathBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        if (sender is TextBox textBox)
            textBox.Text = GetDirFromDrop(files[0]);
    }

    // ── 状态更新 ────────────────────────────────────────────
    private void UpdateStatusPills(int videoCount, int subCount, int matched, int unmatched, long ms)
    {
        var pills = new[] { "📺 视频 " + videoCount, "📝 字幕 " + subCount,
            "● 匹配 " + matched, "● 未匹配 " + unmatched };
        for (int i = 0; i < Math.Min(4, StatusPills.Children.Count); i++)
        {
            if (StatusPills.Children[i] is TextBlock tb)
                tb.Text = pills[i];
        }
        if (StatusPills.Children.Count > 4 && StatusPills.Children[4] is TextBlock tb5)
            tb5.Text = $"{ms}ms";
    }

    private void UpdateSelectedCount()
    {
        var sel = _items.Count(i => i.IsSelected);
        var total = _items.Count;
        SelectedCount.Text = sel.ToString();
        TotalCount.Text = total.ToString();
    }

    // ── 配色切换（已固定为薄荷茶色） ─────────────────────────
    // 移除多余配色，只保留一套

    // 移除 CycleTheme / UpdateThemeRecursive / LoadSavedTheme

    // ── DataGrid 行加载 ─────────────────────────────────────
    private void MatchGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        // 更新勾选统计
        UpdateSelectedCount();
    }
}
