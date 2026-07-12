# Windows 保管库

Windows Vault 是 Vault 产品系列的本机 Windows 成员。它是一个 .NET 8 WPF 应用程序，具有 WebView2 编辑器、本机应用程序清单、执行引擎、自定义规则运行时和本地 Web 应用程序桥接中心。

代码就是产品合同。维护的应用内手册是[src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md)。

## 当前能力

- 选定 Windows 应用程序的默认组和高级策略规则的自定义组。
- 立即、津贴和倒计时模式；时间表；冻结;打瞌睡；和群组导入/导出。
- Windows 应用程序清单和基于窗口的执行组件。
- 由 `src/WindowsBlocker/WebAssets/` 托管的 WebView2 编辑器。
- 通过语法检查和日志源控制自定义规则的执行。
- 用于显式链接的兼容组的环回桥集线器。
- 本机计时器、吐司和面板覆盖窗口。

## 构建

使用签入的解决方案和项目：

```powershell
dotnet build WindowsBlocker.sln
```

应用程序项目以`net8.0-windows`为目标，并使用WPF加WebView2。在 Windows 上构建并运行它，并提供所需的 .NET SDK 和 WebView2 运行时。

## 项目地图

|面积 |源码目录|
| --- | --- |
|集团模型与政策评估| `src/WindowsBlocker/Core/` |
|本土执法 | `src/WindowsBlocker/Enforcement/` |
|应用清单和 WebView 桥接 | `src/WindowsBlocker/WebUI/` |
|自定义规则运行时 | `src/WindowsBlocker/Rules/` |
|桥枢纽| `src/WindowsBlocker/Bridge/` |
| WPF 窗口和覆盖层 | `src/WindowsBlocker/` |

## 文档和翻译

英文文档仍然是规范的。 UI标签使用`src/WindowsBlocker/WebAssets/translation/`中完整的JSON目录；翻译后的手册位于`manual/en.md` 旁边，其余维护文档的翻译副本位于`i18n-docs/<locale>/` 下。
