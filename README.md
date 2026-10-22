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
