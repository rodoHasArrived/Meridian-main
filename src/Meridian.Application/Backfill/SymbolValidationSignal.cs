namespace Meridian.Application.Backfill;

/// <summary>
/// Per-symbol completeness signal produced at the end of a backfill run.
/// </summary>
/// <param name="Symbol">The ticker symbol this signal relates to.</param>
/// <param name="Status">
/// <c>"Pass"</c> — symbol fetched (or confirmed already covered) with at least one bar.
/// <c>"Warn"</c> — symbol fetched successfully but the provider returned zero bars.
/// <c>"Fail"</c> — symbol could not be fetched due to an error.
/// </param>
/// <param name="BarsWritten">
/// Bars written during this run. Zero for symbols that were skipped because their
/// checkpoint already covered the requested range.
/// </param>
/// <param name="CheckpointBarsWritten">
/// Bar count stored in the checkpoint from the previous run.
/// Non-null only when <see cref="Status"/> is <c>"Pass"</c> and the symbol was skipped
/// (resume mode, fully covered). Allows callers to reconcile expected vs. actual counts.
/// </param>
/// <param name="EffectiveFrom">Inclusive start date actually fetched this run (post-resume-advance).</param>
/// <param name="EffectiveTo">Inclusive end date actually fetched this run.</param>
/// <param name="Reason">Human-readable explanation for non-Pass statuses.</param>
public sealed record SymbolValidationSignal(
    string Symbol,
    string Status,
    long BarsWritten,
    long? CheckpointBarsWritten,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Reason = null
)
{
    /// <summary>Creates a Pass signal for a symbol that was fetched and returned bars.</summary>
    public static SymbolValidationSignal Pass(string symbol, long barsWritten, DateOnly? from, DateOnly? to)
        => new(symbol, "Pass", barsWritten, null, from, to);

    /// <summary>
    /// Creates a Pass signal for a symbol that was skipped because its checkpoint
    /// fully covered the requested date range.
    /// </summary>
    public static SymbolValidationSignal PassSkipped(string symbol, long checkpointBarsWritten, DateOnly? coveredThrough)
        => new(symbol, "Pass", 0L, checkpointBarsWritten, null, coveredThrough,
               $"Skipped — already covered through {coveredThrough:yyyy-MM-dd} ({checkpointBarsWritten} bars in checkpoint)");

    /// <summary>Creates a Warn signal for a symbol fetched without error but returning zero bars.</summary>
    public static SymbolValidationSignal Warn(string symbol, DateOnly? from, DateOnly? to, string reason)
        => new(symbol, "Warn", 0L, null, from, to, reason);

    /// <summary>Creates a Fail signal for a symbol that threw an exception during fetch.</summary>
    public static SymbolValidationSignal Fail(string symbol, DateOnly? from, DateOnly? to, string reason)
        => new(symbol, "Fail", 0L, null, from, to, reason);
}
