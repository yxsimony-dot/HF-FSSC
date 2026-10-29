using System.IO;

namespace FSSCWebLauncher;

internal sealed class OfficePdfConversionService
{
    private readonly ReconciliationTemporaryWorkspace _workspace;
    private readonly IOfficePdfWorkerProcessRunner _runner;
    private readonly PdfIntegrityValidator _validator;
    private readonly TimeSpan _timeout;

    public OfficePdfConversionService(
        ReconciliationTemporaryWorkspace workspace,
        IOfficePdfWorkerProcessRunner? runner = null,
        PdfIntegrityValidator? validator = null,
        TimeSpan? timeout = null)
    {
        _workspace = workspace;
        _runner = runner
            ?? new OfficePdfWorkerProcessRunner();
        _validator = validator
            ?? new PdfIntegrityValidator();
        _timeout = timeout
            ?? TimeSpan.FromMinutes(3);
    }

    public async Task<ReconciliationUploadPayloadCreationResult>
        CreatePayloadAsync(
            ReconciliationWorkItem workItem)
    {
        if (workItem.FileKind
            == ReconciliationFileKind.DirectUpload)
        {
            return ReconciliationUploadPayloadCreationResult.Success(
                new ReconciliationUploadPayload(
                    workItem,
                    workItem.SourcePath,
                    isTemporary: false));
        }

        ReconciliationTemporaryItemWorkspace itemWorkspace =
            _workspace.CreateItem();
        string conversionSourcePath;
        SourceFileMetadata? sourceMetadata = null;

        try
        {
            sourceMetadata =
                SourceFileMetadata.TryCapture(workItem.SourcePath);
            conversionSourcePath =
                itemWorkspace.StageSource(workItem.SourcePath);
        }
        catch (FileNotFoundException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源 Office 文件不存在");
        }
        catch (DirectoryNotFoundException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源 Office 文件不存在");
        }
        catch (UnauthorizedAccessException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源 Office 文件或临时目录没有访问权限");
        }
        catch (IOException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源 Office 文件无法复制到临时目录，文件可能被占用");
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源 Office 文件路径无效或不可访问");
        }
        finally
        {
            if (sourceMetadata is not null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                sourceMetadata.Restore(workItem.SourcePath);
            }
        }

        OfficePdfWorkerRequest request = new(
            conversionSourcePath,
            itemWorkspace.OutputPdfPath,
            workItem.FileKind,
            itemWorkspace.StartSignalPath);
        OfficePdfWorkerExecutionResult execution;

        try
        {
            execution = await _runner.RunAsync(
                itemWorkspace.RequestPath,
                request,
                _timeout);
        }
        catch (OperationCanceledException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "Office 转 PDF 已取消");
        }
        catch
        {
            return FailureWithCleanup(
                itemWorkspace,
                "Office 转 PDF 工作进程发生异常");
        }

        if (execution.TimedOut)
        {
            PdfValidationResult timedOutValidation =
                await _validator.ValidateAsync(
                    itemWorkspace.OutputPdfPath,
                    _workspace.BatchDirectory);
            if (!timedOutValidation.IsValid)
            {
                return FailureWithCleanup(
                    itemWorkspace,
                    "Office 转 PDF 超时，未生成完整且稳定的 PDF");
            }

            return Success(
                workItem,
                itemWorkspace);
        }

        if (execution.WorkerResult is null)
        {
            return FailureWithCleanup(
                itemWorkspace,
                execution.Error
                ?? "Office 转 PDF 工作进程未返回结果");
        }

        if (!execution.WorkerResult.Succeeded)
        {
            return FailureWithCleanup(
                itemWorkspace,
                GetStableFailureMessage(
                    execution.WorkerResult));
        }

        if (!PathsEqual(
                execution.WorkerResult.OutputPdfPath,
                itemWorkspace.OutputPdfPath))
        {
            return FailureWithCleanup(
                itemWorkspace,
                "Office 转 PDF 返回了错误的临时文件路径");
        }

        PdfValidationResult validation =
            await _validator.ValidateAsync(
                itemWorkspace.OutputPdfPath,
                _workspace.BatchDirectory);
        if (!validation.IsValid)
        {
            return FailureWithCleanup(
                itemWorkspace,
                $"Office 转 PDF 输出校验失败：{validation.Error}");
        }

        return Success(
            workItem,
            itemWorkspace);
    }

    private static ReconciliationUploadPayloadCreationResult Success(
        ReconciliationWorkItem workItem,
        ReconciliationTemporaryItemWorkspace itemWorkspace)
    {
        return ReconciliationUploadPayloadCreationResult.Success(
            new ReconciliationUploadPayload(
                workItem,
                itemWorkspace.OutputPdfPath,
                isTemporary: true,
                itemWorkspace.Cleanup));
    }

    private static ReconciliationUploadPayloadCreationResult
        FailureWithCleanup(
            ReconciliationTemporaryItemWorkspace itemWorkspace,
            string error)
    {
        string? warning = itemWorkspace.Cleanup();
        return ReconciliationUploadPayloadCreationResult.Failure(
            error,
            warning is null
                ? Array.Empty<string>()
                : [warning]);
    }

    private static string GetStableFailureMessage(
        OfficePdfWorkerResult result)
    {
        return result.ErrorCode switch
        {
            "SOURCE_NOT_FOUND" =>
                "源 Office 文件不存在",
            "WPS_UNAVAILABLE" =>
                "WPS Office 不可用或自动化接口未注册",
            "WPS_COM_ERROR" =>
                "WPS 无法打开或导出当前 Office 文件，文件可能损坏、受密码保护或格式不兼容",
            "ACCESS_DENIED" =>
                "源 Office 文件或临时目录没有访问权限",
            "IO_ERROR" =>
                "Office 文件读取或临时 PDF 写入失败",
            "OUTPUT_PATH_INVALID" or "PATH_INVALID" =>
                "Office 转 PDF 临时路径无效",
            "UNSUPPORTED_FILE_KIND" =>
                "当前 Office 文件类型不受支持",
            "START_SIGNAL_TIMEOUT" =>
                "Office 转 PDF 工作进程启动超时",
            _ =>
                "Office 转 PDF 失败"
        };
    }

    private static bool PathsEqual(
        string first,
        string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed record SourceFileMetadata(
        FileAttributes Attributes,
        DateTime CreationTimeUtc,
        DateTime LastAccessTimeUtc,
        DateTime LastWriteTimeUtc)
    {
        public static SourceFileMetadata? TryCapture(string path)
        {
            try
            {
                FileInfo file = new(path);
                return file.Exists
                    ? new SourceFileMetadata(
                        file.Attributes,
                        file.CreationTimeUtc,
                        file.LastAccessTimeUtc,
                        file.LastWriteTimeUtc)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public void Restore(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                File.SetAttributes(
                    path,
                    Attributes & ~FileAttributes.ReadOnly);
                File.SetCreationTimeUtc(path, CreationTimeUtc);
                File.SetLastWriteTimeUtc(path, LastWriteTimeUtc);
                File.SetLastAccessTimeUtc(path, LastAccessTimeUtc);
                File.SetAttributes(path, Attributes);
            }
            catch
            {
                // Conversion remains authoritative when Windows denies
                // restoring optional source metadata.
            }
        }
    }
}
