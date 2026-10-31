using System.Text.Json;
using Meridian.Application.DirectLending;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>Adapts retained product facts to the shared calculation contract without mutating them.</summary>
public sealed class ContractualCashFlowPreviewService(
    ISecurityMasterQueryService securities,
    ISecurityMasterCashFlowService sources,
    ISecurityCurveStore? curveStore = null,
    IBusinessDayCalendarSource? calendars = null,
    IDirectLendingService? loans = null) : IContractualCashFlowPreviewService
{
    public async Task<ContractualCashFlowResult?> PreviewSecurityAsync(Guid securityId, ContractualCashFlowRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ContractualCashFlowEngine.ValidateRequest(request);
        var security = await securities.GetRecordedByIdAsOfAsync(securityId, request.KnowledgeCutoff, ct).ConfigureAwait(false);
        if (security is null)
            return null;
        if (DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime) > request.ValuationDate)
            return Blocked(request, "TermsNotEffective", "The retained security terms are not effective on the valuation date.");
        var source = await sources.GetCashFlowSourceAsync(securityId, ct).ConfigureAwait(false);
        if (source is not null && source.SourceKind is not (StructuredCashFlowSourceKind.CalculatedBullet or StructuredCashFlowSourceKind.CalculatedSinker))
            return Blocked(request, "AuthoritativeProviderAssigned", "The assigned provider/client schedule cannot be replaced or prepaid again by the calculated preview.");
        ContractualCashFlowContract contract;
        try
        { contract = request.ContractOverride ?? FromSecurity(security); }
        catch (ContractInputUnavailable ex) { return Blocked(request, "ContractInputsUnavailable", ex.Message); }
        catch (SecurityScheduleValidationException ex) { return Blocked(request, "ContractInputsUnavailable", ex.Message); }
        return await CalculateAsync(contract, request, $"security:{securityId}:version:{security.Version}",
            ContractualCashFlowEngine.Hash(new { security.CommonTerms, security.AssetSpecificTerms }), ct).ConfigureAwait(false);
    }

    public async Task<ContractualCashFlowResult?> PreviewLoanAsync(Guid loanId, ContractualCashFlowRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ContractualCashFlowEngine.ValidateRequest(request);
        if (loans is null)
            return Blocked(request, "LoanServiceUnavailable", "Direct-lending service is unavailable.");
        var loan = await loans.GetLoanAsync(loanId, ct).ConfigureAwait(false);
        if (loan is null)
            return null;
        var version = loan.TermsVersions.Where(v => v.RecordedAt <= request.KnowledgeCutoff).OrderBy(v => v.VersionNumber).LastOrDefault();
        if (version is null)
            return Blocked(request, "LoanTermsUnavailable", "No retained loan terms are eligible at the knowledge cutoff.");
        var servicing = await loans.GetServicingStateAsync(loanId, ct).ConfigureAwait(false);
        if (request.ContractOverride is null && (servicing is null || servicing.RevisionHistory.Any(r => r.CreatedAt > request.KnowledgeCutoff || r.EffectiveAsOfDate > request.ValuationDate)))
            return Blocked(request, "HistoricalServicingUnavailable", "Current servicing includes facts outside the requested cutoffs; an authoritative historical balance is required.");
        ContractualCashFlowContract contract;
        try
        {
            if (request.ContractOverride is null && loan.TermsVersions.Count > 1)
                throw new ContractInputUnavailable("Amended loan accrual requires an explicit dated contractual history.");
            contract = request.ContractOverride ?? FromLoan(version.Terms, servicing!);
            if (request.ContractOverride is null)
                contract = contract with
                {
                    Streams = contract.Streams.Select(s => s with
                    {
                        OpeningBalance = servicing!.Balances.PrincipalOutstanding,
                        OpeningBalanceDate = request.ValuationDate
                    }).ToArray()
                };
        }
        catch (ContractInputUnavailable ex) { return Blocked(request, "ContractInputsUnavailable", ex.Message); }
        return await CalculateAsync(contract, request, $"loan:{loanId}:terms:{version.VersionNumber}:servicing:{servicing?.ServicingRevision}", version.TermsHash, ct).ConfigureAwait(false);
    }

    private async Task<ContractualCashFlowResult> CalculateAsync(ContractualCashFlowContract contract, ContractualCashFlowRequest request,
        string version, string termsHash, CancellationToken ct)
    {
        if (request.ContractOverride is null)
        {
            try
            {
                ContractualCashFlowEngine.Validate(contract, request with { Exercises = null, Prepayments = null, Horizon = null, CurvePins = null });
            }
            catch (ArgumentException) { return Blocked(request, "RetainedContractInvalid", "Retained contractual inputs do not form a valid calculation contract."); }
        }
        ContractualCashFlowEngine.Validate(contract, request);
        if ((request.Exercises ?? []).Any(e => !e.HypotheticalNotice))
            return Blocked(request, "NoticeEvidenceVerificationUnavailable", "Retained notices require source verification; this calculation endpoint currently supports hypothetical notices only.");
        var curves = new List<CashFlowCurveSnapshot>();
        foreach (var pin in request.CurvePins ?? [])
        {
            if (curveStore is null)
                return Blocked(request, "CurveStoreUnavailable", "No curve store is configured.");
            var definition = await curveStore.GetDefinitionAsync(pin.CurveId, ct).ConfigureAwait(false);
            var curve = await curveStore.GetVersionByIdAsync(pin.CurveId, pin.CurveVersionId, ct).ConfigureAwait(false);
            if (definition is null || curve is null)
                return Blocked(request, "PinnedCurveUnavailable", "A pinned curve definition or version is unavailable.");
            if (curve.AsOf > request.ValuationDate || curve.RecordedAt > request.KnowledgeCutoff)
                return Blocked(request, "CurveOutsideCutoff", "Pinned curve is after the valuation date or knowledge cutoff.");
            if (!string.Equals(definition.Currency, contract.Currency, StringComparison.OrdinalIgnoreCase))
                return Blocked(request, "CurveCurrencyMismatch", "Pinned benchmark currency differs from the contract currency.");
            // Roles belong to request pins; retain the stored definition and version verbatim.
            curves.Add(new(definition, curve));
        }
        return ContractualCashFlowEngine.Calculate(contract, request, calendars,
            curves.DistinctBy(c => c.Version.CurveVersionId).ToArray(), version, termsHash);
    }

    public static ContractualCashFlowContract FromSecurity(SecurityDetailDto security)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        if (descriptor.CashFlowBehavior is not (SecurityCashFlowBehavior.ContractualSchedule or SecurityCashFlowBehavior.SwapLegs)
            && security.AssetClass is not ("StructuredCredit" or "DirectLoan"))
            throw new ContractInputUnavailable("This asset class requires a product-specific authoritative calculation contract.");
        SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(security.AssetClass, security.AssetSpecificTerms);
        try
        {
            if (SecurityContractualCashFlowTerms.Read(security) is { } companion)
                return companion;
        }
        catch (JsonException) { throw new ContractInputUnavailable("Declared cashFlowContract is malformed or carries an unsupported version/member."); }
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var issue = terms.IssueDate ?? throw new ContractInputUnavailable("Contractual issue/origination date is missing; an effective-from date is not an anchor.");
        var maturity = terms.MaturityDate ?? throw new ContractInputUnavailable("Contractual maturity is missing.");
        var principal = terms.PrincipalFace;
        var conventions = SecurityVersionedTerms.ReadDeclaredScheduleConventions(security.AssetClass, security.AssetSpecificTerms).Block;
        var streams = new List<ContractualCashFlowStream>();
        var rawLegs = ReadRetainedLegRows(security);
        if (terms.Legs is { Count: > 0 } legs)
        {
            if (rawLegs.Length > 0 && rawLegs.Length != legs.Count)
                throw new ContractInputUnavailable("Retained leg rows do not match the resolved contractual legs.");
            var legConventionsField = SecurityAssetTermsSchema.ElementFields(security.AssetClass, "legs")
                .FirstOrDefault(static field => field.Key == SecurityVersionedTerms.ScheduleConventionsKey);
            var decodesLegConventions = legConventionsField is not null
                && SecurityVersionedTerms.Decoding(security.AssetClass, security.AssetSpecificTerms, writeMode: false).Decodes(legConventionsField);
            for (var i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                if (i < rawLegs.Length && SecurityTermReader.ReadString([rawLegs[i]], "currency") is { } legCurrency
                    && !string.Equals(legCurrency, security.Currency, StringComparison.OrdinalIgnoreCase))
                    throw new ContractInputUnavailable("Cross-currency legs require separate currency-specific projection inputs; no FX conversion is inferred.");
                if (legs.Count > 1 && leg.Direction is null)
                    throw new ContractInputUnavailable("Every leg needs a retained pay/receive direction.");
                var bondCoupon = descriptor.CarriesBondCouponStructure && rawLegs.Length == 0;
                var block = bondCoupon ? conventions : decodesLegConventions && i < rawLegs.Length
                    ? SecurityVersionedTerms.ReadScheduleConventions(rawLegs[i], SecurityVersionedTerms.ScheduleConventionsKey) : ScheduleConventionTerms.Empty;
                streams.Add(new(leg.LegId, leg.Notional ?? principal ?? throw new ContractInputUnavailable("Every leg needs a retained notional."), Months(leg.PaymentFrequency ?? terms.PaymentFrequency),
                    DayCount(leg.DayCountConvention ?? terms.DayCountConvention), Rate(leg.FixedRate, block), leg.Direction ?? CashFlowLegDirection.Receive,
                    leg.ExchangesPrincipal, block, PrincipalSchedule: bondCoupon ? terms.PrincipalSchedule : null,
                    NotionalSchedule: leg.NotionalSchedule,
                    IndexName: leg.RateKind == CashFlowLegRateKind.Floating ? leg.IndexName : null,
                    SpreadBps: leg.SpreadBps ?? 0, Resets: leg.ResetSchedule?.Select(r => r with { FixingRate = Rate(r.FixingRate, block) }).ToArray(),
                    Floor: bondCoupon ? Rate(terms.FloorRate, conventions) : null,
                    Cap: bondCoupon ? Rate(terms.CapRate, conventions) : null));
            }
        }
        else
        {
            var rate = Rate(terms.CouponRate, conventions);
            if (terms.InflationIndex is not null)
                throw new ContractInputUnavailable("Inflation-linked cash flows require a supported provider schedule.");
            if (terms.CurrentFactor is not null || terms.HasFactorSchedule)
                throw new ContractInputUnavailable("Observed factor history requires a reconciled contractual balance history; a current factor cannot reconstruct prior accrual.");
            streams.Add(new("main", principal ?? throw new ContractInputUnavailable("Retained principal is required; analytical per-100 amounts are not substituted."), Months(terms.PaymentFrequency), DayCount(terms.DayCountConvention), rate,
                Conventions: conventions, PrincipalSchedule: terms.PrincipalSchedule,
                RateSteps: terms.StepCouponSchedule?.Select(r => r with { Rate = Rate(r.Rate, conventions)!.Value }).ToArray(),
                Floor: Rate(terms.FloorRate, conventions), Cap: Rate(terms.CapRate, conventions),
                Amortization: terms.HasPrincipalSchedule ? CashFlowAmortization.ExplicitPrincipal : CashFlowAmortization.Bullet));
        }
        var exercises = new List<ContractualExerciseEntry>();
        var exerciseStreamId = streams.Count == 1 ? streams[0].StreamId : "main";
        ReadExercises(SecurityVersionedTerms.CallScheduleKey, CashFlowExerciseSide.Call);
        ReadExercises(SecurityVersionedTerms.PutScheduleKey, CashFlowExerciseSide.Put);
        return new(issue, maturity, security.Currency, streams, exercises);

        void ReadExercises(string key, CashFlowExerciseSide side)
        {
            var field = SecurityAssetTermsSchema.Field(security.AssetClass, key);
            if (field is null || !SecurityVersionedTerms.Decoding(security.AssetClass, security.AssetSpecificTerms, writeMode: false).Decodes(field))
                return;
            if (!security.AssetSpecificTerms.TryGetProperty(key, out var rows) || rows.ValueKind == JsonValueKind.Null)
                return;
            if (rows.ValueKind != JsonValueKind.Array)
                throw new ContractInputUnavailable($"Malformed {key}.");
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    throw new ContractInputUnavailable($"Malformed {key} entry.");
                var prefix = side == CashFlowExerciseSide.Call ? "call" : "put";
                var start = Date(row, prefix + "Date");
                if (start is null)
                    continue;
                if (!Enum.TryParse<CashFlowExerciseStyle>(Text(row, "exerciseStyle"), false, out var style))
                    throw new ContractInputUnavailable("Exercise style must be stated.");
                if (Text(row, "priceType") == "MakeWhole")
                    throw new ContractInputUnavailable("Make-whole entry requires a versioned companion benchmark/formula contract.");
                if (Text(row, "priceType") is { } priceType && priceType != "Fixed")
                    throw new ContractInputUnavailable("Unrecognized exercise price type.");
                exercises.Add(new(side, start.Value, Date(row, prefix + "EndDate"), style, Number(row, prefix + "Price"), exerciseStreamId,
                    Text(row, "exerciseFrequency") is { } cadence ? Months(cadence) : null, Text(row, "partialRedemption"),
                    Int(row, "minimumNoticeDays"), Int(row, "maximumNoticeDays"), Text(row, "noticeDayType"), Text(row, "priceBasis") ?? "PercentOfPar"));
            }
        }
    }

    private static JsonElement[] ReadRetainedLegRows(SecurityDetailDto security)
    {
        // Use the resolver's authority order and the schema-owned leg aliases. Pairing its filtered
        // legs with a different raw array can drop currencies or apply another leg's rate units.
        var container = SecurityAssetTermsSchema.Field("Swap", "legs")!;
        foreach (var source in StructuredCashFlowTermsResolver.EnumerateTermSources(security))
        {
            foreach (var key in new[] { container.Key }.Concat(container.Aliases))
            {
                if (!SecurityTermReader.TryGetProperty(source, key, out var array)
                    || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
                    continue;
                var rows = array.EnumerateArray().ToArray();
                if (rows.Any(static row => row.ValueKind != JsonValueKind.Object))
                    throw new ContractInputUnavailable("Retained contractual leg arrays must contain only object rows; malformed legs cannot be skipped.");
                return rows;
            }
        }
        return [];
    }

    public static ContractualCashFlowContract FromLoan(DirectLendingTermsDto terms, LoanServicingStateDto servicing)
    {
        if (terms.AmortizationType == AmortizationType.CustomSchedule || terms.InterestOnlyMonths > 0)
            throw new ContractInputUnavailable("Custom amortization and interest-only transitions require an explicit contractual schedule.");
        var principal = servicing.DrawdownLots.Sum(l => l.OriginalPrincipal);
        if (servicing.DrawdownLots.Any(l => l.DrawdownDate != terms.OriginationDate) || servicing.DrawdownLots.Sum(l => l.RemainingPrincipal) != principal)
            throw new ContractInputUnavailable("Loan drawdowns/payments require a dated balance history, not the current balance alone.");
        var reset = servicing.CurrentRateReset;
        var stream = new ContractualCashFlowStream("main", principal, terms.PaymentFrequency switch
        {
            PaymentFrequency.Monthly => 1,
            PaymentFrequency.Quarterly => 3,
            PaymentFrequency.SemiAnnual => 6,
            PaymentFrequency.Annual => 12,
            PaymentFrequency.Bullet => 0,
            _ => throw new ContractInputUnavailable("Unknown loan cadence.")
        }, terms.DayCountBasis switch
        {
            DayCountBasis.Act360 => DayCountConvention.Actual360,
            DayCountBasis.Act365F => DayCountConvention.Actual365,
            DayCountBasis.Thirty360 => DayCountConvention.Thirty360,
            DayCountBasis.ActualActualISDA => DayCountConvention.ActualActualIsda,
            _ => throw new ContractInputUnavailable("Unknown loan day count.")
        }, terms.FixedAnnualRate, IndexName: terms.RateTypeKind == RateTypeKind.Floating ? terms.InterestIndexName : null,
            SpreadBps: terms.SpreadBps ?? 0, Resets: reset is null ? null : [new(reset.EffectiveDate, reset.ObservedRate)],
            Floor: terms.EffectiveRateFloor ?? terms.FloorRate, Cap: terms.EffectiveRateCap ?? terms.CapRate,
            PikFraction: servicing.IsPikToggled ? 1 : 0,
            Amortization: terms.AmortizationType == AmortizationType.StraightLine ? CashFlowAmortization.EqualPrincipal : CashFlowAmortization.Bullet,
            PrepaymentAllowed: terms.PrepaymentAllowed, PrepaymentPenaltyRate: terms.PrepaymentPenaltyRate ?? 0);
        return new(terms.OriginationDate, terms.MaturityDate, terms.BaseCurrency.ToString(), [stream]);
    }

    private static int Months(string? frequency) => frequency switch
    {
        "Monthly" => 1,
        "Quarterly" => 3,
        "SemiAnnual" or "Semiannually" => 6,
        "Annual" or "Annually" => 12,
        "AtMaturity" or "Bullet" => 0,
        _ => throw new ContractInputUnavailable("A recognized contractual payment cadence is required.")
    };
    private static decimal? Rate(decimal? rate, ScheduleConventionTerms block)
        => rate is { } value && (block.CouponRateUnit == "Percent" || block.CouponRateUnit is null && value > 1) ? value / 100m : rate;
    private static DayCountConvention DayCount(string? text) => DayCountConventions.TryParse(text, out var dayCount) && dayCount != DayCountConvention.Unknown
        ? dayCount : throw new ContractInputUnavailable("A recognized contractual day count is required.");
    private static string? Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static decimal? Number(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
    private static int? Int(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static DateOnly? Date(JsonElement row, string key) => DateOnly.TryParseExact(Text(row, key), "yyyy-MM-dd", out var date) ? date : null;
    private sealed class ContractInputUnavailable(string message) : Exception(message);

    private static ContractualCashFlowResult Blocked(ContractualCashFlowRequest request, string code, string reason)
        => new([], [], [], [new(code, reason)], new(ContractualCashFlowVersions.Engine, ContractualCashFlowVersions.Mapping,
            ContractualCashFlowVersions.Prepayment, Meridian.Contracts.SecurityMaster.Schedules.ScheduleGenerator.KernelVersion,
            ContractualCashFlowEngine.Hash(request), ContractualCashFlowEngine.Hash(new { code, reason }), "", "unavailable", request.ContractOverride is not null, [], [], []));
}
