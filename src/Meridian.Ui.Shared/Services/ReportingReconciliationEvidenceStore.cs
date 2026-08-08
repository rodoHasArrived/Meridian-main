using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Archival;
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
    private const string SnapshotSchemaVersion = "meridian.reporting.reconciliation-evidence.v2";
    private const string LegacySnapshotSchemaVersion = "meridian.reporting.reconciliation-evidence.v1";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _legacyRecoveryGate = new(1, 1);

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

    protected override Snapshot OnSnapshotLoaded(Snapshot snapshot)
    {
        if (string.Equals(snapshot.SchemaVersion, LegacySnapshotSchemaVersion, StringComparison.Ordinal))
        {
            if (!snapshot.ReceiptsOmitBreakEvidence)
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    "The retained reconciliation evidence v1 snapshot contains a non-legacy receipt shape.");
            }

            VerifyLegacySnapshot(snapshot);
            throw new ReportingReconciliationEvidenceMigrationRequiredException(
                "The retained reconciliation evidence snapshot is a verified v1 document and cannot establish the required item-level break evidence. " +
                "Preserve the snapshot, recover the authoritative reconciliation source, and retain a new v2 receipt through the governed completion workflow. " +
                "Do not edit the legacy file or synthesize break evidence during recovery.");
        }

        return snapshot;
    }

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
        await PrepareVerifiedLegacySnapshotForRecoveryAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task PrepareVerifiedLegacySnapshotForRecoveryAsync(CancellationToken ct)
    {
        if (!File.Exists(SnapshotPath))
            return;

        await _legacyRecoveryGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SnapshotPath))
                return;

            var retainedBytes = await File.ReadAllBytesAsync(SnapshotPath, ct).ConfigureAwait(false);
            Snapshot snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<Snapshot>(retainedBytes, JsonOptions)
                    ?? throw new ReportingArtifactCatalogIntegrityException(
                        "The retained reconciliation evidence snapshot deserialized to null.");
            }
            catch (JsonException exception)
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"The retained reconciliation evidence snapshot is malformed and cannot be recovered automatically: {exception.Message}");
            }

            if (!string.Equals(snapshot.SchemaVersion, LegacySnapshotSchemaVersion, StringComparison.Ordinal))
                return;

            if (!snapshot.ReceiptsOmitBreakEvidence)
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    "The retained reconciliation evidence v1 snapshot contains a non-legacy receipt shape.");
            }

            VerifyLegacySnapshot(snapshot);
            var backupPath = $"{SnapshotPath}.legacy-v1.{snapshot.ContentHashSha256[..16]}.json";
            if (File.Exists(backupPath))
            {
                var existingBackup = await File.ReadAllBytesAsync(backupPath, ct).ConfigureAwait(false);
                if (!existingBackup.AsSpan().SequenceEqual(retainedBytes))
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        "The deterministic legacy reconciliation evidence backup path contains different bytes.");
                }
            }
            else
            {
                await AtomicFileWriter.WriteAsync(backupPath, retainedBytes, ct).ConfigureAwait(false);
            }

            // The governed retain command supplies the authoritative v2 replacement receipt. The
            // verified v1 bytes remain immutable at the deterministic backup path for audit and
            // recovery; the configured path is reset only after that preservation succeeds.
            await WriteSnapshotAsync(CreateEmptySnapshot(), ct).ConfigureAwait(false);
        }
        finally
        {
            _legacyRecoveryGate.Release();
        }
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
            ComputeSnapshotHash(ordered))
        {
            ReceiptsIncludeBreakEvidence = true,
            ReceiptsOmitBreakEvidence = false
        };
    }

    private static void VerifySnapshot(Snapshot snapshot)
    {
        if (!string.Equals(snapshot.SchemaVersion, SnapshotSchemaVersion, StringComparison.Ordinal)
            || snapshot.Receipts is null
            || !snapshot.ReceiptsIncludeBreakEvidence
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

    private static void VerifyLegacySnapshot(Snapshot snapshot)
    {
        if (snapshot.Receipts is null
            || !ReportingReconciliationEvidenceValidation.IsLowercaseSha256(snapshot.ContentHashSha256)
            || !string.Equals(
                snapshot.ContentHashSha256,
                ComputeLegacySnapshotHash(snapshot.Receipts),
                StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "The retained reconciliation evidence v1 snapshot failed legacy content-hash verification.");
        }

        foreach (var receipt in snapshot.Receipts)
        {
            try
            {
                ValidateLegacyReceipt(ToLegacyReceipt(receipt));
            }
            catch (ArgumentException exception)
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"The retained reconciliation evidence v1 snapshot contains an invalid legacy receipt: {exception.Message}");
            }
        }
    }

    private static string ComputeSnapshotHash(
        IReadOnlyList<ReportingReconciliationEvidenceReceipt> receipts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                receipts.ToArray(),
                typeof(ReportingReconciliationEvidenceReceipt[]),
                ReportingReconciliationEvidenceJsonContext.Default)))).ToLowerInvariant();

    private static string ComputeLegacySnapshotHash(
        IReadOnlyList<ReportingReconciliationEvidenceReceipt> receipts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                receipts.Select(ToLegacyReceipt).ToArray(),
                typeof(LegacyReportingReconciliationEvidenceReceipt[]),
                ReportingReconciliationEvidenceJsonContext.Default)))).ToLowerInvariant();

    private static LegacyReportingReconciliationEvidenceReceipt ToLegacyReceipt(
        ReportingReconciliationEvidenceReceipt receipt) =>
        new(
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
            receipt.ReconciliationCheckpointId,
            receipt.ReconciliationCheckpointHash,
            receipt.ReconciledAtUtc,
            receipt.HasOpenBreaks,
            receipt.EvidenceIds,
            receipt.CompletionCheckpointId,
            receipt.CompletionCheckpointHash);

    private static void ValidateLegacyReceipt(LegacyReportingReconciliationEvidenceReceipt receipt)
    {
        RequireLegacyText(receipt.TenantId, nameof(receipt.TenantId));
        RequireLegacyText(receipt.OrganizationId, nameof(receipt.OrganizationId));
        RequireLegacyText(receipt.CompanyId, nameof(receipt.CompanyId));
        RequireLegacyText(receipt.FundId, nameof(receipt.FundId));
        RequireLegacyText(receipt.LedgerBookId, nameof(receipt.LedgerBookId));
        RequireLegacyText(receipt.AccountingPeriodId, nameof(receipt.AccountingPeriodId));
        RequireLegacyText(receipt.AccountingBasis, nameof(receipt.AccountingBasis));
        RequireLegacyText(receipt.SourceCheckpointId, nameof(receipt.SourceCheckpointId));
        RequireLegacyText(receipt.ReconciliationCheckpointId, nameof(receipt.ReconciliationCheckpointId));
        RequireLegacyText(receipt.CompletionCheckpointId, nameof(receipt.CompletionCheckpointId));
        RequireLegacyHash(receipt.SourceCheckpointHash, nameof(receipt.SourceCheckpointHash));
        RequireLegacyHash(receipt.ReconciliationCheckpointHash, nameof(receipt.ReconciliationCheckpointHash));
        RequireLegacyHash(receipt.CompletionCheckpointHash, nameof(receipt.CompletionCheckpointHash));
        if (receipt.EvidenceIds.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A legacy retained reconciliation receipt requires a UTC timestamp, distinct source/reconciliation identities, and unique exact evidence.",
                nameof(receipt));
        }

        var evidenceWithoutReceipt = receipt.EvidenceIds
            .Where(item => !string.Equals(
                item,
                $"reconciliation-checkpoint:{receipt.ReconciliationCheckpointId}:{receipt.ReconciliationCheckpointHash}",
                StringComparison.Ordinal))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToImmutableArray();
        var expectedHash = ComputeLegacyReceiptHash(
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
                "A legacy retained reconciliation receipt requires a UTC timestamp, distinct source/reconciliation identities, and unique exact evidence.",
                nameof(receipt));
        }
    }

    private static string ComputeLegacyReceiptHash(
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

    private static void RequireLegacyText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Legacy retained reconciliation identifiers must be present and trimmed.", parameterName);
        }
    }

    private static void RequireLegacyHash(string? value, string parameterName)
    {
        if (!ReportingReconciliationEvidenceValidation.IsLowercaseSha256(value))
        {
            throw new ArgumentException("Legacy retained reconciliation hashes must be lowercase SHA-256 values.", parameterName);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new SnapshotJsonConverter());
        return options;
    }

    public sealed record Snapshot(
        string SchemaVersion,
        IReadOnlyList<ReportingReconciliationEvidenceReceipt> Receipts,
        string ContentHashSha256)
    {
        [JsonIgnore]
        internal bool ReceiptsOmitBreakEvidence { get; init; }

        [JsonIgnore]
        internal bool ReceiptsIncludeBreakEvidence { get; init; }
    }

    private sealed class SnapshotJsonConverter : JsonConverter<Snapshot>
    {
        public override Snapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var schemaVersion = root.GetProperty("schemaVersion").GetString()
                ?? throw new JsonException("A reconciliation evidence snapshot schema version is required.");
            var contentHash = root.GetProperty("contentHashSha256").GetString()
                ?? throw new JsonException("A reconciliation evidence snapshot content hash is required.");
            if (!root.TryGetProperty("receipts", out var receiptsElement)
                || receiptsElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("A reconciliation evidence snapshot receipts array is required.");
            }

            var receipts = JsonSerializer.Deserialize(
                    receiptsElement.GetRawText(),
                    typeof(ReportingReconciliationEvidenceReceipt[]),
                    ReportingReconciliationEvidenceJsonContext.Default)
                as ReportingReconciliationEvidenceReceipt[]
                ?? throw new JsonException("A reconciliation evidence snapshot receipts array is required.");
            var receiptsOmitBreakEvidence = receiptsElement
                .EnumerateArray()
                .All(static receipt => !receipt.TryGetProperty("breakEvidence", out _));
            var receiptsIncludeBreakEvidence = receiptsElement
                .EnumerateArray()
                .All(static receipt => receipt.TryGetProperty("breakEvidence", out _));
            return new Snapshot(schemaVersion, receipts, contentHash)
            {
                ReceiptsOmitBreakEvidence = receiptsOmitBreakEvidence,
                ReceiptsIncludeBreakEvidence = receiptsIncludeBreakEvidence
            };
        }

        public override void Write(Utf8JsonWriter writer, Snapshot value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", value.SchemaVersion);
            writer.WritePropertyName("receipts");
            JsonSerializer.Serialize(
                writer,
                value.Receipts.ToArray(),
                typeof(ReportingReconciliationEvidenceReceipt[]),
                ReportingReconciliationEvidenceJsonContext.Default);
            writer.WriteString("contentHashSha256", value.ContentHashSha256);
            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// Raised only when a legacy reconciliation snapshot passed its v1 integrity checks but cannot
/// satisfy the current per-break evidence requirements without an operator-led recovery.
/// </summary>
public sealed class ReportingReconciliationEvidenceMigrationRequiredException :
    ReportingReconciliationEvidenceRecoveryRequiredException
{
    public ReportingReconciliationEvidenceMigrationRequiredException(string message)
        : base(message)
    {
    }
}

public sealed class InMemoryReportingReconciliationEvidenceStore :
    IReportingReconciliationEvidenceRetentionStore,
    Meridian.Application.Composition.INonProductionOnlyService
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
