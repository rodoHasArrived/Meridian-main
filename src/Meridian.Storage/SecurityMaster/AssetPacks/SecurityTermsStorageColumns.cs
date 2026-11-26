using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Shared column constructors; class-specific economic readers stay with their storage module.</summary>
internal static class SecurityTermsStorageColumns
{
    internal static SecurityTermsProjectionColumn Optional(
        string columnName,
        string termKey,
        SecurityAssetTermFieldType type,
        SecurityTermsProjectionTextNormalization normalization = SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent)
        => SecurityTermsProjectionColumn.Optional(columnName, termKey, type, normalization);

    internal static SecurityTermsProjectionColumn Gate(
        string columnName,
        string termKey,
        SecurityAssetTermFieldType type,
        SecurityTermsProjectionTextNormalization normalization = SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent)
        => SecurityTermsProjectionColumn.Gate(columnName, termKey, type, normalization);

    internal static SecurityTermsProjectionColumn Flag(string columnName, string termKey, bool defaultWhenAbsent)
        => SecurityTermsProjectionColumn.Flag(columnName, termKey, defaultWhenAbsent);

    internal const SecurityTermsProjectionTextNormalization Verbatim = SecurityTermsProjectionTextNormalization.Verbatim;

    /// <summary>A verbatim common-terms string column (exchange, country of risk, issuer).</summary>
    internal static SecurityTermsProjectionColumn CommonTerm(string columnName, string commonTermKey)
        => SecurityTermsProjectionColumn.Derived(columnName, record =>
            PostgresSecurityMasterStore.GetOptionalString(record.CommonTerms, commonTermKey));
}
