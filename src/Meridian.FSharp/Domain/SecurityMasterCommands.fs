namespace Meridian.FSharp.Domain

open System
open System.Text.Json

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

    /// ADR-027 stage 2 (S2-B2): the kind rules whose subject data has NO module home — the exact
    /// complement of definitionExpressibleCodes. The expressible rules moved to
    /// validateDefinitionTerms, which validateKindTerms now composes with this remainder, so the
    /// definition surface is authoritative for every ledgered code on every write while nothing
    /// is checked twice. A rule leaves this function only by gaining a module home and a ledger
    /// entry; the equivalence suite's mutation table keeps the partition honest.
    let private validateCodecOnlyKindTerms (kind: SecurityKind) =
        match kind with
        | SecurityKind.Equity _ -> []
        | SecurityKind.Option _ ->
            []
        | SecurityKind.Future terms ->
            []
            @ requireNotBlank "future_root_symbol_required" "RootSymbol" terms.RootSymbol
            @ requireNotBlank "future_contract_month_required" "ContractMonth" terms.ContractMonth
        | SecurityKind.Bond terms ->
            []
            @ require (not (Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.IsSecuritizedLabel(BondSubclass.label terms.Subclass)))
                (error "bond_securitized_subclass_noncanonical" "Securitized products must be modeled as StructuredCredit, not Bond.")
            @ require (not (Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.IsBankLoanLabel(BondSubclass.label terms.Subclass)))
                (error "bond_bank_loan_subclass_noncanonical" "Bank loans must be modeled as DirectLoan, not Bond.")
            @ require (terms.PrincipalSchedule |> List.forall (fun entry -> entry.Amount > 0m))
                (error "bond_principal_schedule_invalid" "Bond principal schedule amounts must be greater than zero.")
            @ require
                (terms.PrincipalSchedule
                 |> List.forall (fun entry ->
                     entry.PaymentDate <= terms.Maturity
                     && (terms.IssueDate |> Option.forall (fun issueDate -> entry.PaymentDate >= issueDate))))
                (error
                    "bond_principal_schedule_date_invalid"
                    "Bond principal schedule dates must fall on or after IssueDate (when present) and on or before Maturity — out-of-term instalments are silently dropped by cash-flow projections instead of paying down principal.")
            @ require
                (match terms.Par with
                 | Some par -> (terms.PrincipalSchedule |> List.sumBy (fun entry -> entry.Amount)) <= par
                 | None -> true)
                (error
                    "bond_principal_schedule_exceeds_par"
                    "Bond principal schedule amounts must not sum to more than Par — projections cap instalments at the outstanding balance, so an over-committed schedule silently pays less than its persisted amounts.")
            @ require (terms.PrincipalSchedule.IsEmpty || terms.Par.IsSome)
                (error
                    "bond_principal_schedule_requires_par"
                    "Bond principal schedules require Par — without a face value, projections substitute a 100-unit basis and cap instalments against it, silently contradicting the persisted contractual amounts.")
            @ require
                (match terms.Coupon with
                 | BondCouponStructure.Step(schedule, _) -> not schedule.IsEmpty
                 | _ -> true)
                (error
                    "bond_step_schedule_required"
                    "Step-coupon bonds must carry at least one dated step entry — an empty schedule leaves every accrual period without a payable rate.")
            @ require
                (match terms.Coupon with
                 | BondCouponStructure.Step(schedule, _) -> schedule |> List.forall (fun entry -> entry.Rate >= 0m)
                 | _ -> true)
                (error "bond_step_rate_invalid" "Step-coupon rates must be zero or greater.")
            @ require
                (match terms.Coupon with
                 | BondCouponStructure.Step(schedule, _) ->
                     (schedule |> List.map (fun entry -> entry.EffectiveDate) |> List.distinct |> List.length) = schedule.Length
                 | _ -> true)
                (error
                    "bond_step_dates_duplicate"
                    "Step-coupon effective dates must be unique — two rates on one date make the payable coupon depend on input ordering.")
            @ require
                (match terms.Coupon with
                 | BondCouponStructure.Step(schedule, _) ->
                     schedule |> List.forall (fun entry -> entry.EffectiveDate <= terms.Maturity)
                 | _ -> true)
                (error
                    "bond_step_date_after_maturity"
                    "Step-coupon effective dates must fall on or before Maturity — a step past maturity can never take effect.")
            @ require
                (match terms.Coupon with
                 | BondCouponStructure.InflationLinked(realRate, _, _, _, _) -> realRate >= 0m
                 | _ -> true)
                (error "bond_inflation_real_rate_invalid" "Inflation-linked real coupon rate must be zero or greater.")
            @ require
                (match terms.Coupon with
                 | BondCouponStructure.InflationLinked(_, indexName, _, _, _) -> not (String.IsNullOrWhiteSpace indexName)
                 | _ -> true)
                (error "bond_inflation_index_required" "Inflation-linked bonds must name their reference index (e.g. CPI-U).")
            @ require
                (match terms.Coupon with
                 | BondCouponStructure.InflationLinked(_, _, baseIndexValue, indexRatio, _) ->
                     (baseIndexValue |> Option.forall (fun value -> value > 0m))
                     && (indexRatio |> Option.forall (fun ratio -> ratio > 0m))
                 | _ -> true)
                (error
                    "bond_inflation_index_values_invalid"
                    "Inflation-linked base index value and index ratio must be greater than zero when present — a non-positive ratio zeroes or inverts index-adjusted principal.")
        | SecurityKind.FxSpot _ ->
            []
        | SecurityKind.Deposit terms ->
            []
        | SecurityKind.MoneyMarketFund terms ->
            []
        | SecurityKind.CertificateOfDeposit terms ->
            []
        | SecurityKind.CommercialPaper terms ->
            []
        | SecurityKind.TreasuryBill terms ->
            []
        | SecurityKind.Repo terms ->
            []
        | SecurityKind.CashSweep terms ->
            []
        | SecurityKind.OtherSecurity terms ->
            []
            @ requireNotBlank "other_security_category_required" "Category" terms.Category
            @ require (not (Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.IsSecuritizedLabel terms.Category))
                (error "other_security_securitized_noncanonical" "Securitized products must be modeled as StructuredCredit, not OtherSecurity.")
        | SecurityKind.Swap terms ->
            []
            @ require (not terms.Legs.IsEmpty) (error "swap_legs_required" "Swap definitions must include at least one leg.")
            @ (terms.Legs
               |> List.collect (fun leg ->
                   []
                   @ requireNotBlank "swap_leg_type_required" "SwapLeg.LegType" leg.LegType
                   @ requireNotBlank "swap_leg_currency_required" "SwapLeg.Currency" leg.Currency
                   @ require (leg.Notional |> Option.forall (fun notional -> notional > 0m))
                       (error "swap_leg_notional_invalid" "SwapLeg Notional must be greater than zero when present.")
                   @ require
                       (leg.Direction
                        |> Option.forall (fun direction ->
                            String.Equals(direction, "Pay", StringComparison.OrdinalIgnoreCase)
                            || String.Equals(direction, "Receive", StringComparison.OrdinalIgnoreCase)))
                       (error "swap_leg_direction_invalid" "SwapLeg Direction must be either 'Pay' or 'Receive' when present.")
                   @ require (leg.PaymentFrequency |> Option.forall (String.IsNullOrWhiteSpace >> not))
                       (error "swap_leg_payment_frequency_invalid" "SwapLeg PaymentFrequency must not be blank when present.")
                   @ require (leg.NotionalSchedule |> List.forall (fun step -> step.Notional > 0m))
                       (error "swap_leg_notional_schedule_invalid" "SwapLeg NotionalSchedule steps must have a notional greater than zero.")
                   @ require
                       (leg.NotionalSchedule
                        |> List.pairwise
                        |> List.forall (fun (earlier, later) -> earlier.EffectiveDate < later.EffectiveDate))
                       (error "swap_leg_notional_schedule_order_invalid" "SwapLeg NotionalSchedule dates must be strictly ascending.")
                   @ require
                       (leg.NotionalSchedule
                        |> List.forall (fun step -> step.EffectiveDate >= terms.EffectiveDate && step.EffectiveDate <= terms.MaturityDate))
                       (error "swap_leg_notional_schedule_range_invalid" "SwapLeg NotionalSchedule dates must fall within the swap's effective and maturity dates.")
                   @ require
                       (leg.ResetSchedule
                        |> List.pairwise
                        |> List.forall (fun (earlier, later) -> earlier.ResetDate < later.ResetDate))
                       (error "swap_leg_reset_schedule_order_invalid" "SwapLeg ResetSchedule dates must be strictly ascending.")))
        | SecurityKind.DirectLoan terms ->
            []
            @ (terms.Covenants
               |> List.collect (fun covenant ->
                   []
                   @ requireNotBlank "covenant_type_required" "CovenantType" covenant.CovenantType
                   @ requireNotBlank "covenant_threshold_required" "Threshold" covenant.Threshold))
        | SecurityKind.StructuredCredit terms ->
            []
            @ require (terms.AttachmentPoint |> Option.forall (fun point -> point >= 0m && point <= 1m))
                (error "structured_credit_attachment_point_invalid" "StructuredCredit AttachmentPoint must be between zero and one when present — it is a fraction of the deal balance, not a percentage.")
            @ require (terms.DetachmentPoint |> Option.forall (fun point -> point >= 0m && point <= 1m))
                (error "structured_credit_detachment_point_invalid" "StructuredCredit DetachmentPoint must be between zero and one when present — it is a fraction of the deal balance, not a percentage.")
            @ require
                (match terms.AttachmentPoint, terms.DetachmentPoint with
                 | Some attachment, Some detachment -> attachment <= detachment
                 | _ -> true)
                (error "structured_credit_attachment_above_detachment" "StructuredCredit AttachmentPoint must not exceed DetachmentPoint — the tranche absorbs losses from attachment up to detachment.")
            @ require
                (match terms.Maturity with
                 | Some maturity ->
                    (terms.ReinvestmentPeriodEnd |> Option.forall (fun date -> date <= maturity))
                    && (terms.NonCallPeriodEnd |> Option.forall (fun date -> date <= maturity))
                 | None -> true)
                (error "structured_credit_period_after_maturity" "StructuredCredit reinvestment and non-call period ends must fall on or before the retained maturity.")
        | SecurityKind.PrivateFundInterest terms ->
            []
            @ require (terms.Vintage > 0) (error "private_fund_vintage_invalid" "PrivateFundInterest Vintage must be greater than zero.")
        | SecurityKind.PrivateCompanyEquity terms ->
            []
            @ require (terms.OwnershipPercent |> Option.forall (fun percent -> percent >= 0m))
                (error "private_company_ownership_invalid" "PrivateCompanyEquity OwnershipPercent must be zero or greater when present.")
            @ require (terms.CostBasis > 0m) (error "private_company_cost_basis_invalid" "PrivateCompanyEquity CostBasis must be greater than zero.")
            @ require (terms.LatestValuation |> Option.forall (fun amount -> amount >= 0m))
                (error "private_company_valuation_invalid" "PrivateCompanyEquity LatestValuation must be zero or greater when present.")
        | SecurityKind.RealEstateHolding terms ->
            []
            @ requireNotBlank "real_estate_market_required" "AddressOrMarket" terms.AddressOrMarket
            @ require (terms.OwnershipPercent > 0m) (error "real_estate_ownership_invalid" "RealEstateHolding OwnershipPercent must be greater than zero.")
            @ require (terms.AppraisalValue > 0m) (error "real_estate_appraisal_invalid" "RealEstateHolding AppraisalValue must be greater than zero.")
        | SecurityKind.CommitmentGuarantee terms ->
            []
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
        | SecurityKind.CryptoCurrency _ ->
            []
        | SecurityKind.Cfd terms ->
            []
            @ requireNotBlank "cfd_underlying_asset_class_required" "UnderlyingAssetClass" terms.UnderlyingAssetClass
        | SecurityKind.Warrant _ ->
            []
        | SecurityKind.FxForward _ ->
            []
        | SecurityKind.NonDeliverableForward _ ->
            []
        | SecurityKind.CreditDefaultSwap terms ->
            []
            @ requireNotBlank "cds_reference_entity_required" "ReferenceEntity" terms.ReferenceEntity
            @ require
                (String.Equals(terms.ProtectionSide, "Buy", StringComparison.OrdinalIgnoreCase)
                 || String.Equals(terms.ProtectionSide, "Sell", StringComparison.OrdinalIgnoreCase))
                (error "cds_protection_side_invalid" "CreditDefaultSwap ProtectionSide must be either 'Buy' or 'Sell'.")
            @ require (terms.RecoveryRateAssumption |> Option.forall (fun r -> r >= 0m && r <= 1m))
                (error "cds_recovery_rate_invalid" "CreditDefaultSwap RecoveryRateAssumption must be between 0 and 1 when present.")
        | SecurityKind.Swaption terms ->
            []
            @ require (terms.Expiry <= terms.UnderlyingMaturityDate)
                (error "swaption_expiry_invalid" "Swaption Expiry must be on or before UnderlyingMaturityDate.")
            @ require
                (terms.UnderlyingEffectiveDate
                 |> Option.forall (fun effective -> terms.Expiry <= effective && effective < terms.UnderlyingMaturityDate))
                (error "swaption_underlying_dates_invalid" "Swaption UnderlyingEffectiveDate must fall on or after Expiry and before UnderlyingMaturityDate when present.")
        | SecurityKind.CapFloor _ ->
            []
        | SecurityKind.StructuredNote terms ->
            []
            @ requireNotBlank "structured_note_underlying_required" "UnderlyingDescription" terms.UnderlyingDescription
            @ require (terms.PrincipalProtectionPct |> Option.forall (fun p -> p >= 0m && p <= 1m))
                (error "structured_note_protection_invalid" "StructuredNote PrincipalProtectionPct must be between 0 and 1 when present.")
            @ require (terms.ParticipationRate |> Option.forall (fun p -> p >= 0m))
                (error "structured_note_participation_invalid" "StructuredNote ParticipationRate must be zero or greater when present.")
            @ require (terms.ReturnCap |> Option.forall (fun c -> c >= 0m))
                (error "structured_note_return_cap_invalid" "StructuredNote ReturnCap must be zero or greater when present.")
            @ require (terms.BarrierLevel |> Option.forall (fun b -> b > 0m))
                (error "structured_note_barrier_invalid" "StructuredNote BarrierLevel must be greater than zero when present.")
        | SecurityKind.InvestmentFund _ ->
            []
        | SecurityKind.CustomAsset _ ->
            []

    /// The CustomAsset envelope rules, over the three values CustomAssetTerms and the
    /// profile-envelope module both carry. ADR-027 stage 3 (S3-E4) moved them onto the
    /// definition surface when the envelope gained its module home.
    let private validateProfileEnvelope (customProfileId: string) (profileVersion: int) (termsJson: string) =
        // Write-path envelope validation: the DOCUMENT must declare the governance envelope the
        // schema requires (numeric profileVersion, object-valued profileFields), so a tolerant
        // read-side default can never let an incomplete envelope become a canonical record.
        // Reads stay tolerant — this validation runs only on create/amend commands.
        let envelopeErrors =
            if String.IsNullOrWhiteSpace termsJson then
                []
            else
                try
                    use document = JsonDocument.Parse termsJson
                    let root = document.RootElement
                    if root.ValueKind <> JsonValueKind.Object then
                        [ error "custom_asset_terms_invalid" "CustomAsset terms must be a JSON object." ]
                    else
                        []
                        @ (let namesSecuritizedProduct (element: JsonElement) =
                               [ "category"; "subType"; "accountingClassification" ]
                               |> List.exists (fun key ->
                                   match element.TryGetProperty key with
                                   | true, value when value.ValueKind = JsonValueKind.String ->
                                       Meridian.Contracts.SecurityMaster.SecurityCanonicalAssetHomes.IsSecuritizedLabel(value.GetString())
                                   | _ -> false)
                           let noncanonical =
                               namesSecuritizedProduct root
                               || (match root.TryGetProperty "profileFields" with
                                   | true, fields when fields.ValueKind = JsonValueKind.Object -> namesSecuritizedProduct fields
                                   | _ -> false)
                           let resolvesToStructuredCredit =
                               not (String.IsNullOrWhiteSpace customProfileId)
                               && (match Meridian.Contracts.SecurityMaster.SecurityAssetPackRegistry.ProfileAssetClasses.TryGetValue customProfileId with
                                   | true, assetClass -> assetClass = "StructuredCredit"
                                   | _ -> false)
                           require (not noncanonical || resolvesToStructuredCredit)
                               (error "custom_asset_securitized_noncanonical" "Securitized products must resolve to StructuredCredit through a reclassifying profile or a first-class StructuredCredit definition."))
                        @ (match root.TryGetProperty "profileVersion" with
                           | true, value when value.ValueKind = JsonValueKind.Number ->
                               // A JSON number is not enough: a fractional value such as 1.5
                               // would pass here, persist verbatim in the canonical document,
                               // and then read back through the tolerant default as version 1.
                               // Require an actual positive integer on the write path.
                               (match value.TryGetInt32() with
                                | true, version when version > 0 -> []
                                | _ ->
                                    [ error
                                          "custom_asset_profile_version_invalid"
                                          "CustomAsset terms profileVersion must be a positive integer." ])
                           | _ -> [ error "custom_asset_profile_version_missing" "CustomAsset terms must declare a numeric profileVersion." ])
                        @ (match root.TryGetProperty "profileFields" with
                           | true, value when value.ValueKind = JsonValueKind.Object -> []
                           | _ -> [ error "custom_asset_profile_fields_missing" "CustomAsset terms must declare an object-valued profileFields." ])
                with :? JsonException ->
                    [ error "custom_asset_terms_invalid" "CustomAsset terms must be valid JSON." ]

        []
        @ requireNotBlank "custom_asset_profile_required" "CustomProfileId" customProfileId
        @ require (profileVersion > 0)
            (error "custom_asset_profile_version_invalid" "CustomAsset ProfileVersion must be greater than zero.")
        @ requireNotBlank "custom_asset_terms_required" "TermsJson" termsJson
        @ envelopeErrors

    // ── ADR-027 stage 2 (S2-B1): the validation surface over the economic definition ────────────
    //
    // validateDefinitionTerms re-expresses, over SecurityTermModules, every validateKindTerms rule
    // whose subject data has a module home under SecurityMasterLegacyUpgrade.termsFromKind. Rules
    // are gated by the classification's TypeName exactly as validateKindTerms is gated by the kind
    // case, so the two validators agree code-for-code on the expressible set — the equivalence the
    // stage-2 tests pin per class. Rules whose data has no module home (a swap's legs, a bond's
    // subclass/par/principal schedule) stay with the codec-side kind validation
    // (validateCodecOnlyKindTerms); that remainder shrinks as modules gain homes, which S3-E
    // does theme by theme.

    let private moduleString (value: string option) = value |> Option.defaultValue String.Empty

    let private validateDefinitionTerms (typeName: string) (modules: SecurityTermModules) =
        let maturityDate = modules.Maturity |> Option.bind (fun m -> m.MaturityDate)
        let effectiveDate = modules.Maturity |> Option.bind (fun m -> m.EffectiveDate)
        let issueDate = modules.Maturity |> Option.bind (fun m -> m.IssueDate)
        let couponRate = modules.Coupon |> Option.bind (fun c -> c.CouponRate)
        let discountRate = modules.Discount |> Option.bind (fun d -> d.DiscountRate)
        let yieldRate = modules.Discount |> Option.bind (fun d -> d.YieldRate)
        let issuerName = modules.Issuer |> Option.bind (fun i -> i.IssuerName)
        let requireOrderedWhenBoth earlier later validationError =
            require
                (match earlier, later with
                 | Some earlierDate, Some laterDate -> earlierDate <= laterDate
                 | _ -> true)
                validationError
        let contractMultiplier = modules.TradingParameters |> Option.bind (fun t -> t.ContractMultiplier)
        let optionRight = moduleString (modules.Exercise |> Option.bind (fun e -> e.OptionRight))
        let strikePrice = modules.Exercise |> Option.bind (fun e -> e.StrikePrice)
        let isOneOf (values: string list) (value: string) =
            values |> List.exists (fun candidate -> String.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
        let baseCurrency = moduleString (modules.CurrencyPair |> Option.bind (fun p -> p.BaseCurrency))
        let quoteCurrency = moduleString (modules.CurrencyPair |> Option.bind (fun p -> p.QuoteCurrency))
        let pairDiffers = not (String.Equals(baseCurrency, quoteCurrency, StringComparison.OrdinalIgnoreCase))
        let forwardRate = modules.CurrencyPair |> Option.bind (fun p -> p.ForwardRate)
        let notional = modules.Notional |> Option.bind (fun n -> n.Notional)
        let committedAmount = modules.Notional |> Option.bind (fun n -> n.CommittedAmount)
        let unfundedAmount = modules.Notional |> Option.bind (fun n -> n.UnfundedAmount)
        match typeName with
        | "FxSpot" ->
            []
            @ requireNotBlank "fx_base_currency_required" "BaseCurrency" baseCurrency
            @ requireNotBlank "fx_quote_currency_required" "QuoteCurrency" quoteCurrency
            @ require pairDiffers (error "fx_currency_pair_invalid" "BaseCurrency and QuoteCurrency must differ.")
        | "CryptoCurrency" ->
            []
            @ requireNotBlank "crypto_base_currency_required" "BaseCurrency" baseCurrency
            @ requireNotBlank "crypto_quote_currency_required" "QuoteCurrency" quoteCurrency
            @ require pairDiffers (error "crypto_currency_pair_invalid" "BaseCurrency and QuoteCurrency must differ.")
        | "Option" ->
            []
            @ requireNotBlank "option_put_call_required" "PutCall" optionRight
            @ require (isOneOf [ "Put"; "Call" ] optionRight)
                (error "option_put_call_invalid" "Option PutCall must be either 'Put' or 'Call'.")
            @ require (strikePrice |> Option.forall (fun strike -> strike > 0m))
                (error "option_strike_invalid" "Option strike must be greater than zero.")
            @ require (contractMultiplier |> Option.forall (fun multiplier -> multiplier > 0m))
                (error "option_multiplier_invalid" "Option multiplier must be greater than zero.")
        | "Future" ->
            []
            @ require (contractMultiplier |> Option.forall (fun multiplier -> multiplier > 0m))
                (error "future_multiplier_invalid" "Future multiplier must be greater than zero.")
        | "Swaption" ->
            []
            @ require (isOneOf [ "Payer"; "Receiver" ] optionRight)
                (error "swaption_payer_receiver_invalid" "Swaption PayerReceiver must be either 'Payer' or 'Receiver'.")
            @ require (modules.Exercise |> Option.bind (fun e -> e.ExerciseStyle) |> Option.forall (isOneOf [ "European"; "American"; "Bermudan" ]))
                (error "swaption_exercise_style_invalid" "Swaption ExerciseStyle must be European, American or Bermudan when present.")
            @ require (notional |> Option.forall (fun amount -> amount > 0m))
                (error "swaption_notional_invalid" "Swaption Notional must be greater than zero.")
        | "Bond" ->
            []
            @ require (couponRate |> Option.forall (fun rate -> rate >= 0m))
                (error "bond_coupon_invalid" "Bond coupon rate must be zero or greater when present.")
        | "CertificateOfDeposit" ->
            []
            @ requireNotBlank "cd_issuer_required" "IssuerName" (moduleString issuerName)
            @ require (couponRate |> Option.forall (fun rate -> rate >= 0m))
                (error "cd_coupon_invalid" "CertificateOfDeposit CouponRate must be zero or greater when present.")
            @ requireOrderedWhenBoth (modules.Call |> Option.bind (fun c -> c.FirstCallDate)) maturityDate
                (error "cd_callable_date_invalid" "CertificateOfDeposit CallableDate must be on or before Maturity when present.")
        | "Deposit" ->
            []
            @ requireNotBlank "deposit_type_required" "DepositType" (moduleString (modules.Issuer |> Option.bind (fun i -> i.IssuerProgram)))
            @ requireNotBlank "deposit_institution_required" "InstitutionName" (moduleString (modules.Issuer |> Option.bind (fun i -> i.InstitutionName)))
            @ require (couponRate |> Option.forall (fun rate -> rate >= 0m))
                (error "deposit_interest_rate_invalid" "Deposit InterestRate must be zero or greater when present.")
        | "MoneyMarketFund" ->
            []
            @ require (modules.Fund |> Option.bind (fun f -> f.WeightedAverageMaturityDays) |> Option.forall (fun days -> days >= 0))
                (error "mmf_wam_invalid" "MoneyMarketFund WeightedAverageMaturityDays must be zero or greater when present.")
        | "CommercialPaper" ->
            []
            @ requireNotBlank "commercial_paper_issuer_required" "IssuerName" (moduleString issuerName)
            @ require (discountRate |> Option.forall (fun rate -> rate >= 0m))
                (error "commercial_paper_discount_rate_invalid" "CommercialPaper DiscountRate must be zero or greater when present.")
        | "TreasuryBill" ->
            []
            @ requireOrderedWhenBoth (modules.Auction |> Option.bind (fun a -> a.AuctionDate)) maturityDate
                (error "treasury_bill_auction_date_invalid" "TreasuryBill AuctionDate must be on or before Maturity when present.")
            @ require (discountRate |> Option.forall (fun rate -> rate >= 0m))
                (error "treasury_bill_discount_rate_invalid" "TreasuryBill DiscountRate must be zero or greater when present.")
        | "Repo" ->
            []
            @ requireNotBlank "repo_counterparty_required" "Counterparty" (moduleString (modules.Financing |> Option.bind (fun f -> f.Counterparty)))
            @ requireOrderedWhenBoth (modules.Financing |> Option.bind (fun f -> f.OpenDate)) (modules.Financing |> Option.bind (fun f -> f.CloseDate))
                (error "repo_date_range_invalid" "Repo StartDate must be on or before EndDate.")
            @ require (yieldRate |> Option.forall (fun rate -> rate >= 0m))
                (error "repo_rate_invalid" "Repo RepoRate must be zero or greater when present.")
            @ require (modules.Financing |> Option.bind (fun f -> f.Haircut) |> Option.forall (fun haircut -> haircut >= 0m))
                (error "repo_haircut_invalid" "Repo Haircut must be zero or greater when present.")
        | "CashSweep" ->
            []
            @ requireNotBlank "cash_sweep_program_required" "ProgramName" (moduleString (modules.Sweep |> Option.bind (fun s -> s.ProgramName)))
            @ requireNotBlank "cash_sweep_vehicle_required" "SweepVehicleType"
                (moduleString (modules.Sweep |> Option.bind (fun s -> s.SweepVehicleType) |> Option.map SweepVehicle.label))
            @ require (yieldRate |> Option.forall (fun rate -> rate >= 0m))
                (error "cash_sweep_yield_rate_invalid" "CashSweep YieldRate must be zero or greater when present.")
        | "StructuredCredit" ->
            let structured = modules.StructuredProduct
            let factorEntries = structured |> Option.map (fun s -> s.FactorScheduleEntries) |> Option.defaultValue []
            []
            @ requireNotBlank "structured_credit_tranche_required" "Tranche" (moduleString (structured |> Option.bind (fun s -> s.TrancheClass)))
            @ requireNotBlank "structured_credit_collateral_required" "CollateralType" (moduleString (structured |> Option.bind (fun s -> s.CollateralType)))
            @ require (structured |> Option.bind (fun s -> s.NotionalBalance) |> Option.forall (fun face -> face > 0m))
                (error "structured_credit_original_face_invalid" "StructuredCredit OriginalFace must be greater than zero.")
            @ require (structured |> Option.bind (fun s -> s.Factor) |> Option.forall (fun factor -> factor >= 0m && factor <= 1m))
                (error "structured_credit_current_factor_invalid" "StructuredCredit CurrentFactor must be between zero and one when present — factors above one project cash flows exceeding the original principal.")
            @ requireNotBlank "structured_credit_coupon_index_required" "CouponOrIndex" (moduleString (modules.FloatingRate |> Option.bind (fun f -> f.ReferenceIndex)))
            @ require (factorEntries |> List.forall (fun entry -> entry.Factor >= 0m && entry.Factor <= 1m))
                (error "structured_credit_factor_schedule_invalid" "StructuredCredit factor schedule factors must be between zero and one — factors above one project cash flows exceeding the original principal.")
            @ require
                (factorEntries |> List.map (fun entry -> entry.AsOfDate) |> List.distinct |> List.length = List.length factorEntries)
                (error
                    "structured_credit_factor_schedule_duplicate_date"
                    "StructuredCredit factor schedule dates must be unique — two factors on the same date make the effective outstanding principal depend on input ordering, and readers keep only one of them.")
            @ require
                (factorEntries
                 |> List.sortBy (fun entry -> entry.AsOfDate)
                 |> List.pairwise
                 |> List.forall (fun (earlier, later) -> later.Factor <= earlier.Factor))
                (error
                    "structured_credit_factor_schedule_not_monotonic"
                    "StructuredCredit factor schedule must be non-increasing in date order — a rising factor would grow outstanding principal, which the factor-paydown workflow treats as invalid retained evidence.")
            @ require
                (match maturityDate with
                 | Some maturity -> factorEntries |> List.forall (fun entry -> entry.AsOfDate <= maturity)
                 | None -> true)
                (error
                    "structured_credit_factor_after_maturity"
                    "StructuredCredit factor schedule observations must fall on or before the retained maturity — a factor decline after the legal final date would emit principal paydowns for a tranche that has already reached term.")
            @ require (structured |> Option.bind (fun s -> s.WeightedAvgCoupon) |> Option.forall (fun coupon -> coupon >= 0m))
                (error "structured_credit_weighted_average_coupon_invalid" "StructuredCredit WeightedAverageCoupon must be zero or greater when present.")
            @ require (structured |> Option.bind (fun s -> s.WeightedAvgMaturityMonths) |> Option.forall (fun months -> months >= 0))
                (error "structured_credit_weighted_average_maturity_invalid" "StructuredCredit WeightedAverageMaturityMonths must be zero or greater when present.")
            @ require (structured |> Option.bind (fun s -> s.WeightedAvgLoanAgeMos) |> Option.forall (fun months -> months >= 0))
                (error "structured_credit_weighted_average_loan_age_invalid" "StructuredCredit WeightedAverageLoanAgeMonths must be zero or greater when present.")
        | "Commodity" ->
            []
            @ require (modules.TradingParameters |> Option.bind (fun t -> t.ContractMultiplier) |> Option.forall (fun size -> size > 0m))
                (error "commodity_contract_size_invalid" "Commodity ContractSize must be greater than zero when present.")
        | "Cfd" ->
            // Leverage maps to MarginRequirementPct = 1/leverage * 100 (zero carried as zero), so
            // leverage > 0 holds exactly when the carried margin percentage is positive.
            []
            @ require (modules.TradingParameters |> Option.bind (fun t -> t.MarginRequirementPct) |> Option.forall (fun margin -> margin > 0m))
                (error "cfd_leverage_invalid" "CFD Leverage must be greater than zero when present.")
        | "Warrant" ->
            []
            @ requireNotBlank "warrant_type_required" "WarrantType" optionRight
            @ require (isOneOf [ "Call"; "Put" ] optionRight)
                (error "warrant_type_invalid" "Warrant WarrantType must be either 'Call' or 'Put'.")
            @ require (strikePrice |> Option.forall (fun strike -> strike > 0m))
                (error "warrant_strike_invalid" "Warrant Strike must be greater than zero when present.")
            @ require (contractMultiplier |> Option.forall (fun multiplier -> multiplier > 0m))
                (error "warrant_multiplier_invalid" "Warrant Multiplier must be greater than zero when present.")
        | "CapFloor" ->
            []
            @ requireNotBlank "cap_floor_reference_index_required" "ReferenceIndex" (moduleString (modules.FloatingRate |> Option.bind (fun f -> f.ReferenceIndex)))
            @ require
                (match modules.FloatingRate |> Option.bind (fun f -> f.FloorRate), modules.FloatingRate |> Option.bind (fun f -> f.CapRate) with
                 | Some floor, Some cap -> floor <= cap
                 | _ -> true)
                (error "cap_floor_strike_order_invalid" "CapFloor FloorStrike must be at or below CapStrike when both are present.")
            @ requireOrderedWhenBoth effectiveDate maturityDate
                (error "cap_floor_date_range_invalid" "CapFloor EffectiveDate must be on or before MaturityDate.")
            @ require (isOneOf [ "Cap"; "Floor"; "Collar" ] optionRight)
                (error "cap_floor_type_invalid" "CapFloor CapFloorType must be Cap, Floor or Collar.")
            @ require (not (isOneOf [ "Cap"; "Collar" ] optionRight) || (modules.FloatingRate |> Option.bind (fun f -> f.CapRate)).IsSome)
                (error "cap_floor_cap_strike_required" "CapFloor CapStrike is required for a Cap or Collar.")
            @ require (not (isOneOf [ "Floor"; "Collar" ] optionRight) || (modules.FloatingRate |> Option.bind (fun f -> f.FloorRate)).IsSome)
                (error "cap_floor_floor_strike_required" "CapFloor FloorStrike is required for a Floor or Collar.")
            @ require (modules.Exercise |> Option.bind (fun e -> e.PremiumAmount) |> Option.forall (fun premium -> premium >= 0m))
                (error "cap_floor_premium_invalid" "CapFloor PremiumAmount must be zero or greater when present.")
            @ require (notional |> Option.forall (fun amount -> amount > 0m))
                (error "cap_floor_notional_invalid" "CapFloor Notional must be greater than zero.")
        | "Swap" ->
            []
            @ requireOrderedWhenBoth effectiveDate maturityDate
                (error "swap_date_range_invalid" "Swap EffectiveDate must be on or before MaturityDate.")
        | "CreditDefaultSwap" ->
            []
            @ requireOrderedWhenBoth effectiveDate maturityDate
                (error "cds_date_range_invalid" "CreditDefaultSwap EffectiveDate must be on or before MaturityDate.")
            @ require (couponRate |> Option.forall (fun rate -> rate >= 0m))
                (error "cds_fixed_coupon_invalid" "CreditDefaultSwap FixedCouponBps must be zero or greater.")
            @ require (notional |> Option.forall (fun amount -> amount > 0m))
                (error "cds_notional_invalid" "CreditDefaultSwap Notional must be greater than zero.")
        | "NonDeliverableForward" ->
            []
            @ requireOrderedWhenBoth effectiveDate maturityDate
                (error "ndf_fixing_date_invalid" "NonDeliverableForward FixingDate must be on or before ValueDate.")
            @ requireNotBlank "ndf_base_currency_required" "BaseCurrency" baseCurrency
            @ requireNotBlank "ndf_quote_currency_required" "QuoteCurrency" quoteCurrency
            @ requireNotBlank "ndf_settlement_currency_required" "SettlementCurrency"
                (moduleString (modules.CurrencyPair |> Option.bind (fun p -> p.SettlementCurrency)))
            @ require pairDiffers (error "ndf_currency_pair_invalid" "NonDeliverableForward BaseCurrency and QuoteCurrency must differ.")
            @ require (forwardRate |> Option.forall (fun rate -> rate > 0m))
                (error "ndf_rate_invalid" "NonDeliverableForward ForwardRate must be greater than zero when present.")
            @ require (notional |> Option.forall (fun amount -> amount > 0m))
                (error "ndf_notional_invalid" "NonDeliverableForward Notional must be greater than zero when present.")
        | "FxForward" ->
            []
            @ requireOrderedWhenBoth effectiveDate maturityDate
                (error "fx_forward_trade_date_invalid" "FxForward TradeDate must be on or before ValueDate when present.")
            @ requireNotBlank "fx_forward_base_currency_required" "BaseCurrency" baseCurrency
            @ requireNotBlank "fx_forward_quote_currency_required" "QuoteCurrency" quoteCurrency
            @ require pairDiffers (error "fx_forward_currency_pair_invalid" "FxForward BaseCurrency and QuoteCurrency must differ.")
            @ require (forwardRate |> Option.forall (fun rate -> rate > 0m))
                (error "fx_forward_rate_invalid" "FxForward ForwardRate must be greater than zero when present.")
            @ require (notional |> Option.forall (fun amount -> amount > 0m))
                (error "fx_forward_notional_invalid" "FxForward Notional must be greater than zero when present.")
        | "StructuredNote" ->
            []
            @ requireNotBlank "structured_note_issuer_required" "IssuerName" (moduleString issuerName)
            @ requireOrderedWhenBoth issueDate maturityDate
                (error "structured_note_issue_date_invalid" "StructuredNote IssueDate must be on or before Maturity when present.")
            @ require (couponRate |> Option.forall (fun rate -> rate >= 0m))
                (error "structured_note_coupon_invalid" "StructuredNote CouponRate must be zero or greater when present.")
            @ require (modules.Notional |> Option.bind (fun n -> n.Denomination) |> Option.forall (fun denomination -> denomination > 0m))
                (error "structured_note_denomination_invalid" "StructuredNote Denomination must be greater than zero when present.")
        | "CommitmentGuarantee" ->
            []
            @ requireNotBlank "commitment_guarantee_counterparty_required" "Counterparty" (moduleString issuerName)
            @ requireOrderedWhenBoth effectiveDate maturityDate
                (error "commitment_guarantee_date_range_invalid" "CommitmentGuarantee EffectiveDate must be on or before ExpiryDate when present.")
            @ require (committedAmount |> Option.forall (fun amount -> amount > 0m))
                (error "commitment_guarantee_amount_invalid" "CommitmentGuarantee CommittedAmount must be greater than zero.")
            @ require (unfundedAmount |> Option.forall (fun amount -> amount >= 0m))
                (error "commitment_guarantee_exposure_invalid" "CommitmentGuarantee UnfundedAmount must be zero or greater when present.")
        | "DirectLoan" ->
            []
            @ requireNotBlank "direct_loan_borrower_required" "Borrower" (moduleString issuerName)
        | "PrivateFundInterest" ->
            []
            @ requireNotBlank "private_fund_gp_required" "GpSponsor" (moduleString issuerName)
            @ requireNotBlank "private_fund_strategy_required" "Strategy" (moduleString (modules.Issuer |> Option.bind (fun i -> i.IssuerProgram)))
            @ require (committedAmount |> Option.forall (fun amount -> amount > 0m))
                (error "private_fund_commitment_invalid" "PrivateFundInterest Commitment must be greater than zero.")
            @ require (modules.Notional |> Option.bind (fun n -> n.FundedAmount) |> Option.forall (fun amount -> amount >= 0m))
                (error "private_fund_funded_invalid" "PrivateFundInterest FundedAmount must be zero or greater when present.")
            @ require (unfundedAmount |> Option.forall (fun amount -> amount >= 0m))
                (error "private_fund_unfunded_invalid" "PrivateFundInterest UnfundedAmount must be zero or greater when present.")
        | "PrivateCompanyEquity" ->
            []
            @ requireNotBlank "private_company_issuer_required" "Issuer" (moduleString issuerName)
            @ requireNotBlank "private_company_share_class_required" "ShareClass" (moduleString (modules.EquityBehavior |> Option.bind (fun e -> e.ShareClass)))
            @ requireNotBlank "private_company_round_required" "Round" (moduleString (modules.Issuer |> Option.bind (fun i -> i.IssuerProgram)))
        | "RealEstateHolding" ->
            []
            @ requireNotBlank "real_estate_property_type_required" "PropertyType" (moduleString (modules.Issuer |> Option.bind (fun i -> i.IssuerProgram)))
        | "CustomAsset" ->
            // A definition without the module fails exactly as an empty envelope would.
            match modules.ProfileEnvelope with
            | Some envelope -> validateProfileEnvelope envelope.CustomProfileId envelope.ProfileVersion envelope.TermsJson
            | None -> validateProfileEnvelope String.Empty 0 String.Empty
        | _ -> []

    /// ADR-027 stage 2 (S2-B2): the kind rule set, recomposed. The definition surface
    /// (validateDefinitionTerms over modulesFromKind) is authoritative for every
    /// module-expressible code, and validateCodecOnlyKindTerms keeps the rules whose data has no
    /// module home — the exact ledger partition, so nothing is checked twice and nothing is
    /// dropped. Every command path (create, amend, record validation, the facade's standalone
    /// checks) now validates through this composition.
    let private validateKindTerms (kind: SecurityKind) =
        validateDefinitionTerms (AssetClassRegistry.assetClassName kind) (SecurityMasterLegacyUpgrade.modulesFromKind kind)
        @ validateCodecOnlyKindTerms kind

    let private validateKind (kind: SecurityKind) =
        let dayCounts =
            match kind with
            | SecurityKind.Bond terms -> [ BondTerms.dayCount terms ]
            | SecurityKind.Deposit terms -> [ terms.DayCount ]
            | SecurityKind.CertificateOfDeposit terms -> [ terms.DayCount ]
            | SecurityKind.CommercialPaper terms -> [ terms.DayCount ]
            | SecurityKind.Swap terms -> terms.Legs |> List.map (fun leg -> leg.DayCount)
            | SecurityKind.CreditDefaultSwap terms -> [ terms.DayCount ]
            | SecurityKind.Swaption terms -> [ terms.FixedLegDayCount ]
            | SecurityKind.CapFloor terms -> [ terms.DayCount ]
            | _ -> []
        validateKindTerms kind
        @ require
            (dayCounts |> List.forall (Option.forall (fun value ->
                Enum.IsDefined value && value <> Meridian.Contracts.SecurityMaster.DayCountConvention.Unknown)))
            (error "day_count_invalid" "Supplied day-count conventions must name a supported convention.")

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
                SecurityIdentifier.normalizeIdentifierValue identifier.Kind identifier.Value,
                identifier |> SecurityIdentifier.provider |> Option.defaultValue String.Empty |> SecurityIdentifier.normalizeValue)
            |> List.filter (fun (_, count) -> count > 1)
            |> List.length

        // UPI and Markit RED codes name a product or reference entity shared by many contracts;
        // as a primary identifier the normalized primary-identifier unique index (migration 032)
        // would refuse the second contract, so creates and amends refuse it here.
        let sharedReferencePrimary =
            activeIdentifiers
            |> List.exists (fun identifier ->
                identifier.IsPrimary
                && Meridian.Contracts.SecurityMaster.SecurityIdentifierNormalizer.IsSharedReferenceKind(
                    SecurityIdentifier.toContractKind identifier.Kind))

        []
        @ require (primaryCount = 1)
            (error "primary_identifier_invalid" "Active security identifiers must contain exactly one primary identifier.")
        @ require (not sharedReferencePrimary)
            (error
                "primary_identifier_shared_reference"
                "A UPI or Markit RED code is shared by many contracts and cannot be the primary identifier; mark a contract-level identifier as primary.")
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

    /// Standalone kind-invariant validation for the C# write path: a profile-backed record
    /// reclassified to its resolved first-class kind must satisfy that kind's rule set too, which
    /// can be stricter than the pinned profile's (e.g. PrivateFundInterest requires a strictly
    /// positive commitment while the profile's field range starts at zero).
    let validateKindInvariants (kind: SecurityKind) = validateKind kind

    /// Standalone definition-invariant validation for the stage-2 equivalence tests (and, from
    /// S2-B2, the command write path): the module-expressible subset of the kind rule set, gated
    /// by the classification's canonical TypeName.
    let validateDefinitionInvariants (typeName: string) (modules: SecurityTermModules) =
        validateDefinitionTerms typeName modules

    /// Per-class ledger of validateKindTerms error codes the definition can express today. Codes
    /// absent for a listed class, and every code of an unlisted class, are codec-only: their
    /// subject data has no module home yet, so they stay with validateCodecOnlyKindTerms at the
    /// codec boundary and move here only when a module gains the home (ADR-027 stage 3, S3-E,
    /// moves them theme by theme).
    let definitionExpressibleCodes : Map<string, string list> =
        Map.ofList [
            "Option", [ "option_put_call_required"; "option_put_call_invalid"; "option_strike_invalid"; "option_multiplier_invalid" ]
            "Future", [ "future_multiplier_invalid" ]
            "Bond", [ "bond_coupon_invalid" ]
            "CertificateOfDeposit", [ "cd_issuer_required"; "cd_coupon_invalid"; "cd_callable_date_invalid" ]
            "Deposit", [ "deposit_type_required"; "deposit_institution_required"; "deposit_interest_rate_invalid" ]
            "MoneyMarketFund", [ "mmf_wam_invalid" ]
            "CommercialPaper", [ "commercial_paper_issuer_required"; "commercial_paper_discount_rate_invalid" ]
            "TreasuryBill", [ "treasury_bill_auction_date_invalid"; "treasury_bill_discount_rate_invalid" ]
            "Repo", [ "repo_counterparty_required"; "repo_date_range_invalid"; "repo_rate_invalid"; "repo_haircut_invalid" ]
            "CashSweep", [ "cash_sweep_program_required"; "cash_sweep_vehicle_required"; "cash_sweep_yield_rate_invalid" ]
            "StructuredCredit",
            [ "structured_credit_tranche_required"; "structured_credit_collateral_required"
              "structured_credit_original_face_invalid"; "structured_credit_current_factor_invalid"
              "structured_credit_coupon_index_required"; "structured_credit_factor_schedule_invalid"
              "structured_credit_factor_schedule_duplicate_date"; "structured_credit_factor_schedule_not_monotonic"
              "structured_credit_factor_after_maturity"; "structured_credit_weighted_average_coupon_invalid"
              "structured_credit_weighted_average_maturity_invalid"; "structured_credit_weighted_average_loan_age_invalid" ]
            "Commodity", [ "commodity_contract_size_invalid" ]
            "Cfd", [ "cfd_leverage_invalid" ]
            "Warrant", [ "warrant_type_required"; "warrant_type_invalid"; "warrant_strike_invalid"; "warrant_multiplier_invalid" ]
            "Swaption", [ "swaption_payer_receiver_invalid"; "swaption_exercise_style_invalid"; "swaption_notional_invalid" ]
            "CapFloor",
            [ "cap_floor_reference_index_required"; "cap_floor_strike_order_invalid"; "cap_floor_date_range_invalid"
              "cap_floor_type_invalid"; "cap_floor_cap_strike_required"; "cap_floor_floor_strike_required"
              "cap_floor_premium_invalid"; "cap_floor_notional_invalid" ]
            "Swap", [ "swap_date_range_invalid" ]
            "CreditDefaultSwap", [ "cds_date_range_invalid"; "cds_fixed_coupon_invalid"; "cds_notional_invalid" ]
            "FxSpot", [ "fx_base_currency_required"; "fx_quote_currency_required"; "fx_currency_pair_invalid" ]
            "CryptoCurrency", [ "crypto_base_currency_required"; "crypto_quote_currency_required"; "crypto_currency_pair_invalid" ]
            "NonDeliverableForward",
            [ "ndf_fixing_date_invalid"; "ndf_base_currency_required"; "ndf_quote_currency_required"
              "ndf_settlement_currency_required"; "ndf_currency_pair_invalid"; "ndf_rate_invalid"; "ndf_notional_invalid" ]
            "FxForward",
            [ "fx_forward_trade_date_invalid"; "fx_forward_base_currency_required"; "fx_forward_quote_currency_required"
              "fx_forward_currency_pair_invalid"; "fx_forward_rate_invalid"; "fx_forward_notional_invalid" ]
            "StructuredNote",
            [ "structured_note_issuer_required"; "structured_note_issue_date_invalid"; "structured_note_coupon_invalid"
              "structured_note_denomination_invalid" ]
            "CommitmentGuarantee",
            [ "commitment_guarantee_counterparty_required"; "commitment_guarantee_date_range_invalid"
              "commitment_guarantee_amount_invalid"; "commitment_guarantee_exposure_invalid" ]
            "DirectLoan", [ "direct_loan_borrower_required" ]
            "PrivateFundInterest",
            [ "private_fund_gp_required"; "private_fund_strategy_required"; "private_fund_commitment_invalid"
              "private_fund_funded_invalid"; "private_fund_unfunded_invalid" ]
            "PrivateCompanyEquity", [ "private_company_issuer_required"; "private_company_share_class_required"; "private_company_round_required" ]
            "RealEstateHolding", [ "real_estate_property_type_required" ]
            "CustomAsset",
            [ "custom_asset_profile_required"; "custom_asset_profile_version_invalid"; "custom_asset_terms_required"
              "custom_asset_terms_invalid"; "custom_asset_securitized_noncanonical"
              "custom_asset_profile_version_missing"; "custom_asset_profile_fields_missing" ]
        ]

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
