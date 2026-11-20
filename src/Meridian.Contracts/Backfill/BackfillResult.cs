using System.Linq;
using System.Text.Json.Serialization;
using Meridian.Contracts.Operations;

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProviderProvenanceReceipt? ProviderProvenance { get; init; }

    [JsonIgnore]
    public ProviderProvenanceAccess? ProvenanceAccess { get; init; }

    public BackfillResult ForTenant(string? tenantId) => ProviderProvenance is not null && ProvenanceAccess?.IsVisibleTo(tenantId) != true
        ? this with { ProviderProvenance = null } : this;

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

    public BackfillQuotaReceipt? QuotaReceipt { get; init; }

    /// <summary>Immutable response revisions that were retained after storage confirmed this run's data.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BackfillBatchReceipt>? BatchReceipts { get; init; }

    public static BackfillResult Failed(string provider, IReadOnlyList<string> symbols, DateOnly? from, DateOnly? to, DateTimeOffset started, Exception ex)
        => new(false, provider, symbols.ToArray(), from, to, 0, started, DateTimeOffset.UtcNow, ex.Message);
}
