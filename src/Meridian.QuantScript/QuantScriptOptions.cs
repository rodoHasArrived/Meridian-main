namespace Meridian.QuantScript;

/// <summary>
/// Configuration options for the QuantScript scripting environment.
/// Bind via <c>"QuantScript"</c> section in appsettings.json.
/// </summary>
public sealed class QuantScriptOptions
{
    public const string SectionName = "QuantScript";

    /// <summary>Directory to scan for .csx script files.</summary>
    public string ScriptsDirectory { get; init; } = "scripts";

    /// <summary>Maximum wall-clock seconds a script may run before cancellation.</summary>
    public int RunTimeoutSeconds { get; init; } = 300;

    /// <summary>Maximum seconds allowed for Roslyn compilation.</summary>
    public int CompilationTimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// When false (default), the compiler applies a best-effort advisory guard that rejects
    /// scripts referencing File/Network/Process/Reflection APIs (by source inspection) and
    /// disables <c>#r</c>/<c>#load</c> directives.
    /// <para>
    /// This is NOT a security sandbox: the guard is a source-level denylist that a determined
    /// author can bypass (for example via reflection or runtime type resolution). Do not run
    /// untrusted scripts on the strength of this flag alone; isolate them at the process or OS
    /// level. Set to <see langword="true"/> only for trusted authors who need those APIs.
    /// </para>
    /// </summary>
    public bool EnableUnsafeScripts { get; init; } = false;

    /// <summary>
    /// Maximum number of compiled scripts retained in the in-memory compilation cache. Each
    /// cached entry holds a Roslyn script/compilation graph, so an unbounded cache grows without
    /// limit on a long-running host. When the count exceeds this bound the oldest entries are
    /// evicted first (FIFO). Set to 0 or less to disable caching entirely.
    /// </summary>
    public int MaxCachedCompilations { get; init; } = 256;

    /// <summary>Soft limit on plot requests per run. Excess plots are silently dropped.</summary>
    public int MaxPlotsPerRun { get; init; } = 100;

    /// <summary>Default data root passed to BacktestProxy when not overridden in script.</summary>
    public string DefaultDataRoot { get; init; } = "./data";

    /// <summary>
    /// Maximum allowed increase in the isolated worker tree's observed memory (bytes) during one
    /// request. This portable secondary guard is enabled by default. Windows also receives the
    /// absolute Job Object memory limit in <see cref="MaxWorkerMemoryBytes"/>.
    /// </summary>
    public long MaxMemoryDeltaBytes { get; init; } = 384L * 1024 * 1024;

    /// <summary>
    /// Optional guard for maximum elapsed run time in milliseconds, evaluated after each run.
    /// Set to 0 or less to disable the guard.
    /// </summary>
    public int MaxRunElapsedMilliseconds { get; init; } = 0;

    /// <summary>
    /// Optional guard for maximum emitted artifacts (metrics + plots + trades + captured backtests).
    /// Set to 0 or less to disable the guard.
    /// </summary>
    public int MaxOutputItemsPerRun { get; init; } = 0;

    /// <summary>
    /// Optional absolute path to the dedicated QuantScript worker executable. Normal Meridian
    /// builds publish the worker under <c>workers/quant-script</c>; an explicit override is useful
    /// for hardened deployments. The path is never passed through a shell.
    /// </summary>
    public string? WorkerExecutablePath { get; init; }

    /// <summary>
    /// Optional absolute path to the dotnet host used only when launching a worker DLL instead of
    /// its normal apphost executable. Resolution failure is fatal; no shell fallback is used.
    /// </summary>
    public string? WorkerDotNetHostPath { get; init; }

    /// <summary>
    /// Optional absolute path to the dedicated QuantScript worker assembly. This is a fallback for
    /// environments that intentionally omit the apphost and requires the worker's own runtimeconfig
    /// and deps files. It never defaults to the shared QuantScript library.
    /// </summary>
    public string? WorkerAssemblyPath { get; init; }

    /// <summary>
    /// Optional absolute runtimeconfig path passed to <c>dotnet exec</c>. The resolver otherwise
    /// selects the matching worker runtimeconfig from the worker sidecar directory.
    /// </summary>
    public string? WorkerRuntimeConfigPath { get; init; }

    /// <summary>
    /// Optional absolute deps file passed to <c>dotnet exec</c>. The resolver otherwise selects
    /// the matching worker deps file from the worker sidecar directory.
    /// </summary>
    public string? WorkerDepsFilePath { get; init; }

    /// <summary>
    /// Absolute aggregate memory ceiling for the worker process tree. Windows enforces this as a
    /// Job Object limit; Linux uses aggregate <c>/proc</c> observation as a portable fallback.
    /// </summary>
    public long MaxWorkerMemoryBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>
    /// Aggregate CPU-time ceiling for the worker process tree. Windows enforces this in its Job
    /// Object; portable monitoring remains a best-effort fallback on other platforms.
    /// </summary>
    public int MaxWorkerCpuTimeSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum number of processes in the contained worker tree, including the worker itself.
    /// The default of one prevents child-process creation on Windows.
    /// </summary>
    public int MaxWorkerProcessCount { get; init; } = 1;

    /// <summary>
    /// When true, worker startup fails unless the platform can establish kernel-enforced memory,
    /// CPU, and process-count limits. The current hard-limit implementation uses Windows Job Objects.
    /// </summary>
    public bool RequireHardResourceLimits { get; init; }

    /// <summary>Maximum worker processes that may execute concurrently in one host.</summary>
    public int MaxConcurrentWorkers { get; init; } = 2;

