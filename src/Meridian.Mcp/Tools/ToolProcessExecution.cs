using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Meridian.Mcp.Tools;

/// <summary>
/// Starts a tool inside an operating-system containment boundary. Windows creation is suspended
/// until the process belongs to a kill-on-close Job Object. Linux creation runs through
/// <c>setsid</c> so the process-group identity remains addressable after its leader exits.
/// </summary>
internal sealed class ToolProcessExecution : IDisposable
{
    private static readonly TimeSpan ContainmentStartupGrace = TimeSpan.FromSeconds(5);
    private const string LinuxBootstrapScript =
        "exec 3<&0\n" +
        "IFS= read -r _ <&3 || exit 125\n" +
        "\"$@\" </dev/null 3<&- &\n" +
        "tool_pid=$!\n" +
        "wait \"$tool_pid\"\n" +
        "tool_status=$?\n" +
        "(IFS= read -r _ <&3) &\n" +
        "exit \"$tool_status\"";

    private readonly IToolProcessContainment _containment;
    private bool _disposed;
    private bool _terminationRequested;

    private ToolProcessExecution(
        Process process,
        StreamReader standardOutput,
        StreamReader standardError,
        IToolProcessContainment containment)
    {
        Process = process;
        StandardOutput = standardOutput;
        StandardError = standardError;
        _containment = containment;
    }

    public Process Process { get; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public static ToolProcessExecution Start(ProcessStartInfo startInfo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (string.IsNullOrWhiteSpace(startInfo.FileName))
            throw new ArgumentException("A tool executable is required.", nameof(startInfo));

        if (OperatingSystem.IsWindows())
            return StartWindows(startInfo, ct);
        if (OperatingSystem.IsLinux())
            return StartLinux(startInfo, ct);

        ct.ThrowIfCancellationRequested();
        return StartPortable(startInfo);
    }

    public Exception? RequestTermination()
    {
        if (_terminationRequested)
            return null;

        try
        {
            var failure = _containment.RequestTermination();
            _terminationRequested = failure is null;
            return failure;
        }
        catch (Exception ex)
        {
            // Termination is cleanup. Surface the failure to the runner so it can preserve the
            // caller's cancellation/deadline exception. Leave termination unconfirmed so Dispose
            // makes one final best-effort attempt.
            return ex;
        }
    }

    public Task<Exception?> WaitForContainmentExitAsync(TimeSpan grace) =>
        _containment.WaitForExitAsync(grace);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // This is a final fail-safe for exceptions outside the normal cancellation/timeout paths.
        // The Windows Job Object is additionally kill-on-close.
        if (!_terminationRequested)
            _ = RequestTermination();
        DisposeNoThrow(_containment);
        DisposeNoThrow(StandardOutput);
        DisposeNoThrow(StandardError);
        DisposeNoThrow(Process);
    }

    private static void DisposeNoThrow(IDisposable resource)
    {
        try
        {
            resource.Dispose();
        }
        catch
        {
            // This is a final cleanup path. The runner has already captured any actionable
            // termination/drain failure, so disposal must not mask cancellation or timeout.
        }
    }

