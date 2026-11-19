namespace FSSCWebLauncher;

internal sealed class ReceiptItemProcessor
{
    private readonly Func<
        ReceiptWorkItem,
        Task<ReceiptQueryCheckResult>> _queryAsync;
    private readonly Func<
        ReceiptUploadContext,
        string,
        Task<ReceiptBatchItemResult>> _uploadAsync;
    private readonly ReconciliationUploadPayloadFactory _payloadFactory;
    private readonly Action<ReceiptWorkItem, bool>?
        _conversionStateChanged;

    public ReceiptItemProcessor(
        ReceiptPageClient pageClient,
        ReconciliationUploadPayloadFactory payloadFactory,
        Action<ReceiptWorkItem, bool>?
            conversionStateChanged = null)
        : this(
            pageClient.QueryReceiptForUploadAsync,
            pageClient.UploadReceiptAsync,
            payloadFactory,
            conversionStateChanged)
    {
    }

    internal ReceiptItemProcessor(
        Func<ReceiptWorkItem, Task<ReceiptQueryCheckResult>> queryAsync,
        Func<
            ReceiptUploadContext,
            string,
            Task<ReceiptBatchItemResult>> uploadAsync,
        ReconciliationUploadPayloadFactory payloadFactory,
        Action<ReceiptWorkItem, bool>?
            conversionStateChanged = null)
    {
        _queryAsync = queryAsync;
        _uploadAsync = uploadAsync;
        _payloadFactory = payloadFactory;
        _conversionStateChanged = conversionStateChanged;
    }

    public async Task<ReceiptBatchItemResult> ProcessAsync(
        ReceiptWorkItem workItem)
    {
        ReceiptQueryCheckResult query = await _queryAsync(workItem);
        ReceiptBatchItemResult? terminal = CreateTerminalResult(query);
        if (terminal is not null)
        {
            return terminal;
        }

        bool isOfficeFile =
            workItem.FileKind is ReconciliationFileKind.OfficeWord
                or ReconciliationFileKind.OfficeExcel;
        ReconciliationUploadPayloadCreationResult creation;

        if (isOfficeFile)
        {
            _conversionStateChanged?.Invoke(workItem, true);
        }

        try
        {
            creation = await _payloadFactory.CreateAsync(
                workItem.ToReconciliationWorkItem());
        }
        finally
        {
            if (isOfficeFile)
            {
                _conversionStateChanged?.Invoke(workItem, false);
            }
        }

        if (!creation.Succeeded || creation.Payload is null)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.ConversionFailed,
                creation.Error ?? "文件准备失败")
            {
                Warnings = creation.Warnings
            };
        }

        ReconciliationUploadPayload payload = creation.Payload;
        ReceiptBatchItemResult result;

        try
        {
            result = await _uploadAsync(
                query.UploadContext!,
                payload.UploadPath);
        }
        catch
        {
            result = new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.UploadFailed,
                "上传过程中发生异常");
        }

        string? cleanupWarning = payload.Cleanup();
        if (cleanupWarning is not null)
        {
            result = result with
            {
                Warnings = result.Warnings
                    .Append(cleanupWarning)
                    .ToArray()
            };
        }

        return result;
    }

    private static ReceiptBatchItemResult? CreateTerminalResult(
        ReceiptQueryCheckResult query)
    {
        return query.Disposition switch
        {
            ReceiptQueryDisposition.ReadyForUpload
                when query.UploadContext is not null =>
                null,
            ReceiptQueryDisposition.ExistingAttachment =>
                new ReceiptBatchItemResult(
                    query.WorkItem,
                    ReceiptItemStatus.ExistingAttachment,
                    query.Detail),
            ReceiptQueryDisposition.UploadFailed =>
                new ReceiptBatchItemResult(
                    query.WorkItem,
                    ReceiptItemStatus.UploadFailed,
                    query.Detail),
            _ => new ReceiptBatchItemResult(
                query.WorkItem,
                ReceiptItemStatus.QueryFailed,
                query.Detail
                    ?? "查询结果无法确定是否需要上传")
        };
    }
}
