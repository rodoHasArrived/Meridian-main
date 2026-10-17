param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$OwnedPid
)

$ErrorActionPreference = 'Stop'
$job = [IntPtr]::Zero
$ownedProcess = [IntPtr]::Zero

try {
    # The handle belongs only to this keeper. Assign the idle Node supervisor
    # before READY; all commands it subsequently starts inherit job membership.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class MeridianOwnedJob
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
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
    private struct ExtendedLimits
    {
        public BasicLimits BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass,
        ref ExtendedLimits information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    private static Exception LastError(string operation)
    {
        int code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, operation + ": " + new Win32Exception(code).Message);
    }

    public static IntPtr Open(int pid)
    {
        IntPtr process = OpenProcess(0x00000101, false, pid); // PROCESS_SET_QUOTA | PROCESS_TERMINATE.
        if (process == IntPtr.Zero) throw LastError("OpenProcess");
        return process;
    }

    public static IntPtr Create(IntPtr process)
    {
        // Null security attributes create a non-inheritable, unnamed handle.
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw LastError("CreateJobObject");
        try
        {
            ExtendedLimits limits = new ExtendedLimits();
            limits.BasicLimitInformation.LimitFlags = 0x00002000; // KILL_ON_JOB_CLOSE; no breakaway.
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
                throw LastError("SetInformationJobObject");
            if (!AssignProcessToJobObject(job, process)) throw LastError("AssignProcessToJobObject");
            return job;
        }
        catch { CloseHandle(job); throw; }
    }
}
'@
    $ownedProcess = [MeridianOwnedJob]::Open($OwnedPid)
    [Console]::Out.WriteLine('OPENED')
    [Console]::Out.Flush()
    # The launcher authenticates its original Node process over private IPC
    # after OPENED. PID reuse cannot substitute a process that answers that
    # challenge. Assign only the retained handle, never reopen the numeric PID.
    if ([Console]::In.ReadLine() -eq 'ASSIGN') {
        $job = [MeridianOwnedJob]::Create($ownedProcess)
        [Console]::Out.WriteLine('READY')
        [Console]::Out.Flush()
        # Only the launcher owns the write end. Stop and launcher death both
        # deliver EOF, even when an intermediate command has already exited.
        [void][Console]::In.ReadToEnd()
    }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    if ($job -ne [IntPtr]::Zero) { [void][MeridianOwnedJob]::CloseHandle($job) }
    if ($ownedProcess -ne [IntPtr]::Zero) { [void][MeridianOwnedJob]::CloseHandle($ownedProcess) }
}
