using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SubtitleMatcher.Models;

namespace SubtitleMatcher.Infrastructure;

public class ConfigurationManager
{
    // 配置存储在 NTFS 交替数据流中，附着在 EXE 自身
    // 不产生额外文件、不改写 EXE 二进制、不碰注册表和 AppData
    private static string ConfigPath =>
        (Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/') + ".exe")
        + ":config";

    public AiConfig LoadAiConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AiConfig();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AiConfig>(json) ?? new AiConfig();
        }
        catch
        {
            return new AiConfig();
        }
    }

    public void SaveAiConfig(AiConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"保存配置失败: {ex.Message}");
        }
    }

    // 操作历史纯内存
    public List<OperationRecord> LoadOperationHistory() => new();
    public void SaveOperationHistory(List<OperationRecord> history) { }
}
