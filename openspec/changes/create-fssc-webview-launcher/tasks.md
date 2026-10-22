## 1. 项目与依赖

- [x] 1.1 创建 `FSSCWebLauncher` WPF 项目并配置 `net8.0-windows10.0.19041.0`、x64 与 Windows 桌面目标，运行还原和首次构建并确认命令成功。
- [x] 1.2 引用 `Microsoft.Web.WebView2` 版本 `1.0.4191.47`，确认项目仅包含该网页外壳所需依赖，且未引用 `F:\USB-Web-Launcher` 的业务项目。

## 2. 最小网页外壳

- [x] 2.1 创建只包含单个 WebView2 控件的主窗口，使控件填满客户端区域，并通过检查 XAML 和运行窗口确认没有地址栏、导航工具栏、设置页或其他业务控件。
- [x] 2.2 在窗口加载时使用 `%LOCALAPPDATA%\FSSCWebLauncher\WebView2` 初始化 WebView2 环境，并在初始化成功后导航到固定地址 `https://fssc-w.cnhuafas.com/OSPApplication/application.html`；运行应用并确认目标页面开始加载。
- [x] 2.3 保留网站自身发起的重定向和后续页面导航，实际操作一次站内跳转并确认页面仍在应用窗口内正常工作。

## 3. 错误处理

- [x] 3.1 捕获 WebView2 环境创建和控件初始化异常，显示简短中文错误提示，并通过模拟不可用运行环境确认应用不会无提示退出。
- [x] 3.2 监听首次导航结果，在网络、DNS、TLS 或服务器原因导致加载失败时显示简短中文提示，并通过不可访问地址或断网测试确认失败可见。

## 4. 构建、发布与交付

- [x] 4.1 执行 Release 构建并确认无编译错误，且输出入口名称为 `FSSCWebLauncher.exe`。
- [x] 4.2 使用 `win-x64`、`SelfContained=true`、`PublishSingleFile=false` 和 `PublishTrimmed=false` 发布到独立目录，确认目录中包含 `FSSCWebLauncher.exe`、`WebView2Loader.dll` 及所需 .NET 运行依赖。
- [x] 4.3 从发布目录启动 EXE，人工确认固定网址可加载、窗口除系统标题栏外没有额外工具、关闭窗口后进程正常退出，并确认不存在 USB、PIN、代理、自动化、导出、更新或安装功能。
- [x] 4.4 编写简洁交付说明，明确必须整体保留发布目录且目标 Windows x64 机器需要 Microsoft Edge WebView2 Evergreen Runtime，并核对说明与实际发布产物一致。
