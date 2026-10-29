namespace FSSCWebLauncher;

internal sealed class ReconciliationItemProcessor
{
    private readonly Func<
        ReconciliationWorkItem,
        Task<ReconciliationQueryCheckResult>> _queryAsync;
    private readonly Func<
        ReconciliationUploadContext,
        string,
        Task<ReconciliationPageResult>> _uploadAsync;
    private readonly ReconciliationUploadPayloadFactory _payloadFactory;
    private readonly Action<ReconciliationWorkItem, bool>?
        _conversionStateChanged;

    public ReconciliationItemProcessor(
        ReceiptPageClient pageClient,
        ReconciliationUploadPayloadFactory payloadFactory,
        Action<ReconciliationWorkItem, bool>?
            conversionStateChanged = null)
        : this(
            pageClient.QueryReconciliationAsync,
            pageClient.UploadReconciliationAsync,
            payloadFactory,
            conversionStateChanged)
    {
    }

    internal ReconciliationItemProcessor(
        Func<
            ReconciliationWorkItem,
            Task<ReconciliationQueryCheckResult>> queryAsync,
        Func<
            ReconciliationUploadContext,
            string,
            Task<ReconciliationPageResult>> uploadAsync,
        ReconciliationUploadPayloadFactory payloadFactory,
        Action<ReconciliationWorkItem, bool>?
            conversionStateChanged = null)
    {
        _queryAsync = queryAsync;
        _uploadAsync = uploadAsync;
        _payloadFactory = payloadFactory;
        _conversionStateChanged = conversionStateChanged;
    }

    public async Task<ReconciliationPageResult> ProcessAsync(
        ReconciliationWorkItem workItem)
    {
        ReconciliationQueryCheckResult query =
            await _queryAsync(workItem);
        if (query.Disposition
            == ReconciliationQueryDisposition.ExistingAttachment)
        {
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.ExistingAttachment,
                query.Detail);
        }

        if (query.Disposition
                != ReconciliationQueryDisposition.ReadyForUpload
            || query.UploadContext is null)
        {
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.QueryFailed,
                query.Detail ?? "查询结果无法确定是否需要上传");
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
            creation = await _payloadFactory.CreateAsync(workItem);
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
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.ConversionFailed,
                creation.Error ?? "文件准备失败")
            {
                Warnings = creation.Warnings
            };
        }

        ReconciliationUploadPayload payload = creation.Payload;
        ReconciliationPageResult result;

        try
        {
            result = await _uploadAsync(
                query.UploadContext,
                payload.UploadPath);
        }
        catch
        {
            result = new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.UploadFailed,
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
}
