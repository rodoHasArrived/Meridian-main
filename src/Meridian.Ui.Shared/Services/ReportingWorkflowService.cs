using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compatibility adapter for the Reporting-owned template registry used by workstation callers.
/// </summary>
public sealed class ReportTemplateRegistryService
{
    private readonly ReportingTemplateRegistryService _registry;

    public ReportTemplateRegistryService(IReportTemplateGovernanceStore? store = null)
    {
        _registry = new ReportingTemplateRegistryService(
            store is null ? null : new LegacyTemplateGovernanceStoreAdapter(store));
    }

    public ReportTemplateDefinitionDto Register(ReportTemplateDefinitionDto definition) =>
        _registry.Register(definition);

    public ReportTemplateDefinitionDto? Get(VersionedReportTemplateIdDto id) =>
        _registry.Get(id);

    public ReportTemplateDefinitionDto? Get(
        VersionedReportTemplateIdDto id,
        ReportAccessQueryContext? accessContext) =>
        _registry.Get(id, accessContext?.ToReportingContext());

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> List(bool includeSuperseded = false) =>
        _registry.List(includeSuperseded);

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> List(
        ReportAccessQueryContext? accessContext,
        bool includeSuperseded = false) =>
        _registry.List(accessContext?.ToReportingContext(), includeSuperseded);

    public ReportTemplateGovernanceRecordDto CreateDraft(
        ReportTemplateDraftRequestDto request,
        string actor,
        string? companyId = null,
        IReadOnlyList<string>? reportGroupPrincipalIds = null,
        string? tenantId = null) =>
        _registry.CreateDraft(request, actor, companyId, reportGroupPrincipalIds, tenantId);

    public ReportTemplateGovernanceRecordDto Submit(
        VersionedReportTemplateIdDto id,
        string actor,
        string? note = null,
        ReportAccessQueryContext? accessContext = null) =>
        _registry.Submit(id, actor, note, accessContext?.ToReportingContext());

    public ReportTemplateGovernanceRecordDto Approve(
        VersionedReportTemplateIdDto id,
        ReportTemplateDecisionRequestDto request,
        string actor,
        ReportAccessQueryContext? accessContext = null) =>
        _registry.Approve(id, request, actor, accessContext?.ToReportingContext());

    public ReportTemplateGovernanceRecordDto Reject(
        VersionedReportTemplateIdDto id,
        ReportTemplateDecisionRequestDto request,
        string actor,
        ReportAccessQueryContext? accessContext = null) =>
        _registry.Reject(id, request, actor, accessContext?.ToReportingContext());

    public RenderReportTemplateResponseDto Render(
        RenderReportTemplateRequestDto request,
        ReportAccessQueryContext? accessContext = null) =>
        _registry.Render(request, accessContext?.ToReportingContext());

    public bool CanAccess(VersionedReportTemplateIdDto id, ReportAccessQueryContext? context) =>
        _registry.CanAccess(id, context?.ToReportingContext());

    private sealed class LegacyTemplateGovernanceStoreAdapter(IReportTemplateGovernanceStore store)
        : IReportingTemplateGovernanceStore
    {
        public IReadOnlyList<ReportTemplateGovernanceRecordDto> Load() => store.Load();

        public void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records) => store.Save(records);
    }
}

public sealed record ReportTemplateGovernanceStoreOptions(string SnapshotPath);

/// <summary>
/// Loads and saves reporting template governance records.
/// </summary>
public interface IReportTemplateGovernanceStore
{
    IReadOnlyList<ReportTemplateGovernanceRecordDto> Load();
    void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records);
}

