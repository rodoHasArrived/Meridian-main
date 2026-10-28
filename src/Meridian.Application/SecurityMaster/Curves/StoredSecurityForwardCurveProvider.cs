using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Curves;

/// <summary>
/// Supplies forward curves to the cash-flow projection from stored curves: the one Forward
/// definition whose index matches (trimmed, ignoring case), at its latest version dated on or before
/// the as-of. No definition, no version by that date, or several definitions for the index (the same
/// index in two currencies or tenors) all yield null, so the leg stays flat forward, as a host
/// without curves does today; ambiguity is logged and never guessed.
/// </summary>
public sealed class StoredSecurityForwardCurveProvider : ISecurityForwardCurveProvider
{
    private readonly ISecurityCurveStore _store;
    private readonly ILogger<StoredSecurityForwardCurveProvider> _logger;

    public StoredSecurityForwardCurveProvider(ISecurityCurveStore store, ILogger<StoredSecurityForwardCurveProvider> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, DateOnly asOf, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return null;
        }

        var definitions = await _store.ListDefinitionsAsync(SecurityCurveKind.Forward, indexName: indexName.Trim(), ct: ct).ConfigureAwait(false);
        if (definitions.Count == 0)
        {
            return null;
        }

        if (definitions.Count > 1)
        {
            _logger.LogWarning(
                "Forward curve lookup for index {IndexName} matched {CurveCount} stored curves ({CurveKeys}); the floating leg stays flat forward until the index is qualified.",
                indexName.Trim(),
                definitions.Count,
                string.Join(", ", definitions.Select(static definition => definition.CurveKey)));
            return null;
        }

        var definition = definitions[0];
        var version = await _store.GetLatestVersionAsOfAsync(definition.CurveId, asOf, knownAt: null, ct).ConfigureAwait(false);
        return version is null ? null : SecurityCurveRules.ToForwardCurve(version, definition);
    }
}
