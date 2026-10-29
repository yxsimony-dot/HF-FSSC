using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FSSCWebLauncher;

internal sealed class ReconciliationTemporaryWorkspace
    : IDisposable
{
    private static readonly TimeSpan StaleRetention =
        TimeSpan.FromDays(3);

    private readonly List<ReconciliationTemporaryItemWorkspace>
        _items = [];
    private bool _disposed;

    public ReconciliationTemporaryWorkspace(
        string? temporaryRoot = null)
    {
        string root = temporaryRoot
            ?? Path.Combine(
                Path.GetTempPath(),
                "FSSCWebLauncher",
                "reconciliation");
        RootDirectory = Path.GetFullPath(root);
        Directory.CreateDirectory(RootDirectory);
        CleanupStaleDirectories(
            RootDirectory,
            DateTime.UtcNow - StaleRetention);

        BatchDirectory = Path.Combine(
            RootDirectory,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(BatchDirectory);
    }

    public string RootDirectory { get; }

    public string BatchDirectory { get; }

    public ReconciliationTemporaryItemWorkspace CreateItem()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconciliationTemporaryItemWorkspace item = new(
            Path.Combine(
                BatchDirectory,
                Guid.NewGuid().ToString("N")));
        _items.Add(item);
        return item;
    }

    public IReadOnlyList<string> CleanupBatch()
    {
        List<string> warnings = [];

        foreach (ReconciliationTemporaryItemWorkspace item in _items)
        {
            string? warning = item.Cleanup();
            if (warning is not null)
            {
                warnings.Add(warning);
            }
        }

        try
        {
            if (Directory.Exists(BatchDirectory))
            {
                Directory.Delete(
                    BatchDirectory,
                    recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            warnings.Add(
                "批次临时目录清理失败，程序将在后续启动时再次尝试");
        }

        return warnings.Distinct(
                StringComparer.Ordinal)
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CleanupBatch();
        _disposed = true;
    }

    internal static void CleanupStaleDirectories(
        string rootDirectory,
        DateTime cutoffUtc)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(
                     rootDirectory))
        {
            try
            {
                DirectoryInfo info = new(directory);
                if (info.LastWriteTimeUtc < cutoffUtc)
                {
                    Directory.Delete(
                        directory,
                        recursive: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                // A current lock is retried on a later batch or app launch.
            }
        }
    }
}

internal sealed class ReconciliationTemporaryItemWorkspace
{
    public ReconciliationTemporaryItemWorkspace(
        string directoryPath)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(DirectoryPath);
        OutputPdfPath = Path.Combine(
            DirectoryPath,
            "upload.pdf");
        ExtractedDirectoryPath = Path.Combine(
            DirectoryPath,
            "extracted");
        RequestPath = Path.Combine(
            DirectoryPath,
            "conversion.request.json");
        StartSignalPath = Path.Combine(
            DirectoryPath,
            "conversion.start");
    }

    public string DirectoryPath { get; }

    public string OutputPdfPath { get; }

    public string ExtractedDirectoryPath { get; }

    public string RequestPath { get; }

    public string StartSignalPath { get; }

    internal bool UsedLastAccessTimeSuppression { get; private set; }

    internal int LastAccessTimeSuppressionError { get; private set; }

    public string StageSource(string sourcePath)
    {
        string extension = Path.GetExtension(sourcePath);
        string stagedSourcePath = Path.Combine(
            DirectoryPath,
            $"source{extension}");
        if (!TryCopyWithoutUpdatingLastAccessTime(
                sourcePath,
                stagedSourcePath,
                out int nativeError))
        {
            LastAccessTimeSuppressionError = nativeError;
            File.Copy(
                sourcePath,
                stagedSourcePath,
                overwrite: false);
        }
        else
        {
            UsedLastAccessTimeSuppression = true;
        }

        return stagedSourcePath;
    }

    private static bool TryCopyWithoutUpdatingLastAccessTime(
        string sourcePath,
        string destinationPath,
        out int nativeError)
    {
        nativeError = 0;
        using SafeFileHandle sourceHandle = NativeMethods.CreateFileW(
            sourcePath,
            NativeMethods.GenericRead
                | NativeMethods.FileWriteAttributes,
            NativeMethods.FileShareRead
                | NativeMethods.FileShareWrite
                | NativeMethods.FileShareDelete,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal,
            IntPtr.Zero);
        if (sourceHandle.IsInvalid)
        {
            nativeError = Marshal.GetLastPInvokeError();
            return false;
        }

        NativeMethods.NativeFileTime unchanged = new()
        {
            LowDateTime = uint.MaxValue,
            HighDateTime = uint.MaxValue
        };
        if (!NativeMethods.SetFileTime(
                sourceHandle,
                IntPtr.Zero,
                ref unchanged,
                IntPtr.Zero))
        {
            nativeError = Marshal.GetLastPInvokeError();
            return false;
        }

        using FileStream source = new(
            sourceHandle,
            FileAccess.Read);
        using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        source.CopyTo(destination);
        NativeMethods.SetFileTime(
            sourceHandle,
            IntPtr.Zero,
            ref unchanged,
            IntPtr.Zero);
        return true;
    }

    public string? Cleanup()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(
                    DirectoryPath,
                    recursive: true);
            }

            return null;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return "临时 PDF 清理失败，程序将在批次结束时再次尝试";
        }
    }

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint FileWriteAttributes = 0x00000100;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint FileShareDelete = 0x00000004;
        internal const uint OpenExisting = 3;
        internal const uint FileAttributeNormal = 0x00000080;

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeFileTime
        {
            internal uint LowDateTime;
            internal uint HighDateTime;
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport(
            "kernel32.dll",
            ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileTime(
            SafeFileHandle fileHandle,
            IntPtr creationTime,
            ref NativeFileTime lastAccessTime,
            IntPtr lastWriteTime);
    }
}
