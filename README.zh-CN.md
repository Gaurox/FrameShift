# FrameShift 简体中文说明

🌐 [English](README.md) | **简体中文** | [Español](README.es.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Bahasa Indonesia](README.id.md)

FrameShift 是面向 Windows 10 / 11（x64）的本地媒体工具，使用 C#、.NET 8 和 WinForms，基于 FFmpeg、FFprobe 及可选的本地 AI 模型处理视频、音频、图像和 PDF。

项目作者及维护者为 [Gaurox](https://github.com/Gaurox/FrameShift)，项目采用 [GPL-3.0 许可](LICENSE)，第三方组件与模型的声明见[第三方声明](THIRD_PARTY_NOTICES.md)。

**特别感谢 [zhazhayu01](https://github.com/zhazhayu01)！** 他在 [FrameShift 简体中文 fork](https://github.com/zhazhayu01/FrameShift) 中提供了简体中文界面、提示、安装器及中文 README，并修复了窄窗口下搜索框和操作按钮超出可见边界的布局问题。中文翻译与该布局修复已纳入 1.22.0 版本。

本说明尽可能沿用他的[中文 README 原文](https://github.com/zhazhayu01/FrameShift/blob/f078b0c3490ae43abba807bba24692df8ef446da/README.zh-CN.md)，并根据当前版本更新了发布状态、语言选择和安装器构建说明。原始贡献为提交 [`f078b0c`](https://github.com/zhazhayu01/FrameShift/commit/f078b0c3490ae43abba807bba24692df8ef446da)，基于 FrameShift 1.21.1。感谢他为中文用户所做的翻译、文档与界面改进。

<p align="center">
  <img src="screenshots/Gif_demos/demo_menus_gif.gif" alt="FrameShift 资源管理器右键菜单操作演示" width="900" />
</p>

## 下载与版本状态

**[下载当前公开发行版](https://github.com/Gaurox/FrameShift/releases/latest)**。

当前公开版本为 **[1.23.0](https://github.com/Gaurox/FrameShift/releases/tag/v1.23.0)**，支持英文、简体中文、西班牙文、巴西葡萄牙文和俄文。安装包为自包含的 `win-x64` 程序，最终用户无须单独安装 .NET。

本地 **1.24.0** 安装包已由项目维护者验收，正在准备公开发布。本说明中的新功能和八种语言对应这一版本：新增主要用于观看视频的**均衡音量**，加入法语、日语和印尼语，提供更便于参与的 CSV 翻译流程，并修复更新安装时所选语言未应用到程序窗口的问题。详见 [1.24.0 版本说明](docs/RELEASE_NOTES_1.24.0.md)和[验收记录](docs/RELEASE_QUALIFICATION_1.24.0.md)。

1.23.0 在 Cut Video 中加入 **Find Loop**，支持查找自然循环、预览接缝和创建往返循环，并修复经典视频右键菜单中部分已安装操作被隐藏的问题。当前变更与验证范围见 [1.23.0 版本说明](docs/RELEASE_NOTES_1.23.0.md)和[验证记录](docs/RELEASE_QUALIFICATION_1.23.0.md)。

## 快速使用

1. 打开 FrameShift，将视频、音频、图像或 PDF 拖入主窗口，也可以点击“浏览”或“添加”。
2. 在文件队列中选择要处理的文件，再选择适用的操作；不选择具体文件时，操作按队列中的适用文件执行。
3. 调整参数或预览效果，开始处理。
4. 在源文件所在文件夹查看输出。输出使用唯一命名，避免覆盖原文件和已有结果。

通过安装器完成资源管理器集成后，也可以右键点击文件，从 FrameShift 菜单启动相应操作。开发时直接运行程序不会代替安装器配置右键菜单。主窗口支持批量添加文件，能绕开资源管理器多文件右键菜单的数量限制；每个操作仍受其支持的文件类型与数量要求约束。

## 语言选择与界面

**1.24.0** 的应用程序、安装器及资源管理器菜单支持 **English**、**简体中文**、**Español**、**Português (Brasil)**、**Русский**、**Français**、**日本語** 和 **Bahasa Indonesia** 八种语言，其中法语、日语和印尼语为本次新增。

安装、更新或重新安装时选择的语言会在**下次启动程序时**应用到窗口，每次安装对每个 Windows 用户应用一次，即使此前保存了其他语言，也会正确更新；其他设置会保留。之后仍可在“设置”的“应用程序语言”中另选语言，重启后生效，并自动更新已安装的资源管理器菜单。更新所有用户共用的菜单时，Windows 可能请求管理员授权；这些菜单的语言更改会影响所有账户。若取消授权或菜单更新失败，“设置”会显示提示，已保存的应用程序语言不受影响。

新的翻译流程不要求贡献者编写代码：维护者提供 UTF-8 CSV，其中包含英文原文、上下文及译文。贡献者只编辑 **Translation** 列，保留变量和其他参考列，然后返回文件。离线维护工具支持导出、检查、变更审阅及带备份的预览和导入。参见英文版[翻译参与指南](docs/TRANSLATING.md)和[维护流程](docs/TRANSLATION_MAINTENANCE.md)。

本版本保留了 zhazhayu01 的窄窗口布局修复：明确操作列表的列宽，并为垂直滚动条预留空间，避免搜索框或按钮超出可见边界。360 和 580 像素宽度的布局回归测试通过；不同显示器及实际 DPI 缩放的验证范围见版本验证记录。

## 主要功能

| 类型 | 功能 |
| --- | --- |
| 视频 | 格式转换、移除或提取音轨、提取帧、创建 GIF、合并视频、添加可选字幕轨或烧录字幕、截取、Find Loop 循环查找与预览、裁剪、旋转与翻转、调整尺寸、压缩、均衡音量、调整速度、插帧、查看媒体信息。 |
| 音频 | 格式转换、截取、倒放、压缩、均衡音量、调整音高与速度、降噪。 |
| 图像与 PDF | 图像转换与压缩、多尺寸图标导出、裁剪、旋转与翻转、调整尺寸、图像排版并导出 PDF。 |
| 本地 AI | 移除背景、移除物体、图像和视频超分辨率、RIFE 视频插帧、音轨分离、音频和视频音轨降噪、Whisper 生成字幕、从图像和 PDF 提取文字。 |

合并视频在媒体流特征兼容时直接拼接，否则对支持的 SDR 输入进行 H.264/AAC MP4 归一化；当前工作流拒绝不支持的 HDR 混合或归一化。具体格式、参数和可用预览以相应窗口为准。

字幕可导出标准 SRT、高级 ASS，或保留逐词时间的 FrameShift 字幕项目。文字提取支持 TXT、JSON 和可搜索 PDF，并提供 PDF 页面范围、原生文字复用与渲染质量选项。

### 均衡音量

主要面向电影等视频的观看：减小低声对白与高声动作场景或音乐之间的音量差，减少观看时反复调节音量的需要。也适用于独立音频文件，处理整个混音及所有音轨。

提供**轻度**、**中度**（默认）和**强效 / 夜间模式**三档强度。FFmpeg 在本地单次处理，只重新编码音频，**视频直接复制，不重新编码**。保留声道布局、字幕、章节和元数据，输出使用唯一名称保存在原文件旁。详见[功能说明](docs/BALANCE_VOLUME.md)。

## 本地 AI 与模型

媒体处理在本机完成，不需要上传源文件。可自动下载的 AI 模型会在首次使用时按需下载并进行完整性检查；准备好模型后，相应处理可以离线运行。模型不随安装器打包。部分工作流可使用 DirectML 加速，并在支持的情况下回退至 CPU；模型和硬件不同，速度与资源占用也不同。

在主窗口打开“设置”，可以选择 AI 模型文件夹、恢复默认目录或打开当前模型文件夹，也可以切换浅色、深色及跟随系统主题。模型目录需要是专用的普通文件夹，不能使用驱动器根目录、系统目录或安装目录等受限制位置。

BRIA RMBG-2.0 的均衡与高质量模式需要用户自行从 [BRIA 官方模型页面](https://huggingface.co/briaai/RMBG-2.0/tree/main)取得对应模型文件，放入界面提示的模型目录。FrameShift 不打包、自动下载或重新分发这些模型；它们使用 **CC BY-NC 4.0，限非商业用途**。其他模型的适用许可也应查看[第三方声明](THIRD_PARTY_NOTICES.md)。

## 源码开发

请在 Windows 上安装 **.NET SDK 8.0.427**。仓库的 [global.json](global.json) 锁定该版本，禁用自动滚动至其他 SDK；仅安装较新 SDK 不能替代所要求的版本。

在仓库根目录使用 PowerShell 执行：

```powershell
dotnet --version
dotnet restore .\src\FrameShift\FrameShift.csproj --locked-mode
dotnet restore .\src\FrameShift.SubtitlesWorker\FrameShift.SubtitlesWorker.csproj --locked-mode
dotnet restore .\tests\FrameShift.Tests\FrameShift.Tests.csproj --locked-mode
dotnet test .\tests\FrameShift.Tests\FrameShift.Tests.csproj --no-restore
dotnet run --project .\src\FrameShift\FrameShift.csproj
```

`dotnet --version` 应显示 `8.0.427`。上述测试和运行命令使用默认开发配置；它们不生成可分发安装包。依赖还原需要访问配置的包源，应用运行还依赖项目中配置的 FFmpeg、FFprobe 和原生资源。

直接从源码启动时，若没有保存的语言偏好或安装语言文件，应用程序默认使用英文。可在“设置”中选择简体中文，然后重新启动。

## 构建安装器与许可

Release 构建使用 ImageSharp 4，需要有效且私密的 `sixlabors.lic`。维护者应先确认许可证仍然有效，通过 `SixLaborsLicenseFile` 提供文件路径；不得提交许可证、输出密钥内容，或将许可证放入安装包。不得跳过许可验证，也不得为了构建而降级至存在已知漏洞的 ImageSharp 版本。最终用户不需要自行提供此构建许可证。

1.22.0 安装包已使用 **Inno Setup 6.7.1** 构建验证。简体中文语言文件随仓库保存在 [`installer/Languages/ChineseSimplified.isl`](installer/Languages/ChineseSimplified.isl)，安装脚本直接引用该文件，**无须修改编译器安装目录或另行下载中文文件**。该文件来自 Inno Setup 官方源码仓库，固定来源、原始译者与许可见[语言文件说明](installer/Languages/README.md)。

唯一正式发布入口是仓库根目录的 `build_installer.ps1`：

```powershell
$env:SixLaborsLicenseFile = 'C:\受保护目录\sixlabors.lic'
.\build_installer.ps1
```

示例中的许可证路径需要替换为实际的私密文件路径。如果脚本找不到编译器，可显式指定：

```powershell
.\build_installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

该入口检查发布输入、版本和许可配置，还原锁定依赖，运行必需的 Release 测试，发布自包含的 `win-x64` 程序，并编译安装器。必需步骤失败时应先解决原因，不应绕过检查。源码开发命令、上游发行版和本地安装器构建结果应分别对待。

构建安装包不会自动将其上传至 GitHub，也不会自动安装；运行安装器需要单独操作。

## 项目文档

- [英文项目说明](README.md)：完整功能介绍、截图和历史版本信息。
- [1.24.0 版本说明](docs/RELEASE_NOTES_1.24.0.md)和[验收记录](docs/RELEASE_QUALIFICATION_1.24.0.md)：均衡音量、三种新语言及安装语言修复。
- [均衡音量说明](docs/BALANCE_VOLUME.md)和[翻译参与指南](docs/TRANSLATING.md)。
- [1.23.0 版本说明](docs/RELEASE_NOTES_1.23.0.md)和[验证记录](docs/RELEASE_QUALIFICATION_1.23.0.md)：循环视频、右键菜单修复及验证范围。
- [本地化维护说明](docs/LOCALIZATION.md)：翻译来源、语言选择与资源维护。
- [1.22 与 1.23 分支安排](docs/RELEASE_PLAN_1.22_1.23.md)：通用修复与循环视频功能的分离。
- [zhazhayu01 原 fork 的本地化台账](https://github.com/zhazhayu01/FrameShift/blob/f078b0c3490ae43abba807bba24692df8ef446da/docs/LOCALIZATION_ZH_CN.md)：该贡献在 1.21.1 基线上的修改范围、术语及历史验证记录，不代表本分支 1.22.0 的验证结果。
- [文字提取功能说明](docs/EXTRACT_TEXT.md)。
- [文档索引](docs/README.md)。
- [第三方组件与模型声明](THIRD_PARTY_NOTICES.md)。
- [项目许可证原文](LICENSE)：项目采用 GNU GPL v3.0。

历史文档及许可证保留其原文；本说明不替代许可证条款，也不将尚未完成的构建或测试视为已经通过。
