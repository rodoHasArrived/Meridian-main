using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>Reads only explicitly retained, scoped amount links. Labels never establish provenance.</summary>
public sealed class LedgerAmountProvenanceService(
    IGovernanceReportPackRepository? reportPackRepository,
    IReconciliationBreakQueueRepository? breakQueueRepository = null)
{
    [Obsolete("Authoritative provenance requires authenticated tenant/company scope and a retained amount id.")]
    public Task<LedgerAmountProvenanceDetailDto?> GetAsync(Guid reportId, string scopeKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<LedgerAmountProvenanceDetailDto?>(null);
    }

    public async Task<LedgerAmountProvenanceDetailDto?> GetAsync(
        Guid reportId, string scopeKey, ReconciliationBreakQueueScope accessScope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        if (reportPackRepository is null || !Guid.TryParse(scopeKey, out var amountId) || amountId == Guid.Empty)
            return null;

        var snapshot = await reportPackRepository.GetAsync(reportId, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        var matches = (snapshot.Provenance.LedgerAmounts ?? []).Where(amount => amount.AmountId == amountId).ToArray();
        // Do not return any foreign identity, including labels or the amount itself.
        if (matches.Length == 0 || matches.Any(amount => !Owns(accessScope, amount.Scope)))
            return null;
        var amount = matches[0];
        var warnings = new List<string>
        {
            "Scoped report lineage is retained, but its source content has not been verified. Review required."
        };
        var status = EvidenceStatusDto.ReviewRequired;
        var evidence = new List<LedgerAmountProvenanceEvidenceDto>();
        var cases = new List<LedgerAmountReconciliationCaseDto>();
        var runs = new List<LedgerAmountStrategyRunLinkDto>();
        LedgerAmountSecurityMasterLinkDto? security = null;
        var pointers = snapshot.Provenance.LineagePointers.Where(pointer => pointer.AmountId == amountId).ToArray();
        if (matches.Length != 1 || !Complete(amount.Scope) ||
            !string.Equals(snapshot.FundProfileId, amount.Scope.FundProfileId, StringComparison.Ordinal) ||
            !Sha256Digest.IsWellFormed(amount.SourceSnapshotHash) ||
            !string.Equals(amount.SourceSnapshotHash, snapshot.Provenance.SourceSnapshotHash, StringComparison.Ordinal) ||
            pointers.Any(pointer => pointer.AmountScope != amount.Scope ||
                pointer.SourceSnapshotHash != amount.SourceSnapshotHash) ||
            pointers.GroupBy(pointer => (pointer.EvidenceType, pointer.EvidenceId)).Any(group => group.Count() != 1))
        {
            status = EvidenceStatusDto.Blocked;
            warnings.Add("Amount proof is blocked: retained identity, scope, or snapshot links are ambiguous, foreign, or stale.");
        }
        else
        {
            foreach (var pointer in pointers)
            {
                if (string.IsNullOrWhiteSpace(pointer.EvidenceId) || pointer.CapturedAt is null ||
                    pointer.CapturedAt > snapshot.GeneratedAt)
                {
                    if (status != EvidenceStatusDto.Blocked)
                        status = EvidenceStatusDto.ReviewRequired;
                    warnings.Add("A retained evidence reference is missing its identity or snapshot timestamp.");
                    continue;
                }
                if (pointer.EvidenceType == "reconciliation-case")
                {
                    var retainedCases = breakQueueRepository is null ? [] :
                        await breakQueueRepository.GetAllAsync(accessScope, status: null, ct).ConfigureAwait(false);
                    var linked = retainedCases.Where(item => item.BreakId == pointer.EvidenceId).ToArray();
                    if (linked.Length != 1 || !accessScope.Owns(linked[0]) ||
                        linked[0].FundProfileId != amount.Scope.FundProfileId ||
                        linked[0].LedgerBookId != amount.Scope.LedgerBookId ||
                        !Guid.TryParse(linked[0].AccountingPeriodId, out var periodId) || periodId != amount.Scope.PeriodId)
                    {
                        if (status != EvidenceStatusDto.Blocked)
                            status = linked.Length == 0 ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Blocked;
                        warnings.Add("An explicitly linked reconciliation case is missing, ambiguous, or outside the amount scope.");
                        continue;
                    }
                    if (linked[0].LastUpdatedAt != pointer.CapturedAt)
                    {
                        if (status != EvidenceStatusDto.Blocked)
                            status = EvidenceStatusDto.ReviewRequired;
                        warnings.Add("An explicitly linked reconciliation case changed after its retained snapshot; review required.");
                        continue;
                    }
                    cases.Add(ToCase(linked[0]));
                }
                if (pointer.EvidenceType == "security" && Guid.TryParse(pointer.EvidenceId, out var securityId))
                    security = new(amount.Symbol ?? "", pointer.Route, pointer.RelatedEvidenceIds ?? [],
                        pointer.EvidenceCount ?? 1, pointer.CapturedAt, securityId, pointer.DisplayLabel,
                        pointer.SourceSystem, pointer.EvidenceId);
                if (pointer.EvidenceType is "run" or "strategy-run")
                    runs.Add(new(pointer.EvidenceId, pointer.DisplayLabel, pointer.Route, pointer.SourceSystem, pointer.CapturedAt, true));
                evidence.Add(new(pointer.EvidenceType, pointer.EvidenceId, pointer.DisplayLabel, pointer.Route,
                    pointer.SourceSystem, pointer.RelatedEvidenceIds, pointer.EvidenceCount, pointer.Amount, pointer.CapturedAt));
            }
            if (!evidence.Any(item => item.EvidenceType == "ledger-account") ||
                !evidence.Any(item => item.EvidenceType is "provider-event" or "source-document"))
            {
                if (status != EvidenceStatusDto.Blocked)
                    status = EvidenceStatusDto.ReviewRequired;
                warnings.Add("Retained ledger or supporting source evidence is missing; review required.");
            }
        }
        if (status == EvidenceStatusDto.Blocked)
        {
            evidence.Clear();
            cases.Clear();
            runs.Clear();
            security = null;
        }
        var approval = snapshot.LifecycleEvents
            .Where(item => item.ToStatus is GovernanceReportPackStatusDto.Approved or GovernanceReportPackStatusDto.Retained)
            .OrderByDescending(item => item.ChangedAt).FirstOrDefault();
        return new LedgerAmountProvenanceDetailDto(snapshot.ReportId, amountId.ToString("D"), amount.AccountName,
            amount.Symbol, amount.Amount, snapshot.Currency, evidence, security,
            new(cases.Count, cases.Count(item => item.Status == "Open"), null, cases.Select(item => item.CaseId).ToArray(), cases),
            new(snapshot.Status, approval?.Actor, approval?.ChangedAt, snapshot.LifecycleEvents.Count),
            new(snapshot.ReportId, snapshot.DisplayName, snapshot.ReportKind, snapshot.FundProfileId, snapshot.AsOf,
                snapshot.GeneratedAt, snapshot.Currency, UiApiRoutes.WithParam(UiApiRoutes.FundReportPackById, "reportId", snapshot.ReportId.ToString("D"))),
            warnings.Distinct(StringComparer.Ordinal).ToArray(), runs)
        { Scope = amount.Scope, ProofStatus = status };
    }

    private static bool Owns(ReconciliationBreakQueueScope access, LedgerAmountScopeDto scope)
        => string.Equals(access.TenantId, scope.TenantId, StringComparison.Ordinal) &&
           string.Equals(access.CompanyId, scope.CompanyId, StringComparison.Ordinal);

    private static bool Complete(LedgerAmountScopeDto scope)
        => !string.IsNullOrWhiteSpace(scope.TenantId) && !string.IsNullOrWhiteSpace(scope.CompanyId) &&
           !string.IsNullOrWhiteSpace(scope.FundProfileId) && scope.LedgerBookId != Guid.Empty && scope.PeriodId != Guid.Empty;

    private static LedgerAmountReconciliationCaseDto ToCase(ReconciliationBreakQueueItem item)
        => new(item.BreakId, item.Status.ToString(), item.LifecycleState.ToString(), item.AssignedTo, item.Team,
            item.RequiredSignoffRole, item.SignoffStatus, item.ExceptionRoute, item.RecommendedAction,
            item.DetectedAt, item.LastUpdatedAt, item.Severity.ToString(), item.Variance, item.ToleranceBand,
            item.ReviewedBy, item.ReviewedAt, item.ResolvedBy, item.ResolvedAt, item.ResolutionNote,
            item.SignoffHistory?.Count ?? 0, item.SlaPolicyId, item.SlaDueAt, item.SlaBreached, item.SlaState,
            item.AgeBand, item.BusinessAgeHours, item.SignedOffBy, item.SignedOffAt, item.SignOffNote);
}
