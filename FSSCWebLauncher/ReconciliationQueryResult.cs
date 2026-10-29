namespace FSSCWebLauncher;

internal enum ReconciliationItemStatus
{
    UploadedAndMoved,
    UploadedNotMoved,
    ExistingAttachment,
    QueryFailed,
    UploadFailed,
    MoveFailed,
    ConversionFailed
}

internal enum ReconciliationPageStatus
{
    Uploaded,
    ExistingAttachment,
    QueryFailed,
    UploadFailed,
    ConversionFailed
}

internal enum ReconciliationQueryDisposition
{
    ReadyForUpload,
    ExistingAttachment,
    QueryFailed
}

internal enum ReconciliationUploadMode
{
    Dom,
    Automation
}

internal sealed record ReconciliationPageResult(
    ReconciliationWorkItem WorkItem,
    ReconciliationPageStatus Status,
    string? Detail = null)
{
    public IReadOnlyList<string> Warnings { get; init; } =
        Array.Empty<string>();
}

internal sealed record ReconciliationUploadContext(
    ReconciliationWorkItem WorkItem,
    ReconciliationUploadMode Mode,
    IntPtr WindowHandle);

internal sealed record ReconciliationQueryCheckResult(
    ReconciliationWorkItem WorkItem,
    ReconciliationQueryDisposition Disposition,
    ReconciliationUploadContext? UploadContext = null,
    string? Detail = null);

internal sealed record ReconciliationBatchItemResult(
    ReconciliationWorkItem WorkItem,
    ReconciliationItemStatus Status,
    string? Detail = null)
{
    public IReadOnlyList<string> Warnings { get; init; } =
        Array.Empty<string>();

    public bool IsComplete =>
        Status == ReconciliationItemStatus.UploadedAndMoved;

    public string UserReason => Status switch
    {
        ReconciliationItemStatus.UploadedAndMoved =>
            "上传并移动成功",
        ReconciliationItemStatus.UploadedNotMoved =>
            Detail ?? "上传成功，未配置有效的对账已完成目录，未执行移动",
        ReconciliationItemStatus.ExistingAttachment =>
            "已有附件，未上传，未移动",
        ReconciliationItemStatus.QueryFailed =>
            Detail ?? "查询失败",
        ReconciliationItemStatus.UploadFailed =>
            Detail ?? "上传失败",
        ReconciliationItemStatus.MoveFailed =>
            Detail ?? "上传成功，但移动失败，源文件已保留",
        ReconciliationItemStatus.ConversionFailed =>
            Detail ?? (WorkItem.FileKind
                == ReconciliationFileKind.Archive
                    ? "压缩包处理失败，源文件已保留"
                    : "Office 转 PDF 失败，源文件已保留"),
        _ => Detail ?? "未知状态"
    };
}
