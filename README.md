# FSSCWebLauncher

一个最小的 Windows x64 WPF 网页外壳。启动后使用 Microsoft Edge WebView2 打开固定地址：

`https://fssc-w.cnhuafas.com/OSPApplication/application.html`

## 构建

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' restore .\FSSCWebLauncher\FSSCWebLauncher.csproj --runtime win-x64
& 'C:\Program Files\dotnet\dotnet.exe' build .\FSSCWebLauncher\FSSCWebLauncher.csproj --configuration Release --runtime win-x64
```

## 发布

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish .\FSSCWebLauncher\FSSCWebLauncher.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:PublishTrimmed=false `
  --output .\publish\FSSCWebLauncher-win-x64
```

## 交付

交付整个 `publish\FSSCWebLauncher-win-x64` 目录，并从其中的 `FSSCWebLauncher.exe` 启动。目标电脑不需要单独安装 .NET，但必须安装 Microsoft Edge WebView2 Evergreen Runtime。

## 自动更新

程序顶部提供“检查更新”按钮。点击后，程序会读取本仓库最新 GitHub Release 中的更新清单；发现新版本时，会下载并校验完整发布包，退出当前程序后替换文件并自动重启。

每次向 `main` 分支推送内容时，`.github/workflows/publish.yml` 会自动：

1. 发布 Windows x64 自包含版本。
2. 生成带提交号和 SHA-256 的更新清单。
3. 创建新的 GitHub Release，并上传 ZIP 更新包。

如果仓库的 Actions 权限受到限制，需要在 GitHub 仓库设置中允许工作流拥有 `Read and write permissions`，以便创建 Release。
