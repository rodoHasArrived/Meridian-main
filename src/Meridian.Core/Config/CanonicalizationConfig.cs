namespace Meridian.Application.Config;

/// <summary>
/// Configuration for the deterministic canonicalization pipeline (Phase 2).
/// Controls whether events are enriched with canonical symbols, venues, and condition codes
/// before entering the <c>EventPipeline</c>.
/// </summary>
/// <param name="Enabled">Master switch for the canonicalization pipeline. Default is false.</param>
/// <param name="Version">Canonicalization mapping version stamped on enriched events. Bumped when mapping tables change.</param>
/// <param name="PilotSymbols">
/// Optional list of symbols to canonicalize. When empty or null, all symbols are canonicalized.
/// Use this for incremental rollout validation before enabling for the full universe.
/// </param>
/// <param name="EnableDualWrite">
/// When true, both raw and enriched events are persisted for parity validation.
/// When false, only the enriched event is persisted.
/// </param>
/// <param name="UnresolvedAlertThresholdPercent">
/// Alert threshold for unresolved mapping rate per provider. Default is 0.1%.
/// When exceeded for 5+ minutes, a warning is logged.
/// </param>
/// <param name="ConditionCodesPath">
/// Path to condition-codes.json mapping file. Null uses built-in defaults.
/// </param>
/// <param name="VenueMappingPath">
/// Path to venue-mapping.json mapping file. Null uses built-in defaults.
/// </param>
public sealed record CanonicalizationConfig(
    bool Enabled = false,
    int Version = 1,
    string[]? PilotSymbols = null,
    bool EnableDualWrite = false,
    double UnresolvedAlertThresholdPercent = 0.1,
    string? ConditionCodesPath = null,
    string? VenueMappingPath = null
);
