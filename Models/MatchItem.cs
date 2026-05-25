using System.ComponentModel;
using System.IO;

namespace SubtitleMatcher.Models;

public class MatchItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _subtitleFile = string.Empty;
    private string _subtitleEpisode = string.Empty;
    private string _subtitlePath = string.Empty;
    private string _cachedDisplay = string.Empty;
    private string _matchMethod = string.Empty;
    private string _unmatchedReason = string.Empty;
    private bool _isSuspected;

    public int RowNumber { get; set; }
    public string VideoFile { get; set; } = string.Empty;
    public string VideoEpisode { get; set; } = string.Empty;
    public string VideoPath { get; set; } = string.Empty;
    public string VideoSeriesKey { get; set; } = string.Empty;
    public string SubtitleRootPath { get; set; } = string.Empty;
    public int? FolderSortOrder { get; set; }
    public int? VideoSeason { get; set; }
    public int? VideoEpisodeNumber { get; set; }

    public string SubtitleFile
    {
        get => _subtitleFile;
        set
        {
            if (_subtitleFile != value)
            {
                _subtitleFile = value;
                _cachedDisplay = ComputeDisplay();
                OnPropertyChanged(nameof(SubtitleFile));
                OnPropertyChanged(nameof(SubtitleDisplay));
                OnPropertyChanged(nameof(IsMatched));
            }
        }
    }

    public string SubtitleDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(_cachedDisplay))
                return "— 未匹配 —";
            return _cachedDisplay;
        }
    }

    public bool IsMatched => !string.IsNullOrEmpty(_subtitlePath);

    public string MatchMethod
    {
        get
        {
            if (!string.IsNullOrEmpty(_matchMethod)) return _matchMethod;
            if (!string.IsNullOrEmpty(_unmatchedReason)) return _unmatchedReason;
            return "—";
        }
        set
        {
            if (_matchMethod != value)
            {
                _matchMethod = value;
                OnPropertyChanged(nameof(MatchMethod));
            }
        }
    }

    public string UnmatchedReason
    {
        get => _unmatchedReason;
        set
        {
            if (_unmatchedReason != value)
            {
                _unmatchedReason = value;
                OnPropertyChanged(nameof(UnmatchedReason));
                OnPropertyChanged(nameof(MatchMethod)); // 刷新方式列显示
            }
        }
    }

    public bool IsSuspected
    {
        get => _isSuspected;
        set
        {
            if (_isSuspected != value)
            {
                _isSuspected = value;
                OnPropertyChanged(nameof(IsSuspected));
            }
        }
    }

    public string SubtitleEpisode
    {
        get => _subtitleEpisode;
        set
        {
            if (_subtitleEpisode != value)
            {
                _subtitleEpisode = value;
                OnPropertyChanged(nameof(SubtitleEpisode));
            }
        }
    }

    public string SubtitlePath
    {
        get => _subtitlePath;
        set
        {
            if (_subtitlePath != value)
            {
                _subtitlePath = value;
                _cachedDisplay = ComputeDisplay();
                OnPropertyChanged(nameof(SubtitlePath));
                OnPropertyChanged(nameof(IsMatched));
                OnPropertyChanged(nameof(SubtitleDisplay));
            }
        }
    }

    public IReadOnlyList<string> AllSubtitles { get; set; } = Array.Empty<string>();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private string ComputeDisplay()
    {
        if (string.IsNullOrEmpty(_subtitleFile))
            return string.Empty;
        if (string.IsNullOrEmpty(SubtitleRootPath) || string.IsNullOrEmpty(_subtitlePath))
            return _subtitleFile;
        try
        {
            var rel = Path.GetRelativePath(SubtitleRootPath, _subtitlePath);
            return rel.StartsWith("..", StringComparison.Ordinal) ? _subtitleFile : rel;
        }
        catch
        {
            return _subtitleFile;
        }
    }
}
