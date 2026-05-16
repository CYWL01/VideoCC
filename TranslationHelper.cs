using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SubtitleMatcher
{
    internal static class TranslationHelper
    {
        private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> FailedEndpoints = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly object EndpointLock = new();

        private static readonly string[] DefaultEndpoints =
        [
            "https://libretranslate.de/translate",
            "https://translate.faucet.dev/translate",
        ];

        private static string[] _endpoints = [.. DefaultEndpoints];
        private static string? _customEndpoint;

        public static string[] CurrentEndpoints
        {
            get
            {
                lock (EndpointLock)
                {
                    return [.. _endpoints];
                }
            }
        }

        public static string? CustomEndpoint
        {
            get
            {
                lock (EndpointLock)
                {
                    return _customEndpoint;
                }
            }
        }

        public static void SetCustomEndpoint(string? url)
        {
            lock (EndpointLock)
            {
                var trimmed = url?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    _customEndpoint = null;
                    _endpoints = [.. DefaultEndpoints];
                }
                else
                {
                    if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        trimmed = "https://" + trimmed;
                    }

                    _customEndpoint = trimmed;
                    _endpoints = [trimmed];
                }

                FailedEndpoints.Clear();
            }
        }

        public static void ResetToDefaults()
        {
            SetCustomEndpoint(null);
        }

        public static string? Translate(string text, string source, string target)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var cacheKey = $"{source}:{target}:{text}";
            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;

            string[] currentEndpoints;
            lock (EndpointLock)
            {
                currentEndpoints = [.. _endpoints];
            }

            foreach (var endpoint in currentEndpoints)
            {
                if (FailedEndpoints.ContainsKey(endpoint))
                    continue;

                try
                {
                    var payload = new { q = text, source, target };
                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = HttpClient.PostAsync(endpoint, content).GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode)
                    {
                        var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        using var doc = JsonDocument.Parse(responseText);
                        var translated = doc.RootElement.GetProperty("translatedText").GetString();

                        if (!string.IsNullOrEmpty(translated))
                        {
                            Cache.TryAdd(cacheKey, translated);
                            return translated;
                        }
                    }
                }
                catch
                {
                    FailedEndpoints.TryAdd(endpoint, true);
                }
            }

            Cache.TryAdd(cacheKey, null);
            return null;
        }

        public static string DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "en";

            var cjkCount = 0;
            var hiraganaCount = 0;
            var katakanaCount = 0;
            var hangulCount = 0;
            var totalLetters = 0;

            foreach (var c in text)
            {
                if (char.IsLetter(c))
                {
                    totalLetters++;
                    if (c >= 0x4E00 && c <= 0x9FFF) cjkCount++;
                    else if (c >= 0x3040 && c <= 0x309F) hiraganaCount++;
                    else if (c >= 0x30A0 && c <= 0x30FF) katakanaCount++;
                    else if (c >= 0xAC00 && c <= 0xD7AF) hangulCount++;
                }
            }

            if (totalLetters == 0)
                return "en";

            if (hiraganaCount + katakanaCount > 0)
                return "ja";

            if (hangulCount > 0)
                return "ko";

            if ((double)cjkCount / totalLetters > 0.3)
                return "zh";

            return "en";
        }

        public static string? TranslateToEnglish(string text)
        {
            var lang = DetectLanguage(text);
            return lang == "en" ? text : Translate(text, lang, "en");
        }
    }
}
