using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Meridian.ProcessIsolation;

/// <summary>
/// Resource ceilings applied to a contained process tree. Windows Job Objects enforce all three
/// ceilings in the kernel. Other platforms retain recursive lifecycle containment and aggregate
/// observation, but cannot claim the same hard-limit guarantee through this component.
/// </summary>
public sealed record ContainedProcessLimits(
    long MaxAggregateMemoryBytes,
    TimeSpan MaxAggregateCpuTime,
    int MaxActiveProcesses,
    bool RequireHardLimits = false)
{
    public static ContainedProcessLimits None { get; } = new(0, TimeSpan.Zero, 0);

    internal bool HasResourceLimits =>
        MaxAggregateMemoryBytes > 0 || MaxAggregateCpuTime > TimeSpan.Zero || MaxActiveProcesses > 0;

    internal void Validate()
    {
        if (MaxAggregateMemoryBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxAggregateMemoryBytes));
        if (MaxAggregateCpuTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxAggregateCpuTime));
        if (MaxActiveProcesses < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxActiveProcesses));
        if (RequireHardLimits && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Hard contained-process memory, CPU, and process-count limits require Windows Job Objects.");
        }
    }
}

/// <summary>Aggregate resource observation for the root process and known descendants.</summary>
public readonly record struct ContainedProcessResourceSnapshot(
    long CurrentMemoryBytes,
    long PeakMemoryBytes,
    TimeSpan CpuTime,
    int ActiveProcessCount,
    bool HardLimitsApplied);

