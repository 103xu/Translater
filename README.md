# 划译 · Zotero 桌面翻译

Windows 上的 Zotero PDF 划词翻译工具，使用 DeepSeek API。当前版本为 2.1.0。

## 下载与使用

从 [最新发布页](https://github.com/103xu/Translater/releases/latest) 下载完整程序包，解压后运行 `Translater.exe`。请保留旁边的 `fonts` 文件夹。适用于 Windows 10/11 x64，使用 .NET Framework 4.8。

首次填写自己的 DeepSeek API Key 并保存到托盘。Zotero 在前台时，右下角显示“翻译已开启”。在 PDF 中选中英文并松手，会自动复制选区并发送给 DeepSeek 翻译。剪贴板保留刚选中的英文。

译文显示在半透明便笺中。拖动留白移动窗口，拖动边缘调整大小，右键复制译文或关闭，Esc 收起。翻译失败时可右键重试。本次运行内沿用调整后的位置和大小。

双击托盘图标打开设置，托盘菜单可暂停或退出。升级前请退出旧版，再启动新版。各版本共用单实例锁，旧版先运行时会阻止新版启动。登录 Windows 时启动可在设置中选择。

API Key 存储在当前 Windows 用户的凭据管理器中。使用者自行提供 DeepSeek API 账户，费用按服务商规则计收。

## 构建

```powershell
.\build.ps1
```

输出位于 `dist`。可通过 `-OutputDirectory` 指定输出目录。构建使用系统 .NET Framework 的 C# 编译器，不需要 NuGet。字体和许可证位于 `assets/fonts`，构建时复制到程序旁。

## 版本记录

| 版本 | 内容 | 保存情况 |
| --- | --- | --- |
| [2.1.0](https://github.com/103xu/Translater/releases/tag/v2.1.0) | 自动复制、半透明便笺和翻译提示词优化 | 完整源码及原始程序包 |
| [2.0.1](https://github.com/103xu/Translater/releases/tag/v2.0.1) | 特殊剪贴板格式的提示与手动复制兼容 | 原始程序包及发布记录 |
| [2.0.0](https://github.com/103xu/Translater/releases/tag/v2.0.0) | Zotero 前台提示、黑白界面及字体整理 | 原始程序包及发布记录 |

仓库历史按现存版本归档顺序建立，提交日期为本次归档日期。2.0.0 和 2.0.1 标签对应发布记录，其自动生成的 Source code 压缩包不含应用源码，请下载附带的 Huayi 程序包。早期 1.0 仅余部分测试代码，未作为正式发行版本上传。

## 验证情况

2.1.0 已通过编译、便笺透明度、字号范围、流式显示、位置沿用、关闭与取消处理检查，并通过 DeepSeek 公开测试句检查。桌面全流程划选和拖动验证尚不完整。回归检查源码位于 `tests/Smoke.cs`，其中 `--api` 模式会使用当前用户已保存的密钥调用 DeepSeek。
