using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO;

namespace FSSCWebLauncher;

internal enum ArchivePdfPreparationStage
{
    Extracting,
    Merging,
    Processing
}

internal sealed class ArchivePdfPreparationService
{
    private readonly ReconciliationTemporaryWorkspace _workspace;
    private readonly ArchivePdfExtractor _extractor;
    private readonly PdfMergeService _merger;
    private readonly PdfIntegrityValidator _validator;
    private readonly Action<
        ReconciliationWorkItem,
        ArchivePdfPreparationStage>? _stageChanged;

    public ArchivePdfPreparationService(
        ReconciliationTemporaryWorkspace workspace,
        ArchivePdfExtractor? extractor = null,
        PdfMergeService? merger = null,
        PdfIntegrityValidator? validator = null,
        Action<
            ReconciliationWorkItem,
            ArchivePdfPreparationStage>? stageChanged = null)
    {
        _workspace = workspace;
        _extractor = extractor ?? new ArchivePdfExtractor();
        _merger = merger ?? new PdfMergeService();
        _validator = validator ?? new PdfIntegrityValidator();
        _stageChanged = stageChanged;
    }

    public async Task<ReconciliationUploadPayloadCreationResult>
        CreatePayloadAsync(
            ReconciliationWorkItem workItem)
    {
        if (workItem.FileKind != ReconciliationFileKind.Archive)
        {
            return ReconciliationUploadPayloadCreationResult.Failure(
                "当前文件不是受支持的压缩包");
        }

        ReconciliationTemporaryItemWorkspace itemWorkspace =
            _workspace.CreateItem();

        try
        {
            _stageChanged?.Invoke(
                workItem,
                ArchivePdfPreparationStage.Extracting);
            IReadOnlyList<string> pdfPaths =
                await _extractor.ExtractPdfsAsync(
                    workItem.SourcePath,
                    itemWorkspace.ExtractedDirectoryPath);
            if (pdfPaths.Count == 0)
            {
                return FailureWithCleanup(
                    itemWorkspace,
                    "压缩包内没有找到 PDF 文件");
            }

            foreach (string pdfPath in pdfPaths)
            {
                PdfValidationResult validation =
                    await _validator.ValidateAsync(
                        pdfPath,
                        itemWorkspace.DirectoryPath);
                if (!validation.IsValid)
                {
                    return FailureWithCleanup(
                        itemWorkspace,
                        $"压缩包内 PDF 校验失败：{validation.Error}");
                }
            }

            string uploadPath = pdfPaths[0];
            if (pdfPaths.Count > 1)
            {
                _stageChanged?.Invoke(
                    workItem,
                    ArchivePdfPreparationStage.Merging);
                await _merger.MergeAsync(
                    pdfPaths,
                    itemWorkspace.OutputPdfPath);
                PdfValidationResult mergedValidation =
                    await _validator.ValidateAsync(
                        itemWorkspace.OutputPdfPath,
                        itemWorkspace.DirectoryPath);
                if (!mergedValidation.IsValid)
                {
                    return FailureWithCleanup(
                        itemWorkspace,
                        $"合并 PDF 校验失败：{mergedValidation.Error}");
                }

                uploadPath = itemWorkspace.OutputPdfPath;
            }

            return ReconciliationUploadPayloadCreationResult.Success(
                new ReconciliationUploadPayload(
                    workItem,
                    uploadPath,
                    isTemporary: true,
                    itemWorkspace.Cleanup));
        }
        catch (FileNotFoundException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源压缩包不存在");
        }
        catch (DirectoryNotFoundException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源压缩包不存在");
        }
        catch (ArchivePasswordRequiredException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "压缩包已加密，当前流程不支持密码解压");
        }
        catch (ArchivePathException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "压缩包包含不安全的文件路径，已停止解压");
        }
        catch (ArchiveLimitException exception)
        {
            return FailureWithCleanup(
                itemWorkspace,
                exception.Message);
        }
        catch (PdfMergeException exception)
        {
            return FailureWithCleanup(
                itemWorkspace,
                exception.Message);
        }
        catch (Exception exception) when (
            exception is InvalidFormatException
            or ArchiveOperationException
            or SharpCompressException
            or SharpCompress.Common.CryptographicException
            or System.Security.Cryptography.CryptographicException
            or InvalidOperationException
            or NotSupportedException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "压缩包损坏、加密或格式不受支持");
        }
        catch (UnauthorizedAccessException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源压缩包或临时目录没有访问权限");
        }
        catch (IOException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "压缩包读取、解压或 PDF 合并时发生 I/O 错误");
        }
        catch (System.Security.SecurityException)
        {
            return FailureWithCleanup(
                itemWorkspace,
                "源压缩包或临时目录不可访问");
        }
        finally
        {
            _stageChanged?.Invoke(
                workItem,
                ArchivePdfPreparationStage.Processing);
        }
    }

    private static ReconciliationUploadPayloadCreationResult
        FailureWithCleanup(
            ReconciliationTemporaryItemWorkspace itemWorkspace,
            string error)
    {
        string? warning = itemWorkspace.Cleanup();
        return ReconciliationUploadPayloadCreationResult.Failure(
            error,
            warning is null
                ? Array.Empty<string>()
                : [warning]);
    }
}

