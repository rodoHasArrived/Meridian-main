using System.Text.Json;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// FundStudio-style administration control surface. Drives organization/entity/portfolio/account/book/
/// period/report administration by composing the ledger-domain primitives — multi-book locked periods
/// with evidence-bearing reopen, journal templates, recurring journals, year-end close, and
/// portfolio-specific pricing rules — and recording every privileged action into a tamper-evident,
/// append-only <see cref="FundAdministrationEventLog"/>.
/// </summary>
/// <remarks>
/// State is held in-memory behind a single lock, matching the repository's in-memory service
/// convention. The same <see cref="FundAdministrationEventLog"/> can be shared with the middle-office
/// service so postings, locks, reopens, exports, and deliveries land in one governance chain.
/// </remarks>
public sealed class FundAdministrationControlService
{
    private readonly object _gate = new();
    private readonly FundAdministrationEventLog _eventLog;
    private readonly JournalTemplateBook _templates = new();
    private readonly PortfolioPricingRuleBook _pricingRules = new();
    private readonly LockedAccountingPeriodBook _periods = new();
    private readonly Dictionary<string, RecurringJournalSchedule> _recurringSchedules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OnboardingTemplate> _onboardingTemplates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _postedOccurrences = new(StringComparer.OrdinalIgnoreCase);

    public FundAdministrationControlService(FundAdministrationEventLog? eventLog = null)
    {
        _eventLog = eventLog ?? new FundAdministrationEventLog();
    }

    /// <summary>The append-only, hash-chained governance event log this surface writes to.</summary>
    public FundAdministrationEventLog EventLog => _eventLog;

    // The locked-period, template, and pricing registries are held privately: every mutation must go
    // through this surface's audited methods (which append a governance event), so a caller cannot
    // reopen a period, replace a template, or change a pricing rule without immutable evidence. Read and
    // guard access is exposed here as read-only projections that cannot mutate the registries.

    /// <summary>Returns whether the given accounting period is currently locked for the book.</summary>
    public bool IsPeriodLocked(LedgerBookKey ledgerKey, string periodId) => _periods.IsLocked(ledgerKey, periodId);

    /// <summary>Throws if <paramref name="journalEntry"/> would post into a locked period (read-only guard).</summary>
    public void EnsureCanPost(LedgerBookKey ledgerKey, JournalEntry journalEntry) => _periods.EnsureCanPost(ledgerKey, journalEntry);

    /// <summary>Read-only snapshot of the currently-locked accounting periods.</summary>
    public IReadOnlyList<LockedAccountingPeriod> LockedPeriods => _periods.LockedPeriods;

    /// <summary>Read-only audit trail of every period reopen.</summary>
    public IReadOnlyList<ReopenedAccountingPeriod> PeriodReopenHistory => _periods.ReopenHistory;

    /// <summary>Read-only snapshot of the registered journal templates.</summary>
    public IReadOnlyList<JournalTemplate> RegisteredJournalTemplates => _templates.Templates;

    // ── Journal templates & recurring journals ─────────────────────────────────

    /// <summary>Registers (or replaces) a journal template and records the change.</summary>
    public JournalTemplate RegisterJournalTemplate(JournalTemplate template, string actor)
    {
        ArgumentNullException.ThrowIfNull(template);
        RequireActor(actor);

        lock (_gate)
        {
            _templates.Register(template);
            _eventLog.Append(
                FundAdministrationEventKind.JournalTemplateRegistered,
                actor,
                template.TemplateId,
                $"Registered journal template '{template.Name}' with {template.Lines.Count} line(s).",
                new Dictionary<string, string>
                {
                    ["templateId"] = template.TemplateId,
                    ["lineCount"] = template.Lines.Count.ToString(),
                    ["ledgerBook"] = template.LedgerBook ?? "*",
                    // Capture the full template definition so a same-id replacement that changes accounts,
                    // sides, amounts, factors, dimensions, required parameters, or book scope produces a
                    // distinct (hashed) registration event — recurring schedules resolve the current
                    // template at materialization, so the chain must preserve which version was approved.
                    ["definition"] = DescribeJournalTemplate(template),
                });
        }

        return template;
    }

