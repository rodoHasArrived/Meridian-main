namespace Meridian.Contracts.SecurityMaster;

/// <summary>How the flat decode reads one bound key. The names follow the schema-bound reader.</summary>
public enum SecurityTermRead
{
    RequiredString,
    OptionalString,
    /// <summary>An optional string read through the convention-aware reader.</summary>
    ConventionString,
    RequiredDecimal,
    OptionalDecimal,
    /// <summary>An optional decimal a write refuses when malformed instead of reading as absent.</summary>
    StrictDecimal,
    RequiredInt,
    OptionalInt,
    StrictInt,
    RequiredDate,
    OptionalDate,
    StrictDate,
    /// <summary>A required guid held as a security id.</summary>
    RequiredSecurityId,
    OptionalBoolean,
    /// <summary>An optional boolean the codec reads and writes as <c>false</c> when absent.</summary>
    BooleanDefaultFalse,
    /// <summary>A day-count convention: read through the governed parser, written as its term label.</summary>
    DayCount,
}

/// <summary>
/// One member of a per-class record and the flat key that carries it.
/// </summary>
/// <param name="Path">
/// The member, as <c>Field.Member</c> over the class's record; or a whole module, as <c>Field</c>,
/// when <paramref name="DecodeExpression"/> builds the module through a helper.
/// </param>
/// <param name="Key">The flat key, or null for a member the decode sets without reading one.</param>
/// <param name="Read">How the decode reads the key; null when <paramref name="DecodeExpression"/> says.</param>
/// <param name="DecodeExpression">
/// The decode expression verbatim, for a member the row derives or fixes instead of reading plainly
/// (a constant issuer, a style parsed through its own vocabulary).
/// </param>
/// <param name="WriteVia">
/// The record's own computed property the writer reads instead of the member, where that property
/// supplies the written form (a required date written as its default when the record lacks it).
/// </param>
/// <param name="DecodeOnly">Whether the key is written by another row and only read here.</param>
/// <param name="WriteOnly">
/// Whether the key is only written here: read by a whole-module row of the same field, or by the
/// decode row of the same member when one member is written as several keys.
/// </param>
/// <param name="WriteExpression">
/// The written value verbatim, as an F# expression over <c>terms</c>, where the written form is a
/// conversion no record property states (a label of a union, one arm of a coupon structure).
/// </param>
/// <param name="Items">
/// For a list member, the keys each written entry carries and the entry members they come from.
/// </param>
public sealed record SecurityTermBinding(
    string Path,
    string? Key,
    SecurityTermRead? Read,
    string? DecodeExpression = null,
    string? WriteVia = null,
    bool DecodeOnly = false,
    bool WriteOnly = false,
    string? WriteExpression = null,
    IReadOnlyList<SecurityListItemBinding>? Items = null);

/// <summary>One key of a written list entry and the entry member it carries.</summary>
public sealed record SecurityListItemBinding(string Key, string Member);

/// <summary>Inputs a class's decode reads beyond the schema-bound terms.</summary>
[Flags]
public enum SecurityCodecInputs
{
    None = 0,
    /// <summary>The row's versioned-term decoding, for versioned containers inside a list entry.</summary>
    VersionedTerms = 1,
    /// <summary>The parsed and validated economic schedules (principal, factor, step coupon).</summary>
    EconomicSchedules = 2,
    /// <summary>The profile-resolved terms document, for a list read from it.</summary>
    ResolvedTermsDocument = 4,
}

/// <summary>The bindings of one asset class.</summary>
/// <param name="ReadsResolvedTerms">
/// Whether the class reads its keys from the profile-resolved terms document rather than the root.
/// </param>
/// <param name="VersionedSchedules">
/// Whether the class carries the declared-schedule containers (ADR-024: the
/// <c>scheduleConventions</c> block and the call and put schedules), which the decode reads only at
/// the row's terms version and the writer writes only from the version that declares them.
/// </param>
/// <param name="Inputs">What else the decode reads; see <see cref="SecurityCodecInputs"/>.</param>
/// <param name="WritesConventionsFlag">
/// Whether the writer takes the schedule-conventions version flag for a container nested in a
/// list entry (a swap leg's block) rather than at the root.
/// </param>
public sealed record SecurityClassCodecBinding(
    string AssetClass,
    bool ReadsResolvedTerms,
    IReadOnlyList<SecurityTermBinding> Members,
    bool VersionedSchedules = false,
    SecurityCodecInputs Inputs = SecurityCodecInputs.None,
    bool WritesConventionsFlag = false);

