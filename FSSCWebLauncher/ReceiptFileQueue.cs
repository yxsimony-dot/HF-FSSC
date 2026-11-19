using System.IO;

namespace FSSCWebLauncher;

internal sealed record ReceiptWorkItem(
    string FileName,
    string SerialNumber,
    string SourcePath)
{
    public string RelativePath { get; init; } = FileName;

    public ReconciliationFileKind FileKind { get; } =
        ReconciliationFileQueue.Classify(SourcePath);

    public ReconciliationWorkItem ToReconciliationWorkItem()
    {
        return new ReconciliationWorkItem(
            FileName,
            SerialNumber,
            SourcePath,
            RelativePath);
    }
}

internal static class ReceiptFileQueue
{
    public static IReadOnlyList<ReceiptWorkItem> Create(string directory)
    {
        string rootDirectory = Path.GetFullPath(directory);

        return Directory
            .EnumerateFiles(
                rootDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(ReconciliationFileQueue.IsSupportedFile)
            .Select(path =>
            {
                string fullPath = Path.GetFullPath(path);
                string fileName = Path.GetFileName(fullPath);
                string serialNumber =
                    Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrEmpty(serialNumber))
                {
                    serialNumber = fileName;
                }

                return new ReceiptWorkItem(
                    fileName,
                    serialNumber,
                    fullPath)
                {
                    RelativePath =
                        Path.GetRelativePath(rootDirectory, fullPath)
                };
            })
            .OrderBy(
                item => item.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.RelativePath,
                StringComparer.Ordinal)
            .ToArray();
    }
}
