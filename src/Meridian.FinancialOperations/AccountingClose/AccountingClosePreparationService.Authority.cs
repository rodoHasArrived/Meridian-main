using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingClosePreparationService
{
    private sealed record SourceContext(OperationsContinuityWorkflowDto Workflow, ClosePeriodPlanDto Plan,
        LedgerBookDto Book, LedgerAccountingPeriod Period);
    private sealed record TargetContext(LedgerBookDto Book, LedgerAccountingPeriod Period);

    private async Task<SourceContext> ReadSourceAsync(Guid workflowId, string tenantId, string companyId, CancellationToken ct)
    {
        RequireAuthority();
        if (workflowId == Guid.Empty)
            throw new ArgumentException("A source workflow is required.");
        var workflow = await workflows.GetAsync(workflowId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The source workflow was not found.");
        var plan = await closePlans.GetPeriodPlanScopedAsync(workflowId, tenantId, companyId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The source close plan was not found.");
        if (workflow.LedgerBookId is not { } bookId || bookId == Guid.Empty || plan.LedgerBookId != bookId
            || !Same(workflow.PeriodId, plan.PeriodId) || (plan.WorkflowId is { } retainedId && retainedId != workflowId))
            throw new InvalidOperationException("The source close plan lacks an exact authoritative book/workflow scope.");
        var book = await books!.GetBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The source ledger book was not found.");
        if (book.LedgerBookId != bookId || book.FundStructureNodeId != workflow.FundAccountId
            || (plan.FundAccountId is { } accountId && accountId != workflow.FundAccountId))
            throw new InvalidOperationException("The source ledger book does not belong to the workflow's exact account.");
        LedgerAccountingPeriod? period;
        if (Guid.TryParse(workflow.PeriodId, out var periodId))
            period = await journalStore!.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        else
        {
            var periods = await journalStore!.ListPeriodsAsync(ledgerBookId: bookId, ct: ct).ConfigureAwait(false);
            var matches = periods.Where(candidate => candidate.LedgerBookId == bookId && PeriodMatches(workflow.PeriodId, candidate)).ToArray();
            period = matches.Length == 1 ? matches[0] : null;
        }
        if (period is null || period.LedgerBookId != bookId || period.StartDate > period.EndDate)
            throw new InvalidOperationException("The source period cannot be resolved uniquely in its authoritative ledger book.");
        if (plan.Tasks.Count == 0)
            throw new InvalidOperationException("The source plan contains no reusable tasks.");
        var workflowTaskIds = workflow.CloseChecklist.Select(task => task.TaskId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (plan.Tasks.Count != workflowTaskIds.Count || plan.Tasks.Select(task => task.TaskId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Tasks.Count
            || plan.Tasks.Any(task => !workflowTaskIds.Contains(task.TaskId)))
            throw new InvalidOperationException("The source plan's task mappings do not match its authoritative workflow checklist.");
        return new(workflow, plan, book, period);
    }

    private async Task<TargetContext> ReadTargetAsync(ClosePlanTemplateDto template, Guid bookId, Guid periodId, CancellationToken ct)
    {
        RequireAuthority();
        if (bookId == Guid.Empty || periodId == Guid.Empty)
            throw new ArgumentException("Select an authoritative target ledger book and period.");
        var book = await books!.GetBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The target ledger book was not found.");
        var period = await journalStore!.GetPeriodAsync(periodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The target accounting period was not found.");
        if (book.LedgerBookId != bookId || period.PeriodId != periodId || period.LedgerBookId != bookId)
            throw new InvalidOperationException("The authoritative target period does not belong to the selected ledger book.");
        if (!Same(book.FundProfileId, template.FundProfileId))
            throw new InvalidOperationException("The target ledger book does not belong to the source fund profile.");
        if (book.FundStructureNodeId != template.FundAccountId)
            throw new InvalidOperationException("The target ledger book has an unresolved account mapping; select a book for the source account.");
        if (period.StartDate > period.EndDate || string.IsNullOrWhiteSpace(book.AccountingPolicyId)
            || string.IsNullOrWhiteSpace(book.AccountingPolicyVersion))
            throw new InvalidOperationException("The target accounting period or book policy authority is incomplete.");
        return new(book, period);
    }

    private void RequireAuthority()
    {
        if (books is null || journalStore is null)
            throw new InvalidOperationException("Authoritative ledger book and accounting period services are required to prepare a close plan.");
    }

    private static string SourceFingerprint(SourceContext source) => Hash(new
    {
        source.Workflow.WorkflowId,
        source.Workflow.Version,
        source.Workflow.PeriodId,
        source.Workflow.LedgerBookId,
        source.Book,
        source.Period,
        source.Plan.MaterialityPolicy,
        source.Plan.Configuration,
        source.Plan.EvidenceVersion,
        Tasks = source.Plan.Tasks.Select(task => new
        {
            task.TaskId,
            task.DisplayName,
            task.Owner,
            task.DueDate,
            task.Dependencies,
            Requirements = task.SignOffRequirements.Select(requirement => new
            {
                requirement.Role,
                requirement.RequiredApprovalCount,
                requirement.EvidenceRequirement
            }).ToArray()
        }).ToArray()
    });

    private static LedgerPeriodDto ToDto(LedgerBookDto book, LedgerAccountingPeriod period)
        => new(period.PeriodId, book.LedgerBookId, period.FiscalYear, period.PeriodNo, period.Label,
            period.StartDate, period.EndDate, Enum.TryParse<LedgerPeriodStatusDto>(period.Status, true, out var status)
                ? status : throw new InvalidOperationException("The target period has an unknown authoritative status."),
            period.OpenedAt, period.ClosedAt, period.Version, book.AccountingBasis, book.AccountingPolicyId, book.AccountingPolicyVersion);

    private static bool PeriodMatches(string key, LedgerAccountingPeriod period)
        => (Guid.TryParse(key, out var id) && id == period.PeriodId)
            || Same(key, period.Label)
            || (period.StartDate.Day == 1 && period.EndDate == period.StartDate.AddMonths(1).AddDays(-1)
                && Same(key, period.StartDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)));

    private static ClosePlanTemplateDto FindTemplate(PreparationDocument state, Guid id, int version,
        string tenantId, string companyId)
    {
        var entry = state.Templates.SingleOrDefault(entry => entry.Template.TemplateId == id && entry.Template.Version == version)
            ?? throw new InvalidOperationException("The selected template version was not found.");
        RequireScope(entry.TenantId, entry.CompanyId, tenantId, companyId);
        return entry.Template;
    }

    private static void RequireMutation(string actor, string tenantId, string companyId, OperationsActionOriginDto origin)
    {
        _ = Required(actor, "Authenticated actor");
        _ = Required(tenantId, "Authenticated tenant");
        _ = Required(companyId, "Authenticated company");
        if (origin != OperationsActionOriginDto.HumanOperator)
            throw new InvalidOperationException("Preparing a close period requires a human operator.");
    }

    private static void RequireScope(string retainedTenant, string retainedCompany, string tenantId, string companyId)
    {
        if (retainedTenant != tenantId || retainedCompany != companyId)
            throw new InvalidOperationException("The retained preparation does not belong to the authenticated tenant and company.");
    }

    private static string Required(string? text, string label)
        => string.IsNullOrWhiteSpace(text) ? throw new ArgumentException($"{label} is required.") : text.Trim();
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void ValidateGraph(IReadOnlyList<CloseTaskConfigurationDto> tasks)
    {
        var byId = tasks.ToDictionary(task => task.TaskId, StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string taskId)
        {
            if (!byId.TryGetValue(taskId, out var task))
                throw new InvalidOperationException($"The source dependency '{taskId}' is unresolved.");
            if (complete.Contains(taskId))
                return;
            if (!visiting.Add(taskId))
                throw new InvalidOperationException("The source close-plan dependency graph contains a cycle.");
            foreach (var dependency in task.DependencyConfigurations)
                Visit(dependency.DependsOnTaskId);
            visiting.Remove(taskId);
            complete.Add(taskId);
        }
        foreach (var task in tasks)
            Visit(task.TaskId);
    }

    private static void ValidateCalendar(ClosePreparationCalendarDto calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        _ = Required(calendar.CalendarId, "Calendar ID");
        _ = Required(calendar.Version, "Calendar version");
        if (calendar.WeekendDays is null || calendar.Holidays is null || calendar.WeekendDays.Any(day => day is < 0 or > 6)
            || calendar.WeekendDays.Distinct().Count() != calendar.WeekendDays.Count || calendar.WeekendDays.Count == 7
            || calendar.Holidays.Distinct().Count() != calendar.Holidays.Count || calendar.Holidays.Count > 3660)
            throw new ArgumentException("The explicit calendar must have valid unique weekdays, at least one working weekday, and unique holidays.");
    }

    public static DateOnly CalculateDeadline(DateOnly periodStart, DateOnly periodEnd, CloseDeadlineRuleDto rule,
        ClosePreparationCalendarDto calendar)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ValidateCalendar(calendar);
        if (!Enum.IsDefined(rule.Anchor) || !Enum.IsDefined(rule.DayCount) || !Enum.IsDefined(rule.Adjustment)
            || rule.OffsetDays is < -366 or > 366 || periodStart > periodEnd)
            throw new ArgumentException("A valid explicit deadline rule with an offset from -366 to 366 days is required.");
        var due = rule.Anchor == CloseDeadlineAnchorDto.PeriodStart ? periodStart : periodEnd;
        bool IsBusinessDay(DateOnly date) => !calendar.WeekendDays.Contains((int)date.DayOfWeek) && !calendar.Holidays.Contains(date);
        if (rule.DayCount == CloseDeadlineDayCountDto.CalendarDays)
            due = due.AddDays(rule.OffsetDays);
        else
        {
            var remaining = Math.Abs(rule.OffsetDays);
            var direction = Math.Sign(rule.OffsetDays);
            while (remaining > 0)
            {
                due = due.AddDays(direction);
                if (IsBusinessDay(due))
                    remaining--;
            }
        }
        if (rule.Adjustment != CloseDeadlineAdjustmentDto.None)
        {
            var direction = rule.Adjustment == CloseDeadlineAdjustmentDto.FollowingBusinessDay ? 1 : -1;
            while (!IsBusinessDay(due))
                due = due.AddDays(direction);
        }
        return due;
    }
}
