namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
module SecurityMasterLegacyUpgrade =
    let private classificationFromKind (kind: SecurityKind) =
        match kind with
        | SecurityKind.Equity _ ->
            {
                AssetClass = AssetClass.Equity
                Family = Some AssetFamily.CommonEquity
                SubType = SecuritySubType.CommonShare
                TypeName = "Equity"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Option _ ->
            {
                AssetClass = AssetClass.Derivative
                Family = Some AssetFamily.ListedDerivative
                SubType = SecuritySubType.OptionContract
                TypeName = "Option"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Future _ ->
            {
                AssetClass = AssetClass.Derivative
                Family = Some AssetFamily.ListedDerivative
                SubType = SecuritySubType.FutureContract
                TypeName = "Future"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Bond _ ->
            {
                AssetClass = AssetClass.FixedIncome
                Family = Some AssetFamily.CorporateDebt
                SubType = SecuritySubType.CorporateBond
                TypeName = "Bond"
                IssuerType = Some "Corporate"
                RiskCountry = None
            }
        | SecurityKind.FxSpot _ ->
            {
                AssetClass = AssetClass.Other
                Family = None
                SubType = SecuritySubType.OtherSubType "FxSpot"
                TypeName = "FxSpot"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Deposit terms ->
            {
                AssetClass = AssetClass.CashEquivalent
                Family = Some AssetFamily.BankProduct
                SubType =
                    if String.Equals(terms.DepositType, "DemandDeposit", StringComparison.OrdinalIgnoreCase) then
                        SecuritySubType.DemandDeposit
                    else
                        SecuritySubType.TimeDeposit
                TypeName = "Deposit"
                IssuerType = Some "Bank"
                RiskCountry = None
            }
        | SecurityKind.MoneyMarketFund _ ->
            {
                AssetClass = AssetClass.Fund
                Family = Some AssetFamily.MoneyMarket
                SubType = SecuritySubType.MoneyMarketFund
                TypeName = "MoneyMarketFund"
                IssuerType = Some "FundVehicle"
                RiskCountry = None
            }
        | SecurityKind.CertificateOfDeposit _ ->
            {
                AssetClass = AssetClass.CashEquivalent
                Family = Some AssetFamily.BankProduct
                SubType = SecuritySubType.CertificateOfDeposit
                TypeName = "CertificateOfDeposit"
                IssuerType = Some "Bank"
                RiskCountry = None
            }
        | SecurityKind.CommercialPaper _ ->
            {
                AssetClass = AssetClass.CashEquivalent
                Family = Some AssetFamily.CorporateDebt
                SubType = SecuritySubType.CommercialPaper
                TypeName = "CommercialPaper"
                IssuerType = Some "Corporate"
                RiskCountry = None
            }
        | SecurityKind.TreasuryBill _ ->
            {
                AssetClass = AssetClass.FixedIncome
                Family = Some AssetFamily.Sovereign
                SubType = SecuritySubType.TreasuryBill
                TypeName = "TreasuryBill"
                IssuerType = Some "Sovereign"
                RiskCountry = Some "US"
            }
        | SecurityKind.Repo _ ->
            {
                AssetClass = AssetClass.Financing
                Family = Some AssetFamily.RepurchaseAgreement
                SubType = SecuritySubType.Repo
                TypeName = "Repo"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.CashSweep _ ->
            {
                AssetClass = AssetClass.CashEquivalent
                Family = Some AssetFamily.StructuredCash
                SubType = SecuritySubType.CashSweep
                TypeName = "CashSweep"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.OtherSecurity terms ->
            {
                AssetClass = AssetClass.Other
                Family = None
                SubType = SecuritySubType.OtherSubType (terms.SubType |> Option.defaultValue terms.Category)
                TypeName = "OtherSecurity"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Swap _ ->
            {
                AssetClass = AssetClass.Derivative
                Family = Some AssetFamily.ListedDerivative
                SubType = SecuritySubType.SwapContract
                TypeName = "Swap"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.DirectLoan _ ->
            {
                AssetClass = AssetClass.PrivateCredit
                Family = Some AssetFamily.PrivateLoan
                SubType = SecuritySubType.DirectLoan
                TypeName = "DirectLoan"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Commodity _ ->
            {
                AssetClass = AssetClass.Other
                Family = Some (AssetFamily.OtherFamily "Commodity")
                SubType = SecuritySubType.OtherSubType "Commodity"
                TypeName = "Commodity"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.CryptoCurrency _ ->
            {
                AssetClass = AssetClass.Other
                Family = Some (AssetFamily.OtherFamily "Crypto")
                SubType = SecuritySubType.OtherSubType "CryptoCurrency"
                TypeName = "CryptoCurrency"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Cfd _ ->
            {
                AssetClass = AssetClass.Derivative
                Family = Some AssetFamily.ListedDerivative
                SubType = SecuritySubType.OtherSubType "Cfd"
                TypeName = "Cfd"
                IssuerType = None
                RiskCountry = None
            }
        | SecurityKind.Warrant _ ->
            {
                AssetClass = AssetClass.Derivative
                Family = Some AssetFamily.ListedDerivative
                SubType = SecuritySubType.OtherSubType "Warrant"
                TypeName = "Warrant"
                IssuerType = None
                RiskCountry = None
            }

    let private termsFromKind (kind: SecurityKind) =
        match kind with
        | SecurityKind.Equity terms ->
            {
                SecurityTermModules.empty with
                    EquityBehavior =
                        Some {
                            ShareClass = terms.ShareClass
                            VotingRights = None
                            DistributionType = None
                        }
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
                            CouponType = Some (match terms.Coupon with BondCouponStructure.ZeroCoupon -> "ZeroCoupon" | BondCouponStructure.Floating _ -> "Floating" | _ -> "Fixed")
                            CouponRate = BondTerms.couponRate terms
                            PaymentFrequency = None
                            DayCount = BondTerms.dayCount terms
                        }
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
                            CouponType = Some "SimpleInterest"
                            CouponRate = terms.InterestRate
                            PaymentFrequency = None
                            DayCount = terms.DayCount
                        }
                    Call =
                        Some {
                            IsCallable = terms.IsCallable
                            FirstCallDate = None
                            CallPrice = None
                        }
                    Issuer =
                        Some {
                            IssuerName = None
                            InstitutionName = Some terms.InstitutionName
                            IssuerProgram = Some terms.DepositType
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
                            CouponType = Some "Fixed"
                            CouponRate = terms.CouponRate
                            PaymentFrequency = None
                            DayCount = terms.DayCount
                        }
                    Call =
                        Some {
                            IsCallable = terms.CallableDate.IsSome
                            FirstCallDate = terms.CallableDate
                            CallPrice = None
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.IssuerName
                            InstitutionName = None
                            IssuerProgram = None
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
                            CouponType = Some "Discount"
                            CouponRate = None
                            PaymentFrequency = None
                            DayCount = terms.DayCount
                        }
                    Issuer =
                        Some {
                            IssuerName = Some terms.IssuerName
                            InstitutionName = None
                            IssuerProgram = if terms.IsAssetBacked then Some "AssetBacked" else None
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
                            SweepVehicleType = Some terms.SweepVehicleType
                            SweepFrequency = terms.SweepFrequency
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
