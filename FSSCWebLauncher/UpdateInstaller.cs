using System.Diagnostics;
using System.IO;
using System.Text;

namespace FSSCWebLauncher;

internal static class UpdateInstaller
{
    public static string ErrorLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "FSSCWebLauncher",
        "update-error.log");

    private const string InstallerScript = """
        param(
            [Parameter(Mandatory = $true)][int]$ApplicationProcessId,
            [Parameter(Mandatory = $true)][string]$SourceDirectory,
            [Parameter(Mandatory = $true)][string]$TargetDirectory,
            [Parameter(Mandatory = $true)][string]$ExecutableName,
            [Parameter(Mandatory = $true)][string]$CleanupDirectory,
            [Parameter(Mandatory = $true)][string]$ErrorLogPath
        )

        $ErrorActionPreference = 'Stop'

        try {
            Wait-Process -Id $ApplicationProcessId -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500

            New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null
            $copyCompleted = $false
            for ($attempt = 1; $attempt -le 10; $attempt++) {
                try {
                    Get-ChildItem -LiteralPath $SourceDirectory -Force |
                        Copy-Item -Destination $TargetDirectory -Recurse -Force
                    $copyCompleted = $true
                    break
                }
                catch {
                    if ($attempt -eq 10) {
                        throw
                    }

                    Start-Sleep -Seconds 1
                }
            }

            if (-not $copyCompleted) {
                throw 'Update files could not be copied.'
            }

            $executablePath = Join-Path $TargetDirectory $ExecutableName
            Start-Process -FilePath $executablePath -WorkingDirectory $TargetDirectory
            Start-Sleep -Seconds 2
            Remove-Item -LiteralPath $CleanupDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
        catch {
            $_ | Out-String | Set-Content -LiteralPath $ErrorLogPath -Encoding UTF8
            $existingExecutable = Join-Path $TargetDirectory $ExecutableName
            if (Test-Path -LiteralPath $existingExecutable) {
                Start-Process -FilePath $existingExecutable -WorkingDirectory $TargetDirectory
            }
            exit 1
        }
        """;

    public static void Launch(DownloadedUpdate update)
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("无法确定当前程序路径。");
        }

        string targetDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        string executableName = Path.GetFileName(processPath);
        string packagedExecutable = Path.Combine(
            update.ContentDirectory,
            executableName);
        if (!File.Exists(packagedExecutable))
        {
            throw new InvalidDataException(
                $"更新包中缺少 {executableName}。");
        }

        string scriptPath = Path.Combine(
            update.WorkingDirectory,
            "install-update.ps1");
        File.WriteAllText(
            scriptPath,
            InstallerScript,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Directory.CreateDirectory(
            Path.GetDirectoryName(ErrorLogPath)!);
        TryDeleteFile(ErrorLogPath);

        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = targetDirectory,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!CanWriteToDirectory(targetDirectory))
        {
            startInfo.Verb = "runas";
        }

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ApplicationProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-SourceDirectory");
        startInfo.ArgumentList.Add(update.ContentDirectory);
        startInfo.ArgumentList.Add("-TargetDirectory");
        startInfo.ArgumentList.Add(targetDirectory);
        startInfo.ArgumentList.Add("-ExecutableName");
        startInfo.ArgumentList.Add(executableName);
        startInfo.ArgumentList.Add("-CleanupDirectory");
        startInfo.ArgumentList.Add(update.WorkingDirectory);
        startInfo.ArgumentList.Add("-ErrorLogPath");
        startInfo.ArgumentList.Add(ErrorLogPath);

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动更新安装程序。");
    }

    private static bool CanWriteToDirectory(string directory)
    {
        string probePath = Path.Combine(
            directory,
            $".fssc-update-{Guid.NewGuid():N}.tmp");

        try
        {
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch
        {
            TryDeleteFile(probePath);
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A stale log or probe file does not prevent updating.
        }
    }
}
