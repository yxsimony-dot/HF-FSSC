## Why

现有“发送回单”流程只能按 PDF 文件名查询业务流水号，查询成功后仍需人工逐个点击“上传”、选择对应文件并整理已完成文件。需要将查询后的上传、成功确认和文件归档串成受控的顺序流程，同时避免重复上传已有附件的业务。

## What Changes

- 在顶部任务栏增加“已完成目录”的选择入口、路径显示和持久化，并要求回单目录与已完成目录不能是同一目录。
- 在批次开始时同时保存业务流水号和对应 PDF 完整路径，查询到精确匹配记录后读取该行“附件数量”。
- 当附件数量大于 `0` 时跳过上传，将该项标记为“已有附件”，且不移动本地 PDF。
- 当附件数量为 `0` 时，精确定位同一结果行的“上传”操作，并选择当前业务流水号对应的 PDF。
- 上传后出现“上传成功！”提示或同一结果行附件数量由 `0` 变为 `1`，任一条件满足即判定上传成功。
- 继续坚持 DOM 优先、Windows UI Automation 回退；文件选择框通过控件语义和完整文件路径操作，禁止使用固定坐标或鼠标模拟。
- 严格顺序处理文件；当前文件查询和上传流程完成或失败后，才处理下一文件。
- 批次全部处理结束后，仅将本批次实际上传成功的 PDF 移动到已完成目录；查询失败、上传失败、已有附件和移动失败的文件保留在原目录。
- 扩展批次汇总，分别展示查询失败、已有附件、上传失败和移动失败；全部文件均实际上传并移动成功时显示统一成功结果。

## Capabilities

### New Capabilities

- `receipt-upload-and-completion`: 定义已完成目录配置、查询结果附件判断、对应 PDF 顺序上传、上传成功判定、批次后文件移动和分类汇总。

### Modified Capabilities

无。

## Impact

- 修改 `FSSCWebLauncher/MainWindow.xaml` 和 `FSSCWebLauncher/MainWindow.xaml.cs`，增加已完成目录控件并扩展批处理状态机。
- 修改 `FSSCWebLauncher/LauncherSettingsStore.cs`，持久化已完成目录。
- 修改 `FSSCWebLauncher/ReceiptFileQueue.cs`，在队列快照中保留业务流水号和 PDF 完整路径。
- 修改 `FSSCWebLauncher/ReceiptPageClient.cs`，读取附件数量、定位匹配行上传入口、选择文件并等待成功依据。
- 扩展批次结果模型、结果窗口及验证程序；继续使用现有 .NET 8、WPF、WebView2 和 Windows UI Automation，不增加第三方运行依赖。