/// <summary>
/// Persists reporting template governance records to a snapshot file.
/// </summary>
public sealed class FileReportTemplateGovernanceStore : IReportTemplateGovernanceStore, IReportingTemplateGovernanceStore
{
    private readonly ReportTemplateGovernanceStoreOptions _options;
    private readonly ILogger<FileReportTemplateGovernanceStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportTemplateGovernanceStore(
        ReportTemplateGovernanceStoreOptions options,
        ILogger<FileReportTemplateGovernanceStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_options.SnapshotPath);
                var records = JsonSerializer.Deserialize<ReportTemplateGovernanceSnapshot>(json, _jsonOptions)?.Records
                    ?? throw new JsonException("Report template governance snapshot deserialized to null.");
                ValidateRecords(records);
                return records;
            }
            catch (Exception ex) when (ex is IOException
                                            or JsonException
                                            or InvalidDataException
                                            or ArgumentException
                                            or UnauthorizedAccessException)
            {
                _logger.LogCritical(ex, "Report template governance snapshot at {SnapshotPath} is unreadable; reporting is blocked until the state is recovered.", _options.SnapshotPath);
                throw new ReportingStateCorruptionException(_options.SnapshotPath, ex);
            }
        }
    }

    public void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            ValidateRecords(records);
            var snapshot = new ReportTemplateGovernanceSnapshot(
                records
                    .OrderBy(static record => record.TenantId, StringComparer.Ordinal)
                    .ThenBy(static record => record.CompanyId, StringComparer.Ordinal)
                    .ThenBy(static record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(static record => record.Definition.TemplateId.Version)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private static void ValidateRecords(IReadOnlyList<ReportTemplateGovernanceRecordDto> records)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record is null
                || record.Definition is null
                || string.IsNullOrWhiteSpace(record.Definition.TemplateId.Name)
                || record.Definition.TemplateId.Version <= 0)
            {
                throw new InvalidDataException(
                    "The report template governance snapshot contains an invalid record.");
            }

            var hasTenant = !string.IsNullOrWhiteSpace(record.TenantId);
            var hasCompany = !string.IsNullOrWhiteSpace(record.CompanyId);
            if (hasTenant != hasCompany)
            {
                throw new InvalidDataException(
                    $"Template '{record.Definition.TemplateId.Name}@v{record.Definition.TemplateId.Version}' has incomplete tenant/company ownership.");
            }

            if (hasCompany
                && !string.Equals(
                    record.Definition.AccessPolicy?.CompanyId,
                    record.CompanyId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Template '{record.Definition.TemplateId.Name}@v{record.Definition.TemplateId.Version}' has an access policy outside its immutable company scope.");
            }

            var identity = $"{record.TenantId?.Trim() ?? string.Empty}\u001f{record.CompanyId?.Trim() ?? string.Empty}\u001f{record.Definition.TemplateId.Name.Trim().ToLowerInvariant()}:{record.Definition.TemplateId.Version}";
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"The report template governance snapshot contains duplicate template identity '{identity}'.");
            }
        }
    }

    private sealed record ReportTemplateGovernanceSnapshot(IReadOnlyList<ReportTemplateGovernanceRecordDto> Records);
}

/// <summary>
/// Persists report-pack workflow records to a snapshot file.
/// </summary>
public sealed class FileReportPackWorkflowRecordStore : IReportPackWorkflowRecordStore
{
    private readonly ReportPackWorkflowRecordStoreOptions _options;
    private readonly ILogger<FileReportPackWorkflowRecordStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportPackWorkflowRecordStore(
        ReportPackWorkflowRecordStoreOptions options,
        ILogger<FileReportPackWorkflowRecordStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportPackWorkflowRecordDto> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_options.SnapshotPath);
                return JsonSerializer.Deserialize<ReportPackWorkflowSnapshot>(json, _jsonOptions)?.Records
                    ?? throw new JsonException("Report-pack workflow snapshot deserialized to null.");
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogCritical(ex, "Report-pack workflow snapshot at {SnapshotPath} is unreadable; reporting is blocked until the state is recovered.", _options.SnapshotPath);
                throw new ReportingStateCorruptionException(_options.SnapshotPath, ex);
            }
        }
    }

    public void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            var snapshot = new ReportPackWorkflowSnapshot(
                records
                    .OrderByDescending(static record => record.UpdatedAt)
                    .ThenBy(static record => record.ReportId)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private sealed record ReportPackWorkflowSnapshot(IReadOnlyList<ReportPackWorkflowRecordDto> Records);
}

/// <summary>
/// Manages report-pack submission, review, rejection, restatement, and lifecycle history.
/// </summary>
public sealed partial class ReportPackWorkflowService
{
    private const string LedgerFinancialRecordExplorerId = "ledger";
    private const string PortfolioFinancialRecordExplorerId = "portfolio";
    private const string SecurityInstrumentFinancialRecordExplorerId = "security-instrument";

