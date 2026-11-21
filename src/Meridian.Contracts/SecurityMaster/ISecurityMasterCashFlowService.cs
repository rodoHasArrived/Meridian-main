using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Manages security cash flow sources and retrieves current or historical scenario projections.
/// </summary>
public interface ISecurityMasterCashFlowService
{
    Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(Guid securityId, CancellationToken ct = default);
    Task UpsertCashFlowSourceAsync(UpsertCashFlowSourceRequest request, CancellationToken ct = default);
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default);

    /// <summary>
    /// The projection as of <paramref name="asOf"/> rather than the wall clock: periods paying on or
    /// after its UTC date are projected, and floating legs read the forward curve in force on that
    /// date. <see cref="GetProjectionAsync"/> is this method at <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsOfAsync(Guid securityId, StructuredCashFlowScenario scenario, DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>Calculates using explicit economic and recorded-information cutoffs.</summary>
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(Guid securityId, StructuredCashFlowScenario scenario, BondCalculationContext context, CancellationToken ct = default)
        => context.KnownAt is null && context.SecurityVersion is null && context.Curves is not { Count: > 0 }
            ? GetProjectionAsOfAsync(securityId, scenario, context.AsOf, ct)
            : Task.FromResult<StructuredCashFlowProjectionDto?>(new(securityId, StructuredCashFlowSourceKind.CalculatedBullet,
                scenario, context.AsOf, [], BlockedReason: "This cash-flow service cannot resolve retained calculation inputs."));

    /// <summary>Uses the supplied selected terms rather than rereading today's Security Master.</summary>
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(SecurityDetailDto security, StructuredCashFlowScenario scenario, BondCalculationContext context, CancellationToken ct = default)
        => GetProjectionAsync(security.SecurityId, scenario, context, ct);

    /// <summary>
    /// Reads the Base calculation's capability refusal on the supplied selected terms and cutoffs.
    /// This passive read retains no calculation receipt and exposes no unretained receipt identity.
    /// An implementation without a read-only capability path must refuse rather than fall back to capture.
    /// </summary>
    Task<SecurityCashFlowBlockerDto?> GetProjectionBlockerAsync(SecurityDetailDto security, BondCalculationContext context, CancellationToken ct = default)
        => throw new NotSupportedException("This cash-flow service does not support read-only capability evaluation.");

    /// <summary>Captures selected inputs for callers to retain with their calculation evidence.</summary>
    Task<SecurityCashFlowCalculationReceipt?> CaptureProjectionAsync(Guid securityId, StructuredCashFlowScenario scenario, BondCalculationContext context, CancellationToken ct = default)
        => Task.FromResult<SecurityCashFlowCalculationReceipt?>(null);

    /// <summary>Recomputes a retained receipt, refusing changed or unavailable calendar evidence.</summary>
    Task<StructuredCashFlowProjectionDto> ReplayProjectionAsync(SecurityCashFlowCalculationReceipt receipt, CancellationToken ct = default)
        => Task.FromResult(new StructuredCashFlowProjectionDto(receipt.Security.SecurityId, receipt.Source.SourceKind,
            receipt.Scenario, receipt.Context.AsOf, [], BlockedReason: "This cash-flow service cannot replay retained inputs."));

    /// <summary>Replays evidence fetched from the configured immutable receipt store.</summary>
    Task<StructuredCashFlowProjectionDto?> ReplayProjectionAsync(string contentSha256, CancellationToken ct = default)
        => Task.FromResult<StructuredCashFlowProjectionDto?>(null);

}
