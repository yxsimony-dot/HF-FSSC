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
        int moveFailedCount = Count(
            results,
            ReceiptItemStatus.MoveFailed);

        SummaryText.Text =
            $"共处理 {results.Count} 个，完成 {completeCount} 个，未完成 {incomplete.Length} 个。";
        CategoryText.Text =
            $"上传并移动成功 {completeCount}    查询失败 {queryFailedCount}    "
            + $"已有附件 {existingCount}    上传失败 {uploadFailedCount}    "
            + $"移动失败 {moveFailedCount}";
        FailureListText.Text = incomplete.Length == 0
            ? "所有回单均上传并移动成功"
            : FormatIncompleteResults(incomplete);
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

    private static string FormatIncompleteResults(
        IReadOnlyList<ReceiptBatchItemResult> results)
    {
        ReceiptItemStatus[] categoryOrder =
        [
            ReceiptItemStatus.QueryFailed,
            ReceiptItemStatus.ExistingAttachment,
            ReceiptItemStatus.UploadFailed,
            ReceiptItemStatus.MoveFailed,
            ReceiptItemStatus.UploadedPendingMove
        ];
        List<string> sections = [];

        foreach (ReceiptItemStatus status in categoryOrder)
        {
            ReceiptBatchItemResult[] categoryResults =
                results.Where(result => result.Status == status).ToArray();
            if (categoryResults.Length == 0)
            {
                continue;
            }

            sections.Add($"【{GetCategoryName(status)}】");
            sections.AddRange(categoryResults.Select(result =>
                $"{result.SerialNumber}    {result.UserReason}"));
            sections.Add(string.Empty);
        }

        if (sections.Count > 0 && sections[^1].Length == 0)
        {
            sections.RemoveAt(sections.Count - 1);
        }

        return string.Join(Environment.NewLine, sections);
    }

    private static string GetCategoryName(ReceiptItemStatus status)
    {
        return status switch
        {
            ReceiptItemStatus.QueryFailed => "查询失败",
            ReceiptItemStatus.ExistingAttachment => "已有附件",
            ReceiptItemStatus.UploadFailed => "上传失败",
            ReceiptItemStatus.MoveFailed => "移动失败",
            ReceiptItemStatus.UploadedPendingMove => "等待移动",
            _ => "其他"
        };
    }
}
