using System.IO;

namespace SubtitleMatcher.Infrastructure;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VideoSubtitleMatcher",
        "app.log");

    private static readonly object LockObj = new();

    public static void Log(string message)
    {
        try
        {
            lock (LockObj)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void LogError(string message, Exception ex)
    {
        Log($"ERROR: {message} — {ex.Message}");
    }
}
