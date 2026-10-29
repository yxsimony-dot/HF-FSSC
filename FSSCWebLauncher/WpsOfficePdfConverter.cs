using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FSSCWebLauncher;

internal interface IComAutomationBridge
{
    object CreateApplication(string progId);

    object? GetProperty(object target, string propertyName);

    void SetProperty(
        object target,
        string propertyName,
        object? value);

    object? Invoke(
        object target,
        string methodName,
        params object?[] arguments);

    void Release(object? value);
}

internal sealed class ComAutomationBridge : IComAutomationBridge
{
    public object CreateApplication(string progId)
    {
        Type? applicationType = Type.GetTypeFromProgID(
            progId,
            throwOnError: false);
        if (applicationType is null)
        {
            throw new InvalidOperationException(
                $"未注册 WPS 自动化接口 {progId}");
        }

        return Activator.CreateInstance(applicationType)
            ?? throw new InvalidOperationException(
                $"无法启动 WPS 自动化接口 {progId}");
    }

    public object? GetProperty(
        object target,
        string propertyName)
    {
        return target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);
    }

    public void SetProperty(
        object target,
        string propertyName,
        object? value)
    {
        target.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target,
            [value],
            CultureInfo.InvariantCulture);
    }

    public object? Invoke(
        object target,
        string methodName,
        params object?[] arguments)
    {
        return target.GetType().InvokeMember(
            methodName,
            BindingFlags.InvokeMethod
            | BindingFlags.OptionalParamBinding,
            binder: null,
            target,
            arguments,
            CultureInfo.InvariantCulture);
    }

    public void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

internal sealed class WpsOfficePdfConverter
{
    private const int WordPdfFormat = 17;
    private const int ExcelPdfFormat = 0;

    private readonly IComAutomationBridge _bridge;

    public WpsOfficePdfConverter(
        IComAutomationBridge? bridge = null)
    {
        _bridge = bridge ?? new ComAutomationBridge();
    }

