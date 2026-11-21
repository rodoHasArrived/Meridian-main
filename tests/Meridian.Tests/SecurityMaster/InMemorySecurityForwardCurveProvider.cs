using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Test double for <see cref="ISecurityForwardCurveProvider"/>: the latest curve published for each
/// index (by as-of date) that is not dated after the requested as-of. Index names compare
/// case-insensitively. Production reads stored curves through <c>StoredSecurityForwardCurveProvider</c>.
/// </summary>
public sealed class InMemorySecurityForwardCurveProvider : ISecurityForwardCurveProvider
{
    private readonly ConcurrentDictionary<string, ImmutableCurveSet> _curves = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(SecurityForwardCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentException.ThrowIfNullOrWhiteSpace(curve.IndexName);
        _curves.AddOrUpdate(
            curve.IndexName.Trim(),
            _ => new ImmutableCurveSet([curve]),
            (_, existing) => new ImmutableCurveSet(existing.Curves.Where(c => c.AsOfDate != curve.AsOfDate).Append(curve).ToArray()));
    }

    public Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, DateOnly asOf, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(indexName) || !_curves.TryGetValue(indexName.Trim(), out var set))
        {
            return Task.FromResult<SecurityForwardCurve?>(null);
        }

        return Task.FromResult(set.Curves
            .Where(curve => curve.AsOfDate <= asOf)
            .OrderByDescending(static curve => curve.AsOfDate)
            .FirstOrDefault());
    }

    /// <summary>A <c>Forward:&lt;index&gt;</c> pin selects the published curve whose retained version identity matches; other pins are ignored here.</summary>
    public Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, Meridian.Contracts.FixedIncome.BondCalculationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var pins = context.Curves?.Where(pin => string.Equals(pin.Role, "Forward:" + indexName.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (pins.Length == 0)
            return GetForwardCurveAsync(indexName, DateOnly.FromDateTime(context.AsOf.UtcDateTime), ct);
        if (pins.Length > 1 || string.IsNullOrWhiteSpace(indexName) || !_curves.TryGetValue(indexName.Trim(), out var set))
            return Task.FromResult<SecurityForwardCurve?>(null);
        return Task.FromResult(set.Curves.SingleOrDefault(curve => curve.RetainedInput?.VersionId == pins[0].CurveVersionId));
    }

    private sealed record ImmutableCurveSet(IReadOnlyList<SecurityForwardCurve> Curves);
}
