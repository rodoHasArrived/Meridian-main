using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;
using Meridian.Contracts.Operations;

namespace Meridian.Launcher;

internal static class Program
{
    private const string StartupOperationKind = "lifecycle.startup";

    [STAThread]
    private static int Main(string[] args)
    {
        var serviceDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "service");
        var receiptRoot = Path.Combine(serviceDataRoot, "receipts");
        var supervisorLogPath = Path.Combine(serviceDataRoot, "logs", "lifecycle-supervisor.log");
        var requestId = Guid.NewGuid().ToString("N");
        var launchStartedAtUtc = DateTimeOffset.UtcNow;
        var supervisorPath = Path.Combine(AppContext.BaseDirectory, "Meridian.LifecycleSupervisor.exe");
        if (!File.Exists(supervisorPath))
        {
            var message = "Meridian needs repair because its lifecycle supervisor is missing.";
            var receiptPath = TryPersistLauncherFailure(
                receiptRoot,
                requestId,
                launchStartedAtUtc,
                OperationTerminalState.Blocked,
                message,
                supervisorPath,
                supervisorLogPath,
                processStarted: false);
            MessageBox($"{message}\n\nVerified outcome: {receiptPath ?? "unavailable"}");
            return 4;
        }

        var start = new ProcessStartInfo(supervisorPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.Environment["MDC_LIFECYCLE_REQUEST_ID"] = requestId;
        if (args.Length == 0)
        {
            start.ArgumentList.Add("start");
        }
        else
        {
            foreach (var argument in args)
                start.ArgumentList.Add(argument);
        }

        var existingReceipts = StartupOutcomeReceiptMonitor.Capture(receiptRoot);
        Process? startedProcess;
        try
        {
            startedProcess = Process.Start(start);
        }
        catch (Exception ex)
        {
            var message = $"Meridian could not start its lifecycle supervisor ({ex.GetType().Name}): {ex.Message}";
            var receiptPath = TryPersistLauncherFailure(
                receiptRoot,
                requestId,
                launchStartedAtUtc,
                OperationTerminalState.Failed,
                message,
                supervisorPath,
                supervisorLogPath,
                processStarted: false,
                ex.GetType().Name);
            MessageBox($"{message}\n\nVerified outcome: {receiptPath ?? "unavailable"}\n" +
                       "Repair the installation, run preflight, and retry Meridian.");
            return 1;
        }
        using var process = startedProcess;
        if (process is null)
        {
            const string message =
                "Meridian could not start its lifecycle supervisor because the operating system returned no process.";
            var receiptPath = TryPersistLauncherFailure(
                receiptRoot,
                requestId,
                launchStartedAtUtc,
                OperationTerminalState.Failed,
                message,
                supervisorPath,
                supervisorLogPath,
                processStarted: false);
            MessageBox($"{message}\n\nVerified outcome: {receiptPath ?? "unavailable"}\n" +
                       "Repair the installation, run preflight, and retry Meridian.");
            return 1;
        }

        var startupDeadline = launchStartedAtUtc.AddSeconds(
            ReadStartupTimeoutSeconds(AppContext.BaseDirectory) + 30);
        while (DateTimeOffset.UtcNow < startupDeadline)
        {
            if (TryCompleteFromOutcome(
                    receiptRoot,
                    existingReceipts,
                    requestId,
                    launchStartedAtUtc,
                    serviceDataRoot,
                    out var outcomeExitCode))
                return outcomeExitCode;

            if (process.WaitForExit(250))
            {
                if (TryCompleteFromOutcome(
                        receiptRoot,
                        existingReceipts,
                        requestId,
                        launchStartedAtUtc,
                        serviceDataRoot,
                        out outcomeExitCode))
                    return outcomeExitCode;
                return ReportSupervisorExit(
                    process.ExitCode,
                    serviceDataRoot,
                    requestId,
                    launchStartedAtUtc,
                    supervisorPath);
            }
        }

        var timeoutMessage =
            $"Meridian startup did not produce a verified terminal outcome by {startupDeadline:O}.";
        var timeoutReceipt = TryPersistLauncherFailure(
            receiptRoot,
            requestId,
            launchStartedAtUtc,
            OperationTerminalState.Failed,
            timeoutMessage,
            supervisorPath,
            supervisorLogPath,
            processStarted: true,
            nameof(TimeoutException));
        MessageBox(
            $"{timeoutMessage}\n\n" +
            $"Launcher outcome: {timeoutReceipt ?? "unavailable"}\n" +
            $"Verified outcomes: {receiptRoot}\n" +
            $"Supervisor log: {supervisorLogPath}\n\n" +
            "Inspect the startup evidence, run Meridian.LifecycleSupervisor preflight, and retry Meridian.");
        return 1;
    }

