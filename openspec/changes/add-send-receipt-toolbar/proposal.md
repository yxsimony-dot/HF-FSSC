## Why

当前 FSSC 网页外壳只能展示网站，用户登录后仍需在站内菜单中手动进入“档案收集 -> 电子回单”。需要增加一个可继续扩展的顶部任务栏，让常用入口能够通过稳定的程序化方式触发，而不依赖屏幕坐标。

## What Changes

- 在 WebView2 上方增加一排应用任务栏，首个按钮命名为“发送回单”。
- 点击“发送回单”后，优先在网页 DOM 中精确识别“档案收集”下的“电子回单”菜单项并触发其原生点击行为。
- 当 DOM 方式未找到目标时，使用 Windows UI Automation 按菜单项类型和名称执行 `InvokePattern`。
- 未登录、页面未加载完成或目标菜单不存在时，显示简洁提示，不执行坐标鼠标点击。
- 保持现有固定网址、登录状态目录和其他网页行为不变。

## Capabilities

### New Capabilities

- `receipt-navigation-toolbar`: 定义应用顶部任务栏及“发送回单”按钮进入“电子回单”页面的行为、识别优先级和失败反馈。

### Modified Capabilities

无。

## Impact

- 修改 `FSSCWebLauncher/MainWindow.xaml` 和 `FSSCWebLauncher/MainWindow.xaml.cs`。
- 继续使用现有 WPF、WebView2 和 .NET 8 技术栈，不增加第三方运行依赖。
- 发布新的 Windows x64 自包含目录；不覆盖用户当前运行中的旧发布目录。
