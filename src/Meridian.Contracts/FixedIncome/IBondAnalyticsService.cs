namespace Meridian.Contracts.FixedIncome;

/// <summary>Calculates versioned bond economics without posting journals or submitting orders.</summary>
public interface IBondAnalyticsService
{
    /// <summary>Null means the security is unknown; unsupported economics return typed blockers.</summary>
    Task<BondAnalyticsResult?> CalculateAsync(BondAnalyticsRequest request, CancellationToken ct = default);
}
