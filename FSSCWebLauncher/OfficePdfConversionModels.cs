namespace FSSCWebLauncher;

internal sealed record OfficePdfWorkerRequest(
    string SourcePath,
    string OutputPdfPath,
    ReconciliationFileKind FileKind,
    string StartSignalPath,
    int DelayBeforeConversionMilliseconds = 0);

internal sealed record OfficePdfWorkerResult(
    bool Succeeded,
    string OutputPdfPath,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static OfficePdfWorkerResult Success(string outputPdfPath)
    {
        return new OfficePdfWorkerResult(
            true,
            outputPdfPath);
    }

    public static OfficePdfWorkerResult Failure(
        string outputPdfPath,
        string errorCode,
        string errorMessage)
    {
        return new OfficePdfWorkerResult(
            false,
            outputPdfPath,
            errorCode,
            errorMessage);
    }
}
