# VideoCC 视频字幕匹配

自动匹配视频文件和字幕文件的 Windows 桌面工具，支持文件名评分匹配和 AI 跨语言智能匹配。

## 功能特色

- **双目录扫描** — 分别选择视频目录和字幕目录，递归扫描所有子文件夹
- **文件名评分匹配** — 14 种集号提取模式，评分系统自动匹配（满分约 255 分）
- **AI 批量匹配** — 支持 OpenAI 兼容 API，跨语言/缩写/拼音缩写/谐音/地区译名智能配对
- **三种操作模式** — 复制 / 移动 / 重命名，自动将字幕匹配到视频目录
- **单步撤销** — 每次操作可单独撤销
- **自定义 AI 提示词** — 可查看和修改发送给 AI 的提示词
- **纯单文件运行** — 配置嵌入 EXE 自身（NTFS 数据流），无额外文件

## 快速开始

### 下载

从 [Releases](https://github.com/CYWL01/VideoCC/releases) 下载最新版 `VideoCC.exe`，双击运行。

### 使用

1. 点击「📁 浏览」选择视频文件夹和字幕文件夹（也支持拖拽）
2. 点击「🔍 扫描」自动匹配
3. 勾选要执行的行，点击「▶️ 执行」
4. 如需 AI 匹配：点击「🤖 AI 关」→「⚙️ 配置」填入 API 信息 → 重新扫描

## 匹配流程

### 第一轮：文件名评分匹配（自动）

从文件名提取集号，对同一集的视频和字幕评分：

| 项目 | 满分 | 说明 |
|------|------|------|
| 基础分 | 100 | 集号相同 |
| 季数加分 | 35 | 双方都有季号+35，一方有+10，都无+5 |
| 剧名匹配 | 120 | 文件名剧名一致最高分 |

总分 = 100 + 季数分(0~35) + 剧名分(8~120)，最高约 255 分。

### 第二轮：AI 批量匹配（可选）

未匹配的视频按集分组，每组一次 API 调用发给 AI 配对：

- **置信度 ≥ 0.5** → 自动匹配为「AI 智能匹配」
- **0.3 ≤ 置信度 < 0.5** → 标记为「⚠ 疑似匹配」（黄色高亮）

AI 能识别：跨语言翻译、缩写/简写、中文拼音首字母缩写、谐音/音译、地区译名差异、标点符号差异。

## 集号格式

支持 14 种方式提取集号（13 种正则 + 中文数字解析）：

`S01E02`、`Season 1 E2`、`E12`、`第12集`、`第12话`、`第一集`、`[12]`、`-12-`、`_12_`、`.12.`、` 12.`、行首数字、行尾数字、纯数字

## 非剧集过滤

以下标签的文件不参与匹配（共 26 种）：

```
NCED NCOP PV PVED MENU CM SPECIAL TRAILER PREVIEW SAMPLE
OP ED OPENING ENDING NC CREDITLESS
INTERVIEW MAKING BEHIND TEASER RECAP
EXTRA BONUS OVA OAD SP
```

## 支持的文件格式

| 类型 | 格式 |
|------|------|
| 视频 | `.mp4` `.mkv` `.avi` `.wmv` `.mov` `.flv` `.webm` |
| 字幕 | `.srt` `.ass` `.ssa` `.sub` `.sup` `.txt` |

## 操作模式

| 模式 | 行为 | 原文件 |
|------|------|--------|
| **复制**（默认） | 复制字幕到视频目录 | 保留 |
| **移动** | 移动字幕到视频目录 | 删除 |
| **重命名** | 复制并重命名为视频同名 | 保留 |

## AI 配置

支持任何 OpenAI 兼容 API：

| 服务 | 地址 |
|------|------|
| OpenAI | `https://api.openai.com/v1/chat/completions` |
| 火山引擎（豆包） | `https://ark.cn-beijing.volces.com/api/v3/chat/completions` |
| DeepSeek | `https://api.deepseek.com/v1/chat/completions` |
| 通义千问（阿里云） | `https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions` |
| Ollama（本地） | `http://localhost:11434/v1/chat/completions` |
| LM Studio（本地） | `http://localhost:1234/v1/chat/completions` |

配置存储在 EXE 文件自身的 NTFS 交替数据流中，无额外文件。

## 构建

### 环境要求

- .NET 8 SDK（win-x64）
- Windows（WPF 依赖）

### 编译

```bash
git clone https://github.com/CYWL01/VideoCC.git
cd VideoCC
dotnet build -c Release
```

### 发布单文件

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  --output ./publish
```

输出：`./publish/VideoCC.exe`（约 60-80 MB）

## 许可协议

本软件允许个人免费使用、修改和无偿分享。

**允许：** 个人免费使用、自由修改源码、无偿分享（须保留项目名称和署名）
**禁止：** 售卖、付费打包、网盘收费、有偿代装、任何形式的盈利性收费
**集成：** 可作为插件集成到其他软件，但其功能必须向所有用户免费开放

详细许可见 [LICENSE](LICENSE)。

## 技术栈

- .NET 8 WPF
- 零外部 NuGet 依赖
- 纯单文件发布（SelfContained + PublishSingleFile）

---

**作者：** CYWL01
**版本：** 2.0.0
<img width="1920" height="1531" alt="微信图片_2026-05-26_022256_862" src="https://github.com/user-attachments/assets/831a1ebf-03ce-4765-80e6-5c46fcfa77b2" />
