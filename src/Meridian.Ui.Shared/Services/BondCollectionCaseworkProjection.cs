using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.FixedIncome;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Projects retained collection observations into the existing scoped reconciliation queue.
/// Source clearing is evidence; governed resolution, signoff and reopening remain queue operations.
/// </summary>
public sealed class BondCollectionCaseworkProjection(IReconciliationBreakQueueRepository queue,
    IBondMonitoringService monitoring)
{
    private const string Source = "bond-monitoring";

    public async Task<IReadOnlyList<string>> ProjectAsync(BondMonitoringSnapshotDto captured,
        string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var snapshot = (await monitoring.GetHistoryAsync(captured.Inputs.Scope, ct).ConfigureAwait(false))
            .SingleOrDefault(row => row.SnapshotId == captured.SnapshotId)
            ?? throw new InvalidOperationException("Collection casework requires an immutable retained monitoring snapshot.");
        if (snapshot.Issues.Count != 0 || !Guid.TryParse(snapshot.Inputs.Scope.BookId, out var bookId))
            throw new InvalidOperationException("Collection casework requires valid exact account and ledger-book source evidence.");
        var access = new ReconciliationBreakQueueScope(snapshot.Inputs.Scope.TenantId, snapshot.Inputs.Scope.CompanyId);
        var scope = snapshot.Inputs.Scope;
        var all = await queue.GetAllAsync(access, status: null, ct).ConfigureAwait(false);
        var owned = all.Where(row => row.SourceSystem == Source && row.ExternalAccountId == scope.AccountId
            && row.LedgerBookId == bookId).ToArray();
        var sourceLink = SourceLink(snapshot.SnapshotId);
        var result = new List<string>();
        foreach (var collection in snapshot.Collections.Where(row => IsException(row)))
        {
            var subject = $"obligation:{collection.ObligationId}";
            var reason = string.Create(CultureInfo.InvariantCulture,
                $"{collection.FlowType} {collection.Status}: expected {collection.ExpectedAmount} {collection.Currency}; gross collected {collection.GrossCollected}, net cash {collection.NetCollected}, tax {collection.WithholdingTax}, fees {collection.Fees}, unpaid {collection.RemainingAmount}; due {collection.DueDate:yyyy-MM-dd}, age {collection.AgeDays} days.");
            var paymentEvidence = snapshot.Inputs.Payments.Where(payment => collection.PaymentVersionIds.Contains(payment.VersionId))
                .Select(payment => payment.Provenance.Evidence.EvidenceUri);
            await RetainCase(subject, collection.Currency, collection.ExpectedAmount, collection.GrossCollected,
                reason, collection.Status == "Overdue" ? ReconciliationBreakCategory.MissingCashCoverage
                    : collection.Status == "Unexplained" ? ReconciliationBreakCategory.ClassificationGap : ReconciliationBreakCategory.PartialMatch,
                [sourceLink, .. paymentEvidence]).ConfigureAwait(false);
        }
        foreach (var payment in BondMonitoringEngine.SelectActivePayments(snapshot.Inputs.Payments)
            .Where(row => row.FlowType is "Coupon" or "Interest" or "Principal" or "Maturity" or "FactorPaydown"))
        {
            var allocated = snapshot.Inputs.Allocations.Where(row => row.ActualVersionId == payment.VersionId).Sum(row => row.NetAmount);
            if (allocated == payment.Amount)
                continue;
            await RetainCase($"cash:{payment.FlowId}", payment.Currency, 0m, payment.Amount - allocated,
                string.Create(CultureInfo.InvariantCulture, $"Unmatched {payment.FlowType} cash {payment.Amount - allocated} {payment.Currency}; source movement {payment.VersionId} needs an explicit contractual allocation."),
                ReconciliationBreakCategory.ClassificationGap, [sourceLink, payment.Provenance.Evidence.EvidenceUri]).ConfigureAwait(false);
        }
        // Inform earlier active casework when its source amount clears. This does not resolve it.
        foreach (var prior in owned.Where(row => row.SourceImportId != snapshot.SnapshotId && IsActive(row)))
        {
            var subject = prior.SourceBreakId;
            var cleared = subject is not null && (subject.StartsWith("obligation:", StringComparison.Ordinal)
                ? snapshot.Collections.Any(row => $"obligation:{row.ObligationId}" == subject && row.RemainingAmount == 0m)
                : subject.StartsWith("cash:", StringComparison.Ordinal) && BondMonitoringEngine.SelectActivePayments(snapshot.Inputs.Payments)
                    .Any(payment => $"cash:{payment.FlowId}" == subject
                        && payment.Amount == snapshot.Inputs.Allocations.Where(row => row.ActualVersionId == payment.VersionId).Sum(row => row.NetAmount)));
            if (cleared)
                await AttachObservationAsync(access, prior.BreakId, snapshot, actor,
                    "Retained monitoring now explains the source cash in full. Review the new evidence before resolving this case through the existing governed casework flow.", ct).ConfigureAwait(false);
        }
        return result;

        async Task RetainCase(string subject, string currency, decimal expected, decimal actual, string reason,
            ReconciliationBreakCategory category, IReadOnlyList<string> evidence)
        {
            var caseId = "bond-collection:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[]
                { scope.TenantId, scope.CompanyId, scope.AccountId, scope.BookId, snapshot.SnapshotId, subject }));
            var existing = await queue.GetByIdAsync(access, caseId, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.SourceImportId != snapshot.SnapshotId || existing.SourceBreakId != subject
                    || existing.ExternalAccountId != scope.AccountId || existing.LedgerBookId != bookId)
                    throw new InvalidDataException("Retained collection case identity belongs to different source or account evidence.");
                result.Add(caseId);
                var predecessor = existing.EvidenceLinks?.FirstOrDefault(link => link.StartsWith("reconciliation-case:", StringComparison.Ordinal));
                if (predecessor is not null)
                {
                    var priorId = predecessor["reconciliation-case:".Length..];
                    if (owned.Any(row => row.BreakId == priorId && IsActive(row)))
                        await AttachObservationAsync(access, priorId, snapshot, actor,
                            $"A later retained bond collection observation is linked as case {caseId}. {reason}", ct).ConfigureAwait(false);
                }
                return;
            }
            var previous = owned.Where(row => row.SourceBreakId == subject && row.SourceImportId != snapshot.SnapshotId
                    && row.DetectedAt <= snapshot.Inputs.RecordedAtUtc)
                .OrderByDescending(row => row.DetectedAt).ThenBy(row => row.BreakId, StringComparer.Ordinal).FirstOrDefault();
            var links = evidence.Concat(previous is null ? [] : new[] { $"reconciliation-case:{previous.BreakId}" })
                .Distinct(StringComparer.Ordinal).ToArray();
            var recurring = previous is not null && !IsActive(previous);
            var item = new ReconciliationBreakQueueItem(caseId, snapshot.Inputs.RunId, "Bond collection",
                category, ReconciliationBreakQueueStatus.Open, Math.Abs(actual - expected), reason,
                previous?.AssignedTo ?? actor, snapshot.Inputs.RecordedAtUtc, snapshot.Inputs.RecordedAtUtc,
                Severity: ReconciliationBreakSeverity.High,
                ExceptionRoute: "bond-collection-review", ToleranceBand: 0m,
                ExplainabilitySummary: recurring
                    ? $"A new retained source observation recurs after case {previous!.BreakId} was disposed. Its resolution and signoff remain intact; this linked observation needs review."
                    : previous is null ? "New retained bond collection exception." : $"Continues source exception {previous.BreakId}; prior ownership and history remain inspectable.",
                RoutingTarget: $"/accounting/reconciliation?ledgerBookId={bookId:D}&accountId={Uri.EscapeDataString(scope.AccountId)}&oversight=forecast#account-oversight",
                RecommendedAction: "ReviewAndResolve", ExternalAccountId: scope.AccountId,
                Team: "Accounting", SourceType: "bond-collection", SourceSystem: Source,
                SourceReference: sourceLink, SourceImportId: snapshot.SnapshotId,
                SourceBreakId: subject, SourceFingerprint: snapshot.SnapshotId,
                EvidenceLinks: links, EvidenceCount: links.Length, LedgerBookId: bookId,
                Measures: ReconciliationBreakQueueProjection.BuildDefaultMeasures(expected, actual, Math.Abs(actual - expected), 0m, currency),
                BlockedOutputs: ["BondIncomeForecast"],
                AsOfDate: DateOnly.FromDateTime(snapshot.Inputs.RecordedAtUtc.UtcDateTime),
                DataProvenanceToken: snapshot.Inputs.Obligations.Any(row => row.ModelVersion == "synthetic-verified") ? "simulated" : null)
            { TenantId = scope.TenantId, CompanyId = scope.CompanyId };
            await queue.CreateIfMissingAsync(access, item, ct).ConfigureAwait(false);
            result.Add(caseId);
            if (previous is not null && IsActive(previous))
                await AttachObservationAsync(access, previous.BreakId, snapshot, actor,
                    $"A later retained bond collection observation is linked as case {caseId}. {reason}", ct).ConfigureAwait(false);
        }
    }

    private async Task AttachObservationAsync(ReconciliationBreakQueueScope scope, string caseId,
        BondMonitoringSnapshotDto snapshot, string actor, string note, CancellationToken ct)
    {
        var evidence = SourceLink(snapshot.SnapshotId);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var item = await queue.GetByIdAsync(scope, caseId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("The collection case disappeared while retaining source evidence.");
            if (!IsActive(item) || item.EvidenceLinks?.Contains(evidence, StringComparer.Ordinal) == true)
                return;
            var id = $"bond-collection-observation:{snapshot.SnapshotId}:{caseId}:{item.Version}";
            var command = new ReconciliationCaseworkCommand(caseId, ReconciliationCaseworkAction.AddComment, actor,
                id, snapshot.Inputs.RunId, Source, item.Version,
                Reason: "Retained collection source observation", Note: note,
                CommentId: $"bond-source:{snapshot.SnapshotId}", EvidenceLinks: [evidence],
                ActionOrigin: OperationsActionOriginDto.AutomationAssistant);
            var transition = await queue.ApplyCaseworkCommandAsync(scope, command, ct).ConfigureAwait(false);
            if (transition.Status == ReconciliationBreakQueueTransitionStatus.Success)
                return;
            if (transition.Status != ReconciliationBreakQueueTransitionStatus.Conflict)
                throw new InvalidOperationException($"Collection evidence could not be retained in governed casework: {transition.Error}");
        }
        throw new InvalidOperationException("Collection casework changed repeatedly while attaching source evidence; retry the retained monitoring capture.");
    }

    private static string SourceLink(string snapshotId) => $"bond-monitoring://snapshot/{snapshotId}";
    private static bool IsException(BondCollectionStatusDto row) => row.RemainingAmount != 0m
        && (row.Status is "Overdue" or "Unexplained" || row.GrossCollected != 0m);
    private static bool IsActive(ReconciliationBreakQueueItem item) => item.LifecycleState is not
        (ReconciliationCaseLifecycleState.Resolved or ReconciliationCaseLifecycleState.SignedOff or ReconciliationCaseLifecycleState.Superseded)
        && item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview;
}
