using System;
using System.ComponentModel;
using System.Collections.Generic;

namespace SubtitleMatcher
{
    public class MatchItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _subtitleFile = string.Empty;
        private string _subtitleEpisode = string.Empty;
        private string _subtitlePath = string.Empty;
        private string _cachedSubtitleDisplay = string.Empty;

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
                    _cachedSubtitleDisplay = ComputeCachedDisplay();
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
                if (string.IsNullOrEmpty(_cachedSubtitleDisplay))
                    return "— 未匹配 —";
                return _cachedSubtitleDisplay;
            }
        }

        public bool IsMatched => !string.IsNullOrEmpty(_subtitlePath);

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
                    _cachedSubtitleDisplay = ComputeCachedDisplay();
                    OnPropertyChanged(nameof(SubtitlePath));
                    OnPropertyChanged(nameof(IsMatched));
                    OnPropertyChanged(nameof(SubtitleDisplay));
                }
            }
        }

        public IReadOnlyList<string> AllSubtitles { get; set; } = [];

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

        private string ComputeCachedDisplay()
        {
            if (string.IsNullOrEmpty(_subtitleFile))
                return string.Empty;
            if (string.IsNullOrEmpty(SubtitleRootPath) || string.IsNullOrEmpty(_subtitlePath))
                return _subtitleFile;
            try
            {
                var rel = System.IO.Path.GetRelativePath(SubtitleRootPath, _subtitlePath);
                return rel.StartsWith("..", StringComparison.Ordinal) ? _subtitleFile : rel;
            }
            catch
            {
                return _subtitleFile;
            }
        }
    }
}
