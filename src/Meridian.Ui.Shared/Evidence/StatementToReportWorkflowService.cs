using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public sealed record StatementToReportStartCommand(
    StatementImportCommitRequest Import,
    string TenantId,
    string? CompanyId);

public sealed record StatementToReportWorkflowExecution(
    StatementImportCommitResultDto? ImportResult,
    StatementToReportWorkflowDto Workflow);

public sealed record StatementToReportArtifactDownload(
    StatementToReportArtifactDto Descriptor,
    byte[] Content);

/// <summary>
/// Durable golden-path coordinator from retained statement input through reconciliation evidence
/// to immutable statement/reconciliation report artifacts. It pauses rather than reporting success
/// while reconciliation breaks or cases remain open, and can resume from its last atomic checkpoint.
/// </summary>
public sealed class StatementToReportWorkflowService
{
    private const int SnapshotSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IStatementImportCommitService _imports;
    private readonly IStatementImportEvidenceRetainer _evidence;
    private readonly IStatementRunWorkflowService _statementRuns;
    private readonly string _dataRoot;
    private readonly string _workflowRoot;
    private readonly ILogger<StatementToReportWorkflowService>? _logger;

    public StatementToReportWorkflowService(
        IStatementImportCommitService imports,
        IStatementImportEvidenceRetainer evidence,
        IStatementRunWorkflowService statementRuns,
        string dataRoot,
        ILogger<StatementToReportWorkflowService>? logger = null)
    {
        _imports = imports ?? throw new ArgumentNullException(nameof(imports));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _statementRuns = statementRuns ?? throw new ArgumentNullException(nameof(statementRuns));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
        _workflowRoot = Path.Combine(_dataRoot, "reporting", "statement-to-report");
        _logger = logger;
    }

    public async Task<StatementToReportWorkflowExecution> StartAsync(
        StatementToReportStartCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);

        var workflowId = BuildWorkflowId(command);
        var directory = GetWorkflowDirectory(workflowId);
        Directory.CreateDirectory(directory);
        await using var ownership = await AcquireOwnershipAsync(directory, ct).ConfigureAwait(false);

        var snapshot = await ReadSnapshotAsync(directory, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            var inputPath = Path.Combine(directory, "input", SanitizeFileName(command.Import.Document.FileName));
            await AtomicFileWriter.WriteAsync(
                inputPath,
                command.Import.Document.Content.ToArray(),
                ct).ConfigureAwait(false);
            snapshot = CreateSnapshot(workflowId, command, inputPath);
            await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
        }
        else
        {
            EnsureScopeMatches(snapshot, command.TenantId, command.CompanyId);
        }

