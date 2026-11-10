using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.PrivateCapital;

/// <summary>Operator workflow over durable commitment authority and existing fund-economics kernels.</summary>
public sealed class PrivateCapitalLifecycleService(IPrivateCapitalStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PrivateCapitalWorkbenchDto> GetWorkbenchAsync(PrivateCapitalScopeDto scope, CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        var commitments = await store.ListCommitmentsAsync(scope, ct).ConfigureAwait(false);
        var calls = await store.ListCallsAsync(scope, ct).ConfigureAwait(false);
        var receipts = await store.ListPostingReceiptsAsync(scope, ct).ConfigureAwait(false);
        return new(scope, commitments, calls, receipts, _clock.GetUtcNow());
    }

    public Task<PrivateCapitalCommitmentDto> CreateCommitmentAsync(
        PrivateCapitalScopeDto scope, CreatePrivateCapitalCommitmentRequest request, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        actor = RequireActor(actor);
        if (request.ExpectedRevision < 0 || request.ExpectedRevision == int.MaxValue)
            throw new ArgumentException("Expected revision must permit a positive successor revision.");
        if (request.EvidenceLinks is not { Count: > 0 } || request.EvidenceLinks.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Retained commitment evidence is required.");
        var draft = new PrivateCapitalCommitmentDto(
            PrivateCapitalAuthorityRules.Id(request.CommitmentId, nameof(request.CommitmentId)), scope,
            checked(request.ExpectedRevision + 1),
            PrivateCapitalAuthorityRules.Id(request.InvestorId, nameof(request.InvestorId)),
            PrivateCapitalAuthorityRules.Id(request.CapitalAccountId, nameof(request.CapitalAccountId)),
            request.TotalCommitment, request.CommitmentDate, request.EffectiveFrom, request.InvestmentPeriodEndDate,
            request.IsActive, PrivateCapitalCommitmentApprovalStatus.Draft,
            request.EvidenceLinks?.Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray() ?? [],
            actor, _clock.GetUtcNow());
        PrivateCapitalAuthorityRules.ValidateCommitment(draft);
        return store.SaveCommitmentDraftAsync(draft, request.ExpectedRevision, ct);
    }

    public Task<PrivateCapitalCommitmentDto> ApproveCommitmentAsync(
        PrivateCapitalScopeDto scope, string commitmentId, int revision, string actor, CancellationToken ct = default)
        => store.ApproveCommitmentAsync(PrivateCapitalAuthorityRules.NormalizeScope(scope),
            PrivateCapitalAuthorityRules.Id(commitmentId, nameof(commitmentId)), revision,
            RequireActor(actor), _clock.GetUtcNow(), ct);

    public async Task<PrivateCapitalCallPreviewDto> PreviewCallAsync(
        PrivateCapitalScopeDto scope, PreviewPrivateCapitalCallRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        var callId = PrivateCapitalAuthorityRules.Id(request.CallId, nameof(request.CallId));
        if (!Guid.TryParse(request.PeriodId, out var periodId) || periodId == Guid.Empty)
            throw new ArgumentException("An authoritative accounting period is required.");
        if (request.AmountToCall <= 0m || request.NoticeDate == default || request.DueDate < request.NoticeDate)
            throw new ArgumentException("A positive amount and valid notice/due dates are required.");
        if (request.CommitmentIds is not { Count: > 0 })
            throw new ArgumentException("Select at least one approved commitment.");
        var ids = request.CommitmentIds.Select(x => PrivateCapitalAuthorityRules.Id(x, "CommitmentId")).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("A commitment cannot occur twice in one call.");
        var revisions = await store.ListCommitmentsAsync(scope, ct).ConfigureAwait(false);
        var receipts = await store.ListPostingReceiptsAsync(scope, ct).ConfigureAwait(false);
        var selected = new List<PrivateCapitalCommitmentDto>();
        var blockers = new List<string>();
        foreach (var id in ids.OrderBy(x => x, StringComparer.Ordinal))
        {
            var commitment = revisions.Where(x => x.CommitmentId == id
                && x.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved
                && x.EffectiveFrom <= request.NoticeDate)
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Revision).FirstOrDefault();
            if (commitment is null || !commitment.IsActive)
            {
                blockers.Add($"Commitment '{id}' has no active approved revision effective on the notice date.");
                continue;
            }
            if (await store.HasUnreconciledLegacyCapitalAsync(scope, commitment.InvestorId, ct).ConfigureAwait(false))
            {
                blockers.Add($"Investor '{commitment.InvestorId}' has historical capital activity requiring opening reconciliation.");
                continue;
            }
            selected.Add(commitment);
        }
        if (blockers.Count > 0)
            return new(null, false, blockers);
        var rollForwards = selected.Select(x => CommitmentRollForwardCalculator.Build(
            new InvestorCommitment(x.CommitmentId, scope.FundProfileId, scope.LedgerBookId, x.CapitalAccountId,
                x.InvestorId, scope.Currency, x.TotalCommitment, x.CommitmentDate, x.InvestmentPeriodEndDate,
                CommitmentStatus.Active),
            receipts.Where(r => r.CommitmentId == x.CommitmentId && r.Kind == "Issuance")
                .Select(r => new CommitmentActivityEvent(r.PostingId, ManualJournalEntryTypeDto.CapitalCall,
                    r.EffectiveDate, r.Amount)).ToArray())).ToArray();
        var plan = CapitalCallPlanBuilder.Build(new CapitalCallPlanRequest(callId, scope.FundProfileId,
            request.AmountToCall, request.NoticeDate, request.DueDate, rollForwards,
            CapitalCallAllocationBasis.ProRataByUncalled, request.Purpose));
        blockers.AddRange(plan.ValidationIssues.Where(x => x.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(x => x.Message));
        if (!plan.IsExecutable && blockers.Count == 0)
            blockers.Add("The requested call exceeds callable capacity or cannot be allocated exactly.");
        if (blockers.Count > 0)
            return new(null, false, blockers);
        var byId = selected.ToDictionary(x => x.CommitmentId, StringComparer.Ordinal);
        var lines = plan.Lines.Select(x => new PrivateCapitalCallLineDto(x.InstallmentId, x.Commitment.CommitmentId,
            byId[x.Commitment.CommitmentId].Revision, x.Commitment.InvestorId, x.Commitment.CapitalAccountId,
            x.CallAmount, x.UncalledBeforeCall, x.UncalledAfterCall)).ToArray();
        var call = new PrivateCapitalCallDto(callId, scope, periodId.ToString("D"), request.NoticeDate,
            request.DueDate, request.AmountToCall, request.Purpose?.Trim(), string.Empty, lines, string.Empty, default);
        return new(call with { PlanHash = PrivateCapitalCallPlanHash.Compute(call) }, true, []);
    }

    public async Task<PrivateCapitalCallDto> CreateCallAsync(
        PrivateCapitalScopeDto scope, RetainPrivateCapitalCallRequest request, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Preview);
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        actor = RequireActor(actor);
        var retained = await store.GetCallAsync(scope,
            PrivateCapitalAuthorityRules.Id(request.Preview.CallId, nameof(request.Preview.CallId)), ct).ConfigureAwait(false);
        if (retained is not null)
        {
            var requestedIds = request.Preview.CommitmentIds?.Select(x => PrivateCapitalAuthorityRules.Id(x, "CommitmentId"))
                .OrderBy(x => x, StringComparer.Ordinal).ToArray() ?? [];
            if (retained.PlanHash != request.ExpectedPlanHash
                || retained.AmountToCall != request.Preview.AmountToCall || retained.NoticeDate != request.Preview.NoticeDate
                || retained.DueDate != request.Preview.DueDate || !Guid.TryParse(request.Preview.PeriodId, out var retainedPeriod)
                || retained.PeriodId != retainedPeriod.ToString("D") || retained.Purpose != request.Preview.Purpose?.Trim()
                || !retained.Lines.Select(x => x.CommitmentId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(requestedIds))
                throw new InvalidOperationException("This call identity already retains a different reviewed request.");
            return retained;
        }
        var preview = await PreviewCallAsync(scope, request.Preview, ct).ConfigureAwait(false);
        if (!preview.IsReady || preview.Call is null)
            throw new InvalidOperationException(string.Join(" ", preview.Blockers));
        if (!string.Equals(preview.Call.PlanHash, request.ExpectedPlanHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The call preview changed. Preview and review the current allocation before retaining it.");
        return await store.SaveCallAsync(preview.Call with
        {
            CreatedBy = RequireActor(actor), CreatedAtUtc = _clock.GetUtcNow()
        }, ct).ConfigureAwait(false);
    }

    private static string RequireActor(string actor)
        => !string.IsNullOrWhiteSpace(actor) ? actor.Trim() : throw new ArgumentException("Authenticated actor is required.");
}
