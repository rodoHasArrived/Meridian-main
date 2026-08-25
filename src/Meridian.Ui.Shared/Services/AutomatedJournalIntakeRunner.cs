using Meridian.Application.Accounting;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Contracts.Integrity;

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
    decimal MinimumEvidenceConfidence = 0.75m,
    int MaximumPositionAgeDays = 7);

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
    DateTimeOffset? EvidenceRetainedAtUtc = null,
    AutomatedJournalCapitalAccountReconciliationDto? CapitalAccountReconciliation = null,
    decimal MinimumCapitalAccountConfidence = 0.90m);

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
    string? CompanyId = null,
    string? BatchCorrelationId = null);

/// <summary>
/// Outcome of one automated intake run: producer-side skips plus the intake result
/// (created drafts and intake-side skips). Empty productions return an empty intake
/// rather than an error.
/// </summary>
public enum AutomatedJournalIntakeReadiness
{
    Ready = 0,
    NeedsInvestigation = 1,
    Blocked = 2
}

public sealed record AutomatedJournalIntakeRunResult(
    IReadOnlyList<AutomatedJournalEventProductionSkip> ProducerSkips,
    AutomatedJournalDraftIntakeResult Intake,
    IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto>? EvidenceAssessments = null,
    AutomatedJournalIntakeReadiness Readiness = AutomatedJournalIntakeReadiness.Ready,
    IReadOnlyList<string>? ReadinessBlockers = null)
{
    public IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto> EvidenceAssessments { get; init; } =
        EvidenceAssessments ?? new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ReadinessBlockers { get; init; } = ReadinessBlockers ?? [];
}

