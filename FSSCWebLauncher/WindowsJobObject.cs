using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FSSCWebLauncher;

internal sealed class WindowsJobObject : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private readonly SafeJobHandle _handle;

    public WindowsJobObject()
    {
        _handle = NativeMethods.CreateJobObject(
            IntPtr.Zero,
            null);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "无法创建 Office 转换作业对象");
        }

        JobObjectExtendedLimitInformation information = new()
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        int length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr buffer = Marshal.AllocHGlobal(length);

        try
        {
            Marshal.StructureToPtr(
                information,
                buffer,
                fDeleteOld: false);
            if (!NativeMethods.SetInformationJobObject(
                    _handle,
                    JobObjectExtendedLimitInformationClass,
                    buffer,
                    (uint)length))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "无法配置 Office 转换作业对象");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Assign(Process process)
    {
        if (!NativeMethods.AssignProcessToJobObject(
                _handle,
                process.Handle))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "无法将 Office 转换进程加入受控作业");
        }
    }

    public void Terminate(uint exitCode = 124)
    {
        if (!NativeMethods.TerminateJobObject(
                _handle,
                exitCode))
        {
            int error = Marshal.GetLastWin32Error();
            const int AccessDenied = 5;
            if (error != AccessDenied)
            {
                throw new Win32Exception(
                    error,
                    "无法终止超时的 Office 转换作业");
            }
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle
        : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateJobObjectW",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        internal static extern SafeJobHandle CreateJobObject(
            IntPtr jobAttributes,
            string? name);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            SafeJobHandle job,
            int informationClass,
            IntPtr information,
            uint informationLength);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(
            SafeJobHandle job,
            IntPtr process);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(
            SafeJobHandle job,
            uint exitCode);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
