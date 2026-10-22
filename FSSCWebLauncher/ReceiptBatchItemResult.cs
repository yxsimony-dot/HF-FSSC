namespace FSSCWebLauncher;

internal enum ReceiptItemStatus
{
    UploadedPendingMove,
    UploadedAndMoved,
    QueryFailed,
    ExistingAttachment,
    UploadFailed,
    MoveFailed
}

internal sealed record ReceiptBatchItemResult(
    ReceiptWorkItem WorkItem,
    ReceiptItemStatus Status,
    string? Detail = null)
{
    public string SerialNumber => WorkItem.SerialNumber;

    public bool ShouldMove => Status == ReceiptItemStatus.UploadedPendingMove;

    public bool IsComplete => Status == ReceiptItemStatus.UploadedAndMoved;

    public string UserReason => Status switch
    {
        ReceiptItemStatus.UploadedPendingMove => "上传成功，等待移动",
        ReceiptItemStatus.UploadedAndMoved => "上传并移动成功",
        ReceiptItemStatus.QueryFailed => Detail ?? "查询失败",
        ReceiptItemStatus.ExistingAttachment => "已有附件，未上传，未移动",
        ReceiptItemStatus.UploadFailed => Detail ?? "上传失败",
        ReceiptItemStatus.MoveFailed => Detail ?? "上传成功，但移动失败",
        _ => Detail ?? "未知状态"
    };

    public ReceiptBatchItemResult MarkMoved()
    {
        if (!ShouldMove)
        {
            throw new InvalidOperationException("只有本批次上传成功的文件可以标记为已移动。");
        }

        return this with
        {
            Status = ReceiptItemStatus.UploadedAndMoved,
            Detail = null
        };
    }

    public ReceiptBatchItemResult MarkMoveFailed(string detail)
    {
        if (!ShouldMove)
        {
            throw new InvalidOperationException("只有本批次上传成功的文件可以记录移动失败。");
        }

        return this with
        {
            Status = ReceiptItemStatus.MoveFailed,
            Detail = detail
        };
    }
}
