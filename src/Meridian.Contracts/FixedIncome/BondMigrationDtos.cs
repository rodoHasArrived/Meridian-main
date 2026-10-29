using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Explicit reviewed assignment; no coupon-unit or calendar convention is inferred.</summary>
public sealed record BondMigrationPreviewRequest(
    Guid SecurityId,
    long ExpectedVersion,
    BondCalculationContext Context,
    JsonElement ScheduleConventions);

/// <summary>One economic payment before and after migration, compared by actual payment date.</summary>
public sealed record BondMigrationCashDifference(
    DateOnly PaymentDate,
    decimal PreviousPrincipal,
    decimal CandidatePrincipal,
    decimal PreviousInterest,
    decimal CandidateInterest);

/// <summary>Retained-input-bound preview, never permission to change a security or a posted journal.</summary>
public sealed record BondMigrationPreviewDto(
    BondMigrationPreviewRequest Request,
    string SecurityContentSha256,
    string PreviewSha256,
    StructuredCashFlowProjectionDto? PreviousProjection,
    StructuredCashFlowProjectionDto? CandidateProjection,
    IReadOnlyList<BondMigrationCashDifference> CashDifferences,
    IReadOnlyList<string> Gaps,
    bool CanStage);

/// <summary>An explicitly reviewed operational block bound to exactly one retained terms version.</summary>
public sealed record BondMigrationBlockDto(Guid SecurityId, long Version, string SecurityContentSha256, string Reason);

public sealed record BondMigrationInventoryRowDto(
    Guid SecurityId, long Version, string SecurityContentSha256, string DisplayName,
    bool HasDeclaredConventions, string? BlockedReason);

/// <summary>Full active Security Master inventory read; not a transaction-visibility certificate.</summary>
public sealed record BondMigrationInventoryDto(
    string InventorySha256,
    IReadOnlyList<BondMigrationInventoryRowDto> Bonds,
    bool CutoverEnabled,
    bool RequiresLegacyCalculations,
    string? CurrentReceiptSha256 = null);

public sealed record BondMigrationCutoverRequest(
    string ExpectedInventorySha256,
    string Reviewer,
    IReadOnlyList<BondMigrationBlockDto> ExplicitBlocks,
    string? ExpectedPreviousReceiptSha256 = null);

/// <summary>Immutable reviewed cutover snapshot. Subsequent undeclared terms are blocked live.</summary>
public sealed record BondMigrationCutoverReceiptDto(
    string InventorySha256,
    string Reviewer,
    DateTimeOffset RecordedAt,
    IReadOnlyList<BondMigrationBlockDto> ExplicitBlocks,
    string ModelVersion,
    string? PreviousReceiptSha256 = null);

public interface IBondMigrationService
{
    Task<BondMigrationPreviewDto> PreviewAsync(BondMigrationPreviewRequest request, CancellationToken ct = default);
    Task<UpdateSecurityFieldRequest> PrepareEditAsync(BondMigrationPreviewDto preview, string actor, string justification, CancellationToken ct = default);
    Task<BondMigrationInventoryDto> GetInventoryAsync(CancellationToken ct = default);
    Task<BondMigrationCutoverReceiptDto> CutoverAsync(BondMigrationCutoverRequest request, CancellationToken ct = default);
}

/// <summary>Live gate only: historical receipt replay must keep using its retained calculation version.</summary>
public interface IBondCalculationPolicy
{
    Task<string?> GetBlockerAsync(SecurityDetailDto security, CancellationToken ct = default);
}
