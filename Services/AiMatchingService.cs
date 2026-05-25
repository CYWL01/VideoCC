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
        "你是影视领域专家，精通全球影视命名习惯。能识别翻译、缩写、谐音、地区差异。如果不认识的语音就说明不认该语音。只返回 JSON。";

    public static readonly string DefaultSinglePrompt =
        "名称A：{nameA}\n名称B：{nameB}\n\n判断这两个名称是否指向同一部影视作品。\n\n考虑以下情况：\n- 字幕和视频可能是不同的语言\n- 同一作品的不同写法（全称↔简称、官方名↔常用名）\n- 缩写/简写（如 GoT = Game of Thrones）\n- 中文拼音首字母缩写（如 aqgy = 爱情公寓、ql = 权力的游戏）\n- 谐音/音译（如 Leon = 里昂）\n- 不同地区的叫法差异（大陆译名↔台湾译名↔香港译名）\n- 标点符号差异（如 Harry Potter = Harry·Potter）\n\n只要确定指向同一部作品就视为匹配。如果不认识名称的语言，在 reason 中写明「不认识该语言」。\n\n只返回 JSON：{{\"match\": true/false, \"confidence\": 0.0~1.0, \"reason\": \"简短原因\"}}";

    public static readonly string DefaultBatchPrompt =
        "我有以下视频和字幕，请判断哪些视频和字幕属于同一部影视作品。\n\n考虑以下情况：\n- 字幕和视频可能是不同的语言\n- 同一作品的不同写法（全称↔简称、缩写↔完整名）\n- 中文拼音首字母缩写（如 aqgy = 爱情公寓、ql = 权力的游戏）\n- 谐音/音译差异（如 Leon ↔ 里昂）\n- 地区译名差异（大陆↔台湾↔香港）\n- 标点符号差异\n\n视频：\n{videoList}\n\n字幕：\n{subList}\n\n返回 JSON，包含 matches 和 unmatched 两项：\n- matches: 确定匹配的配对，每项含 video(索引), subtitle(索引), confidence(0~1)\n- unmatched: 确定不匹配的视频，每项含 video(索引), reason(简短原因)\n\n格式：{{\"matches\":[{{\"video\":0,\"subtitle\":0,\"confidence\":0.95}}],\"unmatched\":[{{\"video\":1,\"reason\":\"名称完全不同\"}}]}}";

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

    /// <summary>去除 key 中的集号后缀（如 qle01 → ql），用于拼音缩写匹配</summary>
    public static string StripEpisodeSuffix(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        // 去除尾部集号模式：S01E01、E01、-01、_01、[01]、.01、纯数字 等
        var stripped = System.Text.RegularExpressions.Regex.Replace(key, @"[-\._\[\]\s]?(?:[se]\d+[e]\d+|[e]\d+|\d+)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return stripped.Length > 0 ? stripped : key;
    }

    public async Task<List<AiBatchMatch>> BatchCompareNamesAsync(
        List<(string Key, string Display)> videos,
        List<(string Key, string Display)> subtitles,
        CancellationToken ct = default,
        Dictionary<int, string>? unmatchedReasons = null)
    {
        if (videos.Count == 0 || subtitles.Count == 0)
            return new List<AiBatchMatch>();

        var simpleMatches = new List<AiBatchMatch>();
        var remainingVideos = new List<(string Key, string Display, int Index)>();
        var remainingSubs = new List<(string Key, string Display, int Index)>();

        // 生成去除了集号后缀的 key，用于拼音缩写/简写匹配
        var videoCleanKeys = videos.Select(v => (Orig: v.Key, Clean: StripEpisodeSuffix(Normalize(v.Key)))).ToArray();
        var subCleanKeys = subtitles.Select(s => (Orig: s.Key, Clean: StripEpisodeSuffix(Normalize(s.Key)))).ToArray();

        for (int vi = 0; vi < videos.Count; vi++)
        {
            var a = Normalize(videos[vi].Key);
            var aClean = videoCleanKeys[vi].Clean;
            int matchedSub = -1;
            double matchConf = 1.0;

            for (int si = 0; si < subtitles.Count; si++)
            {
                var b = Normalize(subtitles[si].Key);
                // 精确匹配
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                {
                    matchedSub = si;
                    matchConf = 1.0;
                    break;
                }
                // 去除集号后缀后匹配（处理 qle01 ↔ 权力的游戏e01 这种）
                var bClean = subCleanKeys[si].Clean;
                if (aClean.Length >= 2 && bClean.Length >= 2 && string.Equals(aClean, bClean, StringComparison.OrdinalIgnoreCase))
                {
                    matchedSub = si;
                    matchConf = 0.95;
                    break;
                }
            }
            if (matchedSub >= 0)
                simpleMatches.Add(new AiBatchMatch(vi, matchedSub, matchConf));
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
            // 直接把文件路径发给 AI，不做任何 key 处理
            var videoList = string.Join("\n", remainingVideos.Select((v, i) => $"  [{i}] {v.Display}"));
            var subList = string.Join("\n", remainingSubs.Select((s, i) => $"  [{i}] {s.Display}"));

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
                max_tokens = 2000,
                temperature = 0.3,
            });

            var response = await _http.PostAsync(_config.ApiUrl,
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var llmMatches = ParseBatchResponse(json, unmatchedReasons);

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

    private static List<AiBatchMatch> ParseBatchResponse(string json, Dictionary<int, string>? unmatchedReasons = null)
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

                    // 解析 matches
                    if (resultDoc.RootElement.TryGetProperty("matches", out var arr))
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            var v = item.GetProperty("video").GetInt32();
                            var s = item.GetProperty("subtitle").GetInt32();
                            var conf = item.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5;
                            var reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                            matches.Add(new AiBatchMatch(v, s, conf, reason));
                        }
                    }

                    // 解析 unmatched 原因
                    if (unmatchedReasons != null && resultDoc.RootElement.TryGetProperty("unmatched", out var unmatched))
                    {
                        foreach (var item in unmatched.EnumerateArray())
                        {
                            var v = item.GetProperty("video").GetInt32();
                            var reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(reason))
                                unmatchedReasons[v] = reason;
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
public record AiBatchMatch(int VideoIndex, int SubtitleIndex, double Confidence, string Reason = "");