        return await ContinueAsync(directory, snapshot, ct).ConfigureAwait(false);
    }

    public async Task<StatementToReportWorkflowDto?> GetAsync(
        string workflowId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ValidateWorkflowLookup(workflowId, tenantId);
        var directory = GetWorkflowDirectory(workflowId);
        var snapshot = await ReadSnapshotAsync(directory, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        EnsureScopeMatches(snapshot, tenantId, companyId);
        return snapshot.Workflow;
    }

    public async Task<StatementToReportWorkflowExecution?> ResumeAsync(
        string workflowId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ValidateWorkflowLookup(workflowId, tenantId);
        var directory = GetWorkflowDirectory(workflowId);
        if (!Directory.Exists(directory))
            return null;

        await using var ownership = await AcquireOwnershipAsync(directory, ct).ConfigureAwait(false);
        var snapshot = await ReadSnapshotAsync(directory, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        EnsureScopeMatches(snapshot, tenantId, companyId);
        return await ContinueAsync(directory, snapshot, ct).ConfigureAwait(false);
    }

    public async Task<StatementToReportArtifactDownload?> DownloadArtifactAsync(
        string workflowId,
        string artifactId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        var workflow = await GetAsync(workflowId, tenantId, companyId, ct).ConfigureAwait(false);
        if (workflow is null)
            return null;
        var descriptor = workflow.RetainedArtifacts.FirstOrDefault(item =>
            string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal));
        if (descriptor is null)
            return null;

        var path = ResolveArtifactPath(workflowId, descriptor.ArtifactId);
        var content = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var actualHash = Convert.ToHexString(SHA256.HashData(content));
        if (!string.Equals(actualHash, descriptor.ContentHashSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Retained report artifact '{artifactId}' failed hash verification.");
        return new StatementToReportArtifactDownload(descriptor, content);
    }

    private async Task<StatementToReportWorkflowExecution> ContinueAsync(
        string directory,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        try
        {
            if (snapshot.Workflow.Status == StatementToReportWorkflowStatusDto.Completed)
                return RequireExecution(snapshot);

            if (snapshot.ImportResult is null)
            {
                snapshot = Advance(snapshot, StatementToReportWorkflowStatusDto.Importing,
                    recoveryAction: "Retry the persisted statement import.");
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
                var importRequest = BuildImportRequest(snapshot.Request);
                var imported = await _imports.CommitAsync(importRequest, ct).ConfigureAwait(false);
                snapshot = snapshot with { ImportResult = imported };
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
            }

            if (snapshot.ImportResult!.EvidenceVaultIdentity is null)
            {
                var retained = await _evidence.RetainAsync(
                    snapshot.ImportResult,
                    new StatementImportEvidenceBridgeRequest(
                        snapshot.Request.SourceKind,
                        snapshot.Request.SourceInstitution,
                        snapshot.Request.FundAccountId,
                        snapshot.Request.ExternalAccountId,
                        snapshot.Request.PeriodStart,
                        snapshot.Request.PeriodEnd,
                        snapshot.Request.ImportedBy),
                    ct).ConfigureAwait(false);
                snapshot = snapshot with { ImportResult = retained };
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
            }

            var reconciliation = await _statementRuns
                .GetAsync(snapshot.ImportResult.RunId, ct)
                .ConfigureAwait(false);
            var openBreaks = reconciliation?.Breaks.Count ?? snapshot.ImportResult.BreakCount;
            var openCases = reconciliation?.Cases.Count(IsOpenCase) ?? snapshot.ImportResult.CaseCount;
            if (openBreaks > 0 || openCases > 0)
            {
                snapshot = Advance(
                    snapshot,
                    StatementToReportWorkflowStatusDto.AwaitingReconciliation,
                    breakCount: openBreaks,
                    caseCount: openCases,
                    recoveryAction: "Resolve or disposition the linked reconciliation breaks and cases, then resume this workflow.");
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
                return RequireExecution(snapshot);
            }

            snapshot = Advance(snapshot, StatementToReportWorkflowStatusDto.RenderingReport,
                breakCount: 0, caseCount: 0,
                recoveryAction: "Retry report rendering from the retained statement and reconciliation evidence.");
            await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
            var artifacts = await RetainReportArtifactsAsync(directory, snapshot, reconciliation, ct).ConfigureAwait(false);
            snapshot = Complete(snapshot, artifacts);
            await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
            return RequireExecution(snapshot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failed = Fail(snapshot, ex);
            await SaveSnapshotAsync(directory, failed, CancellationToken.None).ConfigureAwait(false);
            _logger?.LogError(ex, "Statement-to-report workflow {WorkflowId} failed at status {Status}",
                snapshot.Workflow.WorkflowId, snapshot.Workflow.Status);
            return RequireExecution(failed);
        }
    }

    private async Task<IReadOnlyList<StatementToReportArtifactDto>> RetainReportArtifactsAsync(
        string directory,
        WorkflowSnapshot snapshot,
        StatementRunWorkflowResult? reconciliation,
        CancellationToken ct)
    {
        var import = snapshot.ImportResult!;
        var retainedAt = snapshot.Workflow.CreatedAtUtc;
        var evidenceReferences = BuildEvidenceReferences(import);
        var report = new StatementReconciliationReport(
            SchemaVersion: 1,
            WorkflowId: snapshot.Workflow.WorkflowId,
            StatementRunId: import.RunId,
            TenantId: snapshot.Workflow.TenantId,
            CompanyId: snapshot.Workflow.CompanyId,
            SourceInstitution: snapshot.Workflow.SourceInstitution,
            FundAccountId: snapshot.Workflow.FundAccountId,
            ExternalAccountId: snapshot.Workflow.ExternalAccountId,
            PeriodStart: snapshot.Workflow.PeriodStart,
            PeriodEnd: snapshot.Workflow.PeriodEnd,
            RecordCount: import.RecordCount,
            BreakCount: 0,
            CaseCount: 0,
            ReconciliationStatus: "Reconciled",
            KindSummaries: import.KindSummaries,
            EvidenceReferences: evidenceReferences,
            ReconciledCaseIds: import.CaseIds
                .Concat(reconciliation?.Cases.Select(static item => item.CaseId) ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray(),
            RetainedAtUtc: retainedAt);
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, JsonOptions));
        var csvBytes = Encoding.UTF8.GetBytes(RenderKindSummaryCsv(import));

        var artifactDirectory = Path.Combine(directory, "artifacts");
        Directory.CreateDirectory(artifactDirectory);
        var descriptors = new[]
        {
            await RetainArtifactAsync(snapshot.Workflow.WorkflowId, "reconciliation-report-json", "StatementReconciliationReport",
                "statement-reconciliation-report.json", "application/json", jsonBytes, retainedAt, ct),
            await RetainArtifactAsync(snapshot.Workflow.WorkflowId, "kind-summary-csv", "StatementKindSummary",
                "statement-kind-summary.csv", "text/csv", csvBytes, retainedAt, ct)
        };
        var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            snapshot.Workflow.WorkflowId,
            statementRunId = import.RunId,
            artifacts = descriptors,
            evidenceReferences
        }, JsonOptions));
        await AtomicFileWriter.WriteAsync(Path.Combine(artifactDirectory, "manifest.json"), manifestBytes, ct)
            .ConfigureAwait(false);
        return descriptors;
    }

    private async Task<StatementToReportArtifactDto> RetainArtifactAsync(
        string workflowId,
        string artifactId,
        string artifactKind,
        string fileName,
        string contentType,
        byte[] content,
        DateTimeOffset retainedAt,
        CancellationToken ct)
    {
        var path = ResolveArtifactPath(workflowId, artifactId);
        await AtomicFileWriter.WriteAsync(path, content, ct).ConfigureAwait(false);
        return new StatementToReportArtifactDto(
            artifactId,
            artifactKind,
            fileName,
            contentType,
            content.LongLength,
            Convert.ToHexString(SHA256.HashData(content)),
            UiApiRoutes.WithParam(
                UiApiRoutes.WithParam(
                    UiApiRoutes.ReconciliationStatementToReportArtifact,
                    "workflowId",
                    workflowId),
                "artifactId",
                artifactId),
            retainedAt);
    }

    private static string RenderKindSummaryCsv(StatementImportCommitResultDto import)
    {
        var builder = new StringBuilder("kind,recordCount\r\n");
        foreach (var summary in import.KindSummaries.OrderBy(static item => item.Kind, StringComparer.Ordinal))
        {
            builder.Append(EscapeCsv(summary.Kind))
                .Append(',')
                .Append(summary.RecordCount.ToString(CultureInfo.InvariantCulture))
                .Append("\r\n");
        }
        builder.Append("Total,").Append(import.RecordCount.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        return builder.ToString();
    }

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";

    private WorkflowSnapshot CreateSnapshot(
        string workflowId,
        StatementToReportStartCommand command,
        string inputPath)
    {
        var now = DateTimeOffset.UtcNow;
        var relativeInputPath = Path.GetRelativePath(_dataRoot, inputPath).Replace('\\', '/');
        var request = new PersistedRequest(
            relativeInputPath,
            command.Import.Document.FileName,
            command.Import.Document.MappingProfileId,
            command.Import.Document.ExternalAccountId,
            command.Import.ConnectorId,
            command.Import.SourceKind,
            command.Import.SourceInstitution,
            command.Import.FundAccountId,
            command.Import.ExternalAccountId,
            command.Import.PeriodStart,
            command.Import.PeriodEnd,
            command.Import.ToleranceProfileId,
            command.Import.ImportedBy);
        var workflow = new StatementToReportWorkflowDto(
            workflowId,
            StatementToReportWorkflowStatusDto.InputRetained,
            Version: 1,
            command.TenantId.Trim(),
            Normalize(command.CompanyId),
            command.Import.SourceInstitution.Trim(),
            command.Import.FundAccountId.Trim(),
            command.Import.ExternalAccountId.Trim(),
            command.Import.PeriodStart,
            command.Import.PeriodEnd,
            StatementRunId: null,
            EvidenceVaultIdentity: null,
            RetainedArtifacts: [],
            EvidenceReferences: [],
            BreakCount: 0,
            CaseCount: 0,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            CompletedAtUtc: null,
            FailureReason: null,
            RecoveryAction: "Retry the persisted statement import.",
            StatusRoute: BuildStatusRoute(workflowId),
            ResumeRoute: BuildResumeRoute(workflowId));
        return new WorkflowSnapshot(SnapshotSchemaVersion, request, workflow, ImportResult: null, ResumeStatus: workflow.Status);
    }

    private StatementImportCommitRequest BuildImportRequest(PersistedRequest request)
    {
        var inputPath = ResolveDataRootPath(request.RelativeInputPath);
        var content = File.ReadAllBytes(inputPath);
        return new StatementImportCommitRequest(
            new StatementSourceDocument(
                request.FileName,
                content,
                request.MappingProfileId,
                request.DocumentExternalAccountId),
            request.ConnectorId,
            request.SourceKind,
            request.SourceInstitution,
            request.FundAccountId,
            request.ExternalAccountId,
            request.PeriodStart,
            request.PeriodEnd,
            request.ToleranceProfileId,
            request.ImportedBy);
    }

    private static WorkflowSnapshot Advance(
        WorkflowSnapshot snapshot,
        StatementToReportWorkflowStatusDto status,
        int? breakCount = null,
        int? caseCount = null,
        string? recoveryAction = null)
    {
        var import = snapshot.ImportResult;
        var workflow = snapshot.Workflow with
        {
            Status = status,
            Version = snapshot.Workflow.Version + 1,
            StatementRunId = import?.RunId ?? snapshot.Workflow.StatementRunId,
            EvidenceVaultIdentity = import?.EvidenceVaultIdentity ?? snapshot.Workflow.EvidenceVaultIdentity,
            EvidenceReferences = import is null ? snapshot.Workflow.EvidenceReferences : BuildEvidenceReferences(import),
            BreakCount = breakCount ?? import?.BreakCount ?? snapshot.Workflow.BreakCount,
            CaseCount = caseCount ?? import?.CaseCount ?? snapshot.Workflow.CaseCount,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            FailureReason = null,
            RecoveryAction = recoveryAction
        };
        return snapshot with { Workflow = workflow, ResumeStatus = status };
    }

    private static WorkflowSnapshot Complete(
        WorkflowSnapshot snapshot,
        IReadOnlyList<StatementToReportArtifactDto> artifacts)
    {
        var now = DateTimeOffset.UtcNow;
        var import = snapshot.ImportResult!;
        return snapshot with
        {
            Workflow = snapshot.Workflow with
            {
                Status = StatementToReportWorkflowStatusDto.Completed,
                Version = snapshot.Workflow.Version + 1,
                StatementRunId = import.RunId,
                EvidenceVaultIdentity = import.EvidenceVaultIdentity,
                RetainedArtifacts = artifacts,
                EvidenceReferences = BuildEvidenceReferences(import)
                    .Concat(artifacts.Select(static item => $"artifact:{item.ArtifactId}:sha256:{item.ContentHashSha256}"))
                    .ToArray(),
                BreakCount = 0,
                CaseCount = 0,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                FailureReason = null,
                RecoveryAction = null
            },
            ResumeStatus = StatementToReportWorkflowStatusDto.Completed
        };
    }

    private static WorkflowSnapshot Fail(WorkflowSnapshot snapshot, Exception exception)
        => snapshot with
        {
            Workflow = snapshot.Workflow with
            {
                Status = StatementToReportWorkflowStatusDto.Failed,
                Version = snapshot.Workflow.Version + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                FailureReason = exception.Message,
                RecoveryAction = snapshot.ImportResult is null
                    ? "Retry the persisted statement import."
                    : "Resume from the retained import and evidence checkpoint."
            }
        };

    private static IReadOnlyList<string> BuildEvidenceReferences(StatementImportCommitResultDto import)
    {
        var evidence = new List<string>
        {
            $"statement-run:{import.RunId}",
            $"retained-source:{import.RetainedSourcePath}",
            $"retained-canonical:{import.RetainedCanonicalPath}"
        };
        if (import.EvidenceVaultIdentity is { } vault)
            evidence.Add($"evidence-vault:{vault.VaultId}");
        evidence.AddRange(import.BreakIds.Select(static id => $"reconciliation-break:{id}"));
        evidence.AddRange(import.CaseIds.Select(static id => $"reconciliation-case:{id}"));
        return evidence.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
    }

    private static bool IsOpenCase(ReconciliationCase item)
        => !string.Equals(item.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Closed", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Waived", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Superseded", StringComparison.OrdinalIgnoreCase);

    private static StatementToReportWorkflowExecution RequireExecution(WorkflowSnapshot snapshot)
        => new(
            snapshot.ImportResult,
            snapshot.Workflow);

    private static string BuildWorkflowId(StatementToReportStartCommand command)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(command.Import.Document.Content.Span));
        var identity = string.Join('|',
            command.TenantId.Trim(),
            Normalize(command.CompanyId),
            command.Import.SourceInstitution.Trim(),
            command.Import.FundAccountId.Trim(),
            command.Import.ExternalAccountId.Trim(),
            command.Import.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            command.Import.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            command.Import.SourceKind.Trim(),
            Normalize(command.Import.ConnectorId),
            Normalize(command.Import.Document.MappingProfileId),
            Normalize(command.Import.Document.ExternalAccountId),
            Normalize(command.Import.ToleranceProfileId),
            contentHash);
        return "statement-report-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32].ToLowerInvariant();
    }

    private string GetWorkflowDirectory(string workflowId)
    {
        if (!IsSafeWorkflowId(workflowId))
            throw new ArgumentException("Statement-to-report workflow id is invalid.", nameof(workflowId));
        return Path.Combine(_workflowRoot, workflowId);
    }

    private string ResolveArtifactPath(string workflowId, string artifactId)
    {
        var fileName = artifactId switch
        {
            "reconciliation-report-json" => "statement-reconciliation-report.json",
            "kind-summary-csv" => "statement-kind-summary.csv",
            _ => throw new ArgumentException("Unknown statement-to-report artifact id.", nameof(artifactId))
        };
        return Path.Combine(GetWorkflowDirectory(workflowId), "artifacts", fileName);
    }

    private string ResolveDataRootPath(string relativePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(_dataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = _dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Persisted statement input escaped the configured data root.");
        return candidate;
    }

    private static async Task<FileStream> AcquireOwnershipAsync(string directory, CancellationToken ct)
    {
        var lockPath = Path.Combine(directory, "workflow.lock");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new TimeoutException("Another process owns this statement-to-report workflow.", ex);
            }
        }
    }

    private static async Task<WorkflowSnapshot?> ReadSnapshotAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, "workflow.json");
        if (!File.Exists(path))
            return null;
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var snapshot = JsonSerializer.Deserialize<WorkflowSnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException("Statement-to-report workflow snapshot is empty.");
        if (snapshot.SchemaVersion != SnapshotSchemaVersion)
            throw new InvalidDataException($"Unsupported statement-to-report snapshot schema {snapshot.SchemaVersion}.");
        return snapshot;
    }

    private static Task SaveSnapshotAsync(string directory, WorkflowSnapshot snapshot, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(
            Path.Combine(directory, "workflow.json"),
            JsonSerializer.Serialize(snapshot, JsonOptions),
            ct);

    private static void ValidateCommand(StatementToReportStartCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.Import);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.SourceInstitution);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.FundAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.ExternalAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.ImportedBy);
        if (command.Import.Document.Content.IsEmpty)
            throw new ArgumentException("Statement-to-report workflow requires a non-empty statement document.", nameof(command));
        if (command.Import.PeriodEnd < command.Import.PeriodStart)
            throw new ArgumentException("Statement period end must be on or after period start.", nameof(command));
    }

    private static void ValidateWorkflowLookup(string workflowId, string tenantId)
    {
        if (!IsSafeWorkflowId(workflowId))
            throw new ArgumentException("Statement-to-report workflow id is invalid.", nameof(workflowId));
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
    }

    private static bool IsSafeWorkflowId(string workflowId)
        => !string.IsNullOrWhiteSpace(workflowId)
           && workflowId.StartsWith("statement-report-", StringComparison.Ordinal)
           && workflowId.Length == "statement-report-".Length + 32
           && workflowId["statement-report-".Length..].All(Uri.IsHexDigit);

    private static void EnsureScopeMatches(WorkflowSnapshot snapshot, string tenantId, string? companyId)
    {
        if (!string.Equals(snapshot.Workflow.TenantId, tenantId.Trim(), StringComparison.Ordinal)
            || !string.Equals(snapshot.Workflow.CompanyId, Normalize(companyId), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Statement-to-report workflow belongs to another tenant or company scope.");
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            name = "statement.bin";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name;
    }

    private static string BuildStatusRoute(string workflowId)
        => UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementToReportById, "workflowId", workflowId);

    private static string BuildResumeRoute(string workflowId)
        => UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementToReportResume, "workflowId", workflowId);

    private sealed record WorkflowSnapshot(
        int SchemaVersion,
        PersistedRequest Request,
        StatementToReportWorkflowDto Workflow,
        StatementImportCommitResultDto? ImportResult,
        StatementToReportWorkflowStatusDto ResumeStatus);

    private sealed record PersistedRequest(
        string RelativeInputPath,
        string FileName,
        string? MappingProfileId,
        string? DocumentExternalAccountId,
        string? ConnectorId,
        string SourceKind,
        string SourceInstitution,
        string FundAccountId,
        string ExternalAccountId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string? ToleranceProfileId,
        string ImportedBy);

    private sealed record StatementReconciliationReport(
        int SchemaVersion,
        string WorkflowId,
        string StatementRunId,
        string TenantId,
        string? CompanyId,
        string SourceInstitution,
        string FundAccountId,
        string ExternalAccountId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int RecordCount,
        int BreakCount,
        int CaseCount,
        string ReconciliationStatus,
        IReadOnlyList<StatementKindSummaryDto> KindSummaries,
        IReadOnlyList<string> EvidenceReferences,
        IReadOnlyList<string> ReconciledCaseIds,
        DateTimeOffset RetainedAtUtc);
}
