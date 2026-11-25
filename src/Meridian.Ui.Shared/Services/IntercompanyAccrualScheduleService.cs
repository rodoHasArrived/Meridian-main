using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Opt-in, reviewed accrual schedules. A run drafts each completed schedule period that ends after the schedule was
/// approved and has no draft yet, through exactly the preview-then-draft path an operator uses. It never posts, never
/// recomputes a draft that already exists, and reports every refusal instead of estimating.
/// </summary>
public sealed class IntercompanyAccrualScheduleService(IIntercompanyAccrualScheduleStore store, IIntercompanyInterestAccrualDrafter drafter,
    IntercompanyAccountingArrangementService arrangements, IBusinessDayCalendarSource calendars, ILedgerJournalStore journals,
    IManualJournalEntryDraftStore drafts, TimeProvider? clock = null)
{
    /// <summary>One run drafts at most this many periods, oldest first; the next run continues.</summary>
    public const int MaximumWindowsPerRun = 24;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ListAsync(string tenantId, Guid ledgerBookId, CancellationToken ct = default)
        => store.ListAsync(Tenant(tenantId), ledgerBookId, ct);

    public async Task<IntercompanyAccrualScheduleDto> ProposeAsync(string tenantId, string? companyId, string actor,
        ProposeIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = Tenant(tenantId);
        var existing = await store.ListAsync(tenant, request.LedgerBookId, ct).ConfigureAwait(false);
        // A retried proposal returns the schedule it created; the identity reused for another step conflicts.
        if (existing.FirstOrDefault(item => item.Events.Any(step => step.RequestId == request.RequestId)) is { } replay)
        {
            IntercompanyAccrualScheduleRules.IsReplay(replay, request.RequestId, IntercompanyAccrualScheduleActionDto.Propose);
            return replay;
        }
        if (existing.Any(item => item.Kind == request.Kind && item.ProfileArrangementId == request.ProfileArrangementId
            && item.Status is IntercompanyAccrualScheduleStatusDto.Proposed or IntercompanyAccrualScheduleStatusDto.Active))
            throw new InvalidOperationException("This book already has a proposed or active schedule for the profile; disable or reject it first.");
        var next = IntercompanyAccrualScheduleRules.Propose(tenant, companyId, Actor(actor), request, _clock.GetUtcNow());
        return await store.AppendAsync(next, 0, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyAccrualScheduleDto> DecideAsync(string tenantId, string? companyId, string actor, Guid scheduleId,
        DecideIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await RequireAsync(tenantId, companyId, scheduleId, ct).ConfigureAwait(false);
        if (IntercompanyAccrualScheduleRules.IsReplay(current, request.RequestId, request.Action))
            return current;
        var next = IntercompanyAccrualScheduleRules.Decide(current, Actor(actor), request, _clock.GetUtcNow());
        return await store.AppendAsync(next, current.Version, ct).ConfigureAwait(false);
    }

    /// <summary>What a run would do as of <paramref name="asOf"/>, without drafting anything.</summary>
    public async Task<IntercompanyAccrualScheduleRunDto> DueAsync(string tenantId, string? companyId, Guid scheduleId, DateOnly asOf,
        CancellationToken ct = default)
    {
        var schedule = await RequireAsync(tenantId, companyId, scheduleId, ct).ConfigureAwait(false);
        return await EvaluateAsync(schedule, asOf, execute: false, "schedule-preview", ct).ConfigureAwait(false);
    }

    /// <summary>Runs an active schedule now on an operator's request; the drafts record the schedule as their preparer.</summary>
    public async Task<IntercompanyAccrualScheduleRunDto> RunAsync(string tenantId, string? companyId, Guid scheduleId, DateOnly asOf,
        CancellationToken ct = default)
    {
        var schedule = await RequireAsync(tenantId, companyId, scheduleId, ct).ConfigureAwait(false);
        return await RunScheduledAsync(schedule, asOf, ct).ConfigureAwait(false);
    }

    /// <summary>The background runner's entry point: only an active schedule drafts.</summary>
    public Task<IntercompanyAccrualScheduleRunDto> RunScheduledAsync(IntercompanyAccrualScheduleDto schedule, DateOnly asOf, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Status != IntercompanyAccrualScheduleStatusDto.Active)
            throw new InvalidOperationException($"A {schedule.Status} schedule does not draft; only an approved, active schedule runs.");
        return EvaluateAsync(schedule, asOf, execute: true, PreparerFor(schedule), ct);
    }

    /// <summary>The preparer recorded on drafts a schedule creates.</summary>
    public static string PreparerFor(IntercompanyAccrualScheduleDto schedule) => $"accrual-schedule:{schedule.ScheduleId:D}";

    private async Task<IntercompanyAccrualScheduleRunDto> EvaluateAsync(IntercompanyAccrualScheduleDto schedule, DateOnly asOf, bool execute,
        string actor, CancellationToken ct)
    {
        var windows = new List<IntercompanyAccrualScheduleWindowDto>();
        var book = await journals.GetLedgerBookAsync(schedule.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The schedule's ledger book is unavailable.");
        // A proposed schedule shows what would be due if it were approved today.
        var activeFrom = schedule.ActiveFrom ?? asOf;
        IReadOnlyList<(DateOnly From, DateOnly To)> periods;
        try
        { periods = await CompletedPeriodsAsync(schedule, activeFrom, asOf, ct).ConfigureAwait(false); }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return new(schedule.ScheduleId, asOf, execute, [new(activeFrom, asOf > activeFrom ? asOf : activeFrom.AddDays(1),
                IntercompanyAccrualScheduleOutcomeDto.Refused, null, null, error.Message)]);
        }
        foreach (var (from, to) in periods.Take(MaximumWindowsPerRun))
            windows.Add(await WindowAsync(schedule, book, from, to, execute, actor, ct).ConfigureAwait(false));
        return new(schedule.ScheduleId, asOf, execute, windows);
    }

    private async Task<IntercompanyAccrualScheduleWindowDto> WindowAsync(IntercompanyAccrualScheduleDto schedule, LedgerBookRecord book,
        DateOnly from, DateOnly to, bool execute, string actor, CancellationToken ct)
    {
        var request = new IntercompanyAccrualRequestDto(schedule.LedgerBookId, schedule.ProfileArrangementId, schedule.PrincipalArrangementId!.Value, from, to);
        try
        {
            // An existing draft of the window, in any status, is never recomputed or overwritten by a run.
            var firstId = IntercompanyAccrualSourceBuilder.DraftId(schedule.TenantId, request);
            if (await drafts.GetAsync(book.FundProfileId, firstId, ct, schedule.TenantId, schedule.CompanyId).ConfigureAwait(false) is { } existing)
                return new(from, to, IntercompanyAccrualScheduleOutcomeDto.AlreadyDrafted, existing.JournalEntryId, existing.Status.ToString(), null);
            var proposal = await drafter.PreviewAsync(schedule.TenantId, schedule.CompanyId, actor, request, ct).ConfigureAwait(false);
            if (!proposal.IsReady)
                return new(from, to, IntercompanyAccrualScheduleOutcomeDto.Refused, null, null,
                    proposal.Diagnostics.LastOrDefault() ?? proposal.Computation.Diagnostics.LastOrDefault() ?? "The accrual is unavailable.");
            var draftId = proposal.Draft!.JournalEntryId;
            if (draftId != firstId && await drafts.GetAsync(book.FundProfileId, draftId, ct, schedule.TenantId, schedule.CompanyId).ConfigureAwait(false) is { } regenerated)
                return new(from, to, IntercompanyAccrualScheduleOutcomeDto.AlreadyDrafted, regenerated.JournalEntryId, regenerated.Status.ToString(), null);
            if (!execute)
                return new(from, to, IntercompanyAccrualScheduleOutcomeDto.Due, draftId, null, null);
            var created = await drafter.CreateDraftAsync(schedule.TenantId, schedule.CompanyId, actor, new(request, proposal.EvidenceDigest!), ct).ConfigureAwait(false);
            return new(from, to, IntercompanyAccrualScheduleOutcomeDto.Drafted, created.JournalEntryId, created.Status.ToString(), null);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or KeyNotFoundException or UnauthorizedAccessException)
        {
            return new(from, to, IntercompanyAccrualScheduleOutcomeDto.Refused, null, null, error.Message);
        }
    }

    /// <summary>
    /// Completed periods of the loan's declared schedule, read from the profile revision applicable on the day before
    /// <paramref name="asOf"/>, whose last day falls on or after <paramref name="activeFrom"/> and before <paramref name="asOf"/>.
    /// </summary>
    private async Task<IReadOnlyList<(DateOnly From, DateOnly To)>> CompletedPeriodsAsync(IntercompanyAccrualScheduleDto schedule,
        DateOnly activeFrom, DateOnly asOf, CancellationToken ct)
    {
        var day = asOf.AddDays(-1);
        var history = await arrangements.GetHistoryAsync(schedule.TenantId, schedule.ProfileArrangementId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The schedule's accounting profile was not found.");
        var (_, _, revisionId) = IntercompanyAccrualSourceBuilder.ResolveAuthority(history, day, asOf).Single();
        var resolved = await arrangements.ResolveApprovedRevisionAsync(schedule.TenantId, schedule.ProfileArrangementId, revisionId,
            new DateTimeOffset(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
        if (resolved.TemplateKind != IntercompanyAccountingTemplateKindDto.LoanInterest)
            throw new InvalidOperationException("A loan-interest schedule runs only over an approved LoanInterest profile.");
        var terms = resolved.LegalRevision.Terms.Terms?.StructuredTerms?.Schedule
            ?? throw new InvalidOperationException("The profile's legal terms declare no schedule; no accrual period is inferred.");
        if (!calendars.TryGet(terms.CalendarId, out var calendar))
            throw new InvalidOperationException("The declared business-day calendar is unavailable; no fallback was applied.");
        var generated = ScheduleGenerator.Generate(IntercompanyStructuredTermsPreviewService.BuildSpec(terms, calendar));
        if (!generated.Succeeded)
            throw new InvalidOperationException(generated.Failure.Message);
        return generated.Periods
            .Where(period => period.AccrualEnd <= asOf && period.AccrualEnd.AddDays(-1) >= activeFrom)
            .OrderBy(period => period.AccrualStart).Select(period => (period.AccrualStart, period.AccrualEnd)).ToArray();
    }

    private async Task<IntercompanyAccrualScheduleDto> RequireAsync(string tenantId, string? companyId, Guid scheduleId, CancellationToken ct)
    {
        var schedule = await store.GetAsync(Tenant(tenantId), scheduleId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The schedule was not found in this tenant.");
        if (schedule.CompanyId is { } owner && !string.Equals(owner, companyId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The schedule belongs to another company.");
        return schedule;
    }

    private static string Tenant(string tenantId) => string.IsNullOrWhiteSpace(tenantId)
        ? throw new UnauthorizedAccessException("A tenant scope is required.") : tenantId.Trim().ToLowerInvariant();
    private static string Actor(string actor) => string.IsNullOrWhiteSpace(actor)
        ? throw new UnauthorizedAccessException("An authenticated operator is required.") : actor.Trim();
}
