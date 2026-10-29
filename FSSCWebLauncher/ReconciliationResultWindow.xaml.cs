using System.IO;
using System.Windows;

namespace FSSCWebLauncher;

public partial class ReconciliationResultWindow : Window
{
    internal ReconciliationResultWindow(
        IReadOnlyList<ReconciliationBatchItemResult> results)
    {
        InitializeComponent();

        int uploadedAndMovedCount = Count(
            results,
            ReconciliationItemStatus.UploadedAndMoved);
        int uploadedNotMovedCount = Count(
            results,
            ReconciliationItemStatus.UploadedNotMoved);
        int existingAttachmentCount = Count(
            results,
            ReconciliationItemStatus.ExistingAttachment);
        int queryFailedCount = Count(
            results,
            ReconciliationItemStatus.QueryFailed);
        int uploadFailedCount = Count(
            results,
            ReconciliationItemStatus.UploadFailed);
        int moveFailedCount = Count(
            results,
            ReconciliationItemStatus.MoveFailed);
        int conversionFailedCount = Count(
            results,
            ReconciliationItemStatus.ConversionFailed);
        int warningItemCount = results.Count(
            result => result.Warnings.Count > 0);

        SummaryText.Text = CreateSummary(
            results.Count,
            uploadedAndMovedCount,
            uploadedNotMovedCount,
            existingAttachmentCount,
            queryFailedCount,
            uploadFailedCount,
            moveFailedCount,
            conversionFailedCount,
            warningItemCount);
        CategoryText.Text =
            $"上传并移动成功 {uploadedAndMovedCount}    "
            + $"上传成功未移动 {uploadedNotMovedCount}    "
            + $"已有附件 {existingAttachmentCount}    "
            + $"查询失败 {queryFailedCount}    "
            + $"文件准备失败 {conversionFailedCount}    "
            + $"上传失败 {uploadFailedCount}    "
            + $"移动失败 {moveFailedCount}    "
            + $"清理警告 {warningItemCount}";
        DetailListText.Text = results.Count > 0
            && uploadedAndMovedCount == results.Count
            && warningItemCount == 0
                ? "所有对账均上传并移动成功。"
                : FormatReviewResults(results);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static int Count(
        IEnumerable<ReconciliationBatchItemResult> results,
        ReconciliationItemStatus status)
    {
        return results.Count(result => result.Status == status);
    }

    internal static string CreateSummary(
        int totalCount,
        int uploadedAndMovedCount,
        int uploadedNotMovedCount,
        int existingAttachmentCount,
        int queryFailedCount,
        int uploadFailedCount,
        int moveFailedCount,
        int conversionFailedCount,
        int warningItemCount)
    {
        if (totalCount > 0
            && uploadedAndMovedCount == totalCount
            && warningItemCount == 0)
        {
            return "所有对账均上传并移动成功。";
        }

        if (totalCount > 0
            && uploadedNotMovedCount == totalCount
            && warningItemCount == 0)
        {
            return "均上传成功，未配置完成目录，未执行移动。";
        }

        int failureCount =
            queryFailedCount
            + conversionFailedCount
            + uploadFailedCount
            + moveFailedCount;
        return $"共处理 {totalCount} 个文件，"
            + $"上传并移动成功 {uploadedAndMovedCount} 个，"
            + $"上传成功未移动 {uploadedNotMovedCount} 个，"
            + $"已有附件 {existingAttachmentCount} 个，"
            + $"失败 {failureCount} 个，"
            + $"清理警告 {warningItemCount} 个。";
    }

    internal static string FormatReviewResults(
        IReadOnlyList<ReconciliationBatchItemResult> results)
    {
        ReconciliationItemStatus[] categoryOrder =
        [
            ReconciliationItemStatus.UploadedAndMoved,
            ReconciliationItemStatus.UploadedNotMoved,
            ReconciliationItemStatus.ExistingAttachment,
            ReconciliationItemStatus.QueryFailed,
            ReconciliationItemStatus.ConversionFailed,
            ReconciliationItemStatus.UploadFailed,
            ReconciliationItemStatus.MoveFailed
        ];
        List<string> sections = [];

        foreach (ReconciliationItemStatus status in categoryOrder)
        {
            ReconciliationBatchItemResult[] categoryResults = results
                .Where(result =>
                    result.Status == status
                    && (status
                            != ReconciliationItemStatus.UploadedAndMoved
                        || result.Warnings.Count > 0))
                .ToArray();
            if (categoryResults.Length == 0)
            {
                continue;
            }

            sections.Add($"【{GetCategoryName(status)}】");
            sections.AddRange(categoryResults.Select(FormatResult));
            sections.Add(string.Empty);
        }

        if (sections.Count > 0 && sections[^1].Length == 0)
        {
            sections.RemoveAt(sections.Count - 1);
        }

        return string.Join(Environment.NewLine, sections);
    }

    private static string FormatResult(
        ReconciliationBatchItemResult result)
    {
        List<string> lines =
        [
            $"原始相对路径：{result.WorkItem.RelativePath}",
            $"档案名称：{result.WorkItem.ArchiveName}",
            $"原始文件类型：{GetFileType(result.WorkItem)}",
            $"处理阶段：{GetStageName(result)}",
            $"处理结果：{result.UserReason}"
        ];

        if (result.Warnings.Count > 0)
        {
            lines.Add(
                $"警告：{string.Join("；", result.Warnings)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetFileType(
        ReconciliationWorkItem workItem)
    {
        string extension = Path.GetExtension(
            workItem.FileName);
        return string.IsNullOrWhiteSpace(extension)
            ? "无扩展名"
            : extension.TrimStart('.').ToUpperInvariant();
    }

    private static string GetStageName(
        ReconciliationBatchItemResult result)
    {
        return result.Status switch
        {
            ReconciliationItemStatus.UploadedAndMoved
                when result.Warnings.Count > 0 =>
                "临时文件清理",
            ReconciliationItemStatus.UploadedAndMoved =>
                "完成",
            ReconciliationItemStatus.UploadedNotMoved =>
                "移动",
            ReconciliationItemStatus.ExistingAttachment
                or ReconciliationItemStatus.QueryFailed =>
                "查询",
            ReconciliationItemStatus.ConversionFailed =>
                result.WorkItem.FileKind
                    == ReconciliationFileKind.Archive
                        ? "压缩包解压/合并"
                        : "Office 转 PDF",
            ReconciliationItemStatus.UploadFailed =>
                "上传",
            ReconciliationItemStatus.MoveFailed =>
                "移动",
            _ => "处理"
        };
    }

    private static string GetCategoryName(
        ReconciliationItemStatus status)
    {
        return status switch
        {
            ReconciliationItemStatus.UploadedAndMoved =>
                "上传并移动成功（有警告）",
            ReconciliationItemStatus.UploadedNotMoved => "上传成功未移动",
            ReconciliationItemStatus.ExistingAttachment => "已有附件",
            ReconciliationItemStatus.QueryFailed => "查询失败",
            ReconciliationItemStatus.ConversionFailed => "文件准备失败",
            ReconciliationItemStatus.UploadFailed => "上传失败",
            ReconciliationItemStatus.MoveFailed => "移动失败",
            _ => "其他"
        };
    }
}
