using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// Exact break evidence retained at the reconciliation/reporting boundary. Missing source measures
/// remain explicit through <see cref="ReconciliationBreakMeasureDto.UnavailableReason"/>.
/// </summary>
public sealed record ReportingReconciliationBreakEvidence(
    string BreakId,
    ImmutableArray<ReconciliationBreakMeasureDto> Measures,
    ImmutableArray<string> BlockedOutputs,
    ImmutableArray<string> EvidenceIds,
    string EvidenceHashSha256,
    ReconciliationBreakDispositionDto? Disposition = null,
    string? DispositionReason = null,
    string? SupersedingBreakId = null,
    string? DispositionActor = null,
    string? ApprovalActor = null,
    string? ApprovalReference = null);

/// <summary>
/// Immutable proof that the accounting-close workflow committed after the ledger hard close.
/// Final reporting must not treat a hard-close-only receipt as completed close authority.
/// </summary>
public sealed record ReportingCloseWorkflowCompletionEvidence(
    string WorkflowId,
    long WorkflowVersion,
    string FundAccountId,
    string LedgerBookId,
    string AccountingPeriodId,
    string ApprovalId,
    string ApprovalEvidenceHash,
    string ChecklistEvidenceHash,
    string ClosePackageId,
    string ClosePackageEvidenceHash,
    string CloseAuditEventId,
    string CloseAuditHash);

/// <summary>
/// Durable result of a reconciliation/close process. Reporting can consume only an exact retained
/// receipt bound to the same authoritative source checkpoint; it must never synthesize one.
/// </summary>
[method: JsonConstructor]
public sealed record ReportingReconciliationEvidenceReceipt(
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    string FundId,
    string LedgerBookId,
    string AccountingPeriodId,
    string AccountingBasis,
    DateOnly AsOfDate,
    string SourceCheckpointId,
    string SourceCheckpointHash,
    string ReconciliationCheckpointId,
    string ReconciliationCheckpointHash,
    DateTimeOffset ReconciledAtUtc,
    bool HasOpenBreaks,
    ImmutableArray<string> EvidenceIds,
    string? CompletionCheckpointId = null,
    string? CompletionCheckpointHash = null,
    ImmutableArray<ReportingReconciliationBreakEvidence> BreakEvidence = default,
    ReportingCloseWorkflowCompletionEvidence? CloseWorkflowCompletion = null)
{
    /// <summary>
    /// Binary-compatibility constructor for consumers compiled before committed-close evidence
    /// became part of the retained receipt.
    /// </summary>
    public ReportingReconciliationEvidenceReceipt(
        string TenantId,
        string OrganizationId,
        string? CompanyId,
        string FundId,
        string LedgerBookId,
        string AccountingPeriodId,
        string AccountingBasis,
        DateOnly AsOfDate,
        string SourceCheckpointId,
        string SourceCheckpointHash,
        string ReconciliationCheckpointId,
        string ReconciliationCheckpointHash,
        DateTimeOffset ReconciledAtUtc,
        bool HasOpenBreaks,
        ImmutableArray<string> EvidenceIds,
        string? CompletionCheckpointId = null,
        string? CompletionCheckpointHash = null,
        ImmutableArray<ReportingReconciliationBreakEvidence> BreakEvidence = default)
        : this(
            TenantId,
            OrganizationId,
            CompanyId,
            FundId,
            LedgerBookId,
            AccountingPeriodId,
            AccountingBasis,
            AsOfDate,
            SourceCheckpointId,
            SourceCheckpointHash,
            ReconciliationCheckpointId,
            ReconciliationCheckpointHash,
            ReconciledAtUtc,
            HasOpenBreaks,
            EvidenceIds,
            CompletionCheckpointId,
            CompletionCheckpointHash,
            BreakEvidence,
            CloseWorkflowCompletion: null)
    {
    }

    public void Deconstruct(
        out string TenantId,
        out string OrganizationId,
        out string? CompanyId,
        out string FundId,
        out string LedgerBookId,
        out string AccountingPeriodId,
        out string AccountingBasis,
        out DateOnly AsOfDate,
        out string SourceCheckpointId,
        out string SourceCheckpointHash,
        out string ReconciliationCheckpointId,
        out string ReconciliationCheckpointHash,
        out DateTimeOffset ReconciledAtUtc,
        out bool HasOpenBreaks,
        out ImmutableArray<string> EvidenceIds,
        out string? CompletionCheckpointId,
        out string? CompletionCheckpointHash,
        out ImmutableArray<ReportingReconciliationBreakEvidence> BreakEvidence)
    {
        TenantId = this.TenantId;
        OrganizationId = this.OrganizationId;
        CompanyId = this.CompanyId;
        FundId = this.FundId;
        LedgerBookId = this.LedgerBookId;
        AccountingPeriodId = this.AccountingPeriodId;
        AccountingBasis = this.AccountingBasis;
        AsOfDate = this.AsOfDate;
        SourceCheckpointId = this.SourceCheckpointId;
        SourceCheckpointHash = this.SourceCheckpointHash;
        ReconciliationCheckpointId = this.ReconciliationCheckpointId;
        ReconciliationCheckpointHash = this.ReconciliationCheckpointHash;
        ReconciledAtUtc = this.ReconciledAtUtc;
        HasOpenBreaks = this.HasOpenBreaks;
        EvidenceIds = this.EvidenceIds;
        CompletionCheckpointId = this.CompletionCheckpointId;
        CompletionCheckpointHash = this.CompletionCheckpointHash;
        BreakEvidence = this.BreakEvidence;
    }
}

