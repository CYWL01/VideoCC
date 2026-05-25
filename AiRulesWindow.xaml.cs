using System.Windows;
using System.Windows.Documents;
using SubtitleMatcher.Helpers;
using SubtitleMatcher.Infrastructure;
using SubtitleMatcher.Services;

namespace SubtitleMatcher;

public partial class AiRulesWindow : Window
{
    private readonly ConfigurationManager _config;

    /// <summary>提示词有变更，调用方应更新 AI 服务</summary>
    public bool PromptsChanged { get; private set; }

    public AiRulesWindow(ConfigurationManager config)
    {
        InitializeComponent();
        _config = config;
        Loaded += (_, _) => { LoadRules(); LoadPrompts(); };
    }

    private void LoadRules()
    {
        var md = @"# AI 匹配规则说明

VideoCC 使用两轮匹配：文件名评分匹配 → AI 批量匹配（需开启 AI）。

---

## 第一轮：文件名评分匹配（自动执行）

程序通过 14 种方式从文件名中提取集号（13 种正则 + 中文数字解析），对同一集的视频和字幕进行评分。

### 评分公式

`总分 = 基础分(100) + 季数分(5~35) + 剧名分(8~120)`

满分约 255 分。

### 季数加分

| 条件 | 加分 |
|------|------|
| 双方都有季号且一致 | +35 |
| 一方有季号 | +10 |
| 双方都无季号 | +5 |

### 剧名匹配分档

| 条件 | 分数 | 示例 |
|------|------|------|
| 文件名剧名精确匹配 | 120 | `权力的游戏 S01E01` ↔ `权力的游戏 S01E01` |
| 目录剧名重叠 | 90 | `剧集A/第一季/01` ↔ `剧集A/S01/01` |
| 文件剧名匹配对方目录剧名 | 85 | — |
| 双方无剧名（纯集数匹配） | 15 | `01.mkv` ↔ `01.srt` |
| 一方有剧名无冲突 | 8 | — |

### 集号提取格式（14 种）

`S01E02`、`Season 1 E2`、`E12`、`第12集`、`第12话`、`[12]`、`-12-`、`_12_`、`.12.`、` 12.`、`.12`、`^12`、`_12$`、`纯数字`

### 非剧集过滤标签（26 种）

```
NCED NCOP PV PVED MENU CM SPECIAL TRAILER PREVIEW SAMPLE
OP ED OPENING ENDING NC CREDITLESS
INTERVIEW MAKING BEHIND TEASER RECAP
EXTRA BONUS OVA OAD SP
```

含这些标签的文件不参与集号提取（OP/ED/预告/花絮等）。

### 歧义跳过规则

- **同集多剧歧义**：同一集存在多部不同剧名的视频时，不自动匹配（防止 A 视频配 B 字幕）
- **低分跳过**：最佳评分 < 120 且同集有多个视频（不区分子目录/压制组）→ 不自动匹配
- **同分歧义**：多个候选字幕分数相同但剧名不同 → 不自动匹配

---

## 第二轮：AI 批量匹配（需手动开启）

### 开启方式

点击顶部 `🤖 AI 关` → `🤖 AI 开` → 点击 `⚙️ 配置` 填入 API 信息（地址 / Key / 模型）→ 扫描

### 配置保存

配置嵌入在 EXE 文件自身（NTFS 数据流），无需任何额外文件。提示词字段也存储在同一位置。

### 匹配流程

1. 文件名评分匹配完成后，收集所有未匹配的视频
2. **有集号的**：按集分组，每组内所有同集视频和字幕一次发给 AI
3. **无集号的**（电影/单文件）：所有无集号视频和字幕一次发给 AI
4. AI 自行判断配对，返回 `{matches: [{video:N, subtitle:N, confidence:N}]}`
5. 置信度 ≥ 0.5 的自动填入匹配结果

### AI 能识别的情况

| 情况 | 示例 | 说明 |
|------|------|------|
| 不同语言翻译 | `爱情故事` ↔ `Love Story` | 中英日韩法德俄西葡等 |
| 缩写/简写 | `Game of Thrones` ↔ `GoT` | 全称缩写 |
| 中文拼音缩写 | `aqgy` ↔ `爱情公寓` | 拼音首字母 |
| 谐音/音译 | `Leon` ↔ `里昂` | 发音接近 |
| 地区译名差异 | `黑客帝国` ↔ `The Matrix` | 大陆/台湾/香港 |
| 标点差异 | `Breaking Bad` ↔ `Breaking.Bad` | 标点忽略 |

### 批量匹配提示词模板

可在「自定义提示词」标签页中查看和修改实际发送给 AI 的提示词。

---

## 测试文件正确匹配关系

测试目录：`VideoSubtitleMatcherTest`（15 视频 + 14 字幕）

### 文件名评分匹配（关闭 AI 也匹配）

| # | 视频 | 字幕 | 场景 |
|---|------|------|------|
| 1 | 权力的游戏 S01E01.mkv | 权力的游戏 S01E01.srt | 精确文件名匹配，评分 120 |
| 2 | 动漫 [01].mkv | 动漫 [01].srt | 中括号集号 `[01]` |
| 3 | 动漫第1集.mkv | 动漫第1集.srt | 中文集号 `第1集` |
| 4 | Show.Name.S01E01.1080p.mkv | Show.Name.S01E01.srt | 技术标记清洗后精确匹配 |
| 5 | Breaking Bad S01E01.mkv | Breaking-Bad_S01E01.srt | 归一化键一致（空格/短横/下划线） |
| 6 | 绝命毒师 S02E01.mkv | 绝命毒师 S02E01.srt | 双方都有季号 S02 |
| 7 | 剧集A/第一季/01.mkv | 剧集A/S01/01.ass | 目录剧名重叠匹配，评分 90 |

### 需开启 AI

| # | 视频 | 字幕 | 场景 |
|---|------|------|------|
| 8 | 爱情故事.mp4 | Love Story.srt | 跨语言翻译 中↔英 |
| 9 | aqgy.mkv | 爱情公寓.srt | 拼音首字母缩写 |
| 10 | ql E01.mkv | 权力的游戏 E01.srt | 拼音首字母缩写 |
| 11 | 权力的游戏E01.mkv | GoT S01E01.srt | 缩写匹配 全称↔GoT |
| 12 | 黑客帝国.mp4 | The Matrix.srt | 跨语言电影 无集号 |

### 歧义场景

| # | 视频 | 结果 | 原因 |
|---|------|------|------|
| 13 | [NCED] OP [1080p].mkv | 无匹配 | NCED 非剧集标记被过滤 |
| 14 | 剧集甲 E01.mkv + 剧集乙 E01.mkv | 各自匹配自身字幕 | 同集两部不同剧，歧义检查阻止交叉匹配但自身匹配正常 |";

        DocViewer.Document = MarkdownHelper.Render(md);
        DocViewer.Document.PagePadding = new Thickness(0);
    }

