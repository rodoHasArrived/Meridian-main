using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.FixedIncome;
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

    public Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, DateOnly asOf, CancellationToken ct = default)
        => GetForwardCurveAsync(indexName, new BondCalculationContext(new DateTimeOffset(asOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)), ct);

    public async Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, BondCalculationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var asOf = DateOnly.FromDateTime(context.AsOf.UtcDateTime);
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return null;
        }

        var definitions = await _store.ListDefinitionsAsync(SecurityCurveKind.Forward, indexName: indexName.Trim(), ct: ct).ConfigureAwait(false);
        if (definitions.Count == 0)
        {
            return null;
        }

        var pins = context.Curves?.Where(pin => string.Equals(pin.Role, "Forward:" + indexName.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (pins.Length > 1)
            return null;
        if (definitions.Count > 1 && pins.Length == 0)
        {
            _logger.LogWarning(
                "Forward curve lookup for index {IndexName} matched {CurveCount} stored curves ({CurveKeys}); the floating leg stays flat forward until the index is qualified.",
                indexName.Trim(),
                definitions.Count,
                string.Join(", ", definitions.Select(static definition => definition.CurveKey)));
            return null;
        }

        var definition = pins.Length == 1
            ? definitions.SingleOrDefault(candidate => candidate.CurveId == pins[0].CurveId)
            : definitions[0];
        if (definition is null)
            return null;
        var version = pins.Length == 1
            ? await _store.GetVersionByIdAsync(definition.CurveId, pins[0].CurveVersionId, ct).ConfigureAwait(false)
            : await _store.GetLatestVersionAsOfAsync(definition.CurveId, asOf, context.KnownAt, ct).ConfigureAwait(false);
        if (version is null || version.AsOf > asOf || (context.KnownAt is { } cutoff && version.RecordedAt > cutoff))
            return null;
        return SecurityCurveRules.ToForwardCurve(version, definition) with
        {
            RetainedInput = new BondCalculationInput("ForwardCurveVersion", version.CurveKey, version.Version,
                version.ContentSha256, version.CurveVersionId,
                new DateTimeOffset(version.AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), version.RecordedAt)
        };
    }
}
