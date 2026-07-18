using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Storage.Store;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Governed append-only retention facade used by close/reconciliation processing. The exact source
/// checkpoint is part of the immutable key; a non-identical retry is rejected.
/// </summary>
public sealed class ReportingReconciliationEvidenceRetentionService
{
    private readonly IReportingReconciliationEvidenceRetentionStore _store;
    private readonly IReportingAuthoritativeSource? _authoritativeSource;

    public ReportingReconciliationEvidenceRetentionService(
        IReportingReconciliationEvidenceRetentionStore store,
        IReportingAuthoritativeSource? authoritativeSource = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authoritativeSource = authoritativeSource;
    }

    /// <summary>
    /// Internal application command invoked by close/reconciliation completion. It captures the
    /// exact ledger source itself and derives the immutable reporting receipt; no public request can
    /// provide source rows, source identity, or a reporting reconciliation hash.
    /// </summary>
    public async ValueTask<ReportingReconciliationEvidenceReceipt> RetainCompletionAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        ReportingReconciliationCompletionEvidence completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(accessContext);
        ArgumentNullException.ThrowIfNull(completion);
        var source = _authoritativeSource
            ?? throw new ReportingAuthoritativeSourceUnavailableException(
                "No durable authoritative reporting source is configured for reconciliation completion retention.");
        ValidateBoundAccess(accessContext);
        ReportingReconciliationEvidenceValidation.ValidateCompletion(completion);
        var capture = await source
            .CaptureAsync(parameters, accessContext, cancellationToken)
            .ConfigureAwait(false);
        var authority = new ReportingAuthorityScope(
            "reporting-close-evidence-retention",
            capture.Checkpoint.TenantId,
            capture.Checkpoint.OrganizationId,
            capture.Checkpoint.CompanyId,
            [ReportingGovernancePermission.ExecuteRun],
            ReportingCommandOrigin.ServicePrincipal,
            $"close-evidence:{completion.CompletionCheckpointId}",
            []);
        ValidateInternalAuthority(authority, accessContext);
        if (!string.Equals(accessContext.TenantId, capture.Checkpoint.TenantId, StringComparison.Ordinal)
            || !string.Equals(accessContext.CompanyId, capture.Checkpoint.CompanyId, StringComparison.Ordinal))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Reconciliation completion scope does not match the captured tenant/company scope.");
        }
        var receipt = ReportingReconciliationEvidenceValidation.CreateReceipt(
            capture.Checkpoint,
            completion);
        await _store.RetainAsync(receipt, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    public async ValueTask<bool> RetainAsync(
        ReportingReconciliationEvidenceReceipt receipt,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(authority);
        ReportingReconciliationEvidenceValidation.Validate(receipt);
        if (!authority.HasPermission(ReportingGovernancePermission.ExecuteRun)
            || authority.Origin != ReportingCommandOrigin.ServicePrincipal
            || string.IsNullOrWhiteSpace(authority.ActorId)
            || string.IsNullOrWhiteSpace(authority.CorrelationId)
            || !string.Equals(authority.TenantId, receipt.TenantId, StringComparison.Ordinal)
            || !string.Equals(authority.OrganizationId, receipt.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(authority.CompanyId, receipt.CompanyId, StringComparison.Ordinal))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Retaining reconciliation evidence requires server-resolved ExecuteRun authority bound to the exact tenant/organization/company scope.");
        }

        return await _store.RetainAsync(receipt, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateInternalAuthority(
        ReportingAuthorityScope authority,
        ReportAccessQueryContext accessContext)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(accessContext);
        if (authority.Origin != ReportingCommandOrigin.ServicePrincipal
            || !authority.HasPermission(ReportingGovernancePermission.ExecuteRun)
            || string.IsNullOrWhiteSpace(authority.ActorId)
            || string.IsNullOrWhiteSpace(authority.CorrelationId)
            || !accessContext.RequireBoundScope
            || string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
            || !string.Equals(authority.TenantId, accessContext.TenantId, StringComparison.Ordinal)
            || !string.Equals(authority.CompanyId, accessContext.CompanyId, StringComparison.Ordinal))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Reconciliation completion retention requires a server service-principal with ExecuteRun authority bound to the exact tenant and company scope.");
        }
    }

    private static void ValidateBoundAccess(ReportAccessQueryContext accessContext)
    {
        ArgumentNullException.ThrowIfNull(accessContext);
        if (!accessContext.RequireBoundScope
            || string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(accessContext.TenantId)
            || string.IsNullOrWhiteSpace(accessContext.CompanyId)
            || accessContext.HasGlobalOverride)
        {
            throw new ReportingGovernanceAuthorizationException(
                "Reconciliation completion retention requires a server-bound actor, tenant, and company without a global override.");
        }
    }
}