    private static string DescribeJournalTemplate(JournalTemplate template)
    {
        var lines = string.Join(";", template.Lines.Select(line =>
            $"{line.Account.Name}:{line.Account.AccountType}:{line.Account.FinancialAccountId}" +
            $"|{line.Side}|p={line.AmountParameter}|f={line.FixedAmount}|x={line.Factor}|d={line.Dimensions}|m={line.Memo}"));
        var requiredParameters = string.Join(",", template.RequiredParameters);
        return $"book={template.LedgerBook};params={requiredParameters};lines={lines}";
    }

    /// <summary>Schedules a recurring journal, verifying its template is registered first.</summary>
    public RecurringJournalSchedule ScheduleRecurringJournal(RecurringJournalSchedule schedule, string actor)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        RequireActor(actor);

        lock (_gate)
        {
            if (!_templates.TryGet(schedule.TemplateId, out _))
                throw new InvalidOperationException($"Recurring journal '{schedule.ScheduleId}' references unregistered template '{schedule.TemplateId}'.");

            _recurringSchedules[schedule.ScheduleId] = schedule;
            _eventLog.Append(
                FundAdministrationEventKind.RecurringJournalScheduled,
                actor,
                schedule.ScheduleId,
                $"Scheduled recurring journal from template '{schedule.TemplateId}' ({schedule.Cadence}).",
                new Dictionary<string, string>
                {
                    ["templateId"] = schedule.TemplateId,
                    ["cadence"] = schedule.Cadence.ToString(),
                    ["ledgerBook"] = schedule.LedgerKey.LedgerBook,
                });
        }

