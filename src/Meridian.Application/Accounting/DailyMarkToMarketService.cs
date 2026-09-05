using System.Threading;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Core.Logging;
using Meridian.Ledger;
using Serilog;

namespace Meridian.Application.Accounting;

/// <summary>
/// One portfolio position eligible for a daily fair-value mark.
/// </summary>
public sealed record MarkToMarketPosition(
    string Symbol,
    decimal Quantity,
    decimal CostPrice,
    string? FinancialAccountId = null,
    string? InstrumentType = null,
    Guid? SecurityId = null);

/// <summary>
/// A resolved mark price with its provenance for valuation evidence. <see cref="Level"/> records
/// the ASC 820 fair-value classification of the price and <see cref="PriceAsOf"/> the date the
/// price was observed, so downstream valuation can assess defensibility and freshness.
/// </summary>
/// <param name="Provenance">
/// Where the price actually came from. A source that fabricates prices — a synthetic provider, a
/// seeded demo feed — must declare a non-real value here so the mark, the valuation draft, and
/// every report built on it carry the origin outward instead of presenting a model output as an
/// observed market price.
/// </param>
public sealed record MarkPriceQuote(
    decimal Price,
    string Source,
    string EvidenceReference,
    FairValueLevel Level = FairValueLevel.Unclassified,
    DateOnly? PriceAsOf = null,
    DailyPortfolioPriceConfidence Confidence = DailyPortfolioPriceConfidence.High,
    DataProvenance Provenance = DataProvenance.Real)
{
    /// <summary>
    /// Compatibility constructor for callers introduced with the confidence-aware mark contract.
    /// Fair-value classification remains unclassified until the valuation policy supplies a default.
    /// </summary>
    public MarkPriceQuote(
        decimal Price,
        string Source,
        string EvidenceReference,
        DateOnly? ObservedOn,
        DailyPortfolioPriceConfidence Confidence = DailyPortfolioPriceConfidence.High)
        : this(
            Price,
            Source,
            EvidenceReference,
            FairValueLevel.Unclassified,
            ObservedOn,
            Confidence)
    {
    }

    public DateOnly? ObservedOn => PriceAsOf;
}

/// <summary>
/// Trust policy applied before provider marks can enter a governed valuation draft. The policy is
/// explicit so legacy callers can continue to use the fund's <see cref="StalePricePolicy"/> while
/// production scheduling requires observation dates, freshness, and minimum confidence.
/// </summary>
public sealed record MarkPriceQualityPolicy
{
    public static MarkPriceQualityPolicy Standard { get; } = new(
        TimeSpan.FromDays(3),
        DailyPortfolioPriceConfidence.Medium,
        RequireCompleteCoverage: true,
        RequireObservedDate: true);

    public MarkPriceQualityPolicy(
        TimeSpan maximumAge,
        DailyPortfolioPriceConfidence minimumConfidence,
        bool RequireCompleteCoverage = true,
        bool RequireObservedDate = true)
    {
        if (maximumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumAge), "Maximum mark age cannot be negative.");

        MaximumAge = maximumAge;
        MinimumConfidence = minimumConfidence;
        this.RequireCompleteCoverage = RequireCompleteCoverage;
        this.RequireObservedDate = RequireObservedDate;
    }

    public TimeSpan MaximumAge { get; }

    public DailyPortfolioPriceConfidence MinimumConfidence { get; }

    public bool RequireCompleteCoverage { get; }

    public bool RequireObservedDate { get; }
}

/// <summary>Provider mark rejected by a valuation trust or stale-price policy.</summary>
public sealed record MarkPriceRejection(
    string Symbol,
    string Reason,
    DateOnly? ObservedOn = null,
    DailyPortfolioPriceConfidence? Confidence = null,
    string? EvidenceReference = null);

