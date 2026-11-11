using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.FundAdministration;

namespace Meridian.Ui.Shared.Services;

public sealed partial class AutomatedJournalIntakeRunner
{
    // Two overlapping declarations would both read the same earlier tiers before either is
    // admitted. Declarations are rare, so one at a time per host is enough.
    private static readonly SemaphoreSlim DistributionDeclarationGate = new(1, 1);

    /// <summary>
    /// Admits default-interest drafts for the unfunded part of a posted capital call into the
    /// approval queue. Nothing is posted; a blocked run admits nothing.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunCapitalCallDefaultInterestIntakeAsync(
        RunCapitalCallDefaultInterestDraftIntakeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CallId);
        const string lane = "Capital-call default interest";
        var code = CapitalCallDefaultInterestDraftProducer.AssessmentCode;
        var runKey = CapitalCallDefaultInterestDraftProducer.BuildRunAssessmentKey(request.FundProfileId, request.CallId);
        var evaluatedAtUtc = (request.AsOf ?? _timeProvider.GetUtcNow()).ToUniversalTime();
        var (workbench, blocker) = await LoadPrivateCapitalWorkbenchAsync(request.FundProfileId, request.LedgerBookId,
            request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        if (workbench is null)
            return BuildBlockedCapitalCallResult(runKey, code, lane, blocker ?? "The posted private-capital activity is unavailable.");
        var (drafts, activity) = workbench.Value;

        var production = CapitalCallDefaultInterestDraftProducer.Produce(request, activity.FundEvents, activity.LedgerImpacts,
            drafts, evaluatedAtUtc);
        return await AdmitPrivateCapitalEventDraftsAsync(production, runKey, code, lane, request.FundProfileId, request.Currency,
            request.Actor, request.LedgerBookId, request.PeriodId, request.EntityId, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Admits one distribution draft per investor into the approval queue and retains each
    /// declaration's waterfall tiers so the investor's next distribution continues from them.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunDistributionDeclarationIntakeAsync(
        RunDistributionDeclarationDraftIntakeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DistributionId);
        const string lane = "Distribution";
        var code = DistributionDeclarationDraftProducer.AssessmentCode;
        var runKey = DistributionDeclarationDraftProducer.BuildRunAssessmentKey(request.FundProfileId, request.DistributionId);
        var evaluatedAtUtc = (request.AsOf ?? _timeProvider.GetUtcNow()).ToUniversalTime();
        if (_distributionRegister is null)
            return BuildBlockedCapitalCallResult(runKey, code, lane, "The distribution register is unavailable, so earlier waterfall tiers cannot be read.");
        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.CompanyId))
            return BuildBlockedCapitalCallResult(runKey, code, lane, "A distribution needs its tenant and company scope.");
        await DistributionDeclarationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (workbench, blocker) = await LoadPrivateCapitalWorkbenchAsync(request.FundProfileId, request.LedgerBookId,
                request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
            if (workbench is null)
                return BuildBlockedCapitalCallResult(runKey, code, lane, blocker ?? "The journal queue is unavailable.");

            var registered = new Dictionary<string, IReadOnlyList<DistributionDeclarationRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var investorId in (request.Investors ?? []).Where(static line => line is not null && !string.IsNullOrWhiteSpace(line.InvestorId))
                         .Select(static line => line.InvestorId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                registered[investorId] = request.LedgerBookId is { } book
                    ? await _distributionRegister.ListAsync(request.TenantId, request.CompanyId, request.FundProfileId, book, investorId, ct).ConfigureAwait(false)
                    : [];
            }

            var (production, records) = DistributionDeclarationDraftProducer.Produce(request, registered, workbench.Value.Drafts, evaluatedAtUtc);
            var result = await AdmitPrivateCapitalEventDraftsAsync(production, runKey, code, lane, request.FundProfileId, request.Currency,
                request.Actor, request.LedgerBookId, request.PeriodId, request.EntityId, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
            if (result.Readiness != AutomatedJournalIntakeReadiness.Ready)
                return result;

            // Retain each declaration against the draft that carries it. A retry after an interruption
            // finds the same draft and records the same declaration.
            var draftIdByKey = result.Intake.Created
                .Where(static draft => draft.TreasuryContext?.IdempotencyKey is not null)
                .Select(static draft => (Key: draft.TreasuryContext!.IdempotencyKey!, draft.JournalEntryId))
                .Concat(result.Intake.Skipped.Select(static skip => (Key: skip.IdempotencyKey, skip.JournalEntryId)))
                .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First().JournalEntryId, StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                var key = DistributionDeclarationDraftProducer.DraftKey(record.FundProfileId, record.InvestorId, record.DistributionId);
                if (!draftIdByKey.TryGetValue(key, out var journalEntryId))
                    throw new InvalidOperationException($"Distribution '{record.DistributionId}' for investor '{record.InvestorId}' produced no retained draft.");
                // The draft is already admitted; a disconnecting caller must not stop its record.
                await _distributionRegister.RecordAsync(record with { JournalEntryId = journalEntryId }, CancellationToken.None).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            DistributionDeclarationGate.Release();
        }
    }

    private async Task<((IReadOnlyList<ManualJournalEntryDraftDto> Drafts, PrivateCapitalActivityProjectionDto Activity)? Workbench, string? Blocker)> LoadPrivateCapitalWorkbenchAsync(
        string fundProfileId, Guid? ledgerBookId, string? tenantId, string? companyId, CancellationToken ct)
    {
        if (_manualJournalWorkbench is null)
            return (null, "The journal queue and posted private-capital activity source is unavailable.");
        try
        {
            var queue = await _manualJournalWorkbench.GetWorkbenchAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
            var activity = await _manualJournalWorkbench.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
            return ((queue.Drafts, activity), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            return (null, $"The journal queue and posted private-capital activity could not be read: {ex.Message}");
        }
    }

    private async Task<AutomatedJournalIntakeRunResult> AdmitPrivateCapitalEventDraftsAsync(
        PrivateCapitalEventProduction production, string runKey, string assessmentCode, string lane,
        string fundProfileId, string currency, string actor, Guid? ledgerBookId, string? periodId, string? entityId,
        string? tenantId, string? companyId, CancellationToken ct)
    {
        if (production.Blockers.Count > 0)
        {
            var blocked = BuildBlockedCapitalCallResult(runKey, assessmentCode, lane, string.Join(" ", production.Blockers));
            return blocked with { ProducerSkips = production.Skipped, ReadinessBlockers = production.Blockers };
        }

        if (production.Drafts.Count == 0)
            return new AutomatedJournalIntakeRunResult(production.Skipped, EmptyIntake, production.EvidenceAssessments,
                AutomatedJournalIntakeReadiness.Ready, []);

        var intake = await _intake.IntakeDraftsAsync(new AutomatedJournalPreparedDraftIntakeRequest(
            fundProfileId, currency, production.Drafts, actor, ledgerBookId, periodId, entityId, tenantId, companyId,
            production.EvidenceAssessments, BatchCorrelationId: runKey), ct).ConfigureAwait(false);
        return new AutomatedJournalIntakeRunResult(production.Skipped, intake, production.EvidenceAssessments,
            AutomatedJournalIntakeReadiness.Ready, []);
    }
}
