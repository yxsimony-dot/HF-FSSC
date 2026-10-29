using System.IO;

namespace FSSCWebLauncher;

internal enum ReconciliationFileKind
{
    DirectUpload,
    OfficeWord,
    OfficeExcel,
    Archive
}

internal sealed record ReconciliationWorkItem(
    string FileName,
    string ArchiveName,
    string SourcePath,
    string RelativePath)
{
    public ReconciliationFileKind FileKind { get; } =
        ReconciliationFileQueue.Classify(SourcePath);
}

internal static class ReconciliationFileQueue
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".bmp",
            ".gif",
            ".heic",
            ".heif",
            ".jfif",
            ".jpeg",
            ".jpg",
            ".png",
            ".tif",
            ".tiff",
            ".webp",
            ".doc",
            ".docx",
            ".xls",
            ".xlsx",
            ".zip",
            ".rar",
            ".7z"
        };

    public static IReadOnlyList<ReconciliationWorkItem> Create(string directory)
    {
        string rootDirectory = Path.GetFullPath(directory);

        return Directory
            .EnumerateFiles(
                rootDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(IsSupportedFile)
            .Select(path =>
            {
                string fullPath = Path.GetFullPath(path);
                string fileName = Path.GetFileName(fullPath);
                string archiveName =
                    Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrEmpty(archiveName))
                {
                    archiveName = fileName;
                }

                return new ReconciliationWorkItem(
                    fileName,
                    archiveName,
                    fullPath,
                    Path.GetRelativePath(rootDirectory, fullPath));
            })
            .OrderBy(
                item => item.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.RelativePath,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsSupportedFile(string path)
    {
        return SupportedExtensions.Contains(
            Path.GetExtension(path));
    }

    internal static ReconciliationFileKind Classify(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".doc" or ".docx" =>
                ReconciliationFileKind.OfficeWord,
            ".xls" or ".xlsx" =>
                ReconciliationFileKind.OfficeExcel,
            ".zip" or ".rar" or ".7z" =>
                ReconciliationFileKind.Archive,
            _ => ReconciliationFileKind.DirectUpload
        };
    }
}