/// <summary>
/// Immutable result emitted by a server-owned close/reconciliation workflow. The reporting
/// retention command binds this result to a fresh authoritative source checkpoint.
/// </summary>
public sealed record ReportingReconciliationCompletionEvidence(
    string CompletionCheckpointId,
    string CompletionCheckpointHash,
    DateTimeOffset CompletedAtUtc,
    bool HasOpenBreaks,
    ImmutableArray<string> EvidenceIds,
    ImmutableArray<ReportingReconciliationBreakEvidence> BreakEvidence = default,
    ReportingCloseWorkflowCompletionEvidence? CloseWorkflowCompletion = null)
{
    /// <summary>
    /// Binary-compatibility constructor for consumers compiled before committed-close evidence
    /// became part of reconciliation completion.
    /// </summary>
    public ReportingReconciliationCompletionEvidence(
        string CompletionCheckpointId,
        string CompletionCheckpointHash,
        DateTimeOffset CompletedAtUtc,
        bool HasOpenBreaks,
        ImmutableArray<string> EvidenceIds,
        ImmutableArray<ReportingReconciliationBreakEvidence> BreakEvidence)
        : this(
            CompletionCheckpointId,
            CompletionCheckpointHash,
            CompletedAtUtc,
            HasOpenBreaks,
            EvidenceIds,
            BreakEvidence,
            CloseWorkflowCompletion: null)
    {
    }

    public void Deconstruct(
        out string CompletionCheckpointId,
        out string CompletionCheckpointHash,
        out DateTimeOffset CompletedAtUtc,
        out bool HasOpenBreaks,
        out ImmutableArray<string> EvidenceIds,
        out ImmutableArray<ReportingReconciliationBreakEvidence> BreakEvidence)
    {
        CompletionCheckpointId = this.CompletionCheckpointId;
        CompletionCheckpointHash = this.CompletionCheckpointHash;
        CompletedAtUtc = this.CompletedAtUtc;
        HasOpenBreaks = this.HasOpenBreaks;
        EvidenceIds = this.EvidenceIds;
        BreakEvidence = this.BreakEvidence;
    }
}

