using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterCashFlowService
{
    public const string ContextCalculationVersion = "bond-cash-flow-context-v1";

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
        BondCalculationContext context, CancellationToken ct)
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var curves = new Dictionary<string, SecurityForwardCurve>(StringComparer.OrdinalIgnoreCase);
        StructuredCashFlowProjectionDto Blocked(string reason) => new(security.SecurityId, source.SourceKind,
            scenario, context.AsOf, [], SourceLastUpdatedUtc: source.LastUpdatedUtc, TermsUsed: terms, BlockedReason: reason);
        StructuredCashFlowProjectionDto? refused = null;
        if (_bondPolicy is not null && await _bondPolicy.GetBlockerAsync(security, ct).ConfigureAwait(false) is { } policyReason)
            refused = Blocked(policyReason);
        if (context.AsOf == default || context.AsOf.Offset != TimeSpan.Zero || context.KnownAt?.Offset != TimeSpan.Zero && context.KnownAt is not null)
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

        var projection = refused ?? CalculateCaptured(security, source, scenario, context, terms, curves);
        var manifest = BuildInputManifest(security, source, projection, curves.Values);
        projection = projection with { InputManifest = manifest };
        var receipt = new SecurityCashFlowCalculationReceipt(ContextCalculationVersion, security, source,
            scenario, context with { SecurityVersion = security.Version }, curves.Values.OrderBy(curve => curve.IndexName, StringComparer.Ordinal).ToArray(), projection, string.Empty);
        // Freeze caller-owned lists and JSON documents before exposing a replay receipt.
        receipt = JsonSerializer.Deserialize(JsonSerializer.Serialize(receipt, SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt), SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt)!;
        receipt = receipt with { ContentSha256 = ReceiptHash(receipt) };
        if (_receiptStore is not null)
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
        if (receipt.ModelVersion != ContextCalculationVersion || !string.Equals(receipt.ContentSha256, ReceiptHash(receipt), StringComparison.Ordinal))
            return Refused("The retained calculation receipt has an unsupported model or its content digest does not match.");
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
        var terms = StructuredCashFlowTermsResolver.Resolve(receipt.Security);
        var replay = CalculateCaptured(receipt.Security, receipt.Source, receipt.Scenario, receipt.Context, terms, curves, receipt.Projection.Staleness)
            with
        { InputManifest = receipt.Projection.InputManifest };
        if (JsonSerializer.Serialize(replay, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowProjectionDto) != JsonSerializer.Serialize(receipt.Projection, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowProjectionDto))
            return Refused("The captured inputs no longer reproduce the retained calculation output.");
        return replay;
    }

    private StructuredCashFlowProjectionDto CalculateCaptured(SecurityDetailDto security, SecurityCashFlowSourceDto source,
        StructuredCashFlowScenario scenario, BondCalculationContext context, StructuredCashFlowTerms terms,
        IReadOnlyDictionary<string, SecurityForwardCurve> curves, StructuredCashFlowStaleness? retainedFreshness = null)
    {
        var assessmentTime = context.KnownAt ?? DateTimeOffset.UtcNow;
        var freshness = retainedFreshness ?? (source.LastUpdatedUtc is not { } updated ? StructuredCashFlowStaleness.Unknown
            : assessmentTime - updated > StalenessThreshold ? StructuredCashFlowStaleness.Stale : StructuredCashFlowStaleness.Fresh);
        var projection = BuildCalculatedProjection(security, source.SourceKind, scenario, freshness,
            source.LastUpdatedUtc, context.AsOf, _calendars, curves, terms);
        var normalized = terms.PrincipalFace is not > 0m && (!terms.HasLegs || terms.Legs!.Any(leg => leg.Notional is not > 0m));
        return normalized ? projection with
        {
            IsNormalizedPer100 = true,
            BlockedReason = projection.BlockedReason ?? "Schedule is normalized per 100 units; retained principal or leg notional is required before ledger posting."
        } : projection;
    }

    private static IReadOnlyList<BondCalculationInput> BuildInputManifest(SecurityDetailDto security,
        SecurityCashFlowSourceDto source, StructuredCashFlowProjectionDto projection, IEnumerable<SecurityForwardCurve> curves)
    {
        var inputs = new List<BondCalculationInput>
        {
            new("SecurityTerms", security.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), security.Version,
                Hash(JsonSerializer.SerializeToUtf8Bytes(security, SecurityCashFlowReceiptJsonContext.Default.SecurityDetailDto)), security.SecurityId),
            new("CashFlowSource", source.SourceKind.ToString(), ContentSha256: Hash(JsonSerializer.SerializeToUtf8Bytes(source, SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowSourceDto)), RecordedAt: source.LastUpdatedUtc),
            new("CalculationModel", ContextCalculationVersion)
        };
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

    private static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static StructuredCashFlowProjectionDto ExportProjection(SecurityCashFlowCalculationReceipt receipt, bool retained)
        => retained ? receipt.Projection with
        {
            InputManifest = [.. receipt.Projection.InputManifest ?? [],
            new BondCalculationInput("CalculationReceipt", receipt.ContentSha256)]
        } : receipt.Projection;
}