internal sealed class ArchivePdfExtractor
{
    private const int MaxEntryCount = 50_000;
    private const int MaxNestedArchiveDepth = 1;
    private const long MaxExtractedBytes =
        10L * 1024 * 1024 * 1024;
    private static readonly HashSet<string>
        SupportedArchiveExtensions = new(
            [".zip", ".rar", ".7z"],
            StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> ExtractPdfsAsync(
        string archivePath,
        string destinationDirectory)
    {
        return Task.Run<IReadOnlyList<string>>(
            () => ExtractPdfs(
                archivePath,
                destinationDirectory));
    }

    private static IReadOnlyList<string> ExtractPdfs(
        string archivePath,
        string destinationDirectory)
    {
        string fullArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchivePath))
        {
            throw new FileNotFoundException(
                "Archive source does not exist.",
                fullArchivePath);
        }

        string extractionRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(destinationDirectory));
        Directory.CreateDirectory(extractionRoot);

        string archiveStorageRoot = Path.Combine(
            extractionRoot,
            ".archives");
        ExtractionContext context = new(archiveStorageRoot);
        ExtractArchive(
            fullArchivePath,
            extractionRoot,
            depth: 0,
            context);

        return context.PdfPaths
            .OrderBy(
                path => Path.GetRelativePath(
                    extractionRoot,
                    path),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                path => Path.GetRelativePath(
                    extractionRoot,
                    path),
                StringComparer.Ordinal)
            .ToArray();
    }

    private static void ExtractArchive(
        string archivePath,
        string pdfDestinationRoot,
        int depth,
        ExtractionContext context)
    {
        using IArchive archive = ArchiveFactory.OpenArchive(
            archivePath);
        if (archive.IsEncrypted)
        {
            throw new ArchivePasswordRequiredException();
        }

        foreach (IArchiveEntry entry in archive.Entries)
        {
            context.IncrementEntryCount();
            if (entry.IsDirectory)
            {
                continue;
            }

            if (entry.IsEncrypted)
            {
                throw new ArchivePasswordRequiredException();
            }

            string key = entry.Key ?? string.Empty;
            string extension = Path.GetExtension(key);
            if (string.Equals(
                    extension,
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                string destinationPath = ResolveSafeDestination(
                    pdfDestinationRoot,
                    key);
                ExtractEntry(
                    entry,
                    destinationPath,
                    context);
                context.PdfPaths.Add(destinationPath);
                continue;
            }

            if (depth >= MaxNestedArchiveDepth
                || !SupportedArchiveExtensions.Contains(extension))
            {
                continue;
            }

            string nestedArchivePath = ResolveSafeDestination(
                context.ArchiveStorageRoot,
                key);
            ExtractEntry(
                entry,
                nestedArchivePath,
                context);

            string nestedPdfRoot = ResolveSafeDestination(
                pdfDestinationRoot,
                $"{key}.contents");
            Directory.CreateDirectory(nestedPdfRoot);
            ExtractArchive(
                nestedArchivePath,
                nestedPdfRoot,
                depth + 1,
                context);
        }
    }

    private static void ExtractEntry(
        IArchiveEntry entry,
        string destinationPath,
        ExtractionContext context)
    {
        string? parentDirectory =
            Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new ArchivePathException();
        }

        Directory.CreateDirectory(parentDirectory);
        using Stream source = entry.OpenEntryStream();
        using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        context.CopyWithLimit(
            source,
            destination);
    }

    private static string ResolveSafeDestination(
        string extractionRoot,
        string entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            throw new ArchivePathException();
        }

        string normalizedKey = entryKey
            .Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar)
            .Replace(
                '/',
                Path.DirectorySeparatorChar)
            .Replace(
                '\\',
                Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedKey))
        {
            throw new ArchivePathException();
        }

        string destinationPath = Path.GetFullPath(
            Path.Combine(
                extractionRoot,
                normalizedKey));
        string relativePath = Path.GetRelativePath(
            extractionRoot,
            destinationPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals(
                "..",
                StringComparison.Ordinal)
            || relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || relativePath.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new ArchivePathException();
        }

        return destinationPath;
    }

    private sealed class ExtractionContext(
        string archiveStorageRoot)
    {
        private int _entryCount;
        private long _extractedBytes;

        public string ArchiveStorageRoot { get; } =
            archiveStorageRoot;

        public List<string> PdfPaths { get; } = [];

        public void IncrementEntryCount()
        {
            _entryCount++;
            if (_entryCount > MaxEntryCount)
            {
                throw new ArchiveLimitException(
                    "压缩包文件数量过多，已停止解压");
            }
        }

        public void CopyWithLimit(
            Stream source,
            Stream destination)
        {
            byte[] buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = source.Read(
                       buffer,
                       0,
                       buffer.Length)) > 0)
            {
                _extractedBytes += bytesRead;
                if (_extractedBytes > MaxExtractedBytes)
                {
                    throw new ArchiveLimitException(
                        "压缩包解压总大小超过 10 GB，已停止解压");
                }

                destination.Write(
                    buffer,
                    0,
                    bytesRead);
            }
        }
    }
}

