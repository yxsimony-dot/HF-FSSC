## Purpose

该能力为 Windows 用户提供一个专用、最小且行为可验证的华发 FSSC 网页入口，使用户无需输入网址即可从桌面程序进入目标系统，同时避免引入与网页访问无关的本机业务功能。

## ADDED Requirements

### Requirement: 启动后加载固定网址
应用 SHALL 在用户启动 `FSSCWebLauncher.exe` 后创建桌面窗口，并在 WebView 可用时自动导航到 `https://fssc-w.cnhuafas.com/OSPApplication/application.html`。

#### Scenario: 正常启动并进入目标网站
- **WHEN** 用户启动应用，且 WebView2 Runtime 与网络均可用
- **THEN** 应用窗口显示目标网址返回的网页

#### Scenario: 网站在应用内继续导航
- **WHEN** 目标网站自身触发登录跳转、页面跳转或重定向
- **THEN** 应用允许该网站流程继续运行，且不要求用户重新输入初始网址

### Requirement: 提供最小网页外壳
应用 SHALL 只提供承载目标网站所需的窗口和网页区域，MUST NOT 提供可编辑地址栏、前进后退工具栏、设置页面或与目标网站无关的业务控件。

#### Scenario: 查看应用主窗口
- **WHEN** 用户打开应用主窗口
- **THEN** 可用内容区域由目标网页占据，除操作系统窗口边框和标题栏外不显示额外应用工具栏

### Requirement: 不包含参考工程业务功能
应用 MUST NOT 包含 USB Key 检测、PIN 管理、本机认证代理、浏览器扩展注入、网页自动化、流水或回单导出、应用更新器及安装器逻辑。

#### Scenario: 独立运行精简程序
- **WHEN** 用户启动并使用精简程序
- **THEN** 应用不会请求 USB 设备、PIN、管理员权限或参考工程的代理组件

### Requirement: 显示最低限度的启动错误
应用 SHALL 在 WebView2 初始化失败或初始页面导航失败时向用户显示可理解的错误信息，MUST NOT 因该错误无提示退出。

#### Scenario: WebView2 无法初始化
- **WHEN** 运行机器缺少兼容的 WebView2 Runtime，或 WebView2 环境创建失败
- **THEN** 应用显示网页运行环境无法启动的错误提示并保持可关闭的窗口或对话框

#### Scenario: 初始页面导航失败
- **WHEN** 应用已创建 WebView2，但初始网址因网络、DNS、TLS 或服务器原因加载失败
- **THEN** 应用显示目标页面加载失败的错误提示

### Requirement: 以 Windows x64 发布目录交付
应用 SHALL 提供 Windows x64 的自包含发布目录，目录中 MUST 包含可直接启动的 `FSSCWebLauncher.exe` 及其所需运行依赖。

#### Scenario: 在未安装 .NET SDK 的机器上启动
- **WHEN** 用户将完整发布目录放在兼容的 Windows x64 机器上，且该机器已有 WebView2 Runtime
- **THEN** 用户无需安装 .NET SDK 或单独的 .NET Desktop Runtime即可启动应用

#### Scenario: 仅移动主 EXE
- **WHEN** 用户将 `FSSCWebLauncher.exe` 与发布目录中的依赖文件分离
- **THEN** 该使用方式不属于受支持的交付方式，发布说明明确要求保留完整目录
