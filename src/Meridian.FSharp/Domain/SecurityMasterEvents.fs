namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
type SecurityMasterEvent =
    | SecurityCreated of SecurityMasterRecord
    | TermsAmended of beforeVersion:int64 * afterRecord:SecurityMasterRecord
    | SecurityDeactivated of securityId:SecurityId * version:int64 * effectiveTo:DateTimeOffset * provenance:Provenance

[<RequireQualifiedAccess>]
module SecurityMasterEvent =
    let securityId event =
        match event with
        | SecurityMasterEvent.SecurityCreated record -> record.SecurityId
        | SecurityMasterEvent.TermsAmended (_, record) -> record.SecurityId
        | SecurityMasterEvent.SecurityDeactivated (securityId, _, _, _) -> securityId

    let version event =
        match event with
        | SecurityMasterEvent.SecurityCreated record -> record.Version
        | SecurityMasterEvent.TermsAmended (_, record) -> record.Version
        | SecurityMasterEvent.SecurityDeactivated (_, version, _, _) -> version

    let eventType event =
        match event with
        | SecurityMasterEvent.SecurityCreated _ -> "SecurityCreated"
        | SecurityMasterEvent.TermsAmended _ -> "TermsAmended"
        | SecurityMasterEvent.SecurityDeactivated _ -> "SecurityDeactivated"

    let record event =
        match event with
        | SecurityMasterEvent.SecurityCreated record -> Some record
        | SecurityMasterEvent.TermsAmended (_, record) -> Some record
        | SecurityMasterEvent.SecurityDeactivated _ -> None

    let beforeVersion event =
        match event with
        | SecurityMasterEvent.SecurityCreated _ -> None
        | SecurityMasterEvent.TermsAmended (beforeVersion, _) -> Some beforeVersion
        | SecurityMasterEvent.SecurityDeactivated (_, version, _, _) -> Some (version - 1L)

    let affectsActiveProjection event =
        match event with
        | SecurityMasterEvent.SecurityCreated _ -> true
        | SecurityMasterEvent.TermsAmended _ -> true
        | SecurityMasterEvent.SecurityDeactivated _ -> true

    let evolve (state: SecurityMasterRecord option) (event: SecurityMasterEvent) =
        match state, event with
        | None, SecurityMasterEvent.SecurityCreated record ->
            Some (SecurityMasterRecord.normalize record)
        | Some _, SecurityMasterEvent.SecurityCreated _ ->
            state
        | Some _, SecurityMasterEvent.TermsAmended (_, record) ->
            Some (SecurityMasterRecord.normalize record)
        | Some current, SecurityMasterEvent.SecurityDeactivated (_, version, effectiveTo, provenance) ->
            current
            |> SecurityMasterRecord.deactivate effectiveTo provenance
            |> SecurityMasterRecord.withVersion version
            |> SecurityMasterRecord.normalize
            |> Some
        | None, _ ->
            None

// ---------------------------------------------------------------------------
// Corporate Action Events
// ---------------------------------------------------------------------------

/// Opaque identifier for a corporate action event.
type CorpActId = CorpActId of Guid

[<RequireQualifiedAccess>]
type CorpActEvent =
    /// Cash or stock dividend. `DividendPerShare` is in the instrument's currency.
    | Dividend of
        securityId: SecurityId *
        corpActId: CorpActId *
        exDate: DateOnly *
        payDate: DateOnly option *
        dividendPerShare: decimal *
        currency: string
    /// Forward stock split (`SplitRatio` > 1) or reverse split (`SplitRatio` < 1).
    | StockSplit of
        securityId: SecurityId *
        corpActId: CorpActId *
        exDate: DateOnly *
        splitRatio: decimal
    /// Spin-off: a portion of the parent company is distributed as shares of a new entity.
    | SpinOff of
        securityId: SecurityId *
        corpActId: CorpActId *
        exDate: DateOnly *
        newSecurityId: SecurityId *
        distributionRatio: decimal
    /// Merger/absorption: this security is absorbed into an acquirer.
    | MergerAbsorption of
        securityId: SecurityId *
        corpActId: CorpActId *
        effectiveDate: DateOnly *
        acquirerSecurityId: SecurityId *
        exchangeRatio: decimal
    /// Rights issue: existing shareholders are offered additional shares at a subscription price.
    | RightsIssue of
        securityId: SecurityId *
        corpActId: CorpActId *
        exDate: DateOnly *
        subscriptionPricePerShare: decimal *
        rightsPerShare: decimal *
        isRenounceable: bool *
        tradingPeriodEnd: DateOnly option
    /// Tender offer: an acquirer offers to purchase shares at a premium over market price.
    | TenderOffer of
        securityId: SecurityId *
        corpActId: CorpActId *
        offerPricePerShare: decimal *
        currency: string *
        offerPeriodEnd: DateOnly *
        offerorName: string option
    /// Special cash dividend — tax treatment distinct from regular dividends.
    | SpecialDividend of
        securityId: SecurityId *
        corpActId: CorpActId *
        exDate: DateOnly *
        payDate: DateOnly option *
        dividendPerShare: decimal *
        currency: string
    /// Ticker symbol change (e.g. Facebook → Meta).
    | SymbolChange of
        securityId: SecurityId *
        corpActId: CorpActId *
        oldTicker: string *
        newTicker: string *
        effectiveDate: DateOnly
    /// Legal name change.
    | NameChange of
        securityId: SecurityId *
        corpActId: CorpActId *
        oldName: string *
        newName: string *
        effectiveDate: DateOnly
    /// Delisting from exchange (distinct from SecurityDeactivated which is system-internal).
    | Delisting of
        securityId: SecurityId *
        corpActId: CorpActId *
        delistDate: DateOnly *
        finalPrice: decimal option *
        reason: string option
    /// Issuer exercises a call on a callable bond.
    | BondCall of
        securityId: SecurityId *
        corpActId: CorpActId *
        callDate: DateOnly *
        callPricePercentOfPar: decimal *
        noticeDate: DateOnly option
    /// Bond matures and principal is redeemed at scheduled maturity.
    | BondMaturityRedemption of
        securityId: SecurityId *
        corpActId: CorpActId *
        maturityDate: DateOnly *
        finalCouponDate: DateOnly option *
        redemptionPricePercentOfPar: decimal
    /// Return of capital distribution — tax-distinct from dividends; reduces cost basis.
    | ReturnOfCapital of
        securityId: SecurityId *
        corpActId: CorpActId *
        exDate: DateOnly *
        payDate: DateOnly option *
        amountPerShare: decimal *
        currency: string