/// <summary>
/// Durable local production store. The snapshot is atomically replaced, while individual receipts
/// are append-only and content-immutable by their full scope/source key.
/// </summary>
public sealed class FileReportingReconciliationEvidenceStore :
    JsonFileSnapshotStore<FileReportingReconciliationEvidenceStore.Snapshot>,
    IReportingReconciliationEvidenceRetentionStore
{
    private const string SnapshotSchemaVersion = "meridian.reporting.reconciliation-evidence.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileReportingReconciliationEvidenceStore(string snapshotPath)
        : base(
            string.IsNullOrWhiteSpace(snapshotPath)
                ? throw new ArgumentException(
                    "A reporting reconciliation evidence snapshot path is required.",
                    nameof(snapshotPath))
                : snapshotPath,
            JsonOptions)
    {
    }

    protected override Snapshot CreateEmptySnapshot() => CreateSnapshot([]);

    public async ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
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
        CancellationToken cancellationToken = default) =>
        await ReadSnapshotAsync(
            snapshot =>
            {
                VerifySnapshot(snapshot);
                return snapshot.Receipts.SingleOrDefault(receipt =>
                    ReportingReconciliationEvidenceValidation.MatchesKey(
                        receipt,
                        tenantId,
                        organizationId,
                        companyId,
                        fundId,
                        ledgerBookId,
                        accountingPeriodId,
                        accountingBasis,
                        asOfDate,
                        sourceCheckpointId,
                        sourceCheckpointHash));
            },
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<bool> RetainAsync(
        ReportingReconciliationEvidenceReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ReportingReconciliationEvidenceValidation.Validate(receipt);
        return await UpdateSnapshotAsync(
            snapshot =>
            {
                VerifySnapshot(snapshot);
                var existing = snapshot.Receipts.SingleOrDefault(candidate =>
                    ReportingReconciliationEvidenceValidation.HasSameKey(candidate, receipt));
                if (existing is not null)
                {
                    if (!ReportingReconciliationEvidenceValidation.SameReceipt(existing, receipt))
                    {
                        throw new ReportingArtifactCatalogIntegrityException(
                            "Attempted to replace an immutable retained reconciliation receipt with a non-identical payload.");
                    }

                    return (snapshot, true);
                }

                var receipts = snapshot.Receipts
                    .Append(receipt)
                    .OrderBy(static item => item.TenantId, StringComparer.Ordinal)
                    .ThenBy(static item => item.OrganizationId, StringComparer.Ordinal)
                    .ThenBy(static item => item.CompanyId, StringComparer.Ordinal)
                    .ThenBy(static item => item.FundId, StringComparer.Ordinal)
                    .ThenBy(static item => item.LedgerBookId, StringComparer.Ordinal)
                    .ThenBy(static item => item.AccountingPeriodId, StringComparer.Ordinal)
                    .ThenBy(static item => item.AsOfDate)
                    .ThenBy(static item => item.SourceCheckpointId, StringComparer.Ordinal)
                    .ToArray();
                return (CreateSnapshot(receipts), false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static Snapshot CreateSnapshot(
        IReadOnlyList<ReportingReconciliationEvidenceReceipt> receipts)
    {
        var ordered = receipts
            .OrderBy(static item => item.TenantId, StringComparer.Ordinal)
            .ThenBy(static item => item.OrganizationId, StringComparer.Ordinal)
            .ThenBy(static item => item.CompanyId, StringComparer.Ordinal)
            .ThenBy(static item => item.FundId, StringComparer.Ordinal)
            .ThenBy(static item => item.LedgerBookId, StringComparer.Ordinal)
            .ThenBy(static item => item.AccountingPeriodId, StringComparer.Ordinal)
            .ThenBy(static item => item.AsOfDate)
            .ThenBy(static item => item.SourceCheckpointId, StringComparer.Ordinal)
            .ToArray();
        foreach (var receipt in ordered)
        {
            ReportingReconciliationEvidenceValidation.Validate(receipt);
        }
        return new Snapshot(
            SnapshotSchemaVersion,
            ordered,
            ComputeSnapshotHash(ordered));
    }

    private static void VerifySnapshot(Snapshot snapshot)
    {
        if (!string.Equals(snapshot.SchemaVersion, SnapshotSchemaVersion, StringComparison.Ordinal)
            || snapshot.Receipts is null
            || !ReportingReconciliationEvidenceValidation.IsLowercaseSha256(snapshot.ContentHashSha256)
            || !string.Equals(
                snapshot.ContentHashSha256,
                ComputeSnapshotHash(snapshot.Receipts),
                StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "The retained reconciliation evidence snapshot failed schema or content-hash verification.");
        }

        foreach (var receipt in snapshot.Receipts)
        {
            try
            {
                ReportingReconciliationEvidenceValidation.Validate(receipt);
            }
            catch (ArgumentException exception)
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"The retained reconciliation evidence snapshot contains an invalid receipt: {exception.Message}");
            }
        }
    }

    private static string ComputeSnapshotHash(
        IReadOnlyList<ReportingReconciliationEvidenceReceipt> receipts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(receipts, JsonOptions)))).ToLowerInvariant();

    public sealed record Snapshot(
        string SchemaVersion,
        IReadOnlyList<ReportingReconciliationEvidenceReceipt> Receipts,
        string ContentHashSha256);
}

public sealed class InMemoryReportingReconciliationEvidenceStore :
    IReportingReconciliationEvidenceRetentionStore
{
    private readonly object _gate = new();
    private readonly List<ReportingReconciliationEvidenceReceipt> _receipts = [];

    public ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
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
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_receipts.SingleOrDefault(receipt =>
                ReportingReconciliationEvidenceValidation.MatchesKey(
                    receipt,
                    tenantId,
                    organizationId,
                    companyId,
                    fundId,
                    ledgerBookId,
                    accountingPeriodId,
                    accountingBasis,
                    asOfDate,
                    sourceCheckpointId,
                    sourceCheckpointHash)));
        }
    }

    public ValueTask<bool> RetainAsync(
        ReportingReconciliationEvidenceReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportingReconciliationEvidenceValidation.Validate(receipt);
        lock (_gate)
        {
            var existing = _receipts.SingleOrDefault(candidate =>
                ReportingReconciliationEvidenceValidation.HasSameKey(candidate, receipt));
            if (existing is not null)
            {
                if (!ReportingReconciliationEvidenceValidation.SameReceipt(existing, receipt))
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        "Attempted to replace an immutable retained reconciliation receipt with a non-identical payload.");
                }

                return ValueTask.FromResult(true);
            }

            _receipts.Add(receipt);
            return ValueTask.FromResult(false);
        }
    }
}
