using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace FSSCWebLauncher;

internal enum DirectorySelectionOutcome
{
    Applied,
    Cancelled,
    Invalid,
    SameAsOther
}

internal enum BatchDirectoryValidation
{
    Valid,
    ReceiptNotSet,
    ReceiptUnavailable,
    CompletedNotSet,
    CompletedUnavailable,
    SameDirectory
}

public partial class MainWindow : Window
{
    private const string TargetUrl =
        "https://fssc-w.cnhuafas.com/OSPApplication/application.html";

    private bool _initializationStarted;
    private bool _initialNavigationPending;
    private bool _batchRunning;
    private string? _receiptDirectory;
    private string? _completedDirectory;
    private ReceiptPageClient? _receiptPageClient;

    public MainWindow()
    {
        InitializeComponent();
        LauncherSettings settings = LauncherSettingsStore.Load();
        _receiptDirectory = settings.ReceiptDirectory;
        _completedDirectory = settings.CompletedDirectory;
        UpdateDirectoryDisplays();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted)
        {
            return;
        }

        _initializationStarted = true;

        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FSSCWebLauncher",
                "WebView2");

            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            await Browser.EnsureCoreWebView2Async(environment);
            _receiptPageClient = new ReceiptPageClient(
                Browser,
                () => new WindowInteropHelper(this).Handle);
        }
        catch
        {
            MessageBox.Show(
                this,
                "网页运行环境无法启动。请确认已安装 Microsoft Edge WebView2 Runtime。",
                "华发 FSSC",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        Browser.CoreWebView2.NavigationCompleted += OnInitialNavigationCompleted;
        _initialNavigationPending = true;

        try
        {
            Browser.CoreWebView2.Navigate(TargetUrl);
        }
        catch
        {
            _initialNavigationPending = false;
            ShowNavigationError();
        }
    }

    private void OnInitialNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_initialNavigationPending)
        {
            return;
        }

        _initialNavigationPending = false;
        Browser.CoreWebView2.NavigationCompleted -= OnInitialNavigationCompleted;

        if (!e.IsSuccess)
        {
            ShowNavigationError();
        }
    }

    private void OnChooseDirectoryClicked(object sender, RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            return;
        }

        OpenFolderDialog dialog = new()
        {
            Title = "设置回单目录",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_receiptDirectory)
            && Directory.Exists(_receiptDirectory))
        {
            dialog.InitialDirectory = _receiptDirectory;
        }

        string? selectedDirectory = dialog.ShowDialog(this) == true
            ? dialog.FolderName
            : null;
        DirectorySelectionOutcome outcome = ApplySelectedDirectory(
            selectedDirectory,
            completedDirectory: false);
        if (outcome == DirectorySelectionOutcome.Cancelled)
        {
            return;
        }

        if (outcome == DirectorySelectionOutcome.Invalid)
        {
            ShowInvalidDirectoryWarning("设置回单目录");
            return;
        }

        if (outcome == DirectorySelectionOutcome.SameAsOther)
        {
            ShowSameDirectoryWarning("设置回单目录");
            return;
        }

        SaveDirectorySettings("设置回单目录");
    }

    private void OnChooseCompletedDirectoryClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            return;
        }

        OpenFolderDialog dialog = new()
        {
            Title = "设置已完成目录",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_completedDirectory)
            && Directory.Exists(_completedDirectory))
        {
            dialog.InitialDirectory = _completedDirectory;
        }

        string? selectedDirectory = dialog.ShowDialog(this) == true
            ? dialog.FolderName
            : null;
        DirectorySelectionOutcome outcome = ApplySelectedDirectory(
            selectedDirectory,
            completedDirectory: true);
        if (outcome == DirectorySelectionOutcome.Cancelled)
        {
            return;
        }

        if (outcome == DirectorySelectionOutcome.Invalid)
        {
            ShowInvalidDirectoryWarning("设置已完成目录");
            return;
        }

        if (outcome == DirectorySelectionOutcome.SameAsOther)
        {
            ShowSameDirectoryWarning("设置已完成目录");
            return;
        }

        SaveDirectorySettings("设置已完成目录");
    }

    private async void OnSendReceiptClicked(object sender, RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            return;
        }

        if (!TryCreateQueueSnapshot(out IReadOnlyList<ReceiptWorkItem> queue))
        {
            return;
        }

        if (Browser.CoreWebView2 is null || _receiptPageClient is null)
        {
            MessageBox.Show(
                this,
                "网页正在加载，请稍后再点击“发送回单”。",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryBeginBatch())
        {
            return;
        }

        try
        {
            bool pageReady;

            try
            {
                pageReady = await _receiptPageClient.NavigateToReceiptPageAsync();
            }
            catch
            {
                pageReady = false;
            }

            if (!pageReady)
            {
                IReadOnlyList<ReceiptBatchItemResult> pageFailures =
                    queue.Select(workItem =>
                        new ReceiptBatchItemResult(
                            workItem,
                            ReceiptItemStatus.QueryFailed,
                            "无法进入电子回单页面或查询控件未就绪"))
                    .ToArray();
                ShowBatchResults(pageFailures);
                return;
            }

            IReadOnlyList<ReceiptBatchItemResult> webResults =
                await ReceiptBatchRunner.ProcessSequentiallyAsync(
                    queue,
                    _receiptPageClient.ProcessAsync);
            IReadOnlyList<ReceiptBatchItemResult> finalResults =
                ReceiptBatchRunner.MoveUploadedFiles(
                    webResults,
                    _completedDirectory!);

            ShowBatchResults(finalResults);
        }
        finally
        {
            EndBatch();
        }
    }

    private bool TryCreateQueueSnapshot(
        out IReadOnlyList<ReceiptWorkItem> queue)
    {
        queue = Array.Empty<ReceiptWorkItem>();

        BatchDirectoryValidation validation = ValidateBatchDirectories(
            _receiptDirectory,
            _completedDirectory);
        if (validation == BatchDirectoryValidation.ReceiptNotSet)
        {
            MessageBox.Show(
                this,
                "请先设置回单目录。",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (validation == BatchDirectoryValidation.ReceiptUnavailable)
        {
            _receiptDirectory = null;
            UpdateDirectoryDisplays();
            MessageBox.Show(
                this,
                "当前回单目录已不存在，请重新设置。",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (validation == BatchDirectoryValidation.CompletedNotSet)
        {
            MessageBox.Show(
                this,
                "请先设置已完成目录。",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (validation == BatchDirectoryValidation.CompletedUnavailable)
        {
            _completedDirectory = null;
            UpdateDirectoryDisplays();
            MessageBox.Show(
                this,
                "当前已完成目录已不存在，请重新设置。",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (validation == BatchDirectoryValidation.SameDirectory)
        {
            ShowSameDirectoryWarning("发送回单");
            return false;
        }

        try
        {
            queue = ReceiptFileQueue.Create(_receiptDirectory!);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            MessageBox.Show(
                this,
                "当前回单目录无法读取，请检查目录权限后重试。",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (queue.Count == 0)
        {
            MessageBox.Show(
                this,
                "当前目录第一层没有可查询的 PDF 文件。",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    internal DirectorySelectionOutcome ApplySelectedDirectory(
        string? directory,
        bool completedDirectory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return DirectorySelectionOutcome.Cancelled;
        }

        if (!Directory.Exists(directory))
        {
            return DirectorySelectionOutcome.Invalid;
        }

        string normalizedDirectory;
        try
        {
            normalizedDirectory = Path.GetFullPath(directory);
        }
        catch
        {
            return DirectorySelectionOutcome.Invalid;
        }

        string? otherDirectory = completedDirectory
            ? _receiptDirectory
            : _completedDirectory;
        if (!string.IsNullOrWhiteSpace(otherDirectory)
            && LauncherSettingsStore.AreSameDirectory(
                normalizedDirectory,
                otherDirectory))
        {
            return DirectorySelectionOutcome.SameAsOther;
        }

        if (completedDirectory)
        {
            _completedDirectory = normalizedDirectory;
        }
        else
        {
            _receiptDirectory = normalizedDirectory;
        }

        UpdateDirectoryDisplays();
        return DirectorySelectionOutcome.Applied;
    }

    internal static BatchDirectoryValidation ValidateBatchDirectories(
        string? receiptDirectory,
        string? completedDirectory)
    {
        if (string.IsNullOrWhiteSpace(receiptDirectory))
        {
            return BatchDirectoryValidation.ReceiptNotSet;
        }

        if (!Directory.Exists(receiptDirectory))
        {
            return BatchDirectoryValidation.ReceiptUnavailable;
        }

        if (string.IsNullOrWhiteSpace(completedDirectory))
        {
            return BatchDirectoryValidation.CompletedNotSet;
        }

        if (!Directory.Exists(completedDirectory))
        {
            return BatchDirectoryValidation.CompletedUnavailable;
        }

        return LauncherSettingsStore.AreSameDirectory(
                receiptDirectory,
                completedDirectory)
            ? BatchDirectoryValidation.SameDirectory
            : BatchDirectoryValidation.Valid;
    }

    internal bool TryBeginBatch()
    {
        if (_batchRunning)
        {
            return false;
        }

        _batchRunning = true;
        SetBatchControlsEnabled(false);
        return true;
    }

    internal void EndBatch()
    {
        _batchRunning = false;
        SetBatchControlsEnabled(true);
    }

    private void ShowBatchResults(
        IReadOnlyList<ReceiptBatchItemResult> results)
    {
        if (results.Count > 0
            && results.All(result => result.IsComplete))
        {
            MessageBox.Show(
                this,
                "所有回单均上传并移动成功",
                "发送回单",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        BatchResultWindow resultWindow = new(results)
        {
            Owner = this
        };
        resultWindow.ShowDialog();
    }

    private void SaveDirectorySettings(string title)
    {
        if (!LauncherSettingsStore.Save(
                _receiptDirectory,
                _completedDirectory))
        {
            MessageBox.Show(
                this,
                "目录已在本次运行中生效，但无法保存为下次启动的默认目录。",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowSameDirectoryWarning(string title)
    {
        MessageBox.Show(
            this,
            "回单目录与已完成目录不能是同一目录。",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowInvalidDirectoryWarning(string title)
    {
        MessageBox.Show(
            this,
            "所选目录路径无效或已经不存在，请重新选择。",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void UpdateDirectoryDisplays()
    {
        DirectoryPathText.Text = string.IsNullOrWhiteSpace(_receiptDirectory)
            ? "尚未设置有效回单目录"
            : _receiptDirectory;
        CompletedDirectoryPathText.Text =
            string.IsNullOrWhiteSpace(_completedDirectory)
                ? "尚未设置有效已完成目录"
                : _completedDirectory;
    }

    private void SetBatchControlsEnabled(bool enabled)
    {
        SendReceiptButton.IsEnabled = enabled;
        ChooseDirectoryButton.IsEnabled = enabled;
        ChooseCompletedDirectoryButton.IsEnabled = enabled;
    }

    private void ShowNavigationError()
    {
        MessageBox.Show(
            this,
            "目标页面加载失败，请检查网络连接后重新打开应用。",
            "华发 FSSC",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
