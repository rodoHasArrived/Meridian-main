using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityMasterMapping
{
    /// <summary>
    /// The pre-contraction codec evaluated required flat fields in constructor order. That order
    /// is observable in historical replay refusals; module constructors must not choose a new
    /// first failure. All reads still pass through the schema-bound view and no value is repaired.
    /// Optional read-mode fields are tolerant, so only required scalar reads belong here.
    /// </summary>
    private static void EnsureLegacyRequiredReadOrder(
        string assetClass, SchemaBoundTerms bound, SchemaBoundTerms boundTerms)
    {
        switch (assetClass)
        {
            case "Option":
                _ = bound.RequiredGuid("underlyingId");
                _ = bound.RequiredString("putCall");
                _ = bound.RequiredDecimal("strike");
                _ = bound.RequiredDateOnly("expiry");
                _ = bound.RequiredDecimal("multiplier");
                break;
            case "Future":
                _ = bound.RequiredString("rootSymbol");
                _ = bound.RequiredString("contractMonth");
                _ = bound.RequiredDateOnly("expiry");
                _ = bound.RequiredDecimal("multiplier");
                break;
            case "Bond":
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "FxSpot":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                break;
            case "Deposit":
                _ = bound.RequiredString("depositType");
                _ = bound.RequiredString("institutionName");
                break;
            case "CertificateOfDeposit":
                _ = bound.RequiredString("issuerName");
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "CommercialPaper":
                _ = bound.RequiredString("issuerName");
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "TreasuryBill":
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "Repo":
                _ = bound.RequiredString("counterparty");
                _ = bound.RequiredDateOnly("startDate");
                _ = bound.RequiredDateOnly("endDate");
                break;
            case "CashSweep":
                _ = bound.RequiredString("programName");
                _ = bound.RequiredString("sweepVehicleType");
                break;
            case "OtherSecurity":
                _ = bound.RequiredString("category");
                break;
            case "Swap":
                _ = bound.RequiredDateOnly("effectiveDate");
                _ = bound.RequiredDateOnly("maturityDate");
                break;
            case "DirectLoan":
                _ = bound.RequiredString("borrower");
                break;
            case "StructuredCredit":
                _ = boundTerms.RequiredString("tranche");
                _ = boundTerms.RequiredString("collateralType");
                _ = boundTerms.RequiredDecimal("originalFace");
                _ = boundTerms.RequiredString("couponOrIndex");
                break;
            case "PrivateFundInterest":
                _ = boundTerms.RequiredString("gpSponsor");
                _ = boundTerms.RequiredString("strategy");
                _ = boundTerms.RequiredInt("vintage");
                _ = boundTerms.RequiredDecimal("commitment");
                _ = boundTerms.RequiredDateOnly("navDate");
                break;
            case "PrivateCompanyEquity":
                _ = boundTerms.RequiredString("issuer");
                _ = boundTerms.RequiredString("shareClass");
                _ = boundTerms.RequiredString("round");
                _ = boundTerms.RequiredDecimal("costBasis");
                break;
            case "RealEstateHolding":
                _ = boundTerms.RequiredString("propertyType");
                _ = boundTerms.RequiredString("addressOrMarket");
                _ = boundTerms.RequiredDecimal("ownershipPercent");
                _ = boundTerms.RequiredDecimal("appraisalValue");
                _ = boundTerms.RequiredDateOnly("valuationDate");
                break;
            case "CommitmentGuarantee":
                _ = boundTerms.RequiredString("counterparty");
                _ = boundTerms.RequiredDecimal("committedAmount");
                _ = boundTerms.RequiredDateOnly("effectiveDate");
                break;
            case "Commodity":
                _ = bound.RequiredString("commodityType");
                break;
            case "CryptoCurrency":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                break;
            case "Cfd":
                _ = bound.RequiredString("underlyingAssetClass");
                break;
            case "Warrant":
                _ = bound.RequiredGuid("underlyingId");
                _ = bound.RequiredString("warrantType");
                break;
            case "FxForward":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                _ = bound.RequiredDateOnly("valueDate");
                break;
            case "NonDeliverableForward":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                _ = bound.RequiredString("settlementCurrency");
                _ = bound.RequiredDateOnly("fixingDate");
                _ = bound.RequiredDateOnly("valueDate");
                break;
            case "CreditDefaultSwap":
                _ = bound.RequiredString("referenceEntity");
                _ = bound.RequiredString("protectionSide");
                _ = bound.RequiredDecimal("notional");
                _ = bound.RequiredDecimal("fixedCouponBps");
                _ = bound.RequiredDateOnly("effectiveDate");
                _ = bound.RequiredDateOnly("maturityDate");
                break;
            case "Swaption":
                _ = bound.RequiredString("payerReceiver");
                _ = bound.RequiredDecimal("strikeRate");
                _ = bound.RequiredDateOnly("expiry");
                _ = bound.RequiredDateOnly("underlyingMaturityDate");
                _ = bound.RequiredDecimal("notional");
                break;
            case "CapFloor":
                _ = bound.RequiredString("capFloorType");
                _ = bound.RequiredString("referenceIndex");
                _ = bound.RequiredDecimal("notional");
                _ = bound.RequiredDateOnly("effectiveDate");
                _ = bound.RequiredDateOnly("maturityDate");
                break;
            case "StructuredNote":
                _ = bound.RequiredString("issuerName");
                _ = bound.RequiredString("underlyingDescription");
                _ = bound.RequiredDateOnly("maturity");
                break;
        }
    }

    // Unrecognized classes have no schema-bound view. Keep their four tolerant compatibility
    // reads at the call site in their original order; this helper only nests their stored values.
    private static SecurityKind ToUnrecognizedClassKind(
        string assetClass, string? subType, DateOnly? maturity, string? issuerName, string? settlementType)
        => SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
            new MaturityTerms(null, null, ToOption(maturity)),
            new ClassificationDetailTerms(assetClass, ToOption(subType)),
            new IssuerRef(ToOption(issuerName), null, ToOption(settlementType), null, null, null, null)));

}
