## Why

用户需要一个与现有 USB Web Launcher 使用相同桌面网页承载技术、但不包含其银行 USB Key、PIN、导出和更新等业务功能的独立 Windows 程序。该程序应提供一个固定、直接的入口，让用户双击 EXE 后即可在应用窗口中访问华发 FSSC 网站。

## What Changes

- 新建一个 Windows x64 桌面应用，使用 WPF 和 Microsoft Edge WebView2 承载网页。
- 应用启动后立即导航到固定地址 `https://fssc-w.cnhuafas.com/OSPApplication/application.html`。
- 窗口只呈现目标网站，不提供地址栏、导航工具栏、设置页或额外业务操作。
- 以自包含的发布目录交付，用户通过其中的 `FSSCWebLauncher.exe` 启动应用，并保留同目录运行依赖。
- 当 WebView2 无法初始化或页面无法开始加载时，提供最小且可理解的错误提示，避免静默退出。
- 不复制参考工程中的 USB 设备检测、PIN 管理、本机认证代理、浏览器扩展、网页自动化、文件导出、更新器或安装器功能。

## Capabilities

### New Capabilities

- `fssc-webview-launcher`: 定义固定网址桌面外壳的启动、网页承载、窗口行为、错误反馈及发布边界。

### Modified Capabilities

无。

## Impact

- 在当前空工作区中新增独立的 .NET/WPF 工程及对应测试。
- 引入 `Microsoft.Web.WebView2` NuGet 依赖，目标平台为 Windows x64。
- 构建使用本机已有的 .NET SDK 10.0.100，应用目标框架保持为 .NET 8 Windows。
- 运行机器需要可用的 Microsoft Edge WebView2 Evergreen Runtime；应用发布目录携带 .NET 运行时，但不捆绑浏览器运行时。
- 不修改 `F:\USB-Web-Launcher` 参考工程及其发布产物。