/// <summary>Valuation evidence and workbench intake outcome for one daily-close run.</summary>
public sealed record DailyMarkToMarketIntakeRunResult(
    DailyMarkToMarketRun Valuation,
    AutomatedJournalDraftIntakeResult Intake,
    string? BatchCorrelationId = null);

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
    private readonly DailyValuationPositionService? _dailyValuationPositionService;
    private readonly AutomatedJournalEvidencePolicy _evidencePolicy;
    private readonly IAutomatedJournalCapitalAccountReconciliationResolver? _capitalAccountReconciliationResolver;
    private readonly TimeProvider _timeProvider;
    private readonly IManualJournalEntryWorkbenchService? _manualJournalWorkbench;

    public AutomatedJournalIntakeRunner(
        AutomatedJournalDraftIntakeService intake,
        FeeScheduleAccrualEventProducer feeProducer,
        CorporateActionDividendEventProducer? dividendProducer = null,
        ILedgerBookService? ledgerBookService = null,
        DailyMarkToMarketService? dailyMarkToMarketService = null,
        DailyValuationPositionService? dailyValuationPositionService = null,
        AutomatedJournalEvidencePolicy? evidencePolicy = null,
        IAutomatedJournalCapitalAccountReconciliationResolver? capitalAccountReconciliationResolver = null,
        TimeProvider? timeProvider = null,
        IManualJournalEntryWorkbenchService? manualJournalWorkbench = null)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _feeProducer = feeProducer ?? throw new ArgumentNullException(nameof(feeProducer));
        _dividendProducer = dividendProducer;
        _ledgerBookService = ledgerBookService;
        _dailyMarkToMarketService = dailyMarkToMarketService;
        _dailyValuationPositionService = dailyValuationPositionService;
        _evidencePolicy = evidencePolicy ?? AutomatedJournalEvidencePolicy.Default;
        _capitalAccountReconciliationResolver = capitalAccountReconciliationResolver;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _manualJournalWorkbench = manualJournalWorkbench;
    }

    /// <summary>Whether this process can execute the provider-backed daily valuation lane.</summary>
    public bool CanRunDailyMarkToMarket =>
        _dailyMarkToMarketService is not null && _dailyValuationPositionService is not null;

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
                request.Currency,
                request.WindowStart,
                request.WindowEnd,
                request.AsOf ?? DateTimeOffset.UtcNow,
                request.WithholdingTaxRate,
                request.MinimumEvidenceConfidence,
                request.MaximumPositionAgeDays),
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
        if (_dailyValuationPositionService is null)
        {
            throw new InvalidOperationException(
                "Daily mark-to-market intake requires canonical Security Master position resolution, which is not configured.");
        }

        var positionResolution = await _dailyValuationPositionService
            .ResolveAdHocAsync(request.Positions, request.Currency, request.AsOf, ct)
            .ConfigureAwait(false);
        if (!positionResolution.IsReady)
        {
            throw new InvalidOperationException(
                $"Daily mark-to-market intake is blocked: {string.Join(" ", positionResolution.Blockers)}");
        }

        // The scheduled valuation's maximum mark age maps onto the ledger stale-price policy, while
        // MarkPriceQualityPolicy enforces observed-date, minimum-confidence, and complete-coverage
        // controls before any fair-value draft can be retained.
        var policy = new DailyPortfolioPricingPolicy(
            request.FundProfileId,
            request.PolicyId,
            request.PolicyName,
            request.ValuationMethod,
            request.PolicyApprovedBy,
            request.PolicyApprovedAtUtc,
            stalePricePolicy: StalePricePolicy.Of(request.MaximumMarkAgeDays, StalePriceHandling.Block));
        var valuation = await _dailyMarkToMarketService.PrepareAsync(
            new DailyMarkToMarketRequest(
                policy,
                request.PeriodId.ToString("D"),
                request.AsOf,
                request.Currency,
                positionResolution.Positions,
                request.Actor,
                request.Reason,
                new MarkPriceQualityPolicy(
                    TimeSpan.FromDays(request.MaximumMarkAgeDays),
                    request.MinimumConfidence,
                    request.RequireCompleteCoverage,
                    RequireObservedDate: true),
                request.LedgerBookId),
            ct).ConfigureAwait(false);

        var batchCorrelationId = BuildDailyValuationBatchCorrelationId(
            request,
            positionResolution.Positions,
            valuation.Approvals,
            request.BatchCorrelationId);

        if (valuation.Approvals.Count == 0)
        {
            return new DailyMarkToMarketIntakeRunResult(valuation, EmptyIntake, batchCorrelationId);
        }

        var intake = await _intake.IntakeDraftsAsync(
            new AutomatedJournalPreparedDraftIntakeRequest(
                request.FundProfileId,
                request.Currency,
                valuation.Approvals.Select(static approval => approval.Draft).ToArray(),
                request.Actor,
                request.LedgerBookId,
                request.PeriodId.ToString("D"),
                request.EntityId,
                request.TenantId,
                request.CompanyId,
                BatchCorrelationId: batchCorrelationId),
            ct).ConfigureAwait(false);

        return new DailyMarkToMarketIntakeRunResult(valuation, intake, batchCorrelationId);
    }

    private static string BuildDailyValuationBatchCorrelationId(
        RunDailyMarkToMarketDraftIntakeRequest request,
        IReadOnlyList<MarkToMarketPosition> positions,
        IReadOnlyList<AutomatedJournalApproval> approvals,
        string? requestedCorrelationSeed)
    {
        var draftRevision = approvals.Count == 0
            ? "no-adjustment"
            : string.Join('|', approvals
                .Select(static approval => approval.Draft.Metadata.IdempotencyKey)
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Order(StringComparer.Ordinal));
        var seed = FormattableString.Invariant(
            $"daily-valuation-batch|{requestedCorrelationSeed?.Trim() ?? "unseeded"}|{request.FundProfileId.Trim().ToLowerInvariant()}|{request.LedgerBookId:N}|{request.PeriodId:N}|{request.AsOf.ToUniversalTime():O}|{DailyValuationPositionService.ComputeStaticPositionHash(positions)}|{draftRevision}");
        return new Guid(Sha256Digest.ComputeBytesUtf8(seed).AsSpan(0, 16))
            .ToString("D");
    }

    /// <summary>
    /// Projects closing entries from a closed period's trial balance and admits the
    /// resulting draft into the workbench queue. Mutating intake is allowed only while the
    /// period is soft-closed; hard-closed periods remain available through the read-only preview
    /// path but cannot acquire new drafts. A period with no temporary-account balances returns
    /// an empty intake — a correct outcome, not a gap.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunPeriodCloseIntakeAsync(
        RunPeriodCloseDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        var preview = await PreviewPeriodCloseAsync(request, ct).ConfigureAwait(false);
        if (preview.Period.Status != LedgerPeriodStatusDto.SoftClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{preview.Period.Label}' must be soft-closed before closing-entry drafts can be queued; current status is {preview.Period.Status}.");
        }

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

    public Task<AutomatedJournalIntakeRunResult> RunFeeAccrualIntakeAsync(
        RunFeeAccrualDraftIntakeRequest request,
        CancellationToken ct = default)
        => RunFeeAccrualIntakeAtAsync(request, _timeProvider.GetUtcNow(), ct);

    internal async Task<AutomatedJournalIntakeRunResult> RunFeeAccrualIntakeAtAsync(
        RunFeeAccrualDraftIntakeRequest request,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        evaluatedAtUtc = evaluatedAtUtc.ToUniversalTime();
        var eventAsOf = request.AsOf ?? evaluatedAtUtc;
        var (reconciliation, sourceBlocker) = await ResolveCapitalAccountReconciliationAsync(
            request,
            evaluatedAtUtc,
            ct).ConfigureAwait(false);
        var feeEvidence = AutomatedJournalFeeEvidenceEvaluator.Evaluate(
            request.PeriodId,
            request.Currency,
            request.BeginningNav,
            request.EndingNavBeforeFees,
            request.HighWaterMark,
            reconciliation,
            request.MinimumCapitalAccountConfidence,
            evaluatedAtUtc,
            _evidencePolicy);
        var readinessBlockers = sourceBlocker is null
            ? feeEvidence.Blockers
            : feeEvidence.Blockers
                .Append(sourceBlocker)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var assessment = sourceBlocker is null
            ? feeEvidence.Assessment
            : feeEvidence.Assessment with
            {
                Summary = $"{feeEvidence.Assessment.Summary} {sourceBlocker}",
                Reasons = readinessBlockers
            };
        var feeAssessmentKey = FormattableString.Invariant(
            $"fee-basis|{request.FundProfileId.Trim().ToLowerInvariant()}|{request.PeriodId.Trim().ToLowerInvariant()}");
        if (!feeEvidence.IsReady)
        {
            return new AutomatedJournalIntakeRunResult(
                ProducerSkips: [],
                Intake: EmptyIntake,
                EvidenceAssessments: new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase)
                {
                    [feeAssessmentKey] = assessment
                },
                Readiness: feeEvidence.FailureState == AutomatedJournalScheduleStateDto.Blocked
                    ? AutomatedJournalIntakeReadiness.Blocked
                    : AutomatedJournalIntakeReadiness.NeedsInvestigation,
                ReadinessBlockers: readinessBlockers);
        }

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
            feeEvidence.EvidenceLinks.Select(static link => link.Route).ToArray(),
            evaluatedAtUtc,
            request.Actor,
            request.FundProfileId,
            request.PeriodId);

        var evidenceAssessments = events
            .Where(static journalEvent => !string.IsNullOrWhiteSpace(journalEvent.IdempotencyKey))
            .ToDictionary(
                static journalEvent => journalEvent.IdempotencyKey!,
                _ => assessment,
                StringComparer.OrdinalIgnoreCase);
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
                    request.CompanyId,
                    evidenceAssessments),
                ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult(
            production.Skipped,
            intake,
            evidenceAssessments,
            AutomatedJournalIntakeReadiness.Ready,
            []);
    }

    /// <summary>
    /// Plans a fund-level capital call over the operator-attested commitment register and admits
    /// the per-LP issuance drafts into the human approval queue. The called-to-date basis is
    /// recomputed server-side from posted private-capital fund events before planning; runs whose
    /// evidence or capacity cannot be corroborated return Blocked with reasons instead of drafts.
    /// Posting remains exclusively available via the governed workbench lifecycle.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunCapitalCallIssuanceIntakeAsync(
        RunCapitalCallIssuanceDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FundProfileId))
            throw new ArgumentException("Fund profile identifier is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CallId))
            throw new ArgumentException("Capital-call identifier is required.", nameof(request));

        var evaluatedAtUtc = (request.AsOf ?? _timeProvider.GetUtcNow()).ToUniversalTime();
        var runKey = CapitalCallIssuanceDraftProducer.BuildRunAssessmentKey(request.FundProfileId, request.CallId);
        if (_manualJournalWorkbench is null)
        {
            return BuildBlockedCapitalCallResult(
                runKey,
                CapitalCallIssuanceDraftProducer.AssessmentCode,
                "Capital-call issuance",
                "The posted private-capital activity source is unavailable; capital-call issuance cannot corroborate the commitment register.");
        }

        IReadOnlyList<PrivateCapitalFundEventDto> postedFundEvents;
        try
        {
            var activity = await _manualJournalWorkbench
                .GetPrivateCapitalActivityAsync(
                    request.FundProfileId,
                    request.LedgerBookId,
                    ct,
                    request.TenantId,
                    request.CompanyId)
                .ConfigureAwait(false);
            postedFundEvents = activity.FundEvents;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return BuildBlockedCapitalCallResult(
                runKey,
                CapitalCallIssuanceDraftProducer.AssessmentCode,
                "Capital-call issuance",
                "The posted private-capital activity projection could not be read; capital-call issuance is blocked.");
        }

        var production = CapitalCallIssuanceDraftProducer.Produce(request, postedFundEvents, evaluatedAtUtc);
        if (!production.IsReady)
        {
            return new AutomatedJournalIntakeRunResult(
                production.Skipped,
                EmptyIntake,
                production.EvidenceAssessments,
                production.Readiness,
                production.Blockers);
        }

        var intake = await _intake.IntakeDraftsAsync(
            new AutomatedJournalPreparedDraftIntakeRequest(
                request.FundProfileId,
                request.Currency,
                production.Drafts,
                request.Actor,
                request.LedgerBookId,
                request.PeriodId,
                request.EntityId,
                request.TenantId,
                request.CompanyId,
                production.EvidenceAssessments,
                BatchCorrelationId: runKey),
            ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult(
            production.Skipped,
            intake,
            production.EvidenceAssessments,
            AutomatedJournalIntakeReadiness.Ready,
            []);
    }

    /// <summary>
    /// Records LP cash receipts against an issued capital call and admits the per-LP funding
    /// drafts (Dr Cash / Cr Capital Call Receivable) into the human approval queue. The fundable
    /// ceiling is recomputed server-side from the call's posted ledger activity — issuance debits
    /// minus funding credits on each LP's receivable — before drafting; runs that name an
    /// unissued call, exceed the open receivable, or carry no retained funding evidence return
    /// Blocked with reasons instead of drafts. Partial funding drafts the funded portion and
    /// leaves the receivable balance open. Posting remains exclusively available via the governed
    /// workbench lifecycle.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunCapitalCallFundingIntakeAsync(
        RunCapitalCallFundingDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FundProfileId))
            throw new ArgumentException("Fund profile identifier is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CallId))
            throw new ArgumentException("Capital-call identifier is required.", nameof(request));

        var evaluatedAtUtc = (request.AsOf ?? _timeProvider.GetUtcNow()).ToUniversalTime();
        var runKey = CapitalCallFundingDraftProducer.BuildRunAssessmentKey(request.FundProfileId, request.CallId);
        if (_manualJournalWorkbench is null)
        {
            return BuildBlockedCapitalCallResult(
                runKey,
                CapitalCallFundingDraftProducer.AssessmentCode,
                "Capital-call funding",
                "The posted private-capital activity source is unavailable; capital-call funding cannot corroborate the posted issuance.");
        }

        PrivateCapitalActivityProjectionDto activity;
        try
        {
            activity = await _manualJournalWorkbench
                .GetPrivateCapitalActivityAsync(
                    request.FundProfileId,
                    request.LedgerBookId,
                    ct,
                    request.TenantId,
                    request.CompanyId)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return BuildBlockedCapitalCallResult(
                runKey,
                CapitalCallFundingDraftProducer.AssessmentCode,
                "Capital-call funding",
                "The posted private-capital activity projection could not be read; capital-call funding is blocked.");
        }

        var production = CapitalCallFundingDraftProducer.Produce(
            request,
            activity.FundEvents,
            activity.LedgerImpacts,
            evaluatedAtUtc);
        if (!production.IsReady)
        {
            return new AutomatedJournalIntakeRunResult(
                production.Skipped,
                EmptyIntake,
                production.EvidenceAssessments,
                production.Readiness,
                production.Blockers);
        }

        var intake = await _intake.IntakeDraftsAsync(
            new AutomatedJournalPreparedDraftIntakeRequest(
                request.FundProfileId,
                request.Currency,
                production.Drafts,
                request.Actor,
                request.LedgerBookId,
                request.PeriodId,
                request.EntityId,
                request.TenantId,
                request.CompanyId,
                production.EvidenceAssessments,
                BatchCorrelationId: runKey),
            ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult(
            production.Skipped,
            intake,
            production.EvidenceAssessments,
            AutomatedJournalIntakeReadiness.Ready,
            []);
    }

    private static AutomatedJournalIntakeRunResult BuildBlockedCapitalCallResult(
        string runKey,
        string assessmentCode,
        string laneLabel,
        string blocker)
        => new(
            ProducerSkips: [],
            Intake: EmptyIntake,
            EvidenceAssessments: new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase)
            {
                [runKey] = new AutomatedJournalEvidenceAssessmentDto(
                    assessmentCode,
                    ConfidenceScore: 0m,
                    Quality: AutomatedJournalEvidenceQualityDto.Low,
                    RequiresInvestigation: true,
                    Summary: $"{laneLabel} cannot enter approval: {blocker}",
                    Reasons: [blocker])
            },
            Readiness: AutomatedJournalIntakeReadiness.Blocked,
            ReadinessBlockers: [blocker]);

    private async Task<(AutomatedJournalCapitalAccountReconciliationDto? Reconciliation, string? Blocker)>
        ResolveCapitalAccountReconciliationAsync(
            RunFeeAccrualDraftIntakeRequest request,
            DateTimeOffset evaluatedAtUtc,
            CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.CompanyId) ||
            string.IsNullOrWhiteSpace(request.FundProfileId) ||
            !request.LedgerBookId.HasValue ||
            request.LedgerBookId.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.EntityId) ||
            string.IsNullOrWhiteSpace(request.PeriodId) ||
            string.IsNullOrWhiteSpace(request.Currency))
        {
            return (null, "Server-owned capital-account reconciliation requires exact tenant, company, fund, ledger-book, entity, period, and currency scope.");
        }

        if (_capitalAccountReconciliationResolver is null)
        {
            return (null, "The server-owned capital-account reconciliation source is unavailable.");
        }

        try
        {
            var scope = new AutomatedJournalCapitalAccountReconciliationScope(
                request.TenantId.Trim(),
                request.CompanyId.Trim(),
                request.FundProfileId.Trim(),
                request.LedgerBookId.Value,
                request.EntityId.Trim(),
                request.PeriodId.Trim(),
                request.Currency.Trim().ToUpperInvariant(),
                evaluatedAtUtc);
            var reconciliation = await _capitalAccountReconciliationResolver
                .ResolveAsync(scope, ct)
                .ConfigureAwait(false);
            return reconciliation is null
                ? (null, "No server-owned capital-account reconciliation was available for the exact fee-accrual execution scope.")
                : (reconciliation, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return (null, "The server-owned capital-account reconciliation source could not be read; fee-accrual preparation is blocked.");
        }
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