/// <summary>
/// Supplies mark prices for daily portfolio valuation. Implementations return null when
/// no reliable price exists for the symbol at the requested date; the caller surfaces the
/// gap instead of silently marking at cost.
/// </summary>
public interface IMarkPriceSource
{
    Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Stable lookup key for one security account whose carrying value must be hydrated before a
/// daily mark can be projected. Symbols are normalized so producer and consumer keys compare
/// deterministically across process boundaries.
/// </summary>
public sealed record MarkToMarketCarryingValueKey
{
    public MarkToMarketCarryingValueKey(Guid? securityId, string symbol, string? financialAccountId)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security identifier cannot be empty when supplied.", nameof(securityId));
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required for carrying-value lookup.", nameof(symbol));

        SecurityId = securityId;
        Symbol = symbol.Trim().ToUpperInvariant();
        FinancialAccountId = string.IsNullOrWhiteSpace(financialAccountId) ? null : financialAccountId.Trim();
    }

    public Guid? SecurityId { get; }

    public string Symbol { get; }

    public string? FinancialAccountId { get; }

    public static MarkToMarketCarryingValueKey FromPosition(MarkToMarketPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return new MarkToMarketCarryingValueKey(
            position.SecurityId,
            position.Symbol,
            position.FinancialAccountId);
    }
}

/// <summary>
/// Durable carrying-value lookup result. <see cref="Amount"/> being null explicitly means the
/// securities account is absent; zero means it exists with a zero balance.
/// </summary>
public sealed record MarkToMarketCarryingValue
{
    public MarkToMarketCarryingValue(
        decimal? amount,
        string source,
        DateTimeOffset? capturedAtUtc = null,
        string? evidenceReference = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Carrying-value source is required.", nameof(source));

        Amount = amount;
        Source = source.Trim();
        CapturedAtUtc = capturedAtUtc?.ToUniversalTime();
        EvidenceReference = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
    }

    public decimal? Amount { get; }

    public bool AccountExists => Amount.HasValue;

    public string Source { get; }

    public DateTimeOffset? CapturedAtUtc { get; }

    public string? EvidenceReference { get; }
}

/// <summary>Batch scope for one durable carrying-value hydration.</summary>
public sealed record MarkToMarketCarryingValueRequest(
    string FundId,
    string PeriodId,
    Guid? LedgerBookId,
    DateTimeOffset AsOf,
    string BaseCurrency,
    IReadOnlyList<MarkToMarketPosition> Positions);

