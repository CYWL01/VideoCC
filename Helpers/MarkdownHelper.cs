using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace SubtitleMatcher.Helpers;

public static class MarkdownHelper
{
    private static readonly FontFamily EmojiFont = new("Microsoft YaHei UI, Segoe UI Emoji, Segoe UI");
    private static readonly FontFamily MonoFont = new("Consolas, Courier New");
    private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(21, 101, 192));
    private static readonly SolidColorBrush BodyBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush CodeFg = new(Color.FromRgb(198, 40, 40));
    private static readonly SolidColorBrush CodeBg = new(Color.FromRgb(245, 245, 245));
    private static readonly SolidColorBrush H2Brush = new(Color.FromRgb(26, 35, 126));
    private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush TableHdr = new(Color.FromRgb(232, 234, 246));
    private static readonly SolidColorBrush TableAlt = new(Color.FromRgb(248, 249, 251));
    private static readonly SolidColorBrush BorderBr = new(Color.FromRgb(218, 220, 224));

    public static FlowDocument Render(string markdown, string? replaceH1 = null)
    {
        var doc = new FlowDocument
        {
            FontFamily = EmojiFont,
            FontSize = 14,
            Foreground = BodyBrush,
            PagePadding = new Thickness(10),
            LineHeight = 24,
            IsHyphenationEnabled = true
        };

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var tableRows = new List<List<string>>();
        var tableAligns = new List<string>();
        bool inTable = false, inCode = false;
        var codeLines = new List<string>();
        bool inCard = false;
        StackPanel? card = null;

        void FlushTable()
        {
            if (!inTable || tableRows.Count == 0) { tableRows.Clear(); tableAligns.Clear(); inTable = false; return; }
            BuildTable(doc, ref inCard, ref card, tableRows, tableAligns);
            tableRows.Clear(); tableAligns.Clear(); inTable = false;
        }
        void FlushCard()
        {
            if (card == null) return;
            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = BorderBr,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Child = card
            };
            doc.Blocks.Add(new BlockUIContainer(border) { Margin = new Thickness(0, 8, 0, 0) });
            card = null; inCard = false;
        }

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode) { FlushCode(doc, ref inCard, ref card, codeLines); codeLines.Clear(); inCode = false; }
                else inCode = true;
                continue;
            }
            if (inCode) { codeLines.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) { FlushTable(); continue; }

            if (inTable)
            {
                if (IsTableRow(line))
                {
                    if (IsTableSep(line)) tableAligns = ParseAligns(line);
                    else tableRows.Add(ParseCells(line));
                    continue;
                }
                FlushTable();
            }
            else if (IsTableRow(line)) { FlushTable(); tableRows.Add(ParseCells(line)); inTable = true; continue; }

            if (line.StartsWith("### "))
            {
                FlushTable(); EnsureCard(ref inCard, ref card);
                card!.Children.Add(MakeH3(line[4..]));
                continue;
            }
            if (line.StartsWith("## "))
            {
                FlushTable(); FlushCard(); inCard = true;
                card = new StackPanel();
                card.Children.Add(MakeH2(line[3..]));
                continue;
            }
            if (line.StartsWith("# "))
            {
                FlushTable(); FlushCard();
                var t = replaceH1;
                if (t == "") continue;
                t ??= line[2..];
                doc.Blocks.Add(new BlockUIContainer(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 10, 14, 10),
                    Child = new TextBlock { Text = t, FontFamily = EmojiFont, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = BlueBrush, TextWrapping = TextWrapping.Wrap }
                }) { Margin = new Thickness(0, 0, 0, 14) });
                continue;
            }

            FlushTable();
            EnsureCard(ref inCard, ref card);
            var ol = Regex.Match(line, @"^(\d+)\.\s+(.*)$");
            if (ol.Success)
                AddOrderedItem(card!, ol.Groups[1].Value, ol.Groups[2].Value);
            else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("• "))
                AddBullet(card!, line.TrimStart()[2..], line.Length - line.TrimStart().Length);
            else
            {
                var tb = MakeText(line);
                tb.Margin = new Thickness(0, 2, 0, 6);
                card!.Children.Add(tb);
            }
        }

        FlushTable(); FlushCard();
        if (inCode && codeLines.Count > 0) { FlushCode(doc, ref inCard, ref card, codeLines); FlushCard(); }
        return doc;
    }

    private static void FlushCode(FlowDocument doc, ref bool inCard, ref StackPanel? card, List<string> lines)
    {
        EnsureCard(ref inCard, ref card);
        card!.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 42, 53)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 4, 0, 10),
            Child = new TextBlock
            {
                Text = string.Join("\n", lines),
                FontFamily = MonoFont, FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 235, 240)),
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private static void BuildTable(FlowDocument doc, ref bool inCard, ref StackPanel? card, List<List<string>> rows, List<string> aligns)
    {
        EnsureCard(ref inCard, ref card);
        int cols = rows.Max(r => r.Count);
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 10) };
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows.Count; r++) grid.RowDefinitions.Add(new RowDefinition());

        for (int r = 0; r < rows.Count; r++)
        {
            bool hdr = r == 0 && rows.Count > 1;
            for (int c = 0; c < cols; c++)
            {
                var txt = c < rows[r].Count ? rows[r][c] : "";
                var cell = new Border
                {
                    BorderBrush = BorderBr, BorderThickness = new Thickness(0.5),
                    Background = hdr ? TableHdr : (r % 2 == 0 ? TableAlt : Brushes.White),
                    Padding = new Thickness(8, 5, 8, 5),
                    Child = new TextBlock
                    {
                        FontFamily = EmojiFont, FontSize = 12.5,
                        Foreground = BodyBrush,
                        FontWeight = hdr ? FontWeights.Bold : FontWeights.Normal,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                var tb = (TextBlock)cell.Child;
                FillInlines(tb, txt);
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        card!.Children.Add(grid);
    }

    private static void EnsureCard(ref bool inCard, ref StackPanel? card)
    {
        if (!inCard) { inCard = true; card = new StackPanel(); }
    }

    private static TextBlock MakeH2(string text) => new()
    {
        Text = text, FontFamily = EmojiFont, FontSize = 16,
        FontWeight = FontWeights.Bold, Foreground = H2Brush,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
    };

    private static Border MakeH3(string text) => new()
    {
        BorderBrush = new SolidColorBrush(Color.FromRgb(63, 81, 181)),
        BorderThickness = new Thickness(3, 0, 0, 0),
        Margin = new Thickness(0, 10, 0, 4),
        Padding = new Thickness(8, 0, 0, 0),
        Child = new TextBlock
        {
            Text = text, FontFamily = EmojiFont, FontSize = 16,
            FontWeight = FontWeights.Bold, Foreground = H2Brush,
            TextWrapping = TextWrapping.Wrap
        }
    };

    private static TextBlock MakeText(string text)
    {
        var tb = new TextBlock { FontFamily = EmojiFont, FontSize = 14, Foreground = BodyBrush, TextWrapping = TextWrapping.Wrap };
        FillInlines(tb, text);
        return tb;
    }

    private static void FillInlines(TextBlock tb, string text)
    {
        var parts = Regex.Split(text, @"(\*\*.*?\*\*|`[^`]+`)");
        foreach (var part in parts)
        {
            if (part.StartsWith("**") && part.EndsWith("**"))
                tb.Inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeights.Bold, Foreground = BlueBrush });
            else if (part.StartsWith("`") && part.EndsWith("`"))
                tb.Inlines.Add(new Run(part[1..^1]) { FontFamily = MonoFont, FontSize = 12.5, Foreground = CodeFg, Background = CodeBg });
            else
                tb.Inlines.Add(new Run(part));
        }
    }

    private static void AddOrderedItem(StackPanel parent, string num, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var t1 = new TextBlock { Text = $"{num}. ", FontFamily = EmojiFont, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = BlueBrush, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 6, 0) };
        var t2 = new TextBlock { FontFamily = EmojiFont, FontSize = 14, Foreground = BodyBrush, TextWrapping = TextWrapping.Wrap };
        FillInlines(t2, text);
        Grid.SetColumn(t1, 0); Grid.SetColumn(t2, 1);
        g.Children.Add(t1); g.Children.Add(t2);
        parent.Children.Add(g);
    }

    private static void AddBullet(StackPanel parent, string text, int indent)
    {
        var g = new Grid { Margin = new Thickness(indent, 2, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = "• ", FontFamily = EmojiFont, FontSize = 14, Foreground = GrayBrush, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 4, 0) });
        var t2 = new TextBlock { FontFamily = EmojiFont, FontSize = 14, Foreground = BodyBrush, TextWrapping = TextWrapping.Wrap };
        FillInlines(t2, text);
        Grid.SetColumn(t2, 1);
        g.Children.Add(t2);
        parent.Children.Add(g);
    }

    private static bool IsTableRow(string l) { var t = l.Trim(); return t.StartsWith("|") && t.EndsWith("|"); }
    private static bool IsTableSep(string l) => Regex.IsMatch(l.Trim(), @"^\|[\s\-:]+\|");
    private static List<string> ParseAligns(string l)
    {
        return l.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim().StartsWith(":") ? "Center" : "Left").ToList();
    }
    private static List<string> ParseCells(string l)
    {
        var t = l.Trim();
        if (t.StartsWith("|")) t = t[1..];
        if (t.EndsWith("|")) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToList();
    }
}
