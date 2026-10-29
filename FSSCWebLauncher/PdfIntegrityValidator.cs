using System.IO;
using System.Text;

namespace FSSCWebLauncher;

internal sealed record PdfValidationResult(
    bool IsValid,
    string? Error = null)
{
    public static PdfValidationResult Valid()
    {
        return new PdfValidationResult(true);
    }

    public static PdfValidationResult Invalid(string error)
    {
        return new PdfValidationResult(false, error);
    }
}

internal sealed class PdfIntegrityValidator
{
    private const int TailLength = 4096;

    private readonly long _minimumLength;
    private readonly TimeSpan _stabilityDelay;

    public PdfIntegrityValidator(
        long minimumLength = 128,
        TimeSpan? stabilityDelay = null)
    {
        _minimumLength = minimumLength;
        _stabilityDelay = stabilityDelay
            ?? TimeSpan.FromMilliseconds(300);
    }

    public async Task<PdfValidationResult> ValidateAsync(
        string pdfPath,
        string allowedRoot,
        CancellationToken cancellationToken = default)
    {
        string fullPath;
        string fullRoot;

        try
        {
            fullPath = Path.GetFullPath(pdfPath);
            fullRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(allowedRoot));
        }
        catch
        {
            return PdfValidationResult.Invalid(
                "临时 PDF 路径无效");
        }

        if (!IsInsideRoot(fullPath, fullRoot))
        {
            return PdfValidationResult.Invalid(
                "临时 PDF 路径超出当前批次目录");
        }

        FileSnapshot first = ReadSnapshot(fullPath);
        if (!first.IsReadable)
        {
            return PdfValidationResult.Invalid(
                first.Error ?? "临时 PDF 不存在或不可读取");
        }

        if (first.Length < _minimumLength)
        {
            return PdfValidationResult.Invalid(
                "临时 PDF 文件过小");
        }

        await Task.Delay(
            _stabilityDelay,
            cancellationToken);

        FileSnapshot second = ReadSnapshot(fullPath);
        if (!second.IsReadable)
        {
            return PdfValidationResult.Invalid(
                second.Error ?? "临时 PDF 不存在或不可读取");
        }

        if (first.Length != second.Length
            || first.LastWriteTimeUtc != second.LastWriteTimeUtc)
        {
            return PdfValidationResult.Invalid(
                "临时 PDF 仍在写入，文件尚未稳定");
        }

        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            byte[] header = new byte[5];
            if (stream.Read(header, 0, header.Length)
                    != header.Length
                || Encoding.ASCII.GetString(header) != "%PDF-")
            {
                return PdfValidationResult.Invalid(
                    "临时文件没有有效的 PDF 文件头");
            }

            int tailLength = (int)Math.Min(
                TailLength,
                stream.Length);
            byte[] tail = new byte[tailLength];
            stream.Seek(-tailLength, SeekOrigin.End);
            int bytesRead = stream.Read(
                tail,
                0,
                tail.Length);
            string tailText = Encoding.ASCII.GetString(
                tail,
                0,
                bytesRead);
            if (!tailText.Contains(
                    "%%EOF",
                    StringComparison.Ordinal))
            {
                return PdfValidationResult.Invalid(
                    "临时文件没有完整的 PDF 结束标记");
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return PdfValidationResult.Invalid(
                "临时 PDF 无法读取");
        }

        return PdfValidationResult.Valid();
    }

    private static bool IsInsideRoot(
        string fullPath,
        string fullRoot)
    {
        string relative = Path.GetRelativePath(
            fullRoot,
            fullPath);
        return !Path.IsPathRooted(relative)
            && !relative.Equals(
                "..",
                StringComparison.Ordinal)
            && !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            && !relative.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    private static FileSnapshot ReadSnapshot(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return new FileSnapshot(
                true,
                stream.Length,
                File.GetLastWriteTimeUtc(path));
        }
        catch (FileNotFoundException)
        {
            return FileSnapshot.Unreadable(
                "临时 PDF 不存在");
        }
        catch (DirectoryNotFoundException)
        {
            return FileSnapshot.Unreadable(
                "临时 PDF 目录不存在");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return FileSnapshot.Unreadable(
                "临时 PDF 不可读取");
        }
    }

    private sealed record FileSnapshot(
        bool IsReadable,
        long Length,
        DateTime LastWriteTimeUtc,
        string? Error = null)
    {
        public static FileSnapshot Unreadable(string error)
        {
            return new FileSnapshot(
                false,
                0,
                DateTime.MinValue,
                error);
        }
    }
}
