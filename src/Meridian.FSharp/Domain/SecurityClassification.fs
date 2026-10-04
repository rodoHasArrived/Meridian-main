namespace Meridian.FSharp.Domain

[<RequireQualifiedAccess>]
type AssetClass =
    | Equity
    | FixedIncome
    | Fund
    | CashEquivalent
    | Financing
    | Derivative
    | PrivateCredit
    | Other

[<RequireQualifiedAccess>]
type AssetFamily =
    | Sovereign
    | CorporateDebt
    | BankProduct
    | CommonEquity
    | PreferredEquity
    | PartnershipEquity
    | MoneyMarket
    | RepurchaseAgreement
    /// Structured CASH vehicles — sweep programs and the like. Distinct from
    /// <see cref="SecuritizedCredit"/>: this family is a cash-equivalent, and conflating the two
    /// put cash sweeps into the securitized fixed-income accounting slice.
    | StructuredCash
    /// Securitized credit tranches — MBS, ABS, CLO, CMBS, CDO, IO/PO strips. The canonical
    /// family for the `StructuredCredit` asset class (ADR-022).
    | SecuritizedCredit
    | ListedDerivative
    | PrivateLoan
    | OtherFamily of string

[<RequireQualifiedAccess>]
type SecuritySubType =
    | TreasuryBill
    | TreasuryNote
    | TreasuryBond
    | Tips
    | TreasuryFrn
    | CommercialPaper
    | CertificateOfDeposit
    | TimeDeposit
    | DemandDeposit
    | MoneyMarketFund
    | Repo
    | CashSweep
    | CorporateBond
    | MunicipalBond
    | CommonShare
    | PreferredShare
    | Adr
    | MlpUnit
    | LimitedPartnershipInterest
    | ReitShare
    | OptionContract
    | FutureContract
    | SwapContract
    | DirectLoan
    | StructuredCredit
    | PrivateCompanyEquity
    | RealEstateHolding
    | CommitmentGuarantee
    | OtherSubType of string

/// Structured sector / industry classification supporting multiple taxonomy schemes.
type SectorTaxonomy = {
    /// Taxonomy scheme identifier (e.g. "GICS", "ICB", "SIC", "NAICS").
    TaxonomyScheme: string
    SectorCode: string option
    IndustryGroupCode: string option
    IndustryCode: string option
    SubIndustryCode: string option
}

type SecurityClassification = {
    AssetClass: AssetClass
    Family: AssetFamily option
    SubType: SecuritySubType
    TypeName: string
    IssuerType: string option
    RiskCountry: string option
    /// Structured sector / industry taxonomy (GICS, ICB, SIC, NAICS, etc.).
    Taxonomy: SectorTaxonomy option
}

[<RequireQualifiedAccess>]
module SecurityClassification =
    let assetClassName assetClass =
        match assetClass with
        | AssetClass.Equity -> "Equity"
        | AssetClass.FixedIncome -> "FixedIncome"
        | AssetClass.Fund -> "Fund"
        | AssetClass.CashEquivalent -> "CashEquivalent"
        | AssetClass.Financing -> "Financing"
        | AssetClass.Derivative -> "Derivative"
        | AssetClass.PrivateCredit -> "PrivateCredit"
        | AssetClass.Other -> "Other"

    let familyName family =
        match family with
        | AssetFamily.Sovereign -> "Sovereign"
        | AssetFamily.CorporateDebt -> "CorporateDebt"
        | AssetFamily.BankProduct -> "BankProduct"
        | AssetFamily.CommonEquity -> "CommonEquity"
        | AssetFamily.PreferredEquity -> "PreferredEquity"
        | AssetFamily.PartnershipEquity -> "PartnershipEquity"
        | AssetFamily.MoneyMarket -> "MoneyMarket"
        | AssetFamily.RepurchaseAgreement -> "RepurchaseAgreement"
        | AssetFamily.StructuredCash -> "StructuredCash"
        | AssetFamily.SecuritizedCredit -> "SecuritizedCredit"
        | AssetFamily.ListedDerivative -> "ListedDerivative"
        | AssetFamily.PrivateLoan -> "PrivateLoan"
        | AssetFamily.OtherFamily value -> value

    let subTypeName subType =
        match subType with
        | SecuritySubType.TreasuryBill -> "TreasuryBill"
        | SecuritySubType.TreasuryNote -> "TreasuryNote"
        | SecuritySubType.TreasuryBond -> "TreasuryBond"
        | SecuritySubType.Tips -> "Tips"
        | SecuritySubType.TreasuryFrn -> "TreasuryFrn"
        | SecuritySubType.CommercialPaper -> "CommercialPaper"
        | SecuritySubType.CertificateOfDeposit -> "CertificateOfDeposit"
        | SecuritySubType.TimeDeposit -> "TimeDeposit"
        | SecuritySubType.DemandDeposit -> "DemandDeposit"
        | SecuritySubType.MoneyMarketFund -> "MoneyMarketFund"
        | SecuritySubType.Repo -> "Repo"
        | SecuritySubType.CashSweep -> "CashSweep"
        | SecuritySubType.CorporateBond -> "CorporateBond"
        | SecuritySubType.MunicipalBond -> "MunicipalBond"
        | SecuritySubType.CommonShare -> "CommonShare"
        | SecuritySubType.PreferredShare -> "PreferredShare"
        | SecuritySubType.Adr -> "Adr"
        | SecuritySubType.MlpUnit -> "MlpUnit"
        | SecuritySubType.LimitedPartnershipInterest -> "LimitedPartnershipInterest"
        | SecuritySubType.ReitShare -> "ReitShare"
        | SecuritySubType.OptionContract -> "OptionContract"
        | SecuritySubType.FutureContract -> "FutureContract"
        | SecuritySubType.SwapContract -> "SwapContract"
        | SecuritySubType.DirectLoan -> "DirectLoan"
        | SecuritySubType.StructuredCredit -> "StructuredCredit"
        | SecuritySubType.PrivateCompanyEquity -> "PrivateCompanyEquity"
        | SecuritySubType.RealEstateHolding -> "RealEstateHolding"
        | SecuritySubType.CommitmentGuarantee -> "CommitmentGuarantee"
        | SecuritySubType.OtherSubType value -> value
