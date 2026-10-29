## Purpose

为“发送对账”增加可控的 Office 转 PDF 上传能力，使 Word 和 Excel 文件能够沿用现有精确查询、附件判断、上传、归档和结果汇总流程，同时隔离 WPS 卡死风险并保护原始文件。

## ADDED Requirements

### Requirement: 递归纳入受支持的 Office 对账文件
应用 SHALL 在递归读取“对账目录”及其子文件夹时，将 `doc`、`docx`、`xls` 和 `xlsx` 文件加入对账批次。应用 SHALL 使用原始文件名移除最后一个扩展名后的值作为“档案名称”，并 SHALL 保留原始文件相对于“对账目录”的子文件夹路径。

#### Scenario: Word 和 Excel 文件进入批次
- **WHEN** 对账目录包含 `合同.doc`、`说明.docx`、`明细.xls` 和 `报表.xlsx`
- **THEN** 应用将四个文件全部加入对账批次
- **THEN** 应用分别使用 `合同`、`说明`、`明细` 和 `报表` 查询档案名称

#### Scenario: 子文件夹和大小写扩展名
- **WHEN** 对账目录的子文件夹包含 `2026\资料.DOCX` 和 `2026\台账.XLSX`
- **THEN** 应用识别两个 Office 文件且不区分扩展名大小写
- **THEN** 应用保留 `2026` 子文件夹对应的相对路径

#### Scenario: 现有直接上传类型和排除类型保持不变
- **WHEN** 同一目录包含 PDF、支持的图片、Office 文件、压缩包和其他不支持文件
- **THEN** PDF 和支持的图片继续按现有规则直接上传
- **THEN** 四种受支持 Office 文件按本规格执行按需转换
- **THEN** 压缩包和其他不支持文件不进入对账批次

### Requirement: 仅在确认需要上传后转换 Office 文件
应用 SHALL 先使用原始 Office 文件对应的档案名称完成查询。仅当查询结果存在唯一精确匹配档案且该档案附件数量为 `0` 时，应用 SHALL 启动 Office 转 PDF。查询不到、结果不唯一、附件数量不可读或附件数量大于 `0` 时，应用 MUST NOT 启动转换。

#### Scenario: 唯一档案没有附件
- **WHEN** Office 文件对应的档案名称唯一精确匹配且附件数量为 `0`
- **THEN** 应用为该 Office 文件启动转 PDF
- **THEN** 应用在转换完成前不开始下一工作项

#### Scenario: 档案已经存在附件
- **WHEN** Office 文件对应的唯一精确匹配档案附件数量大于 `0`
- **THEN** 应用记录“已有附件”
- **THEN** 应用不转换、不上传且不移动原始 Office 文件

#### Scenario: 查询无法确定唯一目标
- **WHEN** 查询不到精确匹配档案、存在多个精确匹配档案或附件数量无法可靠读取
- **THEN** 应用不启动 Office 转 PDF
- **THEN** 应用保留原始 Office 文件并按查询阶段的对应结果汇总

### Requirement: 隔离并验证 Office 转 PDF
应用 SHALL 通过本机 WPS Office 自动化能力转换 `doc`、`docx`、`xls` 和 `xlsx`，并 SHALL 将转换运行在独立于主界面的受控进程中。转换 SHALL 受超时限制；无论转换正常结束、失败或超时，应用 SHALL 清理本次转换拥有的进程资源，并 MUST NOT 因单个文件转换卡住而无限阻塞主界面。

应用 SHALL 仅使用通过完整性校验的 PDF。校验 SHALL 至少确认文件存在、可读取、大小稳定且具有有效 PDF 起始和结束标记。转换进程正常报告失败时，应用 MUST NOT 因残留输出文件存在而判定转换成功。

#### Scenario: Word 文件转换成功
- **WHEN** WPS Writer 可用且 `doc` 或 `docx` 文件能够正常打开和导出
- **THEN** 应用生成通过完整性校验的临时 PDF
- **THEN** 应用使用该临时 PDF 继续当前档案的上传流程

#### Scenario: Excel 文件转换成功
- **WHEN** WPS Spreadsheets 可用且 `xls` 或 `xlsx` 文件能够正常打开和导出
- **THEN** 应用生成通过完整性校验的临时 PDF
- **THEN** 应用使用该临时 PDF 继续当前档案的上传流程

#### Scenario: 转换后自动化进程未按时退出但 PDF 完整
- **WHEN** 转换达到超时且应用终止本次转换拥有的进程后，输出 PDF 已经写入完成并通过完整性校验
- **THEN** 应用允许使用该临时 PDF 继续上传
- **THEN** 主界面保持可响应

#### Scenario: 转换超时且没有完整 PDF
- **WHEN** 转换达到超时且输出不存在、仍在变化或未通过 PDF 完整性校验
- **THEN** 应用记录“转换超时”
- **THEN** 应用保留原始 Office 文件并继续处理下一项

#### Scenario: WPS 不可用或源文件无法转换
- **WHEN** 所需 WPS 自动化接口未安装、未注册、无法启动，或源 Office 文件损坏、受密码保护、不可读取或无法导出
- **THEN** 应用记录转换失败及可核对原因
- **THEN** 应用保留原始 Office 文件且不上传其他文件

