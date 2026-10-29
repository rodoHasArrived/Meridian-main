using System.Globalization;
using Meridian.Contracts.Integrity;
using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.CashFlows;

/// <summary>Deterministic, calculation-only engine. No wall clock, I/O, or ledger effects.</summary>
public static partial class ContractualCashFlowEngine
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Hash<T>(T value) => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(value, Json));

    public static ContractualCashFlowResult Calculate(
        ContractualCashFlowContract contract, ContractualCashFlowRequest request,
        IBusinessDayCalendarSource? calendars = null,
        IReadOnlyList<CashFlowCurveSnapshot>? curves = null,
        string sourceVersion = "explicit-contract-v1", string? sourceTermsHash = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(request);
        Validate(contract, request);
        var snapshots = new Dictionary<string, CashFlowCalendarSnapshot>(StringComparer.Ordinal);
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var events = new List<ContractualCashFlowEvent>();
        var eligibility = new List<CashFlowExerciseEligibility>();
        var calculations = new List<CashFlowExerciseCalculation>();
        var blockers = new List<CashFlowCalculationBlocker>();
        var market = curves ?? [];
        foreach (var stream in contract.Streams.OrderBy(s => s.StreamId, StringComparer.Ordinal))
        {
            try
            {
                var calendar = ResolveCalendar(stream, calendars);
                var tracked = calendar is null ? null : new ObservedCalendar(calendar);
                var schedule = BuildSchedule(contract, stream, tracked);
                var entries = ResolveExercises(contract, stream, schedule, tracked);
                eligibility.AddRange(entries.Select(e => e.Eligibility));
                Walk(contract, stream, request, schedule, tracked, market, entries, events, calculations);
                if (tracked is not null && tracked.Dates.Count > 0)
                {
                    var first = tracked.Dates.Min();
                    var last = tracked.Dates.Max();
                    var holidays = Enumerable.Range(first.DayNumber, last.DayNumber - first.DayNumber + 1)
                        .Select(DateOnly.FromDayNumber).Where(d => !calendar!.IsBusinessDay(d)).ToArray();
                    var snapshot = new CashFlowCalendarSnapshot(calendar!.CalendarId, calendar.ContentHash, first, last, holidays, calendar.Coverage.From, calendar.Coverage.To);
                    if (snapshots.TryGetValue(calendar.CalendarId, out var previous))
                    {
                        first = first < previous.CoverageStart ? first : previous.CoverageStart;
                        last = last > previous.CoverageEnd ? last : previous.CoverageEnd;
                        snapshot = snapshot with
                        {
                            CoverageStart = first,
                            CoverageEnd = last,
                            NonBusinessDays = Enumerable.Range(first.DayNumber, last.DayNumber - first.DayNumber + 1).Select(DateOnly.FromDayNumber).Where(d => !calendar.IsBusinessDay(d)).ToArray()
                        };
                    }
                    snapshots[calendar.CalendarId] = snapshot;
                    if (!calendar.Coverage.Covers(first) || !calendar.Coverage.Covers(last))
                        warnings.Add($"CalendarCoverageExceeded:{calendar.CalendarId}:{first:yyyy-MM-dd}:{last:yyyy-MM-dd}");
                }
            }
            catch (CalculationRefused ex) { blockers.Add(new(ex.Code, ex.Message, stream.StreamId)); }
            catch (ArgumentOutOfRangeException) { blockers.Add(new("DateRangeExceeded", "Date arithmetic exceeded the supported range.", stream.StreamId)); }
            catch (OverflowException) { blockers.Add(new("NumericRangeExceeded", "Calculation exceeded the supported numeric range.", stream.StreamId)); }
            catch (InvalidOperationException) { blockers.Add(new("CalculationInputsUnresolvable", "Calendar or market-data arithmetic could not resolve the declared inputs.", stream.StreamId)); }
        }
        // An incomplete collection of legs must never look like a complete security projection.
        if (blockers.Count > 0)
        { events.Clear(); calculations.Clear(); }
        var ordered = events.OrderBy(e => e.Date).ThenBy(e => e.StreamId, StringComparer.Ordinal).ThenBy(e => e.EventId, StringComparer.Ordinal).ToArray();
        var calendarEvidence = snapshots.Values.OrderBy(c => c.CalendarId, StringComparer.Ordinal).ToArray();
        var curveEvidence = market.OrderBy(c => c.Definition.CurveKey, StringComparer.Ordinal).ToArray();
        sourceTermsHash ??= Hash(contract);
        var inputHash = Hash(new { contract, request, calendarEvidence, curveEvidence, sourceVersion, sourceTermsHash });
        var outputHash = Hash(new { Events = ordered, Eligibility = eligibility, Calculations = calculations, Blockers = blockers });
        return new(ordered, eligibility, calculations, blockers,
            new(ContractualCashFlowVersions.Engine, ContractualCashFlowVersions.Mapping, ContractualCashFlowVersions.Prepayment,
                ScheduleGenerator.KernelVersion, inputHash, outputHash, sourceTermsHash ?? Hash(contract), sourceVersion,
                request.ContractOverride is not null, calendarEvidence, curveEvidence, warnings.Order(StringComparer.Ordinal).ToArray()))
        { ResolvedContract = contract };
    }

    public static ContractualCashFlowResult Replay(ContractualCashFlowRun run)
    {
        var source = new ReplayCalendarSource(run.Result.Evidence.Calendars);
        return Calculate(run.Contract, run.Request, source, run.Result.Evidence.Curves,
            run.Result.Evidence.SourceVersion, run.Result.Evidence.SourceTermsHash);
    }

    public static bool Verify(ContractualCashFlowRun run)
    {
        var actualOutput = Hash(new
        {
            Events = run.Result.Events,
            Eligibility = run.Result.ExerciseEligibility,
            Calculations = run.Result.ExerciseCalculations,
            Blockers = run.Result.Blockers
        });
        var replay = Replay(run);
        return actualOutput == run.Result.Evidence.OutputHash && replay.Evidence.OutputHash == actualOutput
            && Hash(replay.Evidence) == Hash(run.Result.Evidence)
            && (run.Result.ResolvedContract is null || Hash(run.Result.ResolvedContract) == Hash(run.Contract));
    }

    public static void Validate(ContractualCashFlowContract contract, ContractualCashFlowRequest request)
    {
        ValidateRequest(request);
        static void Require(bool ok, string reason)
        { if (!ok) throw new ArgumentException(reason); }
        Require(contract.Version == 1 && request.EngineVersion == ContractualCashFlowVersions.Engine, "Unsupported contract or engine version.");
        Require(contract.IssueDate < contract.MaturityDate && contract.MaturityDate.DayNumber - contract.IssueDate.DayNumber <= 36600, "Contract must span a positive term of at most 100 years.");
        Require(contract.Currency is { Length: 3 } && contract.Currency.All(char.IsAsciiLetter), "A three-letter contract currency is required.");
        Require(contract.Streams is { Count: > 0 and <= 100 } && contract.Streams.All(s => s is not null), "Between one and 100 streams are required.");
        Require(contract.Streams.Select(s => s.StreamId).Distinct(StringComparer.Ordinal).Count() == contract.Streams.Count, "Stream identities must be unique.");
        Require(request.Horizon is null || request.Horizon >= request.ValuationDate, "Horizon precedes valuation.");
        Require(Enum.IsDefined(request.RateScenario), "Unknown rate scenario.");
        Require((request.Exercises?.Count ?? 0) <= 1200 && (request.Prepayments?.Count ?? 0) <= 1200, "Scenario exceeds 1200 entries.");
        foreach (var s in contract.Streams)
        {
            Require(!string.IsNullOrWhiteSpace(s.StreamId) && s.Principal >= 0 && s.PaymentMonths is >= 0 and <= 1200, "Stream identity, principal or cadence is invalid.");
            Require(Enum.IsDefined(s.DayCount) && s.DayCount != DayCountConvention.Unknown && Enum.IsDefined(s.Amortization) && Enum.IsDefined(s.Direction), "Unknown day count, amortization or direction.");
            Require(s.PikFraction is >= 0 and <= 1 && s.PrepaymentPenaltyRate >= 0 && (s.Floor is null || s.Cap is null || s.Floor <= s.Cap), "Invalid PIK, penalty, floor or cap.");
            Require((s.OpeningBalance is null) == (s.OpeningBalanceDate is null) && s.OpeningBalance is not < 0, "Opening balance requires its evidence date.");
            Require(s.OpeningBalanceDate is null || s.OpeningBalanceDate <= request.ValuationDate, "Opening balance evidence is after valuation.");
            Require(s.OpeningBalanceDate is null || s.OpeningBalanceDate >= contract.IssueDate, "Opening balance evidence precedes origination.");
            Require(s.PrincipalSchedule?.All(p => p is not null && p.Amount >= 0 && p.PaymentDate >= contract.IssueDate && p.PaymentDate <= contract.MaturityDate) != false, "Principal payments must be nonnegative and in term.");
            Require(s.RateSteps?.All(p => p is not null && p.EffectiveDate >= contract.IssueDate && p.EffectiveDate <= contract.MaturityDate) != false, "Rate steps must be in term.");
            Require(s.NotionalSchedule?.All(p => p is not null && p.Notional >= 0 && p.EffectiveDate >= contract.IssueDate && p.EffectiveDate <= contract.MaturityDate) != false, "Notional steps must be nonnegative and in term.");
            Require(s.Resets?.All(p => p is not null && p.ResetDate >= contract.IssueDate && p.ResetDate <= contract.MaturityDate) != false, "Reset dates must be in term.");
            Require(s.CouponAmounts?.All(p => p is not null && p.Amount >= 0) != false, "Coupon amounts must be nonnegative.");
            Require(s.CouponPeriods is null || s.CouponPeriods.Count is > 0 and <= 1200, "Explicit periods must contain between one and 1200 rows.");
            Require(s.NotionalSchedule is null || s.PrincipalSchedule is null, "Principal cash schedules and absolute notional schedules cannot both control the same balance.");
            Require(s.Amortization is not (CashFlowAmortization.EqualPrincipal or CashFlowAmortization.LevelPayment) || s.PrincipalSchedule is not { Count: > 0 } && s.NotionalSchedule is not { Count: > 0 }, "Formula amortization cannot also declare a principal/notional schedule.");
            Require(s.Conventions?.PaymentLagBusinessDays is null or >= 0 and <= 3660, "Payment lag must be between zero and 3660 business days.");
            Require(s.Conventions?.CouponCashBasis is null or "DayCount" or "Level", "Unknown coupon cash basis.");
            Unique(s.RateSteps?.Select(p => p.EffectiveDate), "rate step");
            Unique(s.NotionalSchedule?.Select(p => p.EffectiveDate), "notional step");
            Unique(s.Resets?.Select(p => p.ResetDate), "reset");
            Unique(s.CouponAmounts?.Select(p => p.PaymentDate), "coupon amount");
        }
        var ids = contract.Streams.Select(s => s.StreamId).ToHashSet(StringComparer.Ordinal);
        foreach (var e in contract.Exercises ?? [])
        {
            Require(e is not null && ids.Contains(e.StreamId) && Enum.IsDefined(e.Side) && Enum.IsDefined(e.Style), "Unknown exercise stream, side or style.");
            Require(e.StartDate >= contract.IssueDate && e.StartDate <= contract.MaturityDate && (e.EndDate is null || e.EndDate >= e.StartDate && e.EndDate <= contract.MaturityDate), "Exercise window is outside the contract term.");
            Require(e.ExerciseMonths is null or > 0 && e.CleanPrice is null or >= 0, "Invalid exercise cadence or clean price.");
            Require(e.PartialRedemption is null or "WholeOnly" or "ProRata" or "ByLot", "Unknown partial-redemption method.");
            Require(e.PriceBasis is "PercentOfPar" or "PercentOfAccretedValue", "Unknown exercise price basis.");
            Require(e.AccretedValue is null or > 0, "Accreted value per unit of principal must be positive when stated.");
            Require(e.MinimumNoticeDays is null or >= 0 && e.MaximumNoticeDays is null or >= 0 && (e.MinimumNoticeDays is null || e.MaximumNoticeDays is null || e.MaximumNoticeDays >= e.MinimumNoticeDays), "Invalid notice bounds.");
            Require(e.NoticeDayType is null or "Calendar" or "Business", "Unknown notice day type.");
            Require(e.MinimumNoticeDays is null && e.MaximumNoticeDays is null || e.NoticeDayType is not null, "Notice counts require a day type.");
        }
        Require((contract.Exercises ?? []).Select(e => (e.Side, e.StreamId, e.StartDate)).Distinct().Count() == (contract.Exercises?.Count ?? 0), "Duplicate exercise entry identity.");
        foreach (var e in request.Exercises ?? [])
        {
            Require(e is not null && ids.Contains(e.StreamId) && e.Amount > 0 && e.ExerciseDate >= request.ValuationDate, "Selected exercise must have a stream, positive amount and a date on/after valuation.");
            Require(request.Horizon is null || e.ExerciseDate <= request.Horizon, "Selected exercise lies beyond the requested horizon.");
            Require(e.HypotheticalNotice || !string.IsNullOrWhiteSpace(e.NoticeEvidenceId), "A retained notice requires its evidence reference.");
            Require(!e.HypotheticalNotice || e.NoticeDate >= request.ValuationDate, "A hypothetical notice cannot be invented in the past.");
        }
        Require((request.Exercises ?? []).Select(e => (e.StreamId, e.ExerciseDate)).Distinct().Count() == (request.Exercises?.Count ?? 0), "Competing exercise selections on one date are ambiguous.");
        foreach (var p in request.Prepayments ?? [])
        {
            Require(p is not null && ids.Contains(p.StreamId) && Enum.IsDefined(p.Method) && Enum.IsDefined(p.Policy), "Unknown prepayment stream, method or policy.");
            Require(p.StartDate >= request.ValuationDate && p.StartDate >= contract.IssueDate && p.StartDate < p.EndDate && p.StartDate <= contract.MaturityDate && p.Value >= 0, "Invalid prepayment interval or value.");
            Require(p.Method is not (CashFlowPrepaymentMethod.Cpr or CashFlowPrepaymentMethod.Smm) || p.Value <= 1, "CPR/SMM must be fractions between zero and one.");
            Require(p.Method != CashFlowPrepaymentMethod.Psa || p.SeasoningOrigin is not null && p.SeasoningOrigin <= p.StartDate, "PSA requires a seasoning origin on/before the interval.");
        }
        foreach (var g in (request.Prepayments ?? []).GroupBy(p => p.StreamId))
        {
            var ordered = g.OrderBy(p => p.StartDate).ToArray();
            for (var i = 1; i < ordered.Length; i++)
                Require(ordered[i].StartDate >= ordered[i - 1].EndDate, "Prepayment assumption intervals must not overlap.");
        }
        Require((request.CurvePins ?? []).All(p => p is not null && p.CurveId != Guid.Empty && p.CurveVersionId != Guid.Empty && !string.IsNullOrWhiteSpace(p.BenchmarkKey)), "Curve pins require a key and immutable identities.");
        Require((request.CurvePins ?? []).Select(p => p.BenchmarkKey).Distinct(StringComparer.Ordinal).Count() == (request.CurvePins?.Count ?? 0), "Duplicate curve benchmark key.");
    }

    public static void ValidateRequest(ContractualCashFlowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ValuationDate == default || request.KnowledgeCutoff == default || request.EngineVersion != ContractualCashFlowVersions.Engine)
            throw new ArgumentException("A valuation date, knowledge cutoff and supported engine version are required.");
    }

    private static void Unique(IEnumerable<DateOnly>? dates, string name)
    {
        if (dates is null)
            return;
        var values = dates.ToArray();
        if (values.Distinct().Count() != values.Length)
            throw new ArgumentException($"Duplicate {name} date.");
    }

    private static IBusinessDayCalendar? ResolveCalendar(ContractualCashFlowStream stream, IBusinessDayCalendarSource? source)
    {
        var ids = stream.Conventions?.PaymentCalendars ?? [];
        if (ids.Count == 0)
            return null;
        if (source is null || !source.TryResolve(ids, out var calendar))
            throw Refuse("CalendarUnavailable", "Declared payment calendars cannot be resolved.");
        return calendar;
    }

    private static ScheduleGenerationResult BuildSchedule(ContractualCashFlowContract contract, ContractualCashFlowStream stream, IBusinessDayCalendar? calendar)
    {
        var c = stream.Conventions ?? ScheduleConventionTerms.Empty;
        T Parse<T>(string? raw, T fallback) where T : struct, Enum
            => raw is null ? fallback : Enum.TryParse<T>(raw, false, out var value) && Enum.IsDefined(value) ? value : throw new ArgumentException($"Unknown {typeof(T).Name}: {raw}.");
        var bdc = Parse(c.BusinessDayConvention, BusinessDayConvention.Unadjusted);
        if ((bdc != BusinessDayConvention.Unadjusted || c.PaymentLagBusinessDays is > 0) && calendar is null)
            throw Refuse("CalendarRequired", "Adjustment and payment lag require declared calendars.");
        var adjustment = new BusinessDayAdjustment(bdc, calendar);
        var roll = c.RollConvention switch
        {
            null => c.EndOfMonthRule == true ? ScheduleRoll.EndOfMonth : ScheduleRoll.AnchorDay,
            "AnchorDay" => ScheduleRoll.AnchorDay,
            "EndOfMonth" => ScheduleRoll.EndOfMonth,
            _ => throw new ArgumentException("Unsupported roll convention; use AnchorDay or EndOfMonth.")
        };
        if (c.EndOfMonthRule is { } eom && (eom != (roll == ScheduleRoll.EndOfMonth)))
            throw new ArgumentException("Roll and endOfMonthRule conflict.");
        var spec = new ScheduleSpec(contract.IssueDate, contract.MaturityDate,
            stream.PaymentMonths == 0 ? ScheduleTenor.Term : ScheduleTenor.Months(stream.PaymentMonths),
            Parse(c.Direction, ScheduleDirection.Forward), Roll: roll,
            StubPolicy: Parse(c.StubPolicy, ScheduleStubPolicy.Short), FirstRegularDate: c.FirstRegularDate,
            LastRegularDate: c.LastRegularDate, AccrualAdjustment: c.AccrualAdjusted == true ? adjustment : null,
            PaymentAdjustment: adjustment, PaymentLagBusinessDays: c.PaymentLagBusinessDays ?? 0);
        if (stream.CouponPeriods is not { } explicitPeriods)
        {
            var generated = ScheduleGenerator.Generate(spec);
            if (!generated.Succeeded)
                throw Refuse("ScheduleRefused", generated.Failure!.Message);
            return generated;
        }
        var cursor = contract.IssueDate;
        for (var i = 0; i < explicitPeriods.Count; i++)
        {
            var p = explicitPeriods[i];
            if (p is null || p.Sequence != i + 1 || p.UnadjustedStart != cursor || p.UnadjustedEnd <= cursor || p.AccrualEnd <= p.AccrualStart || p.PaymentDate < p.AccrualEnd || p.IsFinal != (i == explicitPeriods.Count - 1)
                || i > 0 && p.AccrualStart != explicitPeriods[i - 1].AccrualEnd)
                throw new ArgumentException("Explicit coupon periods must be sequential, contiguous and carry valid accrual/payment dates and final markers.");
            cursor = p.UnadjustedEnd;
            if (p.ReferencePeriods is null || p.ReferencePeriods.Count > 1200)
                throw new ArgumentException("Explicit periods require a bounded reference-period collection.");
            var expectedStart = c.AccrualAdjusted == true ? adjustment.Apply(p.UnadjustedStart) : p.UnadjustedStart;
            var expectedEnd = c.AccrualAdjusted == true ? adjustment.Apply(p.UnadjustedEnd) : p.UnadjustedEnd;
            var payment = adjustment.Apply(expectedEnd);
            if (spec.PaymentLagBusinessDays > 0)
                payment = calendar!.AddBusinessDays(payment, spec.PaymentLagBusinessDays);
            if (p.AccrualStart != expectedStart || p.AccrualEnd != expectedEnd || p.PaymentDate != payment)
                throw new ArgumentException("Explicit accrual/payment dates must agree with the declared adjustment and lag.");
        }
        if (cursor != contract.MaturityDate)
            throw new ArgumentException("Explicit coupon periods must cover origination through maturity.");
        return new(spec, explicitPeriods, null, [], []);
    }

    private sealed class ObservedCalendar(IBusinessDayCalendar inner) : IBusinessDayCalendar
    {
        public HashSet<DateOnly> Dates { get; } = [];
        public string CalendarId => inner.CalendarId;
        public string ContentHash => inner.ContentHash;
        public CalendarCoverage Coverage => inner.Coverage;
        public bool IsBusinessDay(DateOnly date) { Dates.Add(date); return inner.IsBusinessDay(date); }
    }
    private sealed class ReplayCalendar(CashFlowCalendarSnapshot snapshot) : IBusinessDayCalendar
    {
        private readonly HashSet<DateOnly> _closed = snapshot.NonBusinessDays.ToHashSet();
        public string CalendarId => snapshot.CalendarId;
        public string ContentHash => snapshot.ContentHash;
        public CalendarCoverage Coverage => new(snapshot.SourceCoverageStart, snapshot.SourceCoverageEnd);
        public bool IsBusinessDay(DateOnly date)
        {
            if (date < snapshot.CoverageStart || date > snapshot.CoverageEnd)
                throw new ArgumentException("Replay calendar does not cover the query.");
            return !_closed.Contains(date);
        }
    }
    private sealed class ReplayCalendarSource(IReadOnlyList<CashFlowCalendarSnapshot> snapshots) : IBusinessDayCalendarSource
    {
        public bool TryGet(string calendarId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBusinessDayCalendar? calendar)
        {
            var snapshot = snapshots.SingleOrDefault(s => string.Equals(s.CalendarId, calendarId, StringComparison.OrdinalIgnoreCase));
            calendar = snapshot is null ? null : new ReplayCalendar(snapshot);
            return calendar is not null;
        }
        public bool TryResolve(IReadOnlyList<string> calendarIds, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBusinessDayCalendar? calendar)
            => TryGet(string.Join("+", calendarIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)), out calendar);
    }
    private sealed class CalculationRefused(string code, string message) : Exception(message) { public string Code => code; }
    private static CalculationRefused Refuse(string code, string message) => new(code, message);
    private static decimal Cash(decimal value) => decimal.Round(value, 8, MidpointRounding.AwayFromZero);
}
