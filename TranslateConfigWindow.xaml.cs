using System;
using System.Net.Http;
using System.Windows;

namespace SubtitleMatcher
{
    public partial class TranslateConfigWindow : Window
    {
        public TranslateConfigWindow()
        {
            InitializeComponent();
            LoadCurrentConfig();
        }

        private void LoadCurrentConfig()
        {
            var endpoints = TranslationHelper.CurrentEndpoints;
            var custom = TranslationHelper.CustomEndpoint;

            if (MainWindow.IsOnlineTranslationEnabled)
            {
                StatusText.Text = "已开启";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                StatusText.Text = "已关闭";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));
            }

            if (!string.IsNullOrEmpty(custom))
            {
                EndpointText.Text = custom;
                EndpointTextBox.Text = custom;
            }
            else
            {
                EndpointText.Text = "默认服务器：" + string.Join("、", endpoints);
                EndpointTextBox.Text = string.Empty;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var url = EndpointTextBox.Text.Trim();
            TranslationHelper.SetCustomEndpoint(string.IsNullOrWhiteSpace(url) ? null : url);
            LoadCurrentConfig();
            MessageBox.Show("翻译 API 地址已保存", "提示");
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            TranslationHelper.ResetToDefaults();
            LoadCurrentConfig();
            MessageBox.Show("已恢复为默认翻译服务器", "提示");
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            var url = EndpointTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                var defaults = TranslationHelper.CurrentEndpoints;
                if (defaults.Length > 0)
                {
                    url = defaults[0];
                }
                else
                {
                    MessageBox.Show("没有可用的服务器地址，请先填入一个地址", "提示");
                    return;
                }
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            TestButton.IsEnabled = false;
            TestButton.Content = "测试中...";

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var payload = new { q = "Hello", source = "en", target = "zh" };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(responseText);
                    var translated = doc.RootElement.GetProperty("translatedText").GetString();
                    MessageBox.Show($"连接成功！\n\n测试翻译：Hello → {translated}", "测试成功");
                }
                else
                {
                    MessageBox.Show($"服务器返回错误：{(int)response.StatusCode} {response.ReasonPhrase}\n\n请检查地址是否正确。", "测试失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连接失败：{ex.Message}\n\n可能原因：\n1. 服务器地址不正确\n2. 服务器未启动\n3. 网络连接问题", "测试失败");
            }
            finally
            {
                TestButton.IsEnabled = true;
                TestButton.Content = "测试连接";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
