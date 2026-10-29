using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;

namespace FSSCWebLauncher;

internal sealed partial class ReceiptPageClient
{
    private const string ReconciliationQueryControlsReadyScript = """
        (() => {
            const normalize = value => (value ?? "")
                .replace(/\s+/g, "")
                .replace(/[：:]$/, "");
            const isVisible = element => {
                if (!element) {
                    return false;
                }

                const style = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rect.width > 0
                    && rect.height > 0;
            };
            const findInput = () => {
                const labels = Array.from(document.querySelectorAll(
                    "label, .formLabel, .ant-form-item-label, .el-form-item__label, .ivu-form-item-label"
                ));

                for (const label of labels) {
                    if (normalize(label.innerText || label.textContent)
                        !== "档案名称") {
                        continue;
                    }

                    if (label.htmlFor) {
                        const linked = document.getElementById(label.htmlFor);
                        if (linked && isVisible(linked)) {
                            return linked;
                        }
                    }

                    const formItem = label.closest(
                        ".ant-form-item, .el-form-item, .ivu-form-item, .form-group"
                    ) ?? label.parentElement;
                    const input = formItem?.querySelector(
                        "input:not([type='hidden']), textarea"
                    );
                    if (input && isVisible(input)) {
                        return input;
                    }
                }

                return null;
            };
            const findQueryButton = input => {
                const selector = [
                    "button",
                    "[role='button']",
                    "input[type='button']",
                    "input[type='submit']"
                ].join(",");
                const matches = scope => Array.from(
                    scope.querySelectorAll(selector)
                ).filter(element => {
                    const text = element.tagName === "INPUT"
                        ? element.value
                        : element.innerText || element.textContent;
                    return isVisible(element)
                        && normalize(text) === "查询"
                        && !element.disabled
                        && element.getAttribute("aria-disabled") !== "true";
                });
                const scope = input?.closest(
                    "form, .ant-form, .el-form, .ivu-form, .query-form, .search-form"
                );
                return (scope ? matches(scope)[0] : null)
                    ?? matches(document)[0]
                    ?? null;
            };

            const input = findInput();
            return Boolean(input && findQueryButton(input));
        })()
        """;

    private const string SetReconciliationArchiveNameScriptTemplate = """
        (() => {
            const target = __ARCHIVE_NAME__;
            const normalizeLabel = value => (value ?? "")
                .replace(/\s+/g, "")
                .replace(/[：:]$/, "");
            const normalizeCell = value => (value ?? "")
                .replace(/\s+/g, " ")
                .trim();
            const isVisible = element => {
                if (!element) {
                    return false;
                }

                const style = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rect.width > 0
                    && rect.height > 0;
            };
            const findInput = () => {
                const labels = Array.from(document.querySelectorAll(
                    "label, .formLabel, .ant-form-item-label, .el-form-item__label, .ivu-form-item-label"
                ));

                for (const label of labels) {
                    if (normalizeLabel(label.innerText || label.textContent)
                        !== "档案名称") {
                        continue;
                    }

                    if (label.htmlFor) {
                        const linked = document.getElementById(label.htmlFor);
                        if (linked && isVisible(linked)) {
                            return linked;
                        }
                    }

                    const formItem = label.closest(
                        ".ant-form-item, .el-form-item, .ivu-form-item, .form-group"
                    ) ?? label.parentElement;
                    const input = formItem?.querySelector(
                        "input:not([type='hidden']), textarea"
                    );
                    if (input && isVisible(input)) {
                        return input;
                    }
                }

                return null;
            };
            const getColumnId = element => {
                for (const attribute of [
                    "col-id",
                    "data-col-id",
                    "aria-colindex"
                ]) {
                    const value = element.getAttribute(attribute);
                    if (value !== null && value !== "") {
                        return `${attribute}:${value}`;
                    }
                }

                return "";
            };
            const readResult = () => {
                const table = Array.from(
                    document.querySelectorAll("table")
                ).find(element =>
                    isVisible(element)
                    && Array.from(element.querySelectorAll("thead th")).some(
                        header => normalizeLabel(
                            header.innerText || header.textContent
                        ) === "档案名称"
                    )
                );
                if (table) {
                    const headers = Array.from(
                        table.querySelectorAll("thead th")
                    );
                    const archiveColumn = headers.findIndex(header =>
                        normalizeLabel(
                            header.innerText || header.textContent
                        ) === "档案名称"
                    );
                    const rows = Array.from(
                        table.querySelectorAll("tbody tr")
                    ).filter(isVisible).map(row => Array.from(
                        row.querySelectorAll("td")
                    ).map(cell => normalizeCell(
                        cell.innerText || cell.textContent
                    )));
                    return {
                        signature: rows
                            .map(cells => cells.join("\u001f"))
                            .join("\u001e"),
                        exactMatch: archiveColumn >= 0 && rows.some(cells =>
                            cells[archiveColumn] === target
                        )
                    };
                }

                const headerSelector = [
                    ".osp-header-cell",
                    ".ag-header-cell",
                    "[role='columnheader']"
                ].join(",");
                const rowSelector = [
                    ".osp-row",
                    ".ag-row",
                    "[role='row']"
                ].join(",");
                const cellSelector = [
                    ".osp-cell",
                    ".ag-cell",
                    "[role='gridcell']"
                ].join(",");
                const grid = Array.from(document.querySelectorAll(
                    ".osp-root, .ag-root, [role='grid']"
                )).find(element =>
                    element.tagName !== "TABLE"
                    && isVisible(element)
                    && Array.from(element.querySelectorAll(
                        headerSelector
                    )).some(header =>
                        normalizeLabel(
                            header.innerText || header.textContent
                        ) === "档案名称"
                    )
                );
                if (!grid) {
                    return {
                        signature: "no-results",
                        exactMatch: false
                    };
                }

                const archiveHeader = Array.from(grid.querySelectorAll(
                    headerSelector
                )).find(header =>
                    normalizeLabel(
                        header.innerText || header.textContent
                    ) === "档案名称"
                );
                const columnId = archiveHeader
                    ? getColumnId(archiveHeader)
                    : "";
                const values = [];
                for (const row of Array.from(
                    grid.querySelectorAll(rowSelector)
                ).filter(row =>
                    isVisible(row)
                    && !row.matches(".osp-header-row, .ag-header-row")
                    && !row.closest(".osp-header-row, .ag-header-row")
                )) {
                    for (const cell of row.querySelectorAll(cellSelector)) {
                        if (!isVisible(cell)
                            || (columnId
                                && getColumnId(cell) !== columnId)) {
                            continue;
                        }

                        const value = normalizeCell(
                            cell.innerText || cell.textContent
                        );
                        if (value) {
                            values.push(value);
                        }
                    }
                }

                return {
                    signature: `virtual#${normalizeCell(
                        grid.innerText || grid.textContent
                    )}`,
                    exactMatch: values.includes(target)
                };
            };

            const before = readResult();
            const input = findInput();
            if (!input || input.disabled || input.readOnly) {
                return {
                    status: "not-found",
                    beforeSignature: before.signature,
                    beforeExactMatch: before.exactMatch
                };
            }

            const prototype = input instanceof HTMLTextAreaElement
                ? HTMLTextAreaElement.prototype
                : HTMLInputElement.prototype;
            const setter = Object.getOwnPropertyDescriptor(
                prototype,
                "value"
            )?.set;
            if (setter) {
                setter.call(input, target);
            } else {
                input.value = target;
            }

            input.dispatchEvent(new Event(
                "input",
                { bubbles: true, composed: true }
            ));
            input.dispatchEvent(new Event(
                "change",
                { bubbles: true, composed: true }
            ));
            input.focus();
            return {
                status: "ready",
                beforeSignature: before.signature,
                beforeExactMatch: before.exactMatch
            };
        })()
        """;