    /// <summary>Maximum requests allowed to wait for a worker slot before admission fails closed.</summary>
    public int MaxQueuedWorkerRequests { get; init; } = 8;

    /// <summary>Maximum time a queued request may wait for a worker slot.</summary>
    public int WorkerQueueWaitTimeoutMilliseconds { get; init; } = 30_000;

    /// <summary>Maximum host-data RPC calls across one script run.</summary>
    public int MaxHostRpcCallsPerRun { get; init; } = 128;

    /// <summary>Maximum records returned across all host-data RPC calls in one script run.</summary>
    public int MaxHostRpcRecordsPerRun { get; init; } = 100_000;

    /// <summary>
    /// Maximum serialized bytes returned across all host-data RPC calls in one script run.
    /// Serialization writes into a bounded stream and stops before this aggregate can be exceeded.
    /// </summary>
    public int MaxHostRpcResponseBytesPerRun { get; init; } = 8 * 1024 * 1024;

    /// <summary>Maximum distinct symbols addressable through host-data RPC in one script run.</summary>
    public int MaxHostRpcSymbolsPerRun { get; init; } = 32;

    /// <summary>Maximum inclusive date range accepted by one host-data RPC request.</summary>
    public int MaxHostRpcDateRangeDays { get; init; } = 3_660;

    /// <summary>Maximum bytes in any single versioned worker-protocol frame.</summary>
    public int MaxWorkerProtocolBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum bytes accepted from worker stdout before the child tree is terminated.</summary>
    public int MaxWorkerStandardOutputBytes { get; init; } = 64 * 1024;

    /// <summary>Maximum bytes accepted from worker stderr before the child tree is terminated.</summary>
    public int MaxWorkerStandardErrorBytes { get; init; } = 64 * 1024;

    /// <summary>Interval used to sample the child working set for portable memory enforcement.</summary>
    public int WorkerMemoryPollIntervalMilliseconds { get; init; } = 25;

    /// <summary>Grace period for worker shutdown and tree cleanup after a terminal result.</summary>
    public int WorkerExitGraceMilliseconds { get; init; } = 2_000;

    /// <summary>
    /// File extension used to identify notebook documents in the scripts directory.
    /// Defaults to <c>.ipynb</c>.
    /// </summary>
    public string NotebookExtension { get; init; } = ".ipynb";

    internal static string? GetIsolationValidationFailure(QuantScriptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RunTimeoutSeconds <= 0)
            return "RunTimeoutSeconds must be positive.";
        if (options.CompilationTimeoutSeconds <= 0)
            return "CompilationTimeoutSeconds must be positive.";
        if (options.MaxWorkerMemoryBytes < 64L * 1024 * 1024)
            return "MaxWorkerMemoryBytes must be at least 67108864 bytes.";
        if (options.MaxMemoryDeltaBytes <= 0)
            return "MaxMemoryDeltaBytes must be positive for isolated execution.";
        if (options.MaxWorkerCpuTimeSeconds <= 0)
            return "MaxWorkerCpuTimeSeconds must be positive.";
        if (options.MaxWorkerProcessCount <= 0)
            return "MaxWorkerProcessCount must be positive.";
        if (options.MaxConcurrentWorkers <= 0 || options.MaxConcurrentWorkers > 64)
            return "MaxConcurrentWorkers must be between 1 and 64.";
        if (options.MaxQueuedWorkerRequests < 0 || options.MaxQueuedWorkerRequests > 4_096)
            return "MaxQueuedWorkerRequests must be between 0 and 4096.";
        if (options.WorkerQueueWaitTimeoutMilliseconds is < 1 or > 300_000)
            return "WorkerQueueWaitTimeoutMilliseconds must be between 1 and 300000.";
        if (options.MaxHostRpcCallsPerRun is < 1 or > 10_000)
            return "MaxHostRpcCallsPerRun must be between 1 and 10000.";
        if (options.MaxHostRpcRecordsPerRun is < 1 or > 10_000_000)
            return "MaxHostRpcRecordsPerRun must be between 1 and 10000000.";
        if (options.MaxHostRpcResponseBytesPerRun is < 1_024 or > 64 * 1024 * 1024)
            return "MaxHostRpcResponseBytesPerRun must be between 1024 and 67108864.";
        if (options.MaxHostRpcSymbolsPerRun is < 1 or > 10_000)
            return "MaxHostRpcSymbolsPerRun must be between 1 and 10000.";
        if (options.MaxHostRpcDateRangeDays is < 1 or > 36_600)
            return "MaxHostRpcDateRangeDays must be between 1 and 36600.";
        if (options.MaxWorkerProtocolBytes is < 1_024 or > 64 * 1024 * 1024)
            return "MaxWorkerProtocolBytes must be between 1024 and 67108864.";
        if (options.MaxWorkerStandardOutputBytes is <= 0 or > 64 * 1024 * 1024 ||
            options.MaxWorkerStandardErrorBytes is <= 0 or > 64 * 1024 * 1024)
        {
            return "Worker stdout and stderr limits must be between 1 and 67108864 bytes.";
        }
        if (options.WorkerMemoryPollIntervalMilliseconds is < 5 or > 1_000)
            return "WorkerMemoryPollIntervalMilliseconds must be between 5 and 1000.";
        if (options.WorkerExitGraceMilliseconds is < 100 or > 30_000)
            return "WorkerExitGraceMilliseconds must be between 100 and 30000.";
        if (!string.IsNullOrWhiteSpace(options.WorkerExecutablePath) &&
            !string.IsNullOrWhiteSpace(options.WorkerAssemblyPath))
        {
            return "Configure either WorkerExecutablePath or WorkerAssemblyPath, not both.";
        }

        return null;
    }
}
