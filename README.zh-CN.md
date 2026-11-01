# FrameShift 简体中文说明

FrameShift 是面向 Windows 10/11 x64 的本地媒体工具，基于 C#、.NET 8、WinForms、
FFmpeg 和 FFprobe。媒体处理在本机完成，AI 模型按需下载。

当前公开版本仍为 **1.21.1**。本分支正在准备**尚未发布的 1.22.0 英文／简体中文版本**，
包含通用可靠性修复，不包含 Find Loop；循环视频功能保留到未来的 1.23。

安装程序可选择 English 或 简体中文。安装程序自身、组件说明和资源管理器右键菜单使用
所选语言，并保存应用程序的默认语言。应用程序的“设置”中也可选择语言，在下次启动时
生效；这不会修改需要管理员权限的资源管理器菜单。重新运行安装程序可更改菜单语言。

可将文件拖入主窗口，再选择转换、压缩、截取、裁剪、调整尺寸、提取帧、字幕或 AI 工具。
新文件保存在源文件旁边，自动使用唯一名称，原文件不会被覆盖。AI 模型不包含在安装包中。

中文措辞和窄窗口布局修复来自
[zhazhayu01 的贡献](https://github.com/zhazhayu01/FrameShift/commit/f078b0c3490ae43abba807bba24692df8ef446da)，
在此基础上改为保留英文和中文的资源文件，并补充通用修复的相关提示。原项目作者为
[Gaurox](https://github.com/Gaurox/FrameShift)，继续使用 [GPL-3.0](LICENSE) 许可，
第三方声明保持不变。

构建说明见 [英文 README](README.md)，语言维护说明见
[LOCALIZATION.md](docs/LOCALIZATION.md)，当前验证与未完成事项见
[1.22 验证记录](docs/RELEASE_QUALIFICATION_1.22.0.md)。