#### Scenario: 转换期间显示当前文件状态
- **WHEN** 应用正在将一个 Office 对账文件转换为 PDF
- **THEN** “发送对账”按钮显示 `PDF转换中...`
- **THEN** 按钮提示信息包含当前原始 Office 文件的相对路径
- **THEN** 转换结束后按钮离开 PDF 转换状态并继续显示普通处理状态

### Requirement: 上传临时 PDF 而不是原始 Office 文件
Office 文件转换成功后，应用 SHALL 将生成的临时 PDF 作为当前档案的实际上传文件。应用 MUST NOT 将原始 `doc`、`docx`、`xls` 或 `xlsx` 文件直接提交到网页，也 MUST NOT 使用其他工作项的临时 PDF。

#### Scenario: 上传文件选择使用临时 PDF
- **WHEN** 当前 Office 文件转换成功且网页仍确认该唯一档案附件数量为 `0`
- **THEN** 应用向网页文件输入或 Windows 文件选择框提交当前工作项的临时 PDF 完整路径
- **THEN** 被提交文件的扩展名为 `.pdf`

#### Scenario: 上传前档案状态发生变化
- **WHEN** Office 文件转换完成后，上传前重新检查发现档案不再唯一或附件数量已经大于 `0`
- **THEN** 应用不提交临时 PDF
- **THEN** 应用删除临时 PDF并保留原始 Office 文件

#### Scenario: 临时 PDF 在提交前不可用
- **WHEN** 临时 PDF 在上传前丢失、不可读取或不再通过完整性校验
- **THEN** 应用记录上传前文件校验失败
- **THEN** 应用不直接上传原始 Office 文件

### Requirement: 清理临时 PDF 并归档原始 Office 文件
应用 SHALL 在 Office 项目进入上传终态后删除本次生成的临时 PDF，且 MUST NOT 将临时 PDF移动到“对账已完成目录”。上传成功后，若存在有效“对账已完成目录”，应用 SHALL 移动原始 Office 文件并保留相对子文件夹结构；若没有有效完成目录，应用 SHALL 保留原始 Office 文件。上传失败时应用 SHALL 保留原始 Office 文件。

临时 PDF 清理失败时，应用 SHALL 在最终结果中显示清理警告并继续保护性清理，但该清理警告 MUST NOT 将已经确认的网页上传改判为失败。

#### Scenario: 上传成功并配置完成目录
- **WHEN** `对账目录\2026\报表.xlsx` 对应的临时 PDF 上传成功且完成目录有效
- **THEN** 应用删除临时 PDF
- **THEN** 应用将原始文件移动为 `对账已完成目录\2026\报表.xlsx`
- **THEN** 应用不在完成目录中保存转换产生的 PDF

#### Scenario: 上传成功但没有完成目录
- **WHEN** Office 文件对应的临时 PDF 上传成功但没有有效完成目录
- **THEN** 应用删除临时 PDF
- **THEN** 应用保留原始 Office 文件并记录“上传成功未移动”

#### Scenario: 上传失败
- **WHEN** 临时 PDF 上传失败或等待上传结果超时
- **THEN** 应用删除临时 PDF
- **THEN** 应用保留原始 Office 文件并记录上传失败

#### Scenario: 原始 Office 文件移动失败
- **WHEN** 临时 PDF 已上传成功但原始 Office 文件因同名冲突、权限、占用、路径或 I/O 错误无法移动
- **THEN** 应用 MUST NOT 覆盖完成目录中的文件
- **THEN** 应用保留原始 Office 文件并记录“上传成功但移动失败”

#### Scenario: 临时 PDF 首次删除失败
- **WHEN** 应用无法在当前项结束时删除临时 PDF
- **THEN** 应用记录临时文件清理警告并在批次结束时再次尝试清理
- **THEN** 应用继续处理下一工作项

### Requirement: 串行处理并汇总 Office 阶段结果
应用 SHALL 将 Office 查询、按需转换、上传、临时文件清理和原始文件移动纳入现有严格串行批次。单个 Office 项目转换失败、超时、上传失败、清理警告或移动失败时，只要网页仍可继续操作，应用 SHALL 继续处理下一项。最终弹窗 SHALL 明确区分转换失败与查询失败、上传失败和移动失败，并 SHALL 标识存在临时文件清理警告的项目。

#### Scenario: 转换失败后继续处理
- **WHEN** 一个 Office 文件转换失败且后续队列仍有 PDF、图片或 Office 文件
- **THEN** 应用保留失败文件并继续处理下一工作项
- **THEN** 后续工作项不使用失败项目的临时路径或状态

#### Scenario: 混合批次全部处理结束
- **WHEN** 批次同时包含直接上传文件、成功转换的 Office 文件、已有附件项目和转换失败项目
- **THEN** 应用在所有项目进入终态后显示一次汇总弹窗
- **THEN** 汇总按项目显示原始相对路径、档案名称、最终状态和失败或警告原因

#### Scenario: Office 文件全部转换上传并归档成功
- **WHEN** 所有 Office 文件均生成有效 PDF、上传成功、临时 PDF 删除成功且原始文件移动成功
- **THEN** 汇总将这些项目计入“上传并移动成功”
- **THEN** 汇总不显示临时 PDF 为待处理文件
