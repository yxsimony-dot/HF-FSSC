using System.Diagnostics;
using System.IO;

namespace FSSCWebLauncher;

internal sealed record OfficePdfWorkerExecutionResult(
    bool TimedOut,
    int? ExitCode,
    OfficePdfWorkerResult? WorkerResult,
    string? Error = null);

internal interface IOfficePdfWorkerProcessRunner
{
    Task<OfficePdfWorkerExecutionResult> RunAsync(
        string requestPath,
        OfficePdfWorkerRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

internal sealed class OfficePdfWorkerProcessRunner
    : IOfficePdfWorkerProcessRunner
{
    private readonly string _launcherPath;

    public OfficePdfWorkerProcessRunner(
        string? launcherPath = null)
    {
        _launcherPath = Path.GetFullPath(
            launcherPath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "无法确定 Office 转换工作进程路径"));
    }

    public async Task<OfficePdfWorkerExecutionResult> RunAsync(
        string requestPath,
        OfficePdfWorkerRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout));
        }

        string fullRequestPath = Path.GetFullPath(requestPath);
        string signalPath = Path.GetFullPath(
            request.StartSignalPath);
        DeleteIfPresent(signalPath);
        DeleteIfPresent(
            OfficePdfWorker.GetResultPath(fullRequestPath));
        OfficePdfWorker.WriteRequest(
            fullRequestPath,
            request);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _launcherPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        process.StartInfo.ArgumentList.Add(
            OfficePdfWorker.CommandName);
        process.StartInfo.ArgumentList.Add(
            fullRequestPath);

        using WindowsJobObject job = new();
        OfficeAutomationProcessTracker officeProcesses = new();
        bool assigned = false;

        try
        {
            if (!process.Start())
            {
                return new OfficePdfWorkerExecutionResult(
                    false,
                    null,
                    null,
                    "无法启动 Office 转换工作进程");
            }

            job.Assign(process);
            assigned = true;
            Directory.CreateDirectory(
                Path.GetDirectoryName(signalPath)!);
            File.WriteAllText(signalPath, string.Empty);

            bool timedOut = await WaitForExitAsync(
                process,
                job,
                officeProcesses,
                timeout,
                cancellationToken);
            if (timedOut)
            {
                job.Terminate();
                await WaitForExitAfterTerminationAsync(
                    process);
                return new OfficePdfWorkerExecutionResult(
                    true,
                    TryGetExitCode(process),
                    TryReadResult(fullRequestPath));
            }

            officeProcesses.AssignNewProcesses(job);
            OfficePdfWorkerResult? workerResult =
                TryReadResult(fullRequestPath);
            if (workerResult is null)
            {
                return new OfficePdfWorkerExecutionResult(
                    false,
                    process.ExitCode,
                    null,
                    "Office 转换工作进程未返回结果");
            }

            return new OfficePdfWorkerExecutionResult(
                false,
                process.ExitCode,
                workerResult);
        }
        catch (OperationCanceledException)
        {
            if (assigned)
            {
                TryTerminate(job);
            }
            else
            {
                TryKill(process);
            }

            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            if (assigned)
            {
                TryTerminate(job);
            }
            else
            {
                TryKill(process);
            }

            return new OfficePdfWorkerExecutionResult(
                false,
                TryGetExitCode(process),
                null,
                $"Office 转换工作进程启动或通信失败：{exception.Message}");
        }
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        WindowsJobObject job,
        OfficeAutomationProcessTracker officeProcesses,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        Task waitForExit = process.WaitForExitAsync(
            cancellationToken);

        while (!waitForExit.IsCompleted)
        {
            officeProcesses.AssignNewProcesses(job);
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                officeProcesses.AssignNewProcesses(job);
                return true;
            }

            TimeSpan delay = remaining < TimeSpan.FromMilliseconds(100)
                ? remaining
                : TimeSpan.FromMilliseconds(100);
            await Task.WhenAny(
                waitForExit,
                Task.Delay(delay, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        await waitForExit;
        return false;
    }

    private static OfficePdfWorkerResult? TryReadResult(
        string requestPath)
    {
        try
        {
            string resultPath =
                OfficePdfWorker.GetResultPath(requestPath);
            return File.Exists(resultPath)
                ? OfficePdfWorker.ReadResult(requestPath)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WaitForExitAfterTerminationAsync(
        Process process)
    {
        try
        {
            await process.WaitForExitAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Closing the job handle remains the final kill-on-close boundary.
        }
    }

    private static void TryTerminate(WindowsJobObject job)
    {
        try
        {
            job.Terminate();
        }
        catch
        {
            // Disposing the job still applies kill-on-close.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: false);
            }
        }
        catch
        {
            // The process may already have exited.
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited
                ? process.ExitCode
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

internal sealed class OfficeAutomationProcessTracker
{
    private static readonly string[] ProcessNames =
    [
        "wps",
        "et",
        "wpp"
    ];

    private readonly HashSet<int> _preexistingProcessIds =
        SnapshotProcessIds();
    private readonly HashSet<int> _assignedProcessIds = [];

    public void AssignNewProcesses(WindowsJobObject job)
    {
        foreach (Process process in GetOfficeProcesses())
        {
            using (process)
            {
                int processId;

                try
                {
                    processId = process.Id;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (_preexistingProcessIds.Contains(processId)
                    || _assignedProcessIds.Contains(processId))
                {
                    continue;
                }

                try
                {
                    job.Assign(process);
                    _assignedProcessIds.Add(processId);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
                {
                    // The process can exit between enumeration and assignment.
                }
            }
        }
    }

    private static HashSet<int> SnapshotProcessIds()
    {
        return GetOfficeProcesses()
            .Select(process =>
            {
                using (process)
                {
                    try
                    {
                        return process.Id;
                    }
                    catch (InvalidOperationException)
                    {
                        return -1;
                    }
                }
            })
            .Where(processId => processId >= 0)
            .ToHashSet();
    }

    private static IEnumerable<Process> GetOfficeProcesses()
    {
        foreach (string processName in ProcessNames)
        {
            Process[] processes;

            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (Process process in processes)
            {
                yield return process;
            }
        }
    }
}
