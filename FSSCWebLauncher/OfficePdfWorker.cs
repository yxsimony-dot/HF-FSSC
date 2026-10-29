using System.IO;
using System.Text.Json;

namespace FSSCWebLauncher;

internal static class OfficePdfWorker
{
    internal const string CommandName = "--office-pdf-worker";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    public static bool TryGetRequestPath(
        IReadOnlyList<string> arguments,
        out string requestPath)
    {
        requestPath = string.Empty;
        if (arguments.Count != 2
            || !string.Equals(
                arguments[0],
                CommandName,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(arguments[1]))
        {
            return false;
        }

        requestPath = arguments[1];
        return true;
    }

    public static string GetResultPath(string requestPath)
    {
        return $"{Path.GetFullPath(requestPath)}.result.json";
    }

    public static int Run(
        string requestPath,
        WpsOfficePdfConverter? converter = null)
    {
        string fullRequestPath;
        try
        {
            fullRequestPath = Path.GetFullPath(requestPath);
        }
        catch
        {
            return 2;
        }

        string resultPath = GetResultPath(fullRequestPath);
        OfficePdfWorkerResult result;

        try
        {
            OfficePdfWorkerRequest request =
                JsonSerializer.Deserialize<OfficePdfWorkerRequest>(
                    File.ReadAllText(fullRequestPath),
                    JsonOptions)
                ?? throw new InvalidDataException(
                    "转换请求内容为空");
            if (!WaitForStartSignal(
                    request.StartSignalPath,
                    TimeSpan.FromSeconds(15)))
            {
                result = OfficePdfWorkerResult.Failure(
                    request.OutputPdfPath,
                    "START_SIGNAL_TIMEOUT",
                    "转换工作进程未收到启动信号");
            }
            else
            {
                if (request.DelayBeforeConversionMilliseconds > 0)
                {
                    Thread.Sleep(
                        request.DelayBeforeConversionMilliseconds);
                }

                result = (converter ?? new WpsOfficePdfConverter())
                    .Convert(request);
            }
        }
        catch (JsonException exception)
        {
            result = OfficePdfWorkerResult.Failure(
                string.Empty,
                "REQUEST_INVALID",
                $"转换请求格式无效：{exception.Message}");
        }
        catch (Exception exception)
        {
            result = OfficePdfWorkerResult.Failure(
                string.Empty,
                "WORKER_FAILED",
                $"转换工作进程失败：{exception.Message}");
        }

        try
        {
            WriteResult(resultPath, result);
            return result.Succeeded ? 0 : 1;
        }
        catch
        {
            return 3;
        }
    }

    internal static OfficePdfWorkerResult ReadResult(
        string requestPath)
    {
        return JsonSerializer.Deserialize<OfficePdfWorkerResult>(
                File.ReadAllText(GetResultPath(requestPath)),
                JsonOptions)
            ?? throw new InvalidDataException(
                "转换工作进程结果为空");
    }

    internal static void WriteRequest(
        string requestPath,
        OfficePdfWorkerRequest request)
    {
        string fullPath = Path.GetFullPath(requestPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(
                request,
                JsonOptions));
    }

    private static bool WaitForStartSignal(
        string startSignalPath,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        string fullPath = Path.GetFullPath(startSignalPath);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(fullPath))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    private static void WriteResult(
        string resultPath,
        OfficePdfWorkerResult result)
    {
        string temporaryPath = $"{resultPath}.tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                result,
                JsonOptions));
        File.Move(
            temporaryPath,
            resultPath,
            overwrite: true);
    }
}
