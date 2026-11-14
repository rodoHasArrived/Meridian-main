using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>Retains source operands and routes discrepancies into existing governed casework.</summary>
public sealed class ThreeWayReconciliationService(
    IThreeWayReconciliationSourceReader sources,
    FileThreeWayReconciliationEvidenceStore store,
    IReconciliationBreakQueueRepository queue,
    ThreeWayReconciliationOptions options,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ThreeWayReconciliationResultDto> RunAsync(Guid accountId, ReconciliationBreakQueueScope access,
        ThreeWayReconciliationRequestDto request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        if (request.OperationId.Length > 200)
            throw new ArgumentException("Operation identity cannot exceed 200 characters.");
        var scope = await sources.AuthorizeAsync(accountId, access, request.LedgerBookId, request.AccountingPeriodId, ct)
            .ConfigureAwait(false);
        var requestHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request,
            ThreeWayReconciliationJsonContext.Default.ThreeWayReconciliationRequestDto));
        await using var lease = await store.AcquireOperationAsync(scope, request.OperationId, ct).ConfigureAwait(false);
        var retained = await store.ReadRunAsync(scope, request.OperationId, ct).ConfigureAwait(false);
        if (retained is not null && retained.RequestHash != requestHash)
            throw new InvalidOperationException("Reconciliation operation identity is already bound to another request.");
        if (retained is null)
        {
            await sources.CaptureAsync(scope, ct).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            var cutoff = request.CutoffUtc?.ToUniversalTime() ?? now;
            if (cutoff > now)
                throw new ArgumentException("Comparison cutoff cannot be in the future.");
            var snapshots = await sources.ReadAsync(scope, cutoff, ct).ConfigureAwait(false);
            var checks = ThreeWayReconciliationComparer.Compare(snapshots, cutoff,
                options.MaximumSourceAge, options.MaximumSourceDrift);
            var result = new ThreeWayReconciliationResultDto(
                "three-way:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(scope,
                    ThreeWayReconciliationJsonContext.Default.ThreeWayReconciliationScopeDto) + "|" + request.OperationId), request.OperationId, scope,
                cutoff, now, checks.Any(check => check.Status == "Blocked") ? "Blocked"
                    : checks.Any(check => check.Status == "Break") ? "Breaks" : "Matched",
                snapshots, checks, checks.Where(check => check.Status != "Matched")
                    .Select(check => BreakIdentity(scope, check.CheckId)).ToArray(),
                DataProvenanceExtensions.Strongest(snapshots.Select(source =>
                    DataProvenanceExtensions.ParseTokenOrSimulated(source.DataProvenanceToken))).Token())
            {
                AccountingPeriodVersion = snapshots.SingleOrDefault(source => source.Source == "ledger")?.AccountingPeriodVersion,
                AdjustmentProposals = ThreeWayAdjustmentProposalBuilder.Build(snapshots, checks, cutoff)
            };
            retained = new ThreeWayStoredRun(requestHash, result);
            // A prepared receipt freezes operands before casework. Interrupted publication resumes it.
            await store.SaveRunAsync(retained, ct).ConfigureAwait(false);
        }
        if (!retained.Completed)
        {
            await PublishCasesAsync(retained.Result, access, ct).ConfigureAwait(false);
            retained = retained with { Completed = true };
            await store.SaveRunAsync(retained, ct).ConfigureAwait(false);
        }
        return retained.Result;
    }

    public async Task<ThreeWayReconciliationResultDto?> GetLatestAsync(Guid accountId, ReconciliationBreakQueueScope access,
        Guid bookId, Guid periodId, CancellationToken ct = default)
    {
        var scope = await sources.AuthorizeAsync(accountId, access, bookId, periodId, ct).ConfigureAwait(false);
        return await store.GetLatestAsync(scope, ct).ConfigureAwait(false);
    }

    private async Task PublishCasesAsync(ThreeWayReconciliationResultDto result, ReconciliationBreakQueueScope access, CancellationToken ct)
    {
        foreach (var check in result.Checks.Where(check => check.Status != "Matched"))
        {
            var id = BreakIdentity(result.Scope, check.CheckId);
            var evidence = result.Sources.Select(source => source.EvidenceReference).ToArray();
            var item = new ReconciliationBreakQueueItem(id, result.RunId, "Three-way accounting reconciliation",
                check.Measure == "cash" && check.Status == "Break" ? ReconciliationBreakCategory.CashMismatch
                    : check.Status == "Blocked" ? check.LeftSource == "oms" || check.RightSource == "oms"
                        ? ReconciliationBreakCategory.MissingPortfolioCoverage
                        : check.LeftSource == "broker" || check.RightSource == "broker"
                            ? ReconciliationBreakCategory.MissingExternalStatementCoverage : ReconciliationBreakCategory.MissingLedgerCoverage
                    : ReconciliationBreakCategory.AmountMismatch,
                ReconciliationBreakQueueStatus.Open, (check.LeftAmount ?? 0m) - (check.RightAmount ?? 0m),
                check.Explanation, null, result.RecordedAtUtc, result.RecordedAtUtc,
                Severity: check.Status == "Blocked" ? ReconciliationBreakSeverity.Critical : ReconciliationBreakSeverity.High,
                ToleranceProfileId: "three-way-usd-v1", ToleranceBand: check.Tolerance,
                FundAccountId: result.Scope.FinancialAccountId.ToString("D"),
                RecommendedAction: "Inspect retained source operands. Replay evidenced missing fills; submit evidenced corrections through governed journal approval, then rerun comparison.",
                EvidenceLinks: evidence, EvidenceCount: evidence.Length,
                SourceType: "three-way", SourceSystem: "three-way", SourceReference: check.CheckId, SourceImportId: result.RunId,
                SourceFingerprint: Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(check,
                    ThreeWayReconciliationJsonContext.Default.ThreeWayReconciliationCheckDto)),
                LedgerBookId: result.Scope.LedgerBookId, AccountingPeriodId: result.Scope.AccountingPeriodId.ToString("D"),
                AsOfDate: DateOnly.FromDateTime(result.CutoffUtc.UtcDateTime), DataProvenanceToken: result.DataProvenanceToken)
            {
                TenantId = access.TenantId,
                CompanyId = access.CompanyId,
                FundProfileId = result.Scope.FundProfileId
            };
            // A subject is stable across comparison runs. Creation input belongs to the
            // first observation, so later runs reuse that case rather than replaying a changed
            // create payload. A separate cross-process subject lease closes the check/create race.
            await using var caseLease = await store.AcquireCaseAsync(result.Scope, id, ct).ConfigureAwait(false);
            var existing = await queue.GetByIdAsync(access, id, ct).ConfigureAwait(false);
            if (existing is null)
                await queue.CreateIfMissingAsync(access, item, ct).ConfigureAwait(false);
            else if (existing.SourceType != "three-way" || existing.SourceSystem != "three-way" ||
                existing.SourceReference != check.CheckId || existing.FundProfileId != result.Scope.FundProfileId ||
                existing.FundAccountId != result.Scope.FinancialAccountId.ToString("D") ||
                existing.LedgerBookId != result.Scope.LedgerBookId ||
                existing.AccountingPeriodId != result.Scope.AccountingPeriodId.ToString("D"))
                throw new InvalidOperationException("The retained stable comparison subject belongs to another source or accounting scope.");
            if (await queue.GetByIdAsync(access, id, ct).ConfigureAwait(false) is null)
                throw new InvalidOperationException("Required reconciliation case was not durably retained.");
        }
        // Source clearing and operator disposition are separate. A partial/blocked comparison clears nothing.
        if (result.Status != "Blocked" && queue is IReconciliationBreakObservationRepository observations)
            await observations.ObserveCompletedRunAsync(new ReconciliationCompletedRunObservation(
                result.RunId, new ReconciliationRunObservationScope(result.Scope.TenantId, result.Scope.CompanyId,
                    result.Scope.FundProfileId, result.Scope.FinancialAccountId.ToString("D"), result.Scope.LedgerBookId,
                    result.Scope.AccountingPeriodId.ToString("D"), "three-way", JsonSerializer.Serialize(
                        new[] { result.Scope.ProviderId, result.Scope.ExternalAccountId }, ThreeWayReconciliationJsonContext.Default.StringArray)),
                result.RecordedAtUtc, true, true, result.Checks.Where(check => check.Status == "Break")
                    .Select(check => new ReconciliationBreakObservation(BreakIdentity(result.Scope, check.CheckId), check.CheckId)).ToArray()), ct)
                .ConfigureAwait(false);
    }

    private static string BreakIdentity(ThreeWayReconciliationScopeDto scope, string checkId)
        => "three-way:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(scope,
            ThreeWayReconciliationJsonContext.Default.ThreeWayReconciliationScopeDto) + "|" + checkId);
}
