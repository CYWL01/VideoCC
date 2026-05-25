using System.Text.RegularExpressions;

namespace SubtitleMatcher.Helpers;

public class ChineseNumberParser
{
    private static readonly Dictionary<char, int> Digits = new()
    {
        {'零',0}, {'一',1}, {'二',2}, {'三',3}, {'四',4},
        {'五',5}, {'六',6}, {'七',7}, {'八',8}, {'九',9},
        {'十',10}, {'百',100}, {'千',1000}
    };

    public int Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        input = input.Trim();

        // 纯数字直接返回
        if (int.TryParse(input, out var n)) return n;

        // 包含"百/千"的复杂数字
        if (input.Any(c => c is '百' or '千'))
        {
            int result = 0, temp = 0;
            foreach (var c in input)
            {
                if (Digits.TryGetValue(c, out var v))
                {
                    if (v >= 10) { temp = Math.Max(temp, 1) * v; result += temp; temp = 0; }
                    else temp += v;
                }
            }
            return result + temp;
        }

        // 十以内的直接数字
        if (input.Length == 1 && Digits.TryGetValue(input[0], out var d) && d <= 10)
            return d;

        // "十二" → 12, "三十五" → 35
        int val = 0;
        foreach (var c in input)
        {
            if (Digits.TryGetValue(c, out var v))
            {
                if (v == 10)
                {
                    if (val == 0) val = 10;
                    else val *= 10;
                }
                else val += v;
            }
        }
        return val > 0 ? val : 0;
    }

    public string ToSeason(string input)
    {
        var n = Parse(input);
        return n > 0 ? $"S{n:D2}" : string.Empty;
    }

    public string ToEpisode(string input)
    {
        var n = Parse(input);
        return n > 0 ? $"E{n:D2}" : string.Empty;
    }
}