/// <summary>
/// Durable evidence read boundary. Implementations must return only an exact retained receipt for
/// the complete scope and source key.
/// </summary>
public interface IReportingReconciliationEvidenceStore
{
    ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The retained evidence is authentic legacy data but cannot certify current reporting without a
/// new governed reconciliation/close receipt. Adapters throw this instead of misclassifying a
/// verified upgrade condition as corruption or silently synthesizing missing item evidence.
/// </summary>
public class ReportingReconciliationEvidenceRecoveryRequiredException : IOException
{
    public ReportingReconciliationEvidenceRecoveryRequiredException(string message)
        : base(message)
    {
    }
}

/// <summary>Append-only retention boundary used by the governed reconciliation completion path.</summary>
public interface IReportingReconciliationEvidenceRetentionStore : IReportingReconciliationEvidenceStore
{
    ValueTask<bool> RetainAsync(
        ReportingReconciliationEvidenceReceipt receipt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Canonical receipt construction and validation shared by application, development, and durable
/// storage adapters. Validation fails closed on malformed scope, hashes, timestamps, or evidence.
/// </summary>
public static class ReportingReconciliationEvidenceValidation
{
    public static ReportingReconciliationEvidenceReceipt CreateReceipt(
        ReportingAuthoritativeSourceCheckpoint source,
        ReportingReconciliationCompletionEvidence completion)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateCompletion(completion);
        RequireText(source.TenantId, nameof(source.TenantId));
        RequireText(source.OrganizationId, nameof(source.OrganizationId));
        RequireText(source.CompanyId, nameof(source.CompanyId));
        RequireText(source.FundId, nameof(source.FundId));
        RequireText(source.LedgerBookId, nameof(source.LedgerBookId));
        RequireText(source.AccountingPeriodId, nameof(source.AccountingPeriodId));
        RequireText(source.AccountingBasis, nameof(source.AccountingBasis));
        RequireText(source.CheckpointId, nameof(source.CheckpointId));
        RequireHash(source.CheckpointHash, nameof(source.CheckpointHash));
        ValidateCloseWorkflowCompletion(
            completion.CloseWorkflowCompletion,
            source.LedgerBookId,
            source.AccountingPeriodId);

        var evidence = (source.EvidenceIds.IsDefault ? [] : source.EvidenceIds)
            .Concat(completion.EvidenceIds)
            .Append($"reconciliation-completion:{completion.CompletionCheckpointId}:{completion.CompletionCheckpointHash}")
            .Concat(BuildCloseWorkflowEvidenceIds(completion.CloseWorkflowCompletion))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToImmutableArray();
        var breakEvidence = NormalizeBreakEvidence(completion.BreakEvidence);
        var hash = ComputeReceiptHash(
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
            completion.CompletionCheckpointId,
            completion.CompletionCheckpointHash,
            completion.CompletedAtUtc,
            completion.HasOpenBreaks,
            evidence,
            breakEvidence,
            completion.CloseWorkflowCompletion);
        var checkpointId = $"report-reconciliation-{hash[..32]}";
        return new ReportingReconciliationEvidenceReceipt(
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
            checkpointId,
            hash,
            completion.CompletedAtUtc,
            completion.HasOpenBreaks,
            evidence.Append($"reconciliation-checkpoint:{checkpointId}:{hash}").ToImmutableArray(),
            completion.CompletionCheckpointId,
            completion.CompletionCheckpointHash,
            breakEvidence,
            completion.CloseWorkflowCompletion);
    }

    public static void ValidateCompletion(ReportingReconciliationCompletionEvidence completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        RequireText(completion.CompletionCheckpointId, nameof(completion.CompletionCheckpointId));
        RequireHash(completion.CompletionCheckpointHash, nameof(completion.CompletionCheckpointHash));
        if (completion.CompletedAtUtc == default
            || completion.CompletedAtUtc.Offset != TimeSpan.Zero
            || completion.EvidenceIds.IsDefaultOrEmpty
            || completion.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || completion.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != completion.EvidenceIds.Length)
        {
            throw new ArgumentException(
                "Reconciliation completion evidence requires a UTC timestamp and unique immutable evidence ids.",
                nameof(completion));
        }

        var breakEvidence = NormalizeBreakEvidence(completion.BreakEvidence);
        ValidateCloseWorkflowCompletion(completion.CloseWorkflowCompletion);
        if (completion.HasOpenBreaks != breakEvidence.Any(static item => item.Disposition is null))
        {
            throw new ArgumentException(
                "Reconciliation completion open-break state must match its exact retained break evidence.",
                nameof(completion));
        }
    }

    public static void Validate(ReportingReconciliationEvidenceReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        RequireText(receipt.TenantId, nameof(receipt.TenantId));
        RequireText(receipt.OrganizationId, nameof(receipt.OrganizationId));
        RequireText(receipt.CompanyId, nameof(receipt.CompanyId));
        RequireText(receipt.FundId, nameof(receipt.FundId));
        RequireText(receipt.LedgerBookId, nameof(receipt.LedgerBookId));
        RequireText(receipt.AccountingPeriodId, nameof(receipt.AccountingPeriodId));
        RequireText(receipt.AccountingBasis, nameof(receipt.AccountingBasis));
        RequireText(receipt.SourceCheckpointId, nameof(receipt.SourceCheckpointId));
        RequireText(receipt.ReconciliationCheckpointId, nameof(receipt.ReconciliationCheckpointId));
        RequireText(receipt.CompletionCheckpointId, nameof(receipt.CompletionCheckpointId));
        RequireHash(receipt.SourceCheckpointHash, nameof(receipt.SourceCheckpointHash));
        RequireHash(receipt.ReconciliationCheckpointHash, nameof(receipt.ReconciliationCheckpointHash));
        RequireHash(receipt.CompletionCheckpointHash, nameof(receipt.CompletionCheckpointHash));
        ValidateCloseWorkflowCompletion(
            receipt.CloseWorkflowCompletion,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId);
        if (receipt.EvidenceIds.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A retained reconciliation receipt requires a UTC timestamp, distinct source/reconciliation identities, and unique exact evidence.",
                nameof(receipt));
        }
        var evidenceWithoutReceipt = receipt.EvidenceIds
            .Where(item => !string.Equals(
                item,
                $"reconciliation-checkpoint:{receipt.ReconciliationCheckpointId}:{receipt.ReconciliationCheckpointHash}",
                StringComparison.Ordinal))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToImmutableArray();
        var breakEvidence = NormalizeBreakEvidence(receipt.BreakEvidence);
        var expectedHash = ComputeReceiptHash(
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate,
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash,
            receipt.CompletionCheckpointId!,
            receipt.CompletionCheckpointHash!,
            receipt.ReconciledAtUtc,
            receipt.HasOpenBreaks,
            evidenceWithoutReceipt,
            breakEvidence,
            receipt.CloseWorkflowCompletion);
        var matchedHash = string.Equals(receipt.ReconciliationCheckpointHash, expectedHash, StringComparison.Ordinal)
            ? expectedHash
            : null;
        if (receipt.AsOfDate == default
            || receipt.ReconciledAtUtc == default
            || receipt.ReconciledAtUtc.Offset != TimeSpan.Zero
            || receipt.EvidenceIds.IsDefaultOrEmpty
            || receipt.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || receipt.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != receipt.EvidenceIds.Length
            || string.Equals(receipt.SourceCheckpointId, receipt.ReconciliationCheckpointId, StringComparison.Ordinal)
            || matchedHash is null
            || !string.Equals(
                receipt.ReconciliationCheckpointId,
                $"report-reconciliation-{matchedHash[..32]}",
                StringComparison.Ordinal)
            || !receipt.EvidenceIds.Contains(
                $"reconciliation-completion:{receipt.CompletionCheckpointId}:{receipt.CompletionCheckpointHash}",
                StringComparer.Ordinal)
            || !receipt.EvidenceIds.Contains(
                $"reconciliation-checkpoint:{receipt.ReconciliationCheckpointId}:{receipt.ReconciliationCheckpointHash}",
                StringComparer.Ordinal)
            || BuildCloseWorkflowEvidenceIds(receipt.CloseWorkflowCompletion)
                .Any(required => !receipt.EvidenceIds.Contains(required, StringComparer.Ordinal))
            || receipt.HasOpenBreaks != breakEvidence.Any(static item => item.Disposition is null))
        {
            throw new ArgumentException(
                "A retained reconciliation receipt requires a UTC timestamp, distinct source/reconciliation identities, and unique exact evidence.",
                nameof(receipt));
        }
    }

