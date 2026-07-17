using Meridian.Contracts.Workstation;
using Meridian.Reporting;

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
    public ReportingReconciliationEvidenceInvalidException(string message) : base(message)
    {
    }
}

/// <summary>
/// Production fail-closed adapter over the durable reconciliation/close evidence store.
/// </summary>
public sealed class ReportingReconciliationEvidenceSource : IReportingReconciliationEvidenceSource
{
    private readonly IReportingReconciliationEvidenceStore _store;

    public ReportingReconciliationEvidenceSource(IReportingReconciliationEvidenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
        ReportingRunParametersDto parameters,
        ReportingAuthoritativeSourceCheckpoint source,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(accessContext);
        var receipt = await _store.GetExactAsync(
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
            .ConfigureAwait(false)
            ?? throw new ReportingReconciliationEvidenceMissingException(
                "No retained reconciliation/close checkpoint exists for the exact reporting tenant/fund/book/period/basis/as-of source.");

        try
        {
            ReportingReconciliationEvidenceValidation.Validate(receipt);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reconciliation evidence failed integrity validation: {exception.Message}");
        }

        if (!ExactlyMatches(receipt, source)
            || !string.Equals(receipt.SourceCheckpointId, source.CheckpointId, StringComparison.Ordinal)
            || !string.Equals(receipt.SourceCheckpointHash, source.CheckpointHash, StringComparison.OrdinalIgnoreCase)
            || receipt.ReconciledAtUtc == default
            || receipt.ReconciledAtUtc.Offset != TimeSpan.Zero
            || receipt.HasOpenBreaks
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

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsLowercaseSha256(string? value) =>
        IsSha256(value)
        && string.Equals(value, value!.ToLowerInvariant(), StringComparison.Ordinal);
}
