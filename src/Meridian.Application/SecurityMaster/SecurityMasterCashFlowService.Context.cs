using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterCashFlowService
{
    public const string ContextCalculationVersion = "bond-cash-flow-context-v5";
    public const string ReviewedAccrualContextCalculationVersion = SecurityRecurringAccrualPolicies.FixedBulletReceiptModelVersion;
    public const string PreviousContextCalculationVersion = "bond-cash-flow-context-v4";
    public const string NegativeInterestContextCalculationVersion = "bond-cash-flow-context-v3";
    public const string UnifiedEconomicsContextCalculationVersion = "bond-cash-flow-context-v2";
    public const string LegacyContextCalculationVersion = "bond-cash-flow-context-v1";

    public async Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(
        Guid securityId, StructuredCashFlowScenario scenario, BondCalculationContext context, CancellationToken ct = default)
    {
        var receipt = await CaptureProjectionAsync(securityId, scenario, context, ct).ConfigureAwait(false);
        return receipt is null && context.KnownAt is not null
            ? new StructuredCashFlowProjectionDto(securityId, StructuredCashFlowSourceKind.CalculatedBullet, scenario,
                context.AsOf, [], BlockedReason: "No retained Security Master terms and source assignment are eligible at the knowledge cutoff.")
            : receipt is null ? null : ExportProjection(receipt, _receiptStore is not null);
    }

    public async Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(
        SecurityDetailDto security, StructuredCashFlowScenario scenario, BondCalculationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(context);
        var source = await _store.GetSourceAsync(security.SecurityId, ct).ConfigureAwait(false);
        if (source is null)
            return null;
        return ExportProjection(await CaptureResolvedProjectionAsync(security, source, scenario, context, ct).ConfigureAwait(false), _receiptStore is not null);
    }

    public async Task<SecurityCashFlowBlockerDto?> GetProjectionBlockerAsync(
        SecurityDetailDto security, BondCalculationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        var source = await _store.GetSourceAsync(security.SecurityId, ct).ConfigureAwait(false);
        if (source is null)
            return null;
        var calculation = await CaptureResolvedProjectionAsync(security, source, StructuredCashFlowScenario.Base,
            context, ct, retainReceipt: false).ConfigureAwait(false);
        if (calculation.Projection.BlockedReason is { } legReason
            && SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CashFlowBehavior == SecurityCashFlowBehavior.SwapLegs
            && SecurityVersionedTerms.ReadDeclaredLegScheduleConventions(security.AssetClass, security.AssetSpecificTerms).Status
                == SecurityDeclaredConventionsStatus.Declared)
            return new("LegConventionsNotFollowed", legReason
                + $" Use /api/security-master/{security.SecurityId:D}/cashflow-projections/preview with calculationProfile='{SecurityVanillaSwapProfile.Id}' for supported vanilla IRS analytics; this route never posts.");
        return calculation.Projection.Blocker ?? (calculation.Projection.BlockedReason is { } reason
            ? new SecurityCashFlowBlockerDto("CashFlowProjectionBlocked", reason)
            : null);
    }

    public async Task<SecurityCashFlowCalculationReceipt?> CaptureProjectionAsync(
        Guid securityId, StructuredCashFlowScenario scenario, BondCalculationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var source = await _store.GetSourceAsync(securityId, ct).ConfigureAwait(false);
        if (source is null)
            return null;
        // A recorded cutoff is deliberately strict: projection-only fallback would silently use
        // current terms. This is timestamp eligibility, not a historical transaction snapshot.
        var security = context.KnownAt is { } cutoff
            ? await _queryService.GetRecordedByIdAsOfAsync(securityId, cutoff, ct).ConfigureAwait(false)
            : await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
            return null;
        return await CaptureResolvedProjectionAsync(security, source, scenario, context, ct).ConfigureAwait(false);
    }

    private async Task<SecurityCashFlowCalculationReceipt> CaptureResolvedProjectionAsync(
        SecurityDetailDto security, SecurityCashFlowSourceDto source, StructuredCashFlowScenario scenario,
        BondCalculationContext context, CancellationToken ct, bool retainReceipt = true)
    {
        // Live inputs must declare their family before any tolerant legacy term reader runs.
        // Refusals remain retainable audit receipts; only verified frozen replay may classify old inputs.
        SecurityCashFlowBlockerDto? formatBlocker = null;
        try
        {
            _ = SecurityTermsEnvelope.ResolveFormat(security.AssetSpecificTerms);
            SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(security.AssetClass, security.AssetSpecificTerms);
        }
        catch (SecurityTermsFormatUndeclaredException exception)
        {
            formatBlocker = new("TermsFormatUndeclared", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            formatBlocker = new("SecurityTermsUnreadable", exception.Message);
        }
        var terms = formatBlocker is null ? StructuredCashFlowTermsResolver.Resolve(security) : StructuredCashFlowTerms.Empty;
        var curves = new Dictionary<string, SecurityForwardCurve>(StringComparer.OrdinalIgnoreCase);
        StructuredCashFlowProjectionDto Blocked(string reason) => new(security.SecurityId, source.SourceKind,
            scenario, context.AsOf, [], SourceLastUpdatedUtc: source.LastUpdatedUtc, TermsUsed: terms, BlockedReason: reason);
        StructuredCashFlowProjectionDto? refused = null;
        if (formatBlocker is null && _bondPolicy is not null && await _bondPolicy.GetBlockerAsync(security, ct).ConfigureAwait(false) is { } policyReason)
            refused = Blocked(policyReason);
        if (formatBlocker is not null)
            refused = Blocked(formatBlocker.Reason) with { Blocker = formatBlocker };
        else if (context.AsOf == default || context.AsOf.Offset != TimeSpan.Zero || context.KnownAt?.Offset != TimeSpan.Zero && context.KnownAt is not null)
            refused = Blocked("Calculation cutoffs must be explicit UTC timestamps.");
        else if (source.SecurityId != security.SecurityId || context.SecurityVersion is { } pinnedVersion && security.Version != pinnedVersion)
            refused = Blocked("The selected Security Master version does not match the calculation context.");
        else if (security.EffectiveFrom > context.AsOf || security.EffectiveTo is { } validTo && validTo <= context.AsOf)
            refused = Blocked("The selected Security Master terms are not effective on the economic calculation date.");
        else if (context.KnownAt is { } cutoff && (source.LastUpdatedUtc is null || source.LastUpdatedUtc > cutoff))
            refused = Blocked("No retained cash-flow source assignment is eligible at the knowledge cutoff; use a retained receipt to replay a replaced assignment.");
        else if (source.SourceKind is not (StructuredCashFlowSourceKind.CalculatedBullet or StructuredCashFlowSourceKind.CalculatedSinker))
            refused = Blocked("Context capture requires calculated cash flows; an external schedule needs its own retained provider receipt.");

        if (refused is null)
        {
            foreach (var index in (terms.Legs ?? []).Where(leg => leg.RateKind == CashFlowLegRateKind.Floating && !string.IsNullOrWhiteSpace(leg.IndexName))
                         .Select(leg => leg.IndexName!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var curve = _forwardCurves is null ? null : await _forwardCurves.GetForwardCurveAsync(index, context, ct).ConfigureAwait(false);
                if (curve is { Points.Count: > 0 })
                    curves.Add(index, curve);
                else if (context.KnownAt is not null || context.Curves?.Any(pin => string.Equals(pin.Role, "Forward:" + index, StringComparison.OrdinalIgnoreCase)) == true)
                    refused = Blocked($"No retained forward-curve input for '{index}' is eligible at the requested cutoffs.");
            }
        }

        var modelVersion = SecurityRecurringAccrualPolicies.IsProfileDeclared(security,
            SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).RecurringAccrualPolicy)
            ? ReviewedAccrualContextCalculationVersion : ContextCalculationVersion;
        var projection = refused ?? CalculateCaptured(security, source, scenario, context, terms, curves,
            reviewedAccrualProfiles: modelVersion == ReviewedAccrualContextCalculationVersion);
        var manifest = BuildInputManifest(security, source, projection, curves.Values, modelVersion);
        projection = projection with { InputManifest = manifest };
        var receipt = new SecurityCashFlowCalculationReceipt(modelVersion, security, source,
            scenario, context with { SecurityVersion = security.Version }, curves.Values.OrderBy(curve => curve.IndexName, StringComparer.Ordinal).ToArray(), projection, string.Empty);
        // Freeze caller-owned lists and JSON documents before exposing a replay receipt.
        receipt = JsonSerializer.Deserialize(JsonSerializer.Serialize(receipt, SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt), SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt)!;
        receipt = receipt with { ContentSha256 = ReceiptHash(receipt) };
        if (retainReceipt && _receiptStore is not null)
            await _receiptStore.RetainAsync(receipt, ct).ConfigureAwait(false);
        return receipt;
    }

    public async Task<StructuredCashFlowProjectionDto?> ReplayProjectionAsync(string contentSha256, CancellationToken ct = default)
    {
        if (_receiptStore is null)
            return null;
        var receipt = await _receiptStore.GetAsync(contentSha256, ct).ConfigureAwait(false);
        return receipt is null ? null : ReplayCapturedProjection(receipt, ct);
    }

    public async Task<StructuredCashFlowProjectionDto> ReplayProjectionAsync(SecurityCashFlowCalculationReceipt receipt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var retained = _receiptStore is null ? null : await _receiptStore.GetAsync(receipt.ContentSha256, ct).ConfigureAwait(false);
        if (retained is null)
            return receipt.Projection with { Schedule = [], LegSchedules = null, BlockedReason = "The calculation receipt is not retained in the configured evidence store." };
        return ReplayCapturedProjection(retained, ct);
    }

    private StructuredCashFlowProjectionDto ReplayCapturedProjection(SecurityCashFlowCalculationReceipt receipt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        StructuredCashFlowProjectionDto Refused(string reason) => receipt.Projection with { Schedule = [], LegSchedules = null, BlockedReason = reason };
        var rowVersion = RowVersionForModel(receipt.ModelVersion);
        if (rowVersion < 0 || !string.Equals(receipt.ContentSha256, ReceiptHash(receipt), StringComparison.Ordinal))
            return Refused("The retained calculation receipt has an unsupported model or its content digest does not match.");
        if (!SecurityCashFlowScheduleRowCodec.HasSupportedRows(receipt.Projection, rowVersion))
            return Refused("The retained calculation receipt has an unsupported schedule row contract.");
        if (receipt.Projection.BlockedReason is not null)
            return receipt.Projection;
        foreach (var calendar in receipt.Projection.ScheduleEvidence?.Calendars ?? [])
        {
            if (_calendars is null || !(_calendars.TryGet(calendar.CalendarId, out var retained)
                    || _calendars.TryResolve(calendar.CalendarId.Split('+'), out retained))
                || !string.Equals(retained.ContentHash, calendar.ContentHash, StringComparison.Ordinal))
                return Refused($"Retained calendar '{calendar.CalendarId}' is unavailable or its content changed; replay is blocked.");
        }
        var curves = receipt.ForwardCurves.ToDictionary(curve => curve.IndexName, StringComparer.OrdinalIgnoreCase);
        // The original immutable packet was verified above. Historical receipts can predate
        // explicit family tags; decode just this retained view through the frozen classifier,
        // leaving the receipt, hash, projection and manifest bytes untouched. Live capture stays strict.
        SecurityDetailDto replaySecurity;
        try
        {
            replaySecurity = receipt.Security with
            {
                AssetSpecificTerms = RetainedSecurityTermsReadBoundary.ReadTerms(
                    receipt.Security.AssetClass, receipt.Security.AssetSpecificTerms)
            };
        }
        catch (InvalidDataException exception) { return Refused(exception.Message); }
        var terms = StructuredCashFlowTermsResolver.Resolve(replaySecurity);
        if (receipt.ModelVersion == LegacyContextCalculationVersion && terms.Legs is not null)
            terms = terms with
            {
                Legs = terms.Legs.Select(leg => leg with
                { CurrentIndexResetDate = null, CurrentIndexRateSource = null }).ToArray()
            };
        var replay = CalculateCaptured(replaySecurity, receipt.Source, receipt.Scenario, receipt.Context, terms, curves, receipt.Projection.Staleness, receipt.ModelVersion != LegacyContextCalculationVersion,
            receipt.ModelVersion is ReviewedAccrualContextCalculationVersion or ContextCalculationVersion or PreviousContextCalculationVersion or NegativeInterestContextCalculationVersion,
            rowVersion, factorAwareEconomics: receipt.ModelVersion is ReviewedAccrualContextCalculationVersion or ContextCalculationVersion,
            reviewedAccrualProfiles: receipt.ModelVersion == ReviewedAccrualContextCalculationVersion)
            with
        { InputManifest = receipt.Projection.InputManifest };
        if (JsonSerializer.Serialize(replay, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowProjectionDto) != JsonSerializer.Serialize(receipt.Projection, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowProjectionDto))
            return Refused("The captured inputs no longer reproduce the retained calculation output.");
        return replay;
    }

    private StructuredCashFlowProjectionDto CalculateCaptured(SecurityDetailDto security, SecurityCashFlowSourceDto source,
        StructuredCashFlowScenario scenario, BondCalculationContext context, StructuredCashFlowTerms terms,
        IReadOnlyDictionary<string, SecurityForwardCurve> curves, StructuredCashFlowStaleness? retainedFreshness = null,
        bool unifiedEconomics = true, bool rejectNegativeInterest = true,
        int rowVersion = SecurityCashFlowScheduleRowCodec.CurrentVersion, bool factorAwareEconomics = true,
        bool reviewedAccrualProfiles = true)
    {
        var assessmentTime = context.KnownAt ?? _clock.GetUtcNow();
        var freshness = retainedFreshness ?? (source.LastUpdatedUtc is not { } updated ? StructuredCashFlowStaleness.Unknown
            : assessmentTime - updated > StalenessThreshold ? StructuredCashFlowStaleness.Stale : StructuredCashFlowStaleness.Fresh);
        var projection = BuildCalculatedProjectionCore(security, source.SourceKind, scenario, freshness,
            source.LastUpdatedUtc, context.AsOf, _calendars, curves, terms, unifiedEconomics, rejectNegativeInterest,
            factorAwareEconomics, reviewedAccrualProfiles);
        projection = SecurityCashFlowScheduleRowCodec.ForVersion(projection, rowVersion);
        var normalized = terms.PrincipalFace is not > 0m && (!terms.HasLegs || terms.Legs!.Any(leg => leg.Notional is not > 0m));
        return normalized ? projection with
        {
            IsNormalizedPer100 = true,
            BlockedReason = projection.BlockedReason ?? "Schedule is normalized per 100 units; retained principal or leg notional is required before ledger posting."
        } : projection;
    }

    private static int RowVersionForModel(string modelVersion) => modelVersion switch
    {
        ReviewedAccrualContextCalculationVersion or ContextCalculationVersion => SecurityCashFlowScheduleRowCodec.CurrentVersion,
        PreviousContextCalculationVersion => 1,
        NegativeInterestContextCalculationVersion or UnifiedEconomicsContextCalculationVersion or LegacyContextCalculationVersion => 0,
        _ => -1
    };

    private static IReadOnlyList<BondCalculationInput> BuildInputManifest(SecurityDetailDto security,
        SecurityCashFlowSourceDto source, StructuredCashFlowProjectionDto projection, IEnumerable<SecurityForwardCurve> curves,
        string modelVersion)
    {
        var inputs = new List<BondCalculationInput>
        {
            new("SecurityTerms", security.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), security.Version,
                Hash(JsonSerializer.SerializeToUtf8Bytes(security, SecurityCashFlowReceiptJsonContext.Default.SecurityDetailDto)), security.SecurityId),
            new("CashFlowSource", source.SourceKind.ToString(), ContentSha256: Hash(JsonSerializer.SerializeToUtf8Bytes(source, SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowSourceDto)), RecordedAt: source.LastUpdatedUtc),
            new("CalculationModel", modelVersion)
        };
        if (projection.Schedule.Any(entry => entry.RateEvidence is not null))
            inputs.Add(new("FloatingCouponModel", BondFloatingRateEvidence.ModelVersion));
        if (modelVersion == ReviewedAccrualContextCalculationVersion
            && SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).RecurringAccrualPolicy is { } policy)
            inputs.Add(new("AccrualPolicy", policy.Id));
        if (SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure && projection.ScheduleEvidence is not null)
            inputs.Add(new("ContractualEconomics", BondContractualEconomics.ModelVersion));
        foreach (var curve in curves.OrderBy(curve => curve.IndexName, StringComparer.Ordinal))
            inputs.Add(curve.RetainedInput ?? new BondCalculationInput("ForwardCurveContent", curve.IndexName,
                ContentSha256: Hash(JsonSerializer.SerializeToUtf8Bytes(curve, SecurityCashFlowReceiptJsonContext.Default.SecurityForwardCurve)), AsOf: new DateTimeOffset(curve.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        if (projection.ScheduleEvidence is { } evidence)
        {
            inputs.Add(new("ScheduleKernel", evidence.KernelVersion));
            inputs.Add(new("ScheduleMapping", evidence.DeclaredMappingVersion));
            inputs.AddRange(evidence.Calendars.Select(calendar => new BondCalculationInput("BusinessDayCalendar", calendar.CalendarId, ContentSha256: calendar.ContentHash)));
        }
        return inputs;
    }

    internal static string ReceiptHash(SecurityCashFlowCalculationReceipt receipt)
        => Hash(JsonSerializer.SerializeToUtf8Bytes(receipt with { ContentSha256 = string.Empty }, SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt));

    private static string Hash(byte[] content) => Sha256Digest.Compute(content);

    private static StructuredCashFlowProjectionDto ExportProjection(SecurityCashFlowCalculationReceipt receipt, bool retained)
        => retained ? receipt.Projection with
        {
            InputManifest = [.. receipt.Projection.InputManifest ?? [],
            new BondCalculationInput("CalculationReceipt", receipt.ContentSha256)]
        } : receipt.Projection;
}
