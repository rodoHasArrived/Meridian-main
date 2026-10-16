using Meridian.Strategies.Models;

namespace Meridian.Strategies.Interfaces;

/// <summary>Resolves retained provider trust evidence for the exact source run being promoted.</summary>
public interface IPromotionTrustEvidenceProvider
{
    Task<PromotionTrustEvidence?> GetEvidenceAsync(StrategyRunEntry run, CancellationToken ct = default);
}
