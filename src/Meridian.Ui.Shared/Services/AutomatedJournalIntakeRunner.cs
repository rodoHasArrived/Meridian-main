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
    string? CompanyId = null);

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
    string? CompanyId = null);

/// <summary>
/// Outcome of one automated intake run: producer-side skips plus the intake result
/// (created drafts and intake-side skips). Empty productions return an empty intake
/// rather than an error.
/// </summary>
public sealed record AutomatedJournalIntakeRunResult(
    IReadOnlyList<AutomatedJournalEventProductionSkip> ProducerSkips,
    AutomatedJournalDraftIntakeResult Intake);

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

    public AutomatedJournalIntakeRunner(
        AutomatedJournalDraftIntakeService intake,
        FeeScheduleAccrualEventProducer feeProducer,
        CorporateActionDividendEventProducer? dividendProducer = null)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _feeProducer = feeProducer ?? throw new ArgumentNullException(nameof(feeProducer));
        _dividendProducer = dividendProducer;
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
                DateTimeOffset.UtcNow),
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
                    request.CompanyId),
                ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult(production.Skipped, intake);
    }

    public async Task<AutomatedJournalIntakeRunResult> RunFeeAccrualIntakeAsync(
        RunFeeAccrualDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var production = _feeProducer.Produce(new FeeScheduleAccrualRequest(
            request.FundProfileId,
            request.PeriodId,
            DateTimeOffset.UtcNow,
            request.BeginningNav,
            request.EndingNavBeforeFees,
            request.HighWaterMark,
            request.ManagementFeeRate,
            request.PerformanceFeeRate));

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
                    request.CompanyId),
                ct).ConfigureAwait(false);

        return new AutomatedJournalIntakeRunResult(production.Skipped, intake);
    }
}
