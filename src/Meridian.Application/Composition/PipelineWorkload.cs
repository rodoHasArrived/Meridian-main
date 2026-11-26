namespace Meridian.Application.Composition;

/// <summary>
/// The kind of pipeline a host runs. Each workload owns its own write-ahead log and dedup
/// ledger directory, so a collector and a backfill sharing one data root never write, recover,
/// truncate or compact each other's durability state.
/// </summary>
public enum PipelineWorkload : byte
{
    /// <summary>Live streaming collection (headless collector, desktop and workstation hosts).</summary>
    Collector,

    /// <summary>Historical backfill.</summary>
    Backfill
}

/// <summary>Resolves the per-workload durability directories under a data root.</summary>
public static class PipelineDurabilityPaths
{
    /// <summary>Root of every workload's WAL directory; also the pre-workload shared WAL directory.</summary>
    public const string WalRootName = "_wal";

    /// <summary>Root of every workload's dedup ledger directory; also the pre-workload shared ledger directory.</summary>
    public const string DedupRootName = "_dedup";

    /// <summary>Every workload, in the order tools should visit them.</summary>
    public static IReadOnlyList<PipelineWorkload> AllWorkloads { get; } = [PipelineWorkload.Collector, PipelineWorkload.Backfill];

    /// <summary>Directory name used for <paramref name="workload"/> under the WAL and dedup roots.</summary>
    public static string DirectoryName(PipelineWorkload workload) => workload switch
    {
        PipelineWorkload.Collector => "collector",
        PipelineWorkload.Backfill => "backfill",
        _ => throw new ArgumentOutOfRangeException(nameof(workload), workload, "Unknown pipeline workload.")
    };

    /// <summary>Parses a workload name as written on the command line (case-insensitive).</summary>
    public static bool TryParse(string? value, out PipelineWorkload workload)
    {
        foreach (var candidate in AllWorkloads)
        {
            if (string.Equals(value, DirectoryName(candidate), StringComparison.OrdinalIgnoreCase))
            {
                workload = candidate;
                return true;
            }
        }

        workload = default;
        return false;
    }

    /// <summary>The pre-workload shared WAL directory, <c>{dataRoot}/_wal</c>.</summary>
    public static string LegacyWalDirectory(string dataRoot) => Path.Combine(dataRoot, WalRootName);

    /// <summary>The pre-workload shared dedup directory, <c>{dataRoot}/_dedup</c>.</summary>
    public static string LegacyDedupDirectory(string dataRoot) => Path.Combine(dataRoot, DedupRootName);

    /// <summary>The WAL directory owned by <paramref name="workload"/>.</summary>
    public static string WalDirectory(string dataRoot, PipelineWorkload workload)
        => Path.Combine(LegacyWalDirectory(dataRoot), DirectoryName(workload));

    /// <summary>The dedup ledger directory owned by <paramref name="workload"/>.</summary>
    public static string DedupDirectory(string dataRoot, PipelineWorkload workload)
        => Path.Combine(LegacyDedupDirectory(dataRoot), DirectoryName(workload));

    /// <summary>
    /// Whether <paramref name="workload"/> adopts state left in the shared pre-workload
    /// directories. Only the collector does, so exactly one owner replays legacy segments.
    /// </summary>
    public static bool AdoptsLegacyState(PipelineWorkload workload) => workload == PipelineWorkload.Collector;
}
