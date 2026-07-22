using Meridian.Contracts.Lifecycle;
using Meridian.Contracts.Operations;

namespace Meridian.LifecycleSupervisor;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "start";
        var requestId = LifecycleStartupOutcome.NormalizeRequestId(
            Environment.GetEnvironmentVariable(LifecycleStartupOutcome.RequestIdEnvironmentVariable));
        if (command is "--help" or "-h" or "help")
        {
            ShowUsage();
            return 0;
        }

        LifecycleSupervisorConfiguration configuration;
        try
        {
            configuration = LifecycleSupervisorConfiguration.Load(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            LifecycleStartupOutcomeReceipt? receipt = null;
            try
            {
                receipt = LifecycleStartupOutcome.PersistConfigurationBlocked(
                    AppContext.BaseDirectory,
                    ex,
                    requestId);
            }
            catch (Exception persistenceException)
            {
                Console.Error.WriteLine(
                    $"Verified Blocked outcome persistence also failed ({persistenceException.GetType().Name}): " +
                    persistenceException.Message);
            }
            Console.Error.WriteLine(
                $"Meridian startup is Blocked because lifecycle configuration could not be loaded " +
                $"({ex.GetType().Name}): {ex.Message}");
            if (receipt is not null)
                Console.Error.WriteLine($"Verified outcome: {receipt.ReceiptPath}");
            Console.Error.WriteLine(
                "Recovery: repair service/lifecycle-supervisor.json or its permissions, run preflight, then retry start.");
            return LifecycleSupervisorExitCode.Blocked;
        }
        if (command is "status" or "stop" or "restart" or "preflight" or "open")
        {
            return await SendCommandAsync(configuration, command, requestId).ConfigureAwait(false);
        }

        if (command is not ("start" or "run"))
        {
            Console.Error.WriteLine($"Unknown lifecycle supervisor command '{command}'.");
            ShowUsage();
            return LifecycleSupervisorExitCode.InvalidCommand;
        }

        using var mutex = new Mutex(
            initiallyOwned: false,
            $"Local\\{configuration.PipeName}");
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }
        if (!ownsMutex)
        {
            return await SendCommandAsync(
                configuration,
                command == "start" ? "open" : "status",
                requestId).ConfigureAwait(false);
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            await using var runtime = new LifecycleSupervisorRuntime(configuration);
            return await runtime.RunAsync(
                openBrowser: command == "start",
                requestId,
                shutdown.Token).ConfigureAwait(false);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static async Task<int> SendCommandAsync(
        LifecycleSupervisorConfiguration configuration,
        string command,
        string requestId)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            var commandTimeout = command == "open"
                ? TimeSpan.FromSeconds(configuration.Manifest.StartupTimeoutSeconds + 20)
                : TimeSpan.FromSeconds(10);
            var response = await LifecycleSupervisorClient.SendAsync(
                configuration.PipeName,
                new LifecycleSupervisorMessageDto { Command = command, RequestId = requestId },
                commandTimeout,
                CancellationToken.None).ConfigureAwait(false);
            if (response.Status is not null)
            {
                Console.WriteLine(LifecycleSupervisorConsole.Format(response.Status));
            }
            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                Console.WriteLine(response.Message);
            }
            if (command == "open" &&
                Enum.TryParse<OperationTerminalState>(response.Reason, ignoreCase: true, out var terminalState))
            {
                if (!string.IsNullOrWhiteSpace(response.Detail))
                    Console.WriteLine($"Verified outcome: {response.Detail}");
                return LifecycleSupervisorExitCode.FromOutcome(terminalState);
            }
            if (!response.Success)
                return command == "preflight"
                    ? LifecycleSupervisorExitCode.Blocked
                    : LifecycleSupervisorExitCode.Failed;
            if (command == "stop")
            {
                return await WaitForSupervisorStopAsync(configuration).ConfigureAwait(false)
                    ? LifecycleSupervisorExitCode.Succeeded
                    : LifecycleSupervisorExitCode.Failed;
            }
            return LifecycleSupervisorExitCode.Succeeded;
        }
        catch (TimeoutException)
        {
            if (command == "status")
            {
                Console.WriteLine("Meridian lifecycle supervisor is stopped.");
                return 0;
            }

            if (command == "preflight")
            {
                var preflight = LifecycleSupervisorPreflight.Evaluate(configuration);
                Console.WriteLine(preflight.Message);
                return preflight.Success
                    ? LifecycleSupervisorExitCode.Succeeded
                    : LifecycleSupervisorExitCode.Blocked;
            }

            if (command == "open")
            {
                var operationRequest = LifecycleStartupOutcome.CreateRequest(
                    configuration,
                    requestId,
                    browserRequested: true);
                var receipt = LifecycleStartupOutcome.Persist(
                    configuration,
                    operationRequest,
                    sessionId: requestId,
                    startedAtUtc: startedAtUtc,
                    state: OperationTerminalState.Blocked,
                    prerequisitesSatisfied: true,
                    readinessSatisfied: false,
                    terminalMessage: "The lifecycle supervisor command pipe was unavailable for the open request.",
                    browserRequested: true,
                    browserOpened: false,
                    exceptionType: nameof(TimeoutException));
                Console.Error.WriteLine($"Verified outcome: {receipt.ReceiptPath}");
                return LifecycleSupervisorExitCode.Blocked;
            }

            Console.Error.WriteLine("Meridian lifecycle supervisor is not running.");
            return LifecycleSupervisorExitCode.SupervisorUnavailable;
        }
    }

    private static async Task<bool> WaitForSupervisorStopAsync(
        LifecycleSupervisorConfiguration configuration)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(
            configuration.Manifest.ShutdownTimeoutSeconds +
            configuration.Manifest.DatabaseTimeoutSeconds +
            15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            try
            {
                await LifecycleSupervisorClient.SendAsync(
                    configuration.PipeName,
                    new LifecycleSupervisorMessageDto { Command = "status" },
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
        }

        Console.Error.WriteLine("Meridian lifecycle supervisor did not stop before its combined host/database deadline.");
        return false;
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Meridian.LifecycleSupervisor <start|run|stop|restart|status|preflight|open>");
    }
}

internal static class LifecycleSupervisorConsole
{
    public static string Format(LifecycleSupervisorStatusDto status)
    {
        var host = status.Host is null ? "stopped" : $"PID {status.Host.ProcessId}";
        var database = status.Database?.Mode == LifecycleDatabaseManagementMode.External
            ? "external (non-owning)"
            : status.Database?.ProcessId is { } databasePid
                ? $"dedicated PID {databasePid}"
                : "stopped";
        return $"Meridian supervisor: {(status.Running ? "running" : "stopped")}{Environment.NewLine}" +
               $"  Session: {status.SessionId ?? "none"}{Environment.NewLine}" +
               $"  Host: {host}{Environment.NewLine}" +
               $"  Database: {database}{Environment.NewLine}" +
               $"  Port: {status.HttpPort?.ToString() ?? "none"}{Environment.NewLine}" +
               $"  State: {status.HostLifecycle?.State.ToString() ?? "unknown"}{Environment.NewLine}" +
               $"  Manifest: {status.ManifestPath}";
    }
}
