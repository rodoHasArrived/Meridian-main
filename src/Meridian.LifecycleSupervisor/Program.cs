using Meridian.Contracts.Lifecycle;

namespace Meridian.LifecycleSupervisor;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "start";
        if (command is "--help" or "-h" or "help")
        {
            ShowUsage();
            return 0;
        }

        var configuration = LifecycleSupervisorConfiguration.Load(AppContext.BaseDirectory);
        if (command is "status" or "stop" or "restart" or "preflight" or "open")
        {
            return await SendCommandAsync(configuration, command).ConfigureAwait(false);
        }

        if (command is not ("start" or "run"))
        {
            Console.Error.WriteLine($"Unknown lifecycle supervisor command '{command}'.");
            ShowUsage();
            return 2;
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
            return await SendCommandAsync(configuration, command == "start" ? "open" : "status").ConfigureAwait(false);
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
            return await runtime.RunAsync(openBrowser: command == "start", shutdown.Token).ConfigureAwait(false);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static async Task<int> SendCommandAsync(
        LifecycleSupervisorConfiguration configuration,
        string command)
    {
        try
        {
            var response = await LifecycleSupervisorClient.SendAsync(
                configuration.PipeName,
                new LifecycleSupervisorMessageDto { Command = command },
                TimeSpan.FromSeconds(10),
                CancellationToken.None).ConfigureAwait(false);
            if (response.Status is not null)
            {
                Console.WriteLine(LifecycleSupervisorConsole.Format(response.Status));
            }
            else if (!string.IsNullOrWhiteSpace(response.Message))
            {
                Console.WriteLine(response.Message);
            }
            if (!response.Success) return 1;
            if (command == "stop")
            {
                return await WaitForSupervisorStopAsync(configuration).ConfigureAwait(false)
                    ? 0
                    : 1;
            }
            return 0;
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
                return preflight.Success ? 0 : 1;
            }

            Console.Error.WriteLine("Meridian lifecycle supervisor is not running.");
            return 3;
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