    /// <summary>
    /// Requires the retained close receipt to prove that the exact scoped Operations Continuity
    /// workflow committed its approval, checklist, close package, and hash-chained close audit.
    /// </summary>
    public static ReportingCloseWorkflowCompletionEvidence RequireCommittedCloseWorkflow(
        ReportingReconciliationEvidenceReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Validate(receipt);
        var completion = receipt.CloseWorkflowCompletion
            ?? throw new ArgumentException(
                "Final reporting requires retained proof that the accounting-close workflow committed after ledger hard close.",
                nameof(receipt));
        ValidateCloseWorkflowCompletion(completion, receipt.LedgerBookId, receipt.AccountingPeriodId);
        return completion;
    }

    public static bool HasSameKey(
        ReportingReconciliationEvidenceReceipt left,
        ReportingReconciliationEvidenceReceipt right) =>
        MatchesKey(
            left,
            right.TenantId,
            right.OrganizationId,
            right.CompanyId,
            right.FundId,
            right.LedgerBookId,
            right.AccountingPeriodId,
            right.AccountingBasis,
            right.AsOfDate,
            right.SourceCheckpointId,
            right.SourceCheckpointHash);

    public static bool MatchesKey(
        ReportingReconciliationEvidenceReceipt receipt,
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash) =>
        string.Equals(receipt.TenantId, tenantId, StringComparison.Ordinal)
        && string.Equals(receipt.OrganizationId, organizationId, StringComparison.Ordinal)
        && string.Equals(receipt.CompanyId, companyId, StringComparison.Ordinal)
        && string.Equals(receipt.FundId, fundId, StringComparison.Ordinal)
        && string.Equals(receipt.LedgerBookId, ledgerBookId, StringComparison.Ordinal)
        && string.Equals(receipt.AccountingPeriodId, accountingPeriodId, StringComparison.Ordinal)
        && string.Equals(receipt.AccountingBasis, accountingBasis, StringComparison.Ordinal)
        && receipt.AsOfDate == asOfDate
        && string.Equals(receipt.SourceCheckpointId, sourceCheckpointId, StringComparison.Ordinal)
        && string.Equals(receipt.SourceCheckpointHash, sourceCheckpointHash, StringComparison.OrdinalIgnoreCase);