    [SupportedOSPlatform("windows")]
    private static ToolProcessExecution StartWindows(ProcessStartInfo startInfo, CancellationToken ct)
    {
        var job = WindowsJobContainment.Create();
        AnonymousPipeServerStream? outputPipe = null;
        AnonymousPipeServerStream? errorPipe = null;
        SafeFileHandle? nullInput = null;
        WindowsProcessAttributeList? processAttributes = null;
        Process? process = null;
        IntPtr processHandle = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;

        try
        {
            outputPipe = new AnonymousPipeServerStream(
                PipeDirection.In,
                HandleInheritability.Inheritable);
            errorPipe = new AnonymousPipeServerStream(
                PipeDirection.In,
                HandleInheritability.Inheritable);
            nullInput = WindowsNativeMethods.OpenInheritedNullInput();

            processAttributes = WindowsProcessAttributeList.Create(
                nullInput.DangerousGetHandle(),
                outputPipe.ClientSafePipeHandle.DangerousGetHandle(),
                errorPipe.ClientSafePipeHandle.DangerousGetHandle());
            var startupInfo = new WindowsNativeMethods.StartupInfoEx
            {
                StartupInfo = new WindowsNativeMethods.StartupInfo
                {
                    Size = Marshal.SizeOf<WindowsNativeMethods.StartupInfoEx>(),
                    Flags = WindowsNativeMethods.StartfUseStdHandles,
                    StandardInput = nullInput.DangerousGetHandle(),
                    StandardOutput = outputPipe.ClientSafePipeHandle.DangerousGetHandle(),
                    StandardError = errorPipe.ClientSafePipeHandle.DangerousGetHandle()
                },
                AttributeList = processAttributes.Pointer
            };
            var commandLine = BuildWindowsCommandLine(startInfo);
            environment = WindowsNativeMethods.CreateEnvironmentBlock(startInfo.Environment);
            var creationFlags = WindowsNativeMethods.CreateSuspended
                | WindowsNativeMethods.CreateUnicodeEnvironment
                | WindowsNativeMethods.ExtendedStartupInfoPresent;
            if (startInfo.CreateNoWindow)
                creationFlags |= WindowsNativeMethods.CreateNoWindow;

            if (!WindowsNativeMethods.CreateProcess(
                    applicationName: null,
                    commandLine,
                    processAttributes: IntPtr.Zero,
                    threadAttributes: IntPtr.Zero,
                    inheritHandles: true,
                    creationFlags,
                    environment,
                    string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                        ? null
                        : startInfo.WorkingDirectory,
                    ref startupInfo,
                    out var processInformation))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"Could not start {startInfo.FileName} inside a Windows Job Object.");
            }

            processHandle = processInformation.Process;
            threadHandle = processInformation.Thread;
            outputPipe.DisposeLocalCopyOfClientHandle();
            errorPipe.DisposeLocalCopyOfClientHandle();
            nullInput.Dispose();
            nullInput = null;

