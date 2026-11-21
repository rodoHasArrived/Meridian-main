using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Whether a <c>CustomAsset</c> record pinned to a reclassifying profile can be promoted to that
/// profile's first-class asset class by a write that carries its envelope (ADR-022).
/// <see cref="Blocked"/> records list what the promoting envelope has to correct; for a record pinned
/// to a reclassifying profile, repair and promotion are the same write.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityMasterProfilePromotionDisposition>))]
public enum SecurityMasterProfilePromotionDisposition
{
    Ready,
    Blocked
}

/// <summary>One reason a promotion would be refused if it were applied now.</summary>
public sealed record SecurityMasterProfilePromotionBlockerDto(
    string Code,
    string Message);

/// <summary>
/// One active <c>CustomAsset</c> record pinned to the reported profile. A
/// <see cref="SecurityMasterProfilePromotionDisposition.Ready"/> record decodes under
/// <paramref name="TargetAssetClass"/>, satisfies that class's domain invariants and passes the
/// profile catalog check, so an amendment that resubmits its pinned envelope promotes it.
/// </summary>
public sealed record SecurityMasterProfilePromotionCandidateDto(
    Guid SecurityId,
    string DisplayName,
    string PrimaryIdentifierKind,
    string PrimaryIdentifierValue,
    long Version,
    string SourceAssetClass,
    string CustomProfileId,
    int? ProfileVersion,
    string TargetAssetClass,
    SecurityMasterProfilePromotionDisposition Disposition,
    IReadOnlyList<SecurityMasterProfilePromotionBlockerDto> Blockers);

/// <summary>
/// Report-first inventory of the active <c>CustomAsset</c> records pinned to one reclassifying
/// profile (<see cref="SecurityAssetClassDescriptor.ReclassifyingProfileIds"/>). Building the report
/// changes nothing. <paramref name="AlreadyPromotedCount"/> counts the active records pinned to the
/// profile that already carry <paramref name="TargetAssetClass"/>.
/// </summary>
public sealed record SecurityMasterProfilePromotionReportDto(
    DateTimeOffset GeneratedAt,
    string ProfileId,
    string TargetAssetClass,
    int SecuritiesScanned,
    int AlreadyPromotedCount,
    int CandidateCount,
    int ReadyCount,
    int BlockedCount,
    IReadOnlyList<SecurityMasterProfilePromotionCandidateDto> Candidates);

/// <summary>
/// Builds the read-only promotion report for <c>CustomAsset</c> records pinned to a reclassifying
/// profile.
/// </summary>
public interface ISecurityMasterProfilePromotionService
{
    /// <summary>
    /// Scans the active Security Master population for <c>CustomAsset</c> records pinned to
    /// <paramref name="profileId"/> and dry-runs each one under the profile's target class.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="profileId"/> is not a reclassifying profile.</exception>
    Task<SecurityMasterProfilePromotionReportDto> BuildReportAsync(string profileId, CancellationToken ct = default);
}