    private const string ClickReconciliationQueryButtonScript = """
        (() => {
            const normalize = value => (value ?? "")
                .replace(/\s+/g, "")
                .replace(/[：:]$/, "");
            const isVisible = element => {
                if (!element) {
                    return false;
                }

                const style = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rect.width > 0
                    && rect.height > 0;
            };
            const labels = Array.from(document.querySelectorAll(
                "label, .formLabel, .ant-form-item-label, .el-form-item__label, .ivu-form-item-label"
            ));
            let input = null;
            for (const label of labels) {
                if (normalize(label.innerText || label.textContent)
                    !== "档案名称") {
                    continue;
                }

                const formItem = label.closest(
                    ".ant-form-item, .el-form-item, .ivu-form-item, .form-group"
                ) ?? label.parentElement;
                input = label.htmlFor
                    ? document.getElementById(label.htmlFor)
                    : formItem?.querySelector(
                        "input:not([type='hidden']), textarea"
                    );
                if (input && isVisible(input)) {
                    break;
                }
            }

            const selector = [
                "button",
                "[role='button']",
                "input[type='button']",
                "input[type='submit']"
            ].join(",");
            const matches = scope => Array.from(
                scope.querySelectorAll(selector)
            ).filter(element => {
                const text = element.tagName === "INPUT"
                    ? element.value
                    : element.innerText || element.textContent;
                return isVisible(element)
                    && normalize(text) === "查询"
                    && !element.disabled
                    && element.getAttribute("aria-disabled") !== "true";
            });
            const scope = input?.closest(
                "form, .ant-form, .el-form, .ivu-form, .query-form, .search-form"
            );
            const button = (scope ? matches(scope)[0] : null)
                ?? matches(document)[0]
                ?? null;
            if (!input || !button) {
                return "not-found";
            }

            button.scrollIntoView({
                block: "nearest",
                inline: "nearest"
            });
            button.click();
            return "clicked";
        })()
        """;

    private const string ReadReconciliationQueryStateScriptTemplate = """
        (() => {
            const target = __ARCHIVE_NAME__;
            const normalizeLabel = value => (value ?? "")
                .replace(/\s+/g, "")
                .replace(/[：:]$/, "");
            const normalizeCell = value => (value ?? "")
                .replace(/\s+/g, " ")
                .trim();
            const isVisible = element => {
                if (!element) {
                    return false;
                }

                const style = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rect.width > 0
                    && rect.height > 0;
            };
            const findInput = () => {
                const labels = Array.from(document.querySelectorAll(
                    "label, .formLabel, .ant-form-item-label, .el-form-item__label, .ivu-form-item-label"
                ));

                for (const label of labels) {
                    if (normalizeLabel(label.innerText || label.textContent)
                        !== "档案名称") {
                        continue;
                    }

                    if (label.htmlFor) {
                        const linked = document.getElementById(label.htmlFor);
                        if (linked && isVisible(linked)) {
                            return linked;
                        }
                    }

                    const formItem = label.closest(
                        ".ant-form-item, .el-form-item, .ivu-form-item, .form-group"
                    ) ?? label.parentElement;
                    const input = formItem?.querySelector(
                        "input:not([type='hidden']), textarea"
                    );
                    if (input && isVisible(input)) {
                        return input;
                    }
                }

                return null;
            };
            const findQueryButton = input => {
                const selector = [
                    "button",
                    "[role='button']",
                    "input[type='button']",
                    "input[type='submit']"
                ].join(",");
                const matches = scope => Array.from(
                    scope.querySelectorAll(selector)
                ).filter(element => {
                    const text = element.tagName === "INPUT"
                        ? element.value
                        : element.innerText || element.textContent;
                    return isVisible(element)
                        && normalizeLabel(text) === "查询";
                });
                const scope = input?.closest(
                    "form, .ant-form, .el-form, .ivu-form, .query-form, .search-form"
                );
                return (scope ? matches(scope)[0] : null)
                    ?? matches(document)[0]
                    ?? null;
            };
            const getColumnId = element => {
                for (const attribute of [
                    "col-id",
                    "data-col-id",
                    "aria-colindex"
                ]) {
                    const value = element.getAttribute(attribute);
                    if (value !== null && value !== "") {
                        return `${attribute}:${value}`;
                    }
                }

                return "";
            };

            const input = findInput();
            const button = findQueryButton(input);
            let resultAvailable = false;
            let signature = "no-results";
            let exactMatchCount = 0;
            const table = Array.from(
                document.querySelectorAll("table")
            ).find(element =>
                isVisible(element)
                && Array.from(element.querySelectorAll("thead th")).some(
                    header => normalizeLabel(
                        header.innerText || header.textContent
                    ) === "档案名称"
                )
            );
            let resultScope = table;

            if (table) {
                resultAvailable = true;
                const headers = Array.from(
                    table.querySelectorAll("thead th")
                );
                const archiveColumn = headers.findIndex(header =>
                    normalizeLabel(
                        header.innerText || header.textContent
                    ) === "档案名称"
                );
                const rows = Array.from(
                    table.querySelectorAll("tbody tr")
                ).filter(isVisible).map(row => Array.from(
                    row.querySelectorAll("td")
                ).map(cell => normalizeCell(
                    cell.innerText || cell.textContent
                )));
                exactMatchCount = archiveColumn < 0
                    ? 0
                    : rows.filter(cells =>
                        cells[archiveColumn] === target
                    ).length;
                const wrapper = table.closest(
                    ".ant-table-wrapper, .ant-table, .el-table, .ivu-table-wrapper"
                );
                const emptyText = wrapper?.querySelector(
                    ".ant-empty-description, .ant-table-placeholder, .el-table__empty-text, .ivu-table-tip"
                )?.textContent;
                signature = `${rows
                    .map(cells => cells.join("\u001f"))
                    .join("\u001e")}#${normalizeCell(emptyText)}`;
            } else {
                const headerSelector = [
                    ".osp-header-cell",
                    ".ag-header-cell",
                    "[role='columnheader']"
                ].join(",");
                const rowSelector = [
                    ".osp-row",
                    ".ag-row",
                    "[role='row']"
                ].join(",");
                const cellSelector = [
                    ".osp-cell",
                    ".ag-cell",
                    "[role='gridcell']"
                ].join(",");
                const grid = Array.from(document.querySelectorAll(
                    ".osp-root, .ag-root, [role='grid']"
                )).find(element =>
                    element.tagName !== "TABLE"
                    && isVisible(element)
                    && Array.from(element.querySelectorAll(
                        headerSelector
                    )).some(header =>
                        normalizeLabel(
                            header.innerText || header.textContent
                        ) === "档案名称"
                    )
                );
                if (grid) {
                    resultAvailable = true;
                    resultScope = grid;
                    const archiveHeader = Array.from(
                        grid.querySelectorAll(headerSelector)
                    ).find(header =>
                        normalizeLabel(
                            header.innerText || header.textContent
                        ) === "档案名称"
                    );
                    const columnId = archiveHeader
                        ? getColumnId(archiveHeader)
                        : "";
                    const values = [];
                    for (const row of Array.from(
                        grid.querySelectorAll(rowSelector)
                    ).filter(row =>
                        isVisible(row)
                        && !row.matches(".osp-header-row, .ag-header-row")
                        && !row.closest(".osp-header-row, .ag-header-row")
                    )) {
                        for (const cell of row.querySelectorAll(cellSelector)) {
                            if (!isVisible(cell)
                                || (columnId
                                    && getColumnId(cell) !== columnId)) {
                                continue;
                            }

                            const value = normalizeCell(
                                cell.innerText || cell.textContent
                            );
                            if (value) {
                                values.push(value);
                            }
                        }
                    }

                    exactMatchCount = values.filter(
                        value => value === target
                    ).length;
                    signature = `virtual#${normalizeCell(
                        grid.innerText || grid.textContent
                    )}`;
                }
            }

            const tableScope = resultScope?.closest(
                ".ant-spin-nested-loading, .ant-table-wrapper, .el-loading-parent--relative"
            );
            const spinner = tableScope?.querySelector(
                ".ant-spin-spinning, .el-loading-mask, [aria-busy='true']"
            ) ?? document.querySelector(
                ".ant-spin-spinning, .el-loading-mask, [aria-busy='true']"
            );
            const buttonLoading = Boolean(
                button?.classList?.contains("ant-btn-loading")
                || button?.getAttribute("aria-busy") === "true"
                || button?.disabled
            );

            return {
                controlsPresent: Boolean(input && button),
                inputValue: input?.value ?? "",
                loading: buttonLoading || isVisible(spinner),
                resultAvailable,
                signature,
                exactMatchCount
            };
        })()
        """;

