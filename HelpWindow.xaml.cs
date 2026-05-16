using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SubtitleMatcher
{
    public partial class HelpWindow : Window
    {
        private string _currentView = "simple";
        private double _savedScrollFraction = 0;
        private ScrollViewer? _innerScrollViewer;

        public HelpWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                _innerScrollViewer = FindScrollViewer(HelpDocumentViewer);
                if (_innerScrollViewer != null)
                    _innerScrollViewer.ScrollChanged += (_, _) =>
                    {
                        if (_innerScrollViewer.ExtentHeight > _innerScrollViewer.ViewportHeight)
                            _savedScrollFraction = _innerScrollViewer.VerticalOffset / (_innerScrollViewer.ExtentHeight - _innerScrollViewer.ViewportHeight);
                    };
                HelpDocumentViewer.SizeChanged += OnHelpViewerSizeChanged;
            };
            LoadHelpDocument();
        }

        private void OnHelpViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_innerScrollViewer == null) return;
            if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) < 5) return;

            var fraction = _savedScrollFraction;
            var sv = _innerScrollViewer;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (sv.ExtentHeight > sv.ViewportHeight)
                    sv.ScrollToVerticalOffset(fraction * (sv.ExtentHeight - sv.ViewportHeight));
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LoadHelpDocument()
        {
            switch (_currentView)
            {
                case "simple":
                    HelpDocumentViewer.Document = BuildSimpleDocument();
                    break;
                case "examples":
                    HelpDocumentViewer.Document = BuildSectionDocument("示例场景", false);
                    break;
                case "faq":
                    HelpDocumentViewer.Document = BuildFaqDocument();
                    break;
                default:
                    LoadDetailedDocument();
                    break;
            }
            // 每次切换标签页后重置滚动位置到顶部
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (HelpDocumentViewer.Document == null) return;
                var sv = FindScrollViewer(HelpDocumentViewer);
                sv?.ScrollToHome();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv) return sv;
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void LoadDetailedDocument()
        {
            var readmePath = FindReadmePath();
            if (readmePath != null)
            {
                var helpText = File.ReadAllText(readmePath);
                HelpDocumentViewer.Document = MarkdownHelper.RenderMarkdown(helpText, "视频字幕自动匹配工具-说明文档");
            }
            else
            {
                HelpDocumentViewer.Document = BuildFallbackDocument();
            }
        }

        private FlowDocument BuildSectionDocument(string sectionHeader, bool includeSubsections)
        {
            var readmePath = FindReadmePath();
            if (readmePath == null) return BuildFallbackDocument();

            var text = File.ReadAllText(readmePath);
            var section = ExtractSection(text, sectionHeader, includeSubsections);
            if (string.IsNullOrEmpty(section))
            {
                return BuildFallbackDocument(sectionHeader + " 未找到");
            }

            var fullText = "# 视频字幕自动匹配工具-" + sectionHeader + "\n\n" + section;
            return MarkdownHelper.RenderMarkdown(fullText, "视频字幕自动匹配工具-" + sectionHeader);
        }

        private FlowDocument BuildFaqDocument()
        {
            var readmePath = FindReadmePath();
            if (readmePath == null) return BuildFallbackDocument();

            var text = File.ReadAllText(readmePath);

            var faqSection = ExtractSection(text, "常见问题", false);
            var limitsSection = ExtractSection(text, "当前实现限制", false);
            var notesSection = ExtractSection(text, "注意事项", false);

            var combined = "# 视频字幕自动匹配工具-常见问题\n\n" + faqSection
                + "\n\n" + limitsSection
                + "\n\n" + notesSection;

            return MarkdownHelper.RenderMarkdown(combined, "视频字幕自动匹配工具-常见问题");
        }

        private static string ExtractSection(string markdown, string sectionHeader, bool includeNextSections)
        {
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Equals("## " + sectionHeader, StringComparison.Ordinal) ||
                    trimmed.Equals("# " + sectionHeader, StringComparison.Ordinal))
                {
                    start = i;
                    break;
                }
            }
            if (start == -1) return "";

            int end = lines.Length;
            if (!includeNextSections)
            {
                int headingLevel = lines[start].TrimStart().StartsWith("# ") ? 1 : 2;
                for (int i = start + 1; i < lines.Length; i++)
                {
                    var t = lines[i].TrimStart();
                    if ((headingLevel == 1 && t.StartsWith("# ") && !t.StartsWith("## ")) ||
                        (headingLevel == 2 && t.StartsWith("## ") && !t.StartsWith("### ")))
                    {
                        end = i;
                        break;
                    }
                }
            }

            return string.Join("\n", lines, start, end - start);
        }

        private static string? FindReadmePath()
        {
            var paths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "README.md"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md"),
                Path.Combine(Environment.CurrentDirectory, "README.md")
            };
            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static FlowDocument BuildFallbackDocument(string message = "README.md 文件未找到")
        {
            var fallback = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI Emoji, Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                PagePadding = new Thickness(10),
                LineHeight = 24
            };
            fallback.Blocks.Add(new Paragraph(new Run("视频字幕自动匹配工具-说明文档"))
            {
                FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(21, 101, 192))
            });
            fallback.Blocks.Add(new Paragraph(new Run(message)));
            return fallback;
        }

        private static void SetTabStyle(Button active, params Button[] inactive)
        {
            active.Background = new SolidColorBrush(Color.FromRgb(21, 101, 192));
            active.Foreground = Brushes.White;
            foreach (var btn in inactive)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(97, 97, 97));
            }
        }

        private void TabSimple_Click(object sender, RoutedEventArgs e)
        {
            if (_currentView == "simple") return;
            _currentView = "simple";
            SetTabStyle(TabSimpleButton, TabExamplesButton, TabFaqButton, TabDetailButton);
            LoadHelpDocument();
        }

        private void TabExamples_Click(object sender, RoutedEventArgs e)
        {
            if (_currentView == "examples") return;
            _currentView = "examples";
            SetTabStyle(TabExamplesButton, TabSimpleButton, TabFaqButton, TabDetailButton);
            LoadHelpDocument();
        }

        private void TabFaq_Click(object sender, RoutedEventArgs e)
        {
            if (_currentView == "faq") return;
            _currentView = "faq";
            SetTabStyle(TabFaqButton, TabSimpleButton, TabExamplesButton, TabDetailButton);
            LoadHelpDocument();
        }

        private void TabDetail_Click(object sender, RoutedEventArgs e)
        {
            if (_currentView == "detail") return;
            _currentView = "detail";
            SetTabStyle(TabDetailButton, TabSimpleButton, TabExamplesButton, TabFaqButton);
            LoadHelpDocument();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private FlowDocument BuildSimpleDocument()
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI Emoji, Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                PagePadding = new Thickness(10),
                LineHeight = 24
            };

            var emojiFont = new FontFamily("Microsoft YaHei UI, Segoe UI Emoji, Segoe UI");
            var blueBrush = new SolidColorBrush(Color.FromRgb(21, 101, 192));
            var h2Brush = new SolidColorBrush(Color.FromRgb(26, 35, 126));
            var bodyBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

            var titleBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 10),
                Child = new TextBlock
                {
                    Text = "🎬 视频字幕自动匹配工具",
                    FontFamily = emojiFont,
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = blueBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            doc.Blocks.Add(new BlockUIContainer(titleBorder) { Margin = new Thickness(0, 0, 0, 14) });

            AddParagraph(doc, "一个 Windows 桌面工具，扫描视频和字幕文件，自动根据剧名、季数、集数智能匹配，并批量复制、移动或重命名字幕。", emojiFont, bodyBrush);

            AddH2(doc, "核心功能", emojiFont, h2Brush);

            var features = new[]
            {
                ("🔍 智能扫描", "递归扫描视频和字幕文件夹，自动提取剧名、季数、集数（支持 S01E05、第12集、[01] 等多种格式）"),
                ("🎯 自动匹配", "按「文件名剧名优先 → 文件夹上下文兜底 → 季数 → 集数」顺序匹配，避免多部剧同集数串匹配"),
                ("📋 三种操作", "复制字幕 / 移动字幕 / 重命名（复制并改为视频文件名，方便播放器自动加载）"),
                ("🔧 手动匹配", "自动匹配不准确时，可通过下拉列表或浏览按钮手动指定字幕"),
                ("↩️ 撤销操作", "支持撤销本次运行期间的文件操作，关闭软件后不保留"),
                ("🌐 联网翻译", "可选开启翻译，将中/日/韩文等非英文剧名翻译成英文后匹配，提高跨语言匹配率"),
                ("📊 排序显示", "结果按剧名→季数/文件夹排序→集数升序排序，季数优先使用显式关键词，无关键词时用文件夹数字前缀（仅排序不影响匹配），标签页右侧实时显示扫描状态（已匹配/未匹配数）")
            };

            foreach (var (title, desc) in features)
            {
                AddFeatureItem(doc, title, desc, emojiFont, blueBrush, bodyBrush);
            }

            AddH2(doc, "支持格式", emojiFont, h2Brush);

            var formatGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 4, 0, 10) };
            formatGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            formatGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            var videoLabel = new System.Windows.Controls.TextBlock
            {
                Text = "视频：",
                FontFamily = emojiFont,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = h2Brush,
                Margin = new Thickness(0, 0, 8, 4)
            };
            var videoFormats = new System.Windows.Controls.TextBlock
            {
                Text = "mp4、mkv、avi、wmv、mov、flv、webm",
                FontFamily = emojiFont,
                FontSize = 14,
                Foreground = bodyBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var subLabel = new System.Windows.Controls.TextBlock
            {
                Text = "字幕：",
                FontFamily = emojiFont,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = h2Brush,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var subFormats = new System.Windows.Controls.TextBlock
            {
                Text = "srt、ass、ssa、sub、sup、txt",
                FontFamily = emojiFont,
                FontSize = 14,
                Foreground = bodyBrush,
                TextWrapping = TextWrapping.Wrap
            };

            System.Windows.Controls.Grid.SetColumn(videoLabel, 0);
            System.Windows.Controls.Grid.SetRow(videoLabel, 0);
            System.Windows.Controls.Grid.SetColumn(videoFormats, 1);
            System.Windows.Controls.Grid.SetRow(videoFormats, 0);
            System.Windows.Controls.Grid.SetColumn(subLabel, 0);
            System.Windows.Controls.Grid.SetRow(subLabel, 1);
            System.Windows.Controls.Grid.SetColumn(subFormats, 1);
            System.Windows.Controls.Grid.SetRow(subFormats, 1);

            formatGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            formatGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            formatGrid.Children.Add(videoLabel);
            formatGrid.Children.Add(videoFormats);
            formatGrid.Children.Add(subLabel);
            formatGrid.Children.Add(subFormats);

            var formatCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(228, 228, 228)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Child = formatGrid
            };
            doc.Blocks.Add(new BlockUIContainer(formatCard) { Margin = new Thickness(0, 0, 0, 10) });

            AddH2(doc, "快速上手", emojiFont, h2Brush);

            var steps = new[]
            {
                "选择视频文件夹和字幕文件夹",
                "点击「扫描」自动匹配",
                "检查结果，不准确的项目切换到「手动匹配」页调整",
                "选择操作方式（复制/移动/重命名），勾选需要处理的项目",
                "点击「执行」完成操作，如需撤回点击「撤销」"
            };

            var stepsCard = new System.Windows.Controls.StackPanel();
            for (int i = 0; i < steps.Length; i++)
            {
                var stepGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 2, 0, 4) };
                stepGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
                stepGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                var numTb = new System.Windows.Controls.TextBlock
                {
                    Text = (i + 1) + ". ",
                    FontFamily = emojiFont,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = blueBrush,
                    TextAlignment = System.Windows.TextAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                var contentTb = new System.Windows.Controls.TextBlock
                {
                    Text = steps[i],
                    FontFamily = emojiFont,
                    FontSize = 14,
                    Foreground = bodyBrush,
                    TextWrapping = TextWrapping.Wrap
                };

                System.Windows.Controls.Grid.SetColumn(numTb, 0);
                System.Windows.Controls.Grid.SetColumn(contentTb, 1);
                stepGrid.Children.Add(numTb);
                stepGrid.Children.Add(contentTb);
                stepsCard.Children.Add(stepGrid);
            }

            var stepsBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(228, 228, 228)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Child = stepsCard
            };
            doc.Blocks.Add(new BlockUIContainer(stepsBorder) { Margin = new Thickness(0, 0, 0, 10) });

            AddH2(doc, "许可", emojiFont, h2Brush);

            var licenseItems = new[]
            {
                "✅ 允许个人免费使用、修改和无偿分享",
                "✅ 允许整合到其他软件（功能须免费开放）",
                "❌ 禁止售卖、付费打包、网盘收费等盈利行为",
                "⚠️ 传播时须保留项目名和作者署名",
                "⚠️ 使用风险由使用者自行承担"
            };

            var licenseCard = new System.Windows.Controls.StackPanel();
            foreach (var item in licenseItems)
            {
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = item,
                    FontFamily = emojiFont,
                    FontSize = 14,
                    Foreground = bodyBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 4)
                };
                licenseCard.Children.Add(tb);
            }

            var licenseBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(228, 228, 228)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Child = licenseCard
            };
            doc.Blocks.Add(new BlockUIContainer(licenseBorder));

            return doc;
        }

        private static void AddH2(FlowDocument doc, string text, FontFamily font, SolidColorBrush brush)
        {
            var tb = new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontFamily = font,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 8)
            };
            doc.Blocks.Add(new BlockUIContainer(tb));
        }

        private static void AddParagraph(FlowDocument doc, string text, FontFamily font, SolidColorBrush brush)
        {
            var tb = new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontFamily = font,
                FontSize = 14,
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            doc.Blocks.Add(new BlockUIContainer(tb));
        }

        private static void AddFeatureItem(FlowDocument doc, string title, string desc, FontFamily font, SolidColorBrush titleBrush, SolidColorBrush bodyBrush)
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 3, 0, 6) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            var titleTb = new System.Windows.Controls.TextBlock
            {
                Text = title + " ",
                FontFamily = font,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = titleBrush,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 4, 0)
            };
            var descTb = new System.Windows.Controls.TextBlock
            {
                Text = desc,
                FontFamily = font,
                FontSize = 14,
                Foreground = bodyBrush,
                TextWrapping = TextWrapping.Wrap
            };

            System.Windows.Controls.Grid.SetColumn(titleTb, 0);
            System.Windows.Controls.Grid.SetColumn(descTb, 1);
            grid.Children.Add(titleTb);
            grid.Children.Add(descTb);
            doc.Blocks.Add(new BlockUIContainer(grid));
        }
    }
}
