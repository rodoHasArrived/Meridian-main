namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
module SecurityMasterLegacyUpgrade =
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

    let private mapCouponKind = function
        | "ZeroCoupon" -> CouponKind.ZeroCoupon
        | "Floating" -> CouponKind.Floating
        | "Fixed" -> CouponKind.Fixed
        | "Discount" -> CouponKind.DiscountNote
        | "SimpleInterest" -> CouponKind.OtherCoupon "SimpleInterest"
        | other -> CouponKind.OtherCoupon other

    let private mapDayCount = Option.map DayCountConvention.OtherDayCount

    let private mapSweepVehicle = function
        | "MoneyMarketFund" -> SweepVehicle.MoneyMarketFund
        | "BankDeposit" -> SweepVehicle.BankDeposit
        | "Repo" -> SweepVehicle.Repo
        | other -> SweepVehicle.OtherVehicle other

    let private mapSweepFrequency = Option.map PaymentFrequency.OtherFrequency

    /// Converts the domain's dated factor *levels* into the *transitions* the accounting-event
    /// adapter reads. A level says what the factor is; a paydown event needs to know what it moved
    /// from, so consecutive levels are paired into (prior, current).
    ///
    /// The first observation establishes an opening *level*, not a paydown: it pairs against itself.
    /// Seeding it from par instead would be an assumption that the security was issued immediately
    /// before that row, and it is wrong for every partial-history import — a schedule onboarded
    /// mid-life whose first available trustee row is 0.80 would be read as a 20% principal paydown
    /// in that period, overstating principal returned. A caller who genuinely wants the opening
    /// paydown recorded says so by including the explicit 1.0 row at issuance.
    ///
    /// Every observation is emitted, including ones where the factor did not move. It is tempting
    /// to drop those as empty paydowns, but the coverage check in SecurityMasterAccountingEventService
    /// requires an entry dated inside the reporting period before it will generate anything at all:
    /// silently dropping an unchanged current-period trustee observation turns it into
    /// FACTOR_SCHEDULE_MISSING / FACTOR_STALE and blocks the whole security. A retained no-change
    /// row is harmless downstream — FactorPaydownProjectionService returns NoChange and posts
    /// nothing when prior and current are equal.
    let private toFactorSchedulePoints (schedule: FactorScheduleEntry list) : FactorSchedulePoint list =
        schedule
        |> List.sortBy (fun entry -> entry.AsOfDate)
        |> List.fold
            (fun (priorFactor, points) entry ->
                // None only for the first observation, which therefore reports no movement.
                let effectivePrior = defaultArg priorFactor entry.Factor
                let point =
                    { AsOfDate = entry.AsOfDate
                      PriorFactor = effectivePrior
                      Factor = entry.Factor
                      Source = entry.Source
                      EvidenceLink = entry.EvidenceLink
                      SourceContentHash = entry.SourceContentHash }

                Some entry.Factor, point :: points)
            (None, [])
        |> snd
        |> List.rev

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

    let private termsFromKind (kind: SecurityKind) =
        match kind with
        | SecurityKind.Equity terms ->
            let preferredTerms = preferredTermsFromClassification terms.Classification
            {
                SecurityTermModules.empty with
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
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = Some terms.Expiry
                        }
            }
        | SecurityKind.Future terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = Some terms.Expiry
                        }
            }
        | SecurityKind.Bond terms ->
            let isStructured =
                match terms.Subclass with
                | BondSubclass.MortgageBacked | BondSubclass.AgencyMbs | BondSubclass.CommercialMbs
                | BondSubclass.Cmo | BondSubclass.Clo | BondSubclass.Cdo
                | BondSubclass.AssetBacked
                | BondSubclass.PrincipalOnly | BondSubclass.InterestOnly | BondSubclass.InverseInterestOnly -> true
                | _ -> false
            let structuredTerms =
                if isStructured then
                    Some {
                        Factor = None
                        FactorDate = None
                        FactorSchedule = []
                        WeightedAvgCoupon = None
                        WeightedAvgMaturityMonths = None
                        WeightedAvgLoanAgeMos = None
                        CollateralType = None
                        PoolIdentifier = None
                        TrancheClass = None
                        PrepaymentAssumption = None
                        AverageLifeYears = None
                        IsInterestOnly =
                            match terms.Subclass with
                            | BondSubclass.InterestOnly | BondSubclass.InverseInterestOnly -> true
                            | _ -> false
                        IsPrincipalOnly = terms.Subclass = BondSubclass.PrincipalOnly
                        NotionalBalance = None
                        Originator = None
                        CreditEnhancementPct = None
                    }
                else None
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = Some terms.Maturity
                        }
                    Coupon =
                        Some {
                            CouponType =
                                Some (match terms.Coupon with
                                      | BondCouponStructure.ZeroCoupon -> CouponKind.ZeroCoupon
                                      | BondCouponStructure.Floating _ -> CouponKind.Floating
                                      | _ -> CouponKind.Fixed)
                            CouponRate = BondTerms.couponRate terms
                            PaymentFrequency = None
                            DayCount = BondTerms.dayCount terms |> mapDayCount
                        }
                    StructuredProduct = structuredTerms
            }
        | SecurityKind.FxSpot _ ->
            SecurityTermModules.empty
        | SecurityKind.Deposit terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = terms.Maturity
                        }
                    Coupon =
                        Some {
                            CouponType = Some (mapCouponKind "SimpleInterest")
                            CouponRate = terms.InterestRate
                            PaymentFrequency = None
                            DayCount = terms.DayCount |> mapDayCount
                        }
                    Call =
                        Some {
                            IsCallable = terms.IsCallable
                            FirstCallDate = None
                            CallPrice = None
                            CallSchedule = []
                            MakeWholeSpreadBps = None
                            IsPuttable = false
                            PutSchedule = []
                        }
                    Issuer =
                        Some {
                            IssuerName = None
                            InstitutionName = Some terms.InstitutionName
                            IssuerProgram = Some terms.DepositType
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.MoneyMarketFund terms ->
            {
                SecurityTermModules.empty with
                    Fund =
                        Some {
                            FundFamily = terms.FundFamily
                            WeightedAverageMaturityDays = terms.WeightedAverageMaturityDays
                            SweepEligible = Some terms.SweepEligible
                            LiquidityFeeEligible = Some terms.LiquidityFeeEligible
                        }
            }
        | SecurityKind.CertificateOfDeposit terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = Some terms.Maturity
                        }
                    Coupon =
                        Some {
                            CouponType = Some CouponKind.Fixed
                            CouponRate = terms.CouponRate
                            PaymentFrequency = None
                            DayCount = terms.DayCount |> mapDayCount
                        }
                    Call =
                        Some {
                            IsCallable = terms.CallableDate.IsSome
                            FirstCallDate = terms.CallableDate
                            CallPrice = None
                            CallSchedule = []
                            MakeWholeSpreadBps = None
                            IsPuttable = false
                            PutSchedule = []
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.IssuerName
                            InstitutionName = None
                            IssuerProgram = None
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.CommercialPaper terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = Some terms.Maturity
                        }
                    Discount =
                        Some {
                            DiscountRate = terms.DiscountRate
                            YieldRate = None
                        }
                    Coupon =
                        Some {
                            CouponType = Some (mapCouponKind "Discount")
                            CouponRate = None
                            PaymentFrequency = None
                            DayCount = terms.DayCount |> mapDayCount
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.IssuerName
                            InstitutionName = None
                            IssuerProgram = if terms.IsAssetBacked then Some "AssetBacked" else None
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.TreasuryBill terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = Some terms.Maturity
                        }
                    Discount =
                        Some {
                            DiscountRate = terms.DiscountRate
                            YieldRate = None
                        }
                    Auction =
                        Some {
                            AuctionDate = terms.AuctionDate
                            AuctionType = None
                        }
                    Issuer =
                        Some {
                            IssuerName = Some "US Treasury"
                            InstitutionName = None
                            IssuerProgram = terms.CUSIP
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = Some "US"
                        }
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
                    Financing =
                        Some {
                            Counterparty = Some terms.Counterparty
                            CollateralType = terms.CollateralType
                            Haircut = terms.Haircut
                            OpenDate = Some terms.StartDate
                            CloseDate = Some terms.EndDate
                        }
                    Discount =
                        Some {
                            DiscountRate = None
                            YieldRate = terms.RepoRate
                        }
            }
        | SecurityKind.CashSweep terms ->
            {
                SecurityTermModules.empty with
                    Sweep =
                        Some {
                            ProgramName = Some terms.ProgramName
                            SweepVehicleType = Some (mapSweepVehicle terms.SweepVehicleType)
                            SweepFrequency = terms.SweepFrequency |> mapSweepFrequency
                            TargetAccountType = terms.TargetAccountType
                        }
                    Discount =
                        Some {
                            DiscountRate = None
                            YieldRate = terms.YieldRate
                        }
            }
        | SecurityKind.OtherSecurity terms ->
            {
                SecurityTermModules.empty with
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
                    Issuer =
                        Some {
                            IssuerName = terms.IssuerName
                            InstitutionName = None
                            IssuerProgram = terms.SettlementType
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.Swap terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = Some terms.EffectiveDate
                            IssueDate = Some terms.EffectiveDate
                            MaturityDate = Some terms.MaturityDate
                        }
            }
        | SecurityKind.DirectLoan terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = terms.Maturity
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.Borrower
                            InstitutionName = None
                            IssuerProgram = None
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.StructuredCredit terms ->
            {
                SecurityTermModules.empty with
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
                    Coupon =
                        Some {
                            CouponType = Some (CouponKind.OtherCoupon terms.CouponOrIndex)
                            CouponRate = None
                            // The factor schedule used to be routed here as a payment frequency and
                            // below as a reset frequency, because it was the only string on the
                            // record. It is neither; it now lands in StructuredProduct.FactorSchedule
                            // where the paydown reader looks for it.
                            PaymentFrequency = None
                            DayCount = None
                        }
                    FloatingRate =
                        Some {
                            ReferenceIndex = Some terms.CouponOrIndex
                            SpreadBps = None
                            ResetFrequency = None
                            FloorRate = None
                            CapRate = None
                        }
                    StructuredProduct =
                        Some {
                            // The scalar stays authoritative where it exists — it may have been
                            // updated more recently than the schedule, and it carries no date to
                            // compare against. The schedule supplies the current factor only for a
                            // tranche that has no scalar, in which case its date is known too.
                            // Factor and FactorDate are always derived from the same source so they
                            // cannot describe different points in the paydown.
                            Factor =
                                terms.CurrentFactor
                                |> Option.orElse (terms.FactorSchedule |> List.tryLast |> Option.map (fun entry -> entry.Factor))
                            FactorDate =
                                match terms.CurrentFactor with
                                | Some _ -> None
                                | None -> terms.FactorSchedule |> List.tryLast |> Option.map (fun entry -> entry.AsOfDate)
                            FactorSchedule = toFactorSchedulePoints terms.FactorSchedule
                            WeightedAvgCoupon = None
                            WeightedAvgMaturityMonths = None
                            WeightedAvgLoanAgeMos = None
                            CollateralType = Some terms.CollateralType
                            PoolIdentifier = terms.PoolId
                            TrancheClass = Some terms.Tranche
                            PrepaymentAssumption = None
                            AverageLifeYears = None
                            IsInterestOnly = terms.Tranche.Contains("IO", StringComparison.OrdinalIgnoreCase)
                            IsPrincipalOnly = terms.Tranche.Contains("PO", StringComparison.OrdinalIgnoreCase)
                            NotionalBalance = Some terms.OriginalFace
                            Originator = None
                            CreditEnhancementPct = None
                        }
            }
        | SecurityKind.PrivateFundInterest terms ->
            {
                SecurityTermModules.empty with
                    Fund =
                        Some {
                            FundFamily = Some terms.GpSponsor
                            WeightedAverageMaturityDays = None
                            SweepEligible = None
                            LiquidityFeeEligible = None
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.GpSponsor
                            InstitutionName = None
                            IssuerProgram = Some terms.Strategy
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.PrivateCompanyEquity terms ->
            {
                SecurityTermModules.empty with
                    EquityBehavior =
                        Some {
                            ShareClass = Some terms.ShareClass
                            VotingRights = None
                            DistributionType = None
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.Issuer
                            InstitutionName = None
                            IssuerProgram = Some terms.Round
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.RealEstateHolding terms ->
            {
                SecurityTermModules.empty with
                    Issuer =
                        Some {
                            IssuerName = terms.Sponsor
                            InstitutionName = None
                            IssuerProgram = Some terms.PropertyType
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = Some "RealEstate"
                            IssuerCountry = None
                        }
            }
        | SecurityKind.CommitmentGuarantee terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = Some terms.EffectiveDate
                            IssueDate = Some terms.EffectiveDate
                            MaturityDate = terms.ExpiryDate
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.Counterparty
                            InstitutionName = None
                            IssuerProgram = terms.Beneficiary
                            LeiCode = None
                            UltimateParentName = None
                            IssuerSector = None
                            IssuerCountry = None
                        }
            }
        | SecurityKind.Commodity terms ->
            {
                SecurityTermModules.empty with
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
        | SecurityKind.CryptoCurrency _ ->
            SecurityTermModules.empty
        | SecurityKind.Cfd terms ->
            {
                SecurityTermModules.empty with
                    TradingParameters =
                        Some {
                            LotSize = None
                            TickSize = None
                            ContractMultiplier = None
                            MarginRequirementPct = terms.Leverage |> Option.map (fun l -> 1m / l * 100m)
                            TradingHoursUtc = None
                            CircuitBreakerThresholdPct = None
                        }
            }
        | SecurityKind.Warrant terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = None
                            MaturityDate = terms.Expiry
                        }
                    TradingParameters =
                        Some {
                            LotSize = None
                            TickSize = None
                            ContractMultiplier = terms.Multiplier
                            MarginRequirementPct = None
                            TradingHoursUtc = None
                            CircuitBreakerThresholdPct = None
                        }
            }
        | SecurityKind.InvestmentFund terms ->
            {
                SecurityTermModules.empty with
                    Fund =
                        Some {
                            FundFamily = terms.FundFamily
                            WeightedAverageMaturityDays = None
                            SweepEligible = None
                            LiquidityFeeEligible = None
                        }
            }

    let toEconomicDefinition (record: SecurityMasterRecord) =
        {
            SecurityId = record.SecurityId
            Classification = classificationFromKind record.Kind
            Common = record.Common
            Terms = termsFromKind record.Kind
            Identifiers = record.Identifiers
            Status = record.Status
            Version = record.Version
            EffectiveFrom = record.EffectiveFrom
            EffectiveTo = record.EffectiveTo
            Provenance = record.Provenance
        }
        |> SecurityEconomicDefinition.normalize
