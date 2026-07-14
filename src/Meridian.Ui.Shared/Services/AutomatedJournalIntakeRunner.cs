using Meridian.Application.Accounting;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Request to produce dividend events from Security Master corporate actions and land
/// them in the manual journal workbench queue for the fund profile.
/// </summary>
public sealed record RunDividendDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    IReadOnlyList<DividendAccrualPosition> Positions,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    Guid? LedgerBookId = null,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    decimal WithholdingTaxRate = 0m,
    DateTimeOffset? AsOf = null,
    decimal MinimumEvidenceConfidence = 0.75m);

/// <summary>
/// Request to accrue period fees from fund fee terms and land the drafts in the manual
/// journal workbench queue for the fund profile.
/// </summary>
public sealed record RunFeeAccrualDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    string PeriodId,
    decimal BeginningNav,
    decimal EndingNavBeforeFees,
    decimal HighWaterMark,
    decimal ManagementFeeRate,
    decimal PerformanceFeeRate,
    Guid? LedgerBookId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    DateTimeOffset? AsOf = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    DateTimeOffset? EvidenceRetainedAtUtc = null);

/// <summary>
/// Request to project period-close closing entries from a closed ledger period's trial
/// balance and land the governed draft in the manual journal workbench queue.
/// </summary>
public sealed record RunPeriodCloseDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    Guid PeriodId,
    Guid? LedgerBookId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null);

/// <summary>
/// Explicitly governed end-of-day valuation request. Prices are resolved server-side
/// from the registered historical provider chain; callers provide positions and policy,
/// never the closing prices that enter the books.
/// </summary>
public sealed record RunDailyMarkToMarketDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    Guid LedgerBookId,
    Guid PeriodId,
    DateTimeOffset AsOf,
    IReadOnlyList<MarkToMarketPosition> Positions,
    string PolicyId,
    string PolicyName,
    string ValuationMethod,
    string PolicyApprovedBy,
    DateTimeOffset PolicyApprovedAtUtc,
    string Reason,
    int MaximumMarkAgeDays = 3,
    DailyPortfolioPriceConfidence MinimumConfidence = DailyPortfolioPriceConfidence.Medium,
    bool RequireCompleteCoverage = true,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null);

