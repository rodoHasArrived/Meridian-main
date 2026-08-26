using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using static Meridian.Contracts.Text.TextPrimitives;
using Meridian.Contracts.Integrity;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Applies one governed operator decision to every retained draft in the current daily-valuation
/// batch. Batch membership is read from the server-owned schedule, validation runs for every
/// member before posting begins, and a retry resumes already submitted/approved/posted members.
/// </summary>
public sealed class DailyValuationBatchLifecycleService
{
    private readonly IDailyValuationPortfolioSource _scheduleSource;
    private readonly IManualJournalEntryDraftStore _draftStore;
    private readonly IManualJournalEntryLifecycleService _lifecycle;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DailyValuationBatchLifecycleService(
        IDailyValuationPortfolioSource scheduleSource,
        IManualJournalEntryDraftStore draftStore,
        IManualJournalEntryLifecycleService lifecycle)
    {
        _scheduleSource = scheduleSource ?? throw new ArgumentNullException(nameof(scheduleSource));
        _draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public async Task<DailyValuationBatchLifecycleResultDto> ApproveAndPostAsync(
        DailyValuationBatchLifecycleRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scheduleId = RequireText(request.ScheduleId, nameof(request.ScheduleId));
        var fundProfileId = RequireText(request.FundProfileId, nameof(request.FundProfileId));
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var notes = RequireText(request.Notes, nameof(request.Notes));
        var actionOrigin = request.ActionOrigin;
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var schedule = await _scheduleSource.GetAsync(scheduleId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Daily valuation schedule '{scheduleId}' was not found.");
            EnsureOwnedScope(schedule, tenantId, companyId);
            if (!string.Equals(schedule.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Daily valuation schedule '{scheduleId}' does not belong to fund profile '{fundProfileId}'.");
            }

            var memberIds = schedule.JournalEntryIds
                .Where(static id => id != Guid.Empty)
                .Distinct()
                .OrderBy(static id => id)
                .ToArray();
            if (memberIds.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Daily valuation schedule '{scheduleId}' has no retained draft batch to approve.");
            }
            var batchCorrelationId = string.IsNullOrWhiteSpace(schedule.BatchCorrelationId)
                ? BuildRecoveredBatchCorrelationId(schedule, memberIds)
                : schedule.BatchCorrelationId.Trim();
            var lifecycleEvidence = schedule.EvidenceLinks
                .Select(static link => link.Route)
                .Concat(request.EvidenceLinks)
                .Where(static link => !string.IsNullOrWhiteSpace(link))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var drafts = new List<ManualJournalEntryDraftDto>(memberIds.Length);
            var blockers = new List<string>();
            foreach (var journalEntryId in memberIds)
            {
                var draft = await _draftStore
                    .GetAsync(fundProfileId, journalEntryId, ct, tenantId, companyId)
                    .ConfigureAwait(false);
                if (draft is null)
                {
                    blockers.Add($"Daily valuation draft '{journalEntryId:D}' is missing from the governed workbench.");
                    continue;
                }

                if (draft.LedgerBookId != schedule.LedgerBookId ||
                    !IsDailyValuationDraft(draft) ||
                    !string.Equals(draft.EntityId, schedule.EntityId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(draft.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(draft.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add($"Draft '{journalEntryId:D}' does not match the retained daily-valuation batch scope.");
                    continue;
                }

                if (string.Equals(draft.PreparedBy, actor, StringComparison.OrdinalIgnoreCase) &&
                    draft.Status is not (ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked))
                {
                    blockers.Add($"Draft '{journalEntryId:D}' requires an approver independent from preparer '{draft.PreparedBy}'.");
                    continue;
                }

                if (draft.Status is ManualJournalEntryStatusDto.NeedsFix or ManualJournalEntryStatusDto.Rejected)
                {
                    blockers.Add($"Draft '{journalEntryId:D}' is {draft.Status} and must be repaired before batch approval.");
                    continue;
                }

                drafts.Add(draft);
            }

            if (blockers.Count == 0)
            {
                // Validate every mutable member before any member can post. This catches stale
                // Security Master, chart, period, and evidence controls without partially posting
                // an otherwise invalid batch.
                for (var index = 0; index < drafts.Count; index++)
                {
                    var draft = drafts[index];
                    if (draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked)
                    {
                        continue;
                    }

                    try
                    {
                        var validation = await ApplyAsync(
                            draft,
                            JournalEntryLifecycleActionDto.Validate,
                            actor,
                            notes,
                            batchCorrelationId,
                            actionOrigin,
                            lifecycleEvidence,
                            ct).ConfigureAwait(false);
                        drafts[index] = validation.JournalEntry;
                        if (validation.JournalEntry.Status == ManualJournalEntryStatusDto.NeedsFix ||
                            validation.JournalEntry.ValidationIssues.Any(static issue =>
                                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
                        {
                            blockers.Add($"Draft '{draft.JournalEntryId:D}' has critical validation issues and was not posted.");
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        blockers.Add($"Draft '{draft.JournalEntryId:D}' validation failed: {ex.Message}");
                    }
                }
            }

            if (blockers.Count == 0)
            {
                for (var index = 0; index < drafts.Count; index++)
                {
                    try
                    {
                        drafts[index] = await AdvanceToPostedAsync(
                            drafts[index],
                            actor,
                            notes,
                            batchCorrelationId,
                            actionOrigin,
                            lifecycleEvidence,
                            ct).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException ex)
                    {
                        blockers.Add($"Draft '{drafts[index].JournalEntryId:D}' could not complete batch posting: {ex.Message}");
                        break;
                    }
                }
            }

            var postedIds = new List<Guid>(memberIds.Length);
            foreach (var journalEntryId in memberIds)
            {
                var current = await _draftStore
                    .GetAsync(fundProfileId, journalEntryId, ct, tenantId, companyId)
                    .ConfigureAwait(false);
                if (current?.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked)
                {
                    postedIds.Add(journalEntryId);
                }
            }

            var isComplete = blockers.Count == 0 && postedIds.Count == memberIds.Length;
            if (!isComplete && blockers.Count == 0)
            {
                blockers.Add("Not every retained daily valuation draft reached Posted state.");
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var batchEvidence = new OperationsEvidenceLinkDto(
                $"daily-valuation-batch:{schedule.ScheduleId}:{batchCorrelationId}",
                isComplete ? "Daily valuation batch approval and posting" : "Daily valuation batch posting exception",
                BuildLifecycleEvidenceRoute(
                    isComplete ? "posting" : "review",
                    schedule,
                    memberIds[0],
                    tenantId,
                    companyId),
                "manual-journal-workbench",
                nowUtc);
            await _scheduleSource.SaveAsync(schedule with
            {
                State = isComplete ? DailyValuationScheduleStateDto.Posted : DailyValuationScheduleStateDto.Blocked,
                LastSummary = isComplete
                    ? $"Daily valuation batch '{batchCorrelationId}' posted all {postedIds.Count} governed draft(s)."
                    : $"Daily valuation batch '{batchCorrelationId}' is partially complete ({postedIds.Count}/{memberIds.Length} posted).",
                EvidenceLinks = schedule.EvidenceLinks
                    .Append(batchEvidence)
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Blockers = blockers.ToArray(),
                JournalEntryId = memberIds[0],
                JournalEntryIds = memberIds,
                BatchCorrelationId = batchCorrelationId
            }, ct).ConfigureAwait(false);

            return new DailyValuationBatchLifecycleResultDto(
                schedule.ScheduleId,
                batchCorrelationId,
                isComplete,
                memberIds,
                postedIds,
                blockers);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ManualJournalEntryDraftDto> AdvanceToPostedAsync(
        ManualJournalEntryDraftDto draft,
        string actor,
        string notes,
        string batchCorrelationId,
        OperationsActionOriginDto actionOrigin,
        IReadOnlyList<string> callerEvidence,
        CancellationToken ct)
    {
        var current = draft;
        if (current.Status == ManualJournalEntryStatusDto.Draft)
        {
            current = (await ApplyAsync(
                current,
                JournalEntryLifecycleActionDto.Submit,
                actor,
                notes,
                batchCorrelationId,
                actionOrigin,
                callerEvidence,
                ct).ConfigureAwait(false)).JournalEntry;
        }

        if (current.Status == ManualJournalEntryStatusDto.Submitted)
        {
            current = (await ApplyAsync(
                current,
                JournalEntryLifecycleActionDto.Approve,
                actor,
                notes,
                batchCorrelationId,
                actionOrigin,
                callerEvidence,
                ct).ConfigureAwait(false)).JournalEntry;
        }

        if (current.Status == ManualJournalEntryStatusDto.Approved)
        {
            current = (await ApplyAsync(
                current,
                JournalEntryLifecycleActionDto.Post,
                actor,
                notes,
                batchCorrelationId,
                actionOrigin,
                callerEvidence,
                ct).ConfigureAwait(false)).JournalEntry;
        }

        if (current.Status is not (ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked))
        {
            throw new InvalidOperationException(
                $"Draft remained {current.Status} after the governed batch lifecycle command.");
        }

        return current;
    }

    private async Task<JournalEntryLifecycleActionResultDto> ApplyAsync(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action,
        string actor,
        string notes,
        string batchCorrelationId,
        OperationsActionOriginDto actionOrigin,
        IReadOnlyList<string> callerEvidence,
        CancellationToken ct)
    {
        var evidence = callerEvidence
            .Append(BuildLifecycleEvidenceRoute(
                ActionEvidenceToken(action),
                draft,
                draft.JournalEntryId,
                draft.TenantId,
                draft.CompanyId))
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return await _lifecycle.ApplyLifecycleActionAsync(
            new JournalEntryLifecycleActionRequestDto(
                draft.JournalEntryId,
                draft.FundProfileId,
                action,
                actor,
                draft.Version,
                Notes: $"{notes} Daily valuation batch {batchCorrelationId}.",
                CorrelationId: BuildActionCorrelationId(batchCorrelationId, draft.JournalEntryId, action),
                EvidenceLinks: evidence,
                // Propagated rather than left to default: JournalEntryLifecycleActionRequestDto
                // defaults ActionOrigin to HumanOperator, so omitting it here handed every batch
                // -- including one driven by a service credential -- the human standing that
                // ManualJournalEntryWorkbenchService's EnsureHumanOrigin gate checks for (#2673).
                ActionOrigin: actionOrigin,
                LedgerBookId: draft.LedgerBookId,
                TenantId: draft.TenantId,
                CompanyId: draft.CompanyId),
            ct).ConfigureAwait(false);
    }

    private static string BuildLifecycleEvidenceRoute(
        string action,
        DailyValuationScheduleWorkItem schedule,
        Guid journalEntryId,
        string? tenantId,
        string? companyId)
        => BuildLifecycleEvidenceRoute(
            action,
            schedule.LedgerBookId,
            schedule.PeriodId.ToString("D"),
            journalEntryId,
            tenantId,
            companyId);

    private static string BuildLifecycleEvidenceRoute(
        string action,
        ManualJournalEntryDraftDto draft,
        Guid journalEntryId,
        string? tenantId,
        string? companyId)
        => BuildLifecycleEvidenceRoute(
            action,
            // Not coerced to Guid.Empty. Every other component that meets a ledger book treats an
            // all-zeros id as invalid and refuses it -- AccountingPostingCommandValidator,
            // TradeFillLedgerPostingTarget, DailyMarkToMarketService, ShadowBookValuationService and
            // the wash-sale query among them -- so substituting one here stamped an evidence route
            // with a scope no reader would accept (ACCT-CHECKLIST-01).
            //
            // Unreachable in practice: ApproveAndPostAsync blocks any draft whose LedgerBookId does
            // not equal the schedule's non-nullable book before a draft reaches this path. Requiring
            // it states that invariant instead of silently standing in for it, and both callers of
            // this overload already convert InvalidOperationException into a visible batch blocker,
            // so an unscoped draft would surface rather than post under a fabricated scope.
            draft.LedgerBookId ?? throw new InvalidOperationException(
                $"Daily valuation draft '{journalEntryId:D}' reached evidence-route construction with no "
                + "ledger book; the batch scope check should have blocked it."),
            draft.PeriodId,
            journalEntryId,
            tenantId,
            companyId);

    private static string BuildLifecycleEvidenceRoute(
        string action,
        Guid ledgerBookId,
        string? periodId,
        Guid journalEntryId,
        string? tenantId,
        string? companyId)
    {
        var route = $"/api/workstation/evidence/subjects/accounting-record/{action}/ledger-book/{ledgerBookId:D}/{Uri.EscapeDataString(periodId ?? "unknown")}" +
                    $"?journalEntryId={journalEntryId:D}";
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            route += $"&tenantId={Uri.EscapeDataString(tenantId)}";
        }

        if (!string.IsNullOrWhiteSpace(companyId))
        {
            route += $"&companyId={Uri.EscapeDataString(companyId)}";
        }

        return route;
    }

    private static string BuildActionCorrelationId(
        string batchCorrelationId,
        Guid journalEntryId,
        JournalEntryLifecycleActionDto action)
    {
        var seed = System.Text.Encoding.UTF8.GetBytes(
            $"daily-valuation-lifecycle|{batchCorrelationId}|{journalEntryId:N}|{action}");
        return new Guid(Sha256Digest.ComputeBytes(seed).AsSpan(0, 16)).ToString("D");
    }

    private static string BuildRecoveredBatchCorrelationId(
        DailyValuationScheduleWorkItem schedule,
        IReadOnlyList<Guid> journalEntryIds)
    {
        var seed = System.Text.Encoding.UTF8.GetBytes(
            $"daily-valuation-recovered-lifecycle|{schedule.ScheduleId.Trim().ToLowerInvariant()}|{string.Join('|', journalEntryIds.Order())}");
        return new Guid(Sha256Digest.ComputeBytes(seed).AsSpan(0, 16)).ToString("D");
    }

    private static string ActionEvidenceToken(JournalEntryLifecycleActionDto action)
        => action switch
        {
            JournalEntryLifecycleActionDto.Approve => "approval",
            JournalEntryLifecycleActionDto.Post => "posting",
            JournalEntryLifecycleActionDto.Submit or JournalEntryLifecycleActionDto.Validate => "review",
            _ => action.ToString().ToLowerInvariant()
        };

    private static bool IsDailyValuationDraft(ManualJournalEntryDraftDto draft)
        => draft.TreasuryContext?.IdempotencyKey?.StartsWith(
            "fair-value|",
            StringComparison.OrdinalIgnoreCase) == true;

    private static void EnsureOwnedScope(
        DailyValuationScheduleWorkItem schedule,
        string? tenantId,
        string? companyId)
    {
        if (!string.Equals(schedule.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(schedule.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Daily valuation schedule '{schedule.ScheduleId}' is owned by another tenant/company scope.");
        }
    }

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();
}