/// <summary>
/// Supplies current durable securities-account carrying values in one scoped read. Implementations
/// must return one result for every requested key; use a null amount to report an absent account.
/// </summary>
public interface IMarkToMarketCarryingValueSource
{
    Task<IReadOnlyDictionary<MarkToMarketCarryingValueKey, MarkToMarketCarryingValue>> GetCarryingValuesAsync(
        MarkToMarketCarryingValueRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Request to prepare a governed daily mark-to-market draft for a fund's positions.
/// </summary>
public sealed record DailyMarkToMarketRequest(
    DailyPortfolioPricingPolicy Policy,
    string PeriodId,
    DateTimeOffset AsOf,
    string BaseCurrency,
    IReadOnlyList<MarkToMarketPosition> Positions,
    string Actor,
    string Reason,
    MarkPriceQualityPolicy? QualityPolicy = null,
    Guid? LedgerBookId = null);

/// <summary>
/// Outcome of a daily mark-to-market preparation run. <see cref="Approval"/> is a
/// submitted governed draft awaiting approve/post; <see cref="UnpricedSymbols"/> lists
/// positions that could not be marked and therefore need operator attention.
/// </summary>
public sealed record DailyMarkToMarketRun(
    DailyPortfolioPricingProjection? Projection,
    AutomatedJournalApproval? Approval,
    IReadOnlyList<string> UnpricedSymbols,
    IReadOnlyList<MarkPriceRejection>? RejectedMarks = null,
    IReadOnlyList<AutomatedJournalApproval>? Approvals = null)
{
    public IReadOnlyList<MarkPriceRejection> RejectedMarks { get; init; } = RejectedMarks ?? [];

    public IReadOnlyList<MarkFreshnessAssessmentDto> MarkFreshness { get; init; } = [];

    /// <summary>All per-security/account drafts produced by the valuation batch.</summary>
    public IReadOnlyList<AutomatedJournalApproval> Approvals { get; init; } =
        Approvals ?? (Approval is null ? [] : [Approval]);

    /// <summary>True when a governed draft was submitted for approval.</summary>
    public bool HasDraft => Approvals.Count > 0;

    public int DraftCount => Approvals.Count;

    /// <summary>True when strict completeness policy rejected the whole valuation batch.</summary>
    public bool IsBlocked => Projection is null && Approval is null && RejectedMarks.Count > 0;

    /// <summary>
    /// Stale-priced symbols surfaced for review: those blocked by a
    /// <see cref="StalePriceHandling.Block"/> policy (excluded from the draft) and those retained
    /// under a <see cref="StalePriceHandling.Flag"/> policy (included in the draft but flagged).
    /// Symbols stale under an <see cref="StalePriceHandling.Allow"/> policy are tolerated silently
    /// and are not listed here.
    /// </summary>
    public IReadOnlyList<string> StalePricedSymbols { get; init; } = [];
}

/// <summary>
/// Wires the daily valuation loop: prices positions through an <see cref="IMarkPriceSource"/>,
/// projects balanced fair-value adjustments with <see cref="DailyPortfolioPricingProjector"/>,
/// and submits the result as a governed <see cref="AutomatedJournalApproval"/> draft so the
/// books carry market values once an operator approves and posts it.
/// </summary>
public sealed class DailyMarkToMarketService
{
    private readonly IMarkPriceSource _priceSource;
    private readonly IMarkToMarketCarryingValueSource _carryingValueSource;
    private readonly ILogger _log;

    public DailyMarkToMarketService(IMarkPriceSource priceSource, ILogger? log = null)
        : this(priceSource, ExplicitAbsentCarryingValueSource.Instance, log)
    {
    }

    public DailyMarkToMarketService(
        IMarkPriceSource priceSource,
        IMarkToMarketCarryingValueSource carryingValueSource,
        ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(priceSource);
        ArgumentNullException.ThrowIfNull(carryingValueSource);
        _priceSource = priceSource;
        _carryingValueSource = carryingValueSource;
        _log = log ?? LoggingSetup.ForContext<DailyMarkToMarketService>();
    }

    /// <summary>
    /// Prices the requested positions and submits a governed fair-value draft.
    /// Positions without a price are reported in <see cref="DailyMarkToMarketRun.UnpricedSymbols"/>
    /// and excluded from the draft rather than silently marked at cost.
    /// </summary>
    public Task<DailyMarkToMarketRun> PrepareAsync(DailyMarkToMarketRequest request, CancellationToken ct = default)
        => PrepareCoreAsync(request, previewOnly: false, ct);

    public async Task<ValuationFreshnessPreviewDto> PreviewAsync(DailyMarkToMarketRequest request, CancellationToken ct = default)
    {
        var run = await PrepareCoreAsync(request, previewOnly: true, ct).ConfigureAwait(false);
        var blocked = run.MarkFreshness.Count(static mark => mark.Status == "ReviewRequired");
        return new ValuationFreshnessPreviewDto(ResolveFreshnessPolicy(request).Version,
            run.MarkFreshness.Count, blocked, blocked > 0 ? 1 : 0, run.MarkFreshness, DateTimeOffset.UtcNow);
    }

    private async Task<DailyMarkToMarketRun> PrepareCoreAsync(
        DailyMarkToMarketRequest request, bool previewOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Positions is null || request.Positions.Count == 0)
            throw new ArgumentException("At least one position is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Actor is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Reason is required.", nameof(request));
        if (request.LedgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book identifier cannot be empty when supplied.", nameof(request));

        var asOfDate = DateOnly.FromDateTime(request.AsOf.UtcDateTime);
        var freshnessPolicy = ResolveFreshnessPolicy(request);
        var assessments = new List<MarkFreshnessAssessmentDto>(request.Positions.Count);
        var marks = new List<DailyPortfolioPriceMark>(request.Positions.Count);
        var rejected = new List<MarkPriceRejection>();
        var stalePriced = new List<string>();

        var positionKeys = request.Positions
            .Select(MarkToMarketCarryingValueKey.FromPosition)
            .ToArray();
        var duplicateKey = positionKeys
            .GroupBy(static key => key)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateKey is not null)
        {
            throw new ArgumentException(
                $"Daily valuation position scope contains duplicate security/account key {duplicateKey.Key.Symbol}/{duplicateKey.Key.FinancialAccountId ?? "unscoped"}.",
                nameof(request));
        }

        var carryingValues = await _carryingValueSource.GetCarryingValuesAsync(
            new MarkToMarketCarryingValueRequest(
                request.Policy.FundId,
                request.PeriodId,
                request.LedgerBookId,
                request.AsOf,
                request.BaseCurrency,
                request.Positions),
            ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Carrying-value source returned no result set.");

        foreach (var key in positionKeys)
        {
            if (!carryingValues.TryGetValue(key, out var carryingValue) || carryingValue is null)
            {
                throw new InvalidOperationException(
                    $"Carrying-value source omitted requested security/account key {key.Symbol}/{key.FinancialAccountId ?? "unscoped"}.");
            }
        }

        for (var positionIndex = 0; positionIndex < request.Positions.Count; positionIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var position = request.Positions[positionIndex];
            var carryingValue = carryingValues[positionKeys[positionIndex]];

            var quote = await _priceSource.GetMarkPriceAsync(position.Symbol, asOfDate, ct).ConfigureAwait(false);
            var assessment = freshnessPolicy.Assess(position.Symbol, position.SecurityId,
                position.FinancialAccountId, asOfDate, quote?.PriceAsOf, quote?.Confidence, quote?.Price);
            assessments.Add(assessment);
            if (assessment.BlockReason is { } rejectionReason)
            {
                rejected.Add(new MarkPriceRejection(position.Symbol, rejectionReason, quote?.PriceAsOf,
                    quote?.Confidence, quote?.EvidenceReference));
                if (assessment.AgeDays is { } age && (age < 0 || age > freshnessPolicy.MaximumAgeDays))
                    stalePriced.Add(position.Symbol);
                continue;
            }

            // Assessment admitted a present, dated quote.
            if (quote is null)
                throw new InvalidOperationException("A missing mark cannot be admitted.");
            // Clamped against the quote's origin so neither an optimistic source assertion nor the
            // fund's default level can present a fabricated price as an observable market input.
            var fairValueLevel = FairValueLevelPolicy.Resolve(
                quote.Level,
                request.Policy.DefaultFairValueLevel,
                quote.Provenance);

            if (quote.Provenance.IsNonReal())
            {
                _log.Warning(
                    "Mark price for {Symbol} as of {AsOfDate} originates from {Provenance} source {PriceSource}; " +
                    "retained as a {FairValueLevel} unobservable mark and marked non-real on the valuation draft",
                    position.Symbol, asOfDate, quote.Provenance.Token(), quote.Source, fairValueLevel);
            }

            marks.Add(new DailyPortfolioPriceMark(
                position.Symbol,
                position.Quantity,
                position.CostPrice,
                quote.Price,
                quote.Source,
                quote.EvidenceReference,
                FinancialAccountId: position.FinancialAccountId,
                InstrumentType: position.InstrumentType,
                FairValueLevel: fairValueLevel,
                IsStalePriced: false,
                PriceObservedOn: quote.PriceAsOf,
                Confidence: quote.Confidence,
                SecurityId: position.SecurityId,
                PriorCarryingValue: carryingValue.Amount,
                CarryingValueSource: carryingValue.Source,
                CarryingValueCapturedAtUtc: carryingValue.CapturedAtUtc,
                CarryingValueEvidenceReference: carryingValue.EvidenceReference,
                Provenance: quote.Provenance));
        }

        var unpriced = rejected
            .Select(static item => item.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rejected.Count > 0 || previewOnly)
        {
            _log.Warning(
                "Daily mark-to-market run for fund {FundId} period {PeriodId} blocked because {RejectedCount} marks failed completeness policy",
                request.Policy.FundId, request.PeriodId, rejected.Count);
            return new DailyMarkToMarketRun(null, null, unpriced, rejected)
            {
                MarkFreshness = assessments,
                StalePricedSymbols = stalePriced.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        if (marks.Count == 0)
        {
            _log.Warning(
                "Daily mark-to-market run for fund {FundId} period {PeriodId} priced no positions ({UnpricedCount} unpriced, {StaleCount} stale)",
                request.Policy.FundId, request.PeriodId, unpriced.Length, stalePriced.Count);
            return new DailyMarkToMarketRun(null, null, unpriced, rejected)
            {
                MarkFreshness = assessments,
                StalePricedSymbols = stalePriced.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        var projection = DailyPortfolioPricingProjector.Project(new DailyPortfolioPricingInput(
            new DailyPortfolioPricingPolicy(request.Policy.FundId, request.Policy.PolicyId,
                request.Policy.PolicyName, request.Policy.ValuationMethod, request.Policy.ApprovedBy,
                request.Policy.ApprovedAtUtc, request.Policy.DefaultFairValueLevel,
                freshnessPolicy: freshnessPolicy),
            request.PeriodId,
            request.AsOf,
            request.BaseCurrency,
            marks));

        var drafts = DailyPortfolioPricingDraftBuilder.BuildDrafts(projection);
        if (drafts.Count == 0)
        {
            _log.Information(
                "Daily marks for fund {FundId} period {PeriodId} produced no carrying-value adjustment; nothing to post",
                request.Policy.FundId, request.PeriodId);
            return new DailyMarkToMarketRun(projection, null, unpriced, rejected)
            {
                MarkFreshness = assessments,
                StalePricedSymbols = stalePriced.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        var submittedAtUtc = DateTimeOffset.UtcNow;
        var approvals = drafts
            .Select(draft => AutomatedJournalApproval.Submit(
                draft,
                request.Actor,
                submittedAtUtc,
                request.Reason,
                draft.Metadata.EvidenceReferences.Select(static reference => reference.Uri).ToArray()))
            .ToArray();

        _log.Information(
            "Submitted {DraftCount} fair-value drafts for fund {FundId} period {PeriodId}: net carrying-value adjustment {MarkAdjustment}, cumulative unrealized {NetUnrealized} ({UnpricedCount} unpriced)",
            approvals.Length, request.Policy.FundId, request.PeriodId,
            projection.NetMarkAdjustment, projection.NetUnrealizedGainOrLoss, unpriced.Length);

        return new DailyMarkToMarketRun(projection, approvals[0], unpriced, rejected, approvals)
        {
            MarkFreshness = assessments,
            StalePricedSymbols = stalePriced.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static ValuationFreshnessPolicy ResolveFreshnessPolicy(DailyMarkToMarketRequest request)
    {
        var owner = request.Policy.FreshnessPolicy;
        if (request.QualityPolicy is not { } legacy)
            return owner;
        var age = Math.Min(owner.MaximumAgeDays, checked((int)Math.Floor(legacy.MaximumAge.TotalDays)));
        var confidence = legacy.MinimumConfidence > owner.MinimumConfidence
            ? legacy.MinimumConfidence : owner.MinimumConfidence;
        return new ValuationFreshnessPolicy(age, confidence,
            $"{owner.Version}/resolved/{age}/{confidence}");
    }

    /// <summary>
    /// Compatibility source for direct/synthetic callers that have no durable ledger. It reports
    /// account absence explicitly, causing the projector to use cost basis only for that case.
    /// Production composition should inject a ledger-backed source.
    /// </summary>
    private sealed class ExplicitAbsentCarryingValueSource : IMarkToMarketCarryingValueSource
    {
        public static ExplicitAbsentCarryingValueSource Instance { get; } = new();

        public Task<IReadOnlyDictionary<MarkToMarketCarryingValueKey, MarkToMarketCarryingValue>> GetCarryingValuesAsync(
            MarkToMarketCarryingValueRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyDictionary<MarkToMarketCarryingValueKey, MarkToMarketCarryingValue> result = request.Positions
                .Select(MarkToMarketCarryingValueKey.FromPosition)
                .ToDictionary(
                    static key => key,
                    static _ => new MarkToMarketCarryingValue(
                        amount: null,
                        source: "explicit-account-absent:cost-basis-fallback"));
            return Task.FromResult(result);
        }
    }
}
