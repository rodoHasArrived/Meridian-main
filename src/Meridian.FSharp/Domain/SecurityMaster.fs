namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
type SecurityStatus =
    | Active
    | Inactive

[<RequireQualifiedAccess>]
module SecurityStatus =
    let isActive status = status = SecurityStatus.Active
    let isInactive status = status = SecurityStatus.Inactive

    let asString status =
        match status with
        | SecurityStatus.Active -> "Active"
        | SecurityStatus.Inactive -> "Inactive"

type CommonTerms = {
    DisplayName: string
    Currency: string
    CountryOfRisk: string option
    IssuerName: string option
    Exchange: string option
    LotSize: decimal option
    TickSize: decimal option
}

[<RequireQualifiedAccess>]
module CommonTerms =
    let normalizedDisplayName (terms: CommonTerms) =
        terms.DisplayName.Trim()

    let normalizedCurrency (terms: CommonTerms) =
        terms.Currency.Trim().ToUpperInvariant()

    let withNormalizedCoreFields (terms: CommonTerms) =
        {
            terms with
                DisplayName = normalizedDisplayName terms
                Currency = normalizedCurrency terms
        }

type EquityTerms = { ShareClass: string option }

type OptionTerms = {
    UnderlyingId: SecurityId
    PutCall: string
    Strike: decimal
    Expiry: DateOnly
    Multiplier: decimal
}

type FutureTerms = {
    RootSymbol: string
    ContractMonth: string
    Expiry: DateOnly
    Multiplier: decimal
}

[<RequireQualifiedAccess>]
type BondCouponStructure =
    | Fixed of rate: decimal * dayCount: string option
    | Floating of index: string * spreadBps: decimal option * capRate: decimal option * floorRate: decimal option * dayCount: string option
    | ZeroCoupon

type BondTerms = {
    Maturity: DateOnly
    IssueDate: DateOnly option
    Coupon: BondCouponStructure
    IsCallable: bool
    CallDate: DateOnly option
    IssuerName: string option
    Seniority: string option
}

[<RequireQualifiedAccess>]
module BondTerms =
    let fixedRate maturity couponRate dayCount issuerName =
        { Maturity = maturity; IssueDate = None; Coupon = BondCouponStructure.Fixed(couponRate, dayCount); IsCallable = false; CallDate = None; IssuerName = issuerName; Seniority = None }

    let floatingRate maturity index spreadBps issuerName =
        { Maturity = maturity; IssueDate = None; Coupon = BondCouponStructure.Floating(index, spreadBps, None, None, None); IsCallable = false; CallDate = None; IssuerName = issuerName; Seniority = None }

    let zeroCoupon maturity issuerName =
        { Maturity = maturity; IssueDate = None; Coupon = BondCouponStructure.ZeroCoupon; IsCallable = false; CallDate = None; IssuerName = issuerName; Seniority = None }

    let couponRate (terms: BondTerms) =
        match terms.Coupon with
        | BondCouponStructure.Fixed(rate, _) -> Some rate
        | BondCouponStructure.Floating _ -> None
        | BondCouponStructure.ZeroCoupon -> None

    let dayCount (terms: BondTerms) =
        match terms.Coupon with
        | BondCouponStructure.Fixed(_, dc) -> dc
        | BondCouponStructure.Floating(_, _, _, _, dc) -> dc
        | BondCouponStructure.ZeroCoupon -> None

type FxSpotTerms = {
    BaseCurrency: string
    QuoteCurrency: string
}

type DepositTerms = {
    DepositType: string
    InstitutionName: string
    Maturity: DateOnly option
    InterestRate: decimal option
    DayCount: string option
    IsCallable: bool
}

type MoneyMarketFundTerms = {
    FundFamily: string option
    SweepEligible: bool
    WeightedAverageMaturityDays: int option
    LiquidityFeeEligible: bool
}

type CertificateOfDepositTerms = {
    IssuerName: string
    Maturity: DateOnly
    CouponRate: decimal option
    CallableDate: DateOnly option
    DayCount: string option
}

type CommercialPaperTerms = {
    IssuerName: string
    Maturity: DateOnly
    DiscountRate: decimal option
    DayCount: string option
    IsAssetBacked: bool
}

type TreasuryBillTerms = {
    Maturity: DateOnly
    AuctionDate: DateOnly option
    CUSIP: string option
    DiscountRate: decimal option
}

