using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace SubtitleMatcher
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            LoadLicenseText();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnLicenseViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            OuterScrollViewer.ScrollToVerticalOffset(OuterScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void LoadLicenseText()
        {
            var licensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE");
            string licenseText;
            if (File.Exists(licensePath))
            {
                var raw = File.ReadAllText(licensePath);
                var lines = raw.Replace("\r\n", "\n").Split('\n');
                var skip = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("## "))
                    {
                        skip = i;
                        break;
                    }
                }
                licenseText = string.Join("\n", lines, skip, lines.Length - skip);
            }
            else
            {
                licenseText = GetFallbackLicenseText();
            }
            LicenseDocumentViewer.Document = MarkdownHelper.RenderMarkdown(licenseText, "");
        }

        private static string GetFallbackLicenseText()
        {
            return """
# 视频字幕自动匹配工具-许可
Copyright © 2026 CYWL01
项目名称：视频字幕自动匹配工具

## 软件说明
本软件为纯离线影音辅助实用工具，全程由人工智能辅助编写完成，无专业商业开发资质，仅面向个人日常休闲、影音整理场景免费使用，非商业正式商用软件。

## 授权使用条例
1. 任何个人与用户均可无条件免费下载、安装、运行、本地正常使用本软件，无功能限制、无激活要求、无使用时长限制。
2. 允许使用者对本软件程序进行二次修改、界面调整、功能精简、结构优化等自主调整操作，修改后可自行闭源留存，无需公开源代码。
3. 允许用户无偿转发、无偿分享、无偿传播原版软件及自行修改后的衍生版本，免费分享行为不受任何限制。
4. **严禁对本软件或字幕匹配功能进行盈利性收费**：禁止将本软件原版、修改版、改版、精简版、整合版等任何相关版本，进行售卖出售、付费打包、网盘收费、有偿代装、有偿定制、商业引流收费、平台付费上架等所有以本软件或本功能获利为目的的行为。
5. 所有版本免费传播过程中，必须保留原项目名称「视频字幕自动匹配工具」与原作者署名「CYWL01」，不得篡改作者信息、冒充原创、冒充官方版本。
6. 允许将本软件或相关字幕匹配功能整合到其他软件或商业软件中，但该字幕匹配功能必须向最终用户免费开放，不得对此功能进行任何直接或间接收费，不得将此功能作为付费权益、会员功能、收费插件、付费模块、商业项目收费条件或其他盈利性交易内容。

## 免责声明
1. 本软件由AI辅助开发制作，仅为简易实用工具，不承诺功能绝对完美、运行绝对稳定，不提供任何官方技术售后与功能维护服务。
2. 用户在使用、修改、传播本软件过程中所产生的一切设备异常、文件异常、数据问题、网络问题及各类使用风险，均由使用者自行全部承担，作者不承担任何直接或间接相关法律责任与经济责任。
3. 本软件仅用于合规合法影音辅助用途，严禁用于违规违法场景使用，违规使用后果由使用者自行承担。
4. 本许可协议拥有最终解释权，一经下载、安装、使用本软件，即代表使用者完全认同并自愿遵守以上全部条款。
""";
        }
    }
}
