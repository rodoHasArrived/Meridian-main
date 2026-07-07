namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Supplies the set of symbols currently active in one platform surface (streaming
/// subscriptions, ledger positions, strategy definitions, backfill manifests, ...) so the
/// security-master coverage sweep can find symbols that lack a validated master record.
/// Implementations are registered in DI and unioned by the data quality service.
/// </summary>
public interface ISecurityCoverageSymbolSource
{
    /// <summary>Stable kebab-case name of the surface, e.g. "configured-streaming".</summary>
    string SourceName { get; }

    Task<IReadOnlyCollection<string>> GetActiveSymbolsAsync(CancellationToken ct = default);
}
