using System.Windows;
using SubtitleMatcher.Infrastructure;

namespace SubtitleMatcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Log("应用程序启动");

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.LogError("未处理异常", args.Exception);
            MessageBox.Show($"发生错误: {args.Exception.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log("应用程序退出");
        base.OnExit(e);
    }
}
