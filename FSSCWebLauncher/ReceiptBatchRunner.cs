using System.IO;

namespace FSSCWebLauncher;

internal static class ReceiptBatchRunner
{
    public static async Task<IReadOnlyList<ReceiptBatchItemResult>> ProcessSequentiallyAsync(
        IReadOnlyList<ReceiptWorkItem> workItems,
        Func<ReceiptWorkItem, Task<ReceiptBatchItemResult>> processItemAsync)
    {
        List<ReceiptBatchItemResult> results = new(workItems.Count);

        foreach (ReceiptWorkItem workItem in workItems)
        {
            try
            {
                results.Add(await processItemAsync(workItem));
            }
            catch
            {
                results.Add(new ReceiptBatchItemResult(
                    workItem,
                    ReceiptItemStatus.QueryFailed,
                    "处理过程中发生异常"));
            }
        }

        return results;
    }

    public static IReadOnlyList<ReceiptWorkItem> SelectMoveCandidates(
        IEnumerable<ReceiptBatchItemResult> results)
    {
        return results
            .Where(result => result.ShouldMove)
            .Select(result => result.WorkItem)
            .ToArray();
    }

    public static IReadOnlyList<ReceiptBatchItemResult> MoveUploadedFiles(
        IReadOnlyList<ReceiptBatchItemResult> results,
        string completedDirectory)
    {
        List<ReceiptBatchItemResult> finalized = new(results.Count);

        foreach (ReceiptBatchItemResult result in results)
        {
            finalized.Add(result.ShouldMove
                ? MoveUploadedFile(result, completedDirectory)
                : result);
        }

        return finalized;
    }

    private static ReceiptBatchItemResult MoveUploadedFile(
        ReceiptBatchItemResult result,
        string completedDirectory)
    {
        try
        {
            if (!Directory.Exists(completedDirectory))
            {
                return result.MarkMoveFailed("已完成目录不存在，源文件已保留");
            }

            string sourcePath = Path.GetFullPath(result.WorkItem.SourcePath);
            string destinationPath = Path.GetFullPath(
                Path.Combine(completedDirectory, result.WorkItem.FileName));

            if (!File.Exists(sourcePath))
            {
                return result.MarkMoveFailed("上传成功，但源 PDF 已不存在");
            }

            if (File.Exists(destinationPath))
            {
                return result.MarkMoveFailed(
                    "已完成目录存在同名文件，未覆盖，源文件已保留");
            }

            File.Move(sourcePath, destinationPath, overwrite: false);
            return result.MarkMoved();
        }
        catch (UnauthorizedAccessException)
        {
            return result.MarkMoveFailed("没有权限移动文件，源文件已保留");
        }
        catch (IOException)
        {
            return result.MarkMoveFailed(
                "文件被占用或移动时发生 I/O 错误，源文件已保留");
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return result.MarkMoveFailed("移动路径无效或不可访问，源文件已保留");
        }
    }
}
