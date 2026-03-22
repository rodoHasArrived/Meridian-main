namespace Meridian.FSharp.SecurityMasterInterop

open System
open System.Text.Json
open Meridian.FSharp.Domain

type SecurityIdentifierSnapshot(identifier: Identifier) =
    let provider =
        match identifier.Kind with
        | IdentifierKind.ProviderSymbol provider -> provider
        | _ -> null

    let kind =
        match identifier.Kind with
        | IdentifierKind.Ticker -> "Ticker"
        | IdentifierKind.Isin -> "Isin"
        | IdentifierKind.Cusip -> "Cusip"
        | IdentifierKind.Sedol -> "Sedol"
        | IdentifierKind.Figi -> "Figi"
        | IdentifierKind.ProviderSymbol _ -> "ProviderSymbol"
        | IdentifierKind.InternalCode -> "InternalCode"

    member _.Kind = kind
    member _.Value = identifier.Value
    member _.IsPrimary = identifier.IsPrimary
    member _.ValidFrom = identifier.ValidFrom
    member _.ValidTo =
        match identifier.ValidTo with
        | Some value -> Nullable value
        | None -> Nullable()
    member _.Provider = provider

[<AllowNullLiteral>]
[<Sealed>]
type SecurityMasterSnapshotWrapper(record: SecurityMasterRecord) =
    let schemaVersion = 1

    let assetClass, assetSpecificTermsJson =
        match record.Kind with
        | SecurityKind.Equity terms ->
            "Equity", JsonSerializer.Serialize({| schemaVersion = schemaVersion; shareClass = terms.ShareClass |})
        | SecurityKind.Option terms ->
            let (SecurityId underlyingId) = terms.UnderlyingId
            "Option", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   underlyingId = underlyingId
                   putCall = terms.PutCall
                   strike = terms.Strike
                   expiry = terms.Expiry
                   multiplier = terms.Multiplier |})
        | SecurityKind.Future terms ->
            "Future", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   rootSymbol = terms.RootSymbol
                   contractMonth = terms.ContractMonth
                   expiry = terms.Expiry
                   multiplier = terms.Multiplier |})
        | SecurityKind.Bond terms ->
            "Bond", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   maturity = terms.Maturity
                   couponRate = terms.CouponRate
                   dayCount = terms.DayCount |})
        | SecurityKind.FxSpot terms ->
            "FxSpot", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   baseCurrency = terms.BaseCurrency
                   quoteCurrency = terms.QuoteCurrency |})
        | SecurityKind.Deposit terms ->
            "Deposit", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   depositType = terms.DepositType
                   institutionName = terms.InstitutionName
                   maturity = terms.Maturity
                   interestRate = terms.InterestRate
                   dayCount = terms.DayCount
                   isCallable = terms.IsCallable |})
        | SecurityKind.MoneyMarketFund terms ->
            "MoneyMarketFund", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   fundFamily = terms.FundFamily
                   sweepEligible = terms.SweepEligible
                   weightedAverageMaturityDays = terms.WeightedAverageMaturityDays
                   liquidityFeeEligible = terms.LiquidityFeeEligible |})
        | SecurityKind.CertificateOfDeposit terms ->
            "CertificateOfDeposit", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   issuerName = terms.IssuerName
                   maturity = terms.Maturity
                   couponRate = terms.CouponRate
                   callableDate = terms.CallableDate
                   dayCount = terms.DayCount |})
        | SecurityKind.CommercialPaper terms ->
            "CommercialPaper", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   issuerName = terms.IssuerName
                   maturity = terms.Maturity
                   discountRate = terms.DiscountRate
                   dayCount = terms.DayCount
                   isAssetBacked = terms.IsAssetBacked |})
        | SecurityKind.TreasuryBill terms ->
            "TreasuryBill", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   maturity = terms.Maturity
                   auctionDate = terms.AuctionDate
                   cusip = terms.CUSIP
                   discountRate = terms.DiscountRate |})
        | SecurityKind.Repo terms ->
            "Repo", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   counterparty = terms.Counterparty
                   startDate = terms.StartDate
                   endDate = terms.EndDate
                   repoRate = terms.RepoRate
                   collateralType = terms.CollateralType
                   haircut = terms.Haircut |})
        | SecurityKind.CashSweep terms ->
            "CashSweep", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   programName = terms.ProgramName
                   sweepVehicleType = terms.SweepVehicleType
                   sweepFrequency = terms.SweepFrequency
                   targetAccountType = terms.TargetAccountType
                   yieldRate = terms.YieldRate |})
        | SecurityKind.OtherSecurity terms ->
            "OtherSecurity", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   category = terms.Category
                   subType = terms.SubType
                   maturity = terms.Maturity
                   issuerName = terms.IssuerName
                   settlementType = terms.SettlementType |})
        | SecurityKind.Swap terms ->
            "Swap", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   effectiveDate = terms.EffectiveDate
                   maturityDate = terms.MaturityDate
                   legs =
                        terms.Legs
                        |> List.map (fun leg ->
                            {| legType = leg.LegType
                               currency = leg.Currency
                               index = leg.Index
                               fixedRate = leg.FixedRate |}) |})
        | SecurityKind.DirectLoan terms ->
            "DirectLoan", JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   borrower = terms.Borrower
                   maturity = terms.Maturity
                   covenants =
                        terms.Covenants
                        |> List.map (fun covenant ->
                            {| covenantType = covenant.CovenantType
                               threshold = covenant.Threshold
                               notes = covenant.Notes |}) |})

    let commonTermsJson =
        JsonSerializer.Serialize(
            {| displayName = record.Common.DisplayName
               currency = record.Common.Currency
               countryOfRisk = record.Common.CountryOfRisk
               issuerName = record.Common.IssuerName
               exchange = record.Common.Exchange
               lotSize = record.Common.LotSize
               tickSize = record.Common.TickSize |})

    let provenanceJson =
        JsonSerializer.Serialize(
            {| sourceSystem = record.Provenance.SourceSystem
               sourceRecordId = record.Provenance.SourceRecordId
               asOf = record.Provenance.AsOf
               updatedBy = record.Provenance.UpdatedBy
               reason = record.Provenance.Reason |})

    let primaryIdentifier =
        record.Identifiers
        |> List.tryFind (fun identifier -> identifier.IsPrimary)

    member _.Record = record
    member _.SecurityId = let (SecurityId id) = record.SecurityId in id
    member _.AssetClass = assetClass
    member _.Status = if record.Status = SecurityStatus.Active then "Active" else "Inactive"
    member _.DisplayName = record.Common.DisplayName
    member _.Currency = record.Common.Currency
    member _.CommonTermsJson = commonTermsJson
    member _.AssetSpecificTermsJson = assetSpecificTermsJson
    member _.ProvenanceJson = provenanceJson
    member _.Version = record.Version
    member _.EffectiveFrom = record.EffectiveFrom
    member _.EffectiveTo =
        match record.EffectiveTo with
        | Some value -> Nullable value
        | None -> Nullable()
    member _.PrimaryIdentifierKind =
        primaryIdentifier
        |> Option.map (fun identifier -> SecurityIdentifierSnapshot(identifier).Kind)
        |> Option.defaultValue String.Empty
    member _.PrimaryIdentifierValue =
        primaryIdentifier
        |> Option.map (fun identifier -> identifier.Value)
        |> Option.defaultValue String.Empty
    member _.Identifiers =
        record.Identifiers
        |> List.map SecurityIdentifierSnapshot
        |> List.toArray

