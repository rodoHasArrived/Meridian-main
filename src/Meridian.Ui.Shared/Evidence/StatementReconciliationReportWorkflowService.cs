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
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public sealed record StatementReconciliationReportStartCommand(
    StatementImportCommitRequest Import,
    string TenantId,
    string? CompanyId);

public sealed record StatementReconciliationReportWorkflowExecution(
    StatementImportCommitResultDto? ImportResult,
    StatementReconciliationReportWorkflowDto Workflow);

public sealed record StatementReconciliationReportArtifactDownload(
    StatementReconciliationReportArtifactDto Descriptor,
    byte[] Content);

/// <summary>
/// Durable golden-path coordinator from retained statement input through reconciliation evidence
/// to immutable statement/reconciliation report artifacts. It pauses rather than reporting success
/// while reconciliation breaks or cases remain open, and can resume from its last atomic checkpoint.
/// </summary>
public sealed class StatementReconciliationReportWorkflowService
{
    private const int SnapshotSchemaVersion = 1;
    private const string WorkflowIdPrefix = "statement-reconciliation-report-";
    private const string LegacyWorkflowIdPrefix = "statement-report-";
    private const string WorkflowDirectoryName = "statement-reconciliation-report";
    private const string LegacyWorkflowDirectoryName = "statement-to-report";
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
    private readonly string _legacyWorkflowRoot;
    private readonly ILogger<StatementReconciliationReportWorkflowService>? _logger;
    private readonly IReconciliationBreakQueueRepository? _breakQueue;
    private readonly IStatementReconciliationIntakeAuthority? _intakeAuthority;

    public StatementReconciliationReportWorkflowService(
        IStatementImportCommitService imports,
        IStatementImportEvidenceRetainer evidence,
        IStatementRunWorkflowService statementRuns,
        string dataRoot,
        ILogger<StatementReconciliationReportWorkflowService>? logger = null)
        : this(
            imports,
            evidence,
            statementRuns,
            dataRoot,
            logger,
            breakQueue: null,
            intakeAuthority: null)
    {
    }

    public StatementReconciliationReportWorkflowService(
        IStatementImportCommitService imports,
        IStatementImportEvidenceRetainer evidence,
        IStatementRunWorkflowService statementRuns,
        string dataRoot,
        ILogger<StatementReconciliationReportWorkflowService>? logger,
        IReconciliationBreakQueueRepository? breakQueue)
        : this(
            imports,
            evidence,
            statementRuns,
            dataRoot,
            logger,
            breakQueue,
            intakeAuthority: null)
    {
    }

    public StatementReconciliationReportWorkflowService(
        IStatementImportCommitService imports,
        IStatementImportEvidenceRetainer evidence,
        IStatementRunWorkflowService statementRuns,
        string dataRoot,
        ILogger<StatementReconciliationReportWorkflowService>? logger,
        IReconciliationBreakQueueRepository? breakQueue,
        IStatementReconciliationIntakeAuthority? intakeAuthority)
    {
        _imports = imports ?? throw new ArgumentNullException(nameof(imports));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _statementRuns = statementRuns ?? throw new ArgumentNullException(nameof(statementRuns));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
        _workflowRoot = Path.Combine(_dataRoot, "reporting", WorkflowDirectoryName);
        _legacyWorkflowRoot = Path.Combine(_dataRoot, "reporting", LegacyWorkflowDirectoryName);
        _logger = logger;
        _breakQueue = breakQueue;
        _intakeAuthority = intakeAuthority;
    }

    public async Task<StatementReconciliationReportWorkflowExecution> StartAsync(
        StatementReconciliationReportStartCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);
        var intakeAuthority = RequireIntakeAuthority();
        var preScopeCommand = command with
        {
            Import = command.Import with { AccountingScope = null }
        };
        var retainedBeforeResolution = BuildWorkflowLocations(command, preScopeCommand)
            .Where(static location =>
                File.Exists(Path.Combine(location.Directory, "workflow.json")))
            .ToArray();
        EnsureSingleRetainedAuthority(retainedBeforeResolution);
        var retainedCanRevalidateAfterClose = false;
        if (retainedBeforeResolution.Length == 1)
        {
            var retainedSnapshot = await ReadSnapshotAsync(
                    retainedBeforeResolution[0].Directory,
                    ct)
                .ConfigureAwait(false);
            retainedCanRevalidateAfterClose =
                retainedSnapshot?.Workflow.OperationsWorkflowId.HasValue == true
                || retainedSnapshot?.Workflow.Status
                    == StatementReconciliationReportWorkflowStatusDto.Completed;
        }

