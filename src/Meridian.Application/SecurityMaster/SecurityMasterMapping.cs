using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityMasterMapping
{
    public static CreateSecurity ToCreateCommand(CreateSecurityRequest request)
        => new(
            SecurityId.NewSecurityId(request.SecurityId),
            ToCommonTerms(request.CommonTerms),
            ToFSharpList(request.Identifiers.Select(ToIdentifier)),
            ToSecurityKind(request.AssetClass, request.AssetSpecificTerms, request.CommonTerms, SecurityKindMappingMode.Write),
            request.EffectiveFrom,
            ToProvenance(request.SourceSystem, request.UpdatedBy, request.SourceRecordId, request.Reason, request.EffectiveFrom));

    public static AmendTerms ToAmendCommand(AmendSecurityTermsRequest request, SecurityProjectionRecord current)
    {
        var kind = FSharpOption<SecurityKind>.None;
        if (request.AssetSpecificTermsPatch is JsonElement assetSpecific)
        {
            kind = FSharpOption<SecurityKind>.Some(ToSecurityKind(current.AssetClass, assetSpecific,
                request.CommonTerms ?? current.CommonTerms, SecurityKindMappingMode.Write, current.AssetSpecificTerms));
        }
        else if (request.CommonTerms is JsonElement submittedCommon
            && SecurityAssetClassCatalog.Canonicalize(current.AssetClass) is "Bond" or "DirectLoan" or "StructuredCredit")
        {
            var assetTerms = SecurityAssetClassTermsUpgrader.UpgradeForRead(current.AssetClass, current.AssetSpecificTerms);
            var schedules = SecurityEconomicScheduleParser.Parse(assetTerms, submittedCommon);
            schedules.ThrowIfInvalid();
            if (IsCommonSchedule(schedules.FactorSchedule)
                || IsCommonSchedule(schedules.PrincipalSchedule)
                || IsCommonSchedule(schedules.StepCouponSchedule))
            {
                // Common metadata cannot serialize a schedule. Carry a selected common assertion
                // into the typed kind so a common-only amendment cannot discard it. The unchanged
                // asset document retains its read-version rules, including readable-ahead terms.
                kind = FSharpOption<SecurityKind>.Some(ToSecurityKind(
                    current.AssetClass, current.AssetSpecificTerms, submittedCommon));
            }
        }

        return new(
            SecurityId.NewSecurityId(request.SecurityId),
            request.ExpectedVersion,
            request.CommonTerms is JsonElement common ? FSharpOption<CommonTerms>.Some(ToCommonTerms(common)) : FSharpOption<CommonTerms>.None,
            kind,
            ToFSharpList(request.IdentifiersToAdd.Select(ToIdentifier)),
            ToFSharpList(request.IdentifiersToExpire.Select(ToIdentifier)),
            request.EffectiveFrom,
            ToProvenance(request.SourceSystem, request.UpdatedBy, request.SourceRecordId, request.Reason, request.EffectiveFrom));
    }

    private static bool IsCommonSchedule<T>(SecurityScheduleParseResult<T> schedule)
        => schedule.IsPresent && schedule.SourcePath?.StartsWith("commonTerms.", StringComparison.Ordinal) == true;

    public static DeactivateSecurity ToDeactivateCommand(DeactivateSecurityRequest request)
        => new(
            SecurityId.NewSecurityId(request.SecurityId),
            request.ExpectedVersion,
            request.EffectiveTo,
            ToProvenance(request.SourceSystem, request.UpdatedBy, request.SourceRecordId, request.Reason, request.EffectiveTo));

    public static SecurityMasterRecord ToRecord(SecurityProjectionRecord record)
        => new(
            SecurityId.NewSecurityId(record.SecurityId),
            ToSecurityStatus(record.Status),
            ToCommonTerms(record.CommonTerms),
            ToFSharpList(record.Identifiers.Select(ToIdentifier)),
            ToSecurityKind(record.AssetClass, record.AssetSpecificTerms, record.CommonTerms),
            record.Version,
            record.EffectiveFrom,
            ToOption(record.EffectiveTo),
            ToProvenance(record.Provenance));

    public static SecurityProjectionRecord ToProjection(SecurityMasterSnapshotWrapper snapshot, IReadOnlyList<SecurityAliasDto>? aliases = null)
        => new(
            snapshot.SecurityId,
            snapshot.AssetClass,
            ToSecurityStatus(snapshot.Status),
            snapshot.DisplayName,
            snapshot.Currency,
            snapshot.PrimaryIdentifierKind,
            snapshot.PrimaryIdentifierValue,
            ParseJson(snapshot.CommonTermsJson),
            ParseJson(snapshot.AssetSpecificTermsJson),
            ParseJson(snapshot.ProvenanceJson),
            snapshot.Version,
            snapshot.EffectiveFrom,
            snapshot.EffectiveTo.HasValue ? snapshot.EffectiveTo.Value : null,
            snapshot.Identifiers.Select(ToIdentifierDto).ToArray(),
            aliases ?? Array.Empty<SecurityAliasDto>());

    public static SecurityDetailDto ToDetail(SecurityProjectionRecord projection)
        => new(
            projection.SecurityId,
            projection.AssetClass,
            projection.Status,
            projection.DisplayName,
            projection.Currency,
            projection.CommonTerms,
            projection.AssetSpecificTerms,
            NormalizeIdentifiers(projection.Identifiers),
            projection.Aliases,
            projection.Version,
            projection.EffectiveFrom,
            projection.EffectiveTo);

    public static SecurityMasterEventEnvelope ToEventEnvelope(
        SecurityEconomicDefinitionRecord economic,
        string eventType,
        string actor,
        string sourceSystem,
        string? reason,
        long streamVersion)
    {
        var metadata = JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["sourceSystem"] = sourceSystem,
                ["reason"] = reason,
                [SecurityMasterEventPayloads.SchemaVersionMetadataKey] = SecurityMasterEventPayloads.EconomicDefinitionVersion,
                [SecurityMasterEventPayloads.PayloadTypeMetadataKey] = SecurityMasterEventPayloads.EconomicDefinition
            },
            SecurityMasterJsonContext.Default.DictionaryStringObject);

        return new SecurityMasterEventEnvelope(
            GlobalSequence: null,
            SecurityId: economic.SecurityId,
            StreamVersion: streamVersion,
            EventType: eventType,
            EventTimestamp: DateTimeOffset.UtcNow,
            Actor: actor,
            CorrelationId: null,
            CausationId: null,
            Payload: JsonSerializer.SerializeToElement(
                economic,
                SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord),
            Metadata: metadata,
            PayloadType: SecurityMasterEventPayloads.EconomicDefinition,
            PayloadVersion: SecurityMasterEventPayloads.EconomicDefinitionVersion);
    }

    /// <summary>
    /// Decodes a stored event into an economic definition after checking its declared payload family
    /// and version are ones this node can replay. A payload from a newer node is refused with the
    /// event's identity rather than decoded by an older decoder that would drop what it added.
    /// </summary>
    public static SecurityEconomicDefinitionRecord FromEventEnvelope(SecurityMasterEventEnvelope @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SecurityMasterEventPayloads.EnsureReplayable(
            SecurityMasterEventPayloads.Resolve(@event),
            @event.SecurityId,
            @event.StreamVersion);
        return FromEconomicPayload(@event.Payload);
    }

    public static SecurityEconomicDefinitionRecord FromEconomicPayload(JsonElement payload)
    {
        if (SecurityMasterEventPayloads.IsEconomicDefinitionShape(payload))
        {
            return JsonSerializer.Deserialize(payload, SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord)
                ?? throw new InvalidOperationException("Security economic definition payload could not be deserialized.");
        }

        var projection = JsonSerializer.Deserialize(payload, SecurityMasterJsonContext.Default.SecurityProjectionRecord)
            ?? throw new InvalidOperationException("Security projection payload could not be deserialized.");
        return SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
    }

    public static SecurityProjectionRecord FromProjectionPayload(JsonElement payload)
    {
        var economicRecord = FromEconomicPayload(payload);
        return SecurityEconomicDefinitionAdapter.ToProjection(economicRecord);
    }

    public static SecuritySnapshotRecord ToSnapshot(SecurityEconomicDefinitionRecord economic, DateTimeOffset snapshotTimestamp)
        => new(
            economic.SecurityId,
            economic.Version,
            snapshotTimestamp,
            JsonSerializer.SerializeToElement(
                economic,
                SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord));

    private static SecurityStatus ToSecurityStatus(SecurityStatusDto status)
        => status == SecurityStatusDto.Active ? SecurityStatus.Active : SecurityStatus.Inactive;

    private static SecurityStatusDto ToSecurityStatus(string status)
        => string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase)
            ? SecurityStatusDto.Inactive
            : SecurityStatusDto.Active;

    private static IReadOnlyList<SecurityIdentifierDto> NormalizeIdentifiers(IReadOnlyList<SecurityIdentifierDto> identifiers)
        => identifiers
            .Select(static identifier => identifier with
            {
                NormalizedValue = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier),
                NormalizedProvider = SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier)
            })
            .ToArray();

    private static Identifier ToIdentifier(SecurityIdentifierDto identifier)
        => new(
            ToIdentifierKind(identifier.Kind, identifier.Provider),
            identifier.Value,
            identifier.IsPrimary,
            identifier.ValidFrom,
            ToOption(identifier.ValidTo),
            ToOption(identifier.Provider));

    private static IdentifierKind ToIdentifierKind(SecurityIdentifierKind kind, string? provider)
        => kind switch
        {
            SecurityIdentifierKind.Ticker => IdentifierKind.Ticker,
            SecurityIdentifierKind.Isin => IdentifierKind.Isin,
            SecurityIdentifierKind.Cusip => IdentifierKind.Cusip,
            SecurityIdentifierKind.Sedol => IdentifierKind.Sedol,
            SecurityIdentifierKind.Figi => IdentifierKind.Figi,
            SecurityIdentifierKind.OccOptionSymbol => IdentifierKind.OccOptionSymbol,
            SecurityIdentifierKind.ProviderSymbol => IdentifierKind.NewProviderSymbol(provider ?? string.Empty),
            SecurityIdentifierKind.InternalCode => IdentifierKind.InternalCode,
            SecurityIdentifierKind.Lei => IdentifierKind.Lei,
            SecurityIdentifierKind.PermId => IdentifierKind.PermId,
            SecurityIdentifierKind.Bbgid => IdentifierKind.Bbgid,
            SecurityIdentifierKind.Wkn => IdentifierKind.Wkn,
            SecurityIdentifierKind.Valoren => IdentifierKind.Valoren,
            SecurityIdentifierKind.PermTicker => IdentifierKind.PermTicker,
            SecurityIdentifierKind.Ric => IdentifierKind.Ric,
            SecurityIdentifierKind.Cik => IdentifierKind.Cik,
            SecurityIdentifierKind.LoanX => IdentifierKind.LoanX,
            SecurityIdentifierKind.Clip => IdentifierKind.Clip,
            SecurityIdentifierKind.ListingId => IdentifierKind.ListingId,
            SecurityIdentifierKind.Uti => IdentifierKind.Uti,
            SecurityIdentifierKind.Upi => IdentifierKind.Upi,
            SecurityIdentifierKind.MarkitRedEntity => IdentifierKind.MarkitRedEntity,
            SecurityIdentifierKind.MarkitRedPair => IdentifierKind.MarkitRedPair,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported security identifier kind.")
        };

    private static SecurityIdentifierDto ToIdentifierDto(SecurityIdentifierSnapshot identifier)
    {
        // Read-tolerant: a kind stamped by a newer node degrades to Unknown so the snapshot stays
        // readable; the strict write-side mapping (ToIdentifier) still rejects Unknown, so an
        // unrecognized kind is never silently re-persisted.
        var kind = SecurityMasterEnumReads.ParseOrFallback(identifier.Kind, SecurityIdentifierKind.Unknown);
        return new(
            kind,
            identifier.Value,
            identifier.IsPrimary,
            identifier.ValidFrom,
            identifier.ValidTo.HasValue ? identifier.ValidTo.Value : null,
            string.IsNullOrWhiteSpace(identifier.Provider) ? null : identifier.Provider,
            SecurityIdentifierNormalizer.NormalizeValue(kind, identifier.Value),
            SecurityIdentifierNormalizer.NormalizeProvider(identifier.Provider));
    }

    private static CommonTerms ToCommonTerms(JsonElement json)
        => new(
            GetRequiredString(json, "displayName"),
            GetRequiredString(json, "currency"),
            ToOption(GetOptionalString(json, "countryOfRisk")),
            ToOption(GetOptionalString(json, "issuerName")),
            ToOption(GetOptionalString(json, "exchange")),
            ToOption(GetOptionalDecimal(json, "lotSize")),
            ToOption(GetOptionalDecimal(json, "tickSize")),
            ToOption(GetOptionalString(json, "primaryListingMic")),
            ToOption(GetOptionalString(json, "countryOfIncorporation")),
            ToOption(GetOptionalInt(json, "settlementCycleDays")),
            ToOption(GetOptionalString(json, "holidayCalendarId")));

    /// <summary>
    /// Distinguishes command mapping (create/amend requests) from record reconstruction. Write
    /// mapping is strict: a malformed payload must fail the command instead of being coerced into
    /// a different kind. Read mapping retains legacy vocabulary defaults; economic schedules use
    /// the shared parsing contract in both modes so malformed rows cannot disappear on reconstruction.
    /// </summary>
    private enum SecurityKindMappingMode
    {
        Read,
        Write
    }

    private static SecurityKind ToSecurityKind(string assetClass, JsonElement json, JsonElement commonTerms,
        SecurityKindMappingMode mode = SecurityKindMappingMode.Read, JsonElement? retainedTerms = null)
    {
        // The switch below and every per-class rule it consults are keyed by canonical names; a
        // registered alias ("Loan", "MBS", "PrivateFund") is the class it names, not an unknown
        // class to refuse on write or to degrade to OtherSecurity on read.
        assetClass = SecurityAssetClassCatalog.Canonicalize(assetClass);
        EnsureSupportedAssetSchemaVersion(assetClass, json, mode);
        if (mode == SecurityKindMappingMode.Write)
            SecurityAssetPackAdmission.EnsureSubmittedFields(assetClass, json, retainedTerms);
        // Which versioned containers (the scheduleConventions block, the call and put schedules) this
        // payload decodes, through the one predicate the validator and the cash-flow engine share: a
        // read decodes those at or below the row's own stamp, a write those at or below the class's
        // write version, and a profile envelope none (ADR-024 decision 3). Decided on the payload as
        // stored or submitted, before the upgrade.
        var versionedTerms = SecurityVersionedTerms.Decoding(assetClass, json, writeMode: mode == SecurityKindMappingMode.Write);
        // The guard sees the stored or submitted payload; the decoder reads it at the class's current
        // terms version (ADR-024). Identity steps return the same element.
        json = SecurityAssetClassTermsUpgrader.UpgradeForRead(assetClass, json);
        if (mode == SecurityKindMappingMode.Write)
        {
            // ADR-027 stage 3 (S3-B): the schema's declared read aliases are part of the submission
            // contract — the registry DTOs, the field-edit canonicalizer and the day-count guard all
            // advertise them — but the per-class decode below reads canonical spellings. Rewriting
            // the submitted document once, from the schema table, makes every declared alias land on
            // its canonical key for every class (the S3-B sweep found seven classes whose declared
            // aliases the decode ignored, silently dropping optional values submitted under an
            // advertised spelling). An ambiguous submission — the canonical key and an alias, or two
            // aliases, carrying different values — refuses rather than letting key order decide.
            // Read mode is untouched: stored rows are canonical by construction.
            json = CanonicalizeDeclaredAliases(assetClass, json);
        }
        var terms = ResolveAssetTermsJson(json);
        SecurityEconomicScheduleValidation? economicSchedules = null;
        if (assetClass is "Bond" or "DirectLoan" or "StructuredCredit")
        {
            economicSchedules = SecurityEconomicScheduleParser.Parse(json, commonTerms);
            economicSchedules.ThrowIfInvalid();
        }
        // Domain snapshots retain the valid typed source rows, including their order and split
        // same-date principal payments. Sorting/aggregation belongs to calculated and relational
        // views, not to reconstructing a retained record or emitting its next domain snapshot.

        // S3-D: the schema-bound view the migrated classes (SchemaBoundDecodeClasses) read
        // through; every bound read verifies its key is declared with the type it is read as.
        var bound = new SchemaBoundTerms(assetClass, json, mode);
        // The profile-resolved sub-document some classes read their flat fields from; the schema
        // declares the same keys wherever the bag lives.
        var boundTerms = new SchemaBoundTerms(assetClass, terms, mode);
        // Contracting storage fields reorders nested constructors. Historical read refusal order
        // remains the old codec's order even when several required fields are absent.
        if (mode == SecurityKindMappingMode.Read)
            EnsureLegacyRequiredReadOrder(assetClass, bound, boundTerms);
        var kind = assetClass switch
        {
            "Equity" => SecurityKind.NewEquity(new EquityTerms(new EquityCapitalTerms(ToOption(bound.OptionalString("shareClass")), ToVotingRightsCatOption(bound.OptionalString("votingRightsCat")), ToEquityClassificationOption(bound, mode)))),
            "Option" => SecurityKind.NewOption(new OptionTerms(
                new MaturityTerms(null, null, ToOption(bound.RequiredDateOnly("expiry"))),
                new ListingDetailTerms(
                    ToOption(bound.OptionalString("optChainId")),
                    ToOption(bound.OptionalString("settlementType")),
                    ToOption(bound.OptionalBoolean("isAdjusted") ?? false),
                    ToOption(bound.OptionalDateOnly("lastTradingDt")),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                new ExerciseTerms(
                    FSharpOption<string>.Some(bound.RequiredString("putCall")),
                    ToOption(bound.RequiredDecimal("strike")),
                    null,
                    CanonicalExerciseStyle(ParseExerciseStyle(bound.OptionalString("exerciseStyle"))),
                    FSharpOption<SecurityId>.Some(SecurityId.NewSecurityId(bound.RequiredGuid("underlyingId"))),
                    null),
                new TradingParams(null, null, ToOption(bound.RequiredDecimal("multiplier")), null, null, null))),
            "Future" => SecurityKind.NewFuture(new FutureTerms(
                new MaturityTerms(null, null, ToOption(bound.RequiredDateOnly("expiry"))),
                new ListingDetailTerms(
                    null,
                    ToOption(bound.OptionalString("settlementType")),
                    null,
                    ToOption(bound.OptionalDateOnly("lastTradingDt")),
                    FSharpOption<string>.Some(bound.RequiredString("rootSymbol")),
                    FSharpOption<string>.Some(bound.RequiredString("contractMonth")),
                    ToOption(bound.OptionalDateOnly("firstNoticeDt")),
                    ToOption(bound.OptionalDateOnly("deliveryMonthDt")),
                    ToOption(bound.OptionalString("deliveryLocationCode")),
                    ToOption(bound.OptionalBoolean("isRollTarget") ?? false),
                    ToOption(bound.OptionalInt("rollWindowDays"))),
                new TradingParams(null, null, ToOption(bound.RequiredDecimal("multiplier")), null, null, null))),
            "Bond" => SecurityKind.NewBond(ToBondTerms(
                bound,
                mode,
                economicSchedules!,
                new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),
                ReadCallPutSchedules(json, assetClass, versionedTerms)),
                ReadBondWorkflowJson(json, versionedTerms, mode))),
            "FxSpot" => SecurityKind.NewFxSpot(new FxSpotTerms(
                new CurrencyPairTerms(FSharpOption<string>.Some(bound.RequiredString("baseCurrency")), FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")), null, null))),
            "Deposit" => SecurityKind.NewDeposit(new DepositTerms(
                new MaturityTerms(null, null, ToOption(bound.OptionalDateOnly("maturity"))),
                new IssuerRef(null, FSharpOption<string>.Some(bound.RequiredString("institutionName")), FSharpOption<string>.Some(bound.RequiredString("depositType")), null, null, null, null),
                new CouponTerms(FSharpOption<CouponKind>.Some(CouponKind.NewOtherCoupon("SimpleInterest")),
                    ToOption(bound.OptionalDecimal("interestRate")), null,
                    ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
                EconomicCallTermsModule.Scalar(bound.OptionalBoolean("isCallable") ?? false, null),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "MoneyMarketFund" => SecurityKind.NewMoneyMarketFund(new MoneyMarketFundTerms(
                new FundTerms(ToOption(bound.OptionalString("fundFamily")), ToOption(bound.OptionalInt("weightedAverageMaturityDays")), ToOption(bound.OptionalBoolean("sweepEligible") ?? false), ToOption(bound.OptionalBoolean("liquidityFeeEligible") ?? false)))),
            "CertificateOfDeposit" => SecurityKind.NewCertificateOfDeposit(new CertificateOfDepositTerms(
                new MaturityTerms(null, null, ToOption(bound.RequiredDateOnly("maturity"))),
                new IssuerRef(FSharpOption<string>.Some(bound.RequiredString("issuerName")), null, null, null, null, null, null),
                new CouponTerms(FSharpOption<CouponKind>.Some(CouponKind.Fixed),
                    ToOption(bound.OptionalDecimal("couponRate")), null,
                    ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
                EconomicCallTermsModule.Scalar(bound.OptionalDateOnly("callableDate").HasValue, ToOption(bound.OptionalDateOnly("callableDate"))),
                new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),
                ReadCallPutSchedules(json, assetClass, versionedTerms)),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "CommercialPaper" => SecurityKind.NewCommercialPaper(new CommercialPaperTerms(
                new MaturityTerms(null, null, ToOption(bound.RequiredDateOnly("maturity"))),
                new IssuerRef(FSharpOption<string>.Some(bound.RequiredString("issuerName")), null, ToOption((bound.OptionalBoolean("isAssetBacked") ?? false) ? "AssetBacked" : null), null, null, null, null),
                new DiscountTerms(ToOption(bound.OptionalDecimal("discountRate")), null),
                new CouponTerms(FSharpOption<CouponKind>.Some(CouponKind.DiscountNote), null, null,
                    ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "TreasuryBill" => SecurityKind.NewTreasuryBill(new TreasuryBillTerms(
                new MaturityTerms(null, null, ToOption(bound.RequiredDateOnly("maturity"))),
                new AuctionTerms(ToOption(bound.OptionalDateOnly("auctionDate")), null),
                new IssuerRef(ToOption("US Treasury"), null, ToOption(bound.OptionalString("cusip")), null, null, null, ToOption("US")),
                new DiscountTerms(ToOption(bound.OptionalDecimal("discountRate")), null),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "Repo" => SecurityKind.NewRepo(new RepoTerms(
                new FinancingTerms(FSharpOption<string>.Some(bound.RequiredString("counterparty")), ToOption(bound.OptionalString("collateralType")), ToOption(bound.OptionalDecimal("haircut")), ToOption(bound.RequiredDateOnly("startDate")), ToOption(bound.RequiredDateOnly("endDate"))),
                new DiscountTerms(null, ToOption(bound.OptionalDecimal("repoRate"))),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "CashSweep" => SecurityKind.NewCashSweep(new CashSweepTerms(
                new SweepTerms(FSharpOption<string>.Some(bound.RequiredString("programName")), FSharpOption<SweepVehicle>.Some(ToCodecSweepVehicle(bound.RequiredString("sweepVehicleType"))), ToCodecSweepFrequency(ToOption(bound.OptionalString("sweepFrequency"))), ToOption(bound.OptionalString("targetAccountType"))),
                new DiscountTerms(null, ToOption(bound.OptionalDecimal("yieldRate"))),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "OtherSecurity" => SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
                new MaturityTerms(null, null, ToOption(bound.OptionalDateOnly("maturity"))),
                new ClassificationDetailTerms(bound.RequiredString("category"), ToOption(bound.OptionalString("subType"))),
                new IssuerRef(ToOption(bound.OptionalString("issuerName")), null, ToOption(bound.OptionalString("settlementType")), null, null, null, null))),
            "CustomAsset" => ToCustomAssetKind(bound, mode),
            "Swap" => SecurityKind.NewSwap(new SwapTerms(
                new MaturityTerms(ToOption(bound.RequiredDateOnly("effectiveDate")), ToOption(bound.RequiredDateOnly("effectiveDate")), ToOption(bound.RequiredDateOnly("maturityDate"))),
                new SwapLegTerms(ToFSharpList(GetSwapLegItems(json).Select(leg => ToSwapLeg(leg, ReadLegScheduleConventions(leg, assetClass, versionedTerms), mode)))),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "DirectLoan" => SecurityKind.NewDirectLoan(new DirectLoanTerms(
                new MaturityTerms(null, null, ToOption(bound.OptionalDateOnly("maturity"))),
                new IssuerRef(FSharpOption<string>.Some(bound.RequiredString("borrower")), null, null, null, null, null, null),
                new LoanCovenantTerms(ToFSharpList(GetRequiredArray(json, "covenants").EnumerateArray().Select(ToCovenant))),
                new FloatingRateTerms(ToOption(bound.OptionalString("referenceIndex")),
                    ToOption(bound.OptionalDecimal("spreadBps")),
                    ToOption(bound.OptionalString("resetFrequency")), null, null),
                new CouponTerms(null, ToOption(bound.OptionalDecimal("currentCouponRate")), null, null),
                new PrincipalScheduleTerms(ToFSharpList(economicSchedules!.PrincipalSchedule.SourceEntries.Select(static entry =>
                    new PrincipalPaymentEntry(entry.PaymentDate, entry.Amount)))),
                new ReferenceDataSourceTerms(ToOption(bound.OptionalString("pricingSource")), null),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "StructuredCredit" => SecurityKind.NewStructuredCredit(new StructuredCreditTerms(
                new CouponTerms(FSharpOption<CouponKind>.Some(CouponKind.NewOtherCoupon(boundTerms.RequiredString("couponOrIndex"))), null, null, null),
                new TrancheContractTerms(
                // Typed tranche terms are optional, but a WRITE must not read a malformed supplied
                // value as absent: persisting null for a numeric kind, "1.5" months or an unparseable
                // date would silently discard caller input. Reads stay tolerant so stored rows remain
                // loadable.
                ToStructuredCreditKindOption(boundTerms.OptionalConventionString("structuredCreditKind")),
                    ToOption(boundTerms.OptionalConventionString("seniority")),
                    ToOption(boundTerms.OptionalStrictDecimal("attachmentPoint")),
                    ToOption(boundTerms.OptionalStrictDecimal("detachmentPoint")),
                    ToOption(boundTerms.OptionalStrictDateOnly("reinvestmentPeriodEnd")),
                    ToOption(boundTerms.OptionalStrictDateOnly("nonCallPeriodEnd"))),
                new StructuredProductTerms(
                    ToOption(boundTerms.OptionalDecimal("currentFactor")),
                    null,
                    ToOption(boundTerms.OptionalStrictDecimal("weightedAverageCoupon")),
                    ToOption(boundTerms.OptionalStrictInt("weightedAverageMaturityMonths")),
                    ToOption(boundTerms.OptionalStrictInt("weightedAverageLoanAgeMonths")),
                    FSharpOption<string>.Some(boundTerms.RequiredString("collateralType")),
                    ToOption(boundTerms.OptionalString("poolId")),
                    FSharpOption<string>.Some(boundTerms.RequiredString("tranche")),
                    null,
                    null,
                    false,
                    false,
                    ToOption(boundTerms.RequiredDecimal("originalFace")),
                    null,
                    null,
                    ToOption(boundTerms.OptionalString("factorSchedule")),
                    ToFSharpList(economicSchedules!.FactorSchedule.SourceEntries.Select(static entry =>
                    new EconomicFactorScheduleEntry(entry.AsOfDate, entry.Factor)))),
                new MaturityTerms(null, null, ToOption(boundTerms.OptionalDateOnly("maturity"))),
                // From the root: a profile envelope decodes no versioned container, so its
                // profileFields never reach the block or the schedules.
                new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),
                ReadCallPutSchedules(json, assetClass, versionedTerms)),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "PrivateFundInterest" => SecurityKind.NewPrivateFundInterest(new PrivateFundInterestTerms(
                new PrivateAssetValuationTerms(
                    ToOption(boundTerms.RequiredInt("vintage")),
                    ToOption(boundTerms.RequiredDateOnly("navDate")),
                    ToOption(boundTerms.OptionalString("lockup")),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                new NotionalTerms(
                    null,
                    null,
                    ToOption(boundTerms.RequiredDecimal("commitment")),
                    ToOption(boundTerms.OptionalDecimal("fundedAmount")),
                    ToOption(boundTerms.OptionalDecimal("unfundedAmount"))),
                new IssuerRef(FSharpOption<string>.Some(boundTerms.RequiredString("gpSponsor")), null, FSharpOption<string>.Some(boundTerms.RequiredString("strategy")), null, null, null, null))),
            "PrivateCompanyEquity" => SecurityKind.NewPrivateCompanyEquity(new PrivateCompanyEquityTerms(
                new EquityCapitalTerms(FSharpOption<string>.Some(boundTerms.RequiredString("shareClass")), null, null),
                new PrivateAssetValuationTerms(
                    null,
                    null,
                    null,
                    ToOption(boundTerms.OptionalDecimal("ownershipPercent")),
                    ToOption(boundTerms.RequiredDecimal("costBasis")),
                    ToOption(boundTerms.OptionalDecimal("latestValuation")),
                    ToOption(boundTerms.OptionalString("transferRestrictions")),
                    null,
                    null,
                    null,
                    null),
                new IssuerRef(FSharpOption<string>.Some(boundTerms.RequiredString("issuer")), null, FSharpOption<string>.Some(boundTerms.RequiredString("round")), null, null, null, null))),
            "RealEstateHolding" => SecurityKind.NewRealEstateHolding(new RealEstateHoldingTerms(
                new PrivateAssetValuationTerms(
                    null,
                    null,
                    null,
                    ToOption(boundTerms.RequiredDecimal("ownershipPercent")),
                    null,
                    null,
                    null,
                    FSharpOption<string>.Some(boundTerms.RequiredString("addressOrMarket")),
                    ToOption(boundTerms.RequiredDecimal("appraisalValue")),
                    ToOption(boundTerms.RequiredDateOnly("valuationDate")),
                    ToOption(boundTerms.OptionalString("debtStack"))),
                new IssuerRef(ToOption(boundTerms.OptionalString("sponsor")), null, FSharpOption<string>.Some(boundTerms.RequiredString("propertyType")), null, null, ToOption("RealEstate"), null))),
            "CommitmentGuarantee" => SecurityKind.NewCommitmentGuarantee(new CommitmentGuaranteeTerms(
                new MaturityTerms(ToOption(boundTerms.RequiredDateOnly("effectiveDate")), ToOption(boundTerms.RequiredDateOnly("effectiveDate")), ToOption(boundTerms.OptionalDateOnly("expiryDate"))),
                new NotionalTerms(null, null, ToOption(boundTerms.RequiredDecimal("committedAmount")), null, ToOption(boundTerms.OptionalDecimal("unfundedAmount"))),
                new IssuerRef(FSharpOption<string>.Some(boundTerms.RequiredString("counterparty")), null, ToOption(boundTerms.OptionalString("beneficiary")), null, null, null, null),
                new ContractualRateTerms(null, null, ToOption(boundTerms.OptionalDecimal("feeRate")), null, null),
                new FinancingTerms(null, ToOption(boundTerms.OptionalString("collateral")), null, null, null),
                new LoanCovenantTerms(ToFSharpList(GetOptionalArrayItems(terms, "covenants").Select(ToCovenant))))),
            "Commodity" => SecurityKind.NewCommodity(new CommodityTerms(new CommodityContractTerms(
                bound.RequiredString("commodityType"),
                ToOption(bound.OptionalString("denomination")),
                ToOption(bound.OptionalDecimal("contractSize"))))),
            "CryptoCurrency" => SecurityKind.NewCryptoCurrency(new CryptoTerms(
                new CurrencyPairTerms(FSharpOption<string>.Some(bound.RequiredString("baseCurrency")), FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")), null, null),
                new DigitalAssetTerms(ToOption(bound.OptionalString("network"))))),
            "Cfd" => SecurityKind.NewCfd(new CfdTerms(new UnderlyingExposureTerms(
                bound.RequiredString("underlyingAssetClass"),
                ToOption(bound.OptionalString("underlyingDescription")),
                ToOption(bound.OptionalDecimal("leverage"))))),
            "Warrant" => SecurityKind.NewWarrant(new WarrantTerms(
                new MaturityTerms(null, null, ToOption(bound.OptionalDateOnly("expiry"))),
                new ExerciseTerms(
                    FSharpOption<string>.Some(bound.RequiredString("warrantType")),
                    ToOption(bound.OptionalDecimal("strike")),
                    null,
                    null,
                    FSharpOption<SecurityId>.Some(SecurityId.NewSecurityId(bound.RequiredGuid("underlyingId"))),
                    null),
                new TradingParams(null, null, ToOption(bound.OptionalDecimal("multiplier")), null, null, null))),
            "FxForward" => SecurityKind.NewFxForward(new FxForwardTerms(
                new MaturityTerms(ToOption(bound.OptionalStrictDateOnly("tradeDate")), null, ToOption(bound.RequiredDateOnly("valueDate"))),
                new CurrencyPairTerms(
                    FSharpOption<string>.Some(bound.RequiredString("baseCurrency")),
                    FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")),
                    null,
                    ToOption(bound.OptionalStrictDecimal("forwardRate"))),
                new NotionalTerms(ToOption(bound.OptionalStrictDecimal("notional")), null, null, null, null),
                new SettlementTerms(ToOption(bound.OptionalConventionString("settlementType"))))),
            "NonDeliverableForward" => SecurityKind.NewNonDeliverableForward(new NonDeliverableForwardTerms(
                new MaturityTerms(ToOption(bound.RequiredDateOnly("fixingDate")), null, ToOption(bound.RequiredDateOnly("valueDate"))),
                new CurrencyPairTerms(
                    FSharpOption<string>.Some(bound.RequiredString("baseCurrency")),
                    FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")),
                    FSharpOption<string>.Some(bound.RequiredString("settlementCurrency")),
                    ToOption(bound.OptionalStrictDecimal("forwardRate"))),
                new NotionalTerms(ToOption(bound.OptionalStrictDecimal("notional")), null, null, null, null),
                new ReferenceDataSourceTerms(null, ToOption(bound.OptionalConventionString("fixingSource"))))),
            "CreditDefaultSwap" => SecurityKind.NewCreditDefaultSwap(new CreditDefaultSwapTerms(
                new MaturityTerms(ToOption(bound.RequiredDateOnly("effectiveDate")), ToOption(bound.RequiredDateOnly("effectiveDate")), ToOption(bound.RequiredDateOnly("maturityDate"))),
                new CreditProtectionTerms(
                    bound.RequiredString("referenceEntity"),
                    ToOption(bound.OptionalConventionString("referenceObligation")),
                    bound.RequiredString("protectionSide"),
                    ToOption(bound.OptionalConventionString("seniority")),
                    ToOption(bound.OptionalConventionString("restructuringClause")),
                    ToOption(bound.OptionalStrictDecimal("upfrontPoints")),
                    ToOption(bound.OptionalStrictDecimal("recoveryRateAssumption")),
                    ToOption(bound.OptionalConventionString("indexName"))),
                new NotionalTerms(ToOption(bound.RequiredDecimal("notional")), null, null, null, null),
                new ContractualRateTerms(null, ToOption(bound.RequiredDecimal("fixedCouponBps")), null,
                    ToOption(bound.OptionalConventionString("paymentFrequency")),
                    ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)))),
            "Swaption" => SecurityKind.NewSwaption(new SwaptionTerms(
                new MaturityTerms(ToOption(bound.OptionalStrictDateOnly("underlyingEffectiveDate")), null, ToOption(bound.RequiredDateOnly("expiry"))),
                new ExerciseTerms(
                    FSharpOption<string>.Some(bound.RequiredString("payerReceiver")),
                    null,
                    ToOption(bound.RequiredDecimal("strikeRate")),
                    ToOption(bound.OptionalConventionString("exerciseStyle")),
                    null,
                    null),
                new NotionalTerms(ToOption(bound.RequiredDecimal("notional")), null, null, null, null),
                new UnderlyingSwapTerms(bound.RequiredDateOnly("underlyingMaturityDate"),
                    ToOption(bound.OptionalConventionString("floatingIndex")),
                    ToOption(bound.OptionalConventionString("fixedLegPaymentFrequency")),
                    ToDayCountOption(bound.OptionalConventionString("fixedLegDayCount"), mode, "fixedLegDayCount")),
                new SettlementTerms(ToOption(bound.OptionalConventionString("settlementType"))),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "CapFloor" => SecurityKind.NewCapFloor(new CapFloorTerms(
                new MaturityTerms(ToOption(bound.RequiredDateOnly("effectiveDate")), null, ToOption(bound.RequiredDateOnly("maturityDate"))),
                new ExerciseTerms(FSharpOption<string>.Some(bound.RequiredString("capFloorType")), null, null, null, null, ToOption(bound.OptionalStrictDecimal("premiumAmount"))),
                new FloatingRateTerms(FSharpOption<string>.Some(bound.RequiredString("referenceIndex")), null, ToOption(bound.OptionalConventionString("resetFrequency")), ToOption(bound.OptionalStrictDecimal("floorStrike")), ToOption(bound.OptionalStrictDecimal("capStrike"))),
                new NotionalTerms(ToOption(bound.RequiredDecimal("notional")), null, null, null, null),
                new ContractualRateTerms(null, null, null, null, ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "StructuredNote" => SecurityKind.NewStructuredNote(new StructuredNoteTerms(
                new MaturityTerms(null, ToOption(bound.OptionalStrictDateOnly("issueDate")), ToOption(bound.RequiredDateOnly("maturity"))),
                new StructuredPayoffTerms(
                    bound.RequiredString("underlyingDescription"),
                    ToOption(bound.OptionalStrictDecimal("principalProtectionPct")),
                    ToOption(bound.OptionalStrictDecimal("participationRate")),
                    ToOption(bound.OptionalStrictDecimal("returnCap")),
                    ToOption(bound.OptionalStrictDecimal("barrierLevel"))),
                new NotionalTerms(null, ToOption(bound.OptionalStrictDecimal("denomination")), null, null, null),
                new IssuerRef(FSharpOption<string>.Some(bound.RequiredString("issuerName")), null, null, null, null, null, null),
                new ContractualRateTerms(ToOption(bound.OptionalStrictDecimal("couponRate")), null, null,
                    ToOption(bound.OptionalConventionString("paymentFrequency")), null),
                ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write)))),
            "InvestmentFund" => SecurityKind.NewInvestmentFund(ToInvestmentFundReferenceTerms(bound)),
            // Read tolerance must not become write tolerance. On a create, an unrecognized class
            // ("ExchangeTradedFund", which the pack registry only PLANS; a typo like "Equitiy")
            // would otherwise persist silently as OtherSecurity — and because OtherSecurity is a
            // catalog class, every later amend passes the round-trip guard, so the
            // misclassification is permanent and never surfaces. Amend and deactivate are guarded
            // in SecurityMasterService.EnsureAssetClassIsRecognizedHere before they reach here;
            // this arm is the one guard the create path has.
            _ when mode == SecurityKindMappingMode.Write =>
                throw new InvalidOperationException(UnrecognizedAssetClassOnWrite(assetClass)),
            // Unknown classes degrade to OtherSecurity with the raw class preserved as the category
            // instead of failing every read of the row. A newer node can register a class this node
            // has no deserializer for; throwing here made that a total read outage per security
            // (see the InvestmentFund regression in SecurityMasterMappingInteropTests).
            _ => ToUnrecognizedClassKind(
                assetClass,
                GetOptionalString(json, "subType"),
                GetOptionalDateOnly(json, "maturity"),
                GetOptionalString(json, "issuerName"),
                GetOptionalString(json, "settlementType"))
        };

        EnsureDeclaredVocabulariesOnWrite(assetClass, json, mode, versionedTerms);
        return kind;
    }

    /// <summary>
    /// The schema-driven backstop for closed-vocabulary discriminants on the WRITE path: every
    /// declared vocabulary in <see cref="SecurityAssetTermsSchema"/> is enforced here, so adding one
    /// to the table constrains create/amend commands without a matching edit to this mapping.
    /// <para>It runs AFTER the kind is built purely for diagnosis quality: the decode sites that
    /// branch on a discriminant (the equity classification, the bond coupon structure) already throw
    /// with the precise reason their case needs, and letting them speak first keeps those messages.
    /// The mapping is a pure function, so nothing has been committed by the time this runs — the
    /// ordering costs nothing but the wording of the exception.</para>
    /// </summary>
    private static void EnsureDeclaredVocabulariesOnWrite(
        string assetClass, JsonElement json, SecurityKindMappingMode mode, SecurityVersionedTermDecoding versionedTerms)
    {
        if (mode != SecurityKindMappingMode.Write || json.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in SecurityAssetTermsSchema.DiscriminantFields(assetClass))
        {
            // Absent (or explicitly null) leaves the decode site's documented default alone — an
            // omitted couponType is legitimately Fixed. A present token of the WRONG KIND is not
            // absent, though it reaches the decode sites as if it were: GetOptionalString erases a
            // number, bool, object, or array to null, so the write persists the missing-value
            // default and discards the structure the payload named, which is the same silent
            // rewrite an undeclared spelling would cause. Refuse it the same way.
            if (!json.TryGetProperty(field.Key, out var token) || token.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (token.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    UndeclaredDiscriminantValue(assetClass, field.Key, token.GetRawText()));
            }

            var raw = token.GetString();
            if (field.Allows(raw))
            {
                continue;
            }

            throw new InvalidOperationException(UndeclaredDiscriminantValue(assetClass, field.Key, raw));
        }

        // Typed conventions must name a value their parser recognizes. Day counts normalize to
        // canonical market labels; an unparseable day count would otherwise be
        // persisted and silently read as Unknown by every accrual and cash-flow calculation.
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass)
                     .Where(static field => field.Convention != SecurityTermConventionKind.None))
        {
            if (!json.TryGetProperty(field.Key, out var token) || token.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var raw = token.ValueKind == JsonValueKind.String ? token.GetString() : token.GetRawText();
            if (token.ValueKind != JsonValueKind.String || !field.SatisfiesConvention(raw))
            {
                throw new InvalidOperationException(
                    SecurityTermConventions.DescribeUnrecognized(field.Convention, assetClass, field.Key, raw));
            }
        }

        EnsureElementConventionsOnWrite(assetClass, json, versionedTerms);
    }

    /// <summary>
    /// Typed conventions declared on the elements of an array term (the swap legs' payment
    /// frequency and day count) are enforced on write exactly like top-level ones; otherwise a leg
    /// could persist a cadence every typed reader and schedule would then misread. Every present
    /// alias is checked, since readers differ in which spelling they prefer. Only conventions this
    /// node writes are enforced, and only in the arrays the write decodes: never inside a versioned
    /// container of a profile envelope, through the shared predicate
    /// (<see cref="SecurityVersionedTermDecoding.Decodes"/>), because versioned class terms are
    /// flat-codec-only (ADR-024 decision 3, kept by the contract step).
    /// </summary>
    private static void EnsureElementConventionsOnWrite(string assetClass, JsonElement json, SecurityVersionedTermDecoding versionedTerms)
    {
        foreach (var arrayField in SecurityAssetTermsSchema.WritableFields(assetClass)
                     .Where(static field => field.Type == SecurityAssetTermFieldType.Array))
        {
            if (!versionedTerms.Decodes(arrayField))
            {
                continue;
            }

            var conventionFields = SecurityAssetTermsSchema.ElementFields(assetClass, arrayField.Key)
                .Where(static field => field.Convention != SecurityTermConventionKind.None)
                .ToArray();
            if (conventionFields.Length == 0)
            {
                continue;
            }

            foreach (var arrayKey in new[] { arrayField.Key }.Concat(arrayField.Aliases))
            {
                if (!json.TryGetProperty(arrayKey, out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var element in array.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object))
                {
                    foreach (var field in conventionFields)
                    {
                        foreach (var key in new[] { field.Key }.Concat(field.Aliases))
                        {
                            if (!element.TryGetProperty(key, out var token) || token.ValueKind == JsonValueKind.Null)
                            {
                                continue;
                            }

                            var raw = token.ValueKind == JsonValueKind.String ? token.GetString() : token.GetRawText();
                            if (token.ValueKind != JsonValueKind.String || !field.SatisfiesConvention(raw))
                            {
                                throw new InvalidOperationException(
                                    SecurityTermConventions.DescribeUnrecognized(field.Convention, assetClass, $"{arrayField.Key}[].{key}", raw));
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The shared rejection reason for a discriminant value outside its declared vocabulary, so the
    /// decode sites and the schema backstop reject a given value with identical wording.
    /// </summary>
    private static string UndeclaredDiscriminantValue(string assetClass, string key, string? value)
        => SecurityAssetTermsSchema.Field(assetClass, key) is { } field
            ? field.DescribeUndeclaredValue(assetClass, value)
            : $"Value '{value}' is not a declared '{key}' value for asset class '{assetClass}'.";

    /// <summary>
    /// Maps a CustomAsset payload to the first-class <see cref="SecurityKind.CustomAsset"/> case,
    /// carrying the document verbatim so the profile envelope and dynamic profile fields survive
    /// amend round-trips. A legacy CustomAsset row without a profile envelope degrades to the
    /// pre-existing OtherSecurity salvage on READS only; create/amend commands that name
    /// CustomAsset without a profile envelope fail here instead of being silently re-typed, so the
    /// F# CustomAsset invariants (profile envelope, profileFields object) always run for writes.
    /// <para>The <c>profileVersion ?? 1</c> default is READ tolerance only: the F# write-path
    /// validation (<c>validateKind</c>) parses the document and rejects create/amend commands whose
    /// envelope lacks a numeric <c>profileVersion</c> or an object-valued <c>profileFields</c>, so
    /// the default can never mint a canonical record with an incomplete envelope.</para>
    /// </summary>
    private static SecurityKind ToCustomAssetKind(SchemaBoundTerms terms, SecurityKindMappingMode mode)
    {
        if (terms.IsObject
            && terms.OptionalString("customProfileId") is { } customProfileId
            && !string.IsNullOrWhiteSpace(customProfileId))
        {
            return SecurityKind.NewCustomAsset(new ProfileEnvelopeTerms(
                customProfileId,
                terms.OptionalInt("profileVersion") ?? 1,
                terms.RawText));
        }

        if (mode == SecurityKindMappingMode.Write)
        {
            throw new InvalidOperationException(
                "CustomAsset terms must include a non-empty 'customProfileId' referencing an approved asset profile. " +
                "Select an approved profile (or use asset class 'OtherSecurity' for unprofiled instruments) instead of submitting an envelope-less CustomAsset.");
        }

        // The salvage reads the OtherSecurity shape off a row the CustomAsset schema describes only
        // as a profile envelope: category and subType are declared for the class and bind; the
        // other three are ledgered in UnboundCompatibilityReads.
        var salvage = terms.Unbound;
        return SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
            new MaturityTerms(null, null, ToOption(salvage.OptionalDateOnly("maturity"))),
            new ClassificationDetailTerms(terms.OptionalString("category") ?? "CustomAsset", ToOption(terms.OptionalString("subType"))),
            new IssuerRef(ToOption(salvage.OptionalString("issuerName")), null, ToOption(salvage.OptionalString("settlementType")), null, null, null, null)));
    }

    private static BondSubclass ParseBondSubclass(string? subclass) => subclass switch
    {
        "Sovereign" => BondSubclass.Sovereign,
        "Municipal" => BondSubclass.Municipal,
        "Agency" => BondSubclass.Agency,
        "Convertible" => BondSubclass.Convertible,
        "InflationLinked" => BondSubclass.InflationLinked,
        "FloatingRate" => BondSubclass.FloatingRate,
        "SinkingFund" => BondSubclass.SinkingFund,
        "StepRate" => BondSubclass.StepRate,
        "FixedToFloat" => BondSubclass.FixedToFloat,
        "Vrdn" => BondSubclass.Vrdn,
        "AuctionRate" => BondSubclass.AuctionRate,
        "Corporate" => BondSubclass.Corporate,
        null or "" => BondSubclass.Corporate,
        var other => BondSubclass.NewOther(other)
    };

    /// <summary>
    /// Decodes the flat bond coupon contract. <c>couponType</c> is a closed schema vocabulary
    /// (<c>Fixed</c>, <c>Floating</c>, <c>ZeroCoupon</c>, <c>Step</c>, <c>InflationLinked</c>) with
    /// no escape: an unrecognized value cannot be carried, it collapses to <c>Fixed</c> and takes
    /// the label plus every structure-specific field (<c>floatingIndex</c>, the step schedule, the
    /// inflation block) with it. So the fallback arm is READ tolerance only — the same
    /// read-tolerant/write-strict split the CustomAsset envelope check and the equity classification
    /// decode already apply. An absent <c>couponType</c> still means <c>Fixed</c> on both paths: it
    /// is the serializer's own spelling for a plain fixed coupon, not an unreadable value.
    /// </summary>
    private static BondTerms ToBondTerms(
        SchemaBoundTerms terms,
        SecurityKindMappingMode mode,
        SecurityEconomicScheduleValidation economicSchedules,
        DeclaredScheduleTerms declaredSchedule,
        FSharpOption<string> workflowJson)
    {
        var couponType = terms.OptionalString("couponType") ?? "Fixed";
        BondCouponStructure coupon = couponType switch
        {
            "Floating" => BondCouponStructure.NewFloating(
                terms.RequiredString("floatingIndex"),
                ToOption(terms.OptionalDecimal("spreadBps")),
                ToOption(terms.OptionalDecimal("capRate")),
                ToOption(terms.OptionalDecimal("floorRate")),
                ToDayCountOption(terms.OptionalConventionString("dayCount"), mode)),
            "ZeroCoupon" => BondCouponStructure.ZeroCoupon,
            "Step" => BondCouponStructure.NewStep(
                ToFSharpList(economicSchedules.StepCouponSchedule.SourceEntries.Select(static entry =>
                    new StepCouponEntry(entry.EffectiveDate, entry.Rate))),
                ToDayCountOption(terms.OptionalConventionString("dayCount"), mode)),
            // The scalar couponRate slot carries the inflation-linked REAL rate; couponType
            // discriminates, so a fixed-coupon read can never pick up an indexed rate.
            "InflationLinked" => BondCouponStructure.NewInflationLinked(
                terms.OptionalDecimal("couponRate") ?? 0m,
                terms.RequiredString("inflationIndex"),
                ToOption(terms.OptionalDecimal("inflationBaseIndexValue")),
                ToOption(terms.OptionalDecimal("inflationIndexRatio")),
                ToDayCountOption(terms.OptionalConventionString("dayCount"), mode)),
            "Fixed" => BondCouponStructure.NewFixed(
                terms.OptionalDecimal("couponRate") ?? 0m,
                ToDayCountOption(terms.OptionalConventionString("dayCount"), mode)),
            // A WRITE fails closed on an unrecognized coupon type: silently persisting a typo
            // ("Floter") as a fixed coupon would change the bond's economics and drop the fields
            // the named structure owns.
            _ when mode == SecurityKindMappingMode.Write =>
                throw new InvalidOperationException(UndeclaredDiscriminantValue("Bond", "couponType", couponType)),
            // Read tolerance: an unrecognized coupon type must not fail every read of the row.
            _ => BondCouponStructure.NewFixed(
                terms.OptionalDecimal("couponRate") ?? 0m,
                ToDayCountOption(terms.OptionalConventionString("dayCount"), mode))
        };
        return new BondTerms(
                new MaturityTerms(null, ToOption(terms.OptionalDateOnly("issueDate")), ToOption(terms.RequiredDateOnly("maturity"))),
                new BondCouponTerms(coupon, ToPaymentFrequencyOption(terms.OptionalString("paymentFrequency"))),
                EconomicCallTermsModule.Scalar(terms.OptionalBoolean("isCallable") ?? false, ToOption(terms.OptionalDateOnly("callDate"))),
                new IssuerRef(ToOption(terms.OptionalString("issuerName")), null, null, null, null, null, null),
                new DebtClassificationTerms(ParseBondSubclass(terms.OptionalString("subclass")), ToOption(terms.OptionalString("seniority"))),
                new BondRedemptionTerms(ToOption(terms.OptionalDecimal("par")),
                    ToOption(terms.OptionalDateOnly("legalFinalMaturity")),
                    ToOption(terms.OptionalDateOnly("preRefundDate")),
                    ToOption(terms.OptionalDateOnly("mandatoryPutDate"))),
                new PrincipalScheduleTerms(ToFSharpList(economicSchedules.PrincipalSchedule.SourceEntries.Select(static entry =>
                new PrincipalPaymentEntry(entry.PaymentDate, entry.Amount)))),
                declaredSchedule, workflowJson);
    }

    private static FSharpOption<string> ReadBondWorkflowJson(JsonElement payload,
        SecurityVersionedTermDecoding decoding, SecurityKindMappingMode mode)
    {
        var field = SecurityAssetTermsSchema.Field("Bond", "bondWorkflow");
        if (field is null || !decoding.Decodes(field) || !payload.TryGetProperty("bondWorkflow", out var workflow)
            || !SecurityAssetTermsSchema.IsPopulatedTermValue(workflow))
            return FSharpOption<string>.None;
        if (mode == SecurityKindMappingMode.Write
            && !SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.bondWorkflow",
                workflow.GetRawText(), out _, out var reason))
            throw new InvalidOperationException(reason);
        return FSharpOption<string>.Some(workflow.GetRawText());
    }

    private const string ScheduleConventionsKey = SecurityVersionedTerms.ScheduleConventionsKey;
    private const string CallScheduleKey = SecurityVersionedTerms.CallScheduleKey;
    private const string PutScheduleKey = SecurityVersionedTerms.PutScheduleKey;

    // The callSchedule and putSchedule arrays (ADR-024, readable before writable), behind the same
    // shared predicate as the block: decoded only when the class declares them and the payload decodes
    // them. Entries are read tolerantly and verbatim; one that is not an object, or has no parseable
    // date, is not a term and is dropped. SecurityMasterService refuses to re-serialize a row whose
    // entries this cannot type, before anything could be dropped.
    private static CallPutSchedules ReadCallPutSchedules(JsonElement container, string assetClass, SecurityVersionedTermDecoding versionedTerms)
    {
        var calls = ReadEntries(container, SecurityAssetTermsSchema.Field(assetClass, CallScheduleKey), versionedTerms, ToCallScheduleTerm).ToArray();
        var puts = ReadEntries(container, SecurityAssetTermsSchema.Field(assetClass, PutScheduleKey), versionedTerms, ToPutScheduleTerm).ToArray();
        return calls.Length == 0 && puts.Length == 0
            ? CallPutSchedulesModule.Empty
            : new CallPutSchedules(ToFSharpList(calls), ToFSharpList(puts));
    }

    private static IEnumerable<T> ReadEntries<T>(
        JsonElement container,
        SecurityAssetTermField? field,
        SecurityVersionedTermDecoding versionedTerms,
        Func<JsonElement, T?> read)
        where T : class
    {
        if (field is null || !versionedTerms.Decodes(field))
        {
            return [];
        }

        return GetOptionalArrayItems(container, field.Key)
            .Where(static entry => entry.ValueKind == JsonValueKind.Object)
            .Select(read)
            .OfType<T>();
    }

    private static CallScheduleTerm? ToCallScheduleTerm(JsonElement entry)
        => GetOptionalDateOnly(entry, "callDate") is { } callDate
            ? new CallScheduleTerm(
                callDate,
                ToOption(GetOptionalDateOnly(entry, "callEndDate")),
                ToOption(GetOptionalString(entry, "exerciseStyle")),
                ToOption(GetOptionalString(entry, "exerciseFrequency")),
                ToOption(GetOptionalDecimal(entry, "callPrice")),
                ToOption(GetOptionalString(entry, "priceBasis")),
                ToOption(GetOptionalString(entry, "priceType")),
                ToOption(GetOptionalDecimal(entry, "makeWholeSpreadBps")),
                ToOption(GetOptionalString(entry, "partialRedemption")),
                ToOption(GetOptionalInt(entry, "minimumNoticeDays")),
                ToOption(GetOptionalInt(entry, "maximumNoticeDays")),
                ToOption(GetOptionalString(entry, "noticeDayType")))
            : null;

    private static PutScheduleTerm? ToPutScheduleTerm(JsonElement entry)
        => GetOptionalDateOnly(entry, "putDate") is { } putDate
            ? new PutScheduleTerm(
                putDate,
                ToOption(GetOptionalDateOnly(entry, "putEndDate")),
                ToOption(GetOptionalString(entry, "exerciseStyle")),
                ToOption(GetOptionalString(entry, "exerciseFrequency")),
                ToOption(GetOptionalDecimal(entry, "putPrice")),
                ToOption(GetOptionalString(entry, "priceBasis")),
                ToOption(GetOptionalInt(entry, "minimumNoticeDays")),
                ToOption(GetOptionalInt(entry, "maximumNoticeDays")),
                ToOption(GetOptionalString(entry, "noticeDayType")))
            : null;

    private static ScheduleConventions ReadScheduleConventions(JsonElement container, string assetClass, SecurityVersionedTermDecoding versionedTerms)
        => ReadScheduleConventions(container, SecurityAssetTermsSchema.Field(assetClass, ScheduleConventionsKey), versionedTerms);

    private static ScheduleConventions ReadLegScheduleConventions(JsonElement leg, string assetClass, SecurityVersionedTermDecoding versionedTerms)
        => leg.ValueKind == JsonValueKind.Object
            ? ReadScheduleConventions(
                leg,
                SecurityAssetTermsSchema.ElementFields(assetClass, "legs").FirstOrDefault(static field => field.Key == ScheduleConventionsKey),
                versionedTerms)
            : ScheduleConventionsModule.Empty;

    // The scheduleConventions block (ADR-024, readable before writable): decoded only when the class
    // declares it and the payload decodes it (SecurityVersionedTermDecoding), so a row stamped below
    // the block's version, an unstamped row, a profile envelope and every write decode nothing and keep
    // their bytes. The shared reader (SecurityVersionedTerms.ReadScheduleConventions) reads the members
    // tolerantly and verbatim; the F# record drops blank text members, as it always has.
    // SecurityMasterService refuses to re-serialize a row whose block content this cannot type, before
    // anything could be dropped.
    private static ScheduleConventions ReadScheduleConventions(JsonElement container, SecurityAssetTermField? field, SecurityVersionedTermDecoding versionedTerms)
    {
        if (field is null || !versionedTerms.Decodes(field))
        {
            return ScheduleConventionsModule.Empty;
        }

        var block = SecurityVersionedTerms.ReadScheduleConventions(container, field.Key);
        if (block.IsEmpty)
        {
            return ScheduleConventionsModule.Empty;
        }

        return new ScheduleConventions(
            ToOption(block.BusinessDayConvention),
            ToOption(block.AccrualAdjusted),
            ToFSharpList(block.PaymentCalendars),
            ToOption(block.PaymentLagBusinessDays),
            ToOption(block.RollConvention),
            ToOption(block.EndOfMonthRule),
            ToOption(block.FirstRegularDate),
            ToOption(block.LastRegularDate),
            ToOption(block.Direction),
            ToOption(block.StubPolicy),
            ToOption(block.CouponRateUnit),
            ToOption(block.CouponCashBasis));
    }

    // Match the cash-flow resolver's alias priority, while refusing malformed supplied economics
    // instead of persisting them as absent. The pre-widening four-field leg remains readable.
    private static SwapLeg ToSwapLeg(JsonElement json, ScheduleConventions scheduleConventions, SecurityKindMappingMode mode)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Each swap leg must be a JSON object.");
        }

        return new SwapLeg(
            ToOption(ReadSwapLegString(json, "legId", "id", "name")),
            ReadSwapLegString(json, "legType", "rateType", "type")
                ?? throw new InvalidOperationException("Missing required string 'legType' in a swap leg."),
            ReadSwapLegString(json, "currency")
                ?? throw new InvalidOperationException("Missing required string 'currency' in a swap leg."),
            ToOption(NormalizeSwapLegDirection(ReadSwapLegString(json, "direction", "payReceive", "payOrReceive", "side"))),
            ToOption(ReadSwapLegString(json, "index", "indexName", "referenceIndex")),
            ToOption(ReadSwapLegDecimal(json, "fixedRate", "rate", "couponRate")),
            ToOption(ReadSwapLegDecimal(json, "spreadBps")),
            ToOption(ReadSwapLegDecimal(json, "currentIndexRate", "lastFixing", "currentRate", "indexRate")),
            ToOption(ReadSwapLegDecimal(json, "notional", "notionalAmount", "faceAmount", "principal")),
            ToOption(ReadSwapLegString(json, "paymentFrequency", "frequency")),
            ToSwapLegDayCountOption(json, mode),
            ReadSwapLegPrincipalExchange(json),
            ToFSharpList(ReadSwapLegNotionalSchedule(json)),
            ToFSharpList(ReadSwapLegResetSchedule(json)),
            scheduleConventions);
    }

    // Dated schedules on a leg. Like the leg's scalar terms, a malformed entry is refused rather
    // than read as absent, so a write never silently drops an amortization step or a fixing.
    private static IEnumerable<SwapNotionalStep> ReadSwapLegNotionalSchedule(JsonElement json)
        => ReadSwapLegScheduleItems(json, "notionalSchedule", "amortizationSchedule")
            .Select(static item => new SwapNotionalStep(
                ReadScheduleDate(item, "notionalSchedule", "effectiveDate", "date"),
                SecurityTermReader.ReadDecimal(item, "notional", "amount")
                    ?? throw new InvalidOperationException("Each swap leg notionalSchedule entry must have a numeric 'notional'.")))
            .ToArray();

    private static IEnumerable<SwapResetEntry> ReadSwapLegResetSchedule(JsonElement json)
        => ReadSwapLegScheduleItems(json, "resetSchedule", "fixings")
            .Select(static item => new SwapResetEntry(
                ReadScheduleDate(item, "resetSchedule", "resetDate", "fixingDate", "date"),
                ToOption(ReadOptionalScheduleDecimal(item, "resetSchedule", "fixingRate", "fixing", "rate"))))
            .ToArray();

    private static IEnumerable<JsonElement> ReadSwapLegScheduleItems(JsonElement json, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!SecurityTermReader.TryGetProperty(json, alias, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Swap leg property '{alias}' must be a JSON array.");
            }

            var items = new List<JsonElement>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"Each swap leg '{alias}' entry must be a JSON object.");
                }

                items.Add(item);
            }

            return items;
        }

        return [];
    }

    private static DateOnly ReadScheduleDate(JsonElement item, string schedule, params string[] aliases)
        => SecurityTermReader.ReadDate(item, aliases)
           ?? throw new InvalidOperationException($"Each swap leg {schedule} entry must have an ISO-8601 '{aliases[0]}'.");

    private static decimal? ReadOptionalScheduleDecimal(JsonElement item, string schedule, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!SecurityTermReader.TryGetProperty(item, alias, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            return SecurityTermReader.ReadDecimal(item, alias)
                ?? throw new InvalidOperationException($"Swap leg {schedule} property '{alias}' must be a number or numeric string.");
        }

        return null;
    }

    private static IEnumerable<JsonElement> GetSwapLegItems(JsonElement json)
    {
        JsonElement? emptyArray = null;
        foreach (var alias in new[] { "legs", "swapLegs", "cashFlowLegs" })
        {
            if (!SecurityTermReader.TryGetProperty(json, alias, out var value))
            {
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Swap leg container '{alias}' must be a JSON array.");
            }

            if (value.GetArrayLength() > 0)
            {
                return value.EnumerateArray();
            }

            // The resolver tries the next alias when an array is empty. An entirely empty set
            // still reaches the domain's existing nonempty-legs validation.
            emptyArray = value;
        }

        return emptyArray?.EnumerateArray()
            ?? throw new InvalidOperationException("Missing required array 'legs'.");
    }

    private static string? ReadSwapLegString(JsonElement json, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!SecurityTermReader.TryGetProperty(json, alias, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Swap leg property '{alias}' must be a JSON string.");
            }

            if (SecurityTermReader.ReadString(json, alias) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    private static decimal? ReadSwapLegDecimal(JsonElement json, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!SecurityTermReader.TryGetProperty(json, alias, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            return SecurityTermReader.ReadDecimal(json, alias)
                ?? throw new InvalidOperationException($"Swap leg property '{alias}' must be a number or numeric string.");
        }

        return null;
    }

    private static bool ReadSwapLegPrincipalExchange(JsonElement json)
    {
        foreach (var alias in new[] { "exchangesPrincipal", "principalExchange", "notionalExchange" })
        {
            if (!SecurityTermReader.TryGetProperty(json, alias, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"Swap leg property '{alias}' must be a boolean or boolean string.");
        }

        return false;
    }

    private static string? NormalizeSwapLegDirection(string? direction)
    {
        var normalized = direction?.Trim().ToUpperInvariant();
        if (normalized?.Contains("PAY", StringComparison.Ordinal) == true)
        {
            return "Pay";
        }

        return normalized?.Contains("REC", StringComparison.Ordinal) == true ? "Receive" : direction;
    }

    private static Covenant ToCovenant(JsonElement json)
        => new(
            GetRequiredString(json, "covenantType"),
            GetRequiredString(json, "threshold"),
            ToOption(GetOptionalString(json, "notes")));

    private static Provenance ToProvenance(string sourceSystem, string updatedBy, string? sourceRecordId, string? reason, DateTimeOffset asOf)
        => new(sourceSystem, ToOption(sourceRecordId), asOf, updatedBy, ToOption(reason));

    private static Provenance ToProvenance(JsonElement json)
        => new(
            GetRequiredString(json, "sourceSystem"),
            ToOption(GetOptionalString(json, "sourceRecordId")),
            GetRequiredDateTimeOffset(json, "asOf"),
            GetRequiredString(json, "updatedBy"),
            ToOption(GetOptionalString(json, "reason")));

    private static FSharpList<T> ToFSharpList<T>(IEnumerable<T> values)
        => ListModule.OfSeq(values);

    // Listed options historically normalize recognized styles and leave unknown historical
    // labels undeclared. Keep that codec rule while storing the result in the shared module.
    private static FSharpOption<string> CanonicalExerciseStyle(FSharpOption<ExerciseStyle> style)
        => style is null ? null! : FSharpOption<string>.Some(style.Value.ToString());

    private static FSharpOption<RateConventionTerms> ToRateConventionOption(string? token)
        => token is null ? null! : FSharpOption<RateConventionTerms>.Some(new RateConventionTerms(token));

    private static SweepVehicle ToCodecSweepVehicle(string value)
        => value switch
        {
            "MoneyMarketFund" => SweepVehicle.MoneyMarketFund,
            "BankDeposit" => SweepVehicle.BankDeposit,
            "Repo" => SweepVehicle.Repo,
            _ => SweepVehicle.NewOtherVehicle(value)
        };

    private static FSharpOption<PaymentFrequency> ToCodecSweepFrequency(FSharpOption<string> value)
        => value is null ? null! : FSharpOption<PaymentFrequency>.Some(PaymentFrequency.NewOtherFrequency(value.Value));

    private static FSharpOption<ExerciseStyle> ParseExerciseStyle(string? value)
        => value?.Trim() switch
        {
            "American" => FSharpOption<ExerciseStyle>.Some(ExerciseStyle.American),
            "European" => FSharpOption<ExerciseStyle>.Some(ExerciseStyle.European),
            "Bermudan" => FSharpOption<ExerciseStyle>.Some(ExerciseStyle.Bermudan),
            _ => FSharpOption<ExerciseStyle>.None
        };

    private static FSharpOption<Meridian.Contracts.SecurityMaster.DayCountConvention> ToSwapLegDayCountOption(
        JsonElement json, SecurityKindMappingMode mode)
    {
        string[] aliases = ["dayCountConvention", "dayCount", "dayCountBasis"];
        if (mode == SecurityKindMappingMode.Write)
        {
            foreach (var alias in aliases)
            {
                if (!HasNonNullProperty(json, alias))
                    continue;
                var raw = ReadSwapLegString(json, alias);
                if (!string.IsNullOrWhiteSpace(raw) && !DayCountConventions.TryParseTerm(raw, out _))
                    throw new InvalidOperationException(SecurityTermConventions.DescribeUnrecognized(
                        SecurityTermConventionKind.DayCount, "Swap", $"legs[].{alias}", raw));
            }
        }
        return ToDayCountOption(ReadSwapLegString(json, aliases), mode);
    }

    private static FSharpOption<Meridian.Contracts.SecurityMaster.DayCountConvention> ToDayCountOption(
        string? value, SecurityKindMappingMode mode, string field = "dayCount")
    {
        if (string.IsNullOrWhiteSpace(value))
            return FSharpOption<Meridian.Contracts.SecurityMaster.DayCountConvention>.None;
        if (!DayCountConventions.TryParseTerm(value, out var convention) && mode == SecurityKindMappingMode.Write)
            throw new InvalidOperationException($"Invalid {field} '{value}'. Supply a supported day-count convention.");
        return FSharpOption<Meridian.Contracts.SecurityMaster.DayCountConvention>.Some(convention);
    }

    private static FSharpOption<string> ToOption(string? value)
        => string.IsNullOrWhiteSpace(value) ? FSharpOption<string>.None : FSharpOption<string>.Some(value);

    private static FSharpOption<decimal> ToOption(decimal? value)
        => value.HasValue ? FSharpOption<decimal>.Some(value.Value) : FSharpOption<decimal>.None;

    private static FSharpOption<int> ToOption(int? value)
        => value.HasValue ? FSharpOption<int>.Some(value.Value) : FSharpOption<int>.None;

    private static FSharpOption<bool> ToOption(bool? value)
        => value.HasValue ? FSharpOption<bool>.Some(value.Value) : FSharpOption<bool>.None;

    private static FSharpOption<DistributionPolicy> ToDistributionPolicyOption(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? FSharpOption<DistributionPolicy>.None
            : FSharpOption<DistributionPolicy>.Some(value.Trim() switch
            {
                "Accumulating" => DistributionPolicy.Accumulating,
                "Distributing" => DistributionPolicy.Distributing,
                "Sweep" => DistributionPolicy.Sweep,
                var other => DistributionPolicy.NewOtherDistribution(other)
            });

    private static FSharpOption<DateOnly> ToOption(DateOnly? value)
        => value.HasValue ? FSharpOption<DateOnly>.Some(value.Value) : FSharpOption<DateOnly>.None;

    private static FSharpOption<DateTimeOffset> ToOption(DateTimeOffset? value)
        => value.HasValue ? FSharpOption<DateTimeOffset>.Some(value.Value) : FSharpOption<DateTimeOffset>.None;

    private static FSharpOption<PaymentFrequency> ToPaymentFrequencyOption(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? FSharpOption<PaymentFrequency>.None
            : FSharpOption<PaymentFrequency>.Some(value.Trim() switch
            {
                "Daily" => PaymentFrequency.Daily,
                "Weekly" => PaymentFrequency.Weekly,
                "Monthly" => PaymentFrequency.Monthly,
                "Quarterly" => PaymentFrequency.Quarterly,
                "SemiAnnual" => PaymentFrequency.SemiAnnual,
                "Annual" => PaymentFrequency.Annual,
                var other => PaymentFrequency.NewOtherFrequency(other)
            });

    private static string UnrecognizedAssetClassOnWrite(string assetClass)
        => $"Asset class '{assetClass}' is not a Security Master asset class this node recognizes, so the write is refused: " +
           "persisting it would silently reclassify the security as OtherSecurity and the misclassification could never be " +
           "corrected through the amend path. Use one of the catalog asset classes " +
           $"({string.Join(", ", SecurityAssetClassCatalog.AssetClasses)}), or apply the change from a node that supports this class.";

    private static JsonElement ParseJson(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    private static void EnsureSupportedAssetSchemaVersion(string assetClass, JsonElement json, SecurityKindMappingMode mode)
    {
        // A write is also refused when it is readable but not writable on this node: stamped above
        // the class's write version, or populating a term introduced above it (ADR-024).
        if (mode == SecurityKindMappingMode.Write)
        {
            SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, json);
        }
        else
        {
            SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, json);
        }

        var schemaVersion = GetOptionalInt(json, "schemaVersion")
            ?? SecurityMasterSchemaVersions.DefaultAssetSpecificTerms;
        var isProfileBacked = IsProfileBackedAssetPayload(assetClass, json);
        if (SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(schemaVersion, isProfileBacked))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported schemaVersion '{schemaVersion}' for asset class '{assetClass}'.");
    }

    private static bool IsProfileBackedAssetPayload(string assetClass, JsonElement json)
        => SupportsProfileBackedTerms(assetClass)
           && json.TryGetProperty("customProfileId", out var customProfileId)
           && customProfileId.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(customProfileId.GetString());

    private static bool SupportsProfileBackedTerms(string assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms;

    /// <summary>
    /// Rewrites a submitted asset-terms document so every value lands on its schema-canonical key:
    /// for each declared root field whose canonical key is absent, a value submitted under one of
    /// the field's declared aliases moves to the canonical key. Two spellings of the same field
    /// carrying different raw values refuse — an ambiguous submission must not be resolved by key
    /// order; identical duplicates collapse to the canonical spelling. Undeclared keys pass
    /// through untouched (the codec's existing posture), and a document that already uses only
    /// canonical spellings comes back unchanged.
    /// </summary>
    private static JsonElement CanonicalizeDeclaredAliases(string assetClass, JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !SecurityAssetTermsSchema.TryGetFields(assetClass, out var fields))
        {
            return json;
        }

        System.Text.Json.Nodes.JsonObject? rewritten = null;
        foreach (var field in fields)
        {
            if (field.Aliases.Count == 0)
            {
                continue;
            }

            string? presentSpelling = json.TryGetProperty(field.Key, out _) ? field.Key : null;
            foreach (var alias in field.Aliases)
            {
                if (!json.TryGetProperty(alias, out var aliasValue))
                {
                    continue;
                }

                if (presentSpelling is not null)
                {
                    var existing = (rewritten?[field.Key]?.ToJsonString())
                        ?? (json.TryGetProperty(presentSpelling, out var presentValue) ? presentValue.GetRawText() : null);
                    if (!string.Equals(NormalizeRawJson(existing), NormalizeRawJson(aliasValue.GetRawText()), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Ambiguous '{field.Key}' submission for asset class '{assetClass}': '{presentSpelling}' and " +
                            $"'{alias}' carry different values. Submit one spelling of the field.");
                    }

                    rewritten ??= System.Text.Json.Nodes.JsonNode.Parse(json.GetRawText())!.AsObject();
                    rewritten.Remove(alias);
                    continue;
                }

                rewritten ??= System.Text.Json.Nodes.JsonNode.Parse(json.GetRawText())!.AsObject();
                rewritten.Remove(alias);
                rewritten[field.Key] = System.Text.Json.Nodes.JsonNode.Parse(aliasValue.GetRawText());
                presentSpelling = alias;
            }
        }

        return rewritten is null ? json : JsonSerializer.SerializeToElement(rewritten);
    }

    private static string? NormalizeRawJson(string? rawJson)
        => rawJson is null ? null : System.Text.Json.Nodes.JsonNode.Parse(rawJson)?.ToJsonString();

    /// <summary>
    /// ADR-027 stage 3 (S3-D): classes whose scalar decode reads run through the schema-bound
    /// reader (<c>SchemaBoundTerms</c>, in <c>SecurityMasterMapping.SchemaBound.cs</c>), so every
    /// key the codec reads is verified declared with the type it is read as — codec/schema drift
    /// throws instead of silently reading past the table. The migration is complete: the ledger
    /// names every catalog class, and a sweep test holds it equal to the catalog so a class cannot
    /// be added without a schema-bound decode. Each cohort was behavior-neutral by the
    /// decode/encode sweeps, the write goldens and the replay corpus, and the sweep suite exercises
    /// every bound read through the fully-populated twins. The reads the schema cannot bind are
    /// not silent exceptions to this: they are ledgered in <see cref="UnboundCompatibilityReads"/>.
    /// Structural decodes (coupon structures, schedules and their entries, the schedule-conventions
    /// block, swap legs, covenants, the profile envelope) keep their bespoke readers.
    /// </summary>
    public static IReadOnlyList<string> SchemaBoundDecodeClasses { get; } =
        ["FxSpot", "Deposit", "MoneyMarketFund", "Commodity", "CryptoCurrency", "Cfd",
         "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "Repo", "CashSweep", "OtherSecurity", "Warrant",
         "Option", "Future", "FxForward", "NonDeliverableForward", "CreditDefaultSwap", "Swaption", "CapFloor", "StructuredNote",
         "DirectLoan", "StructuredCredit", "PrivateFundInterest", "PrivateCompanyEquity", "RealEstateHolding", "CommitmentGuarantee", "Swap",
         "Equity", "Bond", "InvestmentFund", "CustomAsset"];

    private static JsonElement ResolveAssetTermsJson(JsonElement json)
        => json.TryGetProperty("profileFields", out var profileFields) && profileFields.ValueKind == JsonValueKind.Object
            ? profileFields
            : json;

    private static JsonElement GetRequiredArray(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidOperationException($"Missing required array '{propertyName}'.");

    private static IEnumerable<JsonElement> GetOptionalArrayItems(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            yield return item;
        }
    }

    private static string GetRequiredString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"Missing required string '{propertyName}'.");

    private static string? GetOptionalString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal GetRequiredDecimal(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : throw new InvalidOperationException($"Missing required decimal '{propertyName}'.");

    private static decimal? GetOptionalDecimal(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : null;

    private static int? GetOptionalInt(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var intValue)
            ? intValue
            : null;

    /// <summary>
    /// Write-strict optional readers: in <see cref="SecurityKindMappingMode.Write"/> a property that
    /// is present and not null but does not parse as its declared type is rejected instead of being
    /// read as absent. In read mode they match the tolerant readers above.
    /// </summary>
    private static string? GetOptionalString(JsonElement json, string propertyName, SecurityKindMappingMode mode)
    {
        var parsed = GetOptionalString(json, propertyName);
        if (parsed is null && mode == SecurityKindMappingMode.Write && HasNonNullProperty(json, propertyName))
            throw new InvalidOperationException(MalformedOptionalValue(propertyName, "a string"));
        return parsed;
    }

    private static decimal? GetOptionalDecimal(JsonElement json, string propertyName, SecurityKindMappingMode mode)
    {
        var parsed = GetOptionalDecimal(json, propertyName);
        if (parsed is null && mode == SecurityKindMappingMode.Write && HasNonNullProperty(json, propertyName))
            throw new InvalidOperationException(MalformedOptionalValue(propertyName, "a decimal number"));
        return parsed;
    }

    private static int? GetOptionalInt(JsonElement json, string propertyName, SecurityKindMappingMode mode)
    {
        var parsed = GetOptionalInt(json, propertyName);
        if (parsed is null && mode == SecurityKindMappingMode.Write && HasNonNullProperty(json, propertyName))
            throw new InvalidOperationException(MalformedOptionalValue(propertyName, "a whole number"));
        return parsed;
    }

    private static DateOnly? GetOptionalDateOnly(JsonElement json, string propertyName, SecurityKindMappingMode mode)
    {
        var parsed = GetOptionalDateOnly(json, propertyName);
        if (parsed is null && mode == SecurityKindMappingMode.Write && HasNonNullProperty(json, propertyName))
            throw new InvalidOperationException(MalformedOptionalValue(propertyName, "an ISO-8601 date"));
        return parsed;
    }

    private static bool HasNonNullProperty(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object
           && json.TryGetProperty(propertyName, out var value)
           && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string MalformedOptionalValue(string propertyName, string expected)
        => $"Property '{propertyName}' must be {expected} when supplied; omit it or send null to leave it unset.";

    private static int GetRequiredInt(JsonElement json, string propertyName)
        => GetOptionalInt(json, propertyName)
           ?? throw new InvalidOperationException($"Missing required integer '{propertyName}'.");

    private static bool? GetOptionalBoolean(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    private static Guid GetRequiredGuid(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid)
            ? guid
            : throw new InvalidOperationException($"Missing required guid '{propertyName}'.");

    private static DateOnly GetRequiredDateOnly(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var date)
            ? date
            : throw new InvalidOperationException($"Missing required date '{propertyName}'.");

    private static DateOnly? GetOptionalDateOnly(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var date)
            ? date
            : null;

    private static DateTimeOffset GetRequiredDateTimeOffset(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var date)
            ? date
            : throw new InvalidOperationException($"Missing required timestamp '{propertyName}'.");

    private static JsonElement? GetOptionalObject(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static JsonElement GetRequiredObject(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidOperationException($"Missing required object '{propertyName}'.");

    private static FSharpOption<Meridian.Contracts.Domain.Enums.InstrumentType> ToInstrumentTypeOption(int? raw)
        => raw.HasValue
            ? FSharpOption<Meridian.Contracts.Domain.Enums.InstrumentType>.Some((Meridian.Contracts.Domain.Enums.InstrumentType)raw.Value)
            : FSharpOption<Meridian.Contracts.Domain.Enums.InstrumentType>.None;

    private static FSharpOption<BondSubclass> ToBondSubclassOption(string? raw)
        => raw switch
        {
            "Corporate" => FSharpOption<BondSubclass>.Some(BondSubclass.Corporate),
            "Government" => FSharpOption<BondSubclass>.Some(BondSubclass.Sovereign),
            "Sovereign" => FSharpOption<BondSubclass>.Some(BondSubclass.Sovereign),
            "Municipal" => FSharpOption<BondSubclass>.Some(BondSubclass.Municipal),
            "Convertible" => FSharpOption<BondSubclass>.Some(BondSubclass.Convertible),
            not null => FSharpOption<BondSubclass>.Some(BondSubclass.NewOther(raw)),
            null => FSharpOption<BondSubclass>.None
        };

    /// <summary>
    /// The typed securitisation family. Governed labels match case-insensitively, as the taxonomy
    /// validator does, and canonicalise to the named case; an unrecognised label is kept verbatim as
    /// <c>OtherStructuredCreditKind</c> rather than dropped, mirroring the F# parser, and the
    /// taxonomy validator is what reports it.
    /// </summary>
    private static FSharpOption<StructuredCreditKind> ToStructuredCreditKindOption(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return FSharpOption<StructuredCreditKind>.None;

        var label = raw.Trim();
        return FSharpOption<StructuredCreditKind>.Some(label.ToUpperInvariant() switch
        {
            "AGENCYMBS" => StructuredCreditKind.AgencyMbs,
            "NONAGENCYMBS" => StructuredCreditKind.NonAgencyMbs,
            "COMMERCIALMBS" => StructuredCreditKind.CommercialMbs,
            "ASSETBACKED" => StructuredCreditKind.AssetBacked,
            "CLO" => StructuredCreditKind.Clo,
            "CDO" => StructuredCreditKind.Cdo,
            "CMO" => StructuredCreditKind.Cmo,
            "INTERESTONLY" => StructuredCreditKind.InterestOnly,
            "PRINCIPALONLY" => StructuredCreditKind.PrincipalOnly,
            _ => StructuredCreditKind.NewOtherStructuredCreditKind(label)
        });
    }

    /// <summary>
    /// The pre-contraction codec evaluated required flat fields in constructor order. That order
    /// is observable in historical replay refusals; module constructors must not choose a new
    /// first failure. All reads still pass through the schema-bound view and no value is repaired.
    /// Optional read-mode fields are tolerant, so only required scalar reads belong here.
    /// </summary>
    private static void EnsureLegacyRequiredReadOrder(
        string assetClass, SchemaBoundTerms bound, SchemaBoundTerms boundTerms)
    {
        switch (assetClass)
        {
            case "Option":
                _ = bound.RequiredGuid("underlyingId");
                _ = bound.RequiredString("putCall");
                _ = bound.RequiredDecimal("strike");
                _ = bound.RequiredDateOnly("expiry");
                _ = bound.RequiredDecimal("multiplier");
                break;
            case "Future":
                _ = bound.RequiredString("rootSymbol");
                _ = bound.RequiredString("contractMonth");
                _ = bound.RequiredDateOnly("expiry");
                _ = bound.RequiredDecimal("multiplier");
                break;
            case "Bond":
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "FxSpot":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                break;
            case "Deposit":
                _ = bound.RequiredString("depositType");
                _ = bound.RequiredString("institutionName");
                break;
            case "CertificateOfDeposit":
                _ = bound.RequiredString("issuerName");
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "CommercialPaper":
                _ = bound.RequiredString("issuerName");
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "TreasuryBill":
                _ = bound.RequiredDateOnly("maturity");
                break;
            case "Repo":
                _ = bound.RequiredString("counterparty");
                _ = bound.RequiredDateOnly("startDate");
                _ = bound.RequiredDateOnly("endDate");
                break;
            case "CashSweep":
                _ = bound.RequiredString("programName");
                _ = bound.RequiredString("sweepVehicleType");
                break;
            case "OtherSecurity":
                _ = bound.RequiredString("category");
                break;
            case "Swap":
                _ = bound.RequiredDateOnly("effectiveDate");
                _ = bound.RequiredDateOnly("maturityDate");
                break;
            case "DirectLoan":
                _ = bound.RequiredString("borrower");
                break;
            case "StructuredCredit":
                _ = boundTerms.RequiredString("tranche");
                _ = boundTerms.RequiredString("collateralType");
                _ = boundTerms.RequiredDecimal("originalFace");
                _ = boundTerms.RequiredString("couponOrIndex");
                break;
            case "PrivateFundInterest":
                _ = boundTerms.RequiredString("gpSponsor");
                _ = boundTerms.RequiredString("strategy");
                _ = boundTerms.RequiredInt("vintage");
                _ = boundTerms.RequiredDecimal("commitment");
                _ = boundTerms.RequiredDateOnly("navDate");
                break;
            case "PrivateCompanyEquity":
                _ = boundTerms.RequiredString("issuer");
                _ = boundTerms.RequiredString("shareClass");
                _ = boundTerms.RequiredString("round");
                _ = boundTerms.RequiredDecimal("costBasis");
                break;
            case "RealEstateHolding":
                _ = boundTerms.RequiredString("propertyType");
                _ = boundTerms.RequiredString("addressOrMarket");
                _ = boundTerms.RequiredDecimal("ownershipPercent");
                _ = boundTerms.RequiredDecimal("appraisalValue");
                _ = boundTerms.RequiredDateOnly("valuationDate");
                break;
            case "CommitmentGuarantee":
                _ = boundTerms.RequiredString("counterparty");
                _ = boundTerms.RequiredDecimal("committedAmount");
                _ = boundTerms.RequiredDateOnly("effectiveDate");
                break;
            case "Commodity":
                _ = bound.RequiredString("commodityType");
                break;
            case "CryptoCurrency":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                break;
            case "Cfd":
                _ = bound.RequiredString("underlyingAssetClass");
                break;
            case "Warrant":
                _ = bound.RequiredGuid("underlyingId");
                _ = bound.RequiredString("warrantType");
                break;
            case "FxForward":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                _ = bound.RequiredDateOnly("valueDate");
                break;
            case "NonDeliverableForward":
                _ = bound.RequiredString("baseCurrency");
                _ = bound.RequiredString("quoteCurrency");
                _ = bound.RequiredString("settlementCurrency");
                _ = bound.RequiredDateOnly("fixingDate");
                _ = bound.RequiredDateOnly("valueDate");
                break;
            case "CreditDefaultSwap":
                _ = bound.RequiredString("referenceEntity");
                _ = bound.RequiredString("protectionSide");
                _ = bound.RequiredDecimal("notional");
                _ = bound.RequiredDecimal("fixedCouponBps");
                _ = bound.RequiredDateOnly("effectiveDate");
                _ = bound.RequiredDateOnly("maturityDate");
                break;
            case "Swaption":
                _ = bound.RequiredString("payerReceiver");
                _ = bound.RequiredDecimal("strikeRate");
                _ = bound.RequiredDateOnly("expiry");
                _ = bound.RequiredDateOnly("underlyingMaturityDate");
                _ = bound.RequiredDecimal("notional");
                break;
            case "CapFloor":
                _ = bound.RequiredString("capFloorType");
                _ = bound.RequiredString("referenceIndex");
                _ = bound.RequiredDecimal("notional");
                _ = bound.RequiredDateOnly("effectiveDate");
                _ = bound.RequiredDateOnly("maturityDate");
                break;
            case "StructuredNote":
                _ = bound.RequiredString("issuerName");
                _ = bound.RequiredString("underlyingDescription");
                _ = bound.RequiredDateOnly("maturity");
                break;
        }
    }

    // Unrecognized classes have no schema-bound view. Keep their four tolerant compatibility
    // reads at the call site in their original order; this helper only nests their stored values.
    private static SecurityKind ToUnrecognizedClassKind(
        string assetClass, string? subType, DateOnly? maturity, string? issuerName, string? settlementType)
        => SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
            new MaturityTerms(null, null, ToOption(maturity)),
            new ClassificationDetailTerms(assetClass, ToOption(subType)),
            new IssuerRef(ToOption(issuerName), null, ToOption(settlementType), null, null, null, null)));

}