    private static readonly string ReconciliationVirtualGridHelperScript =
        AdaptReceiptScript(VirtualGridHelperScript);

    private static readonly string
        SetReconciliationArchiveScriptTemplate =
            AdaptReceiptScript(SetReceiptSerialScriptTemplate);

    private static readonly string
        ClickReconciliationQueryScript =
            AdaptReceiptScript(ClickQueryButtonScript);

    private static readonly string
        ReadReconciliationRowStateScriptTemplate =
            AdaptReceiptScript(ReadQueryStateScriptTemplate);

    private static readonly string
        PrepareReconciliationUploadScriptTemplate =
            AdaptReceiptScript(PrepareUploadScriptTemplate);

    private static readonly string
        FindPreparedReconciliationFileInputScriptTemplate =
            AdaptReceiptScript(FindPreparedFileInputScriptTemplate);

    private static readonly string
        ReadReconciliationUploadStateScriptTemplate =
            AdaptReceiptScript(ReadUploadStateScriptTemplate);

    private static readonly string
        CleanupReconciliationUploadObservationScriptTemplate =
            AdaptReceiptScript(CleanupUploadObservationScriptTemplate);

    public async Task<ReconciliationPageResult>
        ProcessReconciliationAsync(
            ReconciliationWorkItem workItem)
    {
        if (workItem.FileKind != ReconciliationFileKind.DirectUpload)
        {
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.ConversionFailed,
                "当前文件必须先准备为临时 PDF，不能直接上传原文件");
        }

        ReconciliationQueryCheckResult query =
            await QueryReconciliationAsync(workItem);
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