            if (!WindowsNativeMethods.AssignProcessToJobObject(job.Handle, processHandle))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"Could not assign tool process {processInformation.ProcessId} to its Windows Job Object.");
            }

            process = Process.GetProcessById(unchecked((int)processInformation.ProcessId));
            ct.ThrowIfCancellationRequested();
            if (WindowsNativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"Could not resume tool process {processInformation.ProcessId} after containment was established.");
            }

            WindowsNativeMethods.CloseHandle(threadHandle);
            threadHandle = IntPtr.Zero;
            WindowsNativeMethods.CloseHandle(processHandle);
            processHandle = IntPtr.Zero;

            var standardOutput = new StreamReader(
                outputPipe,
                startInfo.StandardOutputEncoding ?? Console.OutputEncoding,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            var standardError = new StreamReader(
                errorPipe,
                startInfo.StandardErrorEncoding ?? Console.OutputEncoding,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            outputPipe = null;
            errorPipe = null;

            return new ToolProcessExecution(process, standardOutput, standardError, job);
        }
        catch
        {
            if (processHandle != IntPtr.Zero)
                _ = WindowsNativeMethods.TerminateProcess(processHandle, 1);
            DisposeNoThrow(job);
            if (process is not null)
                DisposeNoThrow(process);
            throw;
        }
        finally
        {
            if (threadHandle != IntPtr.Zero)
                WindowsNativeMethods.CloseHandle(threadHandle);
            if (processHandle != IntPtr.Zero)
                WindowsNativeMethods.CloseHandle(processHandle);
            if (environment != IntPtr.Zero)
                Marshal.FreeHGlobal(environment);
            if (processAttributes is not null)
                DisposeNoThrow(processAttributes);
            if (nullInput is not null)
                DisposeNoThrow(nullInput);
            if (outputPipe is not null)
                DisposeNoThrow(outputPipe);
            if (errorPipe is not null)
                DisposeNoThrow(errorPipe);
        }
    }

    [SupportedOSPlatform("linux")]
    private static ToolProcessExecution StartLinux(ProcessStartInfo startInfo, CancellationToken ct)
    {
        var setSidPath = File.Exists("/usr/bin/setsid")
            ? "/usr/bin/setsid"
            : File.Exists("/bin/setsid")
                ? "/bin/setsid"
                : throw new PlatformNotSupportedException(
                    "Linux tool containment requires the util-linux 'setsid' executable.");
        if (!File.Exists("/bin/sh"))
        {
            throw new PlatformNotSupportedException(
                "Linux tool containment requires the POSIX '/bin/sh' bootstrap executable.");
        }

        var containedStartInfo = CreateSetSidStartInfo(setSidPath, startInfo);
        var process = Process.Start(containedStartInfo)
            ?? throw new InvalidOperationException($"Could not start {startInfo.FileName}.");

        UnixProcessGroupContainment? containment = null;
        try
        {
            containment = UnixProcessGroupContainment.CreateVerified(
                process,
                process.StandardInput,
                ContainmentStartupGrace);
            ct.ThrowIfCancellationRequested();
            containment.ReleaseTarget();

            return new ToolProcessExecution(
                process,
                process.StandardOutput,
                process.StandardError,
                containment);
        }
        catch (Exception startupFailure) when (ct.IsCancellationRequested)
        {
            CleanupFailedLinuxStart(process, containment);
            throw new OperationCanceledException(
                $"Tool process {process.Id} was canceled while Linux containment was starting.",
                startupFailure,
                ct);
        }
        catch
        {
            CleanupFailedLinuxStart(process, containment);
            throw;
        }
    }

    [SupportedOSPlatform("linux")]
    private static void CleanupFailedLinuxStart(
        Process process,
        UnixProcessGroupContainment? containment)
    {
        if (containment is not null)
        {
            try
            {
                _ = containment.RequestTermination();
            }
            catch
            {
                // Preserve the startup exception; disposing the containment closes its gate.
            }
            DisposeNoThrow(containment);
        }
        else
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The target is still held behind the bootstrap gate. Preserve the startup error;
                // disposal below closes its input and prevents it from being released by us.
            }
        }

        try
        {
            _ = process.WaitForExit((int)ContainmentStartupGrace.TotalMilliseconds);
        }
        catch
        {
            // Cleanup must not replace the startup or caller-cancellation exception.
        }

        DisposeProcessStreamsNoThrow(process);
        DisposeNoThrow(process);
    }

    private static void DisposeProcessStreamsNoThrow(Process process)
    {
        try
        {
            DisposeNoThrow(process.StandardInput);
        }
        catch
        {
            // A stream property can itself throw when process startup is only partially complete.
        }

        try
        {
            DisposeNoThrow(process.StandardOutput);
        }
        catch
        {
            // Continue disposing the remaining redirected streams.
        }

        try
        {
            DisposeNoThrow(process.StandardError);
        }
        catch
        {
            // Continue to Process.Dispose without replacing the startup failure.
        }
    }

    private static ToolProcessExecution StartPortable(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        return new ToolProcessExecution(
            process,
            process.StandardOutput,
            process.StandardError,
            new PortableProcessTreeContainment(process));
    }

    private static ProcessStartInfo CreateSetSidStartInfo(
        string setSidPath,
        ProcessStartInfo target)
    {
        var startInfo = new ProcessStartInfo(setSidPath)
        {
            WorkingDirectory = target.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = target.CreateNoWindow,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = target.StandardOutputEncoding,
            StandardErrorEncoding = target.StandardErrorEncoding
        };
        startInfo.Environment.Clear();
        foreach (var (key, value) in target.Environment)
            startInfo.Environment[key] = value;

        if (target.ArgumentList.Count > 0)
        {
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("/bin/sh");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(LinuxBootstrapScript);
            startInfo.ArgumentList.Add("meridian-tool-bootstrap");
            startInfo.ArgumentList.Add(target.FileName);
            foreach (var argument in target.ArgumentList)
                startInfo.ArgumentList.Add(argument);
        }
        else
        {
            var bootstrapArguments = string.Join(
                ' ',
                new[]
                {
                    "--",
                    "/bin/sh",
                    "-c",
                    LinuxBootstrapScript,
                    "meridian-tool-bootstrap",
                    target.FileName
                }.Select(QuoteWindowsArgument));
            startInfo.Arguments = string.IsNullOrWhiteSpace(target.Arguments)
                ? bootstrapArguments
                : $"{bootstrapArguments} {target.Arguments}";
        }

        return startInfo;
    }

    private static StringBuilder BuildWindowsCommandLine(ProcessStartInfo startInfo)
    {
        var commandLine = new StringBuilder(QuoteWindowsArgument(startInfo.FileName));
        if (startInfo.ArgumentList.Count > 0)
        {
            foreach (var argument in startInfo.ArgumentList)
            {
                commandLine.Append(' ');
                commandLine.Append(QuoteWindowsArgument(argument));
            }
        }
        else if (!string.IsNullOrWhiteSpace(startInfo.Arguments))
        {
            commandLine.Append(' ');
            commandLine.Append(startInfo.Arguments);
        }

        return commandLine;
    }

    private static string QuoteWindowsArgument(string value)
    {
        if (value.Length > 0 && !value.Any(static character => char.IsWhiteSpace(character) || character == '"'))
            return value;

        var quoted = new StringBuilder(value.Length + 2);
        quoted.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                quoted.Append('\\', (backslashes * 2) + 1);
                quoted.Append(character);
                backslashes = 0;
                continue;
            }

            quoted.Append('\\', backslashes);
            quoted.Append(character);
            backslashes = 0;
        }

        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }

    private interface IToolProcessContainment : IDisposable
    {
        Exception? RequestTermination();

        Task<Exception?> WaitForExitAsync(TimeSpan grace);
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsJobContainment(SafeJobHandle handle) : IToolProcessContainment
    {
        public SafeJobHandle Handle { get; } = handle;

        public static WindowsJobContainment Create()
        {
            var handle = WindowsNativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create a Windows Job Object.");

            var limits = new WindowsNativeMethods.JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new WindowsNativeMethods.JobObjectBasicLimitInformation
                {
                    LimitFlags = WindowsNativeMethods.JobObjectLimitKillOnJobClose
                }
            };
            if (!WindowsNativeMethods.SetInformationJobObject(
                    handle,
                    WindowsNativeMethods.JobObjectExtendedLimitInformationClass,
                    ref limits,
                    (uint)Marshal.SizeOf<WindowsNativeMethods.JobObjectExtendedLimitInformation>()))
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new Win32Exception(error, "Could not configure kill-on-close Windows Job Object containment.");
            }

            return new WindowsJobContainment(handle);
        }

        public Exception? RequestTermination()
        {
            if (Handle.IsClosed || Handle.IsInvalid)
                return null;

            var queryFailure = TryGetActiveProcessCount(out var activeProcesses);
            if (queryFailure is not null)
                return queryFailure;
            if (activeProcesses == 0)
                return null;

            return WindowsNativeMethods.TerminateJobObject(Handle, 1)
                ? null
                : new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not terminate the Windows Job Object that contains the tool process.");
        }

        public async Task<Exception?> WaitForExitAsync(TimeSpan grace)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < grace)
            {
                var queryFailure = TryGetActiveProcessCount(out var activeProcesses);
                if (queryFailure is not null)
                    return queryFailure;
                if (activeProcesses == 0)
                    return null;

                await Task.Delay(25).ConfigureAwait(false);
            }

            return new TimeoutException(
                $"The Windows Job Object still contained active tool processes after {grace.TotalSeconds:0.###} seconds.");
        }

        private Exception? TryGetActiveProcessCount(out uint activeProcesses)
        {
            activeProcesses = 0;
            if (!WindowsNativeMethods.QueryInformationJobObject(
                    Handle,
                    WindowsNativeMethods.JobObjectBasicAccountingInformationClass,
                    out var accounting,
                    (uint)Marshal.SizeOf<WindowsNativeMethods.JobObjectBasicAccountingInformation>(),
                    out _))
            {
                return new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not observe Windows Job Object termination.");
            }

            activeProcesses = accounting.ActiveProcesses;
            return null;
        }

        public void Dispose() => Handle.Dispose();
    }

    [SupportedOSPlatform("linux")]
    private sealed class UnixProcessGroupContainment : IToolProcessContainment
    {
        private const int NoSuchProcess = 3;
        private const int SignalKill = 9;
        private readonly int _processGroupId;
        private StreamWriter? _controlInput;
        private bool _targetReleased;

        private UnixProcessGroupContainment(int processGroupId, StreamWriter controlInput)
        {
            _processGroupId = processGroupId;
            _controlInput = controlInput;
        }

        public static UnixProcessGroupContainment CreateVerified(
            Process process,
            StreamWriter controlInput,
            TimeSpan grace)
        {
            var processId = process.Id;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < grace)
            {
                var observedGroupId = UnixNativeMethods.GetProcessGroupId(processId);
                if (observedGroupId == processId)
                    return new UnixProcessGroupContainment(processId, controlInput);

                if (observedGroupId < 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != NoSuchProcess)
                    {
                        throw new Win32Exception(
                            error,
                            $"Could not verify the Linux process group for tool process {processId}.");
                    }

                    if (process.HasExited)
                    {
                        throw new InvalidOperationException(
                            $"Linux tool bootstrap process {processId} exited before its process group was established.");
                    }
                }

                Thread.Sleep(1);
            }

            throw new TimeoutException(
                $"Linux tool bootstrap process {processId} did not establish process group {processId} " +
                $"within {grace.TotalSeconds:0.###} seconds.");
        }

        public void ReleaseTarget()
        {
            if (_targetReleased)
                return;

            var controlInput = _controlInput
                ?? throw new ObjectDisposedException(nameof(UnixProcessGroupContainment));
            controlInput.WriteLine("start");
            controlInput.Flush();
            _targetReleased = true;
        }

        public Exception? RequestTermination()
        {
            if (UnixNativeMethods.Kill(-_processGroupId, SignalKill) == 0)
            {
                DisposeControlInput();
                return null;
            }

            var error = Marshal.GetLastPInvokeError();
            DisposeControlInput();
            return error == NoSuchProcess
                ? null
                : new Win32Exception(
                    error,
                    $"Could not terminate Linux tool process group {_processGroupId}.");
        }

        public async Task<Exception?> WaitForExitAsync(TimeSpan grace)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < grace)
            {
                if (UnixNativeMethods.Kill(-_processGroupId, 0) != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    return error == NoSuchProcess
                            ? null
                            : new Win32Exception(
                                error,
                                $"Could not observe Linux tool process group {_processGroupId}.");
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            return new TimeoutException(
                $"Linux tool process group {_processGroupId} remained active after {grace.TotalSeconds:0.###} seconds.");
        }

        public void Dispose() => DisposeControlInput();

        private void DisposeControlInput()
        {
            var controlInput = Interlocked.Exchange(ref _controlInput, null);
            if (controlInput is null)
                return;

            try
            {
                controlInput.Dispose();
            }
            catch
            {
                // Closing the startup/keeper control pipe is best-effort cleanup.
            }
        }
    }

    private sealed class PortableProcessTreeContainment(Process process) : IToolProcessContainment
    {
        public Exception? RequestTermination()
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (Exception ex) when (ex is Win32Exception or AggregateException or NotSupportedException)
            {
                return ex;
            }
        }

        public async Task<Exception?> WaitForExitAsync(TimeSpan grace)
        {
            using var deadline = new CancellationTokenSource(grace);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                return null;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                return new TimeoutException(
                    $"Tool process {process.Id} remained active after {grace.TotalSeconds:0.###} seconds.");
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (Win32Exception ex)
            {
                return ex;
            }
        }

        public void Dispose()
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsProcessAttributeList : IDisposable
    {
        private IntPtr _pointer;
        private IntPtr _handleBuffer;
        private bool _initialized;

        private WindowsProcessAttributeList()
        {
        }

        public IntPtr Pointer => _pointer;

        public static WindowsProcessAttributeList Create(params IntPtr[] inheritedHandles)
        {
            var attributes = new WindowsProcessAttributeList();
            try
            {
                attributes.Initialize(inheritedHandles);
                return attributes;
            }
            catch
            {
                attributes.Dispose();
                throw;
            }
        }

        private void Initialize(IReadOnlyList<IntPtr> inheritedHandles)
        {
            nuint attributeListSize = 0;
            _ = WindowsNativeMethods.InitializeProcThreadAttributeList(
                IntPtr.Zero,
                attributeCount: 1,
                flags: 0,
                ref attributeListSize);
            if (attributeListSize == 0 || attributeListSize > int.MaxValue)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not size the Windows process handle allowlist.");
            }

            _pointer = Marshal.AllocHGlobal(checked((int)attributeListSize));
            if (!WindowsNativeMethods.InitializeProcThreadAttributeList(
                    _pointer,
                    attributeCount: 1,
                    flags: 0,
                    ref attributeListSize))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not initialize the Windows process handle allowlist.");
            }

            _initialized = true;
            var handleBufferSize = checked(inheritedHandles.Count * IntPtr.Size);
            _handleBuffer = Marshal.AllocHGlobal(handleBufferSize);
            for (var index = 0; index < inheritedHandles.Count; index++)
                Marshal.WriteIntPtr(_handleBuffer, index * IntPtr.Size, inheritedHandles[index]);

            if (!WindowsNativeMethods.UpdateProcThreadAttribute(
                    _pointer,
                    flags: 0,
                    WindowsNativeMethods.ProcThreadAttributeHandleList,
                    _handleBuffer,
                    checked((nuint)handleBufferSize),
                    previousValue: IntPtr.Zero,
                    returnSize: IntPtr.Zero))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not set the Windows process inherited-handle allowlist.");
            }
        }

        public void Dispose()
        {
            if (_initialized)
            {
                WindowsNativeMethods.DeleteProcThreadAttributeList(_pointer);
                _initialized = false;
            }

            if (_pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_pointer);
                _pointer = IntPtr.Zero;
            }

            if (_handleBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_handleBuffer);
                _handleBuffer = IntPtr.Zero;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static class WindowsNativeMethods
    {
        public const uint CreateSuspended = 0x00000004;
        public const uint CreateUnicodeEnvironment = 0x00000400;
        public const uint ExtendedStartupInfoPresent = 0x00080000;
        public const uint CreateNoWindow = 0x08000000;
        public const uint StartfUseStdHandles = 0x00000100;
        public const uint JobObjectLimitKillOnJobClose = 0x00002000;
        public const int JobObjectBasicAccountingInformationClass = 1;
        public const int JobObjectExtendedLimitInformationClass = 9;
        public static readonly IntPtr ProcThreadAttributeHandleList = new(0x00020002);

        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;

        public static SafeFileHandle OpenInheritedNullInput()
        {
            var securityAttributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                InheritHandle = true
            };
            var handle = CreateFile(
                "NUL",
                GenericRead,
                FileShareRead | FileShareWrite,
                ref securityAttributes,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not open NUL for tool-process input.");
            return handle;
        }

        public static IntPtr CreateEnvironmentBlock(IDictionary<string, string?> variables)
        {
            var entries = variables
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value ?? string.Empty}");
            // StringToHGlobalUni adds one terminal NUL; the embedded NUL makes the environment
            // block double-terminated, including when the environment is empty.
            return Marshal.StringToHGlobalUni(string.Join('\0', entries) + '\0');
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(
            string? applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string? currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(
            IntPtr attributeList,
            int attributeCount,
            uint flags,
            ref nuint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            nuint size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            SafeJobHandle job,
            int informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryInformationJobObject(
            SafeJobHandle job,
            int informationClass,
            out JobObjectBasicAccountingInformation information,
            uint informationLength,
            out uint returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            ref SecurityAttributes securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct StartupInfo
        {
            public int Size;
            public string? Reserved;
            public string? Desktop;
            public string? Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort Reserved2Size;
            public IntPtr Reserved2;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInfoEx
        {
            public StartupInfo StartupInfo;
            public IntPtr AttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;

            [MarshalAs(UnmanagedType.Bool)]
            public bool InheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectBasicLimitInformation
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
        public struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectBasicAccountingInformation
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
    }

    [SupportedOSPlatform("linux")]
    private static class UnixNativeMethods
    {
        [DllImport("libc", EntryPoint = "getpgid", SetLastError = true)]
        public static extern int GetProcessGroupId(int processId);

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        public static extern int Kill(int processId, int signal);
    }

    [SupportedOSPlatform("windows")]
    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => WindowsNativeMethods.CloseHandle(handle);
    }
}
