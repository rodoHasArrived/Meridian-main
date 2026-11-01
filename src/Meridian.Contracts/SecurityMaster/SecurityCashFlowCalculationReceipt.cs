using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// A portable calculation receipt. Callers retain this record alongside their governed evidence;
/// its digest detects accidental changes and is not an authentication or authorization signature.
/// Replay never reloads current security terms, source assignments or market curves.
/// </summary>
public sealed record SecurityCashFlowCalculationReceipt(
    string ModelVersion,
    SecurityDetailDto Security,
    SecurityCashFlowSourceDto Source,
    StructuredCashFlowScenario Scenario,
    BondCalculationContext Context,
    IReadOnlyList<SecurityForwardCurve> ForwardCurves,
    StructuredCashFlowProjectionDto Projection,
    string ContentSha256);

/// <summary>Immutable content-addressed retained calculation evidence.</summary>
public interface ISecurityCashFlowReceiptStore
{
    Task RetainAsync(SecurityCashFlowCalculationReceipt receipt, CancellationToken ct = default);
    Task<SecurityCashFlowCalculationReceipt?> GetAsync(string contentSha256, CancellationToken ct = default);
}
