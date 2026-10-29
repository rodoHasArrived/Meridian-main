namespace Meridian.Contracts.FixedIncome;

/// <summary>
/// Explicit economic and recorded-information cutoffs for bond calculations. Recorded timestamps
/// describe eligibility among retained rows, not historical transaction visibility.
/// </summary>
public sealed record BondCalculationContext(
    DateTimeOffset AsOf,
    DateOnly? SettlementDate = null,
    DateTimeOffset? KnownAt = null,
    long? SecurityVersion = null,
    IReadOnlyList<BondPinnedCurveInput>? Curves = null);

/// <summary>An immutable market-input version selected for a calculation role.</summary>
public sealed record BondPinnedCurveInput(string Role, Guid CurveId, Guid CurveVersionId);

/// <summary>A replayable dependency, including the retained content hash when available.</summary>
public sealed record BondCalculationInput(
    string Kind,
    string Key,
    long? Version = null,
    string? ContentSha256 = null,
    Guid? VersionId = null,
    DateTimeOffset? AsOf = null,
    DateTimeOffset? RecordedAt = null);
