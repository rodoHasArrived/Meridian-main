using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Meridian.Reporting;

/// <summary>
/// Durable result of a reconciliation/close process. Reporting can consume only an exact retained
/// receipt bound to the same authoritative source checkpoint; it must never synthesize one.
/// </summary>
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
    string? CompletionCheckpointHash = null);

/// <summary>
/// Immutable result emitted by a server-owned close/reconciliation workflow. The reporting
/// retention command binds this result to a fresh authoritative source checkpoint.
/// </summary>
public sealed record ReportingReconciliationCompletionEvidence(
    string CompletionCheckpointId,
    string CompletionCheckpointHash,
    DateTimeOffset CompletedAtUtc,
    bool HasOpenBreaks,
    ImmutableArray<string> EvidenceIds);

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

        var evidence = (source.EvidenceIds.IsDefault ? [] : source.EvidenceIds)
            .Concat(completion.EvidenceIds)
            .Append($"reconciliation-completion:{completion.CompletionCheckpointId}:{completion.CompletionCheckpointHash}")
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToImmutableArray();
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
            evidence);
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
            completion.CompletionCheckpointHash);
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
            evidenceWithoutReceipt);
        if (receipt.AsOfDate == default
            || receipt.ReconciledAtUtc == default
            || receipt.ReconciledAtUtc.Offset != TimeSpan.Zero
            || receipt.EvidenceIds.IsDefaultOrEmpty
            || receipt.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || receipt.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != receipt.EvidenceIds.Length
            || string.Equals(receipt.SourceCheckpointId, receipt.ReconciliationCheckpointId, StringComparison.Ordinal)
            || !string.Equals(
                receipt.ReconciliationCheckpointId,
                $"report-reconciliation-{expectedHash[..32]}",
                StringComparison.Ordinal)
            || !string.Equals(receipt.ReconciliationCheckpointHash, expectedHash, StringComparison.Ordinal)
            || !receipt.EvidenceIds.Contains(
                $"reconciliation-completion:{receipt.CompletionCheckpointId}:{receipt.CompletionCheckpointHash}",
                StringComparer.Ordinal)
            || !receipt.EvidenceIds.Contains(
                $"reconciliation-checkpoint:{receipt.ReconciliationCheckpointId}:{receipt.ReconciliationCheckpointHash}",
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A retained reconciliation receipt requires a UTC timestamp, distinct source/reconciliation identities, and unique exact evidence.",
                nameof(receipt));
        }
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
        left with { EvidenceIds = ImmutableArray<string>.Empty }
            == right with { EvidenceIds = ImmutableArray<string>.Empty }
        && left.EvidenceIds.SequenceEqual(right.EvidenceIds, StringComparer.Ordinal);

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
        ImmutableArray<string> evidenceIds)
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
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}
