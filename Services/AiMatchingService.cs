using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SubtitleMatcher.Infrastructure;
using SubtitleMatcher.Models;

namespace SubtitleMatcher.Services;

public class AiMatchingService
{
    private readonly HttpClient _http;
    private AiConfig _config;

    // ── 默认提示词（用户未自定义时使用）──
    public static readonly string DefaultSystemPrompt =
        "你是影视领域专家，精通全球影视命名习惯。能识别翻译、缩写、谐音、地区差异。只返回 JSON。";

    public static readonly string DefaultSinglePrompt =
        "名称A：{nameA}\n名称B：{nameB}\n\n判断这两个名称是否指向同一部影视作品。\n\n考虑以下情况：\n- 不同语言的翻译（中↔英↔日↔韩↔法↔德↔俄↔西↔葡等）\n- 同一作品的不同写法（全称↔简称、官方名↔常用名）\n- 缩写/简写（如 GoT = Game of Thrones）\n- 中文拼音首字母缩写（如 QL = 权力的游戏、ZHC = 甄嬛传）\n- 谐音/音译（如 Leon = 里昂）\n- 不同地区的叫法差异（大陆译名↔台湾译名↔香港译名）\n- 标点符号差异（如 Harry Potter = Harry·Potter）\n\n只要确定指向同一部作品就视为匹配。\n\n只返回 JSON：{{\"match\": true/false, \"confidence\": 0.0~1.0, \"reason\": \"简短原因\"}}";

    public static readonly string DefaultBatchPrompt =
        "我有以下视频和字幕，请判断哪些视频和字幕属于同一部影视作品。\n\n考虑以下情况：\n- 不同语言的翻译（中↔英↔日↔韩↔法↔德↔俄↔西↔葡等）\n- 同一作品的不同写法（全称↔简称、缩写↔完整名）\n- 中文拼音首字母缩写（如 QL = 权力的游戏）\n- 谐音/音译差异（如 Leon ↔ 里昂）\n- 地区译名差异（大陆↔台湾↔香港）\n- 标点符号差异\n\n只要确定指向同一部作品就匹配。\n\n视频：\n{videoList}\n\n字幕：\n{subList}\n\n只返回 JSON。key 用 matches，每项用 video(索引数字), subtitle(索引数字), confidence(0~1小数)：\n{{\"matches\":[{{\"video\":0,\"subtitle\":0,\"confidence\":0.95}}]}}";

