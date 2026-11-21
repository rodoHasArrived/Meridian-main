namespace Meridian.FSharp.Domain

/// What each asset class's flat row carries of the term modules its record holds.
///
/// The stage-3 contraction (ADR-027) made every per-class record hold whole shared term modules,
/// so the record type no longer says which members a class has: a warrant's record can carry an
/// exercise style, a deposit's an issue date. The flat codec writes and reads only the class's
/// own members, so a value anywhere else would be accepted, carried by the economic definition
/// and then dropped by the next write of the row. This module is the list the type used to be:
/// per class and embedded module, the members the flat row persists.
///
/// The flat decode sets only these members, so a record built through the codec never trips the
/// check; it is a record constructed directly that can. The list is pinned against the real
/// writer and reader by SecurityKindPersistenceTests, which sets every member of every embedded
/// module in turn and requires the verdict here to match what the round trip does to it.
[<RequireQualifiedAccess>]
module SecurityKindPersistence =

    // One entry per module member: its name, and whether the record holds a value there. A
    // module needs an enumerator here only when some class persists part of it; a module every
    // holder persists whole (the swap legs, the tranche contract, the credit protection, …) has
    // nothing to refuse.

    let private maturity (terms: MaturityTerms) =
        [ "EffectiveDate", terms.EffectiveDate.IsSome
          "IssueDate", terms.IssueDate.IsSome
          "MaturityDate", terms.MaturityDate.IsSome ]

    let private listing (terms: ListingDetailTerms) =
        [ "OptChainId", terms.OptChainId.IsSome
          "SettlementType", terms.SettlementType.IsSome
          "IsAdjusted", terms.IsAdjusted.IsSome
          "LastTradingDt", terms.LastTradingDt.IsSome
          "RootSymbol", terms.RootSymbol.IsSome
          "ContractMonth", terms.ContractMonth.IsSome
          "FirstNoticeDt", terms.FirstNoticeDt.IsSome
          "DeliveryMonthDt", terms.DeliveryMonthDt.IsSome
          "DeliveryLocationCode", terms.DeliveryLocationCode.IsSome
          "IsRollTarget", terms.IsRollTarget.IsSome
          "RollWindowDays", terms.RollWindowDays.IsSome ]

    let private exercise (terms: ExerciseTerms) =
        [ "OptionRight", terms.OptionRight.IsSome
          "StrikePrice", terms.StrikePrice.IsSome
          "StrikeRate", terms.StrikeRate.IsSome
          "ExerciseStyle", terms.ExerciseStyle.IsSome
          "UnderlyingSecurityId", terms.UnderlyingSecurityId.IsSome
          "PremiumAmount", terms.PremiumAmount.IsSome ]

    let private trading (terms: TradingParams) =
        [ "LotSize", terms.LotSize.IsSome
          "TickSize", terms.TickSize.IsSome
          "ContractMultiplier", terms.ContractMultiplier.IsSome
          "MarginRequirementPct", terms.MarginRequirementPct.IsSome
          "TradingHoursUtc", terms.TradingHoursUtc.IsSome
          "CircuitBreakerThresholdPct", terms.CircuitBreakerThresholdPct.IsSome ]

    let private call (terms: EconomicCallTerms) =
        [ "IsCallable", terms.IsCallable
          "FirstCallDate", terms.FirstCallDate.IsSome
          "CallPrice", terms.CallPrice.IsSome
          "CallSchedule", not terms.CallSchedule.IsEmpty
          "MakeWholeSpreadBps", terms.MakeWholeSpreadBps.IsSome
          "IsPuttable", terms.IsPuttable
          "PutSchedule", not terms.PutSchedule.IsEmpty ]

    let private issuer (terms: IssuerRef) =
        [ "IssuerName", terms.IssuerName.IsSome
          "InstitutionName", terms.InstitutionName.IsSome
          "IssuerProgram", terms.IssuerProgram.IsSome
          "LeiCode", terms.LeiCode.IsSome
          "UltimateParentName", terms.UltimateParentName.IsSome
          "IssuerSector", terms.IssuerSector.IsSome
          "IssuerCountry", terms.IssuerCountry.IsSome ]

    let private currencyPair (terms: CurrencyPairTerms) =
        [ "BaseCurrency", terms.BaseCurrency.IsSome
          "QuoteCurrency", terms.QuoteCurrency.IsSome
          "SettlementCurrency", terms.SettlementCurrency.IsSome
          "ForwardRate", terms.ForwardRate.IsSome ]

    let private coupon (terms: CouponTerms) =
        [ "CouponType", terms.CouponType.IsSome
          "CouponRate", terms.CouponRate.IsSome
          "PaymentFrequency", terms.PaymentFrequency.IsSome
          "DayCount", terms.DayCount.IsSome ]

    let private discount (terms: DiscountTerms) =
        [ "DiscountRate", terms.DiscountRate.IsSome
          "YieldRate", terms.YieldRate.IsSome ]

    let private auction (terms: AuctionTerms) =
        [ "AuctionDate", terms.AuctionDate.IsSome
          "AuctionType", terms.AuctionType.IsSome ]

    let private floatingRate (terms: FloatingRateTerms) =
        [ "ReferenceIndex", terms.ReferenceIndex.IsSome
          "SpreadBps", terms.SpreadBps.IsSome
          "ResetFrequency", terms.ResetFrequency.IsSome
          "FloorRate", terms.FloorRate.IsSome
          "CapRate", terms.CapRate.IsSome ]

    let private referenceSource (terms: ReferenceDataSourceTerms) =
        [ "PricingSource", terms.PricingSource.IsSome
          "FixingSource", terms.FixingSource.IsSome ]

    let private pool (terms: StructuredProductTerms) =
        [ "Factor", terms.Factor.IsSome
          "FactorDate", terms.FactorDate.IsSome
          "WeightedAvgCoupon", terms.WeightedAvgCoupon.IsSome
          "WeightedAvgMaturityMonths", terms.WeightedAvgMaturityMonths.IsSome
          "WeightedAvgLoanAgeMos", terms.WeightedAvgLoanAgeMos.IsSome
          "CollateralType", terms.CollateralType.IsSome
          "PoolIdentifier", terms.PoolIdentifier.IsSome
          "TrancheClass", terms.TrancheClass.IsSome
          "PrepaymentAssumption", terms.PrepaymentAssumption.IsSome
          "AverageLifeYears", terms.AverageLifeYears.IsSome
          "IsInterestOnly", terms.IsInterestOnly
          "IsPrincipalOnly", terms.IsPrincipalOnly
          "NotionalBalance", terms.NotionalBalance.IsSome
          "Originator", terms.Originator.IsSome
          "CreditEnhancementPct", terms.CreditEnhancementPct.IsSome
          "FactorScheduleReference", terms.FactorScheduleReference.IsSome
          "FactorScheduleEntries", not terms.FactorScheduleEntries.IsEmpty ]

    let private valuation (terms: PrivateAssetValuationTerms) =
        [ "Vintage", terms.Vintage.IsSome
          "NavDate", terms.NavDate.IsSome
          "Lockup", terms.Lockup.IsSome
          "OwnershipPercent", terms.OwnershipPercent.IsSome
          "CostBasis", terms.CostBasis.IsSome
          "LatestValuation", terms.LatestValuation.IsSome
          "TransferRestrictions", terms.TransferRestrictions.IsSome
          "AddressOrMarket", terms.AddressOrMarket.IsSome
          "AppraisalValue", terms.AppraisalValue.IsSome
          "ValuationDate", terms.ValuationDate.IsSome
          "DebtStack", terms.DebtStack.IsSome ]

    let private notional (terms: NotionalTerms) =
        [ "Notional", terms.Notional.IsSome
          "Denomination", terms.Denomination.IsSome
          "CommittedAmount", terms.CommittedAmount.IsSome
          "FundedAmount", terms.FundedAmount.IsSome
          "UnfundedAmount", terms.UnfundedAmount.IsSome ]

    let private capital (terms: EquityCapitalTerms) =
        [ "ShareClass", terms.ShareClass.IsSome
          "VotingRightsCat", terms.VotingRightsCat.IsSome
          "Classification", terms.Classification.IsSome ]

    let private contractualRate (terms: ContractualRateTerms) =
        [ "CouponRate", terms.CouponRate.IsSome
          "FixedCouponBps", terms.FixedCouponBps.IsSome
          "FeeRate", terms.FeeRate.IsSome
          "PaymentFrequency", terms.PaymentFrequency.IsSome
          "DayCount", terms.DayCount.IsSome ]

    let private financing (terms: FinancingTerms) =
        [ "Counterparty", terms.Counterparty.IsSome
          "CollateralType", terms.CollateralType.IsSome
          "Haircut", terms.Haircut.IsSome
          "OpenDate", terms.OpenDate.IsSome
          "CloseDate", terms.CloseDate.IsSome ]

    let private fund (terms: FundTerms) =
        [ "FundFamily", terms.FundFamily.IsSome
          "WeightedAverageMaturityDays", terms.WeightedAverageMaturityDays.IsSome
          "SweepEligible", terms.SweepEligible.IsSome
          "LiquidityFeeEligible", terms.LiquidityFeeEligible.IsSome ]

    /// The members of a record field that hold a value the class does not persist.
    let private beyond (field: string) (persisted: string list) (members: (string * bool) list) =
        members
        |> List.filter (fun (name, isSet) -> isSet && not (List.contains name persisted))
        |> List.map (fun (name, _) -> field + "." + name)

    // The five trading parameters no listed-contract row carries.
    let private multiplierOnly = [ "ContractMultiplier" ]

    /// The record members that hold a value the class's flat row does not persist, as
    /// "Field.Member" paths over the per-class record. Empty for every record the flat decode
    /// builds.
    let unpersistedMembers (kind: SecurityKind) : string list =
        match kind with
        | SecurityKind.Equity _
        | SecurityKind.MoneyMarketFund _
        | SecurityKind.Commodity _
        | SecurityKind.Cfd _
        | SecurityKind.CustomAsset _ -> []
        | SecurityKind.Option terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "ListingDetail" [ "OptChainId"; "SettlementType"; "IsAdjusted"; "LastTradingDt" ] (listing terms.ListingDetail)
            @ beyond "Exercise" [ "OptionRight"; "StrikePrice"; "ExerciseStyle"; "UnderlyingSecurityId" ] (exercise terms.Exercise)
            @ beyond "TradingParameters" multiplierOnly (trading terms.TradingParameters)
        | SecurityKind.Future terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "ListingDetail"
                [ "SettlementType"; "LastTradingDt"; "RootSymbol"; "ContractMonth"; "FirstNoticeDt"; "DeliveryMonthDt"
                  "DeliveryLocationCode"; "IsRollTarget"; "RollWindowDays" ]
                (listing terms.ListingDetail)
            @ beyond "TradingParameters" multiplierOnly (trading terms.TradingParameters)
        | SecurityKind.Bond terms ->
            beyond "Dates" [ "IssueDate"; "MaturityDate" ] (maturity terms.Dates)
            // The declared call and put schedules are the record's DeclaredSchedule; the call
            // module's own lists are a projection of them and are never read from the record.
            @ beyond "Callability" [ "IsCallable"; "FirstCallDate" ] (call terms.Callability)
            @ beyond "IssuerTerms" [ "IssuerName" ] (issuer terms.IssuerTerms)
        | SecurityKind.FxSpot terms ->
            beyond "CurrencyPair" [ "BaseCurrency"; "QuoteCurrency" ] (currencyPair terms.CurrencyPair)
        | SecurityKind.Deposit terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "IssuerTerms" [ "InstitutionName"; "IssuerProgram" ] (issuer terms.IssuerTerms)
            @ beyond "CouponTerms" [ "CouponType"; "CouponRate"; "DayCount" ] (coupon terms.CouponTerms)
            @ beyond "Callability" [ "IsCallable" ] (call terms.Callability)
        | SecurityKind.CertificateOfDeposit terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "IssuerTerms" [ "IssuerName" ] (issuer terms.IssuerTerms)
            @ beyond "CouponTerms" [ "CouponType"; "CouponRate"; "DayCount" ] (coupon terms.CouponTerms)
            @ beyond "Callability" [ "IsCallable"; "FirstCallDate" ] (call terms.Callability)
        | SecurityKind.CommercialPaper terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "IssuerTerms" [ "IssuerName"; "IssuerProgram" ] (issuer terms.IssuerTerms)
            @ beyond "DiscountTerms" [ "DiscountRate" ] (discount terms.DiscountTerms)
            @ beyond "CouponTerms" [ "CouponType"; "DayCount" ] (coupon terms.CouponTerms)
        | SecurityKind.TreasuryBill terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "Auction" [ "AuctionDate" ] (auction terms.Auction)
            @ beyond "IssuerTerms" [ "IssuerName"; "IssuerProgram"; "IssuerCountry" ] (issuer terms.IssuerTerms)
            @ beyond "DiscountTerms" [ "DiscountRate" ] (discount terms.DiscountTerms)
        | SecurityKind.Repo terms ->
            beyond "DiscountTerms" [ "YieldRate" ] (discount terms.DiscountTerms)
        | SecurityKind.CashSweep terms ->
            beyond "DiscountTerms" [ "YieldRate" ] (discount terms.DiscountTerms)
        | SecurityKind.OtherSecurity terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "IssuerTerms" [ "IssuerName"; "IssuerProgram" ] (issuer terms.IssuerTerms)
        | SecurityKind.Swap _ -> []
        | SecurityKind.DirectLoan terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "IssuerTerms" [ "IssuerName" ] (issuer terms.IssuerTerms)
            @ beyond "FloatingRate" [ "ReferenceIndex"; "SpreadBps"; "ResetFrequency" ] (floatingRate terms.FloatingRate)
            @ beyond "CouponTerms" [ "CouponRate" ] (coupon terms.CouponTerms)
            @ beyond "ReferenceDataSource" [ "PricingSource" ] (referenceSource terms.ReferenceDataSource)
        | SecurityKind.StructuredCredit terms ->
            beyond "CouponTerms" [ "CouponType" ] (coupon terms.CouponTerms)
            // The interest-only and principal-only flags are derived from the tranche kind and
            // label when the pool module is projected; the record's own flags are never written.
            @ beyond "Pool"
                [ "Factor"; "WeightedAvgCoupon"; "WeightedAvgMaturityMonths"; "WeightedAvgLoanAgeMos"; "CollateralType"
                  "PoolIdentifier"; "TrancheClass"; "NotionalBalance"; "FactorScheduleReference"; "FactorScheduleEntries" ]
                (pool terms.Pool)
            @ beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
        | SecurityKind.PrivateFundInterest terms ->
            beyond "PrivateAssetValuation" [ "Vintage"; "NavDate"; "Lockup" ] (valuation terms.PrivateAssetValuation)
            @ beyond "Notional" [ "CommittedAmount"; "FundedAmount"; "UnfundedAmount" ] (notional terms.Notional)
            @ beyond "IssuerTerms" [ "IssuerName"; "IssuerProgram" ] (issuer terms.IssuerTerms)
        | SecurityKind.PrivateCompanyEquity terms ->
            beyond "Capital" [ "ShareClass" ] (capital terms.Capital)
            @ beyond "PrivateAssetValuation"
                [ "OwnershipPercent"; "CostBasis"; "LatestValuation"; "TransferRestrictions" ]
                (valuation terms.PrivateAssetValuation)
            @ beyond "IssuerTerms" [ "IssuerName"; "IssuerProgram" ] (issuer terms.IssuerTerms)
        | SecurityKind.RealEstateHolding terms ->
            beyond "PrivateAssetValuation"
                [ "OwnershipPercent"; "AddressOrMarket"; "AppraisalValue"; "ValuationDate"; "DebtStack" ]
                (valuation terms.PrivateAssetValuation)
            @ beyond "IssuerTerms" [ "IssuerName"; "IssuerProgram"; "IssuerSector" ] (issuer terms.IssuerTerms)
        | SecurityKind.CommitmentGuarantee terms ->
            beyond "Notional" [ "CommittedAmount"; "UnfundedAmount" ] (notional terms.Notional)
            @ beyond "IssuerTerms" [ "IssuerName"; "IssuerProgram" ] (issuer terms.IssuerTerms)
            @ beyond "ContractualRate" [ "FeeRate" ] (contractualRate terms.ContractualRate)
            @ beyond "CollateralTerms" [ "CollateralType" ] (financing terms.CollateralTerms)
        | SecurityKind.CryptoCurrency terms ->
            beyond "CurrencyPair" [ "BaseCurrency"; "QuoteCurrency" ] (currencyPair terms.CurrencyPair)
        | SecurityKind.Warrant terms ->
            beyond "Dates" [ "MaturityDate" ] (maturity terms.Dates)
            @ beyond "Exercise" [ "OptionRight"; "StrikePrice"; "UnderlyingSecurityId" ] (exercise terms.Exercise)
            @ beyond "TradingParameters" multiplierOnly (trading terms.TradingParameters)
        | SecurityKind.FxForward terms ->
            beyond "Dates" [ "EffectiveDate"; "MaturityDate" ] (maturity terms.Dates)
            @ beyond "CurrencyPair" [ "BaseCurrency"; "QuoteCurrency"; "ForwardRate" ] (currencyPair terms.CurrencyPair)
            @ beyond "Notional" [ "Notional" ] (notional terms.Notional)
        | SecurityKind.NonDeliverableForward terms ->
            beyond "Dates" [ "EffectiveDate"; "MaturityDate" ] (maturity terms.Dates)
            @ beyond "Notional" [ "Notional" ] (notional terms.Notional)
            @ beyond "ReferenceDataSource" [ "FixingSource" ] (referenceSource terms.ReferenceDataSource)
        | SecurityKind.CreditDefaultSwap terms ->
            beyond "Notional" [ "Notional" ] (notional terms.Notional)
            @ beyond "ContractualRate" [ "FixedCouponBps"; "PaymentFrequency"; "DayCount" ] (contractualRate terms.ContractualRate)
        | SecurityKind.Swaption terms ->
            beyond "Dates" [ "EffectiveDate"; "MaturityDate" ] (maturity terms.Dates)
            @ beyond "Exercise" [ "OptionRight"; "StrikeRate"; "ExerciseStyle" ] (exercise terms.Exercise)
            @ beyond "Notional" [ "Notional" ] (notional terms.Notional)
        | SecurityKind.CapFloor terms ->
            beyond "Dates" [ "EffectiveDate"; "MaturityDate" ] (maturity terms.Dates)
            @ beyond "Exercise" [ "OptionRight"; "PremiumAmount" ] (exercise terms.Exercise)
            @ beyond "FloatingRate" [ "ReferenceIndex"; "ResetFrequency"; "FloorRate"; "CapRate" ] (floatingRate terms.FloatingRate)
            @ beyond "Notional" [ "Notional" ] (notional terms.Notional)
            @ beyond "ContractualRate" [ "DayCount" ] (contractualRate terms.ContractualRate)
        | SecurityKind.StructuredNote terms ->
            beyond "Dates" [ "IssueDate"; "MaturityDate" ] (maturity terms.Dates)
            @ beyond "Notional" [ "Denomination" ] (notional terms.Notional)
            @ beyond "IssuerTerms" [ "IssuerName" ] (issuer terms.IssuerTerms)
            @ beyond "ContractualRate" [ "CouponRate"; "PaymentFrequency" ] (contractualRate terms.ContractualRate)
        | SecurityKind.InvestmentFund terms ->
            beyond "FundTerms" [ "FundFamily" ] (fund terms.FundTerms)
            @ beyond "ReferenceDataSource" [ "PricingSource" ] (referenceSource terms.ReferenceDataSource)

    /// A member the flat row does not store because the class fixes it, with the one value the
    /// decode gives it.
    let private fixedTo (path: string) (expected: 'T) (actual: 'T option) =
        match actual with
        | Some value when value <> expected -> [ path ]
        | _ -> []

    /// The record members whose value the class derives rather than stores, and which hold a
    /// value other than the derived one: the next read of the row would replace it. An absent
    /// value is not a conflict, since the decode fills it. Empty for every record the flat
    /// decode builds.
    let derivedMemberConflicts (kind: SecurityKind) : string list =
        // A swap, a credit default swap and a commitment issue on their effective date.
        let issuedOnEffectiveDate (dates: MaturityTerms) =
            match dates.IssueDate with
            | Some _ when dates.IssueDate <> dates.EffectiveDate -> [ "Dates.IssueDate" ]
            | _ -> []
        match kind with
        | SecurityKind.Deposit terms ->
            fixedTo "CouponTerms.CouponType" (CouponKind.OtherCoupon "SimpleInterest") terms.CouponTerms.CouponType
        | SecurityKind.CertificateOfDeposit terms ->
            fixedTo "CouponTerms.CouponType" CouponKind.Fixed terms.CouponTerms.CouponType
            // The row stores the call date alone; callable is whether it has one.
            @ (if terms.Callability.IsCallable <> terms.Callability.FirstCallDate.IsSome then [ "Callability.IsCallable" ] else [])
        | SecurityKind.CommercialPaper terms ->
            fixedTo "CouponTerms.CouponType" CouponKind.DiscountNote terms.CouponTerms.CouponType
            // The row stores one flag, so the program is "AssetBacked" or absent.
            @ fixedTo "IssuerTerms.IssuerProgram" "AssetBacked" terms.IssuerTerms.IssuerProgram
        | SecurityKind.TreasuryBill terms ->
            fixedTo "IssuerTerms.IssuerName" "US Treasury" terms.IssuerTerms.IssuerName
            @ fixedTo "IssuerTerms.IssuerCountry" "US" terms.IssuerTerms.IssuerCountry
        | SecurityKind.RealEstateHolding terms ->
            fixedTo "IssuerTerms.IssuerSector" "RealEstate" terms.IssuerTerms.IssuerSector
        | SecurityKind.Swap terms -> issuedOnEffectiveDate terms.Dates
        | SecurityKind.CreditDefaultSwap terms -> issuedOnEffectiveDate terms.Dates
        | SecurityKind.CommitmentGuarantee terms -> issuedOnEffectiveDate terms.Dates
        | _ -> []
