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
                        FactorScheduleReference = None
                    }
                else None
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = terms.IssueDate
                            MaturityDate = Some terms.Maturity
                        }
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
                            DayCount = BondTerms.dayCount terms |> mapDayCount
                        }
                    // Call and issuer terms are carried like the other callable / issued classes
                    // (deposits, CDs), so the economic definition holds what the bond record holds.
                    Call =
                        Some {
                            IsCallable = terms.IsCallable
                            FirstCallDate = terms.CallDate
                            CallPrice = None
                            CallSchedule = []
                            MakeWholeSpreadBps = None
                            IsPuttable = false
                            PutSchedule = []
                        }
                    Issuer =
                        terms.IssuerName
                        |> Option.map (fun issuerName ->
                            {
                                IssuerName = Some issuerName
                                InstitutionName = None
                                IssuerProgram = None
                                LeiCode = None
                                UltimateParentName = None
                                IssuerSector = None
                                IssuerCountry = None
                            })
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
                    // The persisted legal final maturity must reach the shared economic-terms
                    // document too: consumers of SecurityEconomicDefinitionRecord.EconomicTerms
                    // (the accounting adapter among them) read maturity only from the Maturity
                    // module, not from the retained asset-specific terms.
                    Maturity =
                        terms.Maturity
                        |> Option.map (fun maturity ->
                            {
                                EffectiveDate = None
                                IssueDate = None
                                MaturityDate = Some maturity
                            })
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
                            Factor = terms.CurrentFactor
                            FactorDate = None
                            WeightedAvgCoupon = terms.WeightedAverageCoupon
                            WeightedAvgMaturityMonths = terms.WeightedAverageMaturityMonths
                            WeightedAvgLoanAgeMos = terms.WeightedAverageLoanAgeMonths
                            CollateralType = Some terms.CollateralType
                            PoolIdentifier = terms.PoolId
                            TrancheClass = Some terms.Tranche
                            PrepaymentAssumption = None
                            AverageLifeYears = None
                            // The typed kind decides the strip flags when it is present; the tranche
                            // label heuristic stays for records that predate the typed term.
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
                            NotionalBalance = Some terms.OriginalFace
                            Originator = None
                            CreditEnhancementPct = None
                            // The trustee-report pointer travels as evidence lineage, not as a
                            // payment or reset frequency.
                            FactorScheduleReference = terms.FactorSchedule
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
        | SecurityKind.FxForward terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = terms.TradeDate
                            IssueDate = None
                            MaturityDate = Some terms.ValueDate
                        }
            }
        | SecurityKind.NonDeliverableForward terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = Some terms.FixingDate
                            IssueDate = None
                            MaturityDate = Some terms.ValueDate
                        }
            }
        | SecurityKind.CreditDefaultSwap terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = Some terms.EffectiveDate
                            IssueDate = Some terms.EffectiveDate
                            MaturityDate = Some terms.MaturityDate
                        }
                    Coupon =
                        Some {
                            CouponType = Some CouponKind.Fixed
                            // Running coupon quoted in basis points per annum; the economic module carries a rate.
                            CouponRate = Some (terms.FixedCouponBps / 10000m)
                            PaymentFrequency = terms.PaymentFrequency |> Option.map PaymentFrequency.parse
                            DayCount = terms.DayCount |> mapDayCount
                        }
            }
        | SecurityKind.Swaption terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = terms.UnderlyingEffectiveDate
                            IssueDate = None
                            MaturityDate = Some terms.Expiry
                        }
            }
        | SecurityKind.CapFloor terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = Some terms.EffectiveDate
                            IssueDate = None
                            MaturityDate = Some terms.MaturityDate
                        }
                    FloatingRate =
                        Some {
                            ReferenceIndex = Some terms.ReferenceIndex
                            SpreadBps = None
                            ResetFrequency = terms.ResetFrequency
                            FloorRate = terms.FloorStrike
                            CapRate = terms.CapStrike
                        }
            }
        | SecurityKind.StructuredNote terms ->
            {
                SecurityTermModules.empty with
                    Maturity =
                        Some {
                            EffectiveDate = None
                            IssueDate = terms.IssueDate
                            MaturityDate = Some terms.Maturity
                        }
                    Coupon =
                        terms.CouponRate
                        |> Option.map (fun couponRate ->
                            {
                                // The coupon may be contingent on the underlying; the style is not declared.
                                CouponType = None
                                CouponRate = Some couponRate
                                PaymentFrequency = terms.PaymentFrequency |> Option.map PaymentFrequency.parse
                                DayCount = None
                            })
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
        | SecurityKind.CustomAsset _ ->
            // Custom-asset terms are an opaque profile-governed document; no structural
            // economic-term modules can be extracted without interpreting the profile.
            SecurityTermModules.empty

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

    // ── Structured-credit reclassification (ADR-022, report first) ──────────────────────────────

    /// Category labels the CustomAsset validator treats as naming a securitised product.
    let private securitizedCustomCategories =
        [ "MBS"; "ABS"; "CLO"; "CMBS"; "StructuredCredit" ]

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
        match subclass with
        | BondSubclass.AssetBacked -> Ok StructuredCreditKind.AssetBacked
        | BondSubclass.AgencyMbs -> Ok StructuredCreditKind.AgencyMbs
        | BondSubclass.CommercialMbs -> Ok StructuredCreditKind.CommercialMbs
        | BondSubclass.Cmo -> Ok StructuredCreditKind.Cmo
        | BondSubclass.Clo -> Ok StructuredCreditKind.Clo
        | BondSubclass.Cdo -> Ok StructuredCreditKind.Cdo
        | BondSubclass.PrincipalOnly -> Ok StructuredCreditKind.PrincipalOnly
        | BondSubclass.InterestOnly -> Ok StructuredCreditKind.InterestOnly
        | BondSubclass.MortgageBacked -> Error (kindAmbiguous "MortgageBacked" "AgencyMbs or NonAgencyMbs")
        | BondSubclass.InverseInterestOnly -> Error (kindUnrepresentable "InverseInterestOnly")
        | other -> Error (kindUnrepresentable (BondSubclass.label other))

    let private isSecuritizedBondSubclass subclass =
        match subclass with
        | BondSubclass.AssetBacked | BondSubclass.MortgageBacked | BondSubclass.AgencyMbs
        | BondSubclass.CommercialMbs | BondSubclass.Cmo | BondSubclass.Clo | BondSubclass.Cdo
        | BondSubclass.PrincipalOnly | BondSubclass.InterestOnly | BondSubclass.InverseInterestOnly -> true
        | _ -> false

    let private kindFromCustomCategory (category: string) =
        match category.Trim().ToUpperInvariant() with
        | "ABS" -> Ok StructuredCreditKind.AssetBacked
        | "CLO" -> Ok StructuredCreditKind.Clo
        | "CMBS" -> Ok StructuredCreditKind.CommercialMbs
        | "MBS" -> Error (kindAmbiguous category "AgencyMbs or NonAgencyMbs")
        | _ -> Error (kindAmbiguous category "one structured-credit-kind taxonomy value")

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
                    Tranche = tranche
                    PoolId = draft.PoolId
                    CollateralType = collateralType
                    OriginalFace = originalFace
                    CurrentFactor = draft.CurrentFactor
                    CouponOrIndex = couponOrIndex
                    FactorSchedule = None
                    FactorScheduleEntries = []
                    Maturity = draft.Maturity
                    Kind = draft.Kind
                    Seniority = draft.Seniority
                    AttachmentPoint = None
                    DetachmentPoint = None
                    WeightedAverageCoupon = None
                    WeightedAverageMaturityMonths = None
                    WeightedAverageLoanAgeMonths = None
                    ReinvestmentPeriodEnd = None
                    NonCallPeriodEnd = None
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
        let draft =
            { emptyDraft with
                OriginalFace = terms.Par
                CouponOrIndex = couponOrIndex
                Maturity = Some terms.Maturity
                Kind = kind
                Seniority = terms.Seniority }
        finish securityId (StructuredCreditReclassificationSource.SecuritizedBondSubclass terms.Subclass) draft (kindBlockers @ couponBlockers)

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
        securitizedCustomCategories
        |> List.exists (fun known -> String.Equals(known, category.Trim(), StringComparison.OrdinalIgnoreCase))

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
