using System.Collections.Immutable;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public interface IReportingReconciliationEvidenceSource
{
    ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
        ReportingRunParametersDto parameters,
        ReportingAuthoritativeSourceCheckpoint source,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A healthy evidence store had no receipt for the exact certified source cut. This is a logical
/// readiness blocker, not a dependency outage or integrity failure.
/// </summary>
public class ReportingReconciliationReadinessException :
    ReportingAuthoritativeSourceUnavailableException
{
    protected ReportingReconciliationReadinessException(string message) : base(message)
    {
    }
}

public sealed class ReportingReconciliationEvidenceMissingException :
    ReportingReconciliationReadinessException
{
    public ReportingReconciliationEvidenceMissingException(string message) : base(message)
    {
    }
}

public sealed class ReportingReconciliationEvidenceInvalidException :
    ReportingReconciliationReadinessException
{
    public ReportingReconciliationEvidenceInvalidException(
        string message,
        int blockingCount = 1,
        IReadOnlyList<string>? evidenceReferences = null) : base(message)
    {
        BlockingCount = blockingCount;
        EvidenceReferences = evidenceReferences ?? [];
    }

    public int BlockingCount { get; }

    public IReadOnlyList<string> EvidenceReferences { get; }
}

/// <summary>
/// Production fail-closed adapter over the durable reconciliation/close evidence store.
/// </summary>
public sealed class ReportingReconciliationEvidenceSource : IReportingReconciliationEvidenceSource
{
    private readonly IReportingReconciliationEvidenceStore _store;
    private readonly IReconciliationBreakQueueRepository? _breakQueue;

    public ReportingReconciliationEvidenceSource(
        IReportingReconciliationEvidenceStore store,
        IReconciliationBreakQueueRepository? breakQueue = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _breakQueue = breakQueue;
    }

    internal IReconciliationBreakQueueRepository? BreakQueueAuthority => _breakQueue;

    public async ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
        ReportingRunParametersDto parameters,
        ReportingAuthoritativeSourceCheckpoint source,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(accessContext);
        var sourceQueueScope = RequireExactQueueScope(
            source.TenantId,
            source.CompanyId,
            "authoritative reporting source");
        ReportingReconciliationEvidenceReceipt? receipt;
        try
        {
            receipt = await _store.GetExactAsync(
                    source.TenantId,
                    source.OrganizationId,
                    source.CompanyId,
                    source.FundId,
                    source.LedgerBookId,
                    source.AccountingPeriodId,
                    source.AccountingBasis,
                    source.AsOfDate,
                    source.CheckpointId,
                    source.CheckpointHash,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ReportingReconciliationEvidenceRecoveryRequiredException exception)
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                $"Retained reconciliation evidence is verified legacy data but cannot certify current reporting: {exception.Message} Re-run reconciliation and close to create item-level v2 evidence, then retry.",
                blockingCount: 1,
                evidenceReferences: ["reconciliation-evidence:migration-required"]);
        }
        if (receipt is null)
        {
            throw new ReportingReconciliationEvidenceMissingException(
                "No retained reconciliation/close checkpoint exists for the exact reporting tenant/fund/book/period/basis/as-of source.");
        }

        var receiptQueueScope = RequireExactQueueScope(
            receipt.TenantId,
            receipt.CompanyId,
            "retained reconciliation receipt");
        if (!ExactlyMatches(receipt, source)
            || !string.Equals(receiptQueueScope.TenantId, sourceQueueScope.TenantId, StringComparison.Ordinal)
            || !string.Equals(receiptQueueScope.CompanyId, sourceQueueScope.CompanyId, StringComparison.Ordinal)
            || !string.Equals(receipt.SourceCheckpointId, source.CheckpointId, StringComparison.Ordinal)
            || !string.Equals(receipt.SourceCheckpointHash, source.CheckpointHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                "The retained reconciliation/close receipt does not match the exact requested reporting tenant/fund/book/period/source scope.");
        }

        try
        {
            ReportingReconciliationEvidenceValidation.Validate(receipt);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reconciliation evidence failed integrity validation: {exception.Message}");
        }

        if (receipt.HasOpenBreaks)
        {
            var openBreaks = receipt.BreakEvidence
                .Where(static item => item.Disposition is null)
                .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
                .ToArray();
            var breakIds = string.Join(", ", openBreaks.Select(static item => item.BreakId));
            throw new ReportingReconciliationEvidenceInvalidException(
                $"The exact retained reconciliation receipt has {openBreaks.Length} unresolved break(s): {breakIds}.",
                openBreaks.Length,
                BuildOpenBreakEvidenceReferences(openBreaks, receipt.EvidenceIds));
        }

        await EnsureCurrentCanonicalQueueMatchesReceiptAsync(
                receipt,
                source,
                receiptQueueScope,
                cancellationToken)
            .ConfigureAwait(false);

        if (receipt.ReconciledAtUtc == default
            || receipt.ReconciledAtUtc.Offset != TimeSpan.Zero
            || !IsLowercaseSha256(receipt.ReconciliationCheckpointHash)
            || string.IsNullOrWhiteSpace(receipt.ReconciliationCheckpointId)
            || string.IsNullOrWhiteSpace(receipt.CompletionCheckpointId)
            || !IsLowercaseSha256(receipt.CompletionCheckpointHash)
            || string.Equals(receipt.ReconciliationCheckpointId, source.CheckpointId, StringComparison.Ordinal)
            || receipt.EvidenceIds.IsDefaultOrEmpty
            || receipt.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || !receipt.EvidenceIds.Contains(
                $"reconciliation-checkpoint:{receipt.ReconciliationCheckpointId}:{receipt.ReconciliationCheckpointHash}",
                StringComparer.Ordinal))
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                "The retained reconciliation/close receipt is stale, open, cross-scope, or not bound to the exact authoritative source checkpoint.");
        }

        return receipt;
    }

    private async Task EnsureCurrentCanonicalQueueMatchesReceiptAsync(
        ReportingReconciliationEvidenceReceipt receipt,
        ReportingAuthoritativeSourceCheckpoint source,
        ReconciliationBreakQueueScope queueScope,
        CancellationToken cancellationToken)
    {
        if (_breakQueue is null)
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                "The canonical reconciliation queue is unavailable, so final reporting cannot prove that the retained close receipt is still current. Restore the queue, then retry certification.");
        }
        if (!Guid.TryParse(source.LedgerBookId, out var ledgerBookId)
            || !Guid.TryParse(source.AccountingPeriodId, out var accountingPeriodId))
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                "The authoritative source does not contain canonical ledger-book and accounting-period identifiers required to verify the current reconciliation queue head.");
        }

        IReadOnlyList<ReconciliationBreakQueueItem> items;
        try
        {
            items = await _breakQueue
                .GetAllAsync(queueScope, ct: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                $"The canonical reconciliation queue could not be read, so final reporting is blocked: {exception.Message}");
        }

        ImmutableArray<ReportingReconciliationBreakEvidence> current;
        try
        {
            var currentOpenCount = items.Count(item =>
                item.LedgerBookId == ledgerBookId
                && string.Equals(item.FundProfileId, source.FundId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.AccountingPeriodId, source.AccountingPeriodId, StringComparison.OrdinalIgnoreCase)
                && item.AsOfDate == source.AsOfDate
                && item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview);
            current = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
                items,
                source.FundId,
                ledgerBookId,
                accountingPeriodId,
                source.AsOfDate,
                currentOpenCount);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"The canonical reconciliation queue failed close-evidence validation: {exception.Message}");
        }

        var currentOpen = current
            .Where(static item => item.Disposition is null)
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .ToArray();
        if (currentOpen.Length > 0)
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                $"The canonical reconciliation queue changed after the retained close receipt and now has {currentOpen.Length} unresolved break(s): {string.Join(", ", currentOpen.Select(static item => item.BreakId))}. Re-run reconciliation and close before generating a final report.",
                currentOpen.Length,
                BuildOpenBreakEvidenceReferences(currentOpen, receipt.EvidenceIds));
        }

        var retainedBreakEvidence = receipt.BreakEvidence.IsDefault
            ? ImmutableArray<ReportingReconciliationBreakEvidence>.Empty
            : receipt.BreakEvidence;
        var retainedHead = retainedBreakEvidence
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .Select(static item => $"{item.BreakId}:{item.EvidenceHashSha256}")
            .ToArray();
        var currentHead = current
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .Select(static item => $"{item.BreakId}:{item.EvidenceHashSha256}")
            .ToArray();
        if (!retainedHead.SequenceEqual(currentHead, StringComparer.Ordinal))
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                "The canonical reconciliation queue head changed after the retained close receipt. Re-run reconciliation and close to retain a new exact receipt before generating a final report.",
                blockingCount: 1,
                evidenceReferences: current
                    .Select(static item => $"reconciliation-break:{item.BreakId}:evidence-sha256:{item.EvidenceHashSha256}")
                    .ToArray());
        }
    }

    private static ReconciliationBreakQueueScope RequireExactQueueScope(
        string? tenantId,
        string? companyId,
        string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(companyId))
        {
            throw new ReportingReconciliationEvidenceInvalidException(
                $"The {sourceLabel} does not contain the exact tenant and company scope required to verify the canonical reconciliation queue.");
        }

        return new ReconciliationBreakQueueScope(tenantId, companyId);
    }

    private static IReadOnlyList<string> BuildOpenBreakEvidenceReferences(
        IReadOnlyList<ReportingReconciliationBreakEvidence> openBreaks,
        IReadOnlyList<string> receiptEvidence)
    {
        var references = new List<string>(receiptEvidence);
        foreach (var breakItem in openBreaks)
        {
            references.Add($"reconciliation-break:{breakItem.BreakId}");
            references.Add($"reconciliation-break:{breakItem.BreakId}:evidence-sha256:{breakItem.EvidenceHashSha256}");
            references.AddRange(breakItem.BlockedOutputs.Select(output =>
                $"reconciliation-break:{breakItem.BreakId}:blocked-output:{output}"));
            references.AddRange(breakItem.Measures.Select(measure =>
                $"reconciliation-break:{breakItem.BreakId}:measure:{measure.Kind}:expected={Format(measure.Expected)}:actual={Format(measure.Actual)}:variance={Format(measure.Variance)}:tolerance={Format(measure.Tolerance)}:unit={measure.Unit}:unavailable={measure.UnavailableReason ?? string.Empty}"));
            references.AddRange(breakItem.EvidenceIds);
        }

        return references
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Format(decimal? value) =>
        value?.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";

    private static bool ExactlyMatches(
        ReportingReconciliationEvidenceReceipt receipt,
        ReportingAuthoritativeSourceCheckpoint source) =>
        string.Equals(receipt.TenantId, source.TenantId, StringComparison.Ordinal)
        && string.Equals(receipt.OrganizationId, source.OrganizationId, StringComparison.Ordinal)
        && string.Equals(receipt.CompanyId, source.CompanyId, StringComparison.Ordinal)
        && string.Equals(receipt.FundId, source.FundId, StringComparison.Ordinal)
        && string.Equals(receipt.LedgerBookId, source.LedgerBookId, StringComparison.Ordinal)
        && string.Equals(receipt.AccountingPeriodId, source.AccountingPeriodId, StringComparison.Ordinal)
        && string.Equals(receipt.AccountingBasis, source.AccountingBasis, StringComparison.Ordinal)
        && receipt.AsOfDate == source.AsOfDate;

    private static bool IsLowercaseSha256(string? value) =>
        Sha256Digest.IsWellFormed(value)
        && string.Equals(value, value!.ToLowerInvariant(), StringComparison.Ordinal);
}