    public AiMatchingService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _config = new AiConfig();
    }

    public void UpdateConfig(AiConfig config)
    {
        _config = config;
        _http.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(_config.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
    }

    public async Task<AiMatchResult> CompareNamesAsync(string nameA, string nameB, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB))
            return new AiMatchResult(false, 0, "名称为空");

        var a = Normalize(nameA);
        var b = Normalize(nameB);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return new AiMatchResult(true, 1.0, "字符串完全匹配");

        return await LlmCompareAsync(nameA, nameB, ct);
    }

    private async Task<AiMatchResult> LlmCompareAsync(string nameA, string nameB, CancellationToken ct)
    {
        try
        {
            var system = _config.SystemPrompt ?? DefaultSystemPrompt;
            var template = _config.SinglePromptTemplate ?? DefaultSinglePrompt;
            var prompt = template.Replace("{nameA}", nameA).Replace("{nameB}", nameB);

            var body = JsonSerializer.Serialize(new
            {
                model = _config.Model,
                messages = new[] {
                    new { role = "system", content = system },
                    new { role = "user", content = prompt }
                },
                max_tokens = 300,
                temperature = 0.3,
            });

            var response = await _http.PostAsync(_config.ApiUrl,
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseLlmResponse(json);
        }
        catch (Exception ex)
        {
            Logger.LogError("AI 匹配请求失败", ex);
            return new AiMatchResult(false, 0, $"请求失败: {ex.Message}");
        }
    }

    private static AiMatchResult ParseLlmResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (content != null)
                {
                    var resultDoc = JsonDocument.Parse(content);
                    var match = resultDoc.RootElement.GetProperty("match").GetBoolean();
                    var confidence = resultDoc.RootElement.GetProperty("confidence").GetDouble();
                    var reason = resultDoc.RootElement.TryGetProperty("reason", out var r)
                        ? r.GetString() ?? "" : "";
                    return new AiMatchResult(match, confidence, reason);
                }
            }
        }
        catch { }
        return new AiMatchResult(false, 0, "解析响应失败");
    }

    public async Task<List<AiBatchMatch>> BatchCompareNamesAsync(
        List<(string Key, string Display)> videos,
        List<(string Key, string Display)> subtitles,
        CancellationToken ct = default)
    {
        if (videos.Count == 0 || subtitles.Count == 0)
            return new List<AiBatchMatch>();

        var simpleMatches = new List<AiBatchMatch>();
        var remainingVideos = new List<(string Key, string Display, int Index)>();
        var remainingSubs = new List<(string Key, string Display, int Index)>();

        for (int vi = 0; vi < videos.Count; vi++)
        {
            var a = Normalize(videos[vi].Key);
            int matchedSub = -1;
            for (int si = 0; si < subtitles.Count; si++)
            {
                var b = Normalize(subtitles[si].Key);
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                {
                    matchedSub = si;
                    break;
                }
            }
            if (matchedSub >= 0)
                simpleMatches.Add(new AiBatchMatch(vi, matchedSub, 1.0));
            else
                remainingVideos.Add((videos[vi].Key, videos[vi].Display, vi));
        }

        var usedSubs = new HashSet<int>(simpleMatches.Select(m => m.SubtitleIndex));
        for (int si = 0; si < subtitles.Count; si++)
            if (!usedSubs.Contains(si))
                remainingSubs.Add((subtitles[si].Key, subtitles[si].Display, si));

        if (remainingVideos.Count == 0 || remainingSubs.Count == 0)
            return simpleMatches;

        try
        {
            var videoList = string.Join("\n", remainingVideos.Select((v, i) => $"  [{i}] {v.Display}{(v.Key != v.Display ? $" ({v.Key})" : "")}"));
            var subList = string.Join("\n", remainingSubs.Select((s, i) => $"  [{i}] {s.Display}{(s.Key != s.Display ? $" ({s.Key})" : "")}"));

            var system = _config.SystemPrompt ?? DefaultSystemPrompt;
            var template = _config.BatchPromptTemplate ?? DefaultBatchPrompt;
            var prompt = template.Replace("{videoList}", videoList).Replace("{subList}", subList);

            var body = JsonSerializer.Serialize(new
            {
                model = _config.Model,
                messages = new[] {
                    new { role = "system", content = system },
                    new { role = "user", content = prompt }
                },
                max_tokens = 500,
                temperature = 0.3,
            });

            var response = await _http.PostAsync(_config.ApiUrl,
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var llmMatches = ParseBatchResponse(json);

            foreach (var m in llmMatches)
            {
                if (m.VideoIndex >= 0 && m.VideoIndex < remainingVideos.Count &&
                    m.SubtitleIndex >= 0 && m.SubtitleIndex < remainingSubs.Count &&
                    !usedSubs.Contains(remainingSubs[m.SubtitleIndex].Index))
                {
                    simpleMatches.Add(new AiBatchMatch(
                        remainingVideos[m.VideoIndex].Index,
                        remainingSubs[m.SubtitleIndex].Index,
                        m.Confidence));
                    usedSubs.Add(remainingSubs[m.SubtitleIndex].Index);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("AI 批量匹配请求失败", ex);
        }

        return simpleMatches;
    }

    private static List<AiBatchMatch> ParseBatchResponse(string json)
    {
        var matches = new List<AiBatchMatch>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (content != null)
                {
                    var resultDoc = JsonDocument.Parse(content);
                    if (resultDoc.RootElement.TryGetProperty("matches", out var arr))
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            var v = item.GetProperty("video").GetInt32();
                            var s = item.GetProperty("subtitle").GetInt32();
                            var conf = item.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5;
                            matches.Add(new AiBatchMatch(v, s, conf));
                        }
                    }
                }
            }
        }
        catch { }
        return matches;
    }

    private static string Normalize(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[\s\-_\.]+", "");

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await CompareNamesAsync("测试", "test", ct);
            return result.Confidence > 0;
        }
        catch { return false; }
    }
}

public record AiMatchResult(bool IsMatch, double Confidence, string Reason);
public record AiBatchMatch(int VideoIndex, int SubtitleIndex, double Confidence);