        return schedule;
    }

    /// <summary>
    /// Plans a recurring journal's occurrences through <paramref name="throughDate"/>, annotating each
    /// with whether it is currently blocked by a locked period. Read-only; nothing is recorded.
    /// </summary>
    public IReadOnlyList<RecurringJournalOccurrence> PlanRecurringJournals(string scheduleId, DateOnly throughDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            var schedule = ResolveScheduleLocked(scheduleId);
            var template = _templates.Get(schedule.TemplateId);
            var occurrences = RecurringJournalPlanner.PlanThrough(schedule, template, throughDate);
            return RecurringJournalPlanner.ApplyLocks(occurrences, _periods);
        }
    }

    /// <summary>
    /// Returns the recurring journal occurrences due through <paramref name="throughDate"/> that are
    /// not blocked by a locked period and have not yet been confirmed posted. Read-only and idempotent:
    /// it neither consumes occurrences nor records events, so a post that fails or is interrupted can
    /// simply be retried — the occurrence only leaves this list once
    /// <see cref="RecordRecurringJournalPosted"/> confirms it reached the ledger.
    /// </summary>
    /// <remarks>
    /// This is a single-writer planning view: it assumes the caller serializes the plan → post → record
    /// sequence for a given schedule. It does not by itself prevent a duplicate <b>ledger</b> post under
    /// concurrent scheduling — two racing callers could each read the same occurrence and post before
    /// either records it, and <c>Ledger.Post</c> deduplicates on journal-entry id, not idempotency key.
    /// To make the post itself idempotent, post through a target keyed by each occurrence's deterministic
    /// <c>Journal.Metadata.IdempotencyKey</c> (<c>recurring|{scheduleId}|{effectiveDate}</c>): the
    /// planner sets it on every occurrence and the ledger already exposes it on <c>LedgerQuery</c> for a
    /// check-then-post or an idempotent posting path. The <see cref="RecordRecurringJournalPosted"/>
    /// guard is the governance-audit dedup only; it collapses at-least-once posting to a single run event.
    /// </remarks>
    public IReadOnlyList<RecurringJournalOccurrence> DueRecurringJournals(string scheduleId, DateOnly throughDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            var schedule = ResolveScheduleLocked(scheduleId);
            var template = _templates.Get(schedule.TemplateId);
            var occurrences = RecurringJournalPlanner.ApplyLocks(
                RecurringJournalPlanner.PlanThrough(schedule, template, throughDate),
                _periods);

            return occurrences
                .Where(occurrence => !occurrence.BlockedByLock
                                     && !_postedOccurrences.Contains(OccurrenceKey(schedule.ScheduleId, occurrence.EffectiveDate)))
                .ToArray();
        }
    }

    /// <summary>
    /// Records that a recurring journal occurrence has actually posted to the ledger, appending the run
    /// event and removing the occurrence from <see cref="DueRecurringJournals"/>. Recording the same
    /// occurrence again is a no-op (returns <see langword="null"/>), so at-least-once posting collapses
    /// to a single governance record. This dedups the audit event, not the ledger post itself: under
    /// concurrent scheduling, guard the post with the occurrence's deterministic
    /// <c>Metadata.IdempotencyKey</c> as described on <see cref="DueRecurringJournals"/>.
    /// </summary>
    public FundAdministrationEvent? RecordRecurringJournalPosted(string scheduleId, DateOnly effectiveDate, string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        RequireActor(actor);

        lock (_gate)
        {
            var schedule = ResolveScheduleLocked(scheduleId);
            if (!_postedOccurrences.Add(OccurrenceKey(schedule.ScheduleId, effectiveDate)))
                return null;

            return _eventLog.Append(
                FundAdministrationEventKind.RecurringJournalRun,
                actor,
                schedule.ScheduleId,
                $"Posted recurring journal for {effectiveDate:yyyy-MM-dd}.",
                new Dictionary<string, string>
                {
                    ["templateId"] = schedule.TemplateId,
                    ["effectiveDate"] = effectiveDate.ToString("yyyy-MM-dd"),
                    ["ledgerBook"] = schedule.LedgerKey.LedgerBook,
                },
                occurredAtUtc: new DateTimeOffset(effectiveDate.ToDateTime(schedule.PostingTime), TimeSpan.Zero));
        }
    }

    private static string OccurrenceKey(string scheduleId, DateOnly effectiveDate)
        => $"{scheduleId}|{effectiveDate:yyyy-MM-dd}";

    // ── Locked periods & evidence-bearing reopen ───────────────────────────────

    /// <summary>Locks an accounting period and records the lock.</summary>
    public LockedAccountingPeriod LockPeriod(
        LedgerBookKey ledgerKey,
        string periodId,
        DateTimeOffset startsAtInclusive,
        DateTimeOffset endsAtInclusive,
        DateTimeOffset lockedAtUtc,
        string actor,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(ledgerKey);
        RequireActor(actor);

        lock (_gate)
        {
            var locked = _periods.LockPeriod(ledgerKey, periodId, startsAtInclusive, endsAtInclusive, lockedAtUtc, actor, reason);
            _eventLog.Append(
                FundAdministrationEventKind.PeriodLocked,
                actor,
                $"{locked.LedgerKey.LedgerBook}:{locked.PeriodId}",
                $"Locked accounting period '{locked.PeriodId}' for book '{locked.LedgerKey.LedgerBook}'.",
                new Dictionary<string, string>
                {
                    ["periodId"] = locked.PeriodId,
                    ["ledgerBook"] = locked.LedgerKey.LedgerBook,
                    ["reason"] = locked.Reason,
                },
                occurredAtUtc: locked.LockedAtUtc);
            return locked;
        }
    }

    /// <summary>Reopens a locked period with supporting evidence and records the reopen and its evidence.</summary>
    public ReopenedAccountingPeriod ReopenPeriod(
        LedgerBookKey ledgerKey,
        string periodId,
        DateTimeOffset reopenedAtUtc,
        string actor,
        PeriodReopenEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(ledgerKey);
        ArgumentNullException.ThrowIfNull(evidence);
        RequireActor(actor);

        lock (_gate)
        {
            var reopened = _periods.ReopenPeriod(ledgerKey, periodId, reopenedAtUtc, actor, evidence);
            _eventLog.Append(
                FundAdministrationEventKind.PeriodReopened,
                actor,
                $"{reopened.Period.LedgerKey.LedgerBook}:{reopened.Period.PeriodId}",
                $"Reopened period '{reopened.Period.PeriodId}': {evidence.Reason}",
                new Dictionary<string, string>
                {
                    ["periodId"] = reopened.Period.PeriodId,
                    ["ledgerBook"] = reopened.Period.LedgerKey.LedgerBook,
                    ["reopenId"] = evidence.ReopenId,
                    ["approvedBy"] = evidence.ApprovedBy,
                },
                evidence: evidence.EvidenceReferences,
                occurredAtUtc: reopened.ReopenedAtUtc);
            return reopened;
        }
    }

    // ── Year-end close ─────────────────────────────────────────────────────────

    /// <summary>
    /// Projects fiscal-year-end closing entries and the retained-earnings roll-forward. This is a
    /// preview only and records nothing: it has no ledger or posting target, so it cannot commit the
    /// close. Post the projection's closing journals through the ledger, then call
    /// <see cref="RecordYearEndClosed"/> to record the completed close in the governance log.
    /// </summary>
    public YearEndCloseProjection ProjectYearEndClose(YearEndCloseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return YearEndCloseProjector.Project(input);
    }

    /// <summary>
    /// Records that a fiscal year's closing journals have actually posted, appending a
    /// <see cref="FundAdministrationEventKind.YearEndClosed"/> event. Call this only after the governed
    /// closing entries have committed to the ledger, so the immutable trail never marks an unposted year
    /// as closed.
    /// </summary>
    public FundAdministrationEvent RecordYearEndClosed(
        string fiscalYearLabel,
        decimal netIncome,
        DateTimeOffset occurredAtUtc,
        string actor,
        IReadOnlyList<JournalEvidenceReference>? evidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fiscalYearLabel);
        RequireActor(actor);

        var label = fiscalYearLabel.Trim();
        lock (_gate)
        {
            return _eventLog.Append(
                FundAdministrationEventKind.YearEndClosed,
                actor,
                label,
                $"Year-end close recorded for {label} (net income {netIncome}).",
                new Dictionary<string, string>
                {
                    ["fiscalYear"] = label,
                    ["netIncome"] = netIncome.ToString(),
                },
                evidence: evidence,
                occurredAtUtc: occurredAtUtc);
        }
    }

    // ── Portfolio-specific pricing rules ───────────────────────────────────────

    /// <summary>Adds or replaces a portfolio pricing rule and records the change.</summary>
    public PortfolioPricingRule SetPortfolioPricingRule(PortfolioPricingRule rule, string actor)
    {
        ArgumentNullException.ThrowIfNull(rule);
        RequireActor(actor);

        lock (_gate)
        {
            _pricingRules.Add(rule);
            _eventLog.Append(
                FundAdministrationEventKind.PricingRuleChanged,
                actor,
                rule.PortfolioId,
                $"Set pricing rule '{rule.RuleId}' for portfolio '{rule.PortfolioId}' ({rule.PriceSource}/{rule.ValuationMethod}).",
                new Dictionary<string, string>
                {
                    ["ruleId"] = rule.RuleId,
                    ["portfolioId"] = rule.PortfolioId,
                    ["priceSource"] = rule.PriceSource,
                    ["valuationMethod"] = rule.ValuationMethod,
                    ["instrumentType"] = rule.InstrumentType ?? "*",
                    // Fields that drive resolution (RulesFor orders by priority then approval time; Matches
                    // applies the effective window and fair-value level), so a replacement that changes only
                    // these is still a distinct, reconstructable audit event.
                    ["priority"] = rule.Priority.ToString(),
                    ["approvedAtUtc"] = rule.ApprovedAtUtc.ToString("O"),
                    ["effectiveFrom"] = rule.EffectiveFrom?.ToString("yyyy-MM-dd") ?? "*",
                    ["effectiveTo"] = rule.EffectiveTo?.ToString("yyyy-MM-dd") ?? "*",
                    ["fairValueLevel"] = rule.FairValueLevel.ToString(),
                });
            return rule;
        }
    }

    /// <summary>Resolves the effective pricing rule for a portfolio, instrument, and date (read-only).</summary>
    public PortfolioPricingRule? ResolvePortfolioPricing(string portfolioId, string? instrumentType, DateOnly asOf)
        => _pricingRules.Resolve(portfolioId, instrumentType, asOf);

    // ── Onboarding templates ───────────────────────────────────────────────────

    /// <summary>
    /// Registers (or replaces) a reusable onboarding template, recording the change with a
    /// content-identifying governance event so the exact hierarchy/codes/parents approved at
    /// registration are auditable (a later <see cref="FundAdministrationEventKind.OnboardingApplied"/>
    /// event only carries the template id and node count).
    /// </summary>
    public OnboardingTemplate RegisterOnboardingTemplate(OnboardingTemplate template, string actor)
    {
        ArgumentNullException.ThrowIfNull(template);
        RequireActor(actor);

        lock (_gate)
        {
            _onboardingTemplates[template.TemplateId] = template;
            _eventLog.Append(
                FundAdministrationEventKind.OnboardingTemplateRegistered,
                actor,
                template.TemplateId,
                $"Registered onboarding template '{template.Name}' with {template.Nodes.Count} node(s).",
                new Dictionary<string, string>
                {
                    ["templateId"] = template.TemplateId,
                    ["name"] = template.Name,
                    ["nodeCount"] = template.Nodes.Count.ToString(),
                    ["nodes"] = DescribeOnboardingNodes(template),
                });
        }

        return template;
    }

    // Captures the approved template shape in a complete, deterministic JSON representation. JSON
    // preserves field boundaries even when values contain delimiters, while the sorted attributes
    // make the descriptor stable for logically identical attribute maps.
    private static string DescribeOnboardingNodes(OnboardingTemplate template)
        => JsonSerializer.Serialize(template.Nodes.Select(node => new
        {
            node.Key,
            node.NodeType,
            node.CodeTemplate,
            node.NameTemplate,
            node.ParentKey,
            node.BaseCurrency,
            Attributes = node.Attributes
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new { pair.Key, pair.Value }),
        }));

    /// <summary>
    /// Applies a registered onboarding template, producing a concrete structure plan and recording the
    /// application. The plan's nodes are then created through the fund-structure services by the caller.
    /// </summary>
    public OnboardingPlan ApplyOnboardingTemplate(string templateId, IReadOnlyDictionary<string, string> parameters, string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(parameters);
        RequireActor(actor);

        lock (_gate)
        {
            if (!_onboardingTemplates.TryGetValue(templateId.Trim(), out var template))
                throw new KeyNotFoundException($"Onboarding template '{templateId}' is not registered.");

            var plan = template.Apply(parameters);
            _eventLog.Append(
                FundAdministrationEventKind.OnboardingApplied,
                actor,
                template.TemplateId,
                $"Applied onboarding template '{template.Name}' producing {plan.Nodes.Count} node(s).",
                new Dictionary<string, string>
                {
                    ["templateId"] = template.TemplateId,
                    ["nodeCount"] = plan.Nodes.Count.ToString(),
                });
            return plan;
        }
    }

    // ── Posting & report-export governance records ─────────────────────────────

    /// <summary>Records that a journal was posted to a book, for the immutable posting trail.</summary>
    public FundAdministrationEvent RecordJournalPosted(string ledgerBook, JournalEntry entry, string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerBook);
        ArgumentNullException.ThrowIfNull(entry);
        RequireActor(actor);

        var debits = entry.Lines.Sum(static line => line.Debit);
        lock (_gate)
        {
            return _eventLog.Append(
                FundAdministrationEventKind.JournalPosted,
                actor,
                $"{ledgerBook.Trim()}:{entry.JournalEntryId:N}",
                $"Posted journal '{entry.Description}' (debits {debits}).",
                new Dictionary<string, string>
                {
                    ["ledgerBook"] = ledgerBook.Trim(),
                    ["journalEntryId"] = entry.JournalEntryId.ToString(),
                    ["amount"] = debits.ToString(),
                },
                occurredAtUtc: entry.Timestamp);
        }
    }

    /// <summary>Records that a governed report pack was exported, for the immutable export trail.</summary>
    public FundAdministrationEvent RecordReportExport(LedgerReportScheduledExport export, string actor, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(export);
        RequireActor(actor);

        lock (_gate)
        {
            return _eventLog.Append(
                FundAdministrationEventKind.ReportExported,
                actor,
                export.ReportId,
                $"Exported report '{export.ReportId}' for period '{export.PeriodId}' to {export.Recipients.Count} recipient(s).",
                new Dictionary<string, string>
                {
                    ["reportId"] = export.ReportId,
                    ["periodId"] = export.PeriodId,
                    ["fundId"] = export.Schedule.FundId,
                    ["recipientCount"] = export.Recipients.Count.ToString(),
                    ["dueAtUtc"] = export.DueAtUtc.ToString("O"),
                },
                occurredAtUtc: occurredAtUtc);
        }
    }

    // ── Read snapshots ─────────────────────────────────────────────────────────

    /// <summary>All registered recurring journal schedules.</summary>
    public IReadOnlyList<RecurringJournalSchedule> RecurringSchedules
    {
        get
        {
            lock (_gate)
            {
                return _recurringSchedules.Values
                    .OrderBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <summary>All registered onboarding templates.</summary>
    public IReadOnlyList<OnboardingTemplate> OnboardingTemplates
    {
        get
        {
            lock (_gate)
            {
                return _onboardingTemplates.Values
                    .OrderBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    private RecurringJournalSchedule ResolveScheduleLocked(string scheduleId)
    {
        if (!_recurringSchedules.TryGetValue(scheduleId.Trim(), out var schedule))
            throw new KeyNotFoundException($"Recurring journal schedule '{scheduleId}' is not registered.");

        return schedule;
    }

    private static void RequireActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("A governance action must record an actor.", nameof(actor));
    }
}