[<AllowNullLiteral>]
[<Sealed>]
type SecurityMasterCommandResultWrapper(result: Result<SecurityMasterRecord, SecurityValidationError list>) =
    member _.IsSuccess =
        match result with
        | Ok _ -> true
        | Error _ -> false

    member _.Snapshot =
        match result with
        | Ok record -> SecurityMasterSnapshotWrapper(record)
        | Error _ -> null

    member _.Errors =
        match result with
        | Ok _ -> Array.empty
        | Error errors -> errors |> List.map (fun error -> error.Message) |> List.toArray

[<Sealed>]
type SecurityMasterAggregate private () =
    static member private ToResult(initialState: SecurityMasterRecord option, events: Result<SecurityMasterEvent list, SecurityValidationError list>) =
        match events with
        | Error errors -> SecurityMasterCommandResultWrapper(Error errors)
        | Ok eventList ->
            let finalState = eventList |> List.fold SecurityMaster.evolve initialState
            match finalState with
            | Some record -> SecurityMasterCommandResultWrapper(Ok record)
            | None ->
                SecurityMasterCommandResultWrapper(
                    Error [ {
                        Code = "missing_state"
                        Message = "Security master command produced no aggregate state."
                    } ])

    static member Create(command: CreateSecurity) =
        SecurityMasterAggregate.ToResult(None, SecurityMaster.create command)

    static member Amend(current: SecurityMasterRecord, command: AmendTerms) =
        SecurityMasterAggregate.ToResult(Some current, SecurityMaster.amend current command)

    static member Deactivate(current: SecurityMasterRecord, command: DeactivateSecurity) =
        SecurityMasterAggregate.ToResult(Some current, SecurityMaster.deactivate current command)
