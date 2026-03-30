using System.Text.Json.Serialization;

namespace Meridian.Contracts.Treasury;

/// <summary>
/// Operational liquidity state of a money market fund.
/// Liquid funds are within standard WAM limits and fully operational.
/// Restricted funds have elevated WAM or suspended redemptions.
/// Suspended funds are under regulatory or operational hold.
/// Inactive funds have been deactivated in the security master.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MmfLiquidityState>))]
public enum MmfLiquidityState
{
    Liquid,
    Restricted,
    Suspended,
    Inactive
}

/// <summary>
/// Full reference record for a money market fund, including canonical identity,
/// sweep eligibility, weighted-average maturity, and liquidity-fee metadata.
/// </summary>
public sealed record MmfDetailDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? FundFamily,
    bool IsSweepEligible,
    int? WeightedAverageMaturityDays,
    bool HasLiquidityFee,
    string Status,
    long Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>
/// Liquidity projection for a money market fund derived from WAM and operational state.
/// </summary>
public sealed record MmfLiquidityDto(
    Guid SecurityId,
    MmfLiquidityState State,
    int? WeightedAverageMaturityDays,
    DateTimeOffset AsOf);

/// <summary>
/// Sweep-eligibility and fee profile for a money market fund.
/// Used by cash-management and treasury consumers to determine routing eligibility.
/// </summary>
public sealed record MmfSweepProfileDto(
    Guid SecurityId,
    bool IsSweepEligible,
    bool HasLiquidityFee,
    string? FundFamily,
    DateTimeOffset AsOf);

/// <summary>
/// Fund-family grouping projection.
/// Normalised family name is upper-case; members are the canonical security IDs
/// of all active MMFs sharing that family lineage.
/// </summary>
public sealed record MmfFundFamilyDto(
    string NormalizedFamilyName,
    IReadOnlyList<Guid> MemberSecurityIds,
    int MemberCount,
    DateTimeOffset AsOf);

/// <summary>
/// Filter criteria for MMF search queries.
/// All nullable criteria are treated as "any" when not specified.
/// </summary>
public sealed record MmfSearchQuery(
    string? FundFamily = null,
    bool? IsSweepEligible = null,
    bool? HasLiquidityFee = null,
    MmfLiquidityState? LiquidityState = null,
    int? MaxWamDays = null,
    bool ActiveOnly = true,
    int Skip = 0,
    int Take = 50);

/// <summary>
/// Tracks the last successful projection rebuild for a money market fund.
/// Used by governance and operations to verify rebuild freshness.
/// </summary>
public sealed record MmfRebuildCheckpointDto(
    Guid SecurityId,
    long AggregateVersion,
    DateTimeOffset CheckpointedAt,
    string RebuildSource);
