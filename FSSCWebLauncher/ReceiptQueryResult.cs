namespace FSSCWebLauncher;

internal enum ReceiptQueryStatus
{
    Success,
    NotFound,
    Timeout,
    PageError
}

internal sealed record ReceiptQueryResult(
    string SerialNumber,
    ReceiptQueryStatus Status,
    string? Detail = null)
{
    public bool IsSuccess => Status == ReceiptQueryStatus.Success;

    public string FailureReason => Status switch
    {
        ReceiptQueryStatus.NotFound => "未查询到精确匹配记录",
        ReceiptQueryStatus.Timeout => "查询超时",
        ReceiptQueryStatus.PageError => Detail ?? "页面操作异常",
        _ => string.Empty
    };
}

internal enum ReceiptQueryDisposition
{
    ReadyForUpload,
    ExistingAttachment,
    QueryFailed,
    UploadFailed
}

internal enum ReceiptUploadMode
{
    Dom,
    Automation
}

internal sealed record ReceiptUploadContext(
    ReceiptWorkItem WorkItem,
    ReceiptUploadMode Mode,
    IntPtr WindowHandle);

internal sealed record ReceiptQueryCheckResult(
    ReceiptWorkItem WorkItem,
    ReceiptQueryDisposition Disposition,
    ReceiptUploadContext? UploadContext = null,
    string? Detail = null);
