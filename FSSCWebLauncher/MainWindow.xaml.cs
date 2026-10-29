using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
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
    private bool _updateRunning;
    private bool _updateInstallationStarted;
    private string? _receiptDirectory;
    private string? _completedDirectory;
    private string? _reconciliationDirectory;
    private string? _reconciliationCompletedDirectory;
    private ReceiptPageClient? _receiptPageClient;
    private readonly GitHubUpdateService _updateService = new();

    public MainWindow()
    {
        InitializeComponent();
        LauncherSettings settings = LauncherSettingsStore.Load();
        _receiptDirectory = settings.ReceiptDirectory;
        _completedDirectory = settings.CompletedDirectory;
        _reconciliationDirectory = settings.ReconciliationDirectory;
        _reconciliationCompletedDirectory =
            settings.ReconciliationCompletedDirectory;
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

    private void OnChooseReconciliationDirectoryClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            return;
        }

        OpenFolderDialog dialog = new()
        {
            Title = "设置对账目录",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_reconciliationDirectory)
            && Directory.Exists(_reconciliationDirectory))
        {
            dialog.InitialDirectory = _reconciliationDirectory;
        }

        string? selectedDirectory = dialog.ShowDialog(this) == true
            ? dialog.FolderName
            : null;
        DirectorySelectionOutcome outcome =
            ApplySelectedReconciliationDirectory(
                selectedDirectory,
                completedDirectory: false);
        if (outcome == DirectorySelectionOutcome.Cancelled)
        {
            return;
        }

        if (outcome == DirectorySelectionOutcome.Invalid)
        {
            ShowInvalidDirectoryWarning("设置对账目录");
            return;
        }

        if (outcome == DirectorySelectionOutcome.SameAsOther)
        {
            ShowReconciliationSameDirectoryWarning("设置对账目录");
            return;
        }

        SaveDirectorySettings("设置对账目录");
    }

    private void OnChooseReconciliationCompletedDirectoryClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            return;
        }

        OpenFolderDialog dialog = new()
        {
            Title = "设置对账已完成目录",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_reconciliationCompletedDirectory)
            && Directory.Exists(_reconciliationCompletedDirectory))
        {
            dialog.InitialDirectory = _reconciliationCompletedDirectory;
        }

        string? selectedDirectory = dialog.ShowDialog(this) == true
            ? dialog.FolderName
            : null;
        DirectorySelectionOutcome outcome =
            ApplySelectedReconciliationDirectory(
                selectedDirectory,
                completedDirectory: true);
        if (outcome == DirectorySelectionOutcome.Cancelled)
        {
            return;
        }

        if (outcome == DirectorySelectionOutcome.Invalid)
        {
            ShowInvalidDirectoryWarning("设置对账已完成目录");
            return;
        }

        if (outcome == DirectorySelectionOutcome.SameAsOther)
        {
            ShowReconciliationSameDirectoryWarning("设置对账已完成目录");
            return;
        }

        SaveDirectorySettings("设置对账已完成目录");
    }

    private async void OnSendReconciliationClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            return;
        }

        if (!TryCreateReconciliationQueueSnapshot(
                out IReadOnlyList<ReconciliationWorkItem> queue,
                out string? completedDirectorySnapshot))
        {
            return;
        }

        if (Browser.CoreWebView2 is null || _receiptPageClient is null)
        {
            MessageBox.Show(
                this,
                "网页正在加载，请稍后再点击“发送对账”。",
                "发送对账",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryBeginBatch())
        {
            return;
        }

        SetReconciliationProcessingStatus();

        try
        {
            bool pageReady;

            try
            {
                pageReady =
                    await _receiptPageClient.NavigateToReconciliationPageAsync();
            }
            catch
            {
                pageReady = false;
            }

            if (!pageReady)
            {
                IReadOnlyList<ReconciliationBatchItemResult> pageFailures =
                    queue.Select(workItem =>
                        new ReconciliationBatchItemResult(
                            workItem,
                            ReconciliationItemStatus.QueryFailed,
                            "无法进入电子其他会计资料收集页面或查询控件未就绪"))
                    .ToArray();
                ShowReconciliationResults(pageFailures);
                return;
            }

            IReadOnlyList<ReconciliationBatchItemResult> results =
                await ProcessReconciliationBatchAsync(
                    queue,
                    completedDirectorySnapshot);
            ShowReconciliationResults(results);
        }
        finally
        {
            EndBatch();
        }
    }

    private async Task<IReadOnlyList<ReconciliationBatchItemResult>>
        ProcessReconciliationBatchAsync(
            IReadOnlyList<ReconciliationWorkItem> queue,
            string? completedDirectorySnapshot)
    {
        ReconciliationTemporaryWorkspace workspace = new();
        IReadOnlyList<ReconciliationBatchItemResult> results =
            Array.Empty<ReconciliationBatchItemResult>();
        IReadOnlyList<string> cleanupWarnings;

        try
        {
            OfficePdfConversionService conversionService = new(
                workspace);
            ArchivePdfPreparationService archiveService = new(
                workspace,
                stageChanged: SetReconciliationArchiveStatus);
            ReconciliationUploadPayloadFactory payloadFactory = new(
                conversionService.CreatePayloadAsync,
                archiveService.CreatePayloadAsync);
            ReconciliationItemProcessor processor = new(
                _receiptPageClient!,
                payloadFactory,
                SetReconciliationConversionStatus);
            results =
                await ReconciliationBatchRunner.ProcessSequentiallyAsync(
                    queue,
                    completedDirectorySnapshot,
                    processor.ProcessAsync);
        }
        finally
        {
            cleanupWarnings = workspace.CleanupBatch();
        }

        return AttachBatchCleanupWarnings(
            results,
            cleanupWarnings);
    }

    private static IReadOnlyList<ReconciliationBatchItemResult>
        AttachBatchCleanupWarnings(
            IReadOnlyList<ReconciliationBatchItemResult> results,
            IReadOnlyList<string> cleanupWarnings)
    {
        if (results.Count == 0 || cleanupWarnings.Count == 0)
        {
            return results;
        }

        ReconciliationBatchItemResult[] updated = results.ToArray();
        updated[^1] = updated[^1] with
        {
            Warnings = updated[^1].Warnings
                .Concat(cleanupWarnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
        return updated;
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

    private async void OnCheckForUpdatesClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_batchRunning || _updateRunning)
        {
            return;
        }

        _updateRunning = true;
        SetBatchControlsEnabled(false);
        UpdateButtonText.Text = "检查中...";

        using CancellationTokenSource cancellationSource =
            new(TimeSpan.FromMinutes(15));

        try
        {
            AvailableUpdate? update =
                await _updateService.CheckForUpdateAsync(
                    cancellationSource.Token);

            if (update is null)
            {
                MessageBox.Show(
                    this,
                    $"当前已是最新版本（{ApplicationVersion.Current.Version}）。",
                    "检查更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"发现新版本 {update.Version}，发布时间："
                + $"{update.PublishedAt.ToLocalTime():yyyy-MM-dd HH:mm}。\n\n"
                + "是否立即下载并安装？安装时程序会自动关闭并重新启动。",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            Progress<int> progress = new(percent =>
            {
                UpdateButtonText.Text = $"下载中 {percent}%";
            });

            DownloadedUpdate downloadedUpdate =
                await _updateService.DownloadUpdateAsync(
                    update,
                    progress,
                    cancellationSource.Token);

            UpdateButtonText.Text = "正在安装...";
            UpdateInstaller.Launch(downloadedUpdate);
            _updateInstallationStarted = true;
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                this,
                "更新操作超时，请检查网络后重试。",
                "检查更新",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (HttpRequestException)
        {
            MessageBox.Show(
                this,
                "无法连接 GitHub 更新服务，请检查网络后重试。",
                "检查更新",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "更新包无效",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (System.ComponentModel.Win32Exception exception)
            when (exception.NativeErrorCode == 1223)
        {
            MessageBox.Show(
                this,
                "更新安装已取消。",
                "检查更新",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"更新失败：{exception.Message}",
                "检查更新",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (!_updateInstallationStarted)
            {
                _updateRunning = false;
                UpdateButtonText.Text = "检查更新";
                SetBatchControlsEnabled(!_batchRunning);
            }
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

    private bool TryCreateReconciliationQueueSnapshot(
        out IReadOnlyList<ReconciliationWorkItem> queue,
        out string? completedDirectorySnapshot)
    {
        queue = Array.Empty<ReconciliationWorkItem>();
        completedDirectorySnapshot = null;

        if (string.IsNullOrWhiteSpace(_reconciliationDirectory))
        {
            MessageBox.Show(
                this,
                "请先设置对账目录。",
                "发送对账",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (!Directory.Exists(_reconciliationDirectory))
        {
            _reconciliationDirectory = null;
            UpdateDirectoryDisplays();
            MessageBox.Show(
                this,
                "当前对账目录已不存在，请重新设置。",
                "发送对账",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            queue = ReconciliationFileQueue.Create(
                _reconciliationDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            MessageBox.Show(
                this,
                "当前对账目录或其子文件夹无法读取，请检查目录权限后重试。",
                "发送对账",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (queue.Count == 0)
        {
            MessageBox.Show(
                this,
                "当前对账目录及其子文件夹中没有可查询的 PDF、图片、Office 文件或压缩包。",
                "发送对账",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(
                _reconciliationCompletedDirectory))
        {
            if (Directory.Exists(_reconciliationCompletedDirectory)
                && !LauncherSettingsStore.AreSameDirectory(
                    _reconciliationDirectory,
                    _reconciliationCompletedDirectory))
            {
                completedDirectorySnapshot = Path.GetFullPath(
                    _reconciliationCompletedDirectory);
            }
            else
            {
                _reconciliationCompletedDirectory = null;
                UpdateDirectoryDisplays();
            }
        }

        return true;
    }

    internal DirectorySelectionOutcome ApplySelectedDirectory(
        string? directory,
        bool completedDirectory)
    {
        return ApplyDirectorySelection(
            directory,
            completedDirectory,
            ref _receiptDirectory,
            ref _completedDirectory);
    }

    internal DirectorySelectionOutcome ApplySelectedReconciliationDirectory(
        string? directory,
        bool completedDirectory)
    {
        return ApplyDirectorySelection(
            directory,
            completedDirectory,
            ref _reconciliationDirectory,
            ref _reconciliationCompletedDirectory);
    }

    private DirectorySelectionOutcome ApplyDirectorySelection(
        string? directory,
        bool completedDirectory,
        ref string? sourceDirectory,
        ref string? completedTargetDirectory)
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
            ? sourceDirectory
            : completedTargetDirectory;
        if (!string.IsNullOrWhiteSpace(otherDirectory)
            && LauncherSettingsStore.AreSameDirectory(
                normalizedDirectory,
                otherDirectory))
        {
            return DirectorySelectionOutcome.SameAsOther;
        }

        if (completedDirectory)
        {
            completedTargetDirectory = normalizedDirectory;
        }
        else
        {
            sourceDirectory = normalizedDirectory;
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
        ResetReconciliationButtonStatus();
    }

    internal void SetReconciliationConversionStatus(
        ReconciliationWorkItem workItem,
        bool isConverting)
    {
        string content = isConverting
            ? "PDF转换中..."
            : "处理中...";
        string description = isConverting
            ? $"正在转换 PDF：{workItem.RelativePath}"
            : $"正在处理：{workItem.RelativePath}";

        SetReconciliationButtonStatus(
            content,
            description);
    }

    internal void SetReconciliationArchiveStatus(
        ReconciliationWorkItem workItem,
        ArchivePdfPreparationStage stage)
    {
        (string content, string description) = stage switch
        {
            ArchivePdfPreparationStage.Extracting => (
                "解压中...",
                $"正在解压：{workItem.RelativePath}"),
            ArchivePdfPreparationStage.Merging => (
                "PDF合并中...",
                $"正在合并 PDF：{workItem.RelativePath}"),
            _ => (
                "处理中...",
                $"正在处理：{workItem.RelativePath}")
        };

        SetReconciliationButtonStatus(
            content,
            description);
    }

    private void SetReconciliationProcessingStatus()
    {
        SetReconciliationButtonStatus(
            "处理中...",
            "正在处理对账文件");
    }

    private void ResetReconciliationButtonStatus()
    {
        SetReconciliationButtonStatus(
            "发送对账",
            "发送对账");
    }

    private void SetReconciliationButtonStatus(
        string content,
        string description)
    {
        void ApplyStatus()
        {
            SendReconciliationButton.Content = content;
            SendReconciliationButton.ToolTip = description;
            System.Windows.Automation.AutomationProperties.SetName(
                SendReconciliationButton,
                description);
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyStatus();
            return;
        }

        Dispatcher.Invoke(ApplyStatus);
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

    private void ShowReconciliationResults(
        IReadOnlyList<ReconciliationBatchItemResult> results)
    {
        ReconciliationResultWindow resultWindow = new(results)
        {
            Owner = this
        };
        resultWindow.ShowDialog();
    }

    private void SaveDirectorySettings(string title)
    {
        if (!LauncherSettingsStore.SaveAll(
                _receiptDirectory,
                _completedDirectory,
                _reconciliationDirectory,
                _reconciliationCompletedDirectory))
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

    private void ShowReconciliationSameDirectoryWarning(string title)
    {
        MessageBox.Show(
            this,
            "对账目录与对账已完成目录不能是同一目录。",
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
        ReconciliationDirectoryPathText.Text =
            string.IsNullOrWhiteSpace(_reconciliationDirectory)
                ? "尚未设置有效对账目录"
                : _reconciliationDirectory;
        ReconciliationCompletedDirectoryPathText.Text =
            string.IsNullOrWhiteSpace(_reconciliationCompletedDirectory)
                ? "尚未设置有效对账已完成目录"
                : _reconciliationCompletedDirectory;
    }

    private void SetBatchControlsEnabled(bool enabled)
    {
        bool controlsEnabled = enabled && !_updateRunning;
        SendReceiptButton.IsEnabled = controlsEnabled;
        ChooseDirectoryButton.IsEnabled = controlsEnabled;
        ChooseCompletedDirectoryButton.IsEnabled = controlsEnabled;
        SendReconciliationButton.IsEnabled = controlsEnabled;
        ChooseReconciliationDirectoryButton.IsEnabled = controlsEnabled;
        ChooseReconciliationCompletedDirectoryButton.IsEnabled =
            controlsEnabled;
        UpdateButton.IsEnabled = controlsEnabled;
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
