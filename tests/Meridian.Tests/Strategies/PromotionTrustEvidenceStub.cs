using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;

namespace Meridian.Tests.Strategies;

internal sealed class PromotionTrustEvidenceStub(
    Func<StrategyRunEntry, PromotionTrustEvidence?>? resolve = null) : IPromotionTrustEvidenceProvider
{
    public Task<PromotionTrustEvidence?> GetEvidenceAsync(StrategyRunEntry run, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(resolve is null ? Passing(run) : resolve(run));
    }

    public static PromotionTrustEvidence Passing(StrategyRunEntry run) => new(
        run.RunId,
        run.InputHashSha256!,
        "provider-trust/retained-snapshot",
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddHours(1),
        IsComplete: true);
}