[<RequireQualifiedAccess>]
module CorpActEvent =
    let securityId event =
        match event with
        | CorpActEvent.Dividend (secId, _, _, _, _, _) -> secId
        | CorpActEvent.StockSplit (secId, _, _, _) -> secId
        | CorpActEvent.SpinOff (secId, _, _, _, _) -> secId
        | CorpActEvent.MergerAbsorption (secId, _, _, _, _) -> secId
        | CorpActEvent.RightsIssue (secId, _, _, _, _, _, _) -> secId
        | CorpActEvent.TenderOffer (secId, _, _, _, _, _) -> secId
        | CorpActEvent.SpecialDividend (secId, _, _, _, _, _) -> secId
        | CorpActEvent.SymbolChange (secId, _, _, _, _) -> secId
        | CorpActEvent.NameChange (secId, _, _, _, _) -> secId
        | CorpActEvent.Delisting (secId, _, _, _, _) -> secId
        | CorpActEvent.BondCall (secId, _, _, _, _) -> secId
        | CorpActEvent.BondMaturityRedemption (secId, _, _, _, _) -> secId
        | CorpActEvent.ReturnOfCapital (secId, _, _, _, _, _) -> secId

    let corpActId event =
        match event with
        | CorpActEvent.Dividend (_, id, _, _, _, _) -> id
        | CorpActEvent.StockSplit (_, id, _, _) -> id
        | CorpActEvent.SpinOff (_, id, _, _, _) -> id
        | CorpActEvent.MergerAbsorption (_, id, _, _, _) -> id
        | CorpActEvent.RightsIssue (_, id, _, _, _, _, _) -> id
        | CorpActEvent.TenderOffer (_, id, _, _, _, _) -> id
        | CorpActEvent.SpecialDividend (_, id, _, _, _, _) -> id
        | CorpActEvent.SymbolChange (_, id, _, _, _) -> id
        | CorpActEvent.NameChange (_, id, _, _, _) -> id
        | CorpActEvent.Delisting (_, id, _, _, _) -> id
        | CorpActEvent.BondCall (_, id, _, _, _) -> id
        | CorpActEvent.BondMaturityRedemption (_, id, _, _, _) -> id
        | CorpActEvent.ReturnOfCapital (_, id, _, _, _, _) -> id

    let exDate event =
        match event with
        | CorpActEvent.Dividend (_, _, date, _, _, _) -> date
        | CorpActEvent.StockSplit (_, _, date, _) -> date
        | CorpActEvent.SpinOff (_, _, date, _, _) -> date
        | CorpActEvent.MergerAbsorption (_, _, date, _, _) -> date
        | CorpActEvent.RightsIssue (_, _, date, _, _, _, _) -> date
        | CorpActEvent.TenderOffer (_, _, _, _, endDate, _) -> endDate
        | CorpActEvent.SpecialDividend (_, _, date, _, _, _) -> date
        | CorpActEvent.SymbolChange (_, _, _, _, date) -> date
        | CorpActEvent.NameChange (_, _, _, _, date) -> date
        | CorpActEvent.Delisting (_, _, date, _, _) -> date
        | CorpActEvent.BondCall (_, _, date, _, _) -> date
        | CorpActEvent.BondMaturityRedemption (_, _, date, _, _) -> date
        | CorpActEvent.ReturnOfCapital (_, _, date, _, _, _) -> date

    let eventType event =
        match event with
        | CorpActEvent.Dividend _ -> "Dividend"
        | CorpActEvent.StockSplit _ -> "StockSplit"
        | CorpActEvent.SpinOff _ -> "SpinOff"
        | CorpActEvent.MergerAbsorption _ -> "MergerAbsorption"
        | CorpActEvent.RightsIssue _ -> "RightsIssue"
        | CorpActEvent.TenderOffer _ -> "TenderOffer"
        | CorpActEvent.SpecialDividend _ -> "SpecialDividend"
        | CorpActEvent.SymbolChange _ -> "SymbolChange"
        | CorpActEvent.NameChange _ -> "NameChange"
        | CorpActEvent.Delisting _ -> "Delisting"
        | CorpActEvent.BondCall _ -> "BondCall"
        | CorpActEvent.BondMaturityRedemption _ -> "BondMaturityRedemption"
        | CorpActEvent.ReturnOfCapital _ -> "ReturnOfCapital"