    private void LoadPrompts()
    {
        var cfg = _config.LoadAiConfig();
        SystemPromptBox.Text = cfg.SystemPrompt ?? AiMatchingService.DefaultSystemPrompt;
        SinglePromptBox.Text = cfg.SinglePromptTemplate ?? AiMatchingService.DefaultSinglePrompt;
        BatchPromptBox.Text = cfg.BatchPromptTemplate ?? AiMatchingService.DefaultBatchPrompt;
    }

    private void SavePromptsBtn_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _config.LoadAiConfig();
        cfg.SystemPrompt = SystemPromptBox.Text.Trim();
        cfg.SinglePromptTemplate = SinglePromptBox.Text.Trim();
        cfg.BatchPromptTemplate = BatchPromptBox.Text.Trim();
        _config.SaveAiConfig(cfg);
        PromptsChanged = true;
        PromptStatus.Text = "✅ 提示词已保存，下次 AI 调用生效";
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        SystemPromptBox.Text = AiMatchingService.DefaultSystemPrompt;
        SinglePromptBox.Text = AiMatchingService.DefaultSinglePrompt;
        BatchPromptBox.Text = AiMatchingService.DefaultBatchPrompt;
        PromptStatus.Text = "🔄 已重置为默认提示词，点击保存生效";
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