/// <summary>
/// Outcome of one automated intake run: producer-side skips plus the intake result
/// (created drafts and intake-side skips). Empty productions return an empty intake
/// rather than an error.
/// </summary>
public sealed record AutomatedJournalIntakeRunResult(
    IReadOnlyList<AutomatedJournalEventProductionSkip> ProducerSkips,
    AutomatedJournalDraftIntakeResult Intake,
    IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto>? EvidenceAssessments = null)
{
    public IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto> EvidenceAssessments { get; init; } =
        EvidenceAssessments ?? new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Valuation evidence and workbench intake outcome for one daily-close run.</summary>
public sealed record DailyMarkToMarketIntakeRunResult(
    DailyMarkToMarketRun Valuation,
    AutomatedJournalDraftIntakeResult Intake);

internal sealed record PeriodCloseDraftPreview(
    LedgerPeriodDto Period,
    LedgerPeriodSummaryDto Summary,
    PeriodCloseProjection Projection,
    AutomatedJournalDraft? Draft);

/// <summary>
/// Wires the automated event producers to <see cref="AutomatedJournalDraftIntakeService"/>:
/// corporate-action dividends and fee-schedule accruals become governed drafts in the
/// close cockpit's approval queue. The dividend lane requires the Security Master query
/// service; when it is not configured the run fails loudly instead of producing nothing.
/// </summary>
public sealed class AutomatedJournalIntakeRunner
{
    private static readonly AutomatedJournalDraftIntakeResult EmptyIntake = new([], []);

    private readonly AutomatedJournalDraftIntakeService _intake;
    private readonly FeeScheduleAccrualEventProducer _feeProducer;
    private readonly CorporateActionDividendEventProducer? _dividendProducer;
    private readonly ILedgerBookService? _ledgerBookService;
    private readonly DailyMarkToMarketService? _dailyMarkToMarketService;

    public AutomatedJournalIntakeRunner(
        AutomatedJournalDraftIntakeService intake,
        FeeScheduleAccrualEventProducer feeProducer,
        CorporateActionDividendEventProducer? dividendProducer = null,
        ILedgerBookService? ledgerBookService = null,
        DailyMarkToMarketService? dailyMarkToMarketService = null)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _feeProducer = feeProducer ?? throw new ArgumentNullException(nameof(feeProducer));
        _dividendProducer = dividendProducer;
        _ledgerBookService = ledgerBookService;
        _dailyMarkToMarketService = dailyMarkToMarketService;
    }

    public async Task<AutomatedJournalIntakeRunResult> RunDividendIntakeAsync(
        RunDividendDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_dividendProducer is null)
        {
            throw new InvalidOperationException(
                "Corporate-action dividend intake requires the Security Master query service, which is not configured.");
        }

        var production = await _dividendProducer.ProduceAsync(
            new CorporateActionDividendRequest(
                request.Positions,
                request.WindowStart,
                request.WindowEnd,
                request.AsOf ?? DateTimeOffset.UtcNow,
                request.WithholdingTaxRate,
                request.MinimumEvidenceConfidence),
            ct).ConfigureAwait(false);

        var intake = production.Events.Count == 0
            ? EmptyIntake
            : await _intake.IntakeAsync(
                new AutomatedJournalDraftIntakeRequest(
                    request.FundProfileId,
                    request.Currency,
                    production.Events,
                    request.Actor,
                    request.LedgerBookId,
                    request.PeriodId,
                    request.EntityId,
                    request.TenantId,
                    request.CompanyId,
                    production.EvidenceAssessments),
                ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult(production.Skipped, intake, production.EvidenceAssessments);
    }

    /// <summary>
    /// Resolves policy-qualified closing marks and admits the resulting fair-value draft
    /// into the existing human approval queue. Posting remains exclusively available via
    /// the workbench lifecycle and its governed durable posting target.
    /// </summary>
    public async Task<DailyMarkToMarketIntakeRunResult> RunDailyMarkToMarketIntakeAsync(
        RunDailyMarkToMarketDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_dailyMarkToMarketService is null)
        {
            throw new InvalidOperationException(
                "Daily mark-to-market intake requires the registered historical provider chain, which is not configured.");
        }
        if (request.LedgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(request));
        if (request.PeriodId == Guid.Empty)
            throw new ArgumentException("Ledger period id is required.", nameof(request));
        if (request.MaximumMarkAgeDays < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum mark age cannot be negative.");

        var policy = new DailyPortfolioPricingPolicy(
            request.FundProfileId,
            request.PolicyId,
            request.PolicyName,
            request.ValuationMethod,
            request.PolicyApprovedBy,
            request.PolicyApprovedAtUtc);
        var valuation = await _dailyMarkToMarketService.PrepareAsync(
            new DailyMarkToMarketRequest(
                policy,
                request.PeriodId.ToString("D"),
                request.AsOf,
                request.Currency,
                request.Positions,
                request.Actor,
                request.Reason,
                new MarkPriceQualityPolicy(
                    TimeSpan.FromDays(request.MaximumMarkAgeDays),
                    request.MinimumConfidence,
                    request.RequireCompleteCoverage,
                    RequireObservedDate: true)),
            ct).ConfigureAwait(false);

        if (valuation.Approval is null)
        {
            return new DailyMarkToMarketIntakeRunResult(valuation, EmptyIntake);
        }

        var intake = await _intake.IntakeDraftsAsync(
            new AutomatedJournalPreparedDraftIntakeRequest(
                request.FundProfileId,
                request.Currency,
                [valuation.Approval.Draft],
                request.Actor,
                request.LedgerBookId,
                request.PeriodId.ToString("D"),
                request.EntityId,
                request.TenantId,
                request.CompanyId),
            ct).ConfigureAwait(false);

        return new DailyMarkToMarketIntakeRunResult(valuation, intake);
    }

    /// <summary>
    /// Projects closing entries from a closed period's trial balance and admits the
    /// resulting draft into the workbench queue. The period must already be soft- or
    /// hard-closed: closing entries are the accounting consequence of a close decision,
    /// not a way to make one. A period with no temporary-account balances returns an
    /// empty intake — a correct outcome, not a gap.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunPeriodCloseIntakeAsync(
        RunPeriodCloseDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        var preview = await PreviewPeriodCloseAsync(request, ct).ConfigureAwait(false);
        var draft = preview.Draft;
        var intake = draft is null
            ? EmptyIntake
            : await _intake.IntakeDraftsAsync(
                new AutomatedJournalPreparedDraftIntakeRequest(
                    request.FundProfileId,
                    request.Currency,
                    [draft],
                    request.Actor,
                    preview.Summary.LedgerBookId,
                    request.PeriodId.ToString("D"),
                    request.EntityId,
                    request.TenantId,
                    request.CompanyId),
                ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult([], intake);
    }

    internal async Task<PeriodCloseDraftPreview> PreviewPeriodCloseAsync(
        RunPeriodCloseDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_ledgerBookService is null)
        {
            throw new InvalidOperationException(
                "Period-close intake requires the ledger book service, which is not configured.");
        }

        var summary = await _ledgerBookService.GetPeriodSummaryAsync(request.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Ledger period '{request.PeriodId}' was not found or is still open; close the period before running closing entries.");

        if (request.LedgerBookId is { } requestedBook && requestedBook != summary.LedgerBookId)
        {
            throw new InvalidOperationException(
                $"Ledger period '{request.PeriodId}' belongs to book '{summary.LedgerBookId}', not the requested book '{requestedBook}'.");
        }

        var period = (await _ledgerBookService
                .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: summary.LedgerBookId), ct)
                .ConfigureAwait(false))
            .FirstOrDefault(p => p.PeriodId == request.PeriodId)
            ?? throw new InvalidOperationException(
                $"Ledger period '{request.PeriodId}' was not found in book '{summary.LedgerBookId}'.");
        var closingDate = new DateTimeOffset(period.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var projection = PeriodCloseProjector.Project(new PeriodCloseInput(
            request.PeriodId.ToString("D"),
            closingDate,
            BuildTrialBalance(summary.TrialBalance),
            request.Actor));

        return new PeriodCloseDraftPreview(
            period,
            summary,
            projection,
            PeriodCloseDraftBuilder.BuildDraft(projection));
    }

    private static IReadOnlyList<PeriodCloseAccountBalance> BuildTrialBalance(
        IReadOnlyList<LedgerPeriodTrialBalanceLineDto> lines)
    {
        // Preserve the dimensional scope of each trial-balance row: rows sharing an account but
        // split across entities/sleeves must stay separate so the close zeroes each dimension's
        // balance and rolls its retained earnings independently, rather than posting one aggregate.
        var balances = new Dictionary<string, (LedgerAccount Account, LedgerLineDimensionSet? Dimensions, decimal Balance)>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!Enum.TryParse<LedgerAccountType>(line.AccountType, ignoreCase: true, out var accountType))
            {
                throw new InvalidOperationException(
                    $"Trial balance account '{line.AccountName}' has unrecognized account type '{line.AccountType}'; closing entries cannot be projected safely.");
            }

            var account = new LedgerAccount(line.AccountName, accountType, line.Symbol, line.FinancialAccountId);
            var dimensions = LedgerDimensionMapper.ToDomain(line.Dimensions);
            var key = FormattableString.Invariant(
                $"{account.Name}|{account.AccountType}|{account.Symbol}|{account.FinancialAccountId}|{DimensionKey(dimensions)}");

            if (balances.TryGetValue(key, out var existing))
            {
                balances[key] = existing with { Balance = existing.Balance + line.Balance };
            }
            else
            {
                balances[key] = (account, dimensions, line.Balance);
            }
        }

        return balances.Values
            .Select(static row => new PeriodCloseAccountBalance(row.Account, row.Balance, row.Dimensions))
            .ToArray();
    }

    private static string DimensionKey(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
            return string.Empty;

        var externalGl = string.Join(
            ";",
            dimensions.ExternalGlDimensions
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => FormattableString.Invariant($"{pair.Key}={pair.Value}")));

        var key = string.Join(
            "|",
            dimensions.FundId, dimensions.EntityId, dimensions.SleeveId, dimensions.StrategyId,
            dimensions.InvestorId, dimensions.CapitalAccountId, dimensions.InstrumentId?.ToString("D"),
            dimensions.TaxLotId, dimensions.CostCenterId, dimensions.CounterpartyId,
            dimensions.OrganizationId, dimensions.PortfolioId, dimensions.BookId,
            dimensions.AccountId, dimensions.CustomerId, dimensions.VendorId, dimensions.ProjectId,
            externalGl);

        return dimensions.PositionId.HasValue
            ? $"{key}|positionId={dimensions.PositionId.Value:D}"
            : key;
    }

    public async Task<AutomatedJournalIntakeRunResult> RunFeeAccrualIntakeAsync(
        RunFeeAccrualDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var eventAsOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var retainedAtUtc = request.EvidenceRetainedAtUtc ?? eventAsOf;
        var production = _feeProducer.Produce(new FeeScheduleAccrualRequest(
            request.FundProfileId,
            request.PeriodId,
            eventAsOf,
            request.BeginningNav,
            request.EndingNavBeforeFees,
            request.HighWaterMark,
            request.ManagementFeeRate,
            request.PerformanceFeeRate));
        var events = AttachFeeScheduleEvidence(
            production.Events,
            request.EvidenceLinks,
            retainedAtUtc,
            request.Actor,
            request.FundProfileId,
            request.PeriodId);

        var intake = events.Count == 0
            ? EmptyIntake
            : await _intake.IntakeAsync(
                new AutomatedJournalDraftIntakeRequest(
                    request.FundProfileId,
                    request.Currency,
                    events,
                    request.Actor,
                    request.LedgerBookId,
                    request.PeriodId,
                    request.EntityId,
                    request.TenantId,
                    request.CompanyId),
                ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult(production.Skipped, intake);
    }

    private static IReadOnlyList<AutomatedJournalEvent> AttachFeeScheduleEvidence(
        IReadOnlyList<AutomatedJournalEvent> events,
        IReadOnlyList<string>? evidenceLinks,
        DateTimeOffset retainedAtUtc,
        string retainedBy,
        string fundProfileId,
        string periodId)
    {
        var references = (evidenceLinks ?? [])
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select((link, index) => new JournalEvidenceReference(
                EvidenceId: $"fee-schedule:{fundProfileId.Trim()}:{periodId.Trim()}:{index + 1}",
                Uri: link.Trim(),
                Kind: "fee-schedule",
                SourceSystem: "automated-journal-scheduler",
                RetainedAtUtc: retainedAtUtc,
                RetainedBy: retainedBy,
                SubjectId: fundProfileId.Trim()))
            .ToArray();
        if (references.Length == 0)
            return events;

        return events.Select(journalEvent => journalEvent with
            {
                EvidenceReferences = journalEvent.EvidenceReferences
                    .Concat(references)
                    .DistinctBy(static reference => reference.Uri, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();
    }
}
