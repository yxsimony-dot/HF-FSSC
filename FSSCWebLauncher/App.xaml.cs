using System.IO;
using System.Windows;

namespace FSSCWebLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!File.Exists(UpdateInstaller.ErrorLogPath))
        {
            return;
        }

        string errorLogPath = UpdateInstaller.ErrorLogPath;
        try
        {
            File.Delete(errorLogPath);
        }
        catch
        {
            // The message still gives the user enough context to retry.
        }

        Dispatcher.BeginInvoke(() =>
        {
            MessageBox.Show(
                "上次更新未能完成，程序已重新启动。"
                + $"\n错误日志：{errorLogPath}",
                "更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }
}
