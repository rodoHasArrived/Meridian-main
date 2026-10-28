using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Maps persisted Security Master projections to public security summary and detail contracts.
/// </summary>
public static class SecurityMasterDbMapper
{
    public static SecuritySummaryDto ToSummary(SecurityProjectionRecord record)
        => new(
            record.SecurityId,
            record.AssetClass,
            record.Status,
            record.DisplayName,
            $"{record.PrimaryIdentifierKind}:{record.PrimaryIdentifierValue}",
            record.Currency,
            record.Version);

    public static SecurityDetailDto ToDetail(SecurityProjectionRecord record)
        => new(
            record.SecurityId,
            record.AssetClass,
            record.Status,
            record.DisplayName,
            record.Currency,
            record.CommonTerms,
            record.AssetSpecificTerms,
            NormalizeIdentifiers(record.Identifiers),
            record.Aliases,
            record.Version,
            record.EffectiveFrom,
            record.EffectiveTo);

    private static IReadOnlyList<SecurityIdentifierDto> NormalizeIdentifiers(IReadOnlyList<SecurityIdentifierDto> identifiers)
        => identifiers
            .Select(static identifier => identifier with
            {
                NormalizedValue = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier),
                NormalizedProvider = SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier)
            })
            .ToArray();
}
