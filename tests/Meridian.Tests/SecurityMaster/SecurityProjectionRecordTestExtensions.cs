using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Test fixtures that write a projection straight to <c>PostgresSecurityMasterStore</c> bypass the
/// service's declared-family choke point. Since migration 068 the <c>securities</c> table refuses an
/// untagged <c>asset_specific_terms</c> document (<c>ck_securities_terms_format_declared</c>), so such
/// a fixture declares the family first, exactly as every production writer does.
/// </summary>
internal static class SecurityProjectionRecordTestExtensions
{
    public static SecurityProjectionRecord WithDeclaredTermsFormat(this SecurityProjectionRecord record)
        => record with { AssetSpecificTerms = SecurityTermsEnvelope.StampDeclaredFormat(record.AssetSpecificTerms) };
}