        var accountingScope = await intakeAuthority
            .ResolveAccountingScopeAsync(
                new StatementReconciliationIntakeScopeRequest(
                    command.TenantId,
                    command.CompanyId
                    ?? throw new UnauthorizedAccessException(
                        "Statement-to-close intake requires a company scope."),
                    command.Import.FundAccountId,
                    command.Import.ExternalAccountId,
                    command.Import.SourceInstitution,
                    command.Import.PeriodStart,
                    command.Import.PeriodEnd,
                    command.Import.AccountingScope)
                {
                    AllowClosedPeriodForRetainedWorkflow =
                        retainedCanRevalidateAfterClose
                },
                ct)
            .ConfigureAwait(false);
        command = command with
        {
            Import = command.Import with { AccountingScope = accountingScope }
        };

        var locations = BuildWorkflowLocations(command, preScopeCommand);
        var retainedLocations = locations
            .Where(static location =>
                File.Exists(Path.Combine(location.Directory, "workflow.json")))
            .ToArray();
        EnsureSingleRetainedAuthority(retainedLocations);

        var location = retainedLocations.SingleOrDefault() ?? locations[0];
        var workflowId = location.WorkflowId;
        var directory = location.Directory;
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
            var migratedSnapshot = NormalizeRetainedInputLocation(snapshot, directory);
            await EnsureRequestIdentityMatchesAsync(migratedSnapshot, preScopeCommand, ct).ConfigureAwait(false);
            var scopeBoundSnapshot = BindResolvedAccountingScope(
                migratedSnapshot,
                command.Import.AccountingScope);
            if (!ReferenceEquals(scopeBoundSnapshot, snapshot))
            {
                snapshot = scopeBoundSnapshot;
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
            }
        }

        return await ContinueAsync(directory, snapshot, ct).ConfigureAwait(false);
    }

    public async Task<StatementReconciliationReportWorkflowDto?> GetAsync(
        string workflowId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ValidateWorkflowLookup(workflowId, tenantId);
        _ = RequireIntakeAuthority();
        var directory = GetWorkflowDirectory(workflowId);
        var snapshot = await ReadSnapshotAsync(directory, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        EnsureScopeMatches(snapshot, tenantId, companyId);
        if (snapshot.Workflow.Status == StatementReconciliationReportWorkflowStatusDto.Completed)
        {
            EnsureAuthoritativeIntake(snapshot);
        }
        return snapshot.Workflow;
    }

    public async Task<StatementReconciliationReportWorkflowExecution?> ResumeAsync(
        string workflowId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ValidateWorkflowLookup(workflowId, tenantId);
        var intakeAuthority = RequireIntakeAuthority();
        var directory = GetWorkflowDirectory(workflowId);
        if (!Directory.Exists(directory))
            return null;

        await using var ownership = await AcquireOwnershipAsync(directory, ct).ConfigureAwait(false);
        var snapshot = await ReadSnapshotAsync(directory, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        EnsureScopeMatches(snapshot, tenantId, companyId);
        var authorizedScope = await intakeAuthority
            .ResolveAccountingScopeAsync(
                new StatementReconciliationIntakeScopeRequest(
                    tenantId,
                    companyId
                    ?? throw new UnauthorizedAccessException(
                        "Statement-to-close resume requires a company scope."),
                    snapshot.Request.FundAccountId,
                    snapshot.Request.ExternalAccountId,
                    snapshot.Request.SourceInstitution,
                    snapshot.Request.PeriodStart,
                    snapshot.Request.PeriodEnd,
                    snapshot.Request.AccountingScope)
                {
                    AllowClosedPeriodForRetainedWorkflow =
                        snapshot.Workflow.OperationsWorkflowId.HasValue
                        || snapshot.Workflow.Status
                            == StatementReconciliationReportWorkflowStatusDto.Completed
                },
                ct)
            .ConfigureAwait(false);
        var authorizedSnapshot = BindResolvedAccountingScope(snapshot, authorizedScope);
        if (!ReferenceEquals(authorizedSnapshot, snapshot))
        {
            snapshot = authorizedSnapshot;
            await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
        }
        return await ContinueAsync(directory, snapshot, ct).ConfigureAwait(false);
    }

    public async Task<StatementReconciliationReportArtifactDownload?> DownloadArtifactAsync(
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
            throw new InvalidDataException($"Retained statement reconciliation report artifact '{artifactId}' failed hash verification.");
        return new StatementReconciliationReportArtifactDownload(descriptor, content);
    }

    private async Task<StatementReconciliationReportWorkflowExecution> ContinueAsync(
        string directory,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        var intakeAuthority = RequireIntakeAuthority();
        try
        {
            if (snapshot.Workflow.Status == StatementReconciliationReportWorkflowStatusDto.Completed
                && HasAuthoritativeIntake(snapshot))
            {
                return RequireExecution(snapshot);
            }

            if (snapshot.ImportResult is null)
            {
                snapshot = Advance(snapshot, StatementReconciliationReportWorkflowStatusDto.Importing,
                    recoveryAction: "Retry the persisted statement import.");
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
                var importRequest = BuildImportRequest(snapshot);
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

            if (!snapshot.Workflow.OperationsWorkflowId.HasValue
                || snapshot.Workflow.OperationsWorkflowId.Value == Guid.Empty)
            {
                var accountingScope = snapshot.Request.AccountingScope
                    ?? throw new StatementReconciliationIntakeAuthorityException(
                        "STATEMENT_ACCOUNTING_SCOPE_MISSING",
                        "Statement reconciliation report processing is blocked because authoritative accounting scope was not retained.");
                var intake = await intakeAuthority
                    .PublishAsync(
                        snapshot.Workflow.WorkflowId,
                        snapshot.ImportResult,
                        accountingScope,
                        snapshot.Workflow.TenantId,
                        snapshot.Workflow.CompanyId
                        ?? throw new UnauthorizedAccessException(
                            "Statement-to-close publication requires a company scope."),
                        snapshot.Request.ImportedBy,
                        snapshot.Request.SourceInstitution,
                        BuildEvidenceReferences(snapshot.ImportResult),
                        ct)
                    .ConfigureAwait(false);
                ValidateIntakeReceipt(accountingScope, intake);
                snapshot = snapshot with
                {
                    Workflow = snapshot.Workflow with
                    {
                        AccountingScope = ToDto(intake.AccountingScope),
                        OperationsWorkflowId = intake.OperationsWorkflowId,
                        EvidenceReferences = snapshot.Workflow.EvidenceReferences
                            .Concat(intake.EvidenceReferences)
                            .Append($"operations-workflow:{intake.OperationsWorkflowId:D}")
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(static item => item, StringComparer.Ordinal)
                            .ToArray(),
                        Version = snapshot.Workflow.Version + 1,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    }
                };
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
                    StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation,
                    breakCount: openBreaks,
                    caseCount: openCases,
                    recoveryAction: "Resolve or disposition the linked reconciliation breaks and cases, then resume this workflow.");
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
                return RequireExecution(snapshot);
            }

            var queueGate = await EvaluateCanonicalQueueHandoffAsync(
                    snapshot.ImportResult,
                    ct)
                .ConfigureAwait(false);
            if (!queueGate.IsSatisfied)
            {
                snapshot = Advance(
                    snapshot,
                    StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation,
                    breakCount: queueGate.OpenCaseCount,
                    caseCount: queueGate.BlockingCaseCount,
                    recoveryAction: queueGate.RecoveryAction);
                await SaveSnapshotAsync(directory, snapshot, ct).ConfigureAwait(false);
                return RequireExecution(snapshot);
            }

            EnsureAuthoritativeIntake(snapshot);
            snapshot = Advance(snapshot, StatementReconciliationReportWorkflowStatusDto.RenderingReconciliationReport,
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
            _logger?.LogError(ex, "Statement reconciliation report workflow {WorkflowId} failed at status {Status}",
                snapshot.Workflow.WorkflowId, snapshot.Workflow.Status);
            return RequireExecution(failed);
        }
    }

    private IStatementReconciliationIntakeAuthority RequireIntakeAuthority()
        => _intakeAuthority
           ?? throw new StatementReconciliationIntakeAuthorityException(
               "STATEMENT_INTAKE_AUTHORITY_UNAVAILABLE",
               "Statement reconciliation report processing is unavailable because the authoritative statement intake service is not configured. No input, evidence, reconciliation report, or completion record was retained.");

    private static void ValidateIntakeReceipt(
        StatementAccountingScope expectedScope,
        StatementReconciliationIntakeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.OperationsWorkflowId == Guid.Empty)
        {
            throw new StatementReconciliationIntakeAuthorityException(
                "STATEMENT_OPERATIONS_PUBLICATION_MISSING",
                "Statement reconciliation report processing is blocked because intake did not retain an Operations Continuity workflow.");
        }

        if (!AccountingScopeMatches(expectedScope, receipt.AccountingScope))
        {
            throw new StatementReconciliationIntakeAuthorityException(
                "STATEMENT_ACCOUNTING_SCOPE_CONFLICT",
                "Statement reconciliation report processing is blocked because the intake receipt does not match the resolved accounting scope.");
        }
    }

    private static bool HasAuthoritativeIntake(WorkflowSnapshot snapshot)
    {
        var requestScope = snapshot.Request.AccountingScope;
        var workflowScope = snapshot.Workflow.AccountingScope;
        var operationsWorkflowId = snapshot.Workflow.OperationsWorkflowId;
        return snapshot.ImportResult?.EvidenceVaultIdentity is not null
               && requestScope is not null
               && workflowScope is not null
               && AccountingScopeMatches(workflowScope, requestScope)
               && operationsWorkflowId.HasValue
               && operationsWorkflowId.Value != Guid.Empty
               && snapshot.Workflow.EvidenceReferences.Contains(
                   $"operations-workflow:{operationsWorkflowId.Value:D}",
                   StringComparer.Ordinal);
    }

    private static void EnsureAuthoritativeIntake(WorkflowSnapshot snapshot)
    {
        if (!HasAuthoritativeIntake(snapshot))
        {
            throw new StatementReconciliationIntakeAuthorityException(
                "STATEMENT_INTAKE_PUBLICATION_INCOMPLETE",
                "Statement reconciliation report rendering is blocked until retained Evidence Vault identity, exact accounting scope, and Operations Continuity publication are all present.");
        }
    }

    private async Task<CanonicalQueueHandoffGate> EvaluateCanonicalQueueHandoffAsync(
        StatementImportCommitResultDto import,
        CancellationToken ct)
    {
        if (import.CaseCount <= 0 && import.BreakCount <= 0)
        {
            return CanonicalQueueHandoffGate.Satisfied;
        }

        if (_breakQueue is null)
        {
            return CanonicalQueueHandoffGate.Blocked(
                Math.Max(import.CaseCount, import.BreakCount),
                "The canonical reconciliation queue is unavailable. Restore it, complete every retained statement casework handoff, then resume this workflow.");
        }

        var items = (await _breakQueue.GetAllAsync(ct: ct).ConfigureAwait(false))
            .Where(item =>
                string.Equals(item.SourceType, "statement", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.SourceImportId, import.RunId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .ToArray();
        var expectedCount = Math.Max(import.CaseCount, import.BreakCount);
        var expectedSourceBreakIds = import.BreakIds
            .Concat(import.ReconciliationCaseLinks
                .Select(static link => link.BreakId)
                .Where(static breakId => !string.IsNullOrWhiteSpace(breakId))
                .Select(static breakId => breakId!))
            .Where(static breakId => !string.IsNullOrWhiteSpace(breakId))
            .Select(static breakId => breakId.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static breakId => breakId, StringComparer.Ordinal)
            .ToArray();
        if (expectedSourceBreakIds.Length != expectedCount)
        {
            return CanonicalQueueHandoffGate.Blocked(
                expectedCount,
                $"Statement import '{import.RunId}' declares {expectedCount} casework obligation(s) but retains {expectedSourceBreakIds.Length} unique source-break identities. Exact obligation identity is required before rendering a Reconciled report.");
        }

        var itemsBySourceBreak = items
            .Where(static item => !string.IsNullOrWhiteSpace(item.SourceBreakId))
            .GroupBy(static item => item.SourceBreakId!.Trim(), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var missing = expectedSourceBreakIds
            .Where(breakId => !itemsBySourceBreak.TryGetValue(breakId, out var matches)
                || matches.Length != 1)
            .ToArray();
        var unexpected = itemsBySourceBreak.Keys
            .Except(expectedSourceBreakIds, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0 || unexpected.Length > 0 || items.Length != expectedCount)
        {
            return CanonicalQueueHandoffGate.Blocked(
                Math.Max(1, missing.Length + unexpected.Length),
                $"The canonical queue does not exactly match statement import '{import.RunId}' obligations. Missing or duplicate identities: {string.Join(", ", missing.DefaultIfEmpty("none"))}; unexpected identities: {string.Join(", ", unexpected.DefaultIfEmpty("none"))}. Publish and complete the exact retained obligations before rendering a Reconciled report.");
        }

        var open = items.Count(static item =>
            item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview
            || item.LifecycleState is ReconciliationCaseLifecycleState.Open
                or ReconciliationCaseLifecycleState.InReview
                or ReconciliationCaseLifecycleState.Investigating
                or ReconciliationCaseLifecycleState.AwaitingEvidence
                or ReconciliationCaseLifecycleState.Reopened);
        var incompleteHandoffs = items.Count(static item =>
            StatementCaseworkHandoffObligation.HasPending(item)
            || !StatementCaseworkHandoffObligation.HasCompleted(item)
            || string.IsNullOrWhiteSpace(item.FundProfileId)
            || !item.LedgerBookId.HasValue
            || item.LedgerBookId.Value == Guid.Empty
            || !Guid.TryParse(item.AccountingPeriodId, out var accountingPeriodId)
            || accountingPeriodId == Guid.Empty
            || !item.AsOfDate.HasValue
            || item.AsOfDate.Value == default);
        if (open > 0 || incompleteHandoffs > 0)
        {
            return new CanonicalQueueHandoffGate(
                false,
                open,
                Math.Max(open, incompleteHandoffs),
                $"{open} statement case(s) remain open and {incompleteHandoffs} canonical queue handoff obligation(s) are incomplete for import '{import.RunId}'. Complete the queue-owned source/Operations handoff and exact accounting scope, then resume this workflow.");
        }

        return CanonicalQueueHandoffGate.Satisfied;
    }

    private sealed record CanonicalQueueHandoffGate(
        bool IsSatisfied,
        int OpenCaseCount,
        int BlockingCaseCount,
        string RecoveryAction)
    {
        public static CanonicalQueueHandoffGate Satisfied { get; } =
            new(true, 0, 0, string.Empty);

        public static CanonicalQueueHandoffGate Blocked(int count, string recoveryAction) =>
            new(false, count, count, recoveryAction);
    }

    private async Task<IReadOnlyList<StatementReconciliationReportArtifactDto>> RetainReportArtifactsAsync(
        string directory,
        WorkflowSnapshot snapshot,
        StatementRunWorkflowResult? reconciliation,
        CancellationToken ct)
    {
        var import = snapshot.ImportResult!;
        var retainedAt = snapshot.Workflow.CreatedAtUtc;
        var evidenceReferences = snapshot.Workflow.EvidenceReferences
            .Concat(BuildEvidenceReferences(import))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
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

    private async Task<StatementReconciliationReportArtifactDto> RetainArtifactAsync(
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
        return new StatementReconciliationReportArtifactDto(
            artifactId,
            artifactKind,
            fileName,
            contentType,
            content.LongLength,
            Convert.ToHexString(SHA256.HashData(content)),
            UiApiRoutes.WithParam(
                UiApiRoutes.WithParam(
                    UiApiRoutes.ReconciliationStatementReconciliationReportArtifact,
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
        StatementReconciliationReportStartCommand command,
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
            command.Import.ImportedBy,
            command.Import.AccountingScope);
        var workflow = new StatementReconciliationReportWorkflowDto(
            workflowId,
            StatementReconciliationReportWorkflowStatusDto.InputRetained,
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
            ResumeRoute: BuildResumeRoute(workflowId))
        {
            AccountingScope = command.Import.AccountingScope is null
                ? null
                : ToDto(command.Import.AccountingScope)
        };
        return new WorkflowSnapshot(SnapshotSchemaVersion, request, workflow, ImportResult: null, ResumeStatus: workflow.Status);
    }

    private StatementImportCommitRequest BuildImportRequest(WorkflowSnapshot snapshot)
    {
        var request = snapshot.Request;
        var inputPath = ResolveDataRootPath(request.RelativeInputPath);
        var content = File.ReadAllBytes(inputPath);
        var importRequest = new StatementImportCommitRequest(
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
            request.ImportedBy)
        {
            AccountingScope = request.AccountingScope
        };
        var command = new StatementReconciliationReportStartCommand(
            importRequest,
            snapshot.Workflow.TenantId,
            snapshot.Workflow.CompanyId);
        var preScopeCommand = command with
        {
            Import = command.Import with { AccountingScope = null }
        };
        var validWorkflowIds = new[]
        {
            BuildWorkflowId(command, WorkflowIdPrefix),
            BuildWorkflowId(command, LegacyWorkflowIdPrefix),
            BuildWorkflowId(preScopeCommand, WorkflowIdPrefix),
            BuildWorkflowId(preScopeCommand, LegacyWorkflowIdPrefix)
        };
        if (!validWorkflowIds.Contains(
                snapshot.Workflow.WorkflowId,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' retained input bytes no longer match its content-addressed identity.");
        }

        return importRequest;
    }

    private static WorkflowSnapshot Advance(
        WorkflowSnapshot snapshot,
        StatementReconciliationReportWorkflowStatusDto status,
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
            EvidenceReferences = import is null
                ? snapshot.Workflow.EvidenceReferences
                : snapshot.Workflow.EvidenceReferences
                    .Concat(BuildEvidenceReferences(import))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
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
        IReadOnlyList<StatementReconciliationReportArtifactDto> artifacts)
    {
        var now = DateTimeOffset.UtcNow;
        var import = snapshot.ImportResult!;
        return snapshot with
        {
            Workflow = snapshot.Workflow with
            {
                Status = StatementReconciliationReportWorkflowStatusDto.Completed,
                Version = snapshot.Workflow.Version + 1,
                StatementRunId = import.RunId,
                EvidenceVaultIdentity = import.EvidenceVaultIdentity,
                RetainedArtifacts = artifacts,
                EvidenceReferences = snapshot.Workflow.EvidenceReferences
                    .Concat(BuildEvidenceReferences(import))
                    .Concat(artifacts.Select(static item => $"artifact:{item.ArtifactId}:sha256:{item.ContentHashSha256}"))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                BreakCount = 0,
                CaseCount = 0,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                FailureReason = null,
                RecoveryAction = null
            },
            ResumeStatus = StatementReconciliationReportWorkflowStatusDto.Completed
        };
    }

    private static WorkflowSnapshot Fail(WorkflowSnapshot snapshot, Exception exception)
        => snapshot with
        {
            Workflow = snapshot.Workflow with
            {
                Status = StatementReconciliationReportWorkflowStatusDto.Failed,
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
           && !string.Equals(item.Status, "Superseded", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "Dismissed", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(item.Status, "SignedOff", StringComparison.OrdinalIgnoreCase);

    private static StatementReconciliationReportWorkflowExecution RequireExecution(WorkflowSnapshot snapshot)
        => new(
            snapshot.ImportResult,
            snapshot.Workflow);

    private static string BuildWorkflowId(StatementReconciliationReportStartCommand command, string prefix)
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
        return prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32].ToLowerInvariant();
    }

    private IReadOnlyList<WorkflowLocation> BuildWorkflowLocations(
        StatementReconciliationReportStartCommand scopedCommand,
        StatementReconciliationReportStartCommand preScopeCommand)
    {
        var locations = new[]
        {
            BuildWorkflowLocation(scopedCommand, WorkflowIdPrefix),
            BuildWorkflowLocation(scopedCommand, LegacyWorkflowIdPrefix),
            BuildWorkflowLocation(preScopeCommand, WorkflowIdPrefix),
            BuildWorkflowLocation(preScopeCommand, LegacyWorkflowIdPrefix)
        };
        return locations
            .DistinctBy(static location => location.Directory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureSingleRetainedAuthority(
        IReadOnlyCollection<WorkflowLocation> retainedLocations)
    {
        if (retainedLocations.Count > 1)
        {
            throw new InvalidDataException(
                "More than one statement reconciliation report workflow snapshot matches the same retained input. Resolve the duplicate workflow authority before retrying.");
        }
    }

    private WorkflowLocation BuildWorkflowLocation(
        StatementReconciliationReportStartCommand command,
        string prefix)
    {
        var workflowId = BuildWorkflowId(command, prefix);
        return new WorkflowLocation(workflowId, GetWorkflowDirectory(workflowId));
    }

    private string GetWorkflowDirectory(string workflowId)
    {
        if (!IsSafeWorkflowId(workflowId))
            throw new ArgumentException("Statement reconciliation report workflow id is invalid.", nameof(workflowId));
        var root = workflowId.StartsWith(LegacyWorkflowIdPrefix, StringComparison.Ordinal)
            ? _legacyWorkflowRoot
            : _workflowRoot;
        return Path.Combine(root, workflowId);
    }

    private string ResolveArtifactPath(string workflowId, string artifactId)
    {
        var fileName = artifactId switch
        {
            "reconciliation-report-json" => "statement-reconciliation-report.json",
            "kind-summary-csv" => "statement-kind-summary.csv",
            _ => throw new ArgumentException("Unknown statement reconciliation report artifact id.", nameof(artifactId))
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
                throw new TimeoutException("Another process owns this statement reconciliation report workflow.", ex);
            }
        }
    }

    private static async Task<WorkflowSnapshot?> ReadSnapshotAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, "workflow.json");
        if (!File.Exists(path))
            return null;
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        json = json.Replace(
            "\"RenderingReport\"",
            "\"RenderingReconciliationReport\"",
            StringComparison.Ordinal);
        var snapshot = JsonSerializer.Deserialize<WorkflowSnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException("Statement reconciliation report workflow snapshot is empty.");
        if (snapshot.SchemaVersion != SnapshotSchemaVersion)
            throw new InvalidDataException($"Unsupported statement reconciliation report snapshot schema {snapshot.SchemaVersion}.");
        return NormalizeLegacySnapshot(snapshot);
    }

    private static Task SaveSnapshotAsync(string directory, WorkflowSnapshot snapshot, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(
            Path.Combine(directory, "workflow.json"),
            JsonSerializer.Serialize(snapshot, JsonOptions),
            ct);

    private static void ValidateCommand(StatementReconciliationReportStartCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.Import);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.SourceInstitution);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.FundAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.ExternalAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Import.ImportedBy);
        if (command.Import.Document.Content.IsEmpty)
            throw new ArgumentException("Statement reconciliation report workflow requires a non-empty statement document.", nameof(command));
        if (command.Import.PeriodEnd < command.Import.PeriodStart)
            throw new ArgumentException("Statement period end must be on or after period start.", nameof(command));
    }

    private static void ValidateWorkflowLookup(string workflowId, string tenantId)
    {
        if (!IsSafeWorkflowId(workflowId))
            throw new ArgumentException("Statement reconciliation report workflow id is invalid.", nameof(workflowId));
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
    }

    private static bool IsSafeWorkflowId(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            return false;
        var prefix = workflowId.StartsWith(WorkflowIdPrefix, StringComparison.Ordinal)
            ? WorkflowIdPrefix
            : workflowId.StartsWith(LegacyWorkflowIdPrefix, StringComparison.Ordinal)
                ? LegacyWorkflowIdPrefix
                : null;
        return prefix is not null
               && workflowId.Length == prefix.Length + 32
               && workflowId[prefix.Length..].All(Uri.IsHexDigit);
    }

    private static void EnsureScopeMatches(WorkflowSnapshot snapshot, string tenantId, string? companyId)
    {
        if (!string.Equals(snapshot.Workflow.TenantId, tenantId.Trim(), StringComparison.Ordinal)
            || !string.Equals(snapshot.Workflow.CompanyId, Normalize(companyId), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Statement reconciliation report workflow belongs to another tenant or company scope.");
    }

    private async Task EnsureRequestIdentityMatchesAsync(
        WorkflowSnapshot snapshot,
        StatementReconciliationReportStartCommand command,
        CancellationToken ct)
    {
        var request = snapshot.Request;
        var import = command.Import;
        var identityMatches =
            string.Equals(request.SourceInstitution.Trim(), import.SourceInstitution.Trim(), StringComparison.Ordinal)
            && string.Equals(request.FundAccountId.Trim(), import.FundAccountId.Trim(), StringComparison.Ordinal)
            && string.Equals(request.ExternalAccountId.Trim(), import.ExternalAccountId.Trim(), StringComparison.Ordinal)
            && request.PeriodStart == import.PeriodStart
            && request.PeriodEnd == import.PeriodEnd
            && string.Equals(request.SourceKind.Trim(), import.SourceKind.Trim(), StringComparison.Ordinal)
            && string.Equals(Normalize(request.ConnectorId), Normalize(import.ConnectorId), StringComparison.Ordinal)
            && string.Equals(Normalize(request.MappingProfileId), Normalize(import.Document.MappingProfileId), StringComparison.Ordinal)
            && string.Equals(
                Normalize(request.DocumentExternalAccountId),
                Normalize(import.Document.ExternalAccountId),
                StringComparison.Ordinal)
            && string.Equals(
                Normalize(request.ToleranceProfileId),
                Normalize(import.ToleranceProfileId),
                StringComparison.Ordinal);
        if (identityMatches)
        {
            var retainedInput = await File.ReadAllBytesAsync(
                    ResolveDataRootPath(request.RelativeInputPath),
                    ct)
                .ConfigureAwait(false);
            identityMatches = SHA256.HashData(retainedInput)
                .AsSpan()
                .SequenceEqual(SHA256.HashData(import.Document.Content.Span));
        }

        if (!identityMatches)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' is bound to a conflicting retained input identity.");
        }
    }

    private WorkflowSnapshot NormalizeRetainedInputLocation(
        WorkflowSnapshot snapshot,
        string workflowDirectory)
    {
        var configuredPath = ResolveDataRootPath(snapshot.Request.RelativeInputPath);
        if (File.Exists(configuredPath))
        {
            return snapshot;
        }

        var colocatedPath = Path.Combine(
            workflowDirectory,
            "input",
            SanitizeFileName(snapshot.Request.FileName));
        if (!File.Exists(colocatedPath))
        {
            return snapshot;
        }

        var relativePath = Path.GetRelativePath(_dataRoot, colocatedPath)
            .Replace('\\', '/');
        return snapshot with
        {
            Request = snapshot.Request with { RelativeInputPath = relativePath }
        };
    }

    private static WorkflowSnapshot BindResolvedAccountingScope(
        WorkflowSnapshot snapshot,
        StatementAccountingScope? resolvedScope)
    {
        if (resolvedScope is null)
        {
            return snapshot;
        }

        if ((snapshot.Request.AccountingScope is { } requestScope
             && !AccountingScopeMatches(requestScope, resolvedScope))
            || (snapshot.Workflow.AccountingScope is { } workflowScope
                && !AccountingScopeMatches(workflowScope, resolvedScope)))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' is already bound to a conflicting accounting close scope.");
        }

        if (snapshot.Request.AccountingScope is not null
            && snapshot.Workflow.AccountingScope is not null)
        {
            return snapshot;
        }

        return snapshot with
        {
            Request = snapshot.Request with { AccountingScope = resolvedScope },
            Workflow = snapshot.Workflow with
            {
                AccountingScope = ToDto(resolvedScope),
                Version = snapshot.Workflow.Version + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }
        };
    }

    private static bool AccountingScopeMatches(
        StatementAccountingScope retained,
        StatementAccountingScope expected)
        => string.Equals(
               retained.FundProfileId.Trim(),
               expected.FundProfileId.Trim(),
               StringComparison.OrdinalIgnoreCase)
           && retained.LedgerBookId == expected.LedgerBookId
           && retained.AccountingPeriodId == expected.AccountingPeriodId
           && retained.AsOfDate == expected.AsOfDate;

    private static bool AccountingScopeMatches(
        StatementReconciliationAccountingScopeDto retained,
        StatementAccountingScope expected)
        => string.Equals(
               retained.FundProfileId.Trim(),
               expected.FundProfileId.Trim(),
               StringComparison.OrdinalIgnoreCase)
           && retained.LedgerBookId == expected.LedgerBookId
           && retained.AccountingPeriodId == expected.AccountingPeriodId
           && retained.AsOfDate == expected.AsOfDate;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StatementReconciliationAccountingScopeDto ToDto(
        StatementAccountingScope scope)
        => new(
            scope.FundProfileId,
            scope.LedgerBookId,
            scope.AccountingPeriodId,
            scope.AsOfDate);

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
        => UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementReconciliationReportById, "workflowId", workflowId);

    private static string BuildResumeRoute(string workflowId)
        => UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementReconciliationReportResume, "workflowId", workflowId);

    private static string BuildArtifactRoute(string workflowId, string artifactId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(
                UiApiRoutes.ReconciliationStatementReconciliationReportArtifact,
                "workflowId",
                workflowId),
            "artifactId",
            artifactId);

    private static WorkflowSnapshot NormalizeLegacySnapshot(WorkflowSnapshot snapshot)
        => snapshot with
        {
            Workflow = snapshot.Workflow with
            {
                StatusRoute = BuildStatusRoute(snapshot.Workflow.WorkflowId),
                ResumeRoute = BuildResumeRoute(snapshot.Workflow.WorkflowId),
                RetainedArtifacts = snapshot.Workflow.RetainedArtifacts
                    .Select(item => item with
                    {
                        DownloadRoute = BuildArtifactRoute(snapshot.Workflow.WorkflowId, item.ArtifactId)
                    })
                    .ToArray()
            }
        };

    private sealed record WorkflowSnapshot(
        int SchemaVersion,
        PersistedRequest Request,
        StatementReconciliationReportWorkflowDto Workflow,
        StatementImportCommitResultDto? ImportResult,
        StatementReconciliationReportWorkflowStatusDto ResumeStatus);

    private sealed record WorkflowLocation(string WorkflowId, string Directory);

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
        string ImportedBy,
        StatementAccountingScope? AccountingScope = null);

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