    public static bool SameReceipt(
        ReportingReconciliationEvidenceReceipt left,
        ReportingReconciliationEvidenceReceipt right) =>
        left with
        {
            EvidenceIds = ImmutableArray<string>.Empty,
            BreakEvidence = ImmutableArray<ReportingReconciliationBreakEvidence>.Empty
        } == right with
        {
            EvidenceIds = ImmutableArray<string>.Empty,
            BreakEvidence = ImmutableArray<ReportingReconciliationBreakEvidence>.Empty
        }
        && left.EvidenceIds.SequenceEqual(right.EvidenceIds, StringComparer.Ordinal)
        && NormalizeBreakEvidence(left.BreakEvidence).SequenceEqual(NormalizeBreakEvidence(right.BreakEvidence));

    public static ReportingReconciliationBreakEvidence CreateBreakEvidence(
        ReconciliationBreakQueueItem item,
        IReadOnlyList<string>? blockedOutputs = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var measures = NormalizeMeasures(item.Measures);
        var outputs = (blockedOutputs ?? item.BlockedOutputs ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToImmutableArray();
        var evidenceIds = (item.EvidenceLinks ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Append(string.IsNullOrWhiteSpace(item.DispositionApprovalReference)
                ? null
                : item.DispositionApprovalReference.Trim())
            .Where(static value => value is not null)
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToImmutableArray();
        var evidence = new ReportingReconciliationBreakEvidence(
            item.BreakId.Trim(),
            measures,
            outputs,
            evidenceIds,
            string.Empty,
            item.Disposition,
            item.DispositionReason,
            item.SupersedingBreakId,
            item.ResolvedBy,
            item.DispositionApprovedBy,
            item.DispositionApprovalReference);
        return evidence with { EvidenceHashSha256 = ComputeBreakEvidenceHash(evidence) };
    }

    public static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 }
        && value.All(Uri.IsHexDigit)
        && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Retained reconciliation identifiers must be present and trimmed.", parameterName);
        }
    }

    private static void RequireHash(string? value, string parameterName)
    {
        if (!IsLowercaseSha256(value))
        {
            throw new ArgumentException("Retained reconciliation hashes must be lowercase SHA-256 values.", parameterName);
        }
    }

