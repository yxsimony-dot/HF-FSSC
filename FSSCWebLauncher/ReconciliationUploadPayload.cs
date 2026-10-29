using System.IO;

namespace FSSCWebLauncher;

internal sealed class ReconciliationUploadPayload
{
    private readonly Func<string?>? _cleanup;
    private bool _cleanupAttempted;

    public ReconciliationUploadPayload(
        ReconciliationWorkItem workItem,
        string uploadPath,
        bool isTemporary,
        Func<string?>? cleanup = null)
    {
        WorkItem = workItem;
        UploadPath = Path.GetFullPath(uploadPath);
        IsTemporary = isTemporary;
        _cleanup = cleanup;
    }

    public ReconciliationWorkItem WorkItem { get; }

    public string UploadPath { get; }

    public bool IsTemporary { get; }

    public string? Cleanup()
    {
        if (!IsTemporary || _cleanupAttempted)
        {
            return null;
        }

        _cleanupAttempted = true;

        if (_cleanup is not null)
        {
            return _cleanup();
        }

        try
        {
            if (File.Exists(UploadPath))
            {
                File.Delete(UploadPath);
            }

            string? directory = Path.GetDirectoryName(UploadPath);
            if (!string.IsNullOrWhiteSpace(directory)
                && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }

            return null;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return "临时 PDF 清理失败，程序将在批次结束时再次尝试";
        }
    }
}

internal sealed record ReconciliationUploadPayloadCreationResult(
    ReconciliationUploadPayload? Payload,
    string? Error)
{
    public IReadOnlyList<string> Warnings { get; init; } =
        Array.Empty<string>();

    public bool Succeeded => Payload is not null;

    public static ReconciliationUploadPayloadCreationResult Success(
        ReconciliationUploadPayload payload)
    {
        return new ReconciliationUploadPayloadCreationResult(
            payload,
            null);
    }

    public static ReconciliationUploadPayloadCreationResult Failure(
        string error,
        IReadOnlyList<string>? warnings = null)
    {
        return new ReconciliationUploadPayloadCreationResult(
            null,
            error)
        {
            Warnings = warnings ?? Array.Empty<string>()
        };
    }
}

internal sealed class ReconciliationUploadPayloadFactory
{
    private readonly Func<
        ReconciliationWorkItem,
        Task<ReconciliationUploadPayloadCreationResult>> _createOfficePayload;
    private readonly Func<
        ReconciliationWorkItem,
        Task<ReconciliationUploadPayloadCreationResult>> _createArchivePayload;

    public ReconciliationUploadPayloadFactory(
        Func<
            ReconciliationWorkItem,
            Task<ReconciliationUploadPayloadCreationResult>>?
            createOfficePayload = null,
        Func<
            ReconciliationWorkItem,
            Task<ReconciliationUploadPayloadCreationResult>>?
            createArchivePayload = null)
    {
        _createOfficePayload = createOfficePayload
            ?? (_ => Task.FromResult(
                ReconciliationUploadPayloadCreationResult.Failure(
                    "当前版本尚未完成 Office 转 PDF")));
        _createArchivePayload = createArchivePayload
            ?? (_ => Task.FromResult(
                ReconciliationUploadPayloadCreationResult.Failure(
                    "当前版本尚未完成压缩包处理")));
    }

    public Task<ReconciliationUploadPayloadCreationResult> CreateAsync(
        ReconciliationWorkItem workItem)
    {
        if (workItem.FileKind == ReconciliationFileKind.DirectUpload)
        {
            return Task.FromResult(
                ReconciliationUploadPayloadCreationResult.Success(
                    new ReconciliationUploadPayload(
                        workItem,
                        workItem.SourcePath,
                        isTemporary: false)));
        }

        return workItem.FileKind == ReconciliationFileKind.Archive
            ? _createArchivePayload(workItem)
            : _createOfficePayload(workItem);
    }
}
