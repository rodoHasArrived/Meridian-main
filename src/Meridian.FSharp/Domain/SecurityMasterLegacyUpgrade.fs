namespace Meridian.FSharp.Domain

open System
open System.Globalization
open System.Text.Json

[<RequireQualifiedAccess>]
module SecurityMasterLegacyUpgrade =
    /// Where a legacy securitised row the structured-credit reclassification report considers came
    /// from (ADR-022). Each case corresponds to the asset-class validator rule that flags the row.
    [<RequireQualifiedAccess>]
    type StructuredCreditReclassificationSource =
        /// A Bond whose <c>subclass</c> names a securitised product
        /// (<c>SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL</c>).
        | SecuritizedBondSubclass of BondSubclass
        /// A CustomAsset, or an envelope-less legacy custom row read back as OtherSecurity, whose
        /// <c>category</c> names a securitised product (<c>SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL</c>).
        | SecuritizedCustomCategory of string

    /// One reason the report cannot re-model a row as StructuredCredit without operator input. The
    /// report never guesses: a missing required term or an ambiguous label is a blocker, not a default.
    type StructuredCreditReclassificationBlocker = {
        Code: string
        Message: string
    }

    /// The StructuredCredit terms the report could carry over from the source record. Required
    /// StructuredCredit fields are optional here so a blocked proposal still shows what was derived
    /// and what the operator has to supply.
    type StructuredCreditReclassificationDraft = {
        Tranche: string option
        PoolId: string option
        CollateralType: string option
        OriginalFace: decimal option
        CurrentFactor: decimal option
        CouponOrIndex: string option
        Maturity: DateOnly option
        Kind: StructuredCreditKind option
        Seniority: string option
    }

    /// Report-first reclassification proposal for one legacy securitised row (ADR-022).
    /// <see cref="ProposedTerms"/> is present only when <see cref="Blockers"/> is empty; applying a
    /// proposal is a separate governed amendment, never a side effect of building the report.
    type StructuredCreditReclassificationProposal = {
        SecurityId: SecurityId
        Source: StructuredCreditReclassificationSource
        /// The wire label of <see cref="Source"/> (the bond subclass or the custom category), for callers
        /// that do not pattern-match the union.
        SourceLabel: string
        Draft: StructuredCreditReclassificationDraft
        Blockers: StructuredCreditReclassificationBlocker list
        ProposedTerms: StructuredCreditTerms option
    }

    let private preferredTermsFromClassification classification =
        match classification with
        | Some (EquityClassification.Preferred preferred)
        | Some (EquityClassification.ConvertiblePreferred (preferred, _)) -> Some preferred
        | _ -> None

    let private mapDistributionPolicy = function
        | None -> None
        | Some label -> DistributionPolicy.OtherDistribution label |> Some

    let private mapRedemptionStyle = function
        | None -> None
        | Some label -> RedemptionStyle.OtherRedemption label |> Some

    let private mapSweepVehicle = function
        | "MoneyMarketFund" -> SweepVehicle.MoneyMarketFund
        | "BankDeposit" -> SweepVehicle.BankDeposit
        | "Repo" -> SweepVehicle.Repo
        | other -> SweepVehicle.OtherVehicle other

    let private mapSweepFrequency = Option.map PaymentFrequency.OtherFrequency

    /// Builds the canonical classification for a legacy kind from the single
    /// <see cref="AssetClassRegistry"/> source of truth, layering on the two
    /// instrument-data-dependent sub-type refinements the registry cannot express
    /// structurally (demand vs. time deposits, and free-text OtherSecurity sub-types).
    let private classificationFromKind (kind: SecurityKind) =
        let classification = AssetClassRegistry.classification kind
        match kind with
        | SecurityKind.Deposit terms ->
            { classification with
                SubType =
                    if String.Equals(terms.DepositType, "DemandDeposit", StringComparison.OrdinalIgnoreCase) then
                        SecuritySubType.DemandDeposit
                    else
                        SecuritySubType.TimeDeposit }
        | SecurityKind.OtherSecurity terms ->
            { classification with
                SubType = SecuritySubType.OtherSubType (terms.SubType |> Option.defaultValue terms.Category) }
        | _ -> classification

    /// Whether a tranche label names the strip <paramref name="token"/> as a whole token ("IO",
    /// "IO-1", "A/PO"), not as a substring: "Senior" contains the letters "io" and is not an IO strip.
    let private trancheLabelHasToken (token: string) (tranche: string) =
        tranche.Split([| ' '; '-'; '_'; '/'; '.'; ','; ':'; '('; ')' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.exists (fun part -> String.Equals(part, token, StringComparison.OrdinalIgnoreCase))

    /// The economic Accrual module a declared scheduleConventions block fills. These rules are frozen
    /// with the block (ADR-024, 2026-10-25), so a node that reads the block and a node that writes it
    /// produce the same economic terms: the module is filled only when the block names a business-day
    /// convention or a payment calendar; the convention is kept verbatim (accrualAdjusted stays in the
    /// flat block and never turns it into Unadjusted); the payment calendars become one joint calendar
    /// id; and nothing falls back to the common terms' holiday calendar, which the economic record
    /// already carries in its common terms.
    let private accrualFromConventions (conventions: ScheduleConventions) : AccrualTerms option =
        let calendar =
            Meridian.Contracts.Calendars.BusinessDayCalendars.JointCalendarId(conventions.PaymentCalendars)
            |> Option.ofObj
        match conventions.BusinessDayConvention, calendar with
        | None, None -> None
        | convention, calendar ->
            Some ({ AccrualMethod = None
                    AccrualStartDate = None
                    ExDividendDays = None
                    BusinessDayConvention = convention
                    HolidayCalendar = calendar
                    DayCount = None } : AccrualTerms)

    // The economic Call module from a class's scalar call terms and its declared call and put
    // schedules. These rules are frozen with the schedules (ADR-025 Applications, increment 4b), so
    // every node derives the same economic terms from the same row:
    // - only the class's own scalars feed IsCallable and FirstCallDate, and CallPrice stays None,
    //   because no flat class carries a scalar call price;
    // - declared entries feed the lists, IsPuttable and MakeWholeSpreadBps, sorted stably by date;
    // - the economic price is percent of par, so an entry's price is carried only for a Fixed price on
    //   a PercentOfPar basis (an absent type or basis counts as each); IsParCall marks a carried price
    //   of exactly 100;
    // - CallType is "MakeWhole" for a make-whole entry, otherwise the exercise style verbatim.
    let private carriesParPrice (priceType: string option) (priceBasis: string option) =
        (priceType = None || priceType = Some "Fixed")
        && (priceBasis = None || priceBasis = Some "PercentOfPar")

    let private economicCalls (calls: CallScheduleTerm list) : CallScheduleEntry list =
        calls
        |> List.sortBy (fun call -> call.CallDate)
        |> List.map (fun call ->
            let price = if carriesParPrice call.PriceType call.PriceBasis then call.CallPrice else None
            ({ CallDt = call.CallDate
               CallPx = price
               IsParCall = (price = Some 100m)
               CallType = if call.PriceType = Some "MakeWhole" then Some "MakeWhole" else call.ExerciseStyle }
             : CallScheduleEntry))

    let private economicPuts (puts: PutScheduleTerm list) : PutScheduleEntry list =
        puts
        |> List.sortBy (fun put -> put.PutDate)
        |> List.map (fun put ->
            ({ PutDt = put.PutDate
               PutPx = if carriesParPrice None put.PriceBasis then put.PutPrice else None }
             : PutScheduleEntry))

    let private makeWholeSpread (calls: CallScheduleTerm list) : decimal option =
        calls
        |> List.sortBy (fun call -> call.CallDate)
        |> List.tryPick (fun call -> if call.PriceType = Some "MakeWhole" then call.MakeWholeSpreadBps else None)

    let private callFromSchedules (isCallable: bool) (firstCallDate: DateOnly option) (schedules: CallPutSchedules) : EconomicCallTerms =
        let puts = economicPuts schedules.Puts
        { IsCallable = isCallable
          FirstCallDate = firstCallDate
          CallPrice = None
          CallSchedule = economicCalls schedules.Calls
          MakeWholeSpreadBps = makeWholeSpread schedules.Calls
          IsPuttable = not puts.IsEmpty
          PutSchedule = puts }

    let private contractualRateModule (terms: ContractualRateTerms) =
        if terms.CouponRate.IsNone && terms.FixedCouponBps.IsNone && terms.FeeRate.IsNone
           && terms.PaymentFrequency.IsNone && terms.DayCount.IsNone then None else Some terms

    let private termsFromKind (kind: SecurityKind) =
        match kind with
        | SecurityKind.Equity terms ->
            let preferredTerms = preferredTermsFromClassification terms.Classification
            {
                SecurityTermModules.empty with
                    EquityCapital = Some terms.Capital
                    EquityBehavior =
                        Some {
                            ShareClass = terms.ShareClass
                            VotingRights = terms.VotingRightsCat |> Option.map VotingRightsCat.asString
                            DistributionType =
                                preferredTerms
                                |> Option.map (fun preferred -> preferred.DividendType |> DividendType.asString |> DistributionPolicy.OtherDistribution)
                        }
                    Redemption =
                        preferredTerms
                        |> Option.map (fun preferred ->
                            {
                                RedemptionType =
                                    Some (preferred.LiquidationPreference |> LiquidationPreference.asString |> RedemptionStyle.OtherRedemption)
                                RedemptionPrice = preferred.RedemptionPrice
                                IsBullet = None
                                IsAmortizing = None
                            })
                    Call =
                        preferredTerms
                        |> Option.map (fun preferred ->
                            {
                                IsCallable = preferred.CallableDate.IsSome
                                FirstCallDate = preferred.CallableDate
                                CallPrice = None
                                CallSchedule = []
                                MakeWholeSpreadBps = None
                                IsPuttable = false
                                PutSchedule = []
                            })
            }
        | SecurityKind.Option terms ->
            {
                SecurityTermModules.empty with
                    ListingDetail = Some terms.ListingDetail
                    Maturity = Some terms.Dates
                    TradingParameters = Some terms.TradingParameters
                    Exercise = Some terms.Exercise
            }
        | SecurityKind.Future terms ->
            {
                SecurityTermModules.empty with
                    ListingDetail = Some terms.ListingDetail
                    Maturity = Some terms.Dates
                    TradingParameters = Some terms.TradingParameters
            }
        | SecurityKind.Bond terms ->
            let isStructured =
                Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.IsSecuritizedLabel(BondSubclass.label terms.Subclass)
            let structuredTerms =
                if isStructured then
                    Some {
                        Factor = None
                        FactorDate = None
                        WeightedAvgCoupon = None
                        WeightedAvgMaturityMonths = None
                        WeightedAvgLoanAgeMos = None
                        CollateralType = None
                        PoolIdentifier = None
                        TrancheClass = None
                        PrepaymentAssumption = None
                        AverageLifeYears = None
                        IsInterestOnly =
                            [ "INTERESTONLY"; "INVERSEINTERESTONLY" ]
                            |> List.contains (Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.NormalizeLabel(BondSubclass.label terms.Subclass))
                        IsPrincipalOnly = Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.NormalizeLabel(BondSubclass.label terms.Subclass) = "PRINCIPALONLY"
                        NotionalBalance = None
                        Originator = None
                        CreditEnhancementPct = None
                        FactorScheduleReference = None
                        FactorScheduleEntries = []
                    }
                else None
            {
                SecurityTermModules.empty with
                    DeclaredSchedule = Some terms.DeclaredSchedule
                    DebtClassification = Some terms.DebtClassification
                    BondRedemption = Some terms.RedemptionFacts
                    BondCoupon = Some terms.CouponStructure
                    PrincipalSchedule = Some terms.Principal
                    Maturity = Some terms.Dates
                    Coupon =
                        Some {
                            CouponType =
                                Some (match terms.Coupon with
                                      | BondCouponStructure.ZeroCoupon -> CouponKind.ZeroCoupon
                                      | BondCouponStructure.Floating _ -> CouponKind.Floating
                                      | BondCouponStructure.Step _ -> CouponKind.Step
                                      | BondCouponStructure.InflationLinked _ -> CouponKind.OtherCoupon "InflationLinked"
                                      | _ -> CouponKind.Fixed)
                            CouponRate = BondTerms.couponRate terms
                            PaymentFrequency = terms.PaymentFrequency
                            DayCount = BondTerms.dayCount terms
                        }
                    Accrual = accrualFromConventions terms.ScheduleConventions
                    // Call and issuer terms are carried like the other callable / issued classes
                    // (deposits, CDs), so the economic definition holds what the bond record holds.
                    Call = Some (callFromSchedules terms.IsCallable terms.CallDate terms.CallPutSchedules)
                    Issuer = terms.IssuerTerms.IssuerName |> Option.map (fun _ -> terms.IssuerTerms)
                    StructuredProduct = structuredTerms
            }
        | SecurityKind.FxSpot terms ->
            {
                SecurityTermModules.empty with
                    CurrencyPair = Some terms.CurrencyPair
            }
        | SecurityKind.Deposit terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = terms.Maturity
                        }
                    Coupon = Some terms.CouponTerms
                    Call = Some terms.Callability
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.MoneyMarketFund terms ->
            {
                SecurityTermModules.empty with
                    Fund = Some terms.FundTerms
            }
        | SecurityKind.CertificateOfDeposit terms ->
            {
                SecurityTermModules.empty with
                    DeclaredSchedule = Some terms.DeclaredSchedule
                    Maturity = Some terms.Dates
                    Coupon = Some terms.CouponTerms
                    Accrual = accrualFromConventions terms.ScheduleConventions
                    Call = Some (callFromSchedules terms.CallableDate.IsSome terms.CallableDate terms.CallPutSchedules)
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.CommercialPaper terms ->
            {
                SecurityTermModules.empty with
                    Maturity = Some terms.Dates
                    Discount = Some terms.DiscountTerms
                    Coupon = Some terms.CouponTerms
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.TreasuryBill terms ->
            {
                SecurityTermModules.empty with
                    Maturity = Some terms.Dates
                    Discount = Some terms.DiscountTerms
                    Auction = Some terms.Auction
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.Repo terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = Some terms.StartDate
                            IssueDate = Some terms.StartDate
                            MaturityDate = Some terms.EndDate
                        }
                    Financing = Some terms.FinancingTerms
                    Discount = Some terms.DiscountTerms
            }
        | SecurityKind.CashSweep terms ->
            {
                SecurityTermModules.empty with
                    Sweep = Some terms.SweepTerms
                    Discount = Some terms.DiscountTerms
            }
        | SecurityKind.OtherSecurity terms ->
            {
                SecurityTermModules.empty with
                    ClassificationDetail = Some terms.ClassificationDetail
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = terms.Maturity
                        }
                    Payment =
                        Some {
                            PaymentFrequency = None
                            PaymentLagDays = None
                            PaymentCurrency = None
                        }
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.Swap terms ->
            {
                SecurityTermModules.empty with
                    SwapLegs = Some terms.SwapLegs
                    Maturity = Some terms.Dates
            }
        | SecurityKind.DirectLoan terms ->
            {
                SecurityTermModules.empty with
                    ReferenceDataSource = if terms.PricingSource.IsNone then None else Some terms.ReferenceDataSource
                    PrincipalSchedule = Some terms.Principal
                    LoanCovenants = Some terms.LoanCovenants
                    Coupon = if terms.CurrentCouponRate.IsNone then None else Some terms.CouponTerms
                    FloatingRate =
                        if terms.ReferenceIndex.IsNone && terms.SpreadBps.IsNone && terms.ResetFrequency.IsNone then None
                        else Some terms.FloatingRate
                    Maturity = Some terms.Dates
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.StructuredCredit terms ->
            {
                SecurityTermModules.empty with
                    DeclaredSchedule = Some terms.DeclaredSchedule
                    TrancheContract = Some terms.TrancheContract
                    // The persisted legal final maturity must reach the shared economic-terms
                    // document too: consumers of SecurityEconomicDefinitionRecord.EconomicTerms
                    // (the accounting adapter among them) read maturity only from the Maturity
                    // module, not from the retained asset-specific terms.
                    Maturity = terms.Maturity |> Option.map (fun _ -> terms.Dates)
                    Issuer =
                        Some {
                            IssuerName = terms.PoolId
                            InstitutionName = None
                            IssuerProgram = Some terms.CollateralType
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
                    Coupon = Some terms.CouponTerms
                    Accrual = accrualFromConventions terms.ScheduleConventions
                    // Structured credit has no scalar call terms, so a declared schedule fills only the
                    // lists: no first call date or price, and NonCallPeriodEnd never feeds the module.
                    Call =
                        if CallPutSchedules.isEmpty terms.CallPutSchedules then None
                        else Some (callFromSchedules (not terms.CallPutSchedules.Calls.IsEmpty) None terms.CallPutSchedules)
                    FloatingRate =
                        Some {
                            ReferenceIndex = Some terms.CouponOrIndex
                            SpreadBps = None
                            ResetFrequency = None
                            FloorRate = None
                            CapRate = None
                        }
                    StructuredProduct =
                        Some { terms.Pool with
                                IsInterestOnly =
                                    match terms.Kind with
                                    | Some StructuredCreditKind.InterestOnly -> true
                                    | Some _ -> false
                                    | None -> trancheLabelHasToken "IO" terms.Tranche
                                IsPrincipalOnly =
                                    match terms.Kind with
                                    | Some StructuredCreditKind.PrincipalOnly -> true
                                    | Some _ -> false
                                    | None -> trancheLabelHasToken "PO" terms.Tranche
                        }
            }
        | SecurityKind.PrivateFundInterest terms ->
            {
                SecurityTermModules.empty with
                    PrivateAssetValuation = Some terms.PrivateAssetValuation
                    Fund =
                        Some {
                            FundFamily = Some terms.GpSponsor
                            WeightedAverageMaturityDays = None
                            SweepEligible = None
                            LiquidityFeeEligible = None
                        }
                    Issuer = Some terms.IssuerTerms
                    Notional = Some terms.Notional
            }
        | SecurityKind.PrivateCompanyEquity terms ->
            {
                SecurityTermModules.empty with
                    EquityCapital = Some terms.Capital
                    PrivateAssetValuation = Some terms.PrivateAssetValuation
                    EquityBehavior =
                        Some {
                            ShareClass = Some terms.ShareClass
                            VotingRights = None
                            DistributionType = None
                        }
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.RealEstateHolding terms ->
            {
                SecurityTermModules.empty with
                    PrivateAssetValuation = Some terms.PrivateAssetValuation
                    Issuer = Some terms.IssuerTerms
            }
        | SecurityKind.CommitmentGuarantee terms ->
            {
                SecurityTermModules.empty with
                    ContractualRate = contractualRateModule terms.ContractualRate
                    Financing = if terms.Collateral.IsSome then Some terms.CollateralTerms else None
                    LoanCovenants = Some terms.LoanCovenants
                    Maturity = Some terms.Dates
                    Issuer = Some terms.IssuerTerms
                    Notional = Some terms.Notional
            }
        | SecurityKind.Commodity terms ->
            {
                SecurityTermModules.empty with
                    CommodityContract = Some terms.CommodityContract
                    TradingParameters =
                        Some {
                            LotSize = None
                            TickSize = None
                            ContractMultiplier = terms.ContractSize
                            MarginRequirementPct = None
                            TradingHoursUtc = None
                            CircuitBreakerThresholdPct = None
                        }
            }
        | SecurityKind.CryptoCurrency terms ->
            {
                SecurityTermModules.empty with
                    DigitalAsset = if terms.Network.IsNone then None else Some terms.DigitalAsset
                    CurrencyPair = Some terms.CurrencyPair
            }
        | SecurityKind.Cfd terms ->
            {
                SecurityTermModules.empty with
                    UnderlyingExposure = Some terms.UnderlyingExposure
                    TradingParameters =
                        Some {
                            LotSize = None
                            TickSize = None
                            ContractMultiplier = None
                            // Total for every input: zero leverage carries as a zero margin so the
                            // definition validator can refuse it (cfd_leverage_invalid) instead of
                            // this projection throwing before validation runs.
                            MarginRequirementPct = terms.Leverage |> Option.map (fun l -> if l = 0m then 0m else 1m / l * 100m)
                            TradingHoursUtc = None
                            CircuitBreakerThresholdPct = None
                        }
            }
        | SecurityKind.Warrant terms ->
            {
                SecurityTermModules.empty with
                    Maturity = Some terms.Dates
                    TradingParameters = Some terms.TradingParameters
                    Exercise = Some terms.Exercise
            }
        | SecurityKind.FxForward terms ->
            {
                SecurityTermModules.empty with
                    Settlement = if terms.SettlementType.IsNone then None else Some terms.Settlement
                    Maturity = Some terms.Dates
                    CurrencyPair = Some terms.CurrencyPair
                    Notional = Some terms.Notional
            }
        | SecurityKind.NonDeliverableForward terms ->
            {
                SecurityTermModules.empty with
                    ReferenceDataSource = if terms.FixingSource.IsNone then None else Some terms.ReferenceDataSource
                    Maturity = Some terms.Dates
                    CurrencyPair = Some terms.CurrencyPair
                    Notional = Some terms.Notional
            }
        | SecurityKind.CreditDefaultSwap terms ->
            {
                SecurityTermModules.empty with
                    ContractualRate = Some terms.ContractualRate
                    CreditProtection = Some terms.CreditProtection
                    Maturity = Some terms.Dates
                    Coupon =
                        Some {
                            CouponType = Some CouponKind.Fixed
                            // Running coupon quoted in basis points per annum; the economic module carries a rate.
                            CouponRate = terms.ContractualRate.FixedCouponBps |> Option.map (fun basisPoints -> basisPoints / 10000m)
                            PaymentFrequency = terms.PaymentFrequency |> Option.map PaymentFrequency.parse
                            DayCount = terms.DayCount
                        }
                    Notional = Some terms.Notional
            }
        | SecurityKind.Swaption terms ->
            {
                SecurityTermModules.empty with
                    UnderlyingSwap = Some terms.UnderlyingSwap
                    Settlement = if terms.SettlementType.IsSome then Some terms.Settlement else None
                    Maturity = Some terms.Dates
                    Exercise = Some terms.Exercise
                    Notional = Some terms.Notional
            }
        | SecurityKind.CapFloor terms ->
            {
                SecurityTermModules.empty with
                    ContractualRate = contractualRateModule terms.ContractualRate
                    Maturity = Some terms.Dates
                    FloatingRate = Some terms.FloatingRate
                    Exercise = Some terms.Exercise
                    Notional = Some terms.Notional
            }
        | SecurityKind.StructuredNote terms ->
            {
                SecurityTermModules.empty with
                    ContractualRate = contractualRateModule terms.ContractualRate
                    StructuredPayoff = Some terms.StructuredPayoff
                    Maturity = Some terms.Dates
                    // A cadence without a rate (a contingent coupon) still belongs on the coupon module.
                    Coupon =
                        if terms.CouponRate.IsNone && terms.PaymentFrequency.IsNone then
                            None
                        else
                            Some {
                                // The coupon may be contingent on the underlying; the style is not declared.
                                CouponType = None
                                CouponRate = terms.CouponRate
                                PaymentFrequency = terms.PaymentFrequency |> Option.map PaymentFrequency.parse
                                DayCount = None
                            }
                    Issuer = Some terms.IssuerTerms
                    Notional = Some terms.Notional
            }
        | SecurityKind.InvestmentFund terms ->
            {
                SecurityTermModules.empty with
                    FundShare =
                        if terms.FundType.IsNone && terms.NavCurrency.IsNone && terms.DistributionPolicy.IsNone && terms.IsStableNav.IsNone
                        then None else Some terms.FundShare
                    ReferenceDataSource = if terms.PricingSource.IsNone then None else Some terms.ReferenceDataSource
                    Fund = Some terms.FundTerms
            }
        | SecurityKind.CustomAsset terms ->
            // Custom-asset terms are an opaque profile-governed document; no structural
            // economic-term modules can be extracted without interpreting the profile. The
            // envelope and the document it pins travel whole on the profile-envelope module.
            {
                SecurityTermModules.empty with
                    ProfileEnvelope = Some terms
            }

    /// ADR-027 stage 2: the kind → modules projection as a first-class seam. S2-B1's equivalence
    /// tests reach it directly, and S2-B2 derives the economic definition from commands at
    /// mapping time through it.
    let modulesFromKind (kind: SecurityKind) =
        let rateConvention =
            match kind with
            | SecurityKind.Swap terms -> terms.RateConvention
            | SecurityKind.Deposit terms -> terms.RateConvention
            | SecurityKind.CertificateOfDeposit terms -> terms.RateConvention
            | SecurityKind.CommercialPaper terms -> terms.RateConvention
            | SecurityKind.TreasuryBill terms -> terms.RateConvention
            | SecurityKind.Repo terms -> terms.RateConvention
            | SecurityKind.CashSweep terms -> terms.RateConvention
            | SecurityKind.DirectLoan terms -> terms.RateConvention
            | SecurityKind.StructuredCredit terms -> terms.RateConvention
            | SecurityKind.Swaption terms -> terms.RateConvention
            | SecurityKind.CapFloor terms -> terms.RateConvention
            | SecurityKind.StructuredNote terms -> terms.RateConvention
            | _ -> None
        { termsFromKind kind with
            RateConvention = rateConvention }

    let toEconomicDefinition (record: SecurityMasterRecord) =
        {
            SecurityId = record.SecurityId
            Classification = classificationFromKind record.Kind
            Common = record.Common
            Terms = modulesFromKind record.Kind
            Identifiers = record.Identifiers
            Status = record.Status
            Version = record.Version
            EffectiveFrom = record.EffectiveFrom
            EffectiveTo = record.EffectiveTo
            Provenance = record.Provenance
        }
        |> SecurityEconomicDefinition.normalize

    // ── Structured-credit reclassification (ADR-022, report first) ──────────────────────────────

    let private blocker code message : StructuredCreditReclassificationBlocker =
        { Code = code; Message = message }

    let private kindAmbiguous label alternatives =
        blocker
            "structured_credit_kind_ambiguous"
            (sprintf "Label '%s' does not identify one securitisation family; choose %s." label alternatives)

    let private kindUnrepresentable label =
        blocker
            "structured_credit_kind_unrepresentable"
            (sprintf "Label '%s' has no StructuredCreditKind case; extend the structured-credit-kind taxonomy or supply the kind." label)

    /// Maps a securitised Bond subclass onto the structured-credit family it names. Residential
    /// MBS without an agency/non-agency qualifier and inverse IO strips are reported as blockers:
    /// the former is ambiguous, the latter has no typed kind.
    let private kindFromBondSubclass subclass =
        match Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.NormalizeLabel(BondSubclass.label subclass) with
        | "ASSETBACKED" | "ASSETBACKEDSECURITY" | "ABS" -> Ok StructuredCreditKind.AssetBacked
        | "AGENCYMBS" -> Ok StructuredCreditKind.AgencyMbs
        | "NONAGENCYMBS" -> Ok StructuredCreditKind.NonAgencyMbs
        | "COMMERCIALMBS" | "CMBS" | "COMMERCIALMORTGAGEBACKED" | "COMMERCIALMORTGAGEBACKEDSECURITY" -> Ok StructuredCreditKind.CommercialMbs
        | "CMO" -> Ok StructuredCreditKind.Cmo
        | "CLO" | "COLLATERALIZEDLOANOBLIGATION" -> Ok StructuredCreditKind.Clo
        | "CDO" -> Ok StructuredCreditKind.Cdo
        | "PRINCIPALONLY" -> Ok StructuredCreditKind.PrincipalOnly
        | "INTERESTONLY" -> Ok StructuredCreditKind.InterestOnly
        | "MORTGAGEBACKED" | "MORTGAGEBACKEDSECURITY" | "MBS" -> Error (kindAmbiguous (BondSubclass.label subclass) "AgencyMbs or NonAgencyMbs")
        | _ -> Error (kindUnrepresentable (BondSubclass.label subclass))

    let private isSecuritizedBondSubclass subclass =
        Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.IsSecuritizedLabel(BondSubclass.label subclass)

    let private kindFromCustomCategory (category: string) =
        match Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.NormalizeLabel(category) with
        | "STRUCTUREDCREDIT" -> Error (kindAmbiguous category "one structured-credit-kind taxonomy value")
        | _ -> kindFromBondSubclass (BondSubclass.Other category)

    /// A typed kind supplied by the source record wins over the kind implied by its label, but only
    /// when the label is one of the named taxonomy cases; a free-text label stays a blocker.
    let private typedKind (label: string option) =
        label
        |> Option.map StructuredCreditKind.parse
        |> Option.filter (fun kind ->
            match kind with
            | StructuredCreditKind.OtherStructuredCreditKind _ -> false
            | _ -> true)

    let private couponOrIndexFromBond (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.Fixed(rate, _) -> Ok (rate.ToString(CultureInfo.InvariantCulture))
        | BondCouponStructure.Floating(index, _, _, _, _) -> Ok index
        | BondCouponStructure.ZeroCoupon -> Ok "ZeroCoupon"
        | BondCouponStructure.Step _ ->
            Error (blocker "structured_credit_coupon_structure_unrepresentable" "A step coupon schedule has no single couponOrIndex; supply the tranche coupon or index.")
        | BondCouponStructure.InflationLinked _ ->
            Error (blocker "structured_credit_coupon_structure_unrepresentable" "An inflation-linked coupon has no single couponOrIndex; supply the tranche coupon or index.")

    let private requiredBlocker fieldName value =
        match value with
        | Some _ -> None
        | None ->
            Some (
                blocker
                    (sprintf "structured_credit_%s_required" fieldName)
                    (sprintf "StructuredCredit requires '%s' and the source record does not carry it." fieldName))

    let private emptyDraft : StructuredCreditReclassificationDraft =
        { Tranche = None; PoolId = None; CollateralType = None; OriginalFace = None; CurrentFactor = None
          CouponOrIndex = None; Maturity = None; Kind = None; Seniority = None }

    let private finish securityId source (draft: StructuredCreditReclassificationDraft) blockers =
        let blockers =
            blockers
            @ ([ requiredBlocker "tranche" draft.Tranche
                 requiredBlocker "collateral_type" draft.CollateralType
                 requiredBlocker "original_face" draft.OriginalFace
                 requiredBlocker "coupon_or_index" draft.CouponOrIndex ]
               |> List.choose id)
        let proposedTerms =
            match blockers, draft.Tranche, draft.CollateralType, draft.OriginalFace, draft.CouponOrIndex with
            | [], Some tranche, Some collateralType, Some originalFace, Some couponOrIndex ->
                Some ({
                    Pool = { Factor = draft.CurrentFactor; FactorDate = None; WeightedAvgCoupon = None; WeightedAvgMaturityMonths = None; WeightedAvgLoanAgeMos = None; CollateralType = Some (collateralType); PoolIdentifier = draft.PoolId; TrancheClass = Some (tranche); PrepaymentAssumption = None; AverageLifeYears = None; NotionalBalance = Some (originalFace); Originator = None; CreditEnhancementPct = None; FactorScheduleReference = None; IsInterestOnly = false; IsPrincipalOnly = false; FactorScheduleEntries = [] }
                    CouponTerms = { CouponType = Some (CouponKind.OtherCoupon (couponOrIndex)); CouponRate = None; PaymentFrequency = None; DayCount = None }; TrancheContract = { Kind = draft.Kind; Seniority = draft.Seniority; AttachmentPoint = None; DetachmentPoint = None; ReinvestmentPeriodEnd = None; NonCallPeriodEnd = None }
                    Dates = { EffectiveDate = None; IssueDate = None; MaturityDate = draft.Maturity }
                    RateConvention = None
                    DeclaredSchedule = DeclaredScheduleTerms.empty
                } : StructuredCreditTerms)
            | _ -> None
        let sourceLabel =
            match source with
            | StructuredCreditReclassificationSource.SecuritizedBondSubclass subclass -> BondSubclass.label subclass
            | StructuredCreditReclassificationSource.SecuritizedCustomCategory category -> category
        { SecurityId = securityId; Source = source; SourceLabel = sourceLabel; Draft = draft; Blockers = blockers; ProposedTerms = proposedTerms }

    let private splitResult result =
        match result with
        | Ok value -> Some value, []
        | Error reason -> None, [ reason ]

    let private proposeFromBond securityId (terms: BondTerms) =
        let kind, kindBlockers = splitResult (kindFromBondSubclass terms.Subclass)
        let couponOrIndex, couponBlockers = splitResult (couponOrIndexFromBond terms.Coupon)
        // Owner-approved increment-5 decision 3 keeps this blocker after the contract raise:
        // StructuredCredit has no schema-declared issue date or payment frequency to generate it.
        let conventionBlockers =
            if ScheduleConventions.isEmpty terms.ScheduleConventions then []
            else
                [ blocker
                      "structured_credit_schedule_conventions_unwritable"
                      "The bond declares scheduleConventions, but the StructuredCredit schema has no top-level issueDate or paymentFrequency; repair the declared schedule before reclassification." ]
        // Call and put windows can be carried whole once StructuredCredit writes their version.
        let callPutSince =
            match Meridian.Contracts.SecurityMaster.SecurityAssetTermsSchema.Field("StructuredCredit", "callSchedule") with
            | null -> invalidOp "The term schema declares no StructuredCredit callSchedule; the reclassification and the schema have drifted."
            | field -> field.SinceTermsVersion
        let canWriteCallPut = Meridian.Contracts.SecurityMaster.SecurityAssetClassCatalog.GetTermsVersion("StructuredCredit") >= callPutSince
        let callPutBlockers =
            if CallPutSchedules.isEmpty terms.CallPutSchedules || canWriteCallPut then []
            else
                let since = callPutSince
                [ blocker
                      "structured_credit_call_put_schedules_unwritable"
                      (sprintf "The bond declares callSchedule or putSchedule, StructuredCredit terms version %d terms this node cannot write; reclassify from a node that writes StructuredCredit version %d." since since) ]
        let draft =
            { emptyDraft with
                OriginalFace = terms.Par
                CouponOrIndex = couponOrIndex
                Maturity = Some terms.Maturity
                Kind = kind
                Seniority = terms.Seniority }
        let proposal = finish securityId (StructuredCreditReclassificationSource.SecuritizedBondSubclass terms.Subclass) draft (kindBlockers @ couponBlockers @ conventionBlockers @ callPutBlockers)
        if canWriteCallPut then
            { proposal with ProposedTerms = proposal.ProposedTerms |> Option.map (fun proposed -> { proposed with DeclaredSchedule = { proposed.DeclaredSchedule with Exercises = terms.CallPutSchedules } }) }
        else proposal

    let private tryProperty (name: string) (element: JsonElement) =
        if element.ValueKind = JsonValueKind.Object then
            match element.TryGetProperty(name) with
            | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
            | _ -> None
        else
            None

    let private readString name element =
        tryProperty name element
        |> Option.bind (fun value ->
            if value.ValueKind = JsonValueKind.String then
                match value.GetString() with
                | null -> None
                | text when String.IsNullOrWhiteSpace text -> None
                | text -> Some text
            else
                None)

    let private readDecimal name element =
        tryProperty name element
        |> Option.bind (fun value ->
            if value.ValueKind = JsonValueKind.Number then
                match value.TryGetDecimal() with
                | true, number -> Some number
                | _ -> None
            else
                None)

    let private readDate name element =
        readString name element
        |> Option.bind (fun text ->
            match DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, date -> Some date
            | _ -> None)

    /// Reads a term from the profile-governed <c>profileFields</c> object first and the envelope
    /// root second, which is where envelope-less legacy custom rows kept their fields.
    let private readCustomField read name (document: JsonElement) =
        match tryProperty "profileFields" document |> Option.bind (read name) with
        | Some value -> Some value
        | None -> read name document

    let private isSecuritizedCategory (category: string) =
        Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.IsSecuritizedLabel(category)

    let private proposeFromCustomDocument securityId (category: string) (document: JsonElement) =
        let kind, kindBlockers =
            match typedKind (readCustomField readString "structuredCreditKind" document) with
            | Some kind -> Some kind, []
            | None -> splitResult (kindFromCustomCategory category)
        let draft =
            { emptyDraft with
                Tranche = readCustomField readString "tranche" document
                PoolId = readCustomField readString "poolId" document
                CollateralType = readCustomField readString "collateralType" document
                OriginalFace = readCustomField readDecimal "originalFace" document
                CurrentFactor = readCustomField readDecimal "currentFactor" document
                CouponOrIndex = readCustomField readString "couponOrIndex" document
                Maturity = readCustomField readDate "maturity" document
                Kind = kind
                Seniority = readCustomField readString "seniority" document }
        finish securityId (StructuredCreditReclassificationSource.SecuritizedCustomCategory category) draft kindBlockers

    let private proposeFromCustomAsset securityId (terms: CustomAssetTerms) =
        let parsed =
            try
                use document = JsonDocument.Parse(terms.TermsJson)
                Some (document.RootElement.Clone())
            with :? JsonException -> None
        parsed
        |> Option.bind (fun root ->
            readString "category" root
            |> Option.filter isSecuritizedCategory
            |> Option.map (fun category -> proposeFromCustomDocument securityId category root))

    let private proposeFromOtherSecurity securityId (terms: OtherSecurityTerms) =
        if isSecuritizedCategory terms.Category then
            let kind, kindBlockers = splitResult (kindFromCustomCategory terms.Category)
            let draft = { emptyDraft with Maturity = terms.Maturity; Kind = kind }
            Some (finish securityId (StructuredCreditReclassificationSource.SecuritizedCustomCategory terms.Category) draft kindBlockers)
        else
            None

    /// Builds the report-first StructuredCredit reclassification proposal for a legacy securitised
    /// row (ADR-022): a Bond in a securitised subclass, a CustomAsset whose category names a
    /// securitised product, or an envelope-less legacy custom row read back as OtherSecurity.
    /// Returns None for every other record. Every required StructuredCredit term the source does
    /// not carry, and every label that does not identify one securitisation family, is a blocker;
    /// the proposal never fills a gap with a default.
    let proposeStructuredCreditReclassification (record: SecurityMasterRecord) =
        match record.Kind with
        | SecurityKind.Bond terms when isSecuritizedBondSubclass terms.Subclass ->
            Some (proposeFromBond record.SecurityId terms)
        | SecurityKind.CustomAsset terms -> proposeFromCustomAsset record.SecurityId terms
        | SecurityKind.OtherSecurity terms -> proposeFromOtherSecurity record.SecurityId terms
        | _ -> None
