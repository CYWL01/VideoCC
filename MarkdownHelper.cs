using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SubtitleMatcher
{
    internal static class MarkdownHelper
    {
        public static FlowDocument RenderMarkdown(string markdown)
        {
            return RenderMarkdown(markdown, null);
        }

        public static FlowDocument RenderMarkdown(string markdown, string? replaceH1)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI Emoji, Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                PagePadding = new Thickness(10),
                LineHeight = 24,
                IsHyphenationEnabled = true
            };

            var tableRows = new List<List<string>>();
            var tableAligns = new List<string>();
            bool inTable = false, inCodeBlock = false;
            var codeLines = new List<string>();
            bool inCard = false;
            StackPanel? currentCard = null;

            void FlushCard()
            {
                if (currentCard == null) return;
                var cardBorder = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(228, 228, 228)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(16, 12, 16, 12),
                    Child = currentCard
                };
                doc.Blocks.Add(new BlockUIContainer(cardBorder) { Margin = new Thickness(0, 8, 0, 0) });
                currentCard = null;
                inCard = false;
            }

            void FlushTable()
            {
                if (!inTable || tableRows.Count == 0) { tableRows.Clear(); tableAligns.Clear(); inTable = false; return; }
                BuildTable(ref inCard, ref currentCard, tableRows, tableAligns);
                tableRows.Clear(); tableAligns.Clear(); inTable = false;
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock) { FlushCode(ref inCard, ref currentCard, codeLines); codeLines.Clear(); inCodeBlock = false; }
                    else inCodeBlock = true;
                    continue;
                }
                if (inCodeBlock) { codeLines.Add(line); continue; }
                if (string.IsNullOrWhiteSpace(line)) { FlushTable(); continue; }

                if (inTable)
                {
                    if (IsTableRow(line))
                    {
                        if (IsTableSeparator(line)) tableAligns = ParseTableAlignments(line);
                        else tableRows.Add(ParseTableCells(line));
                        continue;
                    }
                    FlushTable();
                }
                else if (IsTableRow(line)) { FlushTable(); tableRows.Add(ParseTableCells(line)); inTable = true; continue; }

                if (line.StartsWith("### "))
                {
                    FlushTable();
                    EnsureCard(ref inCard, ref currentCard);
                    currentCard!.Children.Add(MakeH3(line[4..]));
                    continue;
                }
                if (line.StartsWith("## "))
                {
                    FlushTable();
                    FlushCard();
                    inCard = true;
                    currentCard = new StackPanel();
                    currentCard.Children.Add(MakeH2(line[3..]));
                    continue;
                }
                if (line.StartsWith("# "))
                {
                    FlushTable();
                    FlushCard();
                    var t = replaceH1;
                    if (t == "") continue;
                    if (t == null) t = line[2..];
                    var h1Tb = new TextBlock
                    {
                        Text = t,
                        FontFamily = EmojiFont,
                        FontSize = 22, FontWeight = FontWeights.Bold,
                        Foreground = BlueBrush,
                        TextWrapping = TextWrapping.Wrap
                    };
                    doc.Blocks.Add(new BlockUIContainer(new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(14, 10, 14, 10),
                        Child = h1Tb
                    }) { Margin = new Thickness(0, 0, 0, 14) });
                    continue;
                }

                var om = Regex.Match(line, @"^(\d+)\.\s+(.*)$");
                if (om.Success)
                {
                    FlushTable();
                    EnsureCard(ref inCard, ref currentCard);
                    AddOrderedItem(currentCard!, om.Groups[1].Value, om.Groups[2].Value);
                    continue;
                }
                if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("• "))
                {
                    FlushTable();
                    EnsureCard(ref inCard, ref currentCard);
                    AddBulletItem(currentCard!, line.TrimStart()[2..], line.Length - line.TrimStart().Length);
                    continue;
                }

                FlushTable();
                EnsureCard(ref inCard, ref currentCard);
                var tb = MakeTextBlock(line);
                tb.Margin = new Thickness(0, 2, 0, 6);
                currentCard!.Children.Add(tb);
            }

            FlushTable();
            FlushCard();
            if (inCodeBlock && codeLines.Count > 0) { FlushCode(ref inCard, ref currentCard, codeLines); FlushCard(); }

            return doc;
        }

        private static readonly FontFamily EmojiFont = new FontFamily("Microsoft YaHei UI, Segoe UI Emoji, Segoe UI");
        private static readonly FontFamily MonoFont = new FontFamily("Consolas, Courier New");
        private static readonly SolidColorBrush BlueBrush = new SolidColorBrush(Color.FromRgb(21, 101, 192));
        private static readonly SolidColorBrush BodyBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        private static readonly SolidColorBrush CodeFg = new SolidColorBrush(Color.FromRgb(198, 40, 40));
        private static readonly SolidColorBrush CodeBg = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        private static readonly SolidColorBrush H2Brush = new SolidColorBrush(Color.FromRgb(26, 35, 126));
        private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));

        private static void EnsureCard(ref bool inCard, ref StackPanel? card)
        {
            if (!inCard) { inCard = true; card = new StackPanel(); }
        }

        private static TextBlock MakeTextBlock(string text)
        {
            var tb = new TextBlock { FontFamily = EmojiFont, FontSize = 14, Foreground = BodyBrush, TextWrapping = TextWrapping.Wrap };
            FillInlines(tb, text);
            return tb;
        }

        private static TextBlock MakeH2(string text)
        {
            return new TextBlock { Text = text, FontFamily = EmojiFont, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = H2Brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        }

        private static Border MakeH3(string text)
        {
            var tb = new TextBlock
            {
                Text = text, FontFamily = EmojiFont, FontSize = 16,
                FontWeight = FontWeights.Bold, Foreground = H2Brush,
                TextWrapping = TextWrapping.Wrap
            };
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 81, 181)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Margin = new Thickness(0, 10, 0, 4),
                Padding = new Thickness(8, 0, 0, 0),
                Child = tb
            };
        }

        private static void AddOrderedItem(StackPanel parent, string num, string text)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var numTb = new TextBlock
            {
                Text = num + ". ",
                FontFamily = EmojiFont, FontSize = 14,
                FontWeight = FontWeights.Bold, Foreground = BlueBrush,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 6, 0),
                TextWrapping = TextWrapping.Wrap
            };
            var contentTb = new TextBlock
            {
                FontFamily = EmojiFont, FontSize = 14,
                Foreground = BodyBrush, TextWrapping = TextWrapping.Wrap
            };
            FillInlines(contentTb, text);

            Grid.SetColumn(numTb, 0);
            Grid.SetColumn(contentTb, 1);
            grid.Children.Add(numTb);
            grid.Children.Add(contentTb);
            parent.Children.Add(grid);
        }

        private static void AddBulletItem(StackPanel parent, string text, int indent)
        {
            var grid = new Grid { Margin = new Thickness(indent, 2, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock
            {
                Text = "• ",
                FontFamily = EmojiFont, FontSize = 14, Foreground = GrayBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 4, 0)
            });
            var contentTb = new TextBlock
            {
                FontFamily = EmojiFont, FontSize = 14,
                Foreground = BodyBrush, TextWrapping = TextWrapping.Wrap
            };
            FillInlines(contentTb, text);
            Grid.SetColumn(contentTb, 1);
            grid.Children.Add(contentTb);
            parent.Children.Add(grid);
        }

        private static void FlushCode(ref bool inCard, ref StackPanel? card, List<string> lines)
        {
            EnsureCard(ref inCard, ref card);
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 42, 53)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 10)
            };
            b.Child = new TextBlock
            {
                Text = string.Join("\n", lines),
                FontFamily = MonoFont, FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 235, 240)),
                TextWrapping = TextWrapping.Wrap
            };
            card!.Children.Add(b);
        }

        private static void FillInlines(TextBlock tb, string text)
        {
            var parts = Regex.Split(text, @"(\*\*.*?\*\*|`[^`]+`)");
            foreach (var part in parts)
            {
                if (part.StartsWith("**") && part.EndsWith("**"))
                    tb.Inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeights.Bold, Foreground = BlueBrush, FontFamily = EmojiFont });
                else if (part.StartsWith("`") && part.EndsWith("`"))
                    tb.Inlines.Add(new Run(part[1..^1]) { FontFamily = MonoFont, FontSize = 12.5, Foreground = CodeFg, Background = CodeBg });
                else
                    tb.Inlines.Add(new Run(part) { FontFamily = EmojiFont });
            }
        }

        private static void BuildTable(ref bool inCard, ref StackPanel? card, List<List<string>> rows, List<string> aligns)
        {
            EnsureCard(ref inCard, ref card);
            int colCount = rows[0].Count;
            for (int r = 1; r < rows.Count; r++) if (rows[r].Count > colCount) colCount = rows[r].Count;

            // 列宽使用等比例分配，确保表格始终在卡片宽度内显示完整
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 10) };
            for (int c = 0; c < colCount; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
            }
            for (int r = 0; r < rows.Count; r++) grid.RowDefinitions.Add(new RowDefinition());

            var hdrBg = new SolidColorBrush(Color.FromRgb(232, 234, 246));
            var altBg = new SolidColorBrush(Color.FromRgb(248, 249, 251));
            var borderBr = new SolidColorBrush(Color.FromRgb(218, 220, 224));
            bool hasHdr = rows.Count > 1;

            for (int r = 0; r < rows.Count; r++)
            {
                bool isHdr = hasHdr && r == 0;
                for (int c = 0; c < colCount; c++)
                {
                    var cellText = c < rows[r].Count ? rows[r][c] : "";
                    var cellB = new Border
                    {
                        BorderBrush = borderBr, BorderThickness = new Thickness(0.5),
                        Background = isHdr ? hdrBg : (r % 2 == 0 ? altBg : Brushes.White),
                        Padding = new Thickness(8, 5, 8, 5)
                    };

                    var cellTb = new TextBlock
                    {
                        FontFamily = EmojiFont, FontSize = 12.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                        FontWeight = isHdr ? FontWeights.Bold : FontWeights.Normal,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };
                    FillInlines(cellTb, cellText);
                    cellB.Child = cellTb;
                    Grid.SetRow(cellB, r); Grid.SetColumn(cellB, c);
                    grid.Children.Add(cellB);
                }
            }
            card!.Children.Add(grid);
        }

        private static bool IsTableRow(string line) { var t = line.Trim(); return t.StartsWith("|") && t.EndsWith("|"); }
        private static bool IsTableSeparator(string line) => Regex.IsMatch(line.Trim(), @"^\|[\s\-:]+\|[\s\-:]+\|");
        private static List<string> ParseTableAlignments(string line)
        {
            var r = new List<string>();
            foreach (var c in line.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries))
            { var t = c.Trim(); r.Add(t.StartsWith(":") && t.EndsWith(":") ? "Center" : t.EndsWith(":") ? "Right" : "Left"); }
            return r;
        }
        private static List<string> ParseTableCells(string line)
        {
            var r = new List<string>();
            var t = line.Trim();
            if (t.StartsWith("|")) t = t[1..];
            if (t.EndsWith("|")) t = t[..^1];
            foreach (var c in t.Split('|')) r.Add(c.Trim());
            return r;
        }
    }
}
