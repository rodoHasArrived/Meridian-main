namespace Meridian.FSharp.Domain

open System

type CreateSecurity = {
    SecurityId: SecurityId
    Common: CommonTerms
    Identifiers: Identifier list
    Kind: SecurityKind
    EffectiveFrom: DateTimeOffset
    Provenance: Provenance
}

type AmendTerms = {
    SecurityId: SecurityId
    ExpectedVersion: int64
    Common: CommonTerms option
    Kind: SecurityKind option
    IdentifiersToAdd: Identifier list
    IdentifiersToExpire: Identifier list
    EffectiveFrom: DateTimeOffset
    Provenance: Provenance
}

type DeactivateSecurity = {
    SecurityId: SecurityId
    ExpectedVersion: int64
    EffectiveTo: DateTimeOffset
    Provenance: Provenance
}

type SecurityValidationError = {
    Code: string
    Message: string
}

[<RequireQualifiedAccess>]
module SecurityMaster =
    let private error code message =
        { Code = code; Message = message }

    let private require condition validationError =
        if condition then [] else [ validationError ]

    let private requireNotBlank code fieldName value =
        require
            (not (String.IsNullOrWhiteSpace value))
            (error code $"{fieldName} must not be blank.")

    let private validateCommonTerms (common: CommonTerms) =
        []
        @ requireNotBlank "display_name_required" "DisplayName" common.DisplayName
        @ requireNotBlank "currency_required" "Currency" common.Currency

    let private validateProvenance (provenance: Provenance) =
        []
        @ requireNotBlank "source_system_required" "SourceSystem" provenance.SourceSystem
        @ requireNotBlank "updated_by_required" "UpdatedBy" provenance.UpdatedBy

    let private validateKind (kind: SecurityKind) =
        match kind with
        | SecurityKind.Equity _ -> []
        | SecurityKind.Option terms ->
            []
            @ requireNotBlank "option_put_call_required" "PutCall" terms.PutCall
            @ require
                (String.Equals(terms.PutCall, "Put", StringComparison.OrdinalIgnoreCase)
                 || String.Equals(terms.PutCall, "Call", StringComparison.OrdinalIgnoreCase))
                (error "option_put_call_invalid" "Option PutCall must be either 'Put' or 'Call'.")
            @ require (terms.Strike > 0m) (error "option_strike_invalid" "Option strike must be greater than zero.")
            @ require (terms.Multiplier > 0m) (error "option_multiplier_invalid" "Option multiplier must be greater than zero.")
        | SecurityKind.Future terms ->
            []
            @ requireNotBlank "future_root_symbol_required" "RootSymbol" terms.RootSymbol
            @ requireNotBlank "future_contract_month_required" "ContractMonth" terms.ContractMonth
            @ require (terms.Multiplier > 0m) (error "future_multiplier_invalid" "Future multiplier must be greater than zero.")
        | SecurityKind.Bond terms ->
            []
            @ require (BondTerms.couponRate terms |> Option.forall (fun rate -> rate >= 0m))
                (error "bond_coupon_invalid" "Bond coupon rate must be zero or greater when present.")
        | SecurityKind.FxSpot terms ->
            []
            @ requireNotBlank "fx_base_currency_required" "BaseCurrency" terms.BaseCurrency
            @ requireNotBlank "fx_quote_currency_required" "QuoteCurrency" terms.QuoteCurrency
            @ require (not (String.Equals(terms.BaseCurrency, terms.QuoteCurrency, StringComparison.OrdinalIgnoreCase)))
                (error "fx_currency_pair_invalid" "BaseCurrency and QuoteCurrency must differ.")
        | SecurityKind.Deposit terms ->
            []
            @ requireNotBlank "deposit_type_required" "DepositType" terms.DepositType
            @ requireNotBlank "deposit_institution_required" "InstitutionName" terms.InstitutionName
            @ require (terms.InterestRate |> Option.forall (fun rate -> rate >= 0m))
                (error "deposit_interest_rate_invalid" "Deposit InterestRate must be zero or greater when present.")
        | SecurityKind.MoneyMarketFund terms ->
            []
            @ require (terms.WeightedAverageMaturityDays |> Option.forall (fun days -> days >= 0))
                (error "mmf_wam_invalid" "MoneyMarketFund WeightedAverageMaturityDays must be zero or greater when present.")
        | SecurityKind.CertificateOfDeposit terms ->
            []
            @ requireNotBlank "cd_issuer_required" "IssuerName" terms.IssuerName
            @ require (terms.CouponRate |> Option.forall (fun rate -> rate >= 0m))
                (error "cd_coupon_invalid" "CertificateOfDeposit CouponRate must be zero or greater when present.")
            @ require (terms.CallableDate |> Option.forall (fun callableDate -> callableDate <= terms.Maturity))
                (error "cd_callable_date_invalid" "CertificateOfDeposit CallableDate must be on or before Maturity when present.")
        | SecurityKind.CommercialPaper terms ->
            []
            @ requireNotBlank "commercial_paper_issuer_required" "IssuerName" terms.IssuerName
            @ require (terms.DiscountRate |> Option.forall (fun rate -> rate >= 0m))
                (error "commercial_paper_discount_rate_invalid" "CommercialPaper DiscountRate must be zero or greater when present.")
        | SecurityKind.TreasuryBill terms ->
            []
            @ require (terms.AuctionDate |> Option.forall (fun auctionDate -> auctionDate <= terms.Maturity))
                (error "treasury_bill_auction_date_invalid" "TreasuryBill AuctionDate must be on or before Maturity when present.")
            @ require (terms.DiscountRate |> Option.forall (fun rate -> rate >= 0m))
                (error "treasury_bill_discount_rate_invalid" "TreasuryBill DiscountRate must be zero or greater when present.")
        | SecurityKind.Repo terms ->
            []
            @ requireNotBlank "repo_counterparty_required" "Counterparty" terms.Counterparty
            @ require (terms.StartDate <= terms.EndDate)
                (error "repo_date_range_invalid" "Repo StartDate must be on or before EndDate.")
            @ require (terms.RepoRate |> Option.forall (fun rate -> rate >= 0m))
                (error "repo_rate_invalid" "Repo RepoRate must be zero or greater when present.")
            @ require (terms.Haircut |> Option.forall (fun haircut -> haircut >= 0m))
                (error "repo_haircut_invalid" "Repo Haircut must be zero or greater when present.")
        | SecurityKind.CashSweep terms ->
            []
            @ requireNotBlank "cash_sweep_program_required" "ProgramName" terms.ProgramName
            @ requireNotBlank "cash_sweep_vehicle_required" "SweepVehicleType" terms.SweepVehicleType
            @ require (terms.YieldRate |> Option.forall (fun rate -> rate >= 0m))
                (error "cash_sweep_yield_rate_invalid" "CashSweep YieldRate must be zero or greater when present.")
        | SecurityKind.OtherSecurity terms ->
            []
            @ requireNotBlank "other_security_category_required" "Category" terms.Category
        | SecurityKind.Swap terms ->
            []
            @ require (terms.EffectiveDate <= terms.MaturityDate)
                (error "swap_date_range_invalid" "Swap EffectiveDate must be on or before MaturityDate.")
            @ require (not terms.Legs.IsEmpty) (error "swap_legs_required" "Swap definitions must include at least one leg.")
            @ (terms.Legs
               |> List.collect (fun leg ->
                   []
                   @ requireNotBlank "swap_leg_type_required" "SwapLeg.LegType" leg.LegType
                   @ requireNotBlank "swap_leg_currency_required" "SwapLeg.Currency" leg.Currency
                   @ require (leg.Notional |> Option.forall (fun notional -> notional > 0m))
                       (error "swap_leg_notional_invalid" "SwapLeg Notional must be greater than zero when present.")
                   // SpreadBps is deliberately unconstrained in sign: a floating leg quoted below its
                   // reference index (e.g. SOFR - 10bp) is ordinary economics, and the cash-flow
                   // projection models the leg as CurrentIndexRate + SpreadBps, clamping only the
                   // resulting annual rate.
                   @ require
                       (leg.Direction
                        |> Option.forall (fun direction ->
                            String.Equals(direction, "Pay", StringComparison.OrdinalIgnoreCase)
                            || String.Equals(direction, "Receive", StringComparison.OrdinalIgnoreCase)))
                       (error "swap_leg_direction_invalid" "SwapLeg Direction must be either 'Pay' or 'Receive' when present.")))
        | SecurityKind.DirectLoan terms ->
            []
            @ requireNotBlank "direct_loan_borrower_required" "Borrower" terms.Borrower
            @ (terms.Covenants
               |> List.collect (fun covenant ->
                   []
                   @ requireNotBlank "covenant_type_required" "CovenantType" covenant.CovenantType
                   @ requireNotBlank "covenant_threshold_required" "Threshold" covenant.Threshold))
        | SecurityKind.StructuredCredit terms ->
            []
            @ requireNotBlank "structured_credit_tranche_required" "Tranche" terms.Tranche
            @ requireNotBlank "structured_credit_collateral_required" "CollateralType" terms.CollateralType
            @ require (terms.OriginalFace > 0m)
                (error "structured_credit_original_face_invalid" "StructuredCredit OriginalFace must be greater than zero.")
            @ require (terms.CurrentFactor |> Option.forall (fun factor -> factor >= 0m))
                (error "structured_credit_current_factor_invalid" "StructuredCredit CurrentFactor must be zero or greater when present.")
            @ requireNotBlank "structured_credit_coupon_index_required" "CouponOrIndex" terms.CouponOrIndex
            // A factor outside (0, 1] restates face to zero or above original — both silently wrong
            // in the amortization the schedule feeds, so they are rejected rather than normalized.
            @ (terms.FactorSchedule
               |> List.collect (fun entry ->
                   require (entry.Factor > 0m && entry.Factor <= 1m)
                       (error "structured_credit_factor_schedule_factor_invalid"
                              "StructuredCredit FactorSchedule factors must be greater than zero and no greater than one.")))
            @ require
                (terms.FactorSchedule
                 |> List.map (fun entry -> entry.AsOfDate)
                 |> List.distinct
                 |> List.length = terms.FactorSchedule.Length)
                (error "structured_credit_factor_schedule_duplicate_date"
                       "StructuredCredit FactorSchedule must not carry two points for the same date.")
            // A pool factor only ever amortizes down. An increasing schedule is accepted nowhere
            // downstream — FactorPaydownProjectionService rejects the transition outright with
            // factor-paydown.factor-increase — so accepting it here would let a record be created
            // canonically and then fail High-severity in accounting, which is the exact
            // write-accepts/read-rejects split this change exists to close.
            @ require
                (terms.FactorSchedule
                 |> List.sortBy (fun entry -> entry.AsOfDate)
                 |> List.pairwise
                 |> List.forall (fun (earlier, later) -> later.Factor <= earlier.Factor))
                (error "structured_credit_factor_schedule_not_monotonic"
                       "StructuredCredit FactorSchedule factors must not increase over time; a pool factor only amortizes down.")
        | SecurityKind.PrivateFundInterest terms ->
            []
            @ requireNotBlank "private_fund_gp_required" "GpSponsor" terms.GpSponsor
            @ requireNotBlank "private_fund_strategy_required" "Strategy" terms.Strategy
            @ require (terms.Vintage > 0) (error "private_fund_vintage_invalid" "PrivateFundInterest Vintage must be greater than zero.")
            @ require (terms.Commitment > 0m) (error "private_fund_commitment_invalid" "PrivateFundInterest Commitment must be greater than zero.")
            @ require (terms.FundedAmount |> Option.forall (fun amount -> amount >= 0m))
                (error "private_fund_funded_invalid" "PrivateFundInterest FundedAmount must be zero or greater when present.")
            @ require (terms.UnfundedAmount |> Option.forall (fun amount -> amount >= 0m))
                (error "private_fund_unfunded_invalid" "PrivateFundInterest UnfundedAmount must be zero or greater when present.")
        | SecurityKind.PrivateCompanyEquity terms ->
            []
            @ requireNotBlank "private_company_issuer_required" "Issuer" terms.Issuer
            @ requireNotBlank "private_company_share_class_required" "ShareClass" terms.ShareClass
            @ requireNotBlank "private_company_round_required" "Round" terms.Round
            @ require (terms.OwnershipPercent |> Option.forall (fun percent -> percent >= 0m))
                (error "private_company_ownership_invalid" "PrivateCompanyEquity OwnershipPercent must be zero or greater when present.")
            @ require (terms.CostBasis > 0m) (error "private_company_cost_basis_invalid" "PrivateCompanyEquity CostBasis must be greater than zero.")
            @ require (terms.LatestValuation |> Option.forall (fun amount -> amount >= 0m))
                (error "private_company_valuation_invalid" "PrivateCompanyEquity LatestValuation must be zero or greater when present.")
        | SecurityKind.RealEstateHolding terms ->
            []
            @ requireNotBlank "real_estate_property_type_required" "PropertyType" terms.PropertyType
            @ requireNotBlank "real_estate_market_required" "AddressOrMarket" terms.AddressOrMarket
            @ require (terms.OwnershipPercent > 0m) (error "real_estate_ownership_invalid" "RealEstateHolding OwnershipPercent must be greater than zero.")
            @ require (terms.AppraisalValue > 0m) (error "real_estate_appraisal_invalid" "RealEstateHolding AppraisalValue must be greater than zero.")
        | SecurityKind.CommitmentGuarantee terms ->
            []
            @ requireNotBlank "commitment_guarantee_counterparty_required" "Counterparty" terms.Counterparty
            @ require (terms.CommittedAmount > 0m) (error "commitment_guarantee_amount_invalid" "CommitmentGuarantee CommittedAmount must be greater than zero.")
            @ require (terms.UnfundedAmount |> Option.forall (fun amount -> amount >= 0m))
                (error "commitment_guarantee_exposure_invalid" "CommitmentGuarantee UnfundedAmount must be zero or greater when present.")
            @ require (terms.ExpiryDate |> Option.forall (fun expiry -> terms.EffectiveDate <= expiry))
                (error "commitment_guarantee_date_range_invalid" "CommitmentGuarantee EffectiveDate must be on or before ExpiryDate when present.")
            @ require (terms.FeeRate |> Option.forall (fun rate -> rate >= 0m))
                (error "commitment_guarantee_fee_invalid" "CommitmentGuarantee FeeRate must be zero or greater when present.")
            @ (terms.Covenants
               |> List.collect (fun covenant ->
                   []
                   @ requireNotBlank "covenant_type_required" "CovenantType" covenant.CovenantType
                   @ requireNotBlank "covenant_threshold_required" "Threshold" covenant.Threshold))
        | SecurityKind.Commodity terms ->
            []
            @ requireNotBlank "commodity_type_required" "CommodityType" terms.CommodityType
            @ require (terms.ContractSize |> Option.forall (fun size -> size > 0m))
                (error "commodity_contract_size_invalid" "Commodity ContractSize must be greater than zero when present.")
        | SecurityKind.CryptoCurrency terms ->
            []
            @ requireNotBlank "crypto_base_currency_required" "BaseCurrency" terms.BaseCurrency
            @ requireNotBlank "crypto_quote_currency_required" "QuoteCurrency" terms.QuoteCurrency
            @ require (not (String.Equals(terms.BaseCurrency, terms.QuoteCurrency, StringComparison.OrdinalIgnoreCase)))
                (error "crypto_currency_pair_invalid" "BaseCurrency and QuoteCurrency must differ.")
        | SecurityKind.Cfd terms ->
            []
            @ requireNotBlank "cfd_underlying_asset_class_required" "UnderlyingAssetClass" terms.UnderlyingAssetClass
            @ require (terms.Leverage |> Option.forall (fun l -> l > 0m))
                (error "cfd_leverage_invalid" "CFD Leverage must be greater than zero when present.")
        | SecurityKind.Warrant terms ->
            []
            @ requireNotBlank "warrant_type_required" "WarrantType" terms.WarrantType
            @ require
                (String.Equals(terms.WarrantType, "Call", StringComparison.OrdinalIgnoreCase)
                 || String.Equals(terms.WarrantType, "Put", StringComparison.OrdinalIgnoreCase))
                (error "warrant_type_invalid" "Warrant WarrantType must be either 'Call' or 'Put'.")
            @ require (terms.Strike |> Option.forall (fun s -> s > 0m))
                (error "warrant_strike_invalid" "Warrant Strike must be greater than zero when present.")
            @ require (terms.Multiplier |> Option.forall (fun m -> m > 0m))
                (error "warrant_multiplier_invalid" "Warrant Multiplier must be greater than zero when present.")
        | SecurityKind.InvestmentFund _ ->
            []

    let private validateIdentifier (identifier: Identifier) =
        []
        @ requireNotBlank "identifier_value_required" "Identifier.Value" identifier.Value
        @ (match identifier.Kind with
           | IdentifierKind.ProviderSymbol provider ->
               requireNotBlank "identifier_provider_required" "Identifier.Provider" provider
           | _ -> [])
        @ require (SecurityIdentifier.providerMetadataMatchesKind identifier)
            (error
                "identifier_provider_mismatch"
                "ProviderSymbol metadata must match the authoritative provider namespace carried by Identifier.Kind.")
        @ require (identifier.ValidTo |> Option.forall (fun validTo -> validTo > identifier.ValidFrom))
            (error "identifier_date_range_invalid" "Identifier ValidTo must be later than ValidFrom when present.")

    let private validateActiveIdentifierSet (identifiers: Identifier list) (asOf: DateTimeOffset) =
        let activeIdentifiers =
            identifiers
            |> List.filter (SecurityIdentifier.isActiveAt asOf)

        let primaryCount =
            activeIdentifiers
            |> List.filter (fun identifier -> identifier.IsPrimary)
            |> List.length

        let duplicateCount =
            activeIdentifiers
            |> List.countBy (fun identifier ->
                SecurityIdentifier.kindName identifier,
                SecurityIdentifier.normalizeValue identifier.Value,
                identifier |> SecurityIdentifier.provider |> Option.defaultValue String.Empty |> SecurityIdentifier.normalizeValue)
            |> List.filter (fun (_, count) -> count > 1)
            |> List.length

        []
        @ require (primaryCount = 1)
            (error "primary_identifier_invalid" "Active security identifiers must contain exactly one primary identifier.")
        @ require (duplicateCount = 0)
            (error "duplicate_identifier_active" "Active security identifiers must not contain duplicate kind/value/provider combinations.")

    let private validateRecord (record: SecurityMasterRecord) (asOf: DateTimeOffset) =
        []
        @ validateCommonTerms record.Common
        @ validateProvenance record.Provenance
        @ validateKind record.Kind
        @ (record.Identifiers |> List.collect validateIdentifier)
        @ validateActiveIdentifierSet record.Identifiers asOf

    let private validateCreate (command: CreateSecurity) =
        []
        @ validateCommonTerms command.Common
        @ validateProvenance command.Provenance
        @ validateKind command.Kind
        @ (command.Identifiers |> List.collect validateIdentifier)
        @ validateActiveIdentifierSet command.Identifiers command.EffectiveFrom

    let private validateAmend (current: SecurityMasterRecord) (command: AmendTerms) (nextIdentifiers: Identifier list) =
        let primaryToExpire =
            nextIdentifiers
            |> List.filter (fun identifier -> identifier.IsPrimary)
            |> List.exists (fun identifier ->
                identifier.ValidTo = Some command.EffectiveFrom)

        []
        @ require (current.SecurityId = command.SecurityId)
            (error "security_id_mismatch" "AmendTerms.SecurityId must match the current aggregate SecurityId.")
        @ require (current.Version = command.ExpectedVersion)
            (error "version_conflict" "ExpectedVersion does not match the current security version.")
        @ require (current.Status = SecurityStatus.Active)
            (error "security_inactive" "Inactive securities cannot be amended.")
        @ require (command.EffectiveFrom >= current.EffectiveFrom)
            (error "amend_effective_from_invalid" "AmendTerms.EffectiveFrom cannot be earlier than the security EffectiveFrom.")
        @ require (not primaryToExpire)
            (error "primary_identifier_expiry_forbidden" "Primary identifiers cannot be expired through TermsAmended.")
        @ (command.Common |> Option.map validateCommonTerms |> Option.defaultValue [])
        @ validateProvenance command.Provenance
        @ (command.Kind |> Option.map validateKind |> Option.defaultValue [])
        @ (command.IdentifiersToAdd |> List.collect validateIdentifier)
        @ validateActiveIdentifierSet nextIdentifiers command.EffectiveFrom

    let private validateDeactivate (current: SecurityMasterRecord) (command: DeactivateSecurity) =
        []
        @ require (current.SecurityId = command.SecurityId)
            (error "security_id_mismatch" "DeactivateSecurity.SecurityId must match the current aggregate SecurityId.")
        @ require (current.Version = command.ExpectedVersion)
            (error "version_conflict" "ExpectedVersion does not match the current security version.")
        @ require (current.Status <> SecurityStatus.Inactive)
            (error "already_inactive" "Security is already inactive.")
        @ require (command.EffectiveTo >= current.EffectiveFrom)
            (error "deactivation_effective_to_invalid" "DeactivateSecurity.EffectiveTo cannot be earlier than EffectiveFrom.")
        @ validateProvenance command.Provenance

    let private collectExpiredIdentifiers (identifiersToExpire: Identifier list) (effectiveFrom: DateTimeOffset) (identifiers: Identifier list) =
        identifiers
        |> List.map (fun identifier ->
            if identifiersToExpire |> List.exists (SecurityIdentifier.sameIdentity identifier) then
                { identifier with ValidTo = Some effectiveFrom }
            else
                identifier)

    let create (command: CreateSecurity) =
        match validateCreate command with
        | [] ->
            let record: SecurityMasterRecord = {
                SecurityId = command.SecurityId
                Status = SecurityStatus.Active
                Common = command.Common
                Identifiers = command.Identifiers
                Kind = command.Kind
                Version = 1L
                EffectiveFrom = command.EffectiveFrom
                EffectiveTo = None
                Provenance = command.Provenance
            }

            match validateRecord record command.EffectiveFrom with
            | [] -> Ok [ SecurityMasterEvent.SecurityCreated record ]
            | errors -> Error errors
        | errors -> Error errors

    let amend (current: SecurityMasterRecord) (command: AmendTerms) =
        let nextIdentifiers =
            current.Identifiers
            |> collectExpiredIdentifiers command.IdentifiersToExpire command.EffectiveFrom
            |> fun identifiers -> identifiers @ command.IdentifiersToAdd

        match validateAmend current command nextIdentifiers with
        | [] ->
            let nextRecord: SecurityMasterRecord = {
                current with
                    Common = defaultArg command.Common current.Common
                    Kind = defaultArg command.Kind current.Kind
                    Identifiers = nextIdentifiers
                    Version = current.Version + 1L
                    Provenance = command.Provenance
            }

            match validateRecord nextRecord command.EffectiveFrom with
            | [] -> Ok [ SecurityMasterEvent.TermsAmended(current.Version, nextRecord) ]
            | errors -> Error errors
        | errors -> Error errors

    let deactivate (current: SecurityMasterRecord) (command: DeactivateSecurity) =
        match validateDeactivate current command with
        | [] ->
            Ok [
                SecurityMasterEvent.SecurityDeactivated(
                    current.SecurityId,
                    current.Version + 1L,
                    command.EffectiveTo,
                    command.Provenance)
            ]
        | errors -> Error errors

    let evolve (state: SecurityMasterRecord option) (event: SecurityMasterEvent) =
        SecurityMasterEvent.evolve state event
