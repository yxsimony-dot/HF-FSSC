using System.IO;
using System.Windows;

namespace FSSCWebLauncher;

public partial class BatchResultWindow : Window
{
    internal BatchResultWindow(
        IReadOnlyList<ReceiptBatchItemResult> results)
    {
        InitializeComponent();

        ReceiptBatchItemResult[] incomplete =
            results.Where(result => !result.IsComplete).ToArray();
        int completeCount = Count(
            results,
            ReceiptItemStatus.UploadedAndMoved);
        int queryFailedCount = Count(
            results,
            ReceiptItemStatus.QueryFailed);
        int existingCount = Count(
            results,
            ReceiptItemStatus.ExistingAttachment);
        int uploadFailedCount = Count(
            results,
            ReceiptItemStatus.UploadFailed);
        int conversionFailedCount = Count(
            results,
            ReceiptItemStatus.ConversionFailed);
        int moveFailedCount = Count(
            results,
            ReceiptItemStatus.MoveFailed);
        int warningItemCount = results.Count(
            result => result.Warnings.Count > 0);

        SummaryText.Text =
            $"共处理 {results.Count} 个，完成 {completeCount} 个，未完成 {incomplete.Length} 个。";
        CategoryText.Text =
            $"上传并移动成功 {completeCount}    查询失败 {queryFailedCount}    "
            + $"已有附件 {existingCount}    文件准备失败 {conversionFailedCount}    "
            + $"上传失败 {uploadFailedCount}    移动失败 {moveFailedCount}    "
            + $"清理警告 {warningItemCount}";
        FailureListText.Text = incomplete.Length == 0
            && warningItemCount == 0
            ? "所有回单均上传并移动成功"
            : FormatReviewResults(results);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static int Count(
        IEnumerable<ReceiptBatchItemResult> results,
        ReceiptItemStatus status)
    {
        return results.Count(result => result.Status == status);
    }

    private static string FormatReviewResults(
        IReadOnlyList<ReceiptBatchItemResult> results)
    {
        ReceiptItemStatus[] categoryOrder =
        [
            ReceiptItemStatus.UploadedAndMoved,
            ReceiptItemStatus.QueryFailed,
            ReceiptItemStatus.ExistingAttachment,
            ReceiptItemStatus.ConversionFailed,
            ReceiptItemStatus.UploadFailed,
            ReceiptItemStatus.MoveFailed,
            ReceiptItemStatus.UploadedPendingMove
        ];
        List<string> sections = [];

        foreach (ReceiptItemStatus status in categoryOrder)
        {
            ReceiptBatchItemResult[] categoryResults =
                results.Where(result =>
                    result.Status == status
                    && (status != ReceiptItemStatus.UploadedAndMoved
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
        ReceiptBatchItemResult result)
    {
        string extension = Path.GetExtension(
            result.WorkItem.FileName);
        List<string> lines =
        [
            $"原始相对路径：{result.WorkItem.RelativePath}",
            $"业务流水号：{result.SerialNumber}",
            $"原始文件类型：{(string.IsNullOrWhiteSpace(extension)
                ? "无扩展名"
                : extension.TrimStart('.').ToUpperInvariant())}",
            $"处理结果：{result.UserReason}"
        ];

        if (result.Warnings.Count > 0)
        {
            lines.Add(
                $"警告：{string.Join("；", result.Warnings)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetCategoryName(ReceiptItemStatus status)
    {
        return status switch
        {
            ReceiptItemStatus.UploadedAndMoved =>
                "上传并移动成功（有警告）",
            ReceiptItemStatus.QueryFailed => "查询失败",
            ReceiptItemStatus.ExistingAttachment => "已有附件",
            ReceiptItemStatus.ConversionFailed => "文件准备失败",
            ReceiptItemStatus.UploadFailed => "上传失败",
            ReceiptItemStatus.MoveFailed => "移动失败",
            ReceiptItemStatus.UploadedPendingMove => "等待移动",
            _ => "其他"
        };
    }
}
