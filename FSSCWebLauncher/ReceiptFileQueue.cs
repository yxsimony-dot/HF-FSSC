using System.IO;

namespace FSSCWebLauncher;

internal sealed record ReceiptWorkItem(
    string FileName,
    string SerialNumber,
    string SourcePath);

internal static class ReceiptFileQueue
{
    public static IReadOnlyList<ReceiptWorkItem> Create(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new ReceiptWorkItem(
                Path.GetFileName(path),
                Path.GetFileNameWithoutExtension(path),
                Path.GetFullPath(path)))
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.FileName, StringComparer.Ordinal)
            .ToArray();
    }
}