    private static string ComputeReceiptHash(
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        string completionCheckpointId,
        string completionCheckpointHash,
        DateTimeOffset reconciledAtUtc,
        bool hasOpenBreaks,
        ImmutableArray<string> evidenceIds,
        ImmutableArray<ReportingReconciliationBreakEvidence> breakEvidence,
        ReportingCloseWorkflowCompletionEvidence? closeWorkflowCompletion)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("organizationId", organizationId);
            writer.WriteString("companyId", companyId);
            writer.WriteString("fundId", fundId);
            writer.WriteString("ledgerBookId", ledgerBookId);
            writer.WriteString("accountingPeriodId", accountingPeriodId);
            writer.WriteString("accountingBasis", accountingBasis);
            writer.WriteString("asOfDate", asOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("sourceCheckpointId", sourceCheckpointId);
            writer.WriteString("sourceCheckpointHash", sourceCheckpointHash);
            writer.WriteString("completionCheckpointId", completionCheckpointId);
            writer.WriteString("completionCheckpointHash", completionCheckpointHash);
            writer.WriteString("reconciledAtUtc", reconciledAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteBoolean("hasOpenBreaks", hasOpenBreaks);
            writer.WriteStartArray("evidenceIds");
            foreach (var evidence in evidenceIds.OrderBy(static item => item, StringComparer.Ordinal))
            {
                writer.WriteStringValue(evidence);
            }
            writer.WriteEndArray();
            writer.WriteStartArray("breakEvidence");
            foreach (var breakItem in breakEvidence)
            {
                WriteBreakEvidence(writer, breakItem, includeDeclaredHash: true);
            }
            writer.WriteEndArray();
            WriteCloseWorkflowCompletion(writer, closeWorkflowCompletion);
            writer.WriteEndObject();
        }

        return Sha256Digest.Compute(stream.ToArray());
    }

    private static ImmutableArray<string> BuildCloseWorkflowEvidenceIds(
        ReportingCloseWorkflowCompletionEvidence? completion)
    {
        if (completion is null)
        {
            return [];
        }

        return
        [
            $"operations-workflow:{completion.WorkflowId}:version:{completion.WorkflowVersion}:closed",
            $"operations-approval:{completion.ApprovalId}:{completion.ApprovalEvidenceHash}",
            $"operations-close-checklist:{completion.ChecklistEvidenceHash}",
            $"operations-close-package:{completion.ClosePackageId}:{completion.ClosePackageEvidenceHash}",
            $"operations-close-audit:{completion.CloseAuditEventId}:{completion.CloseAuditHash}"
        ];
    }

    private static void ValidateCloseWorkflowCompletion(
        ReportingCloseWorkflowCompletionEvidence? completion,
        string? expectedLedgerBookId = null,
        string? expectedAccountingPeriodId = null)
    {
        if (completion is null)
        {
            return;
        }

        RequireGuid(completion.WorkflowId, nameof(completion.WorkflowId));
        RequireGuid(completion.FundAccountId, nameof(completion.FundAccountId));
        RequireGuid(completion.LedgerBookId, nameof(completion.LedgerBookId));
        RequireGuid(completion.AccountingPeriodId, nameof(completion.AccountingPeriodId));
        RequireText(completion.ApprovalId, nameof(completion.ApprovalId));
        RequireText(completion.ClosePackageId, nameof(completion.ClosePackageId));
        RequireGuid(completion.CloseAuditEventId, nameof(completion.CloseAuditEventId));
        RequireHash(completion.ApprovalEvidenceHash, nameof(completion.ApprovalEvidenceHash));
        RequireHash(completion.ChecklistEvidenceHash, nameof(completion.ChecklistEvidenceHash));
        RequireHash(completion.ClosePackageEvidenceHash, nameof(completion.ClosePackageEvidenceHash));
        RequireHash(completion.CloseAuditHash, nameof(completion.CloseAuditHash));
        if (completion.WorkflowVersion <= 0)
        {
            throw new ArgumentException(
                "Committed close workflow evidence requires a positive workflow version.",
                nameof(completion));
        }
        if (expectedLedgerBookId is not null
            && !string.Equals(completion.LedgerBookId, expectedLedgerBookId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Committed close workflow evidence does not match the retained ledger-book scope.",
                nameof(completion));
        }
        if (expectedAccountingPeriodId is not null
            && !string.Equals(completion.AccountingPeriodId, expectedAccountingPeriodId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Committed close workflow evidence does not match the retained accounting-period scope.",
                nameof(completion));
        }
    }

