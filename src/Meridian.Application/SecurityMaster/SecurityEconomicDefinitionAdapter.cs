using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static class SecurityEconomicDefinitionAdapter
{
    /// <summary>
    /// Stored classification labels come from the F# domain's explicit name functions, never from the
    /// union's <c>ToString()</c>, which renders <c>OtherFamily "x"</c> / <c>OtherSubType "x"</c> with
    /// their payload syntax and an option-wrapped family as <c>Some(...)</c>.
    /// </summary>
    private static class ClassificationLabels
    {
        public static string AssetClass(SecurityClassification classification)
            => SecurityClassificationModule.assetClassName(classification.AssetClass);

        public static string? Family(SecurityClassification classification)
            => classification.Family is null
                ? null
                : SecurityClassificationModule.familyName(classification.Family.Value);

        public static string SubType(SecurityClassification classification)
            => SecurityClassificationModule.subTypeName(classification.SubType);
    }

    public static SecurityEconomicDefinition ToEconomicDefinition(SecurityProjectionRecord projection)
        => SecurityMasterLegacyUpgrade.toEconomicDefinition(SecurityMasterMapping.ToRecord(projection));

    public static SecurityEconomicDefinitionRecord ToEconomicRecord(SecurityProjectionRecord projection)
        => ToEconomicRecord(ToEconomicDefinition(projection), projection);

    /// <summary>
    /// ADR-027 stage 2 (S2-B3): derives the economic record from a typed domain record already in
    /// hand, skipping the per-write encode → re-decode seam (`ToProjection` → `ToRecord`) the
    /// projection-only overload takes. The command services use this whenever no profile-backed
    /// override replaced the projection's class or terms — there the projection's flat document is
    /// exactly the codec serialization of <paramref name="record"/>, so the derivation is
    /// byte-identical by the replay corpus's amend-cycle pins. Profile-backed overrides and the
    /// replay/rebuild paths keep the projection-only overload: re-deriving from the overridden or
    /// stored document is the authoritative route there, and the projection decode remains the
    /// permanent read-only legacy upcaster.
    /// </summary>
    public static SecurityEconomicDefinitionRecord ToEconomicRecord(SecurityMasterRecord record, SecurityProjectionRecord projection)
        => ToEconomicRecord(SecurityMasterLegacyUpgrade.toEconomicDefinition(record), projection);

    private static SecurityEconomicDefinitionRecord ToEconomicRecord(SecurityEconomicDefinition definition, SecurityProjectionRecord projection)
    {

        // A projection rebuilt from an economic event that predates retained flat terms carries the
        // original economic document under retainedEconomicTerms. Fold it back so modules and fields
        // the flat family cannot express (call and put schedules a row does not declare, redemption,
        // issuer identity, fund and structured-product detail) are written into the next economic
        // event instead of being erased by it. Values the flat revision does express always win, so a
        // declared, non-empty call or put list replaces the retained one.
        var economicTerms = BuildEconomicTermsJson(definition);
        if (SecurityTermsEnvelope.TryGetRetainedEconomicTerms(projection.AssetSpecificTerms, out var retained))
        {
            economicTerms = SecurityTermsEnvelope.FoldRetainedEconomicTerms(economicTerms, retained);
        }

        return new SecurityEconomicDefinitionRecord(
            projection.SecurityId,
            ClassificationLabels.AssetClass(definition.Classification),
            ClassificationLabels.Family(definition.Classification),
            ClassificationLabels.SubType(definition.Classification),
            definition.Classification.TypeName,
            definition.Classification.IssuerType is null ? null : definition.Classification.IssuerType.Value,
            definition.Classification.RiskCountry is null ? null : definition.Classification.RiskCountry.Value,
            projection.Status,
            projection.DisplayName,
            projection.Currency,
            BuildClassificationJson(definition),
            projection.CommonTerms,
            economicTerms,
            projection.Provenance,
            projection.Version,
            projection.EffectiveFrom,
            projection.EffectiveTo,
            projection.Identifiers,
            projection.AssetClass,
            projection.AssetSpecificTerms);
    }

    public static string GetAssetClass(SecurityProjectionRecord projection)
        => ClassificationLabels.AssetClass(ToEconomicDefinition(projection).Classification);

    public static string GetSubType(SecurityProjectionRecord projection)
        => ClassificationLabels.SubType(ToEconomicDefinition(projection).Classification);

    public static SecurityProjectionRecord ToProjection(
        SecurityEconomicDefinitionRecord economic,
        IReadOnlyList<SecurityAliasDto>? aliases = null)
    {
        var legacyAssetClass = string.IsNullOrWhiteSpace(economic.LegacyAssetClass)
            ? economic.TypeName
            : economic.LegacyAssetClass;

        if (economic.LegacyAssetSpecificTerms is null
            && (HasPreferredClassification(economic)
                || SecurityAssetClassCatalog.GetOrDefault(legacyAssetClass).SupportsEquityClassification
                || (SecurityAssetClassCatalog.GetOrDefault(economic.AssetClass).SupportsEquityClassification
                    && !SecurityAssetClassCatalog.GetOrDefault(legacyAssetClass).SupportsProfileBackedTerms)))
        {
            // Structural Common metadata cannot establish the original equity classification, even
            // when every preferred module is absent. Also refuse incomplete imported equity whose
            // TypeName/LegacyAssetClass cannot select the known flat Equity family.
            throw new InvalidOperationException(
                "Equity economic replay requires retained legacy asset-specific terms; " +
                "the economic payload cannot reconstruct the complete classification and preferred/convertible terms.");
        }

        // The legacy slot expects the flat v1 asset-specific-terms family. The retained v1 payload is
        // the authoritative copy and is used whenever the record carries it. When only the structured
        // v2 economic-terms document exists — a stored event written without
        // legacyAssetSpecificTerms, which is the event-replay rebuild path — route it through the
        // upcaster chain so it lands as a v1 payload the guard accepts; putting the raw v2 document
        // here made every read of the row fail with "Unsupported schemaVersion '2'".
        //
        // The flat family is keyed per asset class, so the fallback flattens against the record's
        // asset class: every value the economic document carries (call dates, pool factors, issuer
        // identity, sweep, fund and financing terms) lands at that class's declared flat key. It is
        // still lossy for what the economic document never held — a swap's legs, a bond's
        // subclass, an option's listing detail (SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys)
        // — so the payload is stamped with FlattenedFromMarkerProperty and a projection rebuilt
        // through this route stays distinguishable from one whose flat terms were always present.
        // Retained flat terms are read at the class's current terms version (ADR-024); the flattened
        // branch is already stamped at it.
        var assetSpecificTerms = economic.LegacyAssetSpecificTerms is { } legacyTerms
            ? SecurityAssetClassTermsUpgrader.UpgradeForRead(legacyAssetClass, legacyTerms.Clone())
            : StripRetainedWhenLossless(
                SecurityAssetSpecificTermsUpcasterChain.Normalize(economic.EconomicTerms, legacyAssetClass).Payload,
                economic);
        var primaryIdentifier = economic.Identifiers.FirstOrDefault(identifier => identifier.IsPrimary);

        return new SecurityProjectionRecord(
            economic.SecurityId,
            legacyAssetClass!,
            economic.Status,
            economic.DisplayName,
            economic.Currency,
            primaryIdentifier?.Kind.ToString() ?? string.Empty,
            primaryIdentifier?.Value ?? string.Empty,
            economic.CommonTerms,
            assetSpecificTerms,
            economic.Provenance,
            economic.Version,
            economic.EffectiveFrom,
            economic.EffectiveTo,
            economic.Identifiers,
            aliases ?? Array.Empty<SecurityAliasDto>());
    }

    /// <summary>
    /// ADR-027 stage 1 increment 6: ends <c>retainedEconomicTerms</c> for documents the flat
    /// family holds completely. After the economic-only flatten, the candidate (retained carry
    /// removed) is rebuilt into its next economic document; when that reproduces the source
    /// byte-for-byte, the flat row IS the document and the carry adds nothing, so it is dropped.
    /// Byte equality is deliberately the strictest test: image documents written by the current
    /// serializer pass; historical shapes (the pre-increment-5 module set, different member
    /// forms) and hand-shaped documents fail and keep the carry with its fold-on-read, and any
    /// exception on the trial rebuild fails closed to keeping it. The single tolerated
    /// difference is a module key added after this gate shipped that the image predates and the
    /// rebuild emits null (<see cref="EqualsBeforeLaterModuleKeys"/>), so serializer growth does
    /// not undo the retirement for images already stored. No stored payload is rewritten — this
    /// changes only what a freshly rebuilt projection carries.
    /// </summary>
    private static JsonElement StripRetainedWhenLossless(JsonElement flattened, SecurityEconomicDefinitionRecord economic)
    {
        try
        {
            if (!SecurityTermsEnvelope.TryGetRetainedEconomicTerms(flattened, out _))
            {
                return flattened;
            }

            var candidateTerms = RemoveRetainedKey(flattened);
            var candidate = ToProjection(economic with { LegacyAssetSpecificTerms = candidateTerms });
            var rebuilt = ToEconomicRecord(candidate).EconomicTerms;
            return string.Equals(rebuilt.GetRawText(), economic.EconomicTerms.GetRawText(), StringComparison.Ordinal)
                   || EqualsBeforeLaterModuleKeys(rebuilt, economic.EconomicTerms)
                ? candidateTerms
                : flattened;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException or JsonException)
        {
            return flattened;
        }
    }

    /// <summary>
    /// The one difference the gate tolerates: a module key the serializer gained after the gate
    /// shipped (<see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.ModulesAddedAfterIdentityGate"/>)
    /// that the source image predates and the rebuild emits null. Such a rebuild carries nothing
    /// the source does not, so the carry is still redundant. A later-module key the rebuild
    /// populates, or one the source itself carries, is compared as written.
    /// </summary>
    private static bool EqualsBeforeLaterModuleKeys(JsonElement rebuilt, JsonElement source)
    {
        if (rebuilt.ValueKind != JsonValueKind.Object || source.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var node = JsonNode.Parse(rebuilt.GetRawText())!.AsObject();
        var removed = false;
        foreach (var module in SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.ModulesAddedAfterIdentityGate)
        {
            if (!source.TryGetProperty(module, out _)
                && node.TryGetPropertyValue(module, out var value)
                && value is null)
            {
                node.Remove(module);
                removed = true;
            }
        }

        return removed
               && string.Equals(
                   JsonSerializer.SerializeToElement(node).GetRawText(),
                   source.GetRawText(),
                   StringComparison.Ordinal);
    }

    private static JsonElement RemoveRetainedKey(JsonElement flattened)
    {
        var node = JsonNode.Parse(flattened.GetRawText())!.AsObject();
        node.Remove(SecurityTermsEnvelope.RetainedEconomicTermsKey);
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Keeps a retained economic-terms document attached across a revision rebuilt through the
    /// domain codec. The codec serializes only declared flat keys, so without this an amendment or
    /// deactivation of a projection rebuilt from an economic-only event would drop the retained
    /// document and, with it, every economic value the flat keys cannot hold.
    /// </summary>
    public static SecurityProjectionRecord CarryRetainedEconomicTerms(
        SecurityProjectionRecord previous,
        SecurityProjectionRecord next)
    {
        if (!SecurityTermsEnvelope.TryGetRetainedEconomicTerms(previous.AssetSpecificTerms, out _)
            || SecurityTermsEnvelope.TryGetRetainedEconomicTerms(next.AssetSpecificTerms, out _))
        {
            return next;
        }

        return next with
        {
            AssetSpecificTerms = SecurityTermsEnvelope.CarryRetainedEconomicTerms(previous.AssetSpecificTerms, next.AssetSpecificTerms)
        };
    }

    internal static bool HasPreferredClassification(SecurityEconomicDefinitionRecord economic)
    {
        // Retained events may still carry the legacy F# rendering (Some(PreferredEquity),
        // OtherSubType "..."), so labels are decoded before matching; the payload is not rewritten.
        static bool IsPreferredLabel(string? stored)
            => ClassificationLabelReader.Normalize(stored) is { } value
               && (value.Equals("PreferredEquity", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("PreferredShare", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("Preferred", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("ConvertiblePreferred", StringComparison.OrdinalIgnoreCase));

        return IsPreferredLabel(economic.AssetFamily)
               || IsPreferredLabel(economic.SubType)
               || IsPreferredLabel(economic.TypeName)
               || (economic.Classification.ValueKind == JsonValueKind.Object
                   && economic.Classification.EnumerateObject().Any(property =>
                       property.Name is "assetFamily" or "subType" or "typeName" or "classification"
                       && property.Value.ValueKind == JsonValueKind.String
                       && IsPreferredLabel(property.Value.GetString())));
    }

    private static JsonElement BuildClassificationJson(SecurityEconomicDefinition definition)
        => JsonSerializer.SerializeToElement(new
        {
            assetClass = ClassificationLabels.AssetClass(definition.Classification),
            assetFamily = ClassificationLabels.Family(definition.Classification),
            subType = ClassificationLabels.SubType(definition.Classification),
            typeName = definition.Classification.TypeName,
            issuerType = definition.Classification.IssuerType is null ? null : definition.Classification.IssuerType.Value,
            riskCountry = definition.Classification.RiskCountry is null ? null : definition.Classification.RiskCountry.Value
        });

    private static JsonElement BuildEconomicTermsJson(SecurityEconomicDefinition definition)
    {
        var document = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = SecurityMasterSchemaVersions.EconomicTerms,
            termsFormat = SecurityTermsFormats.Economic,
            maturity = definition.Terms.Maturity is null ? null : new
            {
                effectiveDate = definition.Terms.Maturity.Value.EffectiveDate,
                issueDate = definition.Terms.Maturity.Value.IssueDate,
                maturityDate = definition.Terms.Maturity.Value.MaturityDate
            },
            coupon = definition.Terms.Coupon is null ? null : new
            {
                couponType = definition.Terms.Coupon.Value.CouponType is null ? null : CouponKindModule.Label(definition.Terms.Coupon.Value.CouponType.Value),
                couponRate = definition.Terms.Coupon.Value.CouponRate,
                paymentFrequency = definition.Terms.Coupon.Value.PaymentFrequency is null ? null : PaymentFrequencyModule.Label(definition.Terms.Coupon.Value.PaymentFrequency.Value),
                dayCount = definition.Terms.Coupon.Value.DayCount is null ? null : DayCountConventionModule.Label(definition.Terms.Coupon.Value.DayCount.Value)
            },
            discount = definition.Terms.Discount is null ? null : new
            {
                discountRate = definition.Terms.Discount.Value.DiscountRate,
                yieldRate = definition.Terms.Discount.Value.YieldRate
            },
            accrual = definition.Terms.Accrual is null ? null : new
            {
                accrualMethod = definition.Terms.Accrual.Value.AccrualMethod is null ? null : definition.Terms.Accrual.Value.AccrualMethod.Value,
                accrualStartDate = definition.Terms.Accrual.Value.AccrualStartDate,
                exDividendDays = definition.Terms.Accrual.Value.ExDividendDays,
                businessDayConvention = definition.Terms.Accrual.Value.BusinessDayConvention is null ? null : definition.Terms.Accrual.Value.BusinessDayConvention.Value,
                holidayCalendar = definition.Terms.Accrual.Value.HolidayCalendar is null ? null : definition.Terms.Accrual.Value.HolidayCalendar.Value,
                dayCount = definition.Terms.Accrual.Value.DayCount is null ? null : DayCountConventionModule.Label(definition.Terms.Accrual.Value.DayCount.Value)
            },
            payment = definition.Terms.Payment is null ? null : new
            {
                paymentFrequency = definition.Terms.Payment.Value.PaymentFrequency is null ? null : PaymentFrequencyModule.Label(definition.Terms.Payment.Value.PaymentFrequency.Value),
                paymentLagDays = definition.Terms.Payment.Value.PaymentLagDays,
                paymentCurrency = definition.Terms.Payment.Value.PaymentCurrency is null ? null : definition.Terms.Payment.Value.PaymentCurrency.Value
            },
            redemption = definition.Terms.Redemption is null ? null : new
            {
                redemptionType = definition.Terms.Redemption.Value.RedemptionType is null ? null : RedemptionStyleModule.Label(definition.Terms.Redemption.Value.RedemptionType.Value),
                redemptionPrice = definition.Terms.Redemption.Value.RedemptionPrice,
                isBullet = definition.Terms.Redemption.Value.IsBullet,
                isAmortizing = definition.Terms.Redemption.Value.IsAmortizing
            },
            call = definition.Terms.Call is null ? null : new
            {
                isCallable = definition.Terms.Call.Value.IsCallable,
                firstCallDate = definition.Terms.Call.Value.FirstCallDate,
                callPrice = definition.Terms.Call.Value.CallPrice,
                callSchedule = definition.Terms.Call.Value.CallSchedule
                    .Select(e => new { callDt = e.CallDt, callPx = e.CallPx, isParCall = e.IsParCall, callType = e.CallType is null ? null : e.CallType.Value })
                    .ToArray(),
                makeWholeSpreadBps = definition.Terms.Call.Value.MakeWholeSpreadBps,
                isPuttable = definition.Terms.Call.Value.IsPuttable,
                putSchedule = definition.Terms.Call.Value.PutSchedule
                    .Select(e => new { putDt = e.PutDt, putPx = e.PutPx })
                    .ToArray()
            },
            auction = definition.Terms.Auction is null ? null : new
            {
                auctionDate = definition.Terms.Auction.Value.AuctionDate,
                auctionType = definition.Terms.Auction.Value.AuctionType is null ? null : definition.Terms.Auction.Value.AuctionType.Value
            },
            sweep = definition.Terms.Sweep is null ? null : new
            {
                programName = definition.Terms.Sweep.Value.ProgramName is null ? null : definition.Terms.Sweep.Value.ProgramName.Value,
                sweepVehicleType = definition.Terms.Sweep.Value.SweepVehicleType is null ? null : SweepVehicleModule.Label(definition.Terms.Sweep.Value.SweepVehicleType.Value),
                sweepFrequency = definition.Terms.Sweep.Value.SweepFrequency is null ? null : PaymentFrequencyModule.Label(definition.Terms.Sweep.Value.SweepFrequency.Value),
                targetAccountType = definition.Terms.Sweep.Value.TargetAccountType is null ? null : definition.Terms.Sweep.Value.TargetAccountType.Value
            },
            financing = definition.Terms.Financing is null ? null : new
            {
                counterparty = definition.Terms.Financing.Value.Counterparty is null ? null : definition.Terms.Financing.Value.Counterparty.Value,
                collateralType = definition.Terms.Financing.Value.CollateralType is null ? null : definition.Terms.Financing.Value.CollateralType.Value,
                haircut = definition.Terms.Financing.Value.Haircut,
                openDate = definition.Terms.Financing.Value.OpenDate,
                closeDate = definition.Terms.Financing.Value.CloseDate
            },
            issuer = definition.Terms.Issuer is null ? null : new
            {
                issuerName = definition.Terms.Issuer.Value.IssuerName is null ? null : definition.Terms.Issuer.Value.IssuerName.Value,
                institutionName = definition.Terms.Issuer.Value.InstitutionName is null ? null : definition.Terms.Issuer.Value.InstitutionName.Value,
                issuerProgram = definition.Terms.Issuer.Value.IssuerProgram is null ? null : definition.Terms.Issuer.Value.IssuerProgram.Value,
                leiCode = definition.Terms.Issuer.Value.LeiCode is null ? null : definition.Terms.Issuer.Value.LeiCode.Value,
                ultimateParentName = definition.Terms.Issuer.Value.UltimateParentName is null ? null : definition.Terms.Issuer.Value.UltimateParentName.Value,
                issuerSector = definition.Terms.Issuer.Value.IssuerSector is null ? null : definition.Terms.Issuer.Value.IssuerSector.Value,
                issuerCountry = definition.Terms.Issuer.Value.IssuerCountry is null ? null : definition.Terms.Issuer.Value.IssuerCountry.Value
            },
            equityBehavior = definition.Terms.EquityBehavior is null ? null : new
            {
                shareClass = definition.Terms.EquityBehavior.Value.ShareClass is null ? null : definition.Terms.EquityBehavior.Value.ShareClass.Value,
                votingRights = definition.Terms.EquityBehavior.Value.VotingRights is null ? null : definition.Terms.EquityBehavior.Value.VotingRights.Value,
                distributionType = definition.Terms.EquityBehavior.Value.DistributionType is null ? null : DistributionPolicyModule.Label(definition.Terms.EquityBehavior.Value.DistributionType.Value)
            },
            fund = definition.Terms.Fund is null ? null : new
            {
                fundFamily = definition.Terms.Fund.Value.FundFamily is null ? null : definition.Terms.Fund.Value.FundFamily.Value,
                weightedAverageMaturityDays = definition.Terms.Fund.Value.WeightedAverageMaturityDays,
                sweepEligible = definition.Terms.Fund.Value.SweepEligible,
                liquidityFeeEligible = definition.Terms.Fund.Value.LiquidityFeeEligible
            },
            structuredProduct = definition.Terms.StructuredProduct is null ? null : new
            {
                factor = definition.Terms.StructuredProduct.Value.Factor,
                factorDate = definition.Terms.StructuredProduct.Value.FactorDate,
                weightedAvgCoupon = definition.Terms.StructuredProduct.Value.WeightedAvgCoupon,
                weightedAvgMaturityMonths = definition.Terms.StructuredProduct.Value.WeightedAvgMaturityMonths,
                weightedAvgLoanAgeMos = definition.Terms.StructuredProduct.Value.WeightedAvgLoanAgeMos,
                collateralType = definition.Terms.StructuredProduct.Value.CollateralType is null ? null : definition.Terms.StructuredProduct.Value.CollateralType.Value,
                poolIdentifier = definition.Terms.StructuredProduct.Value.PoolIdentifier is null ? null : definition.Terms.StructuredProduct.Value.PoolIdentifier.Value,
                trancheClass = definition.Terms.StructuredProduct.Value.TrancheClass is null ? null : definition.Terms.StructuredProduct.Value.TrancheClass.Value,
                prepaymentAssumption = definition.Terms.StructuredProduct.Value.PrepaymentAssumption is null ? null : SerializePrepaymentModel(definition.Terms.StructuredProduct.Value.PrepaymentAssumption.Value),
                averageLifeYears = definition.Terms.StructuredProduct.Value.AverageLifeYears,
                isInterestOnly = definition.Terms.StructuredProduct.Value.IsInterestOnly,
                isPrincipalOnly = definition.Terms.StructuredProduct.Value.IsPrincipalOnly,
                notionalBalance = definition.Terms.StructuredProduct.Value.NotionalBalance,
                originator = definition.Terms.StructuredProduct.Value.Originator is null ? null : definition.Terms.StructuredProduct.Value.Originator.Value,
                creditEnhancementPct = definition.Terms.StructuredProduct.Value.CreditEnhancementPct,
                factorScheduleReference = definition.Terms.StructuredProduct.Value.FactorScheduleReference is null ? null : definition.Terms.StructuredProduct.Value.FactorScheduleReference.Value
            },
            // ADR-027 stage 1, increment 5: the eleven modules the serializer historically never
            // wrote. Every module key is emitted (null when the definition has no such module), the
            // same contract the first fourteen keep, so the economic family stops depending on the
            // retained flat document for them.
            floatingRate = definition.Terms.FloatingRate is null ? null : new
            {
                referenceIndex = definition.Terms.FloatingRate.Value.ReferenceIndex is null ? null : definition.Terms.FloatingRate.Value.ReferenceIndex.Value,
                spreadBps = definition.Terms.FloatingRate.Value.SpreadBps,
                resetFrequency = definition.Terms.FloatingRate.Value.ResetFrequency is null ? null : definition.Terms.FloatingRate.Value.ResetFrequency.Value,
                floorRate = definition.Terms.FloatingRate.Value.FloorRate,
                capRate = definition.Terms.FloatingRate.Value.CapRate
            },
            tradingParameters = definition.Terms.TradingParameters is null ? null : new
            {
                lotSize = definition.Terms.TradingParameters.Value.LotSize,
                tickSize = definition.Terms.TradingParameters.Value.TickSize,
                contractMultiplier = definition.Terms.TradingParameters.Value.ContractMultiplier,
                marginRequirementPct = definition.Terms.TradingParameters.Value.MarginRequirementPct,
                tradingHoursUtc = definition.Terms.TradingParameters.Value.TradingHoursUtc is null ? null : definition.Terms.TradingParameters.Value.TradingHoursUtc.Value,
                circuitBreakerThresholdPct = definition.Terms.TradingParameters.Value.CircuitBreakerThresholdPct
            },
            creditRating = definition.Terms.CreditRating is null ? null : new
            {
                ratings = definition.Terms.CreditRating.Value.Ratings
                    .Select(rating => new
                    {
                        agency = rating.Agency,
                        grade = rating.Grade,
                        outlook = rating.Outlook is null ? null : rating.Outlook.Value,
                        issuanceLevel = rating.IssuanceLevel is null ? null : rating.IssuanceLevel.Value,
                        asOfDt = rating.AsOfDt
                    })
                    .ToArray(),
                compositeRating = definition.Terms.CreditRating.Value.CompositeRating is null ? null : definition.Terms.CreditRating.Value.CompositeRating.Value,
                isInvestmentGrade = definition.Terms.CreditRating.Value.IsInvestmentGrade,
                isHighYield = definition.Terms.CreditRating.Value.IsHighYield
            },
            depositaryReceipt = definition.Terms.DepositaryReceipt is null ? null : new
            {
                underlyingSecurityId = definition.Terms.DepositaryReceipt.Value.UnderlyingSecurityId is null ? (Guid?)null : definition.Terms.DepositaryReceipt.Value.UnderlyingSecurityId.Value.Item,
                depositaryBank = definition.Terms.DepositaryReceipt.Value.DepositaryBank is null ? null : definition.Terms.DepositaryReceipt.Value.DepositaryBank.Value,
                ordinaryShareRatio = definition.Terms.DepositaryReceipt.Value.OrdinaryShareRatio,
                underlyingExchangeMic = definition.Terms.DepositaryReceipt.Value.UnderlyingExchangeMic is null ? null : definition.Terms.DepositaryReceipt.Value.UnderlyingExchangeMic.Value,
                underlyingCurrency = definition.Terms.DepositaryReceipt.Value.UnderlyingCurrency is null ? null : definition.Terms.DepositaryReceipt.Value.UnderlyingCurrency.Value,
                underlyingIsin = definition.Terms.DepositaryReceipt.Value.UnderlyingIsin is null ? null : definition.Terms.DepositaryReceipt.Value.UnderlyingIsin.Value,
                underlyingCountry = definition.Terms.DepositaryReceipt.Value.UnderlyingCountry is null ? null : definition.Terms.DepositaryReceipt.Value.UnderlyingCountry.Value,
                cusipOfOrdinary = definition.Terms.DepositaryReceipt.Value.CusipOfOrdinary is null ? null : definition.Terms.DepositaryReceipt.Value.CusipOfOrdinary.Value
            },
            dividendSchedule = definition.Terms.DividendSchedule is null ? null : new
            {
                dividendFrequency = DividendFrequencyModule.Label(definition.Terms.DividendSchedule.Value.DividendFrequency),
                annualDividendRate = definition.Terms.DividendSchedule.Value.AnnualDividendRate,
                lastExDate = definition.Terms.DividendSchedule.Value.LastExDate,
                lastPayDate = definition.Terms.DividendSchedule.Value.LastPayDate,
                lastDividendPerShare = definition.Terms.DividendSchedule.Value.LastDividendPerShare,
                dividendCurrency = definition.Terms.DividendSchedule.Value.DividendCurrency is null ? null : definition.Terms.DividendSchedule.Value.DividendCurrency.Value,
                isQualifiedDividend = definition.Terms.DividendSchedule.Value.IsQualifiedDividend
            },
            covenants = definition.Terms.Covenants is null ? null : new
            {
                covenants = definition.Terms.Covenants.Value.Covenants
                    .Select(covenant => new
                    {
                        kind = CovenantKindModule.Label(covenant.Kind),
                        description = covenant.Description,
                        threshold = covenant.Threshold,
                        metric = covenant.Metric is null ? null : covenant.Metric.Value,
                        testFrequency = covenant.TestFrequency is null ? null : covenant.TestFrequency.Value
                    })
                    .ToArray()
            },
            esg = definition.Terms.Esg is null ? null : new
            {
                esgScore = definition.Terms.Esg.Value.EsgScore,
                esgProvider = definition.Terms.Esg.Value.EsgProvider is null ? null : definition.Terms.Esg.Value.EsgProvider.Value,
                esgRating = definition.Terms.Esg.Value.EsgRating is null ? null : definition.Terms.Esg.Value.EsgRating.Value,
                esgAsOf = definition.Terms.Esg.Value.EsgAsOf,
                isGreenBond = definition.Terms.Esg.Value.IsGreenBond,
                isSocialBond = definition.Terms.Esg.Value.IsSocialBond,
                isSustainabilityLinked = definition.Terms.Esg.Value.IsSustainabilityLinked,
                unSdgAlignments = definition.Terms.Esg.Value.UNSDGAlignments.ToArray(),
                paiIndicatorFlags = definition.Terms.Esg.Value.PaiIndicatorFlags.ToArray()
            },
            venue = definition.Terms.Venue is null ? null : new
            {
                isListedOnExchange = definition.Terms.Venue.Value.IsListedOnExchange,
                primaryExchMic = definition.Terms.Venue.Value.PrimaryExchMic is null ? null : definition.Terms.Venue.Value.PrimaryExchMic.Value,
                executionVenueMic = definition.Terms.Venue.Value.ExecutionVenueMic is null ? null : definition.Terms.Venue.Value.ExecutionVenueMic.Value,
                ccpName = definition.Terms.Venue.Value.CcpName is null ? null : definition.Terms.Venue.Value.CcpName.Value,
                otcProtocol = definition.Terms.Venue.Value.OtcProtocol is null ? null : definition.Terms.Venue.Value.OtcProtocol.Value
            },
            multiCalendar = definition.Terms.MultiCalendar is null ? null : new
            {
                calendars = definition.Terms.MultiCalendar.Value.Calendars
                    .Select(calendar => new { calendarId = calendar.CalendarId, calendarPurpose = calendar.CalendarPurpose })
                    .ToArray()
            },
            inflationLinked = definition.Terms.InflationLinked is null ? null : new
            {
                inflationIndex = definition.Terms.InflationLinked.Value.InflationIndex is null ? null : definition.Terms.InflationLinked.Value.InflationIndex.Value,
                baseIndexValue = definition.Terms.InflationLinked.Value.BaseIndexValue,
                baseDate = definition.Terms.InflationLinked.Value.BaseDate,
                currentFactor = definition.Terms.InflationLinked.Value.CurrentFactor,
                factorDate = definition.Terms.InflationLinked.Value.FactorDate,
                indexFloor = definition.Terms.InflationLinked.Value.IndexFloor
            },
            sinkingFund = definition.Terms.SinkingFund is null ? null : new
            {
                schedule = definition.Terms.SinkingFund.Value.Schedule
                    .Select(instalment => new { sinkDate = instalment.SinkDate, amount = instalment.Amount })
                    .ToArray(),
                sinkFrequency = definition.Terms.SinkingFund.Value.SinkFrequency is null ? null : definition.Terms.SinkingFund.Value.SinkFrequency.Value,
                isProRata = definition.Terms.SinkingFund.Value.IsProRata
            },
            // ADR-027 stage 3 (S3-E): module homes for the codec-only field complement. Same
            // contract as every other module: the key is always emitted, null when unset.
            exercise = definition.Terms.Exercise is null ? null : new
            {
                optionRight = definition.Terms.Exercise.Value.OptionRight is null ? null : definition.Terms.Exercise.Value.OptionRight.Value,
                strikePrice = definition.Terms.Exercise.Value.StrikePrice,
                strikeRate = definition.Terms.Exercise.Value.StrikeRate,
                exerciseStyle = definition.Terms.Exercise.Value.ExerciseStyle is null ? null : definition.Terms.Exercise.Value.ExerciseStyle.Value,
                underlyingSecurityId = definition.Terms.Exercise.Value.UnderlyingSecurityId is null ? (Guid?)null : definition.Terms.Exercise.Value.UnderlyingSecurityId.Value.Item,
                premiumAmount = definition.Terms.Exercise.Value.PremiumAmount
            },
            currencyPair = definition.Terms.CurrencyPair is null ? null : new
            {
                baseCurrency = definition.Terms.CurrencyPair.Value.BaseCurrency is null ? null : definition.Terms.CurrencyPair.Value.BaseCurrency.Value,
                quoteCurrency = definition.Terms.CurrencyPair.Value.QuoteCurrency is null ? null : definition.Terms.CurrencyPair.Value.QuoteCurrency.Value,
                settlementCurrency = definition.Terms.CurrencyPair.Value.SettlementCurrency is null ? null : definition.Terms.CurrencyPair.Value.SettlementCurrency.Value,
                forwardRate = definition.Terms.CurrencyPair.Value.ForwardRate
            },
            notional = definition.Terms.Notional is null ? null : new
            {
                notional = definition.Terms.Notional.Value.Notional,
                denomination = definition.Terms.Notional.Value.Denomination,
                committedAmount = definition.Terms.Notional.Value.CommittedAmount,
                fundedAmount = definition.Terms.Notional.Value.FundedAmount,
                unfundedAmount = definition.Terms.Notional.Value.UnfundedAmount
            },
            profileEnvelope = definition.Terms.ProfileEnvelope is null ? null : new
            {
                customProfileId = definition.Terms.ProfileEnvelope.Value.CustomProfileId,
                profileVersion = definition.Terms.ProfileEnvelope.Value.ProfileVersion,
                terms = EmbedProfileDocument(definition.Terms.ProfileEnvelope.Value.TermsJson)
            }
        });
        // Keep byte-identical economics for records without a dated history. The additive field
        // carries real typed observations; no accounting reader needs the retained flat copy.
        if (definition.Terms.StructuredProduct is { } structured && !structured.Value.FactorScheduleEntries.IsEmpty)
        {
            var expanded = JsonNode.Parse(document.GetRawText())!;
            expanded["structuredProduct"]!["factorScheduleEntries"] = JsonSerializer.SerializeToNode(
                structured.Value.FactorScheduleEntries.Select(entry => new
                {
                    asOfDate = entry.AsOfDate,
                    factor = entry.Factor
                }));
            return JsonSerializer.SerializeToElement(expanded);
        }
        return document;
    }

    /// <summary>
    /// The profile-governed terms document as the economic document carries it: embedded as JSON
    /// when it is JSON, so it stays one queryable document rather than a string inside one. Text
    /// that does not parse is carried as a JSON string, so an invalid document is preserved for
    /// the envelope validation to refuse instead of being dropped here.
    /// </summary>
    private static JsonElement EmbedProfileDocument(string termsJson)
    {
        if (!string.IsNullOrWhiteSpace(termsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(termsJson);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
            }
        }

        return JsonSerializer.SerializeToElement(termsJson ?? string.Empty);
    }

    /// Maps a <see cref="VotingRightsCat"/> DU to a stable wire token.
    /// For <c>OtherVotingRights</c>, the payload string is used directly as the token.
    private static string SerializeVotingRightsCat(VotingRightsCat cat)
    {
        if (cat.IsOtherVotingRights)
            return ((VotingRightsCat.OtherVotingRights)cat).Item;
        if (cat.IsFullVoting)
            return "FullVoting";
        if (cat.IsLimitedVoting)
            return "LimitedVoting";
        if (cat.IsNonVoting)
            return "NonVoting";
        if (cat.IsDualClass)
            return "DualClass";
        if (cat.IsSuperVoting)
            return "SuperVoting";
        throw new InvalidOperationException($"Unsupported {nameof(VotingRightsCat)} case encountered during serialization.");
    }

    private static object SerializePrepaymentModel(PrepaymentModel model)
    {
        if (model.IsPsa)
            return new { model = "Psa", speed = ((PrepaymentModel.Psa)model).speed };
        if (model.IsCpr)
            return new { model = "Cpr", annualRate = ((PrepaymentModel.Cpr)model).annualRate };
        if (model.IsSmm)
            return new { model = "Smm", monthlyRate = ((PrepaymentModel.Smm)model).monthlyRate };
        throw new InvalidOperationException($"Unsupported {nameof(PrepaymentModel)} case encountered during serialization.");
    }
}
