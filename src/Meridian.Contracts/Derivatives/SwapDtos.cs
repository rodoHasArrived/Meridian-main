namespace Meridian.Contracts.Derivatives;

public sealed record SwapReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? SwapType,
    DateOnly EffectiveDate,
    DateOnly MaturityDate,
    string LifecycleState,
    IReadOnlyList<SwapLegDto> Legs,
    string PrimaryIdentifier,
    long Version);

public sealed record SwapLegDto(
    string LegType,
    string Currency,
    string? Index,
    decimal? FixedRate);
