using System.IO;

namespace FSSCWebLauncher;

internal static class ReconciliationBatchRunner
{
    public static async Task<IReadOnlyList<ReconciliationBatchItemResult>>
        ProcessSequentiallyAsync(
            IReadOnlyList<ReconciliationWorkItem> workItems,
            string? completedDirectory,
            Func<ReconciliationWorkItem, Task<ReconciliationPageResult>>
                processItemAsync)
    {
        List<ReconciliationBatchItemResult> results =
            new(workItems.Count);

        foreach (ReconciliationWorkItem workItem in workItems)
        {
            ReconciliationPageResult pageResult;

            try
            {
                pageResult = await processItemAsync(workItem);
            }
            catch
            {
                results.Add(new ReconciliationBatchItemResult(
                    workItem,
                    ReconciliationItemStatus.QueryFailed,
                    "处理过程中发生异常"));
                continue;
            }

            ReconciliationBatchItemResult batchResult =
                pageResult.Status switch
            {
                ReconciliationPageStatus.Uploaded =>
                    CompleteUploadedFile(
                        workItem,
                        completedDirectory),
                ReconciliationPageStatus.ExistingAttachment =>
                    new ReconciliationBatchItemResult(
                        workItem,
                        ReconciliationItemStatus.ExistingAttachment,
                        pageResult.Detail),
                ReconciliationPageStatus.QueryFailed =>
                    new ReconciliationBatchItemResult(
                        workItem,
                        ReconciliationItemStatus.QueryFailed,
                        pageResult.Detail),
                ReconciliationPageStatus.UploadFailed =>
                    new ReconciliationBatchItemResult(
                        workItem,
                        ReconciliationItemStatus.UploadFailed,
                        pageResult.Detail),
                ReconciliationPageStatus.ConversionFailed =>
                    new ReconciliationBatchItemResult(
                        workItem,
                        ReconciliationItemStatus.ConversionFailed,
                        pageResult.Detail),
                _ => new ReconciliationBatchItemResult(
                    workItem,
                    ReconciliationItemStatus.QueryFailed,
                    "页面处理返回未知状态")
            };
            results.Add(batchResult with
            {
                Warnings = pageResult.Warnings
            });
        }

        return results;
    }

    internal static ReconciliationBatchItemResult CompleteUploadedFile(
        ReconciliationWorkItem workItem,
        string? completedDirectory)
    {
        if (string.IsNullOrWhiteSpace(completedDirectory))
        {
            return new ReconciliationBatchItemResult(
                workItem,
                ReconciliationItemStatus.UploadedNotMoved);
        }

        try
        {
            string completedRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(completedDirectory));
            if (!Directory.Exists(completedRoot))
            {
                return MoveFailed(
                    workItem,
                    "对账已完成目录不存在，源文件已保留");
            }

            string sourcePath = Path.GetFullPath(workItem.SourcePath);
            if (!File.Exists(sourcePath))
            {
                return MoveFailed(
                    workItem,
                    "上传成功，但源文件已不存在");
            }

            if (string.IsNullOrWhiteSpace(workItem.RelativePath)
                || Path.IsPathRooted(workItem.RelativePath))
            {
                return MoveFailed(
                    workItem,
                    "相对路径无效，未执行移动，源文件已保留");
            }

            string destinationPath = Path.GetFullPath(
                Path.Combine(completedRoot, workItem.RelativePath));
            string destinationRelative = Path.GetRelativePath(
                completedRoot,
                destinationPath);
            if (Path.IsPathRooted(destinationRelative)
                || destinationRelative.Equals(
                    "..",
                    StringComparison.Ordinal)
                || destinationRelative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                || destinationRelative.StartsWith(
                    $"..{Path.AltDirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                return MoveFailed(
                    workItem,
                    "目标路径超出对账已完成目录，未执行移动，源文件已保留");
            }

            if (string.Equals(
                    sourcePath,
                    destinationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return MoveFailed(
                    workItem,
                    "源文件与目标文件路径相同，未执行移动");
            }

            if (File.Exists(destinationPath))
            {
                return MoveFailed(
                    workItem,
                    "目标位置存在同名文件，未覆盖，源文件已保留");
            }

            string? destinationDirectory =
                Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                return MoveFailed(
                    workItem,
                    "无法确定目标子文件夹，源文件已保留");
            }

            Directory.CreateDirectory(destinationDirectory);
            File.Move(sourcePath, destinationPath, overwrite: false);
            return new ReconciliationBatchItemResult(
                workItem,
                ReconciliationItemStatus.UploadedAndMoved);
        }
        catch (UnauthorizedAccessException)
        {
            return MoveFailed(
                workItem,
                "没有权限创建目录或移动文件，源文件已保留");
        }
        catch (IOException)
        {
            return MoveFailed(
                workItem,
                "文件被占用或移动时发生 I/O 错误，源文件已保留");
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return MoveFailed(
                workItem,
                "移动路径无效或不可访问，源文件已保留");
        }
    }

    private static ReconciliationBatchItemResult MoveFailed(
        ReconciliationWorkItem workItem,
        string detail)
    {
        return new ReconciliationBatchItemResult(
            workItem,
            ReconciliationItemStatus.MoveFailed,
            detail);
    }
}
