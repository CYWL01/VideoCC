using System.IO;
using System.Windows;
using System.Windows.Documents;
using SubtitleMatcher.Helpers;

namespace SubtitleMatcher;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        LoadLicense();
    }

    private void LoadLicense()
    {
        string license;
        try
        {
            license = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE"));
        }
        catch
        {
            license = @"# 许可协议

**本软件允许个人免费使用、修改和无偿分享。**

## ✅ 允许的行为

- **个人免费使用** — 不限设备，不限数量
- **自由修改源码** — 可根据需要自行修改
- **无偿分享** — 可向他人分享本软件，须保留项目名称和署名

## ❌ 禁止的行为

- **售卖** — 不得售卖本软件或其功能
- **收费分发** — 不得付费打包、网盘收费、有偿代装
- **盈利** — 任何形式的盈利性收费均不允许

## 🔌 插件 / 集成

- 本软件可以作为插件或组件集成到其他软件中
- 集成后，其功能仍必须**向所有用户免费开放**，不得因此收费或限制功能

## 📄 其他

- 传播时须保留项目名称 **VideoCC** 和署名 **CYWL01**
- 本软件按「现状」提供，使用风险由使用者自行承担

---

**版本：** 2.0 **技术栈：** .NET 8 WPF **名称：** VideoCC 视频字幕匹配";
        }

        DocViewer.Document = MarkdownHelper.Render(license, "许可协议");
        DocViewer.Document.PagePadding = new Thickness(0);
    }
}
