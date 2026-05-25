namespace SubtitleMatcher.Models;

public class AiConfig
{
    public string ApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public bool Enabled { get; set; } = false;

    // 自定义提示词（为空则使用代码内置默认值）
    public string? SystemPrompt { get; set; }
    public string? SinglePromptTemplate { get; set; }
    public string? BatchPromptTemplate { get; set; }
}

public class OperationRecord
{
    public string OperationType { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool CanUndo { get; set; } = true;
}
