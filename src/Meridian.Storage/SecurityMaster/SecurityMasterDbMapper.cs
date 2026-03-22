using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

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
            record.Identifiers,
            record.Aliases,
            record.Version,
            record.EffectiveFrom,
            record.EffectiveTo);
}