/// <summary>
/// The single statement of which flat key carries which member of a per-class record, for the
/// classes whose flat codec is generated (ADR-027 stage 3). Both codec arms are generated from
/// this table: the decode arm in <c>SecurityMasterMapping.GeneratedDecode.cs</c> and the writer arm
/// in <c>Interop.SecurityMaster.GeneratedWriter.fs</c>. Neither file is edited by hand;
/// <c>SecurityFlatCodecGenerationTests</c> regenerates both and fails when the committed source
/// differs.
/// <para>
/// A member with no row is one the class's flat row does not carry, and decodes as absent. Key
/// types and requiredness are the schema's (<see cref="SecurityAssetTermsSchema"/>); a row names
/// only what the schema does not say: the member, the reader's tolerance, and the few values a
/// class fixes or derives.
/// </para>
/// </summary>
public static class SecurityAssetTermsCodecBindings
{
    private static SecurityTermBinding B(string path, string key, SecurityTermRead read, string? via = null)
        => new(path, key, read, WriteVia: via);

    /// <summary>A member the decode fixes or derives; nothing is written for it.</summary>
    private static SecurityTermBinding Fixed(string path, string decodeExpression)
        => new(path, null, null, decodeExpression);

    /// <summary>A second member read from a key another row writes.</summary>
    private static SecurityTermBinding Also(string path, string key, SecurityTermRead read)
        => new(path, key, read, DecodeOnly: true);

    /// <summary>A whole module built by a helper; the keys it reads are written by <see cref="WrittenFrom"/> rows.</summary>
    private static SecurityTermBinding Module(string field, string decodeExpression)
        => new(field, null, null, decodeExpression);

    /// <summary>A key written from a record property and read by a <see cref="Module"/> row of the same field.</summary>
    private static SecurityTermBinding WrittenFrom(string path, string key, string property)
        => new(path, key, null, WriteVia: property, DecodeOnly: false, WriteOnly: true);

    /// <summary>A member read through its own conversion and written from a record property.</summary>
    private static SecurityTermBinding Converted(string path, string key, string decodeExpression, string property)
        => new(path, key, null, decodeExpression, property);

    /// <summary>A member read through its own conversion and written as a converted value.</summary>
    private static SecurityTermBinding Computed(string path, string key, string decodeExpression, string writeExpression)
        => new(path, key, null, decodeExpression, WriteExpression: writeExpression);

    /// <summary>
    /// A key computed from a member another row decodes: one of several keys a single member (a
    /// coupon structure) is written as. The decode row of <paramref name="path"/> reads it back.
    /// </summary>
    private static SecurityTermBinding WrittenAs(string path, string key, string writeExpression)
        => new(path, key, null, WriteOnly: true, WriteExpression: writeExpression);

    /// <summary>A list member read by its own decode and written entry by entry.</summary>
    private static SecurityTermBinding Listed(string path, string key, string decodeExpression, string property,
        params (string Key, string Member)[] items)
        => new(path, key, null, decodeExpression, property,
            Items: items.Select(static item => new SecurityListItemBinding(item.Key, item.Member)).ToArray());

    private static SecurityClassCodecBinding Class(string assetClass, params SecurityTermBinding[] members)
        => new(assetClass, false, members);

    private static SecurityClassCodecBinding ScheduledClass(string assetClass, params SecurityTermBinding[] members)
        => new(assetClass, false, members, VersionedSchedules: true);

    private static SecurityClassCodecBinding ResolvedClass(string assetClass, params SecurityTermBinding[] members)
        => new(assetClass, true, members);

    private static readonly (string, string)[] CovenantItems =
        [("covenantType", "CovenantType"), ("threshold", "Threshold"), ("notes", "Notes")];

    private static readonly (string, string)[] PrincipalPaymentItems =
        [("paymentDate", "PaymentDate"), ("amount", "Amount")];

