using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SubtitleMatcher.Models;

namespace SubtitleMatcher;

public partial class AiConfigWindow : Window
{
    public AiConfig Config { get; private set; }

    public AiConfigWindow(AiConfig config)
    {
        InitializeComponent();
        Config = config;

        // 加载现有配置
        ApiUrlBox.Text = config.ApiUrl;
        ApiKeyBox.Password = config.ApiKey;
        ModelBox.Text = config.Model;

        // 解决 FlowDocumentScrollViewer 抢占鼠标滚轮的问题：
        // 滚轮在其区域上时转发给外层 ScrollViewer
        Loaded += (_, _) =>
        {
            foreach (var fds in FindVisualChildren<FlowDocumentScrollViewer>(this))
            {
                fds.PreviewMouseWheel += (s, e) =>
                {
                    var parent = fds.Parent;
                    while (parent != null && parent is not ScrollViewer)
                        parent = (parent as FrameworkElement)?.Parent;
                    if (parent is ScrollViewer sv)
                    {
                        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3);
                        e.Handled = true;
                    }
                };
            }
        };
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child))
                yield return sub;
        }
    }

    private async void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        TestResult.Text = "⏳ 测试中...";
        TestResult.Foreground = (Brush)FindResource("TextLightBrush");

        try
        {
            var testConfig = new AiConfig
            {
                ApiUrl = ApiUrlBox.Text.Trim(),
                ApiKey = ApiKeyBox.Password,
                Model = ModelBox.Text.Trim(),
            };

            var service = new Services.AiMatchingService();
            service.UpdateConfig(testConfig);

            var result = await service.CompareNamesAsync("test", "test");
            if (result.Confidence > 0)
            {
                TestResult.Text = $"✅ 连接成功";
                TestResult.Foreground = (Brush)FindResource("SuccessTextBrush");
            }
            else
            {
                TestResult.Text = "❌ 连接失败";
                TestResult.Foreground = (Brush)FindResource("WarnTextBrush");
            }
        }
        catch (Exception ex)
        {
            TestResult.Text = $"❌ 错误: {ex.Message}";
            TestResult.Foreground = (Brush)FindResource("WarnTextBrush");
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        Config.ApiUrl = ApiUrlBox.Text.Trim();
        Config.ApiKey = ApiKeyBox.Password;
        Config.Model = ModelBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        ApiUrlBox.Text = "";
        ApiKeyBox.Password = "";
        ModelBox.Text = "";
        TestResult.Text = "";
        // 清空配置并保存（写入空值到注册表）
        Config.ApiUrl = "";
        Config.ApiKey = "";
        Config.Model = "";
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
