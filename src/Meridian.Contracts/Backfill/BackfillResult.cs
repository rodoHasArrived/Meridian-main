using System.Linq;

namespace Meridian.Contracts.Backfill;

/// <summary>
/// Shared outcome payload for a single historical backfill run.
/// </summary>
public sealed record BackfillResult(
    bool Success,
    string Provider,
    string[] Symbols,
    DateOnly? From,
    DateOnly? To,
    long BarsWritten,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string? Error = null,
    string[]? SkippedSymbols = null,
    SymbolValidationSignal[]? SymbolValidationSignals = null
)
{
    /// <summary>
    /// Symbols skipped because their checkpoint already covered the requested date range.
    /// Only populated when resume-from-checkpoint mode is enabled on the backfill request.
    /// </summary>
    public string[] SkippedSymbols { get; init; } = SkippedSymbols ?? [];

    /// <summary>
    /// Per-symbol completeness signals (pass / warn / fail) produced at the end of the run.
    /// Provides operator-visible trust data for each symbol in <see cref="Symbols"/>.
    /// </summary>
    public SymbolValidationSignal[] SymbolValidationSignals { get; init; } = SymbolValidationSignals ?? [];

    public static BackfillResult Failed(string provider, IReadOnlyList<string> symbols, DateOnly? from, DateOnly? to, DateTimeOffset started, Exception ex)
        => new(false, provider, symbols.ToArray(), from, to, 0, started, DateTimeOffset.UtcNow, ex.Message);
}