    /// <summary>The generated classes, in catalog order.</summary>
    public static IReadOnlyList<SecurityClassCodecBinding> Classes { get; } =
    [
        Class("Equity",
            B("Capital.ShareClass", "shareClass", SecurityTermRead.OptionalString),
            Computed("Capital.VotingRightsCat", "votingRightsCat",
                "ToVotingRightsCatOption(bound.OptionalString(\"votingRightsCat\"))",
                "terms.Capital.VotingRightsCat |> Option.map VotingRightsCat.asString"),
            // The classification union is written as four keys; classification discriminates and
            // the preferred and convertible blocks carry the dependent terms.
            Fixed("Capital.Classification", "ToEquityClassificationOption(bound, mode)"),
            WrittenAs("Capital.Classification", "classification", "EquityCapitalCodec.classification terms.Capital"),
            WrittenAs("Capital.Classification", "otherClassification", "EquityCapitalCodec.otherClassification terms.Capital"),
            WrittenAs("Capital.Classification", "preferredTerms", "EquityCapitalCodec.preferredTerms terms.Capital"),
            WrittenAs("Capital.Classification", "convertibleTerms", "EquityCapitalCodec.convertibleTerms terms.Capital")),
        Class("Option",
            B("Dates.MaturityDate", "expiry", SecurityTermRead.RequiredDate, via: "Expiry"),
            B("ListingDetail.OptChainId", "optChainId", SecurityTermRead.OptionalString),
            B("ListingDetail.SettlementType", "settlementType", SecurityTermRead.OptionalString),
            B("ListingDetail.IsAdjusted", "isAdjusted", SecurityTermRead.BooleanDefaultFalse),
            B("ListingDetail.LastTradingDt", "lastTradingDt", SecurityTermRead.OptionalDate),
            B("Exercise.OptionRight", "putCall", SecurityTermRead.RequiredString),
            B("Exercise.StrikePrice", "strike", SecurityTermRead.RequiredDecimal),
            new("Exercise.ExerciseStyle", "exerciseStyle", null,
                DecodeExpression: "CanonicalExerciseStyle(ParseExerciseStyle(bound.OptionalString(\"exerciseStyle\")))"),
            B("Exercise.UnderlyingSecurityId", "underlyingId", SecurityTermRead.RequiredSecurityId),
            B("TradingParameters.ContractMultiplier", "multiplier", SecurityTermRead.RequiredDecimal)),
        Class("Future",
            B("Dates.MaturityDate", "expiry", SecurityTermRead.RequiredDate, via: "Expiry"),
            B("ListingDetail.SettlementType", "settlementType", SecurityTermRead.OptionalString),
            B("ListingDetail.LastTradingDt", "lastTradingDt", SecurityTermRead.OptionalDate),
            B("ListingDetail.RootSymbol", "rootSymbol", SecurityTermRead.RequiredString),
            B("ListingDetail.ContractMonth", "contractMonth", SecurityTermRead.RequiredString),
            B("ListingDetail.FirstNoticeDt", "firstNoticeDt", SecurityTermRead.OptionalDate),
            B("ListingDetail.DeliveryMonthDt", "deliveryMonthDt", SecurityTermRead.OptionalDate),
            B("ListingDetail.DeliveryLocationCode", "deliveryLocationCode", SecurityTermRead.OptionalString),
            B("ListingDetail.IsRollTarget", "isRollTarget", SecurityTermRead.BooleanDefaultFalse),
            B("ListingDetail.RollWindowDays", "rollWindowDays", SecurityTermRead.OptionalInt),
            B("TradingParameters.ContractMultiplier", "multiplier", SecurityTermRead.RequiredDecimal)),
        new("Bond", false,
        [
            B("Dates.IssueDate", "issueDate", SecurityTermRead.OptionalDate),
            B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
            // The coupon structure is one union written as ten keys; couponType discriminates.
            Fixed("CouponStructure.Structure", "ToBondCouponStructure(bound, mode, economicSchedules)"),
            WrittenAs("CouponStructure.Structure", "couponType", "FlatWriterParts.couponType terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "couponRate", "BondTerms.couponRate terms"),
            WrittenAs("CouponStructure.Structure", "floatingIndex", "FlatWriterParts.floatingIndex terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "spreadBps", "FlatWriterParts.floatingSpreadBps terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "capRate", "FlatWriterParts.floatingCapRate terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "floorRate", "FlatWriterParts.floatingFloorRate terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "stepSchedule", "FlatWriterParts.stepSchedule terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "inflationIndex", "FlatWriterParts.inflationIndex terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "inflationBaseIndexValue", "FlatWriterParts.inflationBaseIndexValue terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "inflationIndexRatio", "FlatWriterParts.inflationIndexRatio terms.Coupon"),
            WrittenAs("CouponStructure.Structure", "dayCount",
                "BondTerms.dayCount terms |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel"),
            Computed("CouponStructure.PaymentFrequency", "paymentFrequency",
                "ToPaymentFrequencyOption(bound.OptionalString(\"paymentFrequency\"))",
                "terms.PaymentFrequency |> Option.map PaymentFrequency.label"),
            Module("Callability",
                "EconomicCallTermsModule.Scalar(bound.OptionalBoolean(\"isCallable\") ?? false, ToOption(bound.OptionalDateOnly(\"callDate\")))"),
            WrittenFrom("Callability.IsCallable", "isCallable", "IsCallable"),
            WrittenFrom("Callability.FirstCallDate", "callDate", "CallDate"),
            B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.OptionalString),
            Computed("DebtClassification.Subclass", "subclass",
                "ParseBondSubclass(bound.OptionalString(\"subclass\"))", "BondSubclass.label terms.Subclass"),
            B("DebtClassification.Seniority", "seniority", SecurityTermRead.OptionalString),
            B("RedemptionFacts.Par", "par", SecurityTermRead.OptionalDecimal),
            B("RedemptionFacts.LegalFinalMaturity", "legalFinalMaturity", SecurityTermRead.OptionalDate),
            B("RedemptionFacts.PreRefundDate", "preRefundDate", SecurityTermRead.OptionalDate),
            B("RedemptionFacts.MandatoryPutDate", "mandatoryPutDate", SecurityTermRead.OptionalDate),
            Listed("Principal.Payments", "principalSchedule", "PrincipalPayments(economicSchedules)", "PrincipalSchedule",
                PrincipalPaymentItems),
            Module("DeclaredSchedule",
                "new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),\n                ReadCallPutSchedules(json, assetClass, versionedTerms))"),
            // Written after stamping by the snapshot wrapper (absent rather than null when undeclared).
            Module("Workflow", "ReadBondWorkflowTerms(json, versionedTerms)"),
        ], VersionedSchedules: true, Inputs: SecurityCodecInputs.EconomicSchedules),
        Class("FxSpot",
            B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
            B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString)),
        Class("Deposit",
            B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
            B("IssuerTerms.InstitutionName", "institutionName", SecurityTermRead.RequiredString, via: "InstitutionName"),
            B("IssuerTerms.IssuerProgram", "depositType", SecurityTermRead.RequiredString, via: "DepositType"),
            Fixed("CouponTerms.CouponType", "FSharpOption<CouponKind>.Some(CouponKind.NewOtherCoupon(\"SimpleInterest\"))"),
            B("CouponTerms.CouponRate", "interestRate", SecurityTermRead.OptionalDecimal),
            B("CouponTerms.DayCount", "dayCount", SecurityTermRead.DayCount),
            Module("Callability", "EconomicCallTermsModule.Scalar(bound.OptionalBoolean(\"isCallable\") ?? false, null)"),
            WrittenFrom("Callability.IsCallable", "isCallable", "IsCallable")),
        Class("MoneyMarketFund",
            B("FundTerms.FundFamily", "fundFamily", SecurityTermRead.OptionalString),
            B("FundTerms.WeightedAverageMaturityDays", "weightedAverageMaturityDays", SecurityTermRead.OptionalInt),
            B("FundTerms.SweepEligible", "sweepEligible", SecurityTermRead.BooleanDefaultFalse),
            B("FundTerms.LiquidityFeeEligible", "liquidityFeeEligible", SecurityTermRead.BooleanDefaultFalse)),
        ScheduledClass("CertificateOfDeposit",
            B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
            B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.RequiredString, via: "IssuerName"),
            Fixed("CouponTerms.CouponType", "FSharpOption<CouponKind>.Some(CouponKind.Fixed)"),
            B("CouponTerms.CouponRate", "couponRate", SecurityTermRead.OptionalDecimal),
            B("CouponTerms.DayCount", "dayCount", SecurityTermRead.DayCount),
            Module("Callability",
                "EconomicCallTermsModule.Scalar(bound.OptionalDateOnly(\"callableDate\").HasValue, ToOption(bound.OptionalDateOnly(\"callableDate\")))"),
            WrittenFrom("Callability.FirstCallDate", "callableDate", "CallableDate"),
            Module("DeclaredSchedule",
                "new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),\n                ReadCallPutSchedules(json, assetClass, versionedTerms))")),
        Class("CommercialPaper",
            B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
            B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.RequiredString, via: "IssuerName"),
            Converted("IssuerTerms.IssuerProgram", "isAssetBacked",
                "ToOption((bound.OptionalBoolean(\"isAssetBacked\") ?? false) ? \"AssetBacked\" : null)", "IsAssetBacked"),
            B("DiscountTerms.DiscountRate", "discountRate", SecurityTermRead.OptionalDecimal),
            Fixed("CouponTerms.CouponType", "FSharpOption<CouponKind>.Some(CouponKind.DiscountNote)"),
            B("CouponTerms.DayCount", "dayCount", SecurityTermRead.DayCount)),
        Class("TreasuryBill",
            B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
            B("Auction.AuctionDate", "auctionDate", SecurityTermRead.OptionalDate),
            Fixed("IssuerTerms.IssuerName", "ToOption(\"US Treasury\")"),
            B("IssuerTerms.IssuerProgram", "cusip", SecurityTermRead.OptionalString),
            Fixed("IssuerTerms.IssuerCountry", "ToOption(\"US\")"),
            B("DiscountTerms.DiscountRate", "discountRate", SecurityTermRead.OptionalDecimal)),
        Class("Repo",
            B("FinancingTerms.Counterparty", "counterparty", SecurityTermRead.RequiredString, via: "Counterparty"),
            B("FinancingTerms.CollateralType", "collateralType", SecurityTermRead.OptionalString),
            B("FinancingTerms.Haircut", "haircut", SecurityTermRead.OptionalDecimal),
            B("FinancingTerms.OpenDate", "startDate", SecurityTermRead.RequiredDate, via: "StartDate"),
            B("FinancingTerms.CloseDate", "endDate", SecurityTermRead.RequiredDate, via: "EndDate"),
            B("DiscountTerms.YieldRate", "repoRate", SecurityTermRead.OptionalDecimal)),
        Class("CashSweep",
            B("SweepTerms.ProgramName", "programName", SecurityTermRead.RequiredString, via: "ProgramName"),
            Converted("SweepTerms.SweepVehicleType", "sweepVehicleType",
                "FSharpOption<SweepVehicle>.Some(ToCodecSweepVehicle(bound.RequiredString(\"sweepVehicleType\")))", "SweepVehicleType"),
            Converted("SweepTerms.SweepFrequency", "sweepFrequency",
                "ToCodecSweepFrequency(ToOption(bound.OptionalString(\"sweepFrequency\")))", "SweepFrequency"),
            B("SweepTerms.TargetAccountType", "targetAccountType", SecurityTermRead.OptionalString),
            B("DiscountTerms.YieldRate", "yieldRate", SecurityTermRead.OptionalDecimal)),
        Class("OtherSecurity",
            B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
            B("ClassificationDetail.Category", "category", SecurityTermRead.RequiredString),
            B("ClassificationDetail.SubType", "subType", SecurityTermRead.OptionalString),
            B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.OptionalString),
            B("IssuerTerms.IssuerProgram", "settlementType", SecurityTermRead.OptionalString)),
        new("Swap", false,
        [
            B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
            Also("Dates.IssueDate", "effectiveDate", SecurityTermRead.RequiredDate),
            B("Dates.MaturityDate", "maturityDate", SecurityTermRead.RequiredDate, via: "MaturityDate"),
            // Each leg carries its own schedule-conventions block from the version that declares it.
            Computed("SwapLegs.Legs", "legs", "ToSwapLegs(json, assetClass, versionedTerms, mode)",
                "terms.Legs |> List.map (FlatWriterParts.swapLeg writesScheduleConventions)"),
        ], Inputs: SecurityCodecInputs.VersionedTerms, WritesConventionsFlag: true),
        new("DirectLoan", false,
        [
            B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
            B("IssuerTerms.IssuerName", "borrower", SecurityTermRead.RequiredString, via: "Borrower"),
            Listed("LoanCovenants.Entries", "covenants", "ToCovenants(GetRequiredArray(json, \"covenants\").EnumerateArray())",
                "Covenants", CovenantItems),
            B("FloatingRate.ReferenceIndex", "referenceIndex", SecurityTermRead.OptionalString),
            B("FloatingRate.SpreadBps", "spreadBps", SecurityTermRead.OptionalDecimal),
            B("FloatingRate.ResetFrequency", "resetFrequency", SecurityTermRead.OptionalString),
            B("CouponTerms.CouponRate", "currentCouponRate", SecurityTermRead.OptionalDecimal),
            Listed("Principal.Payments", "principalSchedule", "PrincipalPayments(economicSchedules)", "PrincipalSchedule",
                PrincipalPaymentItems),
            B("ReferenceDataSource.PricingSource", "pricingSource", SecurityTermRead.OptionalString),
        ], Inputs: SecurityCodecInputs.EconomicSchedules),
        // Scalar keys from the profile-resolved document; the versioned containers from the root,
        // since a profile envelope decodes none.
        new("StructuredCredit", true,
        [
            Converted("CouponTerms.CouponType", "couponOrIndex",
                "FSharpOption<CouponKind>.Some(CouponKind.NewOtherCoupon(bound.RequiredString(\"couponOrIndex\")))", "CouponOrIndex"),
            Computed("TrancheContract.Kind", "structuredCreditKind",
                "ToStructuredCreditKindOption(bound.OptionalConventionString(\"structuredCreditKind\"))",
                "terms.Kind |> Option.map StructuredCreditKind.asString"),
            B("TrancheContract.Seniority", "seniority", SecurityTermRead.ConventionString),
            B("TrancheContract.AttachmentPoint", "attachmentPoint", SecurityTermRead.StrictDecimal),
            B("TrancheContract.DetachmentPoint", "detachmentPoint", SecurityTermRead.StrictDecimal),
            B("TrancheContract.ReinvestmentPeriodEnd", "reinvestmentPeriodEnd", SecurityTermRead.StrictDate),
            B("TrancheContract.NonCallPeriodEnd", "nonCallPeriodEnd", SecurityTermRead.StrictDate),
            B("Pool.Factor", "currentFactor", SecurityTermRead.OptionalDecimal),
            B("Pool.WeightedAvgCoupon", "weightedAverageCoupon", SecurityTermRead.StrictDecimal),
            B("Pool.WeightedAvgMaturityMonths", "weightedAverageMaturityMonths", SecurityTermRead.StrictInt),
            B("Pool.WeightedAvgLoanAgeMos", "weightedAverageLoanAgeMonths", SecurityTermRead.StrictInt),
            B("Pool.CollateralType", "collateralType", SecurityTermRead.RequiredString, via: "CollateralType"),
            B("Pool.PoolIdentifier", "poolId", SecurityTermRead.OptionalString),
            B("Pool.TrancheClass", "tranche", SecurityTermRead.RequiredString, via: "Tranche"),
            Fixed("Pool.IsInterestOnly", "false"),
            Fixed("Pool.IsPrincipalOnly", "false"),
            B("Pool.NotionalBalance", "originalFace", SecurityTermRead.RequiredDecimal, via: "OriginalFace"),
            B("Pool.FactorScheduleReference", "factorSchedule", SecurityTermRead.OptionalString),
            Listed("Pool.FactorScheduleEntries", "factorScheduleEntries", "FactorScheduleEntries(economicSchedules)",
                "FactorScheduleEntries", ("asOfDate", "AsOfDate"), ("factor", "Factor")),
            B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
            Module("DeclaredSchedule",
                "new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),\n                ReadCallPutSchedules(json, assetClass, versionedTerms))"),
        ], VersionedSchedules: true, Inputs: SecurityCodecInputs.EconomicSchedules),
        ResolvedClass("PrivateFundInterest",
            B("PrivateAssetValuation.Vintage", "vintage", SecurityTermRead.RequiredInt),
            B("PrivateAssetValuation.NavDate", "navDate", SecurityTermRead.RequiredDate),
            B("PrivateAssetValuation.Lockup", "lockup", SecurityTermRead.OptionalString),
            B("Notional.CommittedAmount", "commitment", SecurityTermRead.RequiredDecimal),
            B("Notional.FundedAmount", "fundedAmount", SecurityTermRead.OptionalDecimal),
            B("Notional.UnfundedAmount", "unfundedAmount", SecurityTermRead.OptionalDecimal),
            B("IssuerTerms.IssuerName", "gpSponsor", SecurityTermRead.RequiredString, via: "GpSponsor"),
            B("IssuerTerms.IssuerProgram", "strategy", SecurityTermRead.RequiredString, via: "Strategy")),
        ResolvedClass("PrivateCompanyEquity",
            B("Capital.ShareClass", "shareClass", SecurityTermRead.RequiredString, via: "ShareClass"),
            B("PrivateAssetValuation.OwnershipPercent", "ownershipPercent", SecurityTermRead.OptionalDecimal),
            B("PrivateAssetValuation.CostBasis", "costBasis", SecurityTermRead.RequiredDecimal),
            B("PrivateAssetValuation.LatestValuation", "latestValuation", SecurityTermRead.OptionalDecimal),
            B("PrivateAssetValuation.TransferRestrictions", "transferRestrictions", SecurityTermRead.OptionalString),
            B("IssuerTerms.IssuerName", "issuer", SecurityTermRead.RequiredString, via: "Issuer"),
            B("IssuerTerms.IssuerProgram", "round", SecurityTermRead.RequiredString, via: "Round")),
        ResolvedClass("RealEstateHolding",
            B("PrivateAssetValuation.OwnershipPercent", "ownershipPercent", SecurityTermRead.RequiredDecimal, via: "PrivateAssetValuation.OwnershipPercent"),
            B("PrivateAssetValuation.AddressOrMarket", "addressOrMarket", SecurityTermRead.RequiredString),
            B("PrivateAssetValuation.AppraisalValue", "appraisalValue", SecurityTermRead.RequiredDecimal),
            B("PrivateAssetValuation.ValuationDate", "valuationDate", SecurityTermRead.RequiredDate),
            B("PrivateAssetValuation.DebtStack", "debtStack", SecurityTermRead.OptionalString),
            B("IssuerTerms.IssuerName", "sponsor", SecurityTermRead.OptionalString),
            B("IssuerTerms.IssuerProgram", "propertyType", SecurityTermRead.RequiredString, via: "PropertyType"),
            Fixed("IssuerTerms.IssuerSector", "ToOption(\"RealEstate\")")),
        new("CommitmentGuarantee", true,
        [
            B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
            Also("Dates.IssueDate", "effectiveDate", SecurityTermRead.RequiredDate),
            B("Dates.MaturityDate", "expiryDate", SecurityTermRead.OptionalDate),
            B("Notional.CommittedAmount", "committedAmount", SecurityTermRead.RequiredDecimal),
            B("Notional.UnfundedAmount", "unfundedAmount", SecurityTermRead.OptionalDecimal),
            B("IssuerTerms.IssuerName", "counterparty", SecurityTermRead.RequiredString, via: "Counterparty"),
            B("IssuerTerms.IssuerProgram", "beneficiary", SecurityTermRead.OptionalString),
            B("ContractualRate.FeeRate", "feeRate", SecurityTermRead.OptionalDecimal),
            B("CollateralTerms.CollateralType", "collateral", SecurityTermRead.OptionalString),
            Listed("LoanCovenants.Entries", "covenants", "ToCovenants(GetOptionalArrayItems(terms, \"covenants\"))",
                "Covenants", CovenantItems),
        ], Inputs: SecurityCodecInputs.ResolvedTermsDocument),
        Class("Commodity",
            B("CommodityContract.CommodityType", "commodityType", SecurityTermRead.RequiredString),
            B("CommodityContract.Denomination", "denomination", SecurityTermRead.OptionalString),
            B("CommodityContract.ContractSize", "contractSize", SecurityTermRead.OptionalDecimal)),
        Class("CryptoCurrency",
            B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
            B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString),
            B("DigitalAsset.Network", "network", SecurityTermRead.OptionalString)),
        Class("Cfd",
            B("UnderlyingExposure.UnderlyingAssetClass", "underlyingAssetClass", SecurityTermRead.RequiredString),
            B("UnderlyingExposure.UnderlyingDescription", "underlyingDescription", SecurityTermRead.OptionalString),
            B("UnderlyingExposure.Leverage", "leverage", SecurityTermRead.OptionalDecimal)),
        Class("Warrant",
            B("Dates.MaturityDate", "expiry", SecurityTermRead.OptionalDate),
            B("Exercise.OptionRight", "warrantType", SecurityTermRead.RequiredString),
            B("Exercise.StrikePrice", "strike", SecurityTermRead.OptionalDecimal),
            B("Exercise.UnderlyingSecurityId", "underlyingId", SecurityTermRead.RequiredSecurityId),
            B("TradingParameters.ContractMultiplier", "multiplier", SecurityTermRead.OptionalDecimal)),
        Class("FxForward",
            B("Dates.EffectiveDate", "tradeDate", SecurityTermRead.StrictDate),
            B("Dates.MaturityDate", "valueDate", SecurityTermRead.RequiredDate, via: "ValueDate"),
            B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
            B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString),
            B("CurrencyPair.ForwardRate", "forwardRate", SecurityTermRead.StrictDecimal),
            B("Notional.Notional", "notional", SecurityTermRead.StrictDecimal),
            B("Settlement.SettlementType", "settlementType", SecurityTermRead.ConventionString)),
        Class("NonDeliverableForward",
            B("Dates.EffectiveDate", "fixingDate", SecurityTermRead.RequiredDate, via: "FixingDate"),
            B("Dates.MaturityDate", "valueDate", SecurityTermRead.RequiredDate, via: "ValueDate"),
            B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
            B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString),
            B("CurrencyPair.SettlementCurrency", "settlementCurrency", SecurityTermRead.RequiredString),
            B("CurrencyPair.ForwardRate", "forwardRate", SecurityTermRead.StrictDecimal),
            B("Notional.Notional", "notional", SecurityTermRead.StrictDecimal),
            B("ReferenceDataSource.FixingSource", "fixingSource", SecurityTermRead.ConventionString)),
        Class("CreditDefaultSwap",
            B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
            Also("Dates.IssueDate", "effectiveDate", SecurityTermRead.RequiredDate),
            B("Dates.MaturityDate", "maturityDate", SecurityTermRead.RequiredDate, via: "MaturityDate"),
            B("CreditProtection.ReferenceEntity", "referenceEntity", SecurityTermRead.RequiredString),
            B("CreditProtection.ReferenceObligation", "referenceObligation", SecurityTermRead.ConventionString),
            B("CreditProtection.ProtectionSide", "protectionSide", SecurityTermRead.RequiredString),
            B("CreditProtection.Seniority", "seniority", SecurityTermRead.ConventionString),
            B("CreditProtection.RestructuringClause", "restructuringClause", SecurityTermRead.ConventionString),
            B("CreditProtection.UpfrontPoints", "upfrontPoints", SecurityTermRead.StrictDecimal),
            B("CreditProtection.RecoveryRateAssumption", "recoveryRateAssumption", SecurityTermRead.StrictDecimal),
            B("CreditProtection.IndexName", "indexName", SecurityTermRead.ConventionString),
            B("Notional.Notional", "notional", SecurityTermRead.RequiredDecimal),
            B("ContractualRate.FixedCouponBps", "fixedCouponBps", SecurityTermRead.RequiredDecimal, via: "FixedCouponBps"),
            B("ContractualRate.PaymentFrequency", "paymentFrequency", SecurityTermRead.ConventionString),
            B("ContractualRate.DayCount", "dayCount", SecurityTermRead.DayCount)),
        Class("Swaption",
            B("Dates.EffectiveDate", "underlyingEffectiveDate", SecurityTermRead.StrictDate),
            B("Dates.MaturityDate", "expiry", SecurityTermRead.RequiredDate, via: "Expiry"),
            B("Exercise.OptionRight", "payerReceiver", SecurityTermRead.RequiredString),
            B("Exercise.StrikeRate", "strikeRate", SecurityTermRead.RequiredDecimal),
            B("Exercise.ExerciseStyle", "exerciseStyle", SecurityTermRead.ConventionString),
            B("Notional.Notional", "notional", SecurityTermRead.RequiredDecimal),
            B("UnderlyingSwap.MaturityDate", "underlyingMaturityDate", SecurityTermRead.RequiredDate),
            B("UnderlyingSwap.FloatingIndex", "floatingIndex", SecurityTermRead.ConventionString),
            B("UnderlyingSwap.FixedLegPaymentFrequency", "fixedLegPaymentFrequency", SecurityTermRead.ConventionString),
            B("UnderlyingSwap.FixedLegDayCount", "fixedLegDayCount", SecurityTermRead.DayCount),
            B("Settlement.SettlementType", "settlementType", SecurityTermRead.ConventionString)),
        Class("CapFloor",
            B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
            B("Dates.MaturityDate", "maturityDate", SecurityTermRead.RequiredDate, via: "MaturityDate"),
            B("Exercise.OptionRight", "capFloorType", SecurityTermRead.RequiredString),
            B("Exercise.PremiumAmount", "premiumAmount", SecurityTermRead.StrictDecimal),
            B("FloatingRate.ReferenceIndex", "referenceIndex", SecurityTermRead.RequiredString),
            B("FloatingRate.ResetFrequency", "resetFrequency", SecurityTermRead.ConventionString),
            B("FloatingRate.FloorRate", "floorStrike", SecurityTermRead.StrictDecimal),
            B("FloatingRate.CapRate", "capStrike", SecurityTermRead.StrictDecimal),
            B("Notional.Notional", "notional", SecurityTermRead.RequiredDecimal),
            B("ContractualRate.DayCount", "dayCount", SecurityTermRead.DayCount)),
        Class("StructuredNote",
            B("Dates.IssueDate", "issueDate", SecurityTermRead.StrictDate),
            B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
            B("StructuredPayoff.UnderlyingDescription", "underlyingDescription", SecurityTermRead.RequiredString),
            B("StructuredPayoff.PrincipalProtectionPct", "principalProtectionPct", SecurityTermRead.StrictDecimal),
            B("StructuredPayoff.ParticipationRate", "participationRate", SecurityTermRead.StrictDecimal),
            B("StructuredPayoff.ReturnCap", "returnCap", SecurityTermRead.StrictDecimal),
            B("StructuredPayoff.BarrierLevel", "barrierLevel", SecurityTermRead.StrictDecimal),
            B("Notional.Denomination", "denomination", SecurityTermRead.StrictDecimal),
            B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.RequiredString, via: "IssuerName"),
            B("ContractualRate.CouponRate", "couponRate", SecurityTermRead.StrictDecimal),
            B("ContractualRate.PaymentFrequency", "paymentFrequency", SecurityTermRead.ConventionString)),
        Class("InvestmentFund",
            B("FundShare.FundType", "fundType", SecurityTermRead.OptionalString),
            B("FundShare.NavCurrency", "navCurrency", SecurityTermRead.OptionalString),
            Computed("FundShare.DistributionPolicy", "distributionPolicy",
                "ToDistributionPolicyOption(bound.OptionalString(\"distributionPolicy\"))",
                "terms.DistributionPolicy |> Option.map DistributionPolicy.label"),
            B("FundShare.IsStableNav", "isStableNav", SecurityTermRead.OptionalBoolean),
            B("FundTerms.FundFamily", "fundFamily", SecurityTermRead.OptionalString),
            B("ReferenceDataSource.PricingSource", "pricingSource", SecurityTermRead.OptionalString)),
    ];

    /// <summary>The generated classes by name.</summary>
    public static IReadOnlyDictionary<string, SecurityClassCodecBinding> ByAssetClass { get; } =
        Classes.ToDictionary(static binding => binding.AssetClass, StringComparer.Ordinal);
}