    public OfficePdfWorkerResult Convert(
        OfficePdfWorkerRequest request)
    {
        string outputPath = request.OutputPdfPath;
        string? sourcePathToRestore = null;
        SourceFileMetadata? sourceMetadata = null;

        try
        {
            outputPath = Path.GetFullPath(
                request.OutputPdfPath);
            string sourcePath = Path.GetFullPath(
                request.SourcePath);
            if (!File.Exists(sourcePath))
            {
                return OfficePdfWorkerResult.Failure(
                    outputPath,
                    "SOURCE_NOT_FOUND",
                    "源 Office 文件不存在");
            }

            sourcePathToRestore = sourcePath;
            sourceMetadata = SourceFileMetadata.Capture(sourcePath);

            if (!string.Equals(
                    Path.GetExtension(outputPath),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    sourcePath,
                    outputPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return OfficePdfWorkerResult.Failure(
                    outputPath,
                    "OUTPUT_PATH_INVALID",
                    "临时输出必须是独立的 PDF 文件");
            }

            string? outputDirectory =
                Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return OfficePdfWorkerResult.Failure(
                    outputPath,
                    "OUTPUT_PATH_INVALID",
                    "无法确定临时 PDF 输出目录");
            }

            Directory.CreateDirectory(outputDirectory);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            switch (request.FileKind)
            {
                case ReconciliationFileKind.OfficeWord:
                    ConvertWord(sourcePath, outputPath);
                    break;
                case ReconciliationFileKind.OfficeExcel:
                    ConvertExcel(sourcePath, outputPath);
                    break;
                default:
                    return OfficePdfWorkerResult.Failure(
                        outputPath,
                        "UNSUPPORTED_FILE_KIND",
                        "当前文件不是受支持的 Word 或 Excel 文件");
            }

            return OfficePdfWorkerResult.Success(outputPath);
        }
        catch (UnauthorizedAccessException)
        {
            return OfficePdfWorkerResult.Failure(
                outputPath,
                "ACCESS_DENIED",
                "源文件或临时输出目录没有访问权限");
        }
        catch (IOException exception)
        {
            return OfficePdfWorkerResult.Failure(
                outputPath,
                "IO_ERROR",
                $"Office 文件读取或 PDF 写入失败：{exception.Message}");
        }
        catch (COMException exception)
        {
            return OfficePdfWorkerResult.Failure(
                outputPath,
                "WPS_COM_ERROR",
                $"WPS 无法打开或导出当前文件：{exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return OfficePdfWorkerResult.Failure(
                outputPath,
                "WPS_UNAVAILABLE",
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            return OfficePdfWorkerResult.Failure(
                outputPath,
                "PATH_INVALID",
                $"Office 文件或临时 PDF 路径无效：{exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return OfficePdfWorkerResult.Failure(
                outputPath,
                "PATH_INVALID",
                $"Office 文件或临时 PDF 路径无效：{exception.Message}");
        }
        catch (Exception exception)
        {
            return OfficePdfWorkerResult.Failure(
                outputPath,
                "CONVERSION_FAILED",
                $"Office 转 PDF 失败：{exception.Message}");
        }
        finally
        {
            if (sourceMetadata is not null
                && sourcePathToRestore is not null)
            {
                sourceMetadata.Restore(sourcePathToRestore);
            }
        }
    }

    private void ConvertWord(
        string sourcePath,
        string outputPath)
    {
        object? application = null;
        object? documents = null;
        object? document = null;

        try
        {
            application = _bridge.CreateApplication(
                "KWPS.Application");
            TrySetProperty(application, "Visible", false);
            TrySetProperty(application, "DisplayAlerts", 0);
            documents = _bridge.GetProperty(
                application,
                "Documents")
                ?? throw new InvalidOperationException(
                    "WPS Writer 文档集合不可用");
            document = _bridge.Invoke(
                documents,
                "Open",
                sourcePath,
                false,
                true,
                false)
                ?? throw new InvalidOperationException(
                    "WPS Writer 无法打开当前文件");
            _bridge.Invoke(
                document,
                "ExportAsFixedFormat",
                outputPath,
                WordPdfFormat);
        }
        finally
        {
            TryInvoke(document, "Close", false);
            _bridge.Release(document);
            _bridge.Release(documents);
            TryInvoke(application, "Quit", false);
            _bridge.Release(application);
        }
    }

    private void ConvertExcel(
        string sourcePath,
        string outputPath)
    {
        object? application = null;
        object? workbooks = null;
        object? workbook = null;

        try
        {
            application = _bridge.CreateApplication(
                "KET.Application");
            TrySetProperty(application, "Visible", false);
            TrySetProperty(application, "DisplayAlerts", false);
            workbooks = _bridge.GetProperty(
                application,
                "Workbooks")
                ?? throw new InvalidOperationException(
                    "WPS 表格工作簿集合不可用");
            workbook = _bridge.Invoke(
                workbooks,
                "Open",
                sourcePath,
                0,
                true)
                ?? throw new InvalidOperationException(
                    "WPS 表格无法打开当前文件");
            _bridge.Invoke(
                workbook,
                "ExportAsFixedFormat",
                ExcelPdfFormat,
                outputPath);
        }
        finally
        {
            TryInvoke(workbook, "Close", false);
            _bridge.Release(workbook);
            _bridge.Release(workbooks);
            TryInvoke(application, "Quit");
            _bridge.Release(application);
        }
    }

    private void TrySetProperty(
        object target,
        string propertyName,
        object? value)
    {
        try
        {
            _bridge.SetProperty(
                target,
                propertyName,
                value);
        }
        catch
        {
            // Older WPS versions can omit nonessential visibility settings.
        }
    }

    private void TryInvoke(
        object? target,
        string methodName,
        params object?[] arguments)
    {
        if (target is null)
        {
            return;
        }

        try
        {
            _bridge.Invoke(
                target,
                methodName,
                arguments);
        }
        catch
        {
            // Preserve the original conversion result while still releasing COM.
        }
    }

    private sealed record SourceFileMetadata(
        FileAttributes Attributes,
        DateTime CreationTimeUtc,
        DateTime LastAccessTimeUtc,
        DateTime LastWriteTimeUtc)
    {
        public static SourceFileMetadata Capture(string path)
        {
            FileInfo file = new(path);
            return new SourceFileMetadata(
                file.Attributes,
                file.CreationTimeUtc,
                file.LastAccessTimeUtc,
                file.LastWriteTimeUtc);
        }

        public void Restore(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                File.SetAttributes(
                    path,
                    Attributes & ~FileAttributes.ReadOnly);
                File.SetCreationTimeUtc(path, CreationTimeUtc);
                File.SetLastAccessTimeUtc(path, LastAccessTimeUtc);
                File.SetLastWriteTimeUtc(path, LastWriteTimeUtc);
                File.SetAttributes(path, Attributes);
            }
            catch
            {
                // Conversion must never hide its primary result because WPS
                // changed metadata that Windows would not allow us to restore.
            }
        }
    }
}