type RepoTerms = {
    Counterparty: string
    StartDate: DateOnly
    EndDate: DateOnly
    RepoRate: decimal option
    CollateralType: string option
    Haircut: decimal option
}

type CashSweepTerms = {
    ProgramName: string
    SweepVehicleType: string
    SweepFrequency: string option
    TargetAccountType: string option
    YieldRate: decimal option
}

type OtherSecurityTerms = {
    Category: string
    SubType: string option
    Maturity: DateOnly option
    IssuerName: string option
    SettlementType: string option
}

type SwapLeg = {
    LegType: string
    Currency: string
    Index: string option
    FixedRate: decimal option
}

type SwapTerms = {
    EffectiveDate: DateOnly
    MaturityDate: DateOnly
    Legs: SwapLeg list
}

type Covenant = {
    CovenantType: string
    Threshold: string
    Notes: string option
}

type DirectLoanTerms = {
    Borrower: string
    Maturity: DateOnly option
    Covenants: Covenant list
}

type CommodityTerms = {
    CommodityType: string
    Denomination: string option
    ContractSize: decimal option
}

type CryptoTerms = {
    BaseCurrency: string
    QuoteCurrency: string
    Network: string option
}

type CfdTerms = {
    UnderlyingAssetClass: string
    UnderlyingDescription: string option
    Leverage: decimal option
}

type WarrantTerms = {
    UnderlyingId: SecurityId
    WarrantType: string
    Strike: decimal option
    Expiry: DateOnly option
    Multiplier: decimal option
}

[<RequireQualifiedAccess>]
type SecurityKind =
    | Equity of EquityTerms
    | Option of OptionTerms
    | Future of FutureTerms
    | Bond of BondTerms
    | FxSpot of FxSpotTerms
    | Deposit of DepositTerms
    | MoneyMarketFund of MoneyMarketFundTerms
    | CertificateOfDeposit of CertificateOfDepositTerms
    | CommercialPaper of CommercialPaperTerms
    | TreasuryBill of TreasuryBillTerms
    | Repo of RepoTerms
    | CashSweep of CashSweepTerms
    | OtherSecurity of OtherSecurityTerms
    | Swap of SwapTerms
    | DirectLoan of DirectLoanTerms
    | Commodity of CommodityTerms
    | CryptoCurrency of CryptoTerms
    | Cfd of CfdTerms
    | Warrant of WarrantTerms

type Provenance = {
    SourceSystem: string
    SourceRecordId: string option
    AsOf: DateTimeOffset
    UpdatedBy: string
    Reason: string option
}

[<RequireQualifiedAccess>]
module Provenance =
    let withUpdatedAsOf asOf (provenance: Provenance) =
        { provenance with AsOf = asOf }

    let normalize (provenance: Provenance) =
        {
            provenance with
                SourceSystem = provenance.SourceSystem.Trim()
                UpdatedBy = provenance.UpdatedBy.Trim()
        }

type SecurityMasterRecord = {
    SecurityId: SecurityId
    Status: SecurityStatus
    Common: CommonTerms
    Identifiers: Identifier list
    Kind: SecurityKind
    Version: int64
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    Provenance: Provenance
}