    private static void RequireGuid(string? value, string parameterName)
    {
        RequireText(value, parameterName);
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException(
                "Committed close workflow identifiers must be non-empty GUID values.",
                parameterName);
        }
    }

    private static void WriteCloseWorkflowCompletion(
        Utf8JsonWriter writer,
        ReportingCloseWorkflowCompletionEvidence? completion)
    {
        if (completion is null)
        {
            return;
        }

        writer.WriteStartObject("closeWorkflowCompletion");
        writer.WriteString("workflowId", completion.WorkflowId);
        writer.WriteNumber("workflowVersion", completion.WorkflowVersion);
        writer.WriteString("fundAccountId", completion.FundAccountId);
        writer.WriteString("ledgerBookId", completion.LedgerBookId);
        writer.WriteString("accountingPeriodId", completion.AccountingPeriodId);
        writer.WriteString("approvalId", completion.ApprovalId);
        writer.WriteString("approvalEvidenceHash", completion.ApprovalEvidenceHash);
        writer.WriteString("checklistEvidenceHash", completion.ChecklistEvidenceHash);
        writer.WriteString("closePackageId", completion.ClosePackageId);
        writer.WriteString("closePackageEvidenceHash", completion.ClosePackageEvidenceHash);
        writer.WriteString("closeAuditEventId", completion.CloseAuditEventId);
        writer.WriteString("closeAuditHash", completion.CloseAuditHash);
        writer.WriteEndObject();
    }

    private static ImmutableArray<ReportingReconciliationBreakEvidence> NormalizeBreakEvidence(
        ImmutableArray<ReportingReconciliationBreakEvidence> breakEvidence)
    {
        var normalized = breakEvidence.IsDefault
            ? ImmutableArray<ReportingReconciliationBreakEvidence>.Empty
            : breakEvidence
                .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
                .ToImmutableArray();
        if (normalized.Select(static item => item.BreakId).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new ArgumentException("Retained reconciliation break ids must be unique.", nameof(breakEvidence));
        }

        foreach (var item in normalized)
        {
            ValidateBreakEvidence(item);
        }

        return normalized;
    }

    private static ImmutableArray<ReconciliationBreakMeasureDto> NormalizeMeasures(
        IReadOnlyList<ReconciliationBreakMeasureDto>? measures)
    {
        var normalized = (measures ?? [])
            .OrderBy(static item => item.Kind)
            .ToImmutableArray();
        var requiredKinds = Enum.GetValues<ReconciliationBreakMeasureKindDto>();
        if (normalized.Length != requiredKinds.Length
            || normalized.Select(static item => item.Kind).Distinct().Count() != requiredKinds.Length
            || requiredKinds.Any(kind => normalized.All(item => item.Kind != kind)))
        {
            throw new ArgumentException("Break evidence requires exactly one Value, Quantity, and CostBasis measure.", nameof(measures));
        }

        foreach (var measure in normalized)
        {
            var hasExpected = measure.Expected.HasValue;
            var hasActual = measure.Actual.HasValue;
            var hasVariance = measure.Variance.HasValue;
            var hasCompleteComparison = hasExpected && hasActual && hasVariance;
            var isExplicitlyUnavailable = !hasExpected && !hasActual && !hasVariance
                && !string.IsNullOrWhiteSpace(measure.UnavailableReason);
            if (string.IsNullOrWhiteSpace(measure.Unit)
                || !string.Equals(measure.Unit, measure.Unit.Trim(), StringComparison.Ordinal)
                || measure.Tolerance is < 0m
                || hasCompleteComparison && measure.Variance!.Value != measure.Actual!.Value - measure.Expected!.Value
                || hasCompleteComparison && !string.IsNullOrWhiteSpace(measure.UnavailableReason)
                || !hasCompleteComparison && !isExplicitlyUnavailable)
            {
                throw new ArgumentException(
                    "Break measures require a trimmed unit and either a complete, arithmetically honest comparison or an explicit unavailable reason.",
                    nameof(measures));
            }
        }

        return normalized;
    }

    private static void ValidateBreakEvidence(ReportingReconciliationBreakEvidence item)
    {
        RequireText(item.BreakId, nameof(item.BreakId));
        _ = NormalizeMeasures(item.Measures);
        if (item.Disposition is { } disposition && !Enum.IsDefined(disposition))
        {
            throw new ArgumentException(
                $"Retained reconciliation break evidence contains undefined disposition value '{(byte)disposition}'.",
                nameof(item));
        }

        var hasDisposition = item.Disposition.HasValue;
        var dispositionComplete = !hasDisposition
            || !string.IsNullOrWhiteSpace(item.DispositionReason)
                && !string.IsNullOrWhiteSpace(item.DispositionActor);
        var governedExceptionComplete = item.Disposition is not (
                ReconciliationBreakDispositionDto.Waived or ReconciliationBreakDispositionDto.Superseded)
            || !string.IsNullOrWhiteSpace(item.ApprovalActor)
                && !string.IsNullOrWhiteSpace(item.ApprovalReference)
                && !string.Equals(item.DispositionActor, item.ApprovalActor, StringComparison.OrdinalIgnoreCase);
        if (item.Disposition is null && item.BlockedOutputs.IsDefaultOrEmpty
            || item.BlockedOutputs.Any(string.IsNullOrWhiteSpace)
            || item.BlockedOutputs.Any(static value => !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            || item.BlockedOutputs.Distinct(StringComparer.Ordinal).Count() != item.BlockedOutputs.Length
            || item.EvidenceIds.IsDefaultOrEmpty
            || item.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || item.EvidenceIds.Any(static value => !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            || item.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != item.EvidenceIds.Length
            || !dispositionComplete
            || !governedExceptionComplete
            || item.Disposition == ReconciliationBreakDispositionDto.Superseded && string.IsNullOrWhiteSpace(item.SupersedingBreakId)
            || !IsLowercaseSha256(item.EvidenceHashSha256)
            || !string.Equals(item.EvidenceHashSha256, ComputeBreakEvidenceHash(item), StringComparison.Ordinal))
        {
            throw new ArgumentException("Retained reconciliation break evidence is incomplete or failed hash validation.", nameof(item));
        }
    }

    private static string ComputeBreakEvidenceHash(ReportingReconciliationBreakEvidence item)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteBreakEvidence(writer, item, includeDeclaredHash: false);
        }
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static void WriteBreakEvidence(
        Utf8JsonWriter writer,
        ReportingReconciliationBreakEvidence item,
        bool includeDeclaredHash)
    {
        writer.WriteStartObject();
        writer.WriteString("breakId", item.BreakId);
        writer.WriteString("disposition", item.Disposition?.ToString());
        writer.WriteString("dispositionReason", item.DispositionReason);
        writer.WriteString("supersedingBreakId", item.SupersedingBreakId);
        writer.WriteString("dispositionActor", item.DispositionActor);
        writer.WriteString("approvalActor", item.ApprovalActor);
        writer.WriteString("approvalReference", item.ApprovalReference);
        if (includeDeclaredHash)
        {
            writer.WriteString("evidenceHashSha256", item.EvidenceHashSha256);
        }
        writer.WriteStartArray("measures");
        foreach (var measure in item.Measures.OrderBy(static value => value.Kind))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", measure.Kind.ToString());
            if (measure.Expected.HasValue)
                writer.WriteNumber("expected", measure.Expected.Value);
            else
                writer.WriteNull("expected");
            if (measure.Actual.HasValue)
                writer.WriteNumber("actual", measure.Actual.Value);
            else
                writer.WriteNull("actual");
            if (measure.Variance.HasValue)
                writer.WriteNumber("variance", measure.Variance.Value);
            else
                writer.WriteNull("variance");
            if (measure.Tolerance.HasValue)
                writer.WriteNumber("tolerance", measure.Tolerance.Value);
            else
                writer.WriteNull("tolerance");
            writer.WriteString("unit", measure.Unit);
            writer.WriteString("unavailableReason", measure.UnavailableReason);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("blockedOutputs");
        foreach (var output in item.BlockedOutputs.OrderBy(static value => value, StringComparer.Ordinal))
            writer.WriteStringValue(output);
        writer.WriteEndArray();
        writer.WriteStartArray("evidenceIds");
        foreach (var evidenceId in item.EvidenceIds.OrderBy(static value => value, StringComparer.Ordinal))
            writer.WriteStringValue(evidenceId);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