/// <summary>
/// Owns a child process and the smallest reusable containment boundary needed by
/// untrusted-workload clients. Windows children are assigned to a kill-on-close Job Object;
/// other platforms use the runtime's recursive process-tree termination support.
/// </summary>
/// <remarks>
/// This is process lifecycle containment, not an operating-system security sandbox. On
/// non-Windows platforms, and during the short Windows interval between start and Job
/// assignment, a deliberately escaping descendant can race containment.
/// </remarks>
public sealed class ContainedProcess : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan DefaultTerminationGrace = TimeSpan.FromSeconds(2);
    private readonly SafeJobHandle? _windowsJob;
    private readonly bool _hardLimitsApplied;
    private int _terminationRequested;
    private bool _disposed;

    private ContainedProcess(Process process, SafeJobHandle? windowsJob, bool hardLimitsApplied)
    {
        Process = process;
        _windowsJob = windowsJob;
        _hardLimitsApplied = hardLimitsApplied;
    }

    /// <summary>The root child process.</summary>
    public Process Process { get; }

    /// <summary>
    /// Starts a child without shell interpretation and establishes platform containment.
    /// The executable path must already be absolute and resolved by the caller.
    /// </summary>
    public static ContainedProcess Start(
        ProcessStartInfo startInfo,
        ContainedProcessLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        limits ??= ContainedProcessLimits.None;
        limits.Validate();

        if (startInfo.UseShellExecute)
            throw new ArgumentException("Contained processes cannot use shell execution.", nameof(startInfo));
        if (!Path.IsPathFullyQualified(startInfo.FileName) || !File.Exists(startInfo.FileName))
        {
            throw new FileNotFoundException(
                "Contained process executable must be an existing absolute path.",
                startInfo.FileName);
        }
        if (!startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError)
        {
            throw new ArgumentException(
                "Contained processes must redirect stdout and stderr so callers can bound both streams.",
                nameof(startInfo));
        }

        var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("The contained process could not be started.");

        SafeJobHandle? job = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                job = WindowsJob.Create(limits);
                if (!WindowsJob.AssignProcessToJobObject(job, process.Handle))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        $"Could not assign child process {process.Id} to its Windows Job Object.");
                }
            }

            return new ContainedProcess(
                process,
                job,
                hardLimitsApplied: OperatingSystem.IsWindows() && limits.HasResourceLimits);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Preserve the containment-establishment failure.
            }

            job?.Dispose();
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads aggregate resource usage. Windows values come from the Job Object and therefore cover
    /// descendants. Linux values are summed from the root's <c>/proc</c> child tree. Other platforms
    /// fall back to the root process and report that hard limits were not applied.
    /// </summary>
    public ContainedProcessResourceSnapshot GetResourceSnapshot()
    {
        if (_windowsJob is not null && !_windowsJob.IsClosed && !_windowsJob.IsInvalid &&
            WindowsJob.TryReadResourceSnapshot(_windowsJob, _hardLimitsApplied, out var windowsSnapshot))
        {
            return windowsSnapshot;
        }

        if (OperatingSystem.IsLinux() &&
            LinuxProcessTree.TryReadResourceSnapshot(Process.Id, out var linuxSnapshot))
        {
            return linuxSnapshot;
        }

        try
        {
            Process.Refresh();
            return new ContainedProcessResourceSnapshot(
                Math.Max(0, Process.WorkingSet64),
                Math.Max(0, Process.PeakWorkingSet64),
                Process.TotalProcessorTime,
                Process.HasExited ? 0 : 1,
                _hardLimitsApplied);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new ContainedProcessResourceSnapshot(0, 0, TimeSpan.Zero, 0, _hardLimitsApplied);
        }
    }

    /// <summary>Requests termination of the complete contained child tree.</summary>
    public async Task TerminateAsync(TimeSpan? grace = null)
    {
        if (Interlocked.Exchange(ref _terminationRequested, 1) != 0)
        {
            await WaitForExitBestEffortAsync(grace ?? DefaultTerminationGrace).ConfigureAwait(false);
            return;
        }

        try
        {
            if (_windowsJob is not null && !_windowsJob.IsClosed && !_windowsJob.IsInvalid)
            {
                if (!WindowsJob.TerminateJobObject(_windowsJob, 1))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (!Process.HasExited)
                        throw new Win32Exception(error, "Could not terminate the Windows Job Object.");
                }
            }
            else if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (Process.HasExited)
        {
            // The process exited between observation and termination.
        }

        await WaitForExitBestEffortAsync(grace ?? DefaultTerminationGrace).ConfigureAwait(false);
    }

    private async Task WaitForExitBestEffortAsync(TimeSpan grace)
    {
        if (Process.HasExited)
            return;

        using var deadline = new CancellationTokenSource(grace);
        try
        {
            await Process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!Process.HasExited)
            {
                try
                {
                    Process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (Process.HasExited)
                {
                    // Already gone.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!Process.HasExited)
            TerminateAsync().GetAwaiter().GetResult();
        _windowsJob?.Dispose();
        Process.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!Process.HasExited)
            await TerminateAsync().ConfigureAwait(false);
        _windowsJob?.Dispose();
        Process.Dispose();
    }

    private static class WindowsJob
    {
        private const uint JobObjectLimitJobTime = 0x00000004;
        private const uint JobObjectLimitActiveProcess = 0x00000008;
        private const uint JobObjectLimitJobMemory = 0x00000200;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectBasicAccountingInformationClass = 1;
        private const int JobObjectExtendedLimitInformationClass = 9;
        private const int JobObjectMemoryUsageInformationClass = 28;

        public static SafeJobHandle Create(ContainedProcessLimits limits)
        {
            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create a Windows Job Object.");

            var limitFlags = JobObjectLimitKillOnJobClose;
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = limitFlags
                }
            };

            if (limits.MaxAggregateMemoryBytes > 0)
            {
                information.JobMemoryLimit = checked((UIntPtr)(ulong)limits.MaxAggregateMemoryBytes);
                information.BasicLimitInformation.LimitFlags |= JobObjectLimitJobMemory;
            }
            if (limits.MaxAggregateCpuTime > TimeSpan.Zero)
            {
                information.BasicLimitInformation.PerJobUserTimeLimit = limits.MaxAggregateCpuTime.Ticks;
                information.BasicLimitInformation.LimitFlags |= JobObjectLimitJobTime;
            }
            if (limits.MaxActiveProcesses > 0)
            {
                information.BasicLimitInformation.ActiveProcessLimit = checked((uint)limits.MaxActiveProcesses);
                information.BasicLimitInformation.LimitFlags |= JobObjectLimitActiveProcess;
            }

            if (!SetInformationJobObject(
                    handle,
                    JobObjectExtendedLimitInformationClass,
                    ref information,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new Win32Exception(error, "Could not configure kill-on-close Windows Job containment.");
            }

            return handle;
        }

        public static bool TryReadResourceSnapshot(
            SafeJobHandle job,
            bool hardLimitsApplied,
            out ContainedProcessResourceSnapshot snapshot)
        {
            snapshot = default;
            if (!QueryInformationJobObjectAccounting(
                    job,
                    JobObjectBasicAccountingInformationClass,
                    out var accounting,
                    (uint)Marshal.SizeOf<JobObjectBasicAccountingInformation>(),
                    out _))
            {
                return false;
            }

            long currentMemory = 0;
            long peakMemory = 0;
            if (QueryInformationJobObjectMemory(
                    job,
                    JobObjectMemoryUsageInformationClass,
                    out var memory,
                    (uint)Marshal.SizeOf<JobObjectMemoryUsageInformation>(),
                    out _))
            {
                currentMemory = SaturatingToInt64(memory.JobMemory);
                peakMemory = SaturatingToInt64(memory.PeakJobMemoryUsed);
            }
            else if (QueryInformationJobObjectExtended(
                         job,
                         JobObjectExtendedLimitInformationClass,
                         out var extended,
                         (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>(),
                         out _))
            {
                peakMemory = SaturatingToInt64(extended.PeakJobMemoryUsed.ToUInt64());
                currentMemory = peakMemory;
            }

            var cpuTicks = Math.Max(0, accounting.TotalUserTime) + Math.Max(0, accounting.TotalKernelTime);
            snapshot = new ContainedProcessResourceSnapshot(
                currentMemory,
                peakMemory,
                TimeSpan.FromTicks(cpuTicks),
                checked((int)accounting.ActiveProcesses),
                hardLimitsApplied);
            return true;
        }

        private static long SaturatingToInt64(ulong value)
            => value > long.MaxValue ? long.MaxValue : (long)value;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            SafeJobHandle job,
            int informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObjectAccounting(
            SafeJobHandle job,
            int informationClass,
            out JobObjectBasicAccountingInformation information,
            uint informationLength,
            out uint returnLength);

        [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObjectMemory(
            SafeJobHandle job,
            int informationClass,
            out JobObjectMemoryUsageInformation information,
            uint informationLength,
            out uint returnLength);

        [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObjectExtended(
            SafeJobHandle job,
            int informationClass,
            out JobObjectExtendedLimitInformation information,
            uint informationLength,
            out uint returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

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
        private struct JobObjectBasicAccountingInformation
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectMemoryUsageInformation
        {
            public ulong JobMemory;
            public ulong PeakJobMemoryUsed;
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
    }

    private static class LinuxProcessTree
    {
        private const int ClockTicksName = 2;
        private static readonly long ClockTicksPerSecond = Math.Max(1, SysConf(ClockTicksName));

        public static bool TryReadResourceSnapshot(
            int rootProcessId,
            out ContainedProcessResourceSnapshot snapshot)
        {
            snapshot = default;
            var pending = new Stack<int>();
            var visited = new HashSet<int>();
            pending.Push(rootProcessId);
            long memory = 0;
            long cpuTicks = 0;

            while (pending.Count > 0)
            {
                var processId = pending.Pop();
                if (!visited.Add(processId))
                    continue;

                if (TryReadStat(processId, out var residentPages, out var processCpuTicks))
                {
                    memory = SaturatingAdd(memory, SaturatingMultiply(residentPages, Environment.SystemPageSize));
                    cpuTicks = SaturatingAdd(cpuTicks, processCpuTicks);
                }

                try
                {
                    var childrenPath = $"/proc/{processId}/task/{processId}/children";
                    if (!File.Exists(childrenPath))
                        continue;

                    foreach (var child in File.ReadAllText(childrenPath)
                                 .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (int.TryParse(child, out var childProcessId) && childProcessId > 0)
                            pending.Push(childProcessId);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A child can exit while /proc is being sampled.
                }
            }

            if (visited.Count == 0)
                return false;

            var seconds = cpuTicks / (double)ClockTicksPerSecond;
            snapshot = new ContainedProcessResourceSnapshot(
                memory,
                memory,
                TimeSpan.FromSeconds(seconds),
                visited.Count,
                HardLimitsApplied: false);
            return true;
        }

        private static bool TryReadStat(int processId, out long residentPages, out long cpuTicks)
        {
            residentPages = 0;
            cpuTicks = 0;
            try
            {
                var stat = File.ReadAllText($"/proc/{processId}/stat");
                var commandEnd = stat.LastIndexOf(')');
                if (commandEnd < 0 || commandEnd + 2 >= stat.Length)
                    return false;

                var fields = stat[(commandEnd + 2)..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (fields.Length <= 21 ||
                    !long.TryParse(fields[11], out var userTicks) ||
                    !long.TryParse(fields[12], out var kernelTicks) ||
                    !long.TryParse(fields[21], out residentPages))
                {
                    return false;
                }

                residentPages = Math.Max(0, residentPages);
                cpuTicks = SaturatingAdd(Math.Max(0, userTicks), Math.Max(0, kernelTicks));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static long SaturatingMultiply(long left, long right)
        {
            if (left <= 0 || right <= 0)
                return 0;
            return left > long.MaxValue / right ? long.MaxValue : left * right;
        }

        private static long SaturatingAdd(long left, long right)
            => right > long.MaxValue - left ? long.MaxValue : left + right;

        [DllImport("libc", EntryPoint = "sysconf", SetLastError = true)]
        private static extern long SysConf(int name);
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
