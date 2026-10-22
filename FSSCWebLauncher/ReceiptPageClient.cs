using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace FSSCWebLauncher;

internal sealed class ReceiptPageClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private delegate bool EnumWindowsCallback(
        IntPtr windowHandle,
        IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder text,
        int maximumCount);

    private const string ClickReceiptMenuScript = """
        (() => {
            const normalize = value => (value ?? "").replace(/\s+/g, "");
            const isVisible = element => {
                const style = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rect.width > 0
                    && rect.height > 0;
            };

            const menuItems = Array.from(
                document.querySelectorAll("li.ant-menu-item, [role='menuitem']")
            );
            const target = menuItems.find(element =>
                isVisible(element)
                && normalize(element.innerText || element.textContent) === "电子回单"
            );

            if (!target) {
                return "not-found";
            }

            if (target.getAttribute("aria-disabled") === "true") {
                return "disabled";
            }

            target.scrollIntoView({ block: "nearest", inline: "nearest" });
            target.click();
            return "clicked";
        })()
        """;

    private const string ExpandArchiveCollectionScript = """
        (() => {
            const normalize = value => (value ?? "").replace(/\s+/g, "");
            const isVisible = element => {
                const style = window.getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rect.width > 0
                    && rect.height > 0;
            };

            const titles = Array.from(
                document.querySelectorAll(".ant-menu-submenu-title, [role='menuitem']")
            );
            const target = titles.find(element =>
                isVisible(element)
                && normalize(element.innerText || element.textContent) === "档案收集"
            );

            if (!target) {
                return "not-found";
            }

            const submenu = target.closest(".ant-menu-submenu");
            if (submenu?.classList.contains("ant-menu-submenu-open")) {
                return "already-open";
            }

            target.click();
            return "expanded";
        })()
        """;

    private const string QueryControlsReadyScript = """
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
                    if (normalize(label.innerText || label.textContent) !== "业务流水号") {
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
            const findQueryButton = () => Array.from(document.querySelectorAll(
                "button, [role='button'], input[type='button'], input[type='submit']"
            )).find(element => {
                const text = element.tagName === "INPUT"
                    ? element.value
                    : element.innerText || element.textContent;
                return isVisible(element)
                    && normalize(text) === "查询"
                    && !element.disabled
                    && element.getAttribute("aria-disabled") !== "true";
            });

            return Boolean(findInput() && findQueryButton());
        })()
        """;

    private const string VirtualGridHelperScript = """
        (() => {
            const version = 1;
            if (window.__fsscReceiptVirtualGrid?.version === version) {
                return;
            }

            const normalizeLabel = value => (value ?? "")
                .replace(/\s+/g, "")
                .replace(/[：:]$/, "");
            const normalizeCell = value => (value ?? "")
                .replace(/\s+/g, " ")
                .trim();
            const rootSelector = ".osp-root, .ag-root";
            const headerSelector = ".osp-header-cell, .ag-header-cell";
            const rowSelector = ".osp-row, .ag-row, [role='row']";
            const cellSelector = ".osp-cell, .ag-cell, [role='gridcell']";
            const viewportSelector = [
                ".osp-center-cols-viewport",
                ".osp-body-horizontal-scroll-viewport",
                ".ag-center-cols-viewport",
                ".ag-body-horizontal-scroll-viewport"
            ].join(",");
            const sessions = new Map();

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
            const findRoot = () => Array.from(
                document.querySelectorAll(rootSelector)
            ).find(root =>
                isVisible(root)
                && root.querySelector(`${headerSelector}, ${rowSelector}`)
            ) ?? null;
            const columnId = element => {
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
            const rowKey = row => {
                for (const attribute of [
                    "row-id",
                    "data-row-id",
                    "row-index",
                    "data-row-index",
                    "aria-rowindex"
                ]) {
                    const value = row.getAttribute(attribute);
                    if (value !== null && value !== "") {
                        return `${attribute}:${value}`;
                    }
                }

                const rect = row.getBoundingClientRect();
                return `position:${Math.round(rect.top)}`;
            };
            const rows = root => Array.from(
                root.querySelectorAll(rowSelector)
            ).filter(row =>
                !row.matches(".osp-header-row, .ag-header-row")
                && !row.closest(".osp-header-row, .ag-header-row")
            );
            const viewports = root => Array.from(
                root.querySelectorAll(viewportSelector)
            ).filter(viewport =>
                viewport.scrollWidth > viewport.clientWidth + 1
            );
            const currentPosition = root => {
                const viewport = viewports(root)[0];
                return viewport ? Math.round(viewport.scrollLeft) : 0;
            };
            const setPosition = (root, position) => {
                for (const viewport of viewports(root)) {
                    const maximum = Math.max(
                        0,
                        viewport.scrollWidth - viewport.clientWidth
                    );
                    const next = Math.max(0, Math.min(maximum, position));
                    viewport.scrollLeft = next;
                    viewport.dispatchEvent(new Event("scroll", {
                        bubbles: true
                    }));
                }
            };
            const positions = root => {
                const candidates = viewports(root);
                if (candidates.length === 0) {
                    return [0];
                }

                const maximum = Math.max(...candidates.map(viewport =>
                    Math.max(
                        0,
                        viewport.scrollWidth - viewport.clientWidth
                    )
                ));
                const width = Math.max(
                    200,
                    ...candidates.map(viewport => viewport.clientWidth || 0)
                );
                const step = Math.max(160, Math.floor(width * 0.8));
                const result = [];
                for (let position = 0; position < maximum; position += step) {
                    result.push(position);
                }
                result.push(maximum);
                return Array.from(new Set(result.map(Math.round)));
            };
            const createSession = target => ({
                target,
                root: null,
                startedAt: performance.now(),
                loadingSeen: false,
                initialized: false,
                positions: [],
                cursor: 0,
                positionPending: false,
                returningToAttachment: false,
                complete: false,
                serialColumns: new Set(),
                attachmentColumns: new Set(),
                exactRows: new Set(),
                attachments: new Map(),
                attachmentPosition: null
            });
            const clearScan = session => {
                session.initialized = false;
                session.positions = [];
                session.cursor = 0;
                session.positionPending = false;
                session.returningToAttachment = false;
                session.complete = false;
                session.serialColumns.clear();
                session.attachmentColumns.clear();
                session.exactRows.clear();
                session.attachments.clear();
                session.attachmentPosition = null;
            };
            const scan = session => {
                const root = session.root;
                const position = currentPosition(root);
                const headers = Array.from(
                    root.querySelectorAll(headerSelector)
                );
                for (const header of headers) {
                    const id = columnId(header);
                    const label = normalizeLabel(
                        header.innerText || header.textContent
                    );
                    if (!id) {
                        continue;
                    }

                    if (label === "业务流水号") {
                        session.serialColumns.add(id);
                    } else if (label === "附件数量") {
                        session.attachmentColumns.add(id);
                        session.attachmentPosition = position;
                    }
                }

                for (const row of rows(root)) {
                    const key = rowKey(row);
                    for (const cell of row.querySelectorAll(cellSelector)) {
                        const id = columnId(cell);
                        if (!id) {
                            continue;
                        }

                        const text = normalizeCell(
                            cell.innerText || cell.textContent
                        );
                        if (session.serialColumns.has(id)
                            && text === session.target) {
                            session.exactRows.add(key);
                        }
                        if (session.attachmentColumns.has(id)) {
                            session.attachments.set(key, text);
                            session.attachmentPosition = position;
                        }
                    }
                }
            };
            const exactRowKey = target => {
                const session = sessions.get(target);
                if (!session || session.exactRows.size !== 1) {
                    return null;
                }

                return Array.from(session.exactRows)[0];
            };
            const getRowFragments = target => {
                const session = sessions.get(target);
                const key = exactRowKey(target);
                if (!session?.root || key === null) {
                    return [];
                }

                return rows(session.root).filter(row => rowKey(row) === key);
            };
            const getUploadActions = target => Array.from(new Set(
                getRowFragments(target).flatMap(row =>
                    Array.from(row.querySelectorAll(
                        "a, button, [role='button'], [role='link']"
                    ))
                )
            )).filter(element =>
                isVisible(element)
                && normalizeLabel(element.innerText || element.textContent)
                    === "上传"
                && !element.disabled
                && element.getAttribute("aria-disabled") !== "true"
            );
            const getFileInputs = target => Array.from(new Set(
                getRowFragments(target).flatMap(row =>
                    Array.from(row.querySelectorAll("input[type='file']"))
                )
            )).filter(input => !input.disabled);
            const markRows = (target, token) => {
                const fragments = getRowFragments(target);
                for (const row of fragments) {
                    row.setAttribute("data-fssc-receipt-row", token);
                }
                return fragments.length;
            };
            const makeResult = (
                session,
                available = true,
                signaturePrefix = "virtual"
            ) => {
                const exactRows = Array.from(session.exactRows).sort();
                const attachmentEntries = Array.from(
                    session.attachments.entries()
                ).sort(([left], [right]) => left.localeCompare(right));
                const key = exactRows.length === 1 ? exactRows[0] : null;
                const attachmentText = key === null
                    ? ""
                    : session.attachments.get(key) ?? "";
                const attachmentCount = /^\d+$/.test(attachmentText)
                    ? Number(attachmentText)
                    : null;
                const uploadAvailable = key !== null
                    && getUploadActions(session.target).length === 1;
                return {
                    available,
                    scanComplete: session.complete,
                    signature: [
                        signaturePrefix,
                        session.complete ? "complete" : "scanning",
                        session.cursor,
                        session.positionPending ? "pending" : "ready",
                        exactRows.join(","),
                        attachmentEntries.map(
                            ([row, value]) => `${row}=${value}`
                        ).join(",")
                    ].join("#"),
                    exactMatchCount: exactRows.length,
                    attachmentCount,
                    attachmentText,
                    uploadAvailable
                };
            };
            const unavailable = signature => ({
                available: false,
                scanComplete: false,
                signature,
                exactMatchCount: 0,
                attachmentCount: null,
                attachmentText: "",
                uploadAvailable: false
            });
            const peek = target => {
                const root = findRoot();
                if (!root) {
                    return unavailable("no-virtual-grid");
                }

                const serialColumns = new Set();
                for (const header of root.querySelectorAll(headerSelector)) {
                    if (normalizeLabel(
                            header.innerText || header.textContent
                        ) === "业务流水号") {
                        const id = columnId(header);
                        if (id) {
                            serialColumns.add(id);
                        }
                    }
                }

                const exactRows = new Set();
                for (const row of rows(root)) {
                    for (const cell of row.querySelectorAll(cellSelector)) {
                        if (serialColumns.has(columnId(cell))
                            && normalizeCell(
                                cell.innerText || cell.textContent
                            ) === target) {
                            exactRows.add(rowKey(row));
                        }
                    }
                }

                return {
                    available: true,
                    scanComplete: false,
                    signature: `virtual-peek#${normalizeCell(
                        root.innerText || root.textContent
                    )}`,
                    exactMatchCount: exactRows.size,
                    attachmentCount: null,
                    attachmentText: "",
                    uploadAvailable: false
                };
            };
            const begin = target => {
                const before = peek(target);
                sessions.set(target, createSession(target));
                return before;
            };
            const read = (target, options = {}) => {
                const root = findRoot();
                if (!root) {
                    return unavailable("no-virtual-grid");
                }

                let session = sessions.get(target);
                if (!session) {
                    session = createSession(target);
                    sessions.set(target, session);
                }

                if (session.root !== root) {
                    session.root = root;
                    clearScan(session);
                }

                if (options.loading) {
                    session.loadingSeen = true;
                    clearScan(session);
                    return makeResult(session, true, "virtual-loading");
                }

                if (session.loadingSeen) {
                    session.loadingSeen = false;
                    clearScan(session);
                }

                if (performance.now() - session.startedAt < 80) {
                    return makeResult(session, true, "virtual-waiting");
                }

                if (!session.initialized) {
                    session.positions = positions(root);
                    session.cursor = 0;
                    session.initialized = true;
                    session.positionPending = true;
                    setPosition(root, session.positions[0]);
                    return makeResult(session);
                }

                session.positionPending = false;
                scan(session);

                if (session.returningToAttachment) {
                    session.returningToAttachment = false;
                    session.complete = true;
                    return makeResult(session);
                }

                if (!session.complete
                    && session.cursor < session.positions.length - 1) {
                    session.cursor++;
                    session.positionPending = true;
                    setPosition(root, session.positions[session.cursor]);
                    return makeResult(session);
                }

                const current = currentPosition(root);
                if (!session.complete
                    && session.attachmentPosition !== null
                    && Math.abs(current - session.attachmentPosition) > 2) {
                    session.returningToAttachment = true;
                    session.positionPending = true;
                    setPosition(root, session.attachmentPosition);
                    return makeResult(session);
                }

                session.complete = true;
                return makeResult(session);
            };

            window.__fsscReceiptVirtualGrid = {
                version,
                begin,
                peek,
                read,
                hasGrid: () => Boolean(findRoot()),
                getRowFragments,
                getUploadActions,
                getFileInputs,
                markRows
            };
        })();
        """;

    private const string SetReceiptSerialScriptTemplate = """
        (() => {
            const target = __SERIAL__;
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
                    if (normalize(label.innerText || label.textContent) !== "业务流水号") {
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
            const findResultTable = () => Array.from(
                document.querySelectorAll("table")
            ).find(table =>
                isVisible(table)
                && Array.from(table.querySelectorAll("thead th")).some(header =>
                    normalize(header.innerText || header.textContent) === "业务流水号"
                )
            );
            const tableState = () => {
                const table = findResultTable();
                if (!table) {
                    const virtual = window.__fsscReceiptVirtualGrid?.peek(
                        target
                    );
                    return virtual?.available
                        ? {
                            signature: virtual.signature,
                            exactMatch: virtual.exactMatchCount > 0
                        }
                        : { signature: "no-table", exactMatch: false };
                }

                const headers = Array.from(table.querySelectorAll("thead th"));
                const serialColumn = headers.findIndex(header =>
                    normalize(header.innerText || header.textContent)
                        === "业务流水号"
                );
                const rows = Array.from(table.querySelectorAll("tbody tr"))
                    .filter(isVisible)
                    .map(row => Array.from(row.querySelectorAll("td"))
                        .map(cell => (cell.innerText || cell.textContent || "")
                            .replace(/\s+/g, " ")
                            .trim()));
                const wrapper = table.closest(
                    ".ant-table-wrapper, .ant-table, .el-table, .ivu-table-wrapper"
                );
                const emptyText = wrapper?.querySelector(
                    ".ant-empty-description, .ant-table-placeholder, .el-table__empty-text, .ivu-table-tip"
                )?.textContent?.replace(/\s+/g, " ").trim() ?? "";
                return {
                    signature: `${rows
                        .map(cells => cells.join("\u001f"))
                        .join("\u001e")}#${emptyText}`,
                    exactMatch: serialColumn >= 0 && rows.some(cells =>
                        cells[serialColumn] === target
                    )
                };
            };

            const before = tableState();
            window.__fsscReceiptVirtualGrid?.begin(target);
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
            const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
            if (setter) {
                setter.call(input, target);
            } else {
                input.value = target;
            }

            input.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
            input.dispatchEvent(new Event("change", { bubbles: true, composed: true }));
            input.focus();
            return {
                status: "ready",
                beforeSignature: before.signature,
                beforeExactMatch: before.exactMatch
            };
        })()
        """;

    private const string ClickQueryButtonScript = """
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
            const button = Array.from(document.querySelectorAll(
                "button, [role='button'], input[type='button'], input[type='submit']"
            )).find(element => {
                const text = element.tagName === "INPUT"
                    ? element.value
                    : element.innerText || element.textContent;
                return isVisible(element)
                    && normalize(text) === "查询"
                    && !element.disabled
                    && element.getAttribute("aria-disabled") !== "true";
            });

            if (!button) {
                return "not-found";
            }

            const active = document.activeElement;
            if (active instanceof HTMLInputElement
                || active instanceof HTMLTextAreaElement) {
                window.__fsscReceiptVirtualGrid?.begin(active.value);
            }
            button.scrollIntoView({ block: "nearest", inline: "nearest" });
            button.click();
            return "clicked";
        })()
        """;

    private const string ReadQueryStateScriptTemplate = """
        (() => {
            const target = __SERIAL__;
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
                        !== "业务流水号") {
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
            const findQueryButton = () => Array.from(document.querySelectorAll(
                "button, [role='button'], input[type='button'], input[type='submit']"
            )).find(element => {
                const text = element.tagName === "INPUT"
                    ? element.value
                    : element.innerText || element.textContent;
                return isVisible(element) && normalizeLabel(text) === "查询";
            });
            const findResultTable = () => Array.from(
                document.querySelectorAll("table")
            ).find(table =>
                isVisible(table)
                && Array.from(table.querySelectorAll("thead th")).some(header =>
                    normalizeLabel(header.innerText || header.textContent)
                        === "业务流水号"
                )
            );

            const input = findInput();
            const button = findQueryButton();
            const table = findResultTable();
            let signature = "no-table";
            let exactMatchCount = 0;
            let attachmentCount = null;
            let attachmentText = "";
            let uploadAvailable = false;

            if (table) {
                const headers = Array.from(table.querySelectorAll("thead th"));
                const serialColumn = headers.findIndex(header =>
                    normalizeLabel(header.innerText || header.textContent)
                        === "业务流水号"
                );
                const attachmentColumn = headers.findIndex(header =>
                    normalizeLabel(header.innerText || header.textContent)
                        === "附件数量"
                );
                const rows = Array.from(table.querySelectorAll("tbody tr"))
                    .filter(isVisible);
                const rowValues = rows.map(row => Array.from(
                    row.querySelectorAll("td")
                ).map(cell => normalizeCell(
                    cell.innerText || cell.textContent
                )));
                const exactRows = serialColumn < 0
                    ? []
                    : rows.filter((row, index) =>
                        rowValues[index][serialColumn] === target
                    );
                exactMatchCount = exactRows.length;
                if (exactRows.length === 1) {
                    const exactRow = exactRows[0];
                    const cells = Array.from(exactRow.querySelectorAll("td"));
                    if (attachmentColumn >= 0 && attachmentColumn < cells.length) {
                        attachmentText = normalizeCell(
                            cells[attachmentColumn].innerText
                                || cells[attachmentColumn].textContent
                        );
                        attachmentCount = /^\d+$/.test(attachmentText)
                            ? Number(attachmentText)
                            : null;
                    }

                    uploadAvailable = Array.from(exactRow.querySelectorAll(
                        "a, button, [role='button'], [role='link']"
                    )).some(element =>
                        isVisible(element)
                        && normalizeLabel(element.innerText || element.textContent)
                            === "上传"
                        && !element.disabled
                        && element.getAttribute("aria-disabled") !== "true"
                    );
                }

                const wrapper = table.closest(
                    ".ant-table-wrapper, .ant-table, .el-table, .ivu-table-wrapper"
                );
                const emptyText = wrapper?.querySelector(
                    ".ant-empty-description, .ant-table-placeholder, .el-table__empty-text, .ivu-table-tip"
                )?.textContent;
                signature = `${rowValues
                    .map(cells => cells.join("\u001f"))
                    .join("\u001e")}#${normalizeCell(emptyText)}`;
            }

            const tableScope = table?.closest(
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
            const loading = buttonLoading || isVisible(spinner);
            if (!table && window.__fsscReceiptVirtualGrid?.hasGrid()) {
                const virtual = window.__fsscReceiptVirtualGrid.read(
                    target,
                    { loading }
                );
                signature = virtual.signature;
                exactMatchCount = virtual.exactMatchCount;
                attachmentCount = virtual.attachmentCount;
                attachmentText = virtual.attachmentText;
                uploadAvailable = virtual.uploadAvailable;
            }

            return {
                controlsPresent: Boolean(input && button),
                inputValue: input?.value ?? "",
                loading,
                signature,
                exactMatchCount,
                attachmentCount,
                attachmentText,
                uploadAvailable
            };
        })()
        """;

    private const string PrepareUploadScriptTemplate = """
        (() => {
            const target = __SERIAL__;
            const token = __TOKEN__;
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
            const findExactRow = () => {
                const tables = Array.from(document.querySelectorAll("table"))
                    .filter(isVisible);
                const matches = [];
                for (const table of tables) {
                    const headers = Array.from(table.querySelectorAll("thead th"));
                    const serialColumn = headers.findIndex(header =>
                        normalizeLabel(header.innerText || header.textContent)
                            === "业务流水号"
                    );
                    const attachmentColumn = headers.findIndex(header =>
                        normalizeLabel(header.innerText || header.textContent)
                            === "附件数量"
                    );
                    if (serialColumn < 0 || attachmentColumn < 0) {
                        continue;
                    }

                    for (const row of Array.from(
                        table.querySelectorAll("tbody tr")
                    ).filter(isVisible)) {
                        const cells = Array.from(row.querySelectorAll("td"));
                        if (normalizeCell(
                                cells[serialColumn]?.innerText
                                    || cells[serialColumn]?.textContent
                            ) === target) {
                            matches.push({ row, cells, attachmentColumn });
                        }
                    }
                }

                return matches.length === 1 ? matches[0] : null;
            };
            const exact = findExactRow();
            let attachmentCount = null;
            let uploadActions = [];
            let rowInputs = [];
            let markExactRows = () => 0;
            if (exact) {
                const attachmentText = normalizeCell(
                    exact.cells[exact.attachmentColumn]?.innerText
                        || exact.cells[exact.attachmentColumn]?.textContent
                );
                attachmentCount = /^\d+$/.test(attachmentText)
                    ? Number(attachmentText)
                    : null;
                uploadActions = Array.from(exact.row.querySelectorAll(
                    "a, button, [role='button'], [role='link']"
                )).filter(element =>
                    isVisible(element)
                    && normalizeLabel(element.innerText || element.textContent)
                        === "上传"
                    && !element.disabled
                    && element.getAttribute("aria-disabled") !== "true"
                );
                rowInputs = Array.from(
                    exact.row.querySelectorAll("input[type='file']")
                ).filter(input => !input.disabled);
                markExactRows = value => {
                    exact.row.setAttribute(
                        "data-fssc-receipt-row",
                        value
                    );
                    return 1;
                };
            } else {
                const helper = window.__fsscReceiptVirtualGrid;
                const virtual = helper?.read(target, { loading: false });
                if (!virtual?.available
                    || !virtual.scanComplete
                    || virtual.exactMatchCount !== 1) {
                    return {
                        status: "row-unavailable",
                        attachmentCount: null
                    };
                }

                attachmentCount = virtual.attachmentCount;
                uploadActions = helper.getUploadActions(target);
                rowInputs = helper.getFileInputs(target);
                markExactRows = value => helper.markRows(target, value);
            }

            if (attachmentCount !== 0) {
                return {
                    status: attachmentCount === null
                        ? "attachment-unreadable"
                        : "attachment-changed",
                    attachmentCount
                };
            }

            if (uploadActions.length !== 1) {
                return { status: "upload-unavailable", attachmentCount };
            }

            const previous = window.__fsscReceiptUpload;
            try {
                previous?.observer?.disconnect();
                if (previous?.fileClickHandler) {
                    document.removeEventListener(
                        "click",
                        previous.fileClickHandler,
                        true
                    );
                }
            } catch {
                // Start a fresh observation even if old cleanup is unavailable.
            }

            const messageSelectors = [
                ".ant-message-notice-content",
                ".ant-message-custom-content",
                ".ant-notification-notice-message",
                ".el-message",
                ".ivu-message-notice-content",
                "[role='alert']"
            ].join(",");
            const messageElements = () => Array.from(
                document.querySelectorAll(messageSelectors)
            );
            const baselineSuccessNodes = new Set(
                messageElements().filter(element =>
                    normalizeCell(element.innerText || element.textContent)
                        === "上传成功！"
                )
            );
            const state = {
                token,
                successObserved: false,
                failureText: "",
                baselineSuccessNodes,
                clearedBaselineSuccessNodes: new WeakSet(),
                observer: null,
                fileClickHandler: null
            };
            const inspectMessage = element => {
                if (!(element instanceof Element)) {
                    return;
                }

                const text = normalizeCell(
                    element.innerText || element.textContent
                );
                if (state.baselineSuccessNodes.has(element)
                    && text !== "上传成功！") {
                    state.clearedBaselineSuccessNodes.add(element);
                }

                if (text === "上传成功！"
                    && (!state.baselineSuccessNodes.has(element)
                        || state.clearedBaselineSuccessNodes.has(element))) {
                    state.successObserved = true;
                } else if (/上传.*失败|失败.*上传/.test(text)) {
                    state.failureText = text;
                }
            };
            state.observer = new MutationObserver(records => {
                for (const record of records) {
                    if (record.type === "characterData") {
                        inspectMessage(record.target.parentElement);
                    }

                    for (const node of record.addedNodes) {
                        if (!(node instanceof Element)) {
                            continue;
                        }

                        if (node.matches(messageSelectors)) {
                            inspectMessage(node);
                        }
                        for (const element of node.querySelectorAll(
                            messageSelectors
                        )) {
                            inspectMessage(element);
                        }
                    }
                }
            });
            state.observer.observe(document.body, {
                childList: true,
                subtree: true,
                characterData: true
            });
            state.fileClickHandler = event => {
                const input = event.target instanceof HTMLInputElement
                    && event.target.type === "file"
                    ? event.target
                    : null;
                if (!input) {
                    return;
                }

                input.setAttribute(
                    "data-fssc-receipt-file-input",
                    token
                );
                event.preventDefault();
                event.stopImmediatePropagation();
            };
            document.addEventListener(
                "click",
                state.fileClickHandler,
                true
            );
            window.__fsscReceiptUpload = state;
            markExactRows(token);

            if (rowInputs.length === 1) {
                rowInputs[0].setAttribute(
                    "data-fssc-receipt-file-input",
                    token
                );
                return { status: "input-ready", attachmentCount };
            }

            uploadActions[0].scrollIntoView({
                block: "nearest",
                inline: "nearest"
            });
            uploadActions[0].click();
            return { status: "upload-clicked", attachmentCount };
        })()
        """;

    private const string FindPreparedFileInputScriptTemplate = """
        (() => {
            const token = __TOKEN__;
            const state = window.__fsscReceiptUpload;
            if (!state || state.token !== token) {
                return "observation-unavailable";
            }

            const tagged = Array.from(document.querySelectorAll(
                "input[type='file'][data-fssc-receipt-file-input]"
            )).filter(input =>
                input.getAttribute("data-fssc-receipt-file-input") === token
                && !input.disabled
            );
            if (tagged.length === 1) {
                return "ready";
            }

            const row = document.querySelector(
                `[data-fssc-receipt-row="${token}"]`
            );
            const rowInputs = row
                ? Array.from(row.querySelectorAll("input[type='file']"))
                    .filter(input => !input.disabled)
                : [];
            if (rowInputs.length === 1) {
                rowInputs[0].setAttribute(
                    "data-fssc-receipt-file-input",
                    token
                );
                return "ready";
            }

            return "not-found";
        })()
        """;

    private const string ReadUploadStateScriptTemplate = """
        (() => {
            const target = __SERIAL__;
            const token = __TOKEN__;
            const normalizeLabel = value => (value ?? "")
                .replace(/\s+/g, "")
                .replace(/[：:]$/, "");
            const normalizeCell = value => (value ?? "")
                .replace(/\s+/g, " ")
                .trim();
            const state = window.__fsscReceiptUpload;
            const tables = Array.from(document.querySelectorAll("table"));
            const matches = [];
            for (const table of tables) {
                const headers = Array.from(table.querySelectorAll("thead th"));
                const serialColumn = headers.findIndex(header =>
                    normalizeLabel(header.innerText || header.textContent)
                        === "业务流水号"
                );
                const attachmentColumn = headers.findIndex(header =>
                    normalizeLabel(header.innerText || header.textContent)
                        === "附件数量"
                );
                if (serialColumn < 0 || attachmentColumn < 0) {
                    continue;
                }

                for (const row of table.querySelectorAll("tbody tr")) {
                    const cells = Array.from(row.querySelectorAll("td"));
                    if (normalizeCell(
                            cells[serialColumn]?.innerText
                                || cells[serialColumn]?.textContent
                        ) === target) {
                        matches.push({ cells, attachmentColumn });
                    }
                }
            }

            let exactMatchCount = matches.length;
            let attachmentCount = null;
            if (matches.length === 1) {
                const text = normalizeCell(
                    matches[0].cells[matches[0].attachmentColumn]?.innerText
                        || matches[0].cells[matches[0].attachmentColumn]?.textContent
                );
                attachmentCount = /^\d+$/.test(text) ? Number(text) : null;
            }
            if (window.__fsscReceiptVirtualGrid?.hasGrid()) {
                const virtual = window.__fsscReceiptVirtualGrid.read(
                    target,
                    { loading: false }
                );
                exactMatchCount = virtual.exactMatchCount;
                attachmentCount = virtual.attachmentCount;
            }

            return {
                observationAvailable: Boolean(state && state.token === token),
                successMessage: Boolean(
                    state && state.token === token && state.successObserved
                ),
                failureText: state && state.token === token
                    ? state.failureText
                    : "",
                exactMatchCount,
                attachmentCount
            };
        })()
        """;

    private const string CleanupUploadObservationScriptTemplate = """
        (() => {
            const token = __TOKEN__;
            const state = window.__fsscReceiptUpload;
            if (!state || state.token !== token) {
                return false;
            }

            try {
                state.observer?.disconnect();
                if (state.fileClickHandler) {
                    document.removeEventListener(
                        "click",
                        state.fileClickHandler,
                        true
                    );
                }
            } finally {
                document.querySelectorAll(
                    `[data-fssc-receipt-file-input="${token}"]`
                ).forEach(element =>
                    element.removeAttribute("data-fssc-receipt-file-input")
                );
                document.querySelectorAll(
                    `[data-fssc-receipt-row="${token}"]`
                ).forEach(element =>
                    element.removeAttribute("data-fssc-receipt-row")
                );
                delete window.__fsscReceiptUpload;
            }

            return true;
        })()
        """;

    private readonly WebView2 _browser;
    private readonly Func<IntPtr> _windowHandleProvider;
    private readonly ReceiptPageClientTiming _timing;

    public ReceiptPageClient(
        WebView2 browser,
        Func<IntPtr> windowHandleProvider,
        ReceiptPageClientTiming? timing = null)
    {
        _browser = browser;
        _windowHandleProvider = windowHandleProvider;
        _timing = timing ?? ReceiptPageClientTiming.Production;
    }

    public async Task<bool> NavigateToReceiptPageAsync()
    {
        if (_browser.CoreWebView2 is null)
        {
            return false;
        }

        if (await WaitForQueryControlsAsync(TimeSpan.FromMilliseconds(600)))
        {
            return true;
        }

        if (await ExecuteDomStringAsync(ClickReceiptMenuScript) == "clicked"
            && await WaitForQueryControlsAsync(_timing.PageReadyTimeout))
        {
            return true;
        }

        string? expandResult =
            await ExecuteDomStringAsync(ExpandArchiveCollectionScript);
        if (expandResult is "expanded" or "already-open")
        {
            await Task.Delay(250);
            if (await ExecuteDomStringAsync(ClickReceiptMenuScript) == "clicked"
                && await WaitForQueryControlsAsync(_timing.PageReadyTimeout))
            {
                return true;
            }
        }

        IntPtr windowHandle = _windowHandleProvider();
        if (windowHandle == IntPtr.Zero
            || !await TryInvokeReceiptMenuWithAutomationAsync(windowHandle))
        {
            return false;
        }

        return await WaitForQueryControlsAsync(_timing.PageReadyTimeout);
    }

    public async Task<ReceiptQueryResult> QueryAsync(string serialNumber)
    {
        ReceiptRowQueryResult? domResult =
            await QueryWithDomAsync(serialNumber);
        if (domResult is not null)
        {
            return domResult.ToQueryResult(serialNumber);
        }

        IntPtr windowHandle = _windowHandleProvider();
        if (windowHandle == IntPtr.Zero)
        {
            return new ReceiptQueryResult(
                serialNumber,
                ReceiptQueryStatus.PageError,
                "无法访问应用窗口");
        }

        ReceiptRowQueryResult automationResult =
            await QueryWithAutomationAsync(windowHandle, serialNumber);
        return automationResult.ToQueryResult(serialNumber);
    }

    public async Task<ReceiptBatchItemResult> ProcessAsync(
        ReceiptWorkItem workItem)
    {
        ReceiptRowQueryResult? domQuery =
            await QueryWithDomAsync(workItem.SerialNumber);
        if (domQuery is not null)
        {
            ReceiptBatchItemResult? terminal =
                CreatePreUploadResult(workItem, domQuery);
            if (terminal is not null)
            {
                return terminal;
            }

            string? sourceError = ValidateSourceFile(workItem.SourcePath);
            if (sourceError is not null)
            {
                return new ReceiptBatchItemResult(
                    workItem,
                    ReceiptItemStatus.UploadFailed,
                    sourceError);
            }

            DomUploadResult domUpload =
                await UploadWithDomAsync(workItem);
            if (domUpload.Handled)
            {
                return new ReceiptBatchItemResult(
                    workItem,
                    domUpload.Succeeded
                        ? ReceiptItemStatus.UploadedPendingMove
                        : ReceiptItemStatus.UploadFailed,
                    domUpload.Error);
            }
        }

        IntPtr windowHandle = _windowHandleProvider();
        if (windowHandle == IntPtr.Zero)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.UploadFailed,
                "无法访问应用窗口完成上传");
        }

        return await ProcessWithAutomationAsync(windowHandle, workItem);
    }

    private async Task<ReceiptRowQueryResult?> QueryWithDomAsync(
        string serialNumber)
    {
        string setScript = WithVirtualGridHelper(
            SetReceiptSerialScriptTemplate.Replace(
                "__SERIAL__",
                JsonSerializer.Serialize(serialNumber),
                StringComparison.Ordinal));
        DomSetResult? setResult =
            await ExecuteDomObjectAsync<DomSetResult>(setScript);

        if (setResult?.Status != "ready")
        {
            return null;
        }

        await Task.Delay(150);
        if (await ExecuteDomStringAsync(ClickQueryButtonScript) != "clicked")
        {
            return null;
        }

        string stateScript = WithVirtualGridHelper(
            ReadQueryStateScriptTemplate.Replace(
                "__SERIAL__",
                JsonSerializer.Serialize(serialNumber),
                StringComparison.Ordinal));
        Stopwatch stopwatch = Stopwatch.StartNew();
        string? lastSignature = null;
        int stableReadings = 0;
        int unavailableReadings = 0;
        bool loadingObserved = false;
        bool resultChanged = false;

        while (stopwatch.Elapsed < _timing.QueryTimeout)
        {
            DomQueryState? state =
                await ExecuteDomObjectAsync<DomQueryState>(stateScript);
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
                    "查询控件已从页面中消失");
            }

            if (!string.Equals(
                    state.InputValue,
                    serialNumber,
                    StringComparison.Ordinal))
            {
                return new ReceiptRowQueryResult(
                    ReceiptQueryStatus.PageError,
                    null,
                    false,
                    "业务流水号输入值在查询期间发生变化");
            }

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
                    && stopwatch.Elapsed >= _timing.UnobservedQuerySettleDelay);

            if (!state.Loading
                && stableReadings >= 2
                && lifecycleObserved)
            {
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
                        "查询结果存在多个相同业务流水号，无法安全上传");
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

    private async Task<ReceiptRowQueryResult> QueryWithAutomationAsync(
        IntPtr windowHandle,
        string serialNumber)
    {
        AutomationStartResult startResult = await Task.Run(
            () => TryStartAutomationQuery(windowHandle, serialNumber));
        if (!startResult.Started)
        {
            return new ReceiptRowQueryResult(
                ReceiptQueryStatus.PageError,
                null,
                false,
                startResult.Error ?? "无法通过系统控件执行查询");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string? lastSignature = null;
        int stableReadings = 0;
        bool loadingObserved = false;
        bool resultChanged = false;
        int unavailableReadings = 0;
        int inputMismatchReadings = 0;

        while (stopwatch.Elapsed < _timing.QueryTimeout)
        {
            AutomationQueryState state = await Task.Run(
                () => ReadAutomationQueryState(windowHandle, serialNumber));
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
                    serialNumber,
                    StringComparison.Ordinal))
            {
                inputMismatchReadings++;
                if (inputMismatchReadings >= 3)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.PageError,
                        null,
                        false,
                        "业务流水号输入值在查询期间发生变化");
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
                    && stopwatch.Elapsed >= _timing.UnobservedQuerySettleDelay);

            if (!state.Loading
                && stableReadings >= 2
                && lifecycleObserved)
            {
                if (!state.ExactMatch)
                {
                    return new ReceiptRowQueryResult(
                        ReceiptQueryStatus.NotFound,
                        null,
                        false,
                        null);
                }

                AutomationRowState rowState = await Task.Run(
                    () => ReadAutomationRowState(
                        windowHandle,
                        serialNumber));
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

    private static string? ValidateSourceFile(string sourcePath)
    {
        if (!Path.IsPathFullyQualified(sourcePath)
            || !File.Exists(sourcePath))
        {
            return "对应 PDF 已不存在";
        }

        try
        {
            using FileStream stream = File.Open(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return stream.CanRead ? null : "对应 PDF 无法读取";
        }
        catch (UnauthorizedAccessException)
        {
            return "没有权限读取对应 PDF";
        }
        catch (IOException)
        {
            return "对应 PDF 无法读取";
        }
    }

    private static ReceiptBatchItemResult? CreatePreUploadResult(
        ReceiptWorkItem workItem,
        ReceiptRowQueryResult query)
    {
        if (query.Status != ReceiptQueryStatus.Success)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.QueryFailed,
                query.FailureReason);
        }

        if (query.AttachmentCount is > 0)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.ExistingAttachment);
        }

        if (query.AttachmentCount != 0)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.UploadFailed,
                "无法读取附件数量，未执行上传");
        }

        if (!query.UploadAvailable)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.UploadFailed,
                "当前结果行未找到可调用的“上传”操作");
        }

        return null;
    }

    private async Task<DomUploadResult> UploadWithDomAsync(
        ReceiptWorkItem workItem)
    {
        string token = Guid.NewGuid().ToString("N");
        IReadOnlySet<string> baselineDialogIds =
            await Task.Run(CaptureOpenFileDialogIds);
        string prepareScript = WithVirtualGridHelper(
            ReplaceUploadScriptTokens(
                PrepareUploadScriptTemplate,
                workItem.SerialNumber,
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
            await CleanupDomUploadAsync(token);
            return DomUploadResult.Failure(
                prepared.Status switch
                {
                    "row-unavailable" =>
                        "上传前无法重新定位精确匹配结果行",
                    "attachment-unreadable" =>
                        "上传前无法读取附件数量",
                    "attachment-changed" =>
                        "上传前附件数量已发生变化",
                    "upload-unavailable" =>
                        "当前结果行未找到唯一的“上传”操作",
                    _ => "无法准备网页上传"
                });
        }

        bool inputReady = await WaitForPreparedFileInputAsync(token);
        if (!inputReady)
        {
            if (prepared.Status == "input-ready")
            {
                await CleanupDomUploadAsync(token);
                return DomUploadResult.Fallback();
            }

            FileDialogSubmitResult dialogResult = await Task.Run(
                () => TrySubmitOpenFileDialog(
                    baselineDialogIds,
                    workItem.SourcePath,
                    _timing.FileDialogTimeout));
            if (!dialogResult.Submitted)
            {
                await CleanupDomUploadAsync(token);
                return DomUploadResult.Failure(
                    dialogResult.Error
                        ?? "未找到本次上传打开的文件选择框");
            }
        }
        else
        {
            bool fileSet = await TrySetDomFileInputAsync(
                token,
                workItem.SourcePath);
            if (!fileSet)
            {
                await CleanupDomUploadAsync(token);
                return prepared.Status == "input-ready"
                    ? DomUploadResult.Fallback()
                    : DomUploadResult.Failure(
                        "无法将当前 PDF 设置到网页文件输入控件");
            }
        }

        string stateScript = WithVirtualGridHelper(
            ReplaceUploadScriptTokens(
                ReadUploadStateScriptTemplate,
                workItem.SerialNumber,
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
                if (state is null || !state.ObservationAvailable)
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
                        && state.AttachmentCount == 1))
                {
                    return DomUploadResult.Success();
                }

                if (!string.IsNullOrWhiteSpace(state.FailureText))
                {
                    return DomUploadResult.Failure(
                        $"网页提示上传失败：{state.FailureText}");
                }

                if (state.ExactMatchCount > 1)
                {
                    return DomUploadResult.Failure(
                        "上传后出现多个相同业务流水号，无法确认结果");
                }

                await Task.Delay(_timing.PollInterval);
            }

            return DomUploadResult.Failure(
                "上传超时：未出现新的“上传成功！”且附件数量未由 0 变为 1");
        }
        finally
        {
            await CleanupDomUploadAsync(token);
        }
    }

    private async Task<bool> WaitForPreparedFileInputAsync(string token)
    {
        string script = FindPreparedFileInputScriptTemplate.Replace(
            "__TOKEN__",
            JsonSerializer.Serialize(token),
            StringComparison.Ordinal);
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < _timing.FileInputWaitTimeout)
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

            await Task.Delay(_timing.FileInputPollInterval);
        }

        return false;
    }

    private async Task<bool> TrySetDomFileInputAsync(
        string token,
        string sourcePath)
    {
        try
        {
            string documentJson =
                await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.getDocument",
                    """{"depth":1,"pierce":true}""");
            using JsonDocument document = JsonDocument.Parse(documentJson);
            int rootNodeId = document.RootElement
                .GetProperty("root")
                .GetProperty("nodeId")
                .GetInt32();

            string selector =
                $"input[type='file'][data-fssc-receipt-file-input=\"{token}\"]";
            string queryJson =
                await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.querySelector",
                    JsonSerializer.Serialize(new
                    {
                        nodeId = rootNodeId,
                        selector
                    }));
            using JsonDocument query = JsonDocument.Parse(queryJson);
            int nodeId = query.RootElement.GetProperty("nodeId").GetInt32();
            if (nodeId == 0)
            {
                return false;
            }

            await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "DOM.setFileInputFiles",
                JsonSerializer.Serialize(new
                {
                    nodeId,
                    files = new[] { Path.GetFullPath(sourcePath) }
                }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task CleanupDomUploadAsync(string token)
    {
        string script = CleanupUploadObservationScriptTemplate.Replace(
            "__TOKEN__",
            JsonSerializer.Serialize(token),
            StringComparison.Ordinal);
        await ExecuteDomBooleanAsync(script);
    }

    private static string ReplaceUploadScriptTokens(
        string template,
        string serialNumber,
        string token)
    {
        return template
            .Replace(
                "__SERIAL__",
                JsonSerializer.Serialize(serialNumber),
                StringComparison.Ordinal)
            .Replace(
                "__TOKEN__",
                JsonSerializer.Serialize(token),
                StringComparison.Ordinal);
    }

    private static string WithVirtualGridHelper(string script)
    {
        return $"{VirtualGridHelperScript}{Environment.NewLine}{script}";
    }

    private async Task<ReceiptBatchItemResult> ProcessWithAutomationAsync(
        IntPtr windowHandle,
        ReceiptWorkItem workItem)
    {
        ReceiptRowQueryResult query =
            await QueryWithAutomationAsync(
                windowHandle,
                workItem.SerialNumber);
        ReceiptBatchItemResult? terminal =
            CreatePreUploadResult(workItem, query);
        if (terminal is not null)
        {
            return terminal;
        }

        string? sourceError = ValidateSourceFile(workItem.SourcePath);
        if (sourceError is not null)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.UploadFailed,
                sourceError);
        }

        AutomationUploadStartResult start = await Task.Run(
            () => TryStartAutomationUpload(
                windowHandle,
                workItem.SerialNumber));
        if (!start.Started)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.UploadFailed,
                start.Error ?? "无法调用当前结果行的“上传”操作");
        }

        FileDialogSubmitResult dialogResult = await Task.Run(
            () => TrySubmitOpenFileDialog(
                start.BaselineDialogIds,
                workItem.SourcePath,
                _timing.FileDialogTimeout));
        if (!dialogResult.Submitted)
        {
            return new ReceiptBatchItemResult(
                workItem,
                ReceiptItemStatus.UploadFailed,
                dialogResult.Error ?? "未找到可操作的文件选择对话框");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < _timing.UploadTimeout)
        {
            AutomationUploadState state = await Task.Run(
                () => ReadAutomationUploadState(
                    windowHandle,
                    workItem.SerialNumber,
                    start.BaselineMessageIds));
            if (!state.PageAvailable)
            {
                return new ReceiptBatchItemResult(
                    workItem,
                    ReceiptItemStatus.UploadFailed,
                    state.Error ?? "上传后无法读取页面状态");
            }

            if (state.NewSuccessMessage
                || (state.ExactMatch
                    && state.AttachmentCount == 1))
            {
                return new ReceiptBatchItemResult(
                    workItem,
                    ReceiptItemStatus.UploadedPendingMove);
            }

            if (!string.IsNullOrWhiteSpace(state.NewFailureMessage))
            {
                return new ReceiptBatchItemResult(
                    workItem,
                    ReceiptItemStatus.UploadFailed,
                    $"网页提示上传失败：{state.NewFailureMessage}");
            }

            await Task.Delay(_timing.PollInterval);
        }

        return new ReceiptBatchItemResult(
            workItem,
            ReceiptItemStatus.UploadFailed,
            "上传超时：未出现新的“上传成功！”且附件数量未由 0 变为 1");
    }

    private async Task<bool> WaitForQueryControlsAsync(TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        do
        {
            if (await ExecuteDomBooleanAsync(QueryControlsReadyScript))
            {
                return true;
            }

            IntPtr windowHandle = _windowHandleProvider();
            if (windowHandle != IntPtr.Zero
                && await Task.Run(() =>
                    AreAutomationQueryControlsReady(windowHandle)))
            {
                return true;
            }

            await Task.Delay(_timing.PollInterval);
        }
        while (stopwatch.Elapsed < timeout);

        return false;
    }

    private async Task<bool> TryInvokeReceiptMenuWithAutomationAsync(
        IntPtr windowHandle)
    {
        if (await Task.Run(
                () => TryInvokeAutomationMenuItem(windowHandle, "电子回单")))
        {
            return true;
        }

        bool parentReady = await Task.Run(
            () => TryExpandAutomationMenuItem(windowHandle, "档案收集"));
        if (!parentReady)
        {
            return false;
        }

        await Task.Delay(250);
        return await Task.Run(
            () => TryInvokeAutomationMenuItem(windowHandle, "电子回单"));
    }

    private async Task<string?> ExecuteDomStringAsync(string script)
    {
        try
        {
            string result =
                await _browser.CoreWebView2.ExecuteScriptAsync(script);
            return JsonSerializer.Deserialize<string>(result);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ExecuteDomBooleanAsync(string script)
    {
        try
        {
            string result =
                await _browser.CoreWebView2.ExecuteScriptAsync(script);
            return JsonSerializer.Deserialize<bool>(result);
        }
        catch
        {
            return false;
        }
    }

    private async Task<T?> ExecuteDomObjectAsync<T>(string script)
        where T : class
    {
        try
        {
            string result =
                await _browser.CoreWebView2.ExecuteScriptAsync(script);
            return JsonSerializer.Deserialize<T>(result, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool AreAutomationQueryControlsReady(IntPtr windowHandle)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(windowHandle);
            AutomationElement? input = FindReceiptInput(root);
            AutomationElement? button = FindQueryButton(root);
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

    private static AutomationStartResult TryStartAutomationQuery(
        IntPtr windowHandle,
        string serialNumber)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(windowHandle);
            AutomationElement? input = FindReceiptInput(root);
            AutomationElement? button = FindQueryButton(root);
            if (input is null || button is null)
            {
                return new AutomationStartResult(
                    false,
                    "no-grid",
                    false,
                    "未找到业务流水号输入框或查询按钮");
            }

            GridState beforeState = ReadGridState(root, serialNumber);
            if (!TrySetAutomationValue(input, serialNumber))
            {
                return new AutomationStartResult(
                    false,
                    beforeState.Signature,
                    beforeState.ExactMatch,
                    "业务流水号输入框不支持值设置");
            }

            Thread.Sleep(120);
            if (!button.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out object pattern)
                || pattern is not InvokePattern invokePattern)
            {
                return new AutomationStartResult(
                    false,
                    beforeState.Signature,
                    beforeState.ExactMatch,
                    "查询按钮不支持调用");
            }

            invokePattern.Invoke();
            return new AutomationStartResult(
                true,
                beforeState.Signature,
                beforeState.ExactMatch,
                null);
        }
        catch
        {
            return new AutomationStartResult(
                false,
                "no-grid",
                false,
                "系统可访问性查询操作失败");
        }
    }

    private static AutomationQueryState ReadAutomationQueryState(
        IntPtr windowHandle,
        string serialNumber)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(windowHandle);
            AutomationElement? input = FindReceiptInput(root);
            AutomationElement? button = FindQueryButton(root);
            if (input is null || button is null)
            {
                return new AutomationQueryState(
                    false,
                    false,
                    "no-grid",
                    false,
                    string.Empty);
            }

            GridState gridState = ReadGridState(root, serialNumber);
            bool loading =
                !button.Current.IsEnabled || HasVisibleProgressIndicator(root);
            return new AutomationQueryState(
                true,
                loading,
                gridState.Signature,
                gridState.ExactMatch,
                ReadAutomationValue(input));
        }
        catch
        {
            return new AutomationQueryState(
                false,
                false,
                "no-grid",
                false,
                string.Empty);
        }
    }

    private static AutomationElement? FindReceiptInput(AutomationElement root)
    {
        System.Windows.Automation.Condition editCondition =
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit);
        AutomationElementCollection edits =
            root.FindAll(TreeScope.Descendants, editCondition);

        foreach (AutomationElement edit in edits)
        {
            if (!IsUsableElement(edit))
            {
                continue;
            }

            try
            {
                object labeledByValue = edit.GetCurrentPropertyValue(
                    AutomationElement.LabeledByProperty);
                if (labeledByValue is AutomationElement label
                    && NormalizeLabel(label.Current.Name) == "业务流水号")
                {
                    return edit;
                }
            }
            catch
            {
                // Some web accessibility providers do not expose LabeledBy.
            }
        }

        AutomationElementCollection allElements =
            root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
        foreach (AutomationElement label in allElements)
        {
            string name;
            try
            {
                name = label.Current.Name;
            }
            catch
            {
                continue;
            }

            if (NormalizeLabel(name) != "业务流水号")
            {
                continue;
            }

            AutomationElement? ancestor =
                TreeWalker.ControlViewWalker.GetParent(label);
            for (int depth = 0; depth < 4 && ancestor is not null; depth++)
            {
                ControlType ancestorType = ancestor.Current.ControlType;
                if (ancestorType == ControlType.Window
                    || ancestorType == ControlType.Document)
                {
                    break;
                }

                AutomationElementCollection nearbyEdits =
                    ancestor.FindAll(TreeScope.Descendants, editCondition);
                AutomationElement[] usableEdits = nearbyEdits
                    .Cast<AutomationElement>()
                    .Where(IsUsableElement)
                    .ToArray();
                if (usableEdits.Length == 1)
                {
                    return usableEdits[0];
                }

                ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor);
            }
        }

        AutomationElement[] placeholderMatches = edits
            .Cast<AutomationElement>()
            .Where(IsUsableElement)
            .Where(edit =>
            {
                try
                {
                    return string.Equals(
                        edit.Current.Name.Trim(),
                        "请输入",
                        StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();

        if (placeholderMatches.Length == 1)
        {
            return placeholderMatches[0];
        }

        return null;
    }

    private static AutomationElement? FindQueryButton(AutomationElement root)
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
                if (IsUsableElement(button)
                    && string.Equals(
                        button.Current.Name.Trim(),
                        "查询",
                        StringComparison.Ordinal)
                    && button.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out _))
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

    private static GridState ReadGridState(
        AutomationElement root,
        string serialNumber)
    {
        AutomationElement? grid = FindReceiptResultGrid(root);
        if (grid is null)
        {
            return new GridState("no-grid", false);
        }

        try
        {
            AutomationElement? serialHeader = FindSerialColumnHeader(grid);
            if (serialHeader is null)
            {
                return new GridState("grid-without-serial-column", false);
            }

            AutomationElementCollection descendants = grid.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            List<string> signatureParts = [];
            bool exactMatch = false;

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

                if ((controlType == ControlType.Text
                        || controlType == ControlType.Custom
                        || controlType == ControlType.DataItem
                        || controlType == ControlType.DataGrid)
                    && string.Equals(
                        name,
                        serialNumber,
                        StringComparison.Ordinal)
                    && IsInColumn(element, serialHeader))
                {
                    exactMatch = true;
                }
            }

            return new GridState(
                string.Join('\u001f', signatureParts),
                exactMatch);
        }
        catch
        {
            return new GridState("grid-unavailable", false);
        }
    }

    private static AutomationRowState ReadAutomationRowState(
        IntPtr windowHandle,
        string serialNumber)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            return ReadAutomationRowState(root, serialNumber);
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

    private static AutomationRowState ReadAutomationRowState(
        AutomationElement root,
        string serialNumber)
    {
        AutomationElement? grid = FindReceiptResultGrid(root);
        if (grid is null)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "未找到电子回单结果表格");
        }

        AutomationElement? serialHeader =
            FindColumnHeader(grid, "业务流水号");
        AutomationElement? attachmentHeader =
            FindColumnHeader(grid, "附件数量");
        if (serialHeader is null || attachmentHeader is null)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "结果表格缺少业务流水号或附件数量列");
        }

        AutomationElementCollection descendants = grid.FindAll(
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
                        serialNumber,
                        StringComparison.Ordinal)
                    || !IsInColumn(element, serialHeader))
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

        if (matchingRows.Count == 0)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "未找到精确匹配的业务流水号结果行");
        }

        if (matchingRows.Count > 1)
        {
            return new AutomationRowState(
                false,
                null,
                false,
                "结果表格存在多个相同业务流水号");
        }

        AutomationElement exactRow = matchingRows.Values.Single();
        int? attachmentCount =
            ReadAttachmentCount(exactRow, attachmentHeader);
        AutomationElement? uploadAction =
            FindRowUploadAction(exactRow);
        return new AutomationRowState(
            true,
            attachmentCount,
            uploadAction is not null,
            attachmentCount is null
                ? "无法从精确匹配行读取附件数量"
                : null);
    }

    private static AutomationElement? FindColumnHeader(
        AutomationElement grid,
        string headerName)
    {
        try
        {
            if (grid.TryGetCurrentPattern(
                    TablePattern.Pattern,
                    out object tablePatternValue)
                && tablePatternValue is TablePattern tablePattern)
            {
                AutomationElement? tableHeader = tablePattern
                    .Current
                    .GetColumnHeaders()
                    .FirstOrDefault(header =>
                        NormalizeLabel(header.Current.Name) == headerName);
                if (tableHeader is not null)
                {
                    return tableHeader;
                }
            }

            AutomationElementCollection descendants = grid.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            AutomationElement[] matches = descendants
                .Cast<AutomationElement>()
                .Where(element =>
                {
                    try
                    {
                        return NormalizeLabel(element.Current.Name)
                            == headerName;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();
            return matches.FirstOrDefault(element =>
            {
                try
                {
                    return element.Current.ControlType
                        == ControlType.HeaderItem;
                }
                catch
                {
                    return false;
                }
            }) ?? matches.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement? FindDataItemAncestor(
        AutomationElement element,
        AutomationElement grid)
    {
        AutomationElement? current = element;
        string gridKey = GetAutomationElementKey(grid);

        for (int depth = 0; depth < 12 && current is not null; depth++)
        {
            try
            {
                if (current.Current.ControlType == ControlType.DataItem)
                {
                    return current;
                }

                if (GetAutomationElementKey(current) == gridKey)
                {
                    return null;
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static int? ReadAttachmentCount(
        AutomationElement row,
        AutomationElement attachmentHeader)
    {
        try
        {
            AutomationElementCollection descendants = row.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            int[] values = descendants
                .Cast<AutomationElement>()
                .Where(element => IsInColumn(
                    element,
                    attachmentHeader))
                .Select(element =>
                {
                    try
                    {
                        return element.Current.Name.Trim();
                    }
                    catch
                    {
                        return string.Empty;
                    }
                })
                .Where(value => int.TryParse(value, out _))
                .Select(int.Parse)
                .Distinct()
                .ToArray();
            return values.Length == 1 ? values[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement? FindRowUploadAction(
        AutomationElement row)
    {
        try
        {
            AutomationElementCollection descendants = row.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            AutomationElement[] matches = descendants
                .Cast<AutomationElement>()
                .Where(element =>
                {
                    try
                    {
                        return IsUsableElement(element)
                            && string.Equals(
                                element.Current.Name.Trim(),
                                "上传",
                                StringComparison.Ordinal)
                            && element.TryGetCurrentPattern(
                                InvokePattern.Pattern,
                                out _);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static AutomationUploadStartResult TryStartAutomationUpload(
        IntPtr windowHandle,
        string serialNumber)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            AutomationElement? grid = FindReceiptResultGrid(root);
            AutomationElement? serialHeader = grid is null
                ? null
                : FindColumnHeader(grid, "业务流水号");
            if (grid is null || serialHeader is null)
            {
                return AutomationUploadStartResult.Failure(
                    "未找到电子回单结果表格");
            }

            AutomationElementCollection descendants = grid.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            Dictionary<string, AutomationElement> matchingRows =
                new(StringComparer.Ordinal);
            foreach (AutomationElement element in descendants)
            {
                try
                {
                    if (string.Equals(
                            element.Current.Name.Trim(),
                            serialNumber,
                            StringComparison.Ordinal)
                        && IsInColumn(element, serialHeader))
                    {
                        AutomationElement? row =
                            FindDataItemAncestor(element, grid);
                        if (row is not null)
                        {
                            matchingRows.TryAdd(
                                GetAutomationElementKey(row),
                                row);
                        }
                    }
                }
                catch
                {
                    // Continue past stale accessibility elements.
                }
            }

            if (matchingRows.Count != 1)
            {
                return AutomationUploadStartResult.Failure(
                    "无法唯一定位当前业务流水号结果行");
            }

            AutomationElement exactRow = matchingRows.Values.Single();
            AutomationElement? attachmentHeader =
                FindColumnHeader(grid, "附件数量");
            int? count = attachmentHeader is null
                ? null
                : ReadAttachmentCount(exactRow, attachmentHeader);
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
                || invokeValue is not InvokePattern invokePattern)
            {
                return AutomationUploadStartResult.Failure(
                    "当前结果行没有唯一可调用的“上传”控件");
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

    private static AutomationUploadState ReadAutomationUploadState(
        IntPtr windowHandle,
        string serialNumber,
        IReadOnlySet<string> baselineMessageIds)
    {
        try
        {
            AutomationElement root =
                AutomationElement.FromHandle(windowHandle);
            AutomationRowState rowState =
                ReadAutomationRowState(root, serialNumber);
            string? newSuccess = null;
            string? newFailure = null;
            AutomationElementCollection descendants = root.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);

            foreach (AutomationElement element in descendants)
            {
                try
                {
                    string name = element.Current.Name.Trim();
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

    private static HashSet<string> CaptureAutomationUploadMessages(
        AutomationElement root)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        try
        {
            AutomationElementCollection descendants = root.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            foreach (AutomationElement element in descendants)
            {
                try
                {
                    string name = element.Current.Name.Trim();
                    if (string.Equals(
                            name,
                            "上传成功！",
                            StringComparison.Ordinal)
                        || name.Contains(
                            "上传失败",
                            StringComparison.Ordinal))
                    {
                        ids.Add(GetAutomationElementKey(element));
                    }
                }
                catch
                {
                    // Ignore stale message elements.
                }
            }
        }
        catch
        {
            // An empty baseline is safe and still requires a current message.
        }

        return ids;
    }

    private static HashSet<string> CaptureOpenFileDialogIds()
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        try
        {
            foreach (AutomationElement window
                in EnumerateTopLevelAutomationWindows())
            {
                if (IsOpenFileDialog(window))
                {
                    ids.Add(GetAutomationElementKey(window));
                }
            }
        }
        catch
        {
            // Keep an empty baseline when the desktop tree is transient.
        }

        return ids;
    }

    private static FileDialogSubmitResult TrySubmitOpenFileDialog(
        IReadOnlySet<string> baselineDialogIds,
        string sourcePath,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                foreach (AutomationElement dialog
                    in EnumerateTopLevelAutomationWindows())
                {
                    if (!IsOpenFileDialog(dialog)
                        || baselineDialogIds.Contains(
                            GetAutomationElementKey(dialog)))
                    {
                        continue;
                    }

                    AutomationElement? fileNameInput =
                        FindFileNameInput(dialog);
                    AutomationElement? openButton =
                        FindOpenDialogButton(dialog);
                    if (fileNameInput is null || openButton is null)
                    {
                        continue;
                    }

                    if (!TrySetAutomationValue(
                            fileNameInput,
                            Path.GetFullPath(sourcePath)))
                    {
                        return new FileDialogSubmitResult(
                            false,
                            "文件选择框的“文件名”控件不支持值设置");
                    }

                    if (!openButton.TryGetCurrentPattern(
                            InvokePattern.Pattern,
                            out object pattern)
                        || pattern is not InvokePattern invokePattern)
                    {
                        return new FileDialogSubmitResult(
                            false,
                            "文件选择框的“打开”按钮不支持调用");
                    }

                    invokePattern.Invoke();
                    return new FileDialogSubmitResult(true, null);
                }
            }
            catch
            {
                // The dialog can be rebuilding while it appears.
            }

            Thread.Sleep(120);
        }

        return new FileDialogSubmitResult(
            false,
            "未找到本次上传打开的文件选择框");
    }

    private static IReadOnlyList<AutomationElement>
        EnumerateTopLevelAutomationWindows()
    {
        List<AutomationElement> windows = [];
        try
        {
            _ = EnumWindows((windowHandle, _) =>
            {
                try
                {
                    if (IsPotentialOpenFileDialogWindow(windowHandle))
                    {
                        windows.Add(
                            AutomationElement.FromHandle(windowHandle));
                    }
                }
                catch
                {
                    // Ignore top-level windows without an available UIA provider.
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Return any windows captured before desktop enumeration changed.
        }

        return windows;
    }

    private static bool IsPotentialOpenFileDialogWindow(
        IntPtr windowHandle)
    {
        StringBuilder className = new(256);
        _ = GetClassName(
            windowHandle,
            className,
            className.Capacity);
        if (string.Equals(
                className.ToString(),
                "#32770",
                StringComparison.Ordinal))
        {
            return true;
        }

        StringBuilder windowText = new(512);
        _ = GetWindowText(
            windowHandle,
            windowText,
            windowText.Capacity);
        string name = NormalizeLabel(windowText.ToString());
        return name.Contains("打开", StringComparison.Ordinal)
            || name.Contains("Open", StringComparison.OrdinalIgnoreCase)
            || name.Contains("选择文件", StringComparison.Ordinal);
    }

    private static bool IsOpenFileDialog(AutomationElement window)
    {
        try
        {
            if (!IsUsableElement(window))
            {
                return false;
            }

            string name = NormalizeLabel(window.Current.Name);
            if (!name.Contains("打开", StringComparison.Ordinal)
                && !name.Contains("Open", StringComparison.OrdinalIgnoreCase)
                && !name.Contains(
                    "选择文件",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return FindFileNameInput(window) is not null
                && FindOpenDialogButton(window) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static AutomationElement? FindFileNameInput(
        AutomationElement dialog)
    {
        try
        {
            AutomationElementCollection edits = dialog.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Edit));
            List<AutomationElement> semanticMatches = [];
            foreach (AutomationElement edit in edits)
            {
                if (!IsUsableElement(edit)
                    || !edit.TryGetCurrentPattern(
                        ValuePattern.Pattern,
                        out object value)
                    || value is not ValuePattern valuePattern
                    || valuePattern.Current.IsReadOnly)
                {
                    continue;
                }

                bool matches = false;
                try
                {
                    object labeledByValue = edit.GetCurrentPropertyValue(
                        AutomationElement.LabeledByProperty);
                    matches = labeledByValue is AutomationElement label
                        && NormalizeLabel(label.Current.Name)
                            .StartsWith(
                                "文件名",
                                StringComparison.Ordinal);
                }
                catch
                {
                    // Fall through to stable native dialog metadata.
                }

                string automationId = edit.Current.AutomationId;
                string name = NormalizeLabel(edit.Current.Name);
                if (matches
                    || automationId == "1148"
                    || name.StartsWith(
                        "文件名",
                        StringComparison.Ordinal)
                    || name.StartsWith(
                        "File name",
                        StringComparison.OrdinalIgnoreCase))
                {
                    semanticMatches.Add(edit);
                }
            }

            if (semanticMatches.Count == 1)
            {
                return semanticMatches[0];
            }

            if (semanticMatches.Count > 1)
            {
                return null;
            }

            AutomationElementCollection fileNameContainers = dialog.FindAll(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ComboBox),
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "1148")));
            List<AutomationElement> nativeDialogMatches = [];
            foreach (AutomationElement container in fileNameContainers)
            {
                if (!IsUsableElement(container))
                {
                    continue;
                }

                AutomationElementCollection containerEdits = container.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Edit));
                AutomationElement[] writableEdits = containerEdits
                    .Cast<AutomationElement>()
                    .Where(IsWritableAutomationValue)
                    .ToArray();
                if (writableEdits.Length == 1)
                {
                    nativeDialogMatches.Add(writableEdits[0]);
                }
            }

            return nativeDialogMatches.Count == 1
                ? nativeDialogMatches[0]
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWritableAutomationValue(AutomationElement element)
    {
        try
        {
            return IsUsableElement(element)
                && element.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out object value)
                && value is ValuePattern valuePattern
                && !valuePattern.Current.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private static AutomationElement? FindOpenDialogButton(
        AutomationElement dialog)
    {
        try
        {
            AutomationElementCollection buttons = dialog.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Button));
            AutomationElement[] matches = buttons
                .Cast<AutomationElement>()
                .Where(button =>
                {
                    try
                    {
                        string name =
                            NormalizeDialogButtonName(
                                button.Current.Name);
                        return IsUsableElement(button)
                            && (string.Equals(
                                    name,
                                    "打开",
                                    StringComparison.Ordinal)
                                || string.Equals(
                                    name,
                                    "Open",
                                    StringComparison.OrdinalIgnoreCase))
                            && button.TryGetCurrentPattern(
                                InvokePattern.Pattern,
                                out _);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeDialogButtonName(string? value)
    {
        string normalized = NormalizeLabel(value)
            .Replace("&", string.Empty, StringComparison.Ordinal);
        int acceleratorStart = normalized.IndexOf('(');
        return acceleratorStart > 0
            ? normalized[..acceleratorStart]
            : normalized;
    }

    private static string GetAutomationElementKey(
        AutomationElement element)
    {
        try
        {
            int[] runtimeId = element.GetRuntimeId();
            if (runtimeId.Length > 0)
            {
                return string.Join(".", runtimeId);
            }
        }
        catch
        {
            // Fall back to native handle and semantic properties.
        }

        try
        {
            return string.Join(
                "|",
                element.Current.NativeWindowHandle,
                element.Current.AutomationId,
                element.Current.ControlType.ProgrammaticName,
                element.Current.Name);
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private static AutomationElement? FindSerialColumnHeader(
        AutomationElement grid)
    {
        return FindColumnHeader(grid, "业务流水号");
    }

    private static bool IsInColumn(
        AutomationElement element,
        AutomationElement columnHeader)
    {
        try
        {
            string targetHeaderName =
                NormalizeLabel(columnHeader.Current.Name);
            if (element.TryGetCurrentPattern(
                    TableItemPattern.Pattern,
                    out object tableItemPatternValue)
                && tableItemPatternValue is TableItemPattern tableItemPattern
                && tableItemPattern.Current.GetColumnHeaderItems().Any(header =>
                    NormalizeLabel(header.Current.Name) == targetHeaderName))
            {
                return true;
            }
        }
        catch
        {
            // Fall through to other semantic column checks.
        }

        try
        {
            if (element.TryGetCurrentPattern(
                    GridItemPattern.Pattern,
                    out object gridItemPatternValue)
                && gridItemPatternValue is GridItemPattern gridItemPattern
                && columnHeader.TryGetCurrentPattern(
                    GridItemPattern.Pattern,
                    out object headerGridItemPatternValue)
                && headerGridItemPatternValue is GridItemPattern headerGridItemPattern)
            {
                return gridItemPattern.Current.Column
                    == headerGridItemPattern.Current.Column;
            }
        }
        catch
        {
            // Fall through to accessibility bounding rectangles.
        }

        try
        {
            System.Windows.Rect headerBounds =
                columnHeader.Current.BoundingRectangle;
            System.Windows.Rect elementBounds =
                element.Current.BoundingRectangle;
            if (headerBounds.IsEmpty || elementBounds.IsEmpty)
            {
                return false;
            }

            double elementCenter = elementBounds.Left + (elementBounds.Width / 2);
            return elementCenter >= headerBounds.Left
                && elementCenter <= headerBounds.Right;
        }
        catch
        {
            return false;
        }
    }

    private static AutomationElement? FindReceiptResultGrid(
        AutomationElement root)
    {
        System.Windows.Automation.Condition gridCondition =
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.DataGrid);
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
                AutomationElementCollection descendants =
                    grid.FindAll(
                        TreeScope.Descendants,
                        System.Windows.Automation.Condition.TrueCondition);
                if (descendants.Cast<AutomationElement>().Any(element =>
                        NormalizeLabel(element.Current.Name) == "业务流水号"))
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

    private static bool HasVisibleProgressIndicator(AutomationElement root)
    {
        try
        {
            System.Windows.Automation.Condition progressCondition =
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.ProgressBar);
            AutomationElementCollection indicators =
                root.FindAll(TreeScope.Descendants, progressCondition);
            return indicators
                .Cast<AutomationElement>()
                .Any(IsUsableElement);
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetAutomationValue(
        AutomationElement input,
        string value)
    {
        try
        {
            if (input.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out object valuePattern)
                && valuePattern is ValuePattern typedValuePattern
                && !typedValuePattern.Current.IsReadOnly)
            {
                typedValuePattern.SetValue(value);
                return true;
            }

        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string ReadAutomationValue(AutomationElement input)
    {
        try
        {
            if (input.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out object valuePattern)
                && valuePattern is ValuePattern typedValuePattern)
            {
                return typedValuePattern.Current.Value;
            }

        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static bool TryInvokeAutomationMenuItem(
        IntPtr windowHandle,
        string targetName)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(windowHandle);
            System.Windows.Automation.Condition menuItemCondition =
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.MenuItem);
            AutomationElementCollection menuItems =
                root.FindAll(TreeScope.Descendants, menuItemCondition);

            foreach (AutomationElement menuItem in menuItems)
            {
                string name = menuItem.Current.Name.Trim();
                bool nameMatches =
                    string.Equals(name, targetName, StringComparison.Ordinal)
                    || name.EndsWith(
                        $" {targetName}",
                        StringComparison.Ordinal);

                if (!nameMatches
                    || !IsUsableElement(menuItem)
                    || !menuItem.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out object pattern)
                    || pattern is not InvokePattern invokePattern)
                {
                    continue;
                }

                invokePattern.Invoke();
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryExpandAutomationMenuItem(
        IntPtr windowHandle,
        string targetName)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(windowHandle);
            System.Windows.Automation.Condition menuItemCondition =
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.MenuItem);
            AutomationElementCollection menuItems =
                root.FindAll(TreeScope.Descendants, menuItemCondition);

            foreach (AutomationElement menuItem in menuItems)
            {
                if (!string.Equals(
                        menuItem.Current.Name.Trim(),
                        targetName,
                        StringComparison.Ordinal)
                    || !IsUsableElement(menuItem)
                    || !menuItem.TryGetCurrentPattern(
                        ExpandCollapsePattern.Pattern,
                        out object pattern)
                    || pattern is not ExpandCollapsePattern expandCollapsePattern)
                {
                    continue;
                }

                if (expandCollapsePattern.Current.ExpandCollapseState
                    == ExpandCollapseState.Collapsed)
                {
                    expandCollapsePattern.Expand();
                }

                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsUsableElement(AutomationElement element)
    {
        try
        {
            return element.Current.IsEnabled && !element.Current.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeLabel(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string normalized = string.Concat(
            value.Where(character => !char.IsWhiteSpace(character)));
        return normalized.TrimEnd('：', ':');
    }

    private sealed record DomSetResult(
        string Status,
        string BeforeSignature,
        bool BeforeExactMatch);

    private sealed record DomQueryState(
        bool ControlsPresent,
        string InputValue,
        bool Loading,
        string Signature,
        int ExactMatchCount,
        int? AttachmentCount,
        string AttachmentText,
        bool UploadAvailable);

    private sealed record DomPrepareUploadResult(
        string Status,
        int? AttachmentCount);

    private sealed record DomUploadState(
        bool ObservationAvailable,
        bool SuccessMessage,
        string FailureText,
        int ExactMatchCount,
        int? AttachmentCount);

    private sealed record DomUploadResult(
        bool Handled,
        bool Succeeded,
        string? Error)
    {
        public static DomUploadResult Fallback()
        {
            return new DomUploadResult(false, false, null);
        }

        public static DomUploadResult Success()
        {
            return new DomUploadResult(true, true, null);
        }

        public static DomUploadResult Failure(string error)
        {
            return new DomUploadResult(true, false, error);
        }
    }

    private sealed record ReceiptRowQueryResult(
        ReceiptQueryStatus Status,
        int? AttachmentCount,
        bool UploadAvailable,
        string? Detail)
    {
        public string FailureReason => Status switch
        {
            ReceiptQueryStatus.NotFound => "未查询到精确匹配记录",
            ReceiptQueryStatus.Timeout => "查询超时",
            ReceiptQueryStatus.PageError => Detail ?? "页面操作异常",
            _ => string.Empty
        };

        public ReceiptQueryResult ToQueryResult(string serialNumber)
        {
            return new ReceiptQueryResult(
                serialNumber,
                Status,
                Detail);
        }
    }

    private sealed record AutomationStartResult(
        bool Started,
        string BeforeSignature,
        bool BeforeExactMatch,
        string? Error);

    private sealed record AutomationQueryState(
        bool ControlsPresent,
        bool Loading,
        string Signature,
        bool ExactMatch,
        string InputValue);

    private sealed record AutomationRowState(
        bool ExactMatch,
        int? AttachmentCount,
        bool UploadAvailable,
        string? Error);

    private sealed record AutomationUploadStartResult(
        bool Started,
        IReadOnlySet<string> BaselineMessageIds,
        IReadOnlySet<string> BaselineDialogIds,
        string? Error)
    {
        public static AutomationUploadStartResult Failure(string error)
        {
            return new AutomationUploadStartResult(
                false,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                error);
        }
    }

    private sealed record AutomationUploadState(
        bool PageAvailable,
        bool ExactMatch,
        int? AttachmentCount,
        bool NewSuccessMessage,
        string? NewFailureMessage,
        string? Error);

    private sealed record FileDialogSubmitResult(
        bool Submitted,
        string? Error);

    private sealed record GridState(string Signature, bool ExactMatch);
}

internal sealed record ReceiptPageClientTiming(
    TimeSpan PageReadyTimeout,
    TimeSpan QueryTimeout,
    TimeSpan UploadTimeout,
    TimeSpan UnobservedQuerySettleDelay,
    TimeSpan PollInterval,
    TimeSpan FileInputWaitTimeout,
    TimeSpan FileInputPollInterval,
    TimeSpan FileDialogTimeout)
{
    public static ReceiptPageClientTiming Production { get; } = new(
        PageReadyTimeout: TimeSpan.FromSeconds(15),
        QueryTimeout: TimeSpan.FromSeconds(20),
        UploadTimeout: TimeSpan.FromSeconds(30),
        UnobservedQuerySettleDelay: TimeSpan.FromSeconds(2),
        PollInterval: TimeSpan.FromMilliseconds(300),
        FileInputWaitTimeout: TimeSpan.FromSeconds(3),
        FileInputPollInterval: TimeSpan.FromMilliseconds(100),
        FileDialogTimeout: TimeSpan.FromSeconds(8));
}
