using System.Diagnostics;
using System.Reflection;
using Meridian.Storage.Archival;
using Meridian.Storage.Services;

namespace Meridian.ProcessTestHelper;

public static class ProcessTestHelperMarker
{
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
                throw new ArgumentException("A helper mode is required.", nameof(args));

            return args[0] switch
            {
                "write-immediately" => WriteImmediately(args),
                "wait-for-gate-and-write" => await WaitForGateAndWriteAsync(args).ConfigureAwait(false),
                "spawn-gated-mutation" => await SpawnGatedMutationAsync(args, detachChildOutput: false).ConfigureAwait(false),
                "spawn-detached-gated-mutation" => await SpawnGatedMutationAsync(args, detachChildOutput: true).ConfigureAwait(false),
                "delayed-spawn-gated-mutation" => await DelayedSpawnGatedMutationAsync(args).ConfigureAwait(false),
                "emit-output" => await EmitOutputAsync(args).ConfigureAwait(false),
                "audit-append-batch" => await AppendAuditBatchAsync(args).ConfigureAwait(false),
                "wal-append-and-wait" => await AppendWalAndWaitAsync(args).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(args), args[0], "Unknown helper mode.")
            };
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static int WriteImmediately(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 2);
        File.WriteAllText(args[1], "mutated");
        return 0;
    }

    private static async Task<int> AppendAuditBatchAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 6);
        var count = ParsePositiveInt(args[5], "count");
        var paths = Enumerable.Range(0, count)
            .Select(index => Path.Combine(args[1], $"{args[2]}-{index:D3}.jsonl"))
            .ToArray();
        foreach (var path in paths)
            await File.WriteAllTextAsync(path, path).ConfigureAwait(false);
        await File.WriteAllTextAsync(args[3], Environment.ProcessId.ToString()).ConfigureAwait(false);
        await WaitForFileAsync(args[4], TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        var audit = new AuditChainService();
        foreach (var path in paths)
            await audit.AppendEntryAsync(path).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> AppendWalAndWaitAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 4);
        await using var wal = new WriteAheadLog(args[1], new WalOptions
        {
            SyncMode = WalSyncMode.BatchedSync,
            SyncBatchSize = 1_000,
            MaxFlushDelay = TimeSpan.FromMilliseconds(50)
        });
        await wal.InitializeAsync().ConfigureAwait(false);
        await wal.AppendAsync(args[3], "PROCESS-RECOVERY").ConfigureAwait(false);
        await File.WriteAllTextAsync(args[2], "appended").ConfigureAwait(false);
        // The parent kills this process; neither FlushAsync nor DisposeAsync should establish
        // durability for this test. Only the lifecycle-owned delayed flush can do so.
        await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> WaitForGateAndWriteAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 3);
        await WaitForFileAsync(args[2], TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        File.WriteAllText(args[1], "mutated");
        return 0;
    }

    private static async Task<int> SpawnGatedMutationAsync(
        IReadOnlyList<string> args,
        bool detachChildOutput)
    {
        RequireArgumentCount(args, 4);
        var childStartInfo = CreateSelfStartInfo(
            "wait-for-gate-and-write",
            args[2],
            args[3]);
        childStartInfo.RedirectStandardOutput = detachChildOutput;
        childStartInfo.RedirectStandardError = detachChildOutput;
        using var child = Process.Start(childStartInfo)
            ?? throw new InvalidOperationException("Could not start the process-test descendant.");

        File.WriteAllText(
            args[1],
            child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (detachChildOutput)
            return 0;

        await child.WaitForExitAsync().ConfigureAwait(false);
        return child.ExitCode;
    }

    private static async Task<int> DelayedSpawnGatedMutationAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 5);
        await Task.Delay(ParsePositiveInt(args[1], "delayMilliseconds")).ConfigureAwait(false);
        return await SpawnGatedMutationAsync(
                new[] { args[0], args[2], args[3], args[4] },
                detachChildOutput: false)
            .ConfigureAwait(false);
    }

    private static async Task<int> EmitOutputAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 3);
        var lineCount = ParsePositiveInt(args[1], "lineCount");
        var payload = new string('x', ParsePositiveInt(args[2], "payloadLength"));

        for (var index = 0; index < lineCount; index++)
        {
            await Console.Out.WriteLineAsync($"stdout-{index:D4}:{payload}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync($"stderr-{index:D4}:{payload}").ConfigureAwait(false);
        }

        await Console.Out.FlushAsync().ConfigureAwait(false);
        await Console.Error.FlushAsync().ConfigureAwait(false);
        return 0;
    }

    private static ProcessStartInfo CreateSelfStartInfo(params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in args)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
            throw new ArgumentOutOfRangeException(name, value, "The value must be a positive integer.");

        return parsed;
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (deadline.Elapsed >= timeout)
                throw new TimeoutException($"Timed out waiting for gate file {path}.");
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private static void RequireArgumentCount(IReadOnlyCollection<string> args, int expected)
    {
        if (args.Count != expected)
            throw new ArgumentException($"Expected {expected - 1} argument(s) for the selected helper mode.");
    }
}