internal sealed class PdfMergeService
{
    public Task MergeAsync(
        IReadOnlyList<string> sourcePaths,
        string outputPath)
    {
        return Task.Run(() => Merge(
            sourcePaths,
            outputPath));
    }

    private static void Merge(
        IReadOnlyList<string> sourcePaths,
        string outputPath)
    {
        if (sourcePaths.Count < 2)
        {
            throw new PdfMergeException(
                "合并 PDF 时没有足够的源文件");
        }

        List<PdfDocument> sourceDocuments = [];

        try
        {
            using PdfDocument outputDocument = new();
            foreach (string sourcePath in sourcePaths)
            {
                PdfDocument sourceDocument;
                try
                {
                    sourceDocument = PdfReader.Open(
                        sourcePath,
                        PdfDocumentOpenMode.Import);
                }
                catch (Exception exception) when (
                    exception is PdfReaderException
                    or InvalidOperationException
                    or NotSupportedException)
                {
                    throw new PdfMergeException(
                        $"无法读取压缩包内 PDF：{Path.GetFileName(sourcePath)}",
                        exception);
                }

                sourceDocuments.Add(sourceDocument);
                for (int index = 0;
                     index < sourceDocument.PageCount;
                     index++)
                {
                    outputDocument.AddPage(
                        sourceDocument.Pages[index]);
                }
            }

            if (outputDocument.PageCount == 0)
            {
                throw new PdfMergeException(
                    "压缩包内 PDF 没有可合并的页面");
            }

            outputDocument.Save(outputPath);
        }
        finally
        {
            foreach (PdfDocument document in sourceDocuments)
            {
                document.Dispose();
            }
        }
    }
}

internal sealed class ArchivePasswordRequiredException : Exception;

internal sealed class ArchivePathException : Exception;

internal sealed class ArchiveLimitException(string message)
    : Exception(message);

internal sealed class PdfMergeException : Exception
{
    public PdfMergeException(string message)
        : base(message)
    {
    }

    public PdfMergeException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException)
    {
    }
}
