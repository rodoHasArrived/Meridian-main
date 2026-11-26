using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CertificateOfDeposit asset module.</summary>
internal static class CertificateOfDepositAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CertificateOfDeposit", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "CertificateOfDeposit",
            [
                FieldRule.OptionalNonNegative("couponRate", "SM_CD_COUPON_RATE_INVALID", "Certificate of deposit coupon is invalid", "Certificate of deposit coupon rates must be zero or greater."),
                DateOrderRule.Optional("callableDate", "maturity", "SM_CD_CALLABLE_DATE_INVALID", "Certificate of deposit callable date is invalid", "Callable date must be on or before maturity.")
            ]);
}