[<RequireQualifiedAccess>]
module SecurityMasterRecord =
    let primaryIdentifier (record: SecurityMasterRecord) =
        record.Identifiers
        |> List.tryFind (fun identifier -> identifier.IsPrimary)

    let assetClass (record: SecurityMasterRecord) =
        match record.Kind with
        | SecurityKind.Equity _ -> "Equity"
        | SecurityKind.Option _ -> "Option"
        | SecurityKind.Future _ -> "Future"
        | SecurityKind.Bond _ -> "Bond"
        | SecurityKind.FxSpot _ -> "FxSpot"
        | SecurityKind.Deposit _ -> "Deposit"
        | SecurityKind.MoneyMarketFund _ -> "MoneyMarketFund"
        | SecurityKind.CertificateOfDeposit _ -> "CertificateOfDeposit"
        | SecurityKind.CommercialPaper _ -> "CommercialPaper"
        | SecurityKind.TreasuryBill _ -> "TreasuryBill"
        | SecurityKind.Repo _ -> "Repo"
        | SecurityKind.CashSweep _ -> "CashSweep"
        | SecurityKind.OtherSecurity _ -> "OtherSecurity"
        | SecurityKind.Swap _ -> "Swap"
        | SecurityKind.DirectLoan _ -> "DirectLoan"
        | SecurityKind.Commodity _ -> "Commodity"
        | SecurityKind.CryptoCurrency _ -> "CryptoCurrency"
        | SecurityKind.Cfd _ -> "Cfd"
        | SecurityKind.Warrant _ -> "Warrant"

    let isActive (record: SecurityMasterRecord) =
        SecurityStatus.isActive record.Status

    let isInactive (record: SecurityMasterRecord) =
        SecurityStatus.isInactive record.Status

    let containsIdentifier (identifier: Identifier) (record: SecurityMasterRecord) =
        record.Identifiers
        |> List.exists (SecurityIdentifier.sameIdentity identifier)

    let activeIdentifiersAt asOf (record: SecurityMasterRecord) =
        record.Identifiers
        |> List.filter (SecurityIdentifier.isActiveAt asOf)

    let withIdentifiers identifiers (record: SecurityMasterRecord) =
        { record with Identifiers = identifiers }

    let withCommon common (record: SecurityMasterRecord) =
        { record with Common = common }

    let withKind kind (record: SecurityMasterRecord) =
        { record with Kind = kind }

    let withVersion version (record: SecurityMasterRecord) =
        { record with Version = version }

    let withProvenance provenance (record: SecurityMasterRecord) =
        { record with Provenance = provenance }

    let deactivate effectiveTo provenance (record: SecurityMasterRecord) =
        {
            record with
                Status = SecurityStatus.Inactive
                EffectiveTo = Some effectiveTo
                Provenance = provenance
        }

    let normalize (record: SecurityMasterRecord) =
        {
            record with
                Common = record.Common |> CommonTerms.withNormalizedCoreFields
                Provenance = record.Provenance |> Provenance.normalize
        }

[<RequireQualifiedAccess>]
module SecurityKind =
    let assetClass kind =
        match kind with
        | SecurityKind.Equity _ -> "Equity"
        | SecurityKind.Option _ -> "Option"
        | SecurityKind.Future _ -> "Future"
        | SecurityKind.Bond _ -> "Bond"
        | SecurityKind.FxSpot _ -> "FxSpot"
        | SecurityKind.Deposit _ -> "Deposit"
        | SecurityKind.MoneyMarketFund _ -> "MoneyMarketFund"
        | SecurityKind.CertificateOfDeposit _ -> "CertificateOfDeposit"
        | SecurityKind.CommercialPaper _ -> "CommercialPaper"
        | SecurityKind.TreasuryBill _ -> "TreasuryBill"
        | SecurityKind.Repo _ -> "Repo"
        | SecurityKind.CashSweep _ -> "CashSweep"
        | SecurityKind.OtherSecurity _ -> "OtherSecurity"
        | SecurityKind.Swap _ -> "Swap"
        | SecurityKind.DirectLoan _ -> "DirectLoan"
        | SecurityKind.Commodity _ -> "Commodity"
        | SecurityKind.CryptoCurrency _ -> "CryptoCurrency"
        | SecurityKind.Cfd _ -> "Cfd"
        | SecurityKind.Warrant _ -> "Warrant"

    let underlyingSecurityId kind =
        match kind with
        | SecurityKind.Option terms -> Some terms.UnderlyingId
        | SecurityKind.Warrant terms -> Some terms.UnderlyingId
        | _ -> None

    let isDerivative kind =
        match kind with
        | SecurityKind.Option _
        | SecurityKind.Future _
        | SecurityKind.Swap _
        | SecurityKind.Cfd _
        | SecurityKind.Warrant _ -> true
        | SecurityKind.Equity _
        | SecurityKind.Bond _
        | SecurityKind.FxSpot _
        | SecurityKind.Deposit _
        | SecurityKind.MoneyMarketFund _
        | SecurityKind.CertificateOfDeposit _
        | SecurityKind.CommercialPaper _
        | SecurityKind.TreasuryBill _
        | SecurityKind.Repo _
        | SecurityKind.CashSweep _
        | SecurityKind.OtherSecurity _
        | SecurityKind.DirectLoan _
        | SecurityKind.Commodity _
        | SecurityKind.CryptoCurrency _ -> false