        return await UploadReconciliationAsync(
            query.UploadContext,
            workItem.SourcePath);
    }

    public async Task<ReconciliationQueryCheckResult>
        QueryReconciliationAsync(
            ReconciliationWorkItem workItem)
    {
        ReceiptRowQueryResult? domQuery =
            await QueryReconciliationWithDomAsync(workItem);
        if (domQuery is not null)
        {
            return CreateReconciliationQueryCheckResult(
                workItem,
                domQuery,
                ReconciliationUploadMode.Dom,
                IntPtr.Zero);
        }

        IntPtr windowHandle = _windowHandleProvider();
        if (windowHandle == IntPtr.Zero)
        {
            return new ReconciliationQueryCheckResult(
                workItem,
                ReconciliationQueryDisposition.QueryFailed,
                Detail: "无法访问应用窗口完成查询");
        }

        ReceiptRowQueryResult automationQuery =
            await QueryReconciliationWithAutomationAsync(
                windowHandle,
                workItem);
        return CreateReconciliationQueryCheckResult(
            workItem,
            automationQuery,
            ReconciliationUploadMode.Automation,
            windowHandle);
    }

    public async Task<ReconciliationPageResult>
        UploadReconciliationAsync(
            ReconciliationUploadContext uploadContext,
            string uploadPath)
    {
        ReconciliationWorkItem workItem = uploadContext.WorkItem;
        string? sourceError =
            ValidateSourceFile(uploadPath, "上传文件");
        if (sourceError is not null)
        {
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.UploadFailed,
                sourceError);
        }

        if (uploadContext.Mode == ReconciliationUploadMode.Dom)
        {
            DomUploadResult domUpload =
                await UploadReconciliationWithDomAsync(
                    workItem,
                    uploadPath);
            if (domUpload.Handled)
            {
                return new ReconciliationPageResult(
                    workItem,
                    domUpload.Succeeded
                        ? ReconciliationPageStatus.Uploaded
                        : ReconciliationPageStatus.UploadFailed,
                    domUpload.Error);
            }

            IntPtr fallbackHandle = _windowHandleProvider();
            if (fallbackHandle == IntPtr.Zero)
            {
                return new ReconciliationPageResult(
                    workItem,
                    ReconciliationPageStatus.UploadFailed,
                    "无法访问应用窗口完成上传");
            }

            ReceiptRowQueryResult fallbackQuery =
                await QueryReconciliationWithAutomationAsync(
                    fallbackHandle,
                    workItem);
            ReconciliationQueryCheckResult fallbackCheck =
                CreateReconciliationQueryCheckResult(
                    workItem,
                    fallbackQuery,
                    ReconciliationUploadMode.Automation,
                    fallbackHandle);
            if (fallbackCheck.Disposition
                    != ReconciliationQueryDisposition.ReadyForUpload
                || fallbackCheck.UploadContext is null)
            {
                return new ReconciliationPageResult(
                    workItem,
                    fallbackCheck.Disposition
                        == ReconciliationQueryDisposition.ExistingAttachment
                            ? ReconciliationPageStatus.ExistingAttachment
                            : ReconciliationPageStatus.UploadFailed,
                    fallbackCheck.Detail
                        ?? "上传前重新验证当前档案失败");
            }

            return await UploadReconciliationWithAutomationAsync(
                fallbackHandle,
                workItem,
                uploadPath);
        }

        if (uploadContext.WindowHandle == IntPtr.Zero)
        {
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.UploadFailed,
                "无法访问应用窗口完成上传");
        }

        return await UploadReconciliationWithAutomationAsync(
            uploadContext.WindowHandle,
            workItem,
            uploadPath);
    }

    private async Task<ReceiptRowQueryResult?>
        QueryReconciliationWithDomAsync(
            ReconciliationWorkItem workItem)
    {
        string setScript = WithReconciliationVirtualGridHelper(
            SetReconciliationArchiveScriptTemplate.Replace(
                "__ARCHIVE_NAME__",
                JsonSerializer.Serialize(workItem.ArchiveName),
                StringComparison.Ordinal));
        DomSetResult? setResult =
            await ExecuteDomObjectAsync<DomSetResult>(setScript);
        if (setResult?.Status != "ready")
        {
            return null;
        }

        await Task.Delay(150);
        if (await ExecuteDomStringAsync(
                WithReconciliationVirtualGridHelper(
                    ClickReconciliationQueryScript)) != "clicked")
        {
            return null;
        }

        string stateScript = WithReconciliationVirtualGridHelper(
            ReadReconciliationRowStateScriptTemplate.Replace(
                "__ARCHIVE_NAME__",
                JsonSerializer.Serialize(workItem.ArchiveName),
                StringComparison.Ordinal));
        Stopwatch stopwatch = Stopwatch.StartNew();
        string? lastSignature = null;
        int stableReadings = 0;
        int unavailableReadings = 0;
        int inputMismatchReadings = 0;
        bool loadingObserved = false;
        bool resultChanged = false;

        while (stopwatch.Elapsed < _timing.QueryTimeout)
        {
            DomQueryState? state =
                await ExecuteDomObjectAsync<DomQueryState>(
                    stateScript);
            if (state is null)
            {
                unavailableReadings++;
                if (unavailableReadings >= 3)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "查询后无法读取网页结果");
                }

                await Task.Delay(_timing.PollInterval);
                continue;
            }

            unavailableReadings = 0;
            if (!state.ControlsPresent)
            {
                return new ReceiptRowQueryResult(
                    ReceiptQueryStatus.PageError,
                    null,
                    false,
                    "档案名称输入框或查询按钮已从页面中消失");
            }

            if (!string.Equals(
                    state.InputValue,
                    workItem.ArchiveName,
                    StringComparison.Ordinal))
            {
                inputMismatchReadings++;
                if (inputMismatchReadings >= 3)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "档案名称输入值在查询期间发生变化");
                }

                await Task.Delay(_timing.PollInterval);
                continue;
            }

            inputMismatchReadings = 0;
            loadingObserved |= state.Loading;
            resultChanged |= !string.Equals(
                setResult.BeforeSignature,
                state.Signature,
                StringComparison.Ordinal);

            if (state.Loading)
            {
                stableReadings = 0;
            }
            else if (string.Equals(
                         lastSignature,
                         state.Signature,
                         StringComparison.Ordinal))
            {
                stableReadings++;
            }
            else
            {
                stableReadings = 1;
            }

            lastSignature = state.Signature;
            bool lifecycleObserved =
                loadingObserved
                || resultChanged
                || (!setResult.BeforeExactMatch
                    && stopwatch.Elapsed
                        >= _timing.UnobservedQuerySettleDelay);

            if (!state.Loading
                && stableReadings >= 2
                && lifecycleObserved)
            {
                if (state.Signature == "no-table")
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "查询结果表格不可用");
                }

                if (state.ExactMatchCount == 0)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.NotFound,
                        null,
                        false,
                        null);
                }

                if (state.ExactMatchCount != 1)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "查询结果存在多个相同档案名称，无法安全上传");
                }

                if (state.AttachmentCount is null)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        $"无法读取附件数量：{state.AttachmentText}");
                }

                return new ReceiptRowQueryResult(
                    ReceiptQueryStatus.Success,
                    state.AttachmentCount,
                    state.UploadAvailable,
                    null);
            }

            await Task.Delay(_timing.PollInterval);
        }

        return new ReceiptRowQueryResult(
            ReceiptQueryStatus.Timeout,
            null,
            false,
            null);
    }

    private async Task<ReceiptRowQueryResult>
        QueryReconciliationWithAutomationAsync(
            IntPtr windowHandle,
            ReconciliationWorkItem workItem)
    {
        ReconciliationAutomationStartResult startResult =
            await Task.Run(() =>
                TryStartAutomationReconciliationQuery(
                    windowHandle,
                    workItem.ArchiveName));
        if (!startResult.Started)
        {
            return new ReceiptRowQueryResult(
                ReceiptQueryStatus.PageError,
                null,
                false,
                startResult.Error
                    ?? "无法通过系统控件执行查询");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string? lastSignature = null;
        int stableReadings = 0;
        int unavailableReadings = 0;
        int inputMismatchReadings = 0;
        bool loadingObserved = false;
        bool resultChanged = false;

        while (stopwatch.Elapsed < _timing.QueryTimeout)
        {
            ReconciliationAutomationQueryState state =
                await Task.Run(() =>
                    ReadAutomationReconciliationQueryState(
                        windowHandle,
                        workItem.ArchiveName));
            if (!state.ControlsPresent)
            {
                unavailableReadings++;
                if (unavailableReadings >= 3)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "系统可访问性查询控件已不可用");
                }

                await Task.Delay(_timing.PollInterval);
                continue;
            }

            unavailableReadings = 0;
            if (!string.Equals(
                    state.InputValue,
                    workItem.ArchiveName,
                    StringComparison.Ordinal))
            {
                inputMismatchReadings++;
                if (inputMismatchReadings >= 3)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "档案名称输入值在查询期间发生变化");
                }

                await Task.Delay(_timing.PollInterval);
                continue;
            }

            inputMismatchReadings = 0;
            loadingObserved |= state.Loading;
            resultChanged |= !string.Equals(
                startResult.BeforeSignature,
                state.Signature,
                StringComparison.Ordinal);

            if (state.Loading)
            {
                stableReadings = 0;
            }
            else if (string.Equals(
                         lastSignature,
                         state.Signature,
                         StringComparison.Ordinal))
            {
                stableReadings++;
            }
            else
            {
                stableReadings = 1;
            }

            lastSignature = state.Signature;
            bool lifecycleObserved =
                loadingObserved
                || resultChanged
                || (!startResult.BeforeExactMatch
                    && stopwatch.Elapsed
                        >= _timing.UnobservedQuerySettleDelay);

            if (!state.Loading
                && stableReadings >= 2
                && lifecycleObserved)
            {
                if (!state.ResultAvailable)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "未找到包含“档案名称”列的结果表格");
                }

                if (!state.ExactMatch)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.NotFound,
                        null,
                        false,
                        null);
                }

                AutomationRowState rowState =
                    await Task.Run(() =>
                        ReadAutomationReconciliationRowState(
                            windowHandle,
                            workItem.ArchiveName));
                if (!rowState.ExactMatch)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        rowState.Error
                            ?? "无法读取精确匹配结果行");
                }

                if (rowState.AttachmentCount is null)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        rowState.Error
                            ?? "无法读取附件数量");
                }

                return new ReceiptRowQueryResult(
                    ReceiptQueryStatus.Success,
                    rowState.AttachmentCount,
                    rowState.UploadAvailable,
                    null);
            }

            await Task.Delay(_timing.PollInterval);
        }

        return new ReceiptRowQueryResult(
            ReceiptQueryStatus.Timeout,
            null,
            false,
            null);
    }

    private static ReconciliationQueryCheckResult
        CreateReconciliationQueryCheckResult(
            ReconciliationWorkItem workItem,
            ReceiptRowQueryResult query,
            ReconciliationUploadMode mode,
            IntPtr windowHandle)
    {
        if (query.Status != ReceiptQueryStatus.Success)
        {
            return new ReconciliationQueryCheckResult(
                workItem,
                ReconciliationQueryDisposition.QueryFailed,
                Detail: query.FailureReason);
        }

        if (query.AttachmentCount is > 0)
        {
            return new ReconciliationQueryCheckResult(
                workItem,
                ReconciliationQueryDisposition.ExistingAttachment);
        }

        if (query.AttachmentCount != 0)
        {
            return new ReconciliationQueryCheckResult(
                workItem,
                ReconciliationQueryDisposition.QueryFailed,
                Detail: "无法读取附件数量，未执行上传");
        }

        if (!query.UploadAvailable)
        {
            return new ReconciliationQueryCheckResult(
                workItem,
                ReconciliationQueryDisposition.QueryFailed,
                Detail: "当前档案结果行未找到唯一可调用的“上传”操作");
        }

        return new ReconciliationQueryCheckResult(
            workItem,
            ReconciliationQueryDisposition.ReadyForUpload,
            new ReconciliationUploadContext(
                workItem,
                mode,
                windowHandle));
    }

    private async Task<DomUploadResult>
        UploadReconciliationWithDomAsync(
            ReconciliationWorkItem workItem,
            string uploadPath)
    {
        string token = Guid.NewGuid().ToString("N");
        IReadOnlySet<string> baselineDialogIds =
            await Task.Run(CaptureOpenFileDialogIds);
        string prepareScript =
            WithReconciliationVirtualGridHelper(
                ReplaceReconciliationUploadScriptTokens(
                    PrepareReconciliationUploadScriptTemplate,
                    workItem.ArchiveName,
                    token));
        DomPrepareUploadResult? prepared =
            await ExecuteDomObjectAsync<DomPrepareUploadResult>(
                prepareScript);
        if (prepared is null)
        {
            return DomUploadResult.Fallback();
        }

        if (prepared.Status is not ("input-ready" or "upload-clicked"))
        {
            await CleanupReconciliationDomUploadAsync(token);
            return DomUploadResult.Failure(
                prepared.Status switch
                {
                    "row-unavailable" =>
                        "上传前无法重新定位唯一档案结果行",
                    "attachment-unreadable" =>
                        "上传前无法读取附件数量",
                    "attachment-changed" =>
                        "上传前附件数量已发生变化",
                    "upload-unavailable" =>
                        "当前档案结果行未找到唯一的“上传”操作",
                    _ => "无法准备网页上传"
                });
        }

        bool inputReady =
            await WaitForPreparedReconciliationFileInputAsync(
                token);
        if (!inputReady)
        {
            if (prepared.Status == "input-ready")
            {
                await CleanupReconciliationDomUploadAsync(token);
                return DomUploadResult.Fallback();
            }

            FileDialogSubmitResult dialogResult =
                await Task.Run(() =>
                    TrySubmitOpenFileDialog(
                        baselineDialogIds,
                        uploadPath,
                        _timing.FileDialogTimeout));
            if (!dialogResult.Submitted)
            {
                await CleanupReconciliationDomUploadAsync(token);
                return DomUploadResult.Failure(
                    dialogResult.Error
                        ?? "未找到本次上传打开的文件选择框");
            }
        }
        else
        {
            bool fileSet =
                await TrySetReconciliationDomFileInputAsync(
                    token,
                    uploadPath);
            if (!fileSet)
            {
                await CleanupReconciliationDomUploadAsync(token);
                return prepared.Status == "input-ready"
                    ? DomUploadResult.Fallback()
                    : DomUploadResult.Failure(
                        "无法将当前文件设置到网页文件输入控件");
            }
        }

        string stateScript =
            WithReconciliationVirtualGridHelper(
                ReplaceReconciliationUploadScriptTokens(
                    ReadReconciliationUploadStateScriptTemplate,
                    workItem.ArchiveName,
                    token));
        Stopwatch stopwatch = Stopwatch.StartNew();
        int unavailableReadings = 0;

        try
        {
            while (stopwatch.Elapsed < _timing.UploadTimeout)
            {
                DomUploadState? state =
                    await ExecuteDomObjectAsync<DomUploadState>(
                        stateScript);
                if (state is null
                    || !state.ObservationAvailable)
                {
                    unavailableReadings++;
                    if (unavailableReadings >= 3)
                    {
                        return DomUploadResult.Failure(
                            "上传后无法读取网页状态");
                    }

                    await Task.Delay(_timing.PollInterval);
                    continue;
                }

                unavailableReadings = 0;
                if (state.SuccessMessage
                    || (state.ExactMatchCount == 1
                        && state.AttachmentCount is > 0))
                {
                    return DomUploadResult.Success();
                }

                if (!string.IsNullOrWhiteSpace(
                        state.FailureText))
                {
                    return DomUploadResult.Failure(
                        $"网页提示上传失败：{state.FailureText}");
                }

                if (state.ExactMatchCount > 1)
                {
                    return DomUploadResult.Failure(
                        "上传后出现多个相同档案名称，无法确认结果");
                }

                await Task.Delay(_timing.PollInterval);
            }

            return DomUploadResult.Failure(
                "上传超时：未出现新的“上传成功！”且附件数量未由 0 增加");
        }
        finally
        {
            await CleanupReconciliationDomUploadAsync(token);
        }
    }

    private async Task<bool>
        WaitForPreparedReconciliationFileInputAsync(
            string token)
    {
        string script =
            FindPreparedReconciliationFileInputScriptTemplate.Replace(
                "__TOKEN__",
                JsonSerializer.Serialize(token),
                StringComparison.Ordinal);
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed
               < _timing.FileInputWaitTimeout)
        {
            string? state = await ExecuteDomStringAsync(script);
            if (state == "ready")
            {
                return true;
            }

            if (state == "observation-unavailable")
            {
                return false;
            }

            await Task.Delay(
                _timing.FileInputPollInterval);
        }

        return false;
    }

    private async Task<bool>
        TrySetReconciliationDomFileInputAsync(
            string token,
            string sourcePath)
    {
        try
        {
            string documentJson =
                await _browser.CoreWebView2
                    .CallDevToolsProtocolMethodAsync(
                        "DOM.getDocument",
                        """{"depth":1,"pierce":true}""");
            using JsonDocument document =
                JsonDocument.Parse(documentJson);
            int rootNodeId = document.RootElement
                .GetProperty("root")
                .GetProperty("nodeId")
                .GetInt32();

            string selector =
                "input[type='file']"
                + "[data-fssc-reconciliation-file-input="
                + $"\"{token}\"]";
            string queryJson =
                await _browser.CoreWebView2
                    .CallDevToolsProtocolMethodAsync(
                        "DOM.querySelector",
                        JsonSerializer.Serialize(new
                        {
                            nodeId = rootNodeId,
                            selector
                        }));
            using JsonDocument query =
                JsonDocument.Parse(queryJson);
            int nodeId = query.RootElement
                .GetProperty("nodeId")
                .GetInt32();
            if (nodeId == 0)
            {
                return false;
            }

            await _browser.CoreWebView2
                .CallDevToolsProtocolMethodAsync(
                    "DOM.setFileInputFiles",
                    JsonSerializer.Serialize(new
                    {
                        nodeId,
                        files = new[]
                        {
                            Path.GetFullPath(sourcePath)
                        }
                    }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task CleanupReconciliationDomUploadAsync(
        string token)
    {
        string script =
            CleanupReconciliationUploadObservationScriptTemplate
                .Replace(
                    "__TOKEN__",
                    JsonSerializer.Serialize(token),
                    StringComparison.Ordinal);
        await ExecuteDomBooleanAsync(script);
    }

    private static string
        ReplaceReconciliationUploadScriptTokens(
            string template,
            string archiveName,
            string token)
    {
        return template
            .Replace(
                "__ARCHIVE_NAME__",
                JsonSerializer.Serialize(archiveName),
                StringComparison.Ordinal)
            .Replace(
                "__TOKEN__",
                JsonSerializer.Serialize(token),
                StringComparison.Ordinal);
    }

    private static string WithReconciliationVirtualGridHelper(
        string script)
    {
        return $"{ReconciliationVirtualGridHelperScript}"
            + $"{Environment.NewLine}{script}";
    }

    private static string AdaptReceiptScript(string script)
    {
        return script
            .Replace(
                "__SERIAL__",
                "__ARCHIVE_NAME__",
                StringComparison.Ordinal)
            .Replace(
                "业务流水号",
                "档案名称",
                StringComparison.Ordinal)
            .Replace(
                "__fsscReceiptVirtualGrid",
                "__fsscReconciliationVirtualGrid",
                StringComparison.Ordinal)
            .Replace(
                "__fsscReceiptUpload",
                "__fsscReconciliationUpload",
                StringComparison.Ordinal)
            .Replace(
                "data-fssc-receipt-row",
                "data-fssc-reconciliation-row",
                StringComparison.Ordinal)
            .Replace(
                "data-fssc-receipt-file-input",
                "data-fssc-reconciliation-file-input",
                StringComparison.Ordinal);
    }

    private async Task<ReconciliationPageResult>
        UploadReconciliationWithAutomationAsync(
            IntPtr windowHandle,
            ReconciliationWorkItem workItem,
            string uploadPath)
    {
        AutomationUploadStartResult start =
            await Task.Run(() =>
                TryStartAutomationReconciliationUpload(
                    windowHandle,
                    workItem.ArchiveName));
        if (!start.Started)
        {
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.UploadFailed,
                start.Error
                    ?? "无法调用当前档案结果行的“上传”操作");
        }

        FileDialogSubmitResult dialogResult =
            await Task.Run(() =>
                TrySubmitOpenFileDialog(
                    start.BaselineDialogIds,
                    uploadPath,
                    _timing.FileDialogTimeout));
        if (!dialogResult.Submitted)
        {
            return new ReconciliationPageResult(
                workItem,
                ReconciliationPageStatus.UploadFailed,
                dialogResult.Error
                    ?? "未找到可操作的文件选择对话框");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < _timing.UploadTimeout)
        {
            AutomationUploadState state =
                await Task.Run(() =>
                    ReadAutomationReconciliationUploadState(
                        windowHandle,
                        workItem.ArchiveName,
                        start.BaselineMessageIds));
            if (!state.PageAvailable)
            {
                return new ReconciliationPageResult(
                    workItem,
                    ReconciliationPageStatus.UploadFailed,
                    state.Error
                        ?? "上传后无法读取页面状态");
            }

            if (state.NewSuccessMessage
                || (state.ExactMatch
                    && state.AttachmentCount is > 0))
            {
                return new ReconciliationPageResult(
                    workItem,
                    ReconciliationPageStatus.Uploaded);
            }

            if (!string.IsNullOrWhiteSpace(
                    state.NewFailureMessage))
            {
                return new ReconciliationPageResult(
                    workItem,
                    ReconciliationPageStatus.UploadFailed,
                    $"网页提示上传失败："
                    + state.NewFailureMessage);
            }

            await Task.Delay(_timing.PollInterval);
        }

        return new ReconciliationPageResult(
            workItem,
            ReconciliationPageStatus.UploadFailed,
            "上传超时：未出现新的“上传成功！”且附件数量未由 0 增加");
    }

    private async Task<bool> WaitForReconciliationQueryControlsAsync(
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        do
        {
            if (await ExecuteDomBooleanAsync(
                    ReconciliationQueryControlsReadyScript))
            {
                return true;
            }

            IntPtr windowHandle = _windowHandleProvider();
            if (windowHandle != IntPtr.Zero
                && await Task.Run(() =>
                    AreAutomationReconciliationQueryControlsReady(
                        windowHandle)))
            {
                return true;
            }

            await Task.Delay(_timing.PollInterval);
        }
        while (stopwatch.Elapsed < timeout);

        return false;
    }

    private static bool AreAutomationReconciliationQueryControlsReady(
        IntPtr windowHandle)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            AutomationElement? input =
                FindReconciliationInput(root, requireEnabled: true);
            AutomationElement? button =
                FindReconciliationQueryButton(
                    root,
                    requireEnabled: true);
            return input is not null
                && button is not null
                && input.Current.IsEnabled
                && !input.Current.IsOffscreen
                && button.Current.IsEnabled
                && !button.Current.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static ReconciliationAutomationStartResult
        TryStartAutomationReconciliationQuery(
            IntPtr windowHandle,
            string archiveName)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            AutomationElement? input =
                FindReconciliationInput(root, requireEnabled: true);
            AutomationElement? button =
                FindReconciliationQueryButton(
                    root,
                    requireEnabled: true);
            if (input is null || button is null)
            {
                return new ReconciliationAutomationStartResult(
                    false,
                    "no-grid",
                    false,
                    "未找到档案名称输入框或查询按钮");
            }

            ReconciliationAutomationGridState beforeState =
                ReadReconciliationAutomationGridState(
                    root,
                    archiveName);
            if (!TrySetAutomationValue(input, archiveName))
            {
                return new ReconciliationAutomationStartResult(
                    false,
                    beforeState.Signature,
                    beforeState.ExactMatch,
                    "档案名称输入框不支持值设置");
            }

            Thread.Sleep(120);
            if (!button.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out object pattern)
                || pattern is not InvokePattern invokePattern)
            {
                return new ReconciliationAutomationStartResult(
                    false,
                    beforeState.Signature,
                    beforeState.ExactMatch,
                    "查询按钮不支持调用");
            }

            invokePattern.Invoke();
            return new ReconciliationAutomationStartResult(
                true,
                beforeState.Signature,
                beforeState.ExactMatch,
                null);
        }
        catch
        {
            return new ReconciliationAutomationStartResult(
                false,
                "no-grid",
                false,
                "系统可访问性查询操作失败");
        }
    }

    private static ReconciliationAutomationQueryState
        ReadAutomationReconciliationQueryState(
            IntPtr windowHandle,
            string archiveName)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            AutomationElement? input =
                FindReconciliationInput(root, requireEnabled: false);
            AutomationElement? button =
                FindReconciliationQueryButton(
                    root,
                    requireEnabled: false);
            if (input is null || button is null)
            {
                return new ReconciliationAutomationQueryState(
                    false,
                    false,
                    false,
                    "no-grid",
                    false,
                    string.Empty);
            }

            ReconciliationAutomationGridState gridState =
                ReadReconciliationAutomationGridState(
                    root,
                    archiveName);
            bool loading =
                !button.Current.IsEnabled
                || HasVisibleProgressIndicator(root);
            return new ReconciliationAutomationQueryState(
                true,
                loading,
                gridState.ResultAvailable,
                gridState.Signature,
                gridState.ExactMatch,
                ReadAutomationValue(input));
        }
        catch
        {
            return new ReconciliationAutomationQueryState(
                false,
                false,
                false,
                "no-grid",
                false,
                string.Empty);
        }
    }

    private static AutomationElement? FindReconciliationInput(
        AutomationElement root,
        bool requireEnabled)
    {
        System.Windows.Automation.Condition editCondition =
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit);
        AutomationElementCollection edits =
            root.FindAll(TreeScope.Descendants, editCondition);

        foreach (AutomationElement edit in edits)
        {
            if (!IsReconciliationControlAvailable(
                    edit,
                    requireEnabled))
            {
                continue;
            }

            try
            {
                object labeledByValue = edit.GetCurrentPropertyValue(
                    AutomationElement.LabeledByProperty);
                if (labeledByValue is AutomationElement label
                    && NormalizeLabel(label.Current.Name) == "档案名称")
                {
                    return edit;
                }
            }
            catch
            {
                // Continue with label proximity matching.
            }
        }

        AutomationElementCollection allElements = root.FindAll(
            TreeScope.Descendants,
            System.Windows.Automation.Condition.TrueCondition);
        foreach (AutomationElement label in allElements)
        {
            try
            {
                if (NormalizeLabel(label.Current.Name) != "档案名称")
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            AutomationElement? ancestor =
                TreeWalker.ControlViewWalker.GetParent(label);
            for (int depth = 0;
                 depth < 4 && ancestor is not null;
                 depth++)
            {
                try
                {
                    ControlType ancestorType =
                        ancestor.Current.ControlType;
                    if (ancestorType == ControlType.Window
                        || ancestorType == ControlType.Document)
                    {
                        break;
                    }

                    AutomationElement[] nearbyEdits = ancestor
                        .FindAll(TreeScope.Descendants, editCondition)
                        .Cast<AutomationElement>()
                        .Where(edit =>
                            IsReconciliationControlAvailable(
                                edit,
                                requireEnabled))
                        .ToArray();
                    if (nearbyEdits.Length == 1)
                    {
                        return nearbyEdits[0];
                    }

                    ancestor =
                        TreeWalker.ControlViewWalker.GetParent(ancestor);
                }
                catch
                {
                    break;
                }
            }
        }

        return null;
    }

    private static AutomationElement? FindReconciliationQueryButton(
        AutomationElement root,
        bool requireEnabled)
    {
        System.Windows.Automation.Condition buttonCondition =
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button);
        AutomationElementCollection buttons =
            root.FindAll(TreeScope.Descendants, buttonCondition);

        foreach (AutomationElement button in buttons)
        {
            try
            {
                if (IsReconciliationControlAvailable(
                        button,
                        requireEnabled)
                    && string.Equals(
                        button.Current.Name.Trim(),
                        "查询",
                        StringComparison.Ordinal)
                    && (!requireEnabled
                        || button.TryGetCurrentPattern(
                            InvokePattern.Pattern,
                            out _)))
                {
                    return button;
                }
            }
            catch
            {
                // Continue past stale accessibility elements.
            }
        }

        return null;
    }

    private static bool IsReconciliationControlAvailable(
        AutomationElement element,
        bool requireEnabled)
    {
        try
        {
            return !element.Current.IsOffscreen
                && (!requireEnabled || element.Current.IsEnabled);
        }
        catch
        {
            return false;
        }
    }

    private static ReconciliationAutomationGridState
        ReadReconciliationAutomationGridState(
            AutomationElement root,
            string archiveName)
    {
        AutomationElement? grid =
            FindReconciliationResultGrid(root);
        if (grid is null)
        {
            return new ReconciliationAutomationGridState(
                false,
                "no-grid",
                false);
        }

        try
        {
            AutomationElement? archiveHeader =
                FindColumnHeader(grid, "档案名称");
            if (archiveHeader is null)
            {
                return new ReconciliationAutomationGridState(
                    false,
                    "grid-without-archive-name-column",
                    false);
            }

            AutomationElementCollection descendants = grid.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            List<string> signatureParts = [];
            HashSet<string> matchingRows =
                new(StringComparer.Ordinal);

            foreach (AutomationElement element in descendants)
            {
                string name;
                ControlType controlType;
                try
                {
                    name = element.Current.Name.Trim();
                    controlType = element.Current.ControlType;
                }
                catch
                {
                    continue;
                }

                if (name.Length == 0)
                {
                    continue;
                }

                if (signatureParts.Count < 600)
                {
                    signatureParts.Add(
                        $"{controlType.ProgrammaticName}:{name}");
                }

                if (!string.Equals(
                        name,
                        archiveName,
                        StringComparison.Ordinal)
                    || !IsInColumn(element, archiveHeader))
                {
                    continue;
                }

                AutomationElement? row =
                    FindDataItemAncestor(element, grid);
                matchingRows.Add(GetAutomationElementKey(
                    row ?? element));
            }

            return new ReconciliationAutomationGridState(
                true,
                string.Join('\u001f', signatureParts),
                matchingRows.Count > 0);
        }
        catch
        {
            return new ReconciliationAutomationGridState(
                false,
                "grid-unavailable",
                false);
        }
    }

    private static AutomationRowState
        ReadAutomationReconciliationRowState(
            IntPtr windowHandle,
            string archiveName)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            return ReadAutomationReconciliationRowState(
                root,
                archiveName);
        }
        catch
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "系统可访问性结果行不可用");
        }
    }

    private static AutomationRowState
        ReadAutomationReconciliationRowState(
            AutomationElement root,
            string archiveName)
    {
        AutomationElement? grid =
            FindReconciliationResultGrid(root);
        if (grid is null)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "未找到电子其他会计资料收集结果表格");
        }

        AutomationElement? archiveHeader =
            FindColumnHeader(grid, "档案名称");
        AutomationElement? attachmentHeader =
            FindColumnHeader(grid, "附件数量");
        if (archiveHeader is null || attachmentHeader is null)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "结果表格缺少档案名称或附件数量列");
        }

        Dictionary<string, AutomationElement> matchingRows =
            FindReconciliationMatchingRows(
                grid,
                archiveHeader,
                archiveName);
        if (matchingRows.Count == 0)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "未找到精确匹配的档案名称结果行");
        }

        if (matchingRows.Count > 1)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "结果表格存在多个相同档案名称");
        }

        AutomationElement exactRow =
            matchingRows.Values.Single();
        int? attachmentCount =
            ReadAttachmentCount(
                exactRow,
                attachmentHeader);
        AutomationElement? uploadAction =
            FindRowUploadAction(exactRow);
        return new AutomationRowState(
            true,
            attachmentCount,
            uploadAction is not null,
            attachmentCount is null
                ? "无法从精确匹配档案行读取附件数量"
                : null);
    }

    private static AutomationUploadStartResult
        TryStartAutomationReconciliationUpload(
            IntPtr windowHandle,
            string archiveName)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            AutomationElement? grid =
                FindReconciliationResultGrid(root);
            AutomationElement? archiveHeader = grid is null
                ? null
                : FindColumnHeader(grid, "档案名称");
            if (grid is null || archiveHeader is null)
            {
                return AutomationUploadStartResult.Failure(
                    "未找到电子其他会计资料收集结果表格");
            }

            Dictionary<string, AutomationElement> matchingRows =
                FindReconciliationMatchingRows(
                    grid,
                    archiveHeader,
                    archiveName);
            if (matchingRows.Count != 1)
            {
                return AutomationUploadStartResult.Failure(
                    "无法唯一定位当前档案名称结果行");
            }

            AutomationElement exactRow =
                matchingRows.Values.Single();
            AutomationElement? attachmentHeader =
                FindColumnHeader(grid, "附件数量");
            int? count = attachmentHeader is null
                ? null
                : ReadAttachmentCount(
                    exactRow,
                    attachmentHeader);
            if (count != 0)
            {
                return AutomationUploadStartResult.Failure(
                    count is null
                        ? "上传前无法读取附件数量"
                        : "上传前附件数量已发生变化");
            }

            AutomationElement? uploadAction =
                FindRowUploadAction(exactRow);
            if (uploadAction is null
                || !uploadAction.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out object invokeValue)
                || invokeValue
                    is not InvokePattern invokePattern)
            {
                return AutomationUploadStartResult.Failure(
                    "当前档案结果行没有唯一可调用的“上传”控件");
            }

            HashSet<string> baselineMessages =
                CaptureAutomationUploadMessages(root);
            HashSet<string> baselineDialogs =
                CaptureOpenFileDialogIds();
            invokePattern.Invoke();
            return new AutomationUploadStartResult(
                true,
                baselineMessages,
                baselineDialogs,
                null);
        }
        catch
        {
            return AutomationUploadStartResult.Failure(
                "系统可访问性上传操作失败");
        }
    }

    private static AutomationUploadState
        ReadAutomationReconciliationUploadState(
            IntPtr windowHandle,
            string archiveName,
            IReadOnlySet<string> baselineMessageIds)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            AutomationRowState rowState =
                ReadAutomationReconciliationRowState(
                    root,
                    archiveName);
            string? newSuccess = null;
            string? newFailure = null;
            AutomationElementCollection descendants =
                root.FindAll(
                    TreeScope.Descendants,
                    System.Windows.Automation.Condition
                        .TrueCondition);

            foreach (AutomationElement element in descendants)
            {
                try
                {
                    string name =
                        element.Current.Name.Trim();
                    if (name.Length == 0
                        || baselineMessageIds.Contains(
                            GetAutomationElementKey(element)))
                    {
                        continue;
                    }

                    if (string.Equals(
                            name,
                            "上传成功！",
                            StringComparison.Ordinal))
                    {
                        newSuccess = name;
                    }
                    else if ((name.Contains(
                                 "上传",
                                 StringComparison.Ordinal)
                              && name.Contains(
                                  "失败",
                                  StringComparison.Ordinal))
                             || (name.Contains(
                                     "失败",
                                     StringComparison.Ordinal)
                                 && name.Contains(
                                     "文件",
                                     StringComparison.Ordinal)))
                    {
                        newFailure = name;
                    }
                }
                catch
                {
                    // Continue past stale accessibility elements.
                }
            }

            return new AutomationUploadState(
                true,
                rowState.ExactMatch,
                rowState.AttachmentCount,
                newSuccess is not null,
                newFailure,
                rowState.Error);
        }
        catch
        {
            return new AutomationUploadState(
                false,
                false,
                null,
                false,
                null,
                "系统可访问性页面状态不可用");
        }
    }

    private static Dictionary<string, AutomationElement>
        FindReconciliationMatchingRows(
            AutomationElement grid,
            AutomationElement archiveHeader,
            string archiveName)
    {
        AutomationElementCollection descendants =
            grid.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
        Dictionary<string, AutomationElement> matchingRows =
            new(StringComparer.Ordinal);

        foreach (AutomationElement element in descendants)
        {
            try
            {
                if (!string.Equals(
                        element.Current.Name.Trim(),
                        archiveName,
                        StringComparison.Ordinal)
                    || !IsInColumn(
                        element,
                        archiveHeader))
                {
                    continue;
                }

                AutomationElement? row =
                    FindDataItemAncestor(element, grid);
                if (row is not null)
                {
                    matchingRows.TryAdd(
                        GetAutomationElementKey(row),
                        row);
                }
            }
            catch
            {
                // Continue past stale accessibility elements.
            }
        }

        return matchingRows;
    }

    private static AutomationElement? FindReconciliationResultGrid(
        AutomationElement root)
    {
        OrCondition gridCondition = new(
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.DataGrid),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Table));
        AutomationElementCollection grids =
            root.FindAll(TreeScope.Descendants, gridCondition);

        foreach (AutomationElement grid in grids)
        {
            if (!IsUsableElement(grid))
            {
                continue;
            }

            try
            {
                if (FindColumnHeader(grid, "档案名称") is not null)
                {
                    return grid;
                }
            }
            catch
            {
                // Continue past stale grids.
            }
        }

        return null;
    }

    private sealed record ReconciliationDomSetResult(
        string Status,
        string BeforeSignature,
        bool BeforeExactMatch);

    private sealed record ReconciliationDomQueryState(
        bool ControlsPresent,
        string InputValue,
        bool Loading,
        bool ResultAvailable,
        string Signature,
        int ExactMatchCount);

    private sealed record ReconciliationAutomationStartResult(
        bool Started,
        string BeforeSignature,
        bool BeforeExactMatch,
        string? Error);

    private sealed record ReconciliationAutomationQueryState(
        bool ControlsPresent,
        bool Loading,
        bool ResultAvailable,
        string Signature,
        bool ExactMatch,
        string InputValue);

    private sealed record ReconciliationAutomationGridState(
        bool ResultAvailable,
        string Signature,
        bool ExactMatch);
}