    private static bool TryCompleteFromOutcome(
        string receiptRoot,
        IReadOnlyDictionary<string, StartupOutcomeReceiptFingerprint> existingReceipts,
        string requestId,
        DateTimeOffset launchStartedAtUtc,
        string serviceDataRoot,
        out int exitCode)
    {
        exitCode = 1;
        if (!StartupOutcomeReceiptMonitor.TryReadChanged(
                receiptRoot,
                existingReceipts,
                StartupOperationKind,
                requestId,
                launchStartedAtUtc,
                out var receipt,
                out var receiptPath))
            return false;

        exitCode = receipt!.State switch
        {
            OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings => 0,
            OperationTerminalState.Failed => 1,
            OperationTerminalState.Blocked => 4,
            _ => 1
        };
        if (receipt.State != OperationTerminalState.Succeeded)
        {
            MessageBox(
                $"Meridian startup completed with terminal state {receipt.State}.\n\n" +
                $"Verified outcome: {receiptPath}\n" +
                $"Supervisor log: {Path.Combine(serviceDataRoot, "logs", "lifecycle-supervisor.log")}\n\n" +
                string.Join("\n", receipt.Recovery.Select(item => item.Guidance)));
        }
        return true;
    }

    private static int ReportSupervisorExit(
        int exitCode,
        string serviceRoot,
        string requestId,
        DateTimeOffset launchStartedAtUtc,
        string supervisorPath)
    {
        var receiptRoot = Path.Combine(serviceRoot, "receipts");
        var supervisorLogPath = Path.Combine(serviceRoot, "logs", "lifecycle-supervisor.log");
        var message =
            $"The lifecycle supervisor exited with code {exitCode} without producing the request-bound startup outcome.";
        var launcherReceipt = TryPersistLauncherFailure(
            receiptRoot,
            requestId,
            launchStartedAtUtc,
            OperationTerminalState.Failed,
            message,
            supervisorPath,
            supervisorLogPath,
            processStarted: true,
            exceptionType: "SupervisorProcessExit");
        MessageBox(
            $"Meridian did not start successfully: {message}\n\n" +
            $"Launcher outcome: {launcherReceipt ?? "unavailable"}\n" +
            $"Verified outcomes: {receiptRoot}\n" +
            $"Supervisor log: {supervisorLogPath}\n\n" +
            "Inspect the supervisor evidence, repair the reported condition, run " +
            "Meridian.LifecycleSupervisor preflight, and retry Meridian.");
        return ClassifySupervisorExitWithoutVerifiedOutcome(exitCode);
    }

    internal static int ClassifySupervisorExitWithoutVerifiedOutcome(int supervisorExitCode)
        => 1;

    private static string? TryPersistLauncherFailure(
        string receiptRoot,
        string requestId,
        DateTimeOffset startedAtUtc,
        OperationTerminalState state,
        string message,
        string supervisorPath,
        string supervisorLogPath,
        bool processStarted,
        string? exceptionType = null)
    {
        try
        {
            return StartupOutcomeReceiptMonitor.PersistLauncherFailure(
                receiptRoot,
                requestId,
                startedAtUtc,
                state,
                message,
                supervisorPath,
                supervisorLogPath,
                processStarted,
                exceptionType);
        }
        catch
        {
            return null;
        }
    }

    private static int ReadStartupTimeoutSeconds(string installRoot)
    {
        var manifestPath = Path.Combine(installRoot, "service", "lifecycle-supervisor.json");
        if (!File.Exists(manifestPath))
            return 60;
        try
        {
            var manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath),
                LifecycleContractsJsonContext.Default.LifecycleSupervisorManifestDto);
            return Math.Clamp(manifest?.StartupTimeoutSeconds ?? 60, 1, 600);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return 60;
        }
    }

    private static void MessageBox(string message)
        => NativeMessageBox(IntPtr.Zero, message, "Meridian", 0x00000010);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int NativeMessageBox(IntPtr windowHandle, string text, string caption, uint type);
}