    private static readonly IReadOnlyDictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]> AllowedTransitions =
        new Dictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]>
        {
            [ReportPackWorkflowStateDto.Draft] = [ReportPackWorkflowStateDto.InReview, ReportPackWorkflowStateDto.Validated],
            [ReportPackWorkflowStateDto.Validated] = [ReportPackWorkflowStateDto.InReview, ReportPackWorkflowStateDto.PendingApproval, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.InReview] = [ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.PendingApproval, ReportPackWorkflowStateDto.Rejected, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.PendingApproval] = [ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.Rejected] = [ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.Approved] = [ReportPackWorkflowStateDto.Published],
            [ReportPackWorkflowStateDto.Published] = [ReportPackWorkflowStateDto.Restated, ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Restated] = [ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Archived] = []
        };

    private readonly ConcurrentDictionary<Guid, ReportPackWorkflowRecordDto> _records = new();
    private readonly IReportPackWorkflowRecordStore? _store;
    private readonly IReportPackSecurityLineIndex? _securityLineIndex;

    public ReportPackWorkflowService(
        IReportPackWorkflowRecordStore? store = null,
        IReportPackSecurityLineIndex? securityLineIndex = null)
    {
        _store = store;
        _securityLineIndex = securityLineIndex;
        foreach (var record in _store?.Load() ?? [])
        {
            _records[record.ReportId] = record;
        }

        // Backfill the derived security→report-line index from the loaded records (the workflow records
        // are the source of truth), so the index is current on first use after a process start/upgrade.
        _securityLineIndex?.Rebuild(_records.Values);
    }

    public ReportPackWorkflowRecordDto Create(
        string fundProfileId,
        string fundAccountId,
        string period,
        VersionedReportTemplateIdDto templateId,
        string actor,
        IReadOnlyList<ReportPackLineProvenanceDto>? lineProvenance = null,
        ReportAccessPolicyDto? accessPolicy = null,
        ReportAccessQueryContext? accessContext = null)
    {
        var accessIssues = ReportAccessPolicyEvaluator.Validate(accessPolicy);
        if (accessIssues.Count > 0)
        {
            throw new ArgumentException($"Report pack access policy is invalid: {string.Join("; ", accessIssues)}.");
        }

        if (accessContext?.RequireBoundScope == true
            && (string.IsNullOrWhiteSpace(accessContext.TenantId)
                || string.IsNullOrWhiteSpace(accessContext.CompanyId)
                || string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)))
        {
            throw new UnauthorizedAccessException(
                "A server-resolved actor, tenant, and company scope is required to create a reporting pack.");
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var normalizedAccessPolicy = ReportAccessPolicyEvaluator.Normalize(accessPolicy, actor);
        if (normalizedAccessPolicy.Mode == ReportAccessModeDto.CompanyWide
            && string.IsNullOrWhiteSpace(normalizedAccessPolicy.CompanyId)
            && !string.IsNullOrWhiteSpace(accessContext?.CompanyId))
        {
            normalizedAccessPolicy = normalizedAccessPolicy with { CompanyId = accessContext.CompanyId.Trim() };
        }

        if (accessContext?.RequireBoundScope == true
            && !string.IsNullOrWhiteSpace(normalizedAccessPolicy.CompanyId)
            && !string.Equals(normalizedAccessPolicy.CompanyId, accessContext.CompanyId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The reporting pack access policy belongs to another company.");
        }

        var accessPolicyHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(normalizedAccessPolicy));
        var record = new ReportPackWorkflowRecordDto(id, fundProfileId, fundAccountId, period, templateId, ReportPackWorkflowStateDto.Draft, 1, now, actor, now,
            [new ReportPackAuditEventDto(now, actor, "create", ReportPackWorkflowStateDto.Draft, ReportPackWorkflowStateDto.Draft)]
            , null,
            NormalizeLineProvenance(lineProvenance),
            AccessPolicy: normalizedAccessPolicy,
            TenantId: accessContext?.TenantId?.Trim(),
            CompanyId: accessContext?.CompanyId?.Trim(),
            AccessPolicySnapshotHash: accessPolicyHash);
        SaveAndIndex(record);
        return record;
    }

    public ReportPackWorkflowRecordDto Submit(Guid reportId, string actor, string role, string? note = null) =>
        TransitionCore(reportId, ReportPackWorkflowStateDto.InReview, actor, role, note);

    public ReportPackWorkflowRecordDto Transition(
        Guid reportId,
        ReportPackWorkflowStateDto target,
        string actor,
        string role,
        string? note = null,
        OperationsActionOriginDto actionOrigin = OperationsActionOriginDto.HumanOperator)
    {
        if (target == ReportPackWorkflowStateDto.Published)
        {
            throw new InvalidOperationException("Report pack publication requires sign-off, evidence hash, and retained manifest metadata.");
        }

        EnsureHumanOriginForMaterialTransition(target, actionOrigin);
        return TransitionCore(reportId, target, actor, role, note);
    }

    private ReportPackWorkflowRecordDto TransitionCore(Guid reportId, ReportPackWorkflowStateDto target, string actor, string role, string? note = null)
    {
        if (!_records.TryGetValue(reportId, out var record))
            throw new KeyNotFoundException("report pack not found");
        EnsureRole(target, role);
        if (!AllowedTransitions[record.State].Contains(target))
            throw new InvalidOperationException($"invalid transition {record.State} -> {target}");
        var now = DateTimeOffset.UtcNow;
        var next = record with
        {
            State = target,
            UpdatedAt = now,
            AuditTrail = record.AuditTrail.Append(new ReportPackAuditEventDto(now, actor, target.ToString().ToLowerInvariant(), record.State, target, note)).ToArray()
        };
        SaveAndIndex(next);
        return next;
    }

    public ReportPackWorkflowRecordDto Reject(Guid reportId, string reason, string actor, string role, IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var transitioned = TransitionCore(reportId, ReportPackWorkflowStateDto.Rejected, actor, role, reason.Trim());
        var rejectedAt = DateTimeOffset.UtcNow;
        var next = transitioned with
        {
            Rejection = new ReportPackRejectionMetadataDto(
                reason.Trim(),
                actor.Trim(),
                role.Trim(),
                rejectedAt,
                evidenceLinks is null ? null : NormalizeEvidenceLinks(evidenceLinks))
        };
        SaveAndIndex(next);
        return next;
    }

    public ReportPackWorkflowRecordDto Reject(Guid reportId, ReportPackRejectRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Reject(reportId, request.Reason, request.Actor, request.ActorRole, request.EvidenceLinks);
    }

    public ReportPackWorkflowRecordDto Restate(
        Guid reportId,
        string actor,
        string role,
        string reasonCode,
        string approver,
        Guid priorVersionReportId,
        IReadOnlyList<ReportPackChangedLineDto> changedLines,
        OperationsActionOriginDto actionOrigin = OperationsActionOriginDto.HumanOperator)
    {
        EnsureHumanOrigin(actionOrigin, "restate reports");
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("reasonCode is required");
        if (changedLines.Count == 0)
            throw new ArgumentException("changedLines are required");
        var linesWithoutEvidence = changedLines
            .Where(static line => line.EvidenceLinks is null || line.EvidenceLinks.Count == 0 || line.EvidenceLinks.All(static link => string.IsNullOrWhiteSpace(link.EvidenceId)))
            .Select(static line => line.LineKey)
            .ToArray();
        if (linesWithoutEvidence.Length > 0)
        {
            throw new ArgumentException($"Restatement changed lines require evidence links: {string.Join(", ", linesWithoutEvidence)}.");
        }

        var transitioned = Transition(reportId, ReportPackWorkflowStateDto.Restated, actor, role, note: reasonCode);
        var evidenceLinks = changedLines
            .SelectMany(static line => line.EvidenceLinks ?? [])
            .GroupBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var next = transitioned with
        {
            Version = transitioned.Version + 1,
            Restatement = new ReportPackRestatementMetadataDto(reasonCode, approver, priorVersionReportId, changedLines, evidenceLinks)
        };
        SaveAndIndex(next);
        return next;
    }

    public ReportPackWorkflowRecordDto Publish(
        Guid reportId,
        string actor,
        string role,
        string signedOffBy,
        string evidenceHash,
        string manifestId,
        string retainedManifestPath,
        IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks,
        string? note = null,
        ReportBrandingThemeDto? brandingTheme = null,
        OperationsActionOriginDto actionOrigin = OperationsActionOriginDto.HumanOperator,
        string? signedOffRole = null,
        string? signOffReason = null,
        string? signOffContext = null)
    {
        EnsureHumanOrigin(actionOrigin, "publish reports");
        ArgumentException.ThrowIfNullOrWhiteSpace(signedOffBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedManifestPath);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("Publication requires retained evidence links.", nameof(evidenceLinks));
        }

        if (!_records.TryGetValue(reportId, out var record))
        {
            throw new KeyNotFoundException("report pack not found");
        }

        EnsureLineProvenanceTraceable(record.LineProvenance ?? []);
        EnsureNoOrphanEvidence(record.LineProvenance ?? [], evidenceLinks);
        var transitioned = TransitionCore(reportId, ReportPackWorkflowStateDto.Published, actor, role, note);
        var next = transitioned with
        {
            Publication = new ReportPackPublicationManifestDto(
                manifestId.Trim(),
                retainedManifestPath.Trim(),
                evidenceHash.Trim(),
                signedOffBy.Trim(),
                DateTimeOffset.UtcNow,
                NormalizeEvidenceLinks(evidenceLinks),
                NormalizePublicationBrandingTheme(brandingTheme),
                SignedOffRole: NormalizeOptional(signedOffRole) ?? role.Trim(),
                SignOffReason: NormalizeOptional(signOffReason) ?? NormalizeOptional(note),
                SignOffContext: NormalizeOptional(signOffContext) ?? BuildPublicationSignOffContext(actor, role, actionOrigin),
                ActionOrigin: actionOrigin)
        };
        SaveAndIndex(next);
        return next;
    }

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
        => OperationsOriginGuard.RequireHumanOperator(actionOrigin, action);

    private static void EnsureHumanOriginForMaterialTransition(
        ReportPackWorkflowStateDto target,
        OperationsActionOriginDto actionOrigin)
    {
        switch (target)
        {
            case ReportPackWorkflowStateDto.Approved:
                EnsureHumanOrigin(actionOrigin, "approve reports");
                break;
            case ReportPackWorkflowStateDto.Restated:
                EnsureHumanOrigin(actionOrigin, "restate reports");
                break;
            case ReportPackWorkflowStateDto.Archived:
                EnsureHumanOrigin(actionOrigin, "archive reports");
                break;
        }
    }

    public IReadOnlyList<ReportPackWorkflowRecordDto> GetHistory(string period, string fundAccountId) =>
        _records.Values.Where(x => x.Period == period && x.FundAccountId == fundAccountId).OrderByDescending(x => x.Version).ToArray();

    public ReportPackWorkflowRecordDto? GetRecord(Guid reportId) =>
        _records.TryGetValue(reportId, out var record) ? record : null;

    public IReadOnlyList<ReportPackWorkflowRecordDto> ListRecords(int limit = 25) =>
        _records.Values
            .OrderByDescending(static x => x.UpdatedAt)
            .ThenByDescending(static x => x.Version)
            .ThenBy(static x => x.ReportId)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();

    /// <summary>
    /// Every retained report-pack record scoped to a fund profile, newest period first. Unlike
    /// <see cref="ListRecords"/> this is intentionally uncapped: callers that locate restatement
    /// candidates for a closed-period reference-data edit must see the full set of the fund's published
    /// packs, since a silently dropped pack would be a missed restatement.
    /// </summary>
    public IReadOnlyList<ReportPackWorkflowRecordDto> ListRecordsForFundProfile(string fundProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var key = fundProfileId.Trim();
        return OrderFundRecords(
            _records.Values.Where(record => string.Equals(record.FundProfileId, key, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The report packs in a fund profile whose retained report-line provenance references
    /// <paramref name="securityId"/>. When a security→report-line index is wired this is an O(matches)
    /// lookup; otherwise it falls back to the full fund scan filtered by the shared matcher, preserving
    /// behaviour. Callers apply their own state gating (e.g. only Published/Restated packs).
    /// </summary>
    public IReadOnlyList<ReportPackWorkflowRecordDto> ListRecordsForSecurityInFund(Guid securityId, string fundProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var key = fundProfileId.Trim();

        if (_securityLineIndex is not null)
        {
            var matches = new List<ReportPackWorkflowRecordDto>();
            foreach (var entry in _securityLineIndex.LookupByFund(securityId, key))
            {
                if (_records.TryGetValue(entry.ReportId, out var record))
                {
                    matches.Add(record);
                }
            }

            return OrderFundRecords(matches);
        }

        return OrderFundRecords(
            _records.Values.Where(record =>
                string.Equals(record.FundProfileId, key, StringComparison.OrdinalIgnoreCase)
                && ReportPackSecurityLineMatcher.RecordReferencesSecurity(record, securityId)));
    }

    private static IReadOnlyList<ReportPackWorkflowRecordDto> OrderFundRecords(IEnumerable<ReportPackWorkflowRecordDto> records) =>
        records
            .OrderByDescending(static record => record.Period, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static record => record.Version)
            .ThenBy(static record => record.ReportId)
            .ToArray();

    private void SaveAndIndex(ReportPackWorkflowRecordDto record)
    {
        // Single chokepoint for every report-pack mutation. Update the authoritative record map and the
        // derived security→report-line index together BEFORE persisting, so the two in-memory views stay
        // consistent even if PersistRecords throws. (Persisting between them would, on a write failure,
        // leave the index permanently missing a record _records already has — a silent restatement
        // candidate miss, since the index-backed lookup no longer scans _records.) PersistRecords runs
        // last because it serializes the current _records snapshot.
        _records[record.ReportId] = record;
        _securityLineIndex?.Upsert(record);
        PersistRecords();
    }

    private void PersistRecords()
    {
        _store?.Save(_records.Values.ToArray());
    }

    private static void EnsureRole(ReportPackWorkflowStateDto target, string role)
    {
        var normalized = role.Trim().ToLowerInvariant();

        var allowed =
            target == ReportPackWorkflowStateDto.InReview ||
            target == ReportPackWorkflowStateDto.Validated ||
            target == ReportPackWorkflowStateDto.PendingApproval
                ? IsReportingReviewRole(normalized) || IsReportingOperationsRole(normalized)
                : target switch
                {
                    ReportPackWorkflowStateDto.Rejected => IsReportingReviewRole(normalized) || IsReportingApprovalRole(normalized),
                    ReportPackWorkflowStateDto.Approved => IsReportingApprovalRole(normalized),
                    ReportPackWorkflowStateDto.Published => IsReportingPublicationRole(normalized),
                    ReportPackWorkflowStateDto.Restated => IsReportingApprovalRole(normalized),
                    ReportPackWorkflowStateDto.Archived => normalized is "admin" or "records-manager",
                    _ => true
                };
        if (!allowed)
            throw new UnauthorizedAccessException($"Role '{role}' cannot transition to {target}.");
    }

    private static bool IsReportingOperationsRole(string normalized) =>
        normalized is "operator" or "validator" or "admin" or "accounting" or "fundaccountant" or "reportinganalyst" or "controller";

    private static bool IsReportingReviewRole(string normalized) =>
        normalized is "reviewer" or "admin" or "accounting" or "fundaccountant" or "reportinganalyst" or "controller" or "compliance";

    private static bool IsReportingApprovalRole(string normalized) =>
        normalized is "approver" or "admin" or "accounting" or "controller" or "compliance";

    private static bool IsReportingPublicationRole(string normalized) =>
        normalized is "publisher" or "admin" or "accounting" or "controller";

    private static string BuildPublicationSignOffContext(
        string actor,
        string role,
        OperationsActionOriginDto actionOrigin) =>
        $"Published by {actor.Trim()} as {role.Trim()} via {actionOrigin}.";

    private static IReadOnlyList<ReportPackLineProvenanceDto> NormalizeLineProvenance(IReadOnlyList<ReportPackLineProvenanceDto>? lineProvenance) =>
        lineProvenance?
            .Where(static item =>
                !string.IsNullOrWhiteSpace(item.LineKey) &&
                !string.IsNullOrWhiteSpace(item.SourceKind) &&
                !string.IsNullOrWhiteSpace(item.SourceId) &&
                !string.IsNullOrWhiteSpace(item.EvidenceId))
            .Select(NormalizeLineProvenanceItem)
            .ToArray() ?? [];

    private static ReportPackLineProvenanceDto NormalizeLineProvenanceItem(ReportPackLineProvenanceDto item)
    {
        var normalized = item with
        {
            LineKey = item.LineKey.Trim(),
            SourceKind = item.SourceKind.Trim(),
            SourceId = item.SourceId.Trim(),
            EvidenceId = item.EvidenceId.Trim(),
            RunId = NormalizeOptional(item.RunId),
            LedgerEntryId = NormalizeOptional(item.LedgerEntryId),
            ReconciliationCaseId = NormalizeOptional(item.ReconciliationCaseId),
            ReportValue = NormalizeOptional(item.ReportValue),
            SourceSessionId = NormalizeOptional(item.SourceSessionId),
            ReconciliationRunId = NormalizeOptional(item.ReconciliationRunId),
            ProviderEventId = NormalizeOptional(item.ProviderEventId),
            SecurityMasterId = NormalizeOptional(item.SecurityMasterId),
            SecurityDefinitionId = NormalizeOptional(item.SecurityDefinitionId),
            ReconciliationOutcome = NormalizeOptional(item.ReconciliationOutcome),
            ApprovalId = NormalizeOptional(item.ApprovalId),
            FinancialRecordExplorerId = NormalizeFinancialRecordExplorerId(item.FinancialRecordExplorerId),
            FinancialRecordHref = NormalizeOptional(item.FinancialRecordHref)
        };

        var explorerId = normalized.FinancialRecordExplorerId ?? ResolveFinancialRecordExplorerId(normalized);
        return normalized with
        {
            FinancialRecordExplorerId = explorerId,
            FinancialRecordHref = normalized.FinancialRecordHref ?? BuildFinancialRecordHref(explorerId, normalized)
        };
    }

    private static string ResolveFinancialRecordExplorerId(ReportPackLineProvenanceDto item)
    {
        if (ContainsToken(item.SourceKind, "portfolio") ||
            ContainsToken(item.LineKey, "position") ||
            ContainsToken(item.SourceId, "position"))
        {
            return PortfolioFinancialRecordExplorerId;
        }

        if (ContainsToken(item.SourceKind, "ledger") ||
            !string.IsNullOrWhiteSpace(item.LedgerEntryId) ||
            !string.IsNullOrWhiteSpace(item.RunId) ||
            !string.IsNullOrWhiteSpace(item.SourceSessionId) ||
            !string.IsNullOrWhiteSpace(item.ReconciliationRunId) ||
            !string.IsNullOrWhiteSpace(item.ReconciliationCaseId))
        {
            return LedgerFinancialRecordExplorerId;
        }

        if (!string.IsNullOrWhiteSpace(item.SecurityMasterId) ||
            !string.IsNullOrWhiteSpace(item.SecurityDefinitionId) ||
            ContainsToken(item.SourceKind, "security"))
        {
            return SecurityInstrumentFinancialRecordExplorerId;
        }

        return LedgerFinancialRecordExplorerId;
    }

    private static string? NormalizeFinancialRecordExplorerId(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Equals(PortfolioFinancialRecordExplorerId, StringComparison.OrdinalIgnoreCase)
            ? PortfolioFinancialRecordExplorerId
            : normalized.Equals(SecurityInstrumentFinancialRecordExplorerId, StringComparison.OrdinalIgnoreCase)
                ? SecurityInstrumentFinancialRecordExplorerId
                : LedgerFinancialRecordExplorerId;
    }

    private static string BuildFinancialRecordHref(string explorerId, ReportPackLineProvenanceDto line)
    {
        var route = UiApiRoutes.WithParam(
            UiApiRoutes.WorkstationFinancialRecordExplorer,
            "explorerId",
            explorerId);
        var queryParts = new List<string>
        {
            $"lineKey={Uri.EscapeDataString(line.LineKey)}",
            $"sourceId={Uri.EscapeDataString(line.SourceId)}",
            $"evidenceId={Uri.EscapeDataString(line.EvidenceId)}"
        };
        if (!string.IsNullOrWhiteSpace(line.RunId))
        {
            queryParts.Add($"runId={Uri.EscapeDataString(line.RunId)}");
        }

        return UiApiRoutes.WithQuery(route, string.Join("&", queryParts));
    }

    private static bool ContainsToken(string? value, string token) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ReportPackEvidenceLinkDto> NormalizeEvidenceLinks(IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks) =>
        evidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .GroupBy(static link => link.EvidenceId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var link = group.First();
                return link with
                {
                    EvidenceId = link.EvidenceId.Trim(),
                    Label = string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId.Trim() : link.Label.Trim(),
                    Route = string.IsNullOrWhiteSpace(link.Route) ? null : link.Route.Trim(),
                    Source = string.IsNullOrWhiteSpace(link.Source) ? "report-pack" : link.Source.Trim()
                };
            })
            .ToArray();

    private static ReportBrandingThemeDto? NormalizePublicationBrandingTheme(ReportBrandingThemeDto? theme)
    {
        if (theme is null)
        {
            return null;
        }

        return new ReportBrandingThemeDto(
            NormalizeRequired(theme.ThemeId, nameof(theme.ThemeId)),
            NormalizeRequired(theme.Name, nameof(theme.Name)),
            NormalizeRequired(theme.FirmName, nameof(theme.FirmName)),
            NormalizeRequired(theme.PrimaryColor, nameof(theme.PrimaryColor)),
            NormalizeRequired(theme.AccentColor, nameof(theme.AccentColor)),
            NormalizeRequired(theme.TextColor, nameof(theme.TextColor)),
            NormalizeRequired(theme.BackgroundColor, nameof(theme.BackgroundColor)),
            NormalizeOptional(theme.LogoUri),
            NormalizeOptional(theme.FooterText),
            NormalizeOptional(theme.Disclaimer),
            theme.IsBuiltIn);
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static void EnsureLineProvenanceTraceable(IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance)
    {
        var missingValues = lineProvenance
            .Where(static line => string.IsNullOrWhiteSpace(line.ReportValue))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingValues.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires report values for: {string.Join(", ", missingValues.Order(StringComparer.OrdinalIgnoreCase))}.");
        }

        var missingSourcePointers = lineProvenance
            .Where(static line =>
                string.IsNullOrWhiteSpace(line.RunId) &&
                string.IsNullOrWhiteSpace(line.SourceSessionId) &&
                string.IsNullOrWhiteSpace(line.LedgerEntryId) &&
                string.IsNullOrWhiteSpace(line.ReconciliationCaseId) &&
                string.IsNullOrWhiteSpace(line.ReconciliationRunId))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingSourcePointers.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires run, session, ledger, or reconciliation source pointers for: {string.Join(", ", missingSourcePointers.Order(StringComparer.OrdinalIgnoreCase))}.");
        }

        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.LedgerEntryId),
            "ledger entries");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.ProviderEventId),
            "provider events");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.SecurityMasterId) || !string.IsNullOrWhiteSpace(line.SecurityDefinitionId),
            "Security Master definitions");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => (!string.IsNullOrWhiteSpace(line.ReconciliationRunId) || !string.IsNullOrWhiteSpace(line.ReconciliationCaseId))
                && !string.IsNullOrWhiteSpace(line.ReconciliationOutcome),
            "reconciliation outcomes");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.ApprovalId),
            "approval references");
    }

    private static void EnsureLineProvenanceCategory(
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        Func<ReportPackLineProvenanceDto, bool> predicate,
        string category)
    {
        var missing = lineProvenance
            .Where(line => !predicate(line))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires {category} for: {string.Join(", ", missing.Order(StringComparer.OrdinalIgnoreCase))}.");
        }
    }

    private static void EnsureNoOrphanEvidence(
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks)
    {
        var retainedEvidenceIds = evidenceLinks
            .Select(static link => link.EvidenceId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingEvidence = lineProvenance
            .Select(static line => line.EvidenceId)
            .Where(evidenceId => !retainedEvidenceIds.Contains(evidenceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingEvidence.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack publication has orphan evidence: {string.Join(", ", missingEvidence.Order(StringComparer.OrdinalIgnoreCase))}.");
        }

        var missingPointers = lineProvenance
            .SelectMany(EnumerateRetainedProvenancePointers)
            .Where(pointer => !retainedEvidenceIds.Contains(pointer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingPointers.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack publication has orphan provenance pointers: {string.Join(", ", missingPointers.Order(StringComparer.OrdinalIgnoreCase))}.");
        }
    }

    private static IEnumerable<string> EnumerateRetainedProvenancePointers(ReportPackLineProvenanceDto line)
    {
        yield return line.SourceId;

        if (!string.IsNullOrWhiteSpace(line.RunId))
            yield return line.RunId;
        if (!string.IsNullOrWhiteSpace(line.SourceSessionId))
            yield return line.SourceSessionId;
        if (!string.IsNullOrWhiteSpace(line.LedgerEntryId))
            yield return line.LedgerEntryId;
        if (!string.IsNullOrWhiteSpace(line.ReconciliationCaseId))
            yield return line.ReconciliationCaseId;
        if (!string.IsNullOrWhiteSpace(line.ReconciliationRunId))
            yield return line.ReconciliationRunId;
        if (!string.IsNullOrWhiteSpace(line.ProviderEventId))
            yield return line.ProviderEventId;
        if (!string.IsNullOrWhiteSpace(line.SecurityMasterId))
            yield return line.SecurityMasterId;
        if (!string.IsNullOrWhiteSpace(line.SecurityDefinitionId))
            yield return line.SecurityDefinitionId;
        if (!string.IsNullOrWhiteSpace(line.ApprovalId))
            yield return line.ApprovalId;
    }
}
