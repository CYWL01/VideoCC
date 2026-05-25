using System.IO;
using SubtitleMatcher.Infrastructure;
using SubtitleMatcher.Models;

namespace SubtitleMatcher.Services;

/// <summary>
/// 操作历史服务，记录文件操作（复制/移动/重命名）并支持撤销。
/// 历史记录纯内存，不写磁盘。关闭程序后无法撤销。
/// </summary>
public class OperationHistoryService
{
    private readonly ConfigurationManager _config;
    private readonly List<OperationRecord> _history;
    private const int MaxHistory = 100;

    public OperationHistoryService()
    {
        _config = new ConfigurationManager();
        _history = _config.LoadOperationHistory();
    }

    public void Record(string opType, string sourcePath, string targetPath)
    {
        _history.Add(new OperationRecord
        {
            OperationType = opType,
            SourcePath = sourcePath,
            TargetPath = targetPath,
            Timestamp = DateTime.Now,
            CanUndo = true
        });

        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);

        _config.SaveOperationHistory(_history);
    }

    public bool UndoLast()
    {
        var last = _history.LastOrDefault(r => r.CanUndo);
        if (last == null) return false;

        try
        {
            switch (last.OperationType)
            {
                case "Move" when File.Exists(last.TargetPath):
                    var dir = Path.GetDirectoryName(last.SourcePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.Move(last.TargetPath, last.SourcePath);
                    last.CanUndo = false;
                    _config.SaveOperationHistory(_history);
                    Logger.Log($"撤销移动: {last.TargetPath} → {last.SourcePath}");
                    return true;

                case "Copy":
                case "Rename":
                    if (File.Exists(last.TargetPath))
                    {
                        File.Delete(last.TargetPath);
                        last.CanUndo = false;
                        _config.SaveOperationHistory(_history);
                        Logger.Log($"撤销{last.OperationType}: 已删除 {last.TargetPath}");
                        return true;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"撤销失败 [{last.OperationType}]", ex);
        }

        return false;
    }

    public List<OperationRecord> GetAll() => _history.ToList();

    public void Clear()
    {
        _history.Clear();
        _config.SaveOperationHistory(_history);
    }

    public bool HasUndo => _history.Any(r => r.CanUndo);
}
