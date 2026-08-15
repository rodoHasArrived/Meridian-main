using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Text;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Reporting;
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
public sealed partial class StatementReconciliationReportWorkflowService
{
    private const int SnapshotSchemaVersion = 1;
    private const int ArtifactManifestSchemaVersion = 2;
    private const int ArtifactArchiveReceiptSchemaVersion = 1;
    private const string WorkflowIdPrefix = "statement-reconciliation-report-";
    private const string LegacyWorkflowIdPrefix = "statement-report-";
    private const string WorkflowDirectoryName = "statement-reconciliation-report";
    private const string LegacyWorkflowDirectoryName = "statement-to-report";
    private const string ArtifactManifestFileName = "manifest.json";
    private const string ArtifactArchiveReceiptFileName = "archive-receipt.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IStatementImportCommitService _imports;
    private readonly IStatementImportEvidenceRetainer _evidence;
    private readonly IStatementRunWorkflowService _statementRuns;
    private readonly IStatementReconciliationReportAuthorityStore _authorityStore;
    private readonly string _dataRoot;
    private readonly string _workflowRoot;
    private readonly string _legacyWorkflowRoot;
    private readonly ILogger<StatementReconciliationReportWorkflowService>? _logger;
    private readonly IReconciliationBreakQueueRepository? _breakQueue;
    private readonly IStatementReconciliationIntakeAuthority? _intakeAuthority;

    internal bool IsDurablyComposed =>
        _authorityStore.IsDurableAuthority
        && _evidence is ReportingStatementImportEvidenceRetainer retainer
        && retainer.RetainsCanonicalRunEvidence
        && ReferenceEquals(retainer.AuthorityStore, _authorityStore);

    internal bool IsDurablyComposedWith(
        IStatementReconciliationReportAuthorityStore authorityStore) =>
        ReferenceEquals(_authorityStore, authorityStore)
        && IsDurablyComposed;

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
            new FileStatementReconciliationReportAuthorityStore(dataRoot),
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
            new FileStatementReconciliationReportAuthorityStore(dataRoot),
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
        : this(
            imports,
            evidence,
            statementRuns,
            dataRoot,
            new FileStatementReconciliationReportAuthorityStore(dataRoot),
            logger,
            breakQueue,
            intakeAuthority)
    {
    }

    public StatementReconciliationReportWorkflowService(
        IStatementImportCommitService imports,
        IStatementImportEvidenceRetainer evidence,
        IStatementRunWorkflowService statementRuns,
        string dataRoot,
        IStatementReconciliationReportAuthorityStore authorityStore,
        ILogger<StatementReconciliationReportWorkflowService>? logger,
        IReconciliationBreakQueueRepository? breakQueue,
        IStatementReconciliationIntakeAuthority? intakeAuthority)
    {
        _imports = imports ?? throw new ArgumentNullException(nameof(imports));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _statementRuns = statementRuns ?? throw new ArgumentNullException(nameof(statementRuns));
        _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
        var reportingRoot = _authorityStore.IsDurableAuthority
            ? Path.Combine(
                _dataRoot,
                "runtime",
                "statement-reconciliation-authority-workspace",
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture))
            : Path.Combine(_dataRoot, "reporting");
        _workflowRoot = Path.Combine(reportingRoot, WorkflowDirectoryName);
        _legacyWorkflowRoot = Path.Combine(reportingRoot, LegacyWorkflowDirectoryName);
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
        var retainedBeforeResolution = await FindRetainedWorkflowLocationsAsync(
                BuildWorkflowLocations(command, preScopeCommand),
                ct)
            .ConfigureAwait(false);
        EnsureSingleRetainedAuthority(retainedBeforeResolution);
        var retainedCanRevalidateAfterClose = false;
        if (retainedBeforeResolution.Length == 1)
        {
            await using var retainedOwnership = await _authorityStore
                .AcquireWorkflowLeaseAsync(retainedBeforeResolution[0].Scope, ct)
                .ConfigureAwait(false);
            await HydrateWorkspaceAsync(retainedBeforeResolution[0], ct).ConfigureAwait(false);
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
        var retainedLocations = await FindRetainedWorkflowLocationsAsync(locations, ct)
            .ConfigureAwait(false);
        EnsureSingleRetainedAuthority(retainedLocations);

        var location = retainedLocations.SingleOrDefault() ?? locations[0];
        var workflowId = location.WorkflowId;
        var directory = location.Directory;
        Directory.CreateDirectory(directory);
        await using var ownership = await _authorityStore
            .AcquireWorkflowLeaseAsync(location.Scope, ct)
            .ConfigureAwait(false);
        await HydrateWorkspaceAsync(location, ct).ConfigureAwait(false);

        var snapshot = await ReadSnapshotAsync(directory, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            var inputPath = Path.Combine(directory, "input", SanitizeFileName(command.Import.Document.FileName));
            await AtomicFileWriter.WriteAsync(
                inputPath,
                command.Import.Document.Content.ToArray(),
                ct).ConfigureAwait(false);
            snapshot = CreateSnapshot(workflowId, command, inputPath);
            await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
        }
        else
        {
            EnsureScopeMatches(snapshot, command.TenantId, command.CompanyId);
            await VerifyRetainedArtifactAuthorityAsync(directory, snapshot, ct).ConfigureAwait(false);
            var migratedSnapshot = NormalizeRetainedInputLocation(snapshot, directory);
            await EnsureRequestIdentityMatchesAsync(migratedSnapshot, preScopeCommand, ct).ConfigureAwait(false);
            var scopeBoundSnapshot = BindResolvedAccountingScope(
                migratedSnapshot,
                command.Import.AccountingScope);
            if (!ReferenceEquals(scopeBoundSnapshot, snapshot))
            {
                snapshot = scopeBoundSnapshot;
                await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
            }
        }

        return await ContinueAsync(location, snapshot, ct).ConfigureAwait(false);
    }

    public async Task<StatementReconciliationReportWorkflowDto?> GetAsync(
        string workflowId,
        string tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ValidateWorkflowLookup(workflowId, tenantId);
        _ = RequireIntakeAuthority();
        var location = BuildWorkflowLocation(workflowId, tenantId, companyId);
        if (!await _authorityStore
                .DocumentExistsAsync(location.Scope, "workflow.json", ct)
                .ConfigureAwait(false))
        {
            return null;
        }

        await using var ownership = await _authorityStore
            .AcquireWorkflowLeaseAsync(location.Scope, ct)
            .ConfigureAwait(false);
        await HydrateWorkspaceAsync(location, ct).ConfigureAwait(false);
        var snapshot = await ReadSnapshotAsync(location.Directory, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        EnsureScopeMatches(snapshot, tenantId, companyId);
        await VerifyRetainedArtifactAuthorityAsync(location.Directory, snapshot, ct).ConfigureAwait(false);
        if (snapshot.Workflow.Status == StatementReconciliationReportWorkflowStatusDto.Completed)
        {
            EnsureAuthoritativeIntake(snapshot);
        }
        if (snapshot.Workflow.RetainedArtifacts is { Count: > 0 })
        {
            var authorizedSnapshot = snapshot;
            if (HasAuthoritativeIntake(snapshot))
            {
                var authorizedScope = await ResolveRetainedAccountingScopeAsync(
                        snapshot,
                        tenantId,
                        companyId,
                        ct)
                    .ConfigureAwait(false);
                authorizedSnapshot = BindResolvedAccountingScope(snapshot, authorizedScope);
                EnsureAuthoritativeIntake(authorizedSnapshot);
            }

            var reconciliationGate = await EvaluateCurrentReconciliationAsync(
                    authorizedSnapshot,
                    ct)
                .ConfigureAwait(false);
            if (!reconciliationGate.IsSatisfied)
            {
                return AwaitReconciliation(
                        authorizedSnapshot,
                        reconciliationGate,
                        advanceVersion: false)
                    .Workflow;
            }

            return authorizedSnapshot.Workflow;
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
        _ = RequireIntakeAuthority();
        var location = BuildWorkflowLocation(workflowId, tenantId, companyId);
        if (!await _authorityStore
                .DocumentExistsAsync(location.Scope, "workflow.json", ct)
                .ConfigureAwait(false))
        {
            return null;
        }

        await using var ownership = await _authorityStore
            .AcquireWorkflowLeaseAsync(location.Scope, ct)
            .ConfigureAwait(false);
        await HydrateWorkspaceAsync(location, ct).ConfigureAwait(false);
        var snapshot = await ReadSnapshotAsync(location.Directory, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        EnsureScopeMatches(snapshot, tenantId, companyId);
        await VerifyRetainedArtifactAuthorityAsync(location.Directory, snapshot, ct).ConfigureAwait(false);
        var authorizedScope = await ResolveRetainedAccountingScopeAsync(
                snapshot,
                tenantId,
                companyId,
                ct)
            .ConfigureAwait(false);
        var authorizedSnapshot = BindResolvedAccountingScope(snapshot, authorizedScope);
        if (!ReferenceEquals(authorizedSnapshot, snapshot))
        {
            snapshot = authorizedSnapshot;
            await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
        }
        return await ContinueAsync(location, snapshot, ct).ConfigureAwait(false);
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
        if (workflow is null
            || workflow.Status != StatementReconciliationReportWorkflowStatusDto.Completed)
            return null;
        var descriptor = workflow.RetainedArtifacts.FirstOrDefault(item =>
            string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal));
        if (descriptor is null)
            return null;

        var location = BuildWorkflowLocation(workflowId, tenantId, companyId);
        await using var ownership = await _authorityStore
            .AcquireWorkflowLeaseAsync(location.Scope, ct)
            .ConfigureAwait(false);
        await HydrateWorkspaceAsync(location, ct).ConfigureAwait(false);
        var path = ResolveArtifactPath(workflowId, descriptor.ArtifactId);
        var content = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        ValidateArtifactContent(descriptor, content);
        return new StatementReconciliationReportArtifactDownload(descriptor, content);
    }

    private async Task<StatementReconciliationReportWorkflowExecution> ContinueAsync(
        WorkflowLocation location,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        var directory = location.Directory;
        var intakeAuthority = RequireIntakeAuthority();
        try
        {
            if (snapshot.ImportResult is not null && _authorityStore.IsDurableAuthority)
            {
                snapshot = await RetainStatementEvidenceAsync(location, snapshot, ct)
                    .ConfigureAwait(false);
            }

            if (snapshot.Workflow.RetainedArtifacts is { Count: > 0 })
            {
                var completedGate = await EvaluateCurrentReconciliationAsync(snapshot, ct)
                    .ConfigureAwait(false);
                if (snapshot.Workflow.Status
                        == StatementReconciliationReportWorkflowStatusDto.Completed
                    && completedGate.IsSatisfied
                    && HasAuthoritativeIntake(snapshot))
                {
                    return RequireExecution(snapshot);
                }

                var archivedGeneration = await ArchiveCurrentArtifactGenerationAsync(
                        directory,
                        snapshot,
                        ct)
                    .ConfigureAwait(false);
                snapshot = AwaitReconciliation(
                    snapshot,
                    completedGate,
                    advanceVersion: true,
                    archivedGeneration: archivedGeneration);
                await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
                if (!completedGate.IsSatisfied)
                    return RequireExecution(snapshot);
            }

            if (snapshot.ImportResult is null)
            {
                snapshot = Advance(snapshot, StatementReconciliationReportWorkflowStatusDto.Importing,
                    recoveryAction: "Retry the persisted statement import.");
                await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
                var importRequest = await BuildImportRequestAsync(snapshot, ct).ConfigureAwait(false);
                var imported = await _imports.CommitAsync(importRequest, ct).ConfigureAwait(false);
                snapshot = await RetainStatementEvidenceAsync(
                        location,
                        snapshot with { ImportResult = imported },
                        ct)
                    .ConfigureAwait(false);
            }

            if (snapshot.ImportResult!.EvidenceVaultIdentity is null)
            {
                snapshot = await RetainStatementEvidenceAsync(location, snapshot, ct)
                    .ConfigureAwait(false);
            }
            var retainedImport = snapshot.ImportResult
                ?? throw new InvalidDataException(
                    $"Statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' has no retained import checkpoint.");

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
                        retainedImport,
                        accountingScope,
                        snapshot.Workflow.TenantId,
                        snapshot.Workflow.CompanyId
                        ?? throw new UnauthorizedAccessException(
                            "Statement-to-close publication requires a company scope."),
                        snapshot.Request.ImportedBy,
                        snapshot.Request.SourceInstitution,
                        BuildEvidenceReferences(retainedImport),
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
                await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
            }

            var reconciliationGate = await EvaluateCurrentReconciliationAsync(snapshot, ct)
                .ConfigureAwait(false);
            if (!reconciliationGate.IsSatisfied)
            {
                snapshot = AwaitReconciliation(
                    snapshot,
                    reconciliationGate,
                    advanceVersion: true);
                await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
                return RequireExecution(snapshot);
            }

            EnsureAuthoritativeIntake(snapshot);
            snapshot = Advance(snapshot, StatementReconciliationReportWorkflowStatusDto.RenderingReconciliationReport,
                breakCount: 0, caseCount: 0,
                recoveryAction: "Retry report rendering from the retained statement and reconciliation evidence.");
            await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
            var artifactSet = await RetainReportArtifactsAsync(
                    directory,
                    snapshot,
                    reconciliationGate.Reconciliation,
                    ct)
                .ConfigureAwait(false);
            snapshot = Complete(snapshot, artifactSet);
            await SaveSnapshotAsync(location, snapshot, ct).ConfigureAwait(false);
            return RequireExecution(snapshot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (StatementReconciliationReportAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failed = Fail(snapshot, ex);
            await SaveSnapshotAsync(location, failed, CancellationToken.None).ConfigureAwait(false);
            _logger?.LogError(ex, "Statement reconciliation report workflow {WorkflowId} failed at status {Status}",
                snapshot.Workflow.WorkflowId, snapshot.Workflow.Status);
            return RequireExecution(failed);
        }
    }

    private async Task<WorkflowSnapshot> RetainStatementEvidenceAsync(
        WorkflowLocation location,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        var importResult = snapshot.ImportResult
            ?? throw new InvalidDataException(
                $"Statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' has no retained import checkpoint.");
        var retainedEvidence = await _evidence.RetainAsync(
                importResult,
                BuildEvidenceRetentionRequest(snapshot),
                ct)
            .ConfigureAwait(false);
        var retainedSnapshot = Equals(retainedEvidence, importResult)
            ? snapshot
            : snapshot with { ImportResult = retainedEvidence };
        await SaveSnapshotAsync(location, retainedSnapshot, ct).ConfigureAwait(false);
        return retainedSnapshot;
    }

    private static StatementImportEvidenceBridgeRequest BuildEvidenceRetentionRequest(
        WorkflowSnapshot snapshot) =>
        new(
            snapshot.Request.SourceKind,
            snapshot.Request.SourceInstitution,
            snapshot.Request.FundAccountId,
            snapshot.Request.ExternalAccountId,
            snapshot.Request.PeriodStart,
            snapshot.Request.PeriodEnd,
            snapshot.Request.ImportedBy)
        {
            TenantId = snapshot.Workflow.TenantId,
            CompanyId = snapshot.Workflow.CompanyId,
            WorkflowId = snapshot.Workflow.WorkflowId
        };

    private IStatementReconciliationIntakeAuthority RequireIntakeAuthority()
        => _intakeAuthority
           ?? throw new StatementReconciliationIntakeAuthorityException(
               "STATEMENT_INTAKE_AUTHORITY_UNAVAILABLE",
               "Statement reconciliation report processing is unavailable because the authoritative statement intake service is not configured. No input, evidence, reconciliation report, or completion record was retained.");

    private Task<StatementAccountingScope> ResolveRetainedAccountingScopeAsync(
        WorkflowSnapshot snapshot,
        string tenantId,
        string? companyId,
        CancellationToken ct)
        => RequireIntakeAuthority().ResolveAccountingScopeAsync(
            new StatementReconciliationIntakeScopeRequest(
                tenantId,
                companyId
                ?? throw new UnauthorizedAccessException(
                    "Statement-to-close workflow access requires a company scope."),
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
            ct);

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
        string tenantId,
        string? companyId,
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

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            return CanonicalQueueHandoffGate.Blocked(
                Math.Max(import.CaseCount, import.BreakCount),
                "The canonical reconciliation queue cannot be evaluated without the workflow's exact tenant and company scope.");
        }

        var queueScope = new ReconciliationBreakQueueScope(tenantId, companyId);
        var items = (await _breakQueue.GetAllAsync(queueScope, ct: ct).ConfigureAwait(false))
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

    private async Task<RetainedArtifactSet> RetainReportArtifactsAsync(
        string directory,
        WorkflowSnapshot snapshot,
        StatementRunWorkflowResult? reconciliation,
        CancellationToken ct)
    {
        var import = snapshot.ImportResult!;
        var generation = GetNextArtifactGeneration(snapshot.Workflow);
        var retainedAt = snapshot.RenderingReconciliationReportAtUtc
            ?? throw new InvalidDataException(
                $"Statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' has no persisted rendering checkpoint timestamp.");
        var evidenceReferences = snapshot.Workflow.EvidenceReferences
            .Concat(BuildEvidenceReferences(import))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var report = new StatementReconciliationReport(
            SchemaVersion: 1,
            WorkflowId: snapshot.Workflow.WorkflowId,
            ArtifactGeneration: generation,
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
        var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new ArtifactGenerationManifest(
                ArtifactManifestSchemaVersion,
                generation,
                snapshot.Workflow.WorkflowId,
                import.RunId,
                descriptors,
                evidenceReferences),
            JsonOptions));
        await AtomicFileWriter.WriteAsync(
                Path.Combine(artifactDirectory, ArtifactManifestFileName),
                manifestBytes,
                ct)
            .ConfigureAwait(false);
        return new RetainedArtifactSet(
            generation,
            descriptors,
            Convert.ToHexString(SHA256.HashData(manifestBytes)));
    }

    private async Task<StatementReconciliationReportArtifactGenerationDto> ArchiveCurrentArtifactGenerationAsync(
        string directory,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        var workflow = snapshot.Workflow;
        if (workflow.RetainedArtifacts is not { Count: > 0 })
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' has no current artifact generation to archive.");
        }

        var generation = workflow.ArtifactGeneration;
        if (generation <= 0)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' has no valid current artifact generation.");
        }

        if ((workflow.ArtifactHistory ?? []).Any(item => item.Generation == generation))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' already records artifact generation {generation} as historical while it is still current.");
        }

        var generatedAtValues = workflow.RetainedArtifacts
            .Select(static item => item.RetainedAtUtc)
            .Distinct()
            .ToArray();
        if (generatedAtValues.Length != 1)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact descriptors do not share one generation timestamp.");
        }

        var payloads = new List<ArtifactArchivePayload>(workflow.RetainedArtifacts.Count);
        foreach (var descriptor in workflow.RetainedArtifacts)
        {
            var sourcePath = ResolveArtifactPath(workflow.WorkflowId, descriptor.ArtifactId);
            var archiveFileName = ValidateArchiveFileName(descriptor.FileName);
            if (!string.Equals(
                    Path.GetFileName(sourcePath),
                    archiveFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact '{descriptor.ArtifactId}' file name no longer matches its retained descriptor.");
            }

            var content = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
            ValidateArtifactContent(descriptor, content);
            payloads.Add(new ArtifactArchivePayload(archiveFileName, content));
        }

        var manifestPath = Path.Combine(directory, "artifacts", ArtifactManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact manifest is missing.");
        }

        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, ct).ConfigureAwait(false);
        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        ValidateCurrentArtifactManifest(manifestBytes, manifestHash, workflow);
        var archiveDirectory = GetArtifactGenerationArchiveDirectory(directory, generation);
        Directory.CreateDirectory(archiveDirectory);
        foreach (var payload in payloads)
        {
            await WriteImmutableFileAsync(
                    Path.Combine(archiveDirectory, payload.FileName),
                    payload.Content,
                    ct)
                .ConfigureAwait(false);
        }

        await WriteImmutableFileAsync(
                Path.Combine(archiveDirectory, ArtifactManifestFileName),
                manifestBytes,
                ct)
            .ConfigureAwait(false);

        var receiptPath = Path.Combine(archiveDirectory, ArtifactArchiveReceiptFileName);
        ArtifactGenerationArchiveReceipt receipt;
        if (File.Exists(receiptPath))
        {
            var retainedReceiptBytes = await File.ReadAllBytesAsync(receiptPath, ct).ConfigureAwait(false);
            receipt = JsonSerializer.Deserialize<ArtifactGenerationArchiveReceipt>(
                    retainedReceiptBytes,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation} archive receipt is empty.");
        }
        else
        {
            receipt = new ArtifactGenerationArchiveReceipt(
                ArtifactArchiveReceiptSchemaVersion,
                generation,
                workflow.RetainedArtifacts.ToArray(),
                ArtifactManifestFileName,
                manifestBytes.LongLength,
                manifestHash,
                generatedAtValues[0],
                DateTimeOffset.UtcNow,
                (workflow.EvidenceReferences ?? []).ToArray());
            var newReceiptBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(receipt, JsonOptions));
            await WriteImmutableFileAsync(receiptPath, newReceiptBytes, ct).ConfigureAwait(false);
        }

        ValidateArchiveReceipt(
            receipt,
            generation,
            workflow.RetainedArtifacts,
            manifestBytes.LongLength,
            manifestHash,
            generatedAtValues[0],
            workflow.EvidenceReferences ?? []);
        var receiptBytes = await File.ReadAllBytesAsync(receiptPath, ct).ConfigureAwait(false);
        var receiptHash = Convert.ToHexString(SHA256.HashData(receiptBytes));
        var auditEvidence = receipt.EvidenceReferences
            .Concat(BuildArtifactGenerationEvidenceReferences(
                generation,
                receipt.Artifacts,
                receipt.ManifestContentHashSha256))
            .Append(
                $"artifact-generation:{generation}:archive-receipt:sha256:{receiptHash}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        return new StatementReconciliationReportArtifactGenerationDto(
            generation,
            receipt.Artifacts,
            receipt.ManifestFileName,
            receipt.ManifestByteLength,
            receipt.ManifestContentHashSha256,
            receipt.GeneratedAtUtc,
            receipt.ArchivedAtUtc,
            auditEvidence,
            receiptHash);
    }

    private static async Task WriteImmutableFileAsync(
        string path,
        byte[] content,
        CancellationToken ct)
    {
        if (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(content))
            {
                throw new InvalidDataException(
                    $"Immutable statement reconciliation report history file '{Path.GetFileName(path)}' conflicts with retained bytes.");
            }

            return;
        }

        await AtomicFileWriter.WriteAsync(path, content, ct).ConfigureAwait(false);
    }

    private static void ValidateArchiveReceipt(
        ArtifactGenerationArchiveReceipt receipt,
        int generation,
        IReadOnlyList<StatementReconciliationReportArtifactDto> artifacts,
        long manifestByteLength,
        string manifestHash,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<string> evidenceReferences)
    {
        if (receipt.SchemaVersion != ArtifactArchiveReceiptSchemaVersion
            || receipt.Generation != generation
            || !ArtifactDescriptorsMatch(receipt.Artifacts, artifacts)
            || receipt.EvidenceReferences is null
            || !string.Equals(
                receipt.ManifestFileName,
                ArtifactManifestFileName,
                StringComparison.Ordinal)
            || receipt.ManifestByteLength != manifestByteLength
            || !string.Equals(
                receipt.ManifestContentHashSha256,
                manifestHash,
                StringComparison.OrdinalIgnoreCase)
            || receipt.GeneratedAtUtc != generatedAtUtc
            || !receipt.EvidenceReferences.SequenceEqual(
                evidenceReferences,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report artifact generation {generation} archive receipt conflicts with the current immutable generation.");
        }
    }

    private static void ValidateCurrentArtifactManifest(
        byte[] manifestBytes,
        string manifestHash,
        StatementReconciliationReportWorkflowDto workflow)
    {
        ArtifactGenerationManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ArtifactGenerationManifest>(
                    manifestBytes,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact manifest is invalid.",
                ex);
        }

        if ((manifest.SchemaVersion != 1
             && manifest.SchemaVersion != ArtifactManifestSchemaVersion)
            || !WorkflowIdentitiesMatch(manifest.WorkflowId, workflow.WorkflowId)
            || !string.Equals(
                manifest.StatementRunId,
                workflow.StatementRunId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact manifest conflicts with retained workflow identity.");
        }

        // Current workflow evidence can evolve during legacy scope publication or casework reopen.
        // The v2 manifest remains immutable because its retained SHA-256 is verified below.
        if (!CurrentArtifactDescriptorsMatch(manifest.Artifacts, workflow.RetainedArtifacts)
            || manifest.EvidenceReferences is null)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact manifest conflicts with retained artifact evidence.");
        }

        if (manifest.SchemaVersion == 1)
            return;

        var manifestEvidencePrefix =
            $"artifact-generation:{workflow.ArtifactGeneration}:manifest:sha256:";
        var retainedManifestHashes = (workflow.EvidenceReferences ?? [])
            .Where(item => item.StartsWith(manifestEvidencePrefix, StringComparison.Ordinal))
            .Select(item => item[manifestEvidencePrefix.Length..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (manifest.ArtifactGeneration != workflow.ArtifactGeneration
            || retainedManifestHashes.Length != 1
            || !string.Equals(
                retainedManifestHashes[0],
                manifestHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact manifest failed generation or hash verification.");
        }
    }

    private static bool WorkflowIdentitiesMatch(string manifestWorkflowId, string workflowId)
    {
        if (string.Equals(manifestWorkflowId, workflowId, StringComparison.Ordinal))
            return true;
        if (!IsSafeWorkflowId(manifestWorkflowId) || !IsSafeWorkflowId(workflowId))
            return false;

        return string.Equals(
            ExtractWorkflowIdentityHash(manifestWorkflowId),
            ExtractWorkflowIdentityHash(workflowId),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractWorkflowIdentityHash(string workflowId)
        => workflowId.StartsWith(WorkflowIdPrefix, StringComparison.Ordinal)
            ? workflowId[WorkflowIdPrefix.Length..]
            : workflowId[LegacyWorkflowIdPrefix.Length..];

    private static bool CurrentArtifactDescriptorsMatch(
        IReadOnlyList<StatementReconciliationReportArtifactDto>? left,
        IReadOnlyList<StatementReconciliationReportArtifactDto>? right)
    {
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if ((left[index] with { DownloadRoute = string.Empty })
                != (right[index] with { DownloadRoute = string.Empty }))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArtifactDescriptorsMatch(
        IReadOnlyList<StatementReconciliationReportArtifactDto>? left,
        IReadOnlyList<StatementReconciliationReportArtifactDto>? right)
    {
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
    }

    private static IEnumerable<string> BuildArtifactGenerationEvidenceReferences(
        int generation,
        IReadOnlyList<StatementReconciliationReportArtifactDto> artifacts,
        string manifestHash)
    {
        yield return $"artifact-generation:{generation}:manifest:sha256:{manifestHash}";
        foreach (var artifact in artifacts)
        {
            yield return
                $"artifact-generation:{generation}:artifact:{artifact.ArtifactId}:sha256:{artifact.ContentHashSha256}";
        }
    }

    private static void ValidateArtifactContent(
        StatementReconciliationReportArtifactDto descriptor,
        byte[] content)
    {
        var actualHash = Convert.ToHexString(SHA256.HashData(content));
        if (content.LongLength != descriptor.ByteLength
            || !string.Equals(
                actualHash,
                descriptor.ContentHashSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Retained statement reconciliation report artifact '{descriptor.ArtifactId}' failed immutable generation verification.");
        }
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
            TextPrimitives.NormalizeOptional(command.CompanyId),
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

    private async Task<StatementImportCommitRequest> BuildImportRequestAsync(
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        var request = snapshot.Request;
        var inputPath = ResolveDataRootPath(request.RelativeInputPath);
        var content = await File.ReadAllBytesAsync(inputPath, ct).ConfigureAwait(false);
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
        var now = DateTimeOffset.UtcNow;
        var renderingReconciliationReportAtUtc =
            status == StatementReconciliationReportWorkflowStatusDto.RenderingReconciliationReport
                ? snapshot.RenderingReconciliationReportAtUtc ?? now
                : snapshot.RenderingReconciliationReportAtUtc;
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
            UpdatedAtUtc = now,
            FailureReason = null,
            RecoveryAction = recoveryAction
        };
        return snapshot with
        {
            Workflow = workflow,
            ResumeStatus = status,
            RenderingReconciliationReportAtUtc = renderingReconciliationReportAtUtc
        };
    }

    private static int GetNextArtifactGeneration(
        StatementReconciliationReportWorkflowDto workflow)
    {
        var latestHistoryGeneration = (workflow.ArtifactHistory ?? [])
            .Select(static item => item.Generation)
            .DefaultIfEmpty(0)
            .Max();
        return checked(Math.Max(workflow.ArtifactGeneration, latestHistoryGeneration) + 1);
    }

    private static WorkflowSnapshot Complete(
        WorkflowSnapshot snapshot,
        RetainedArtifactSet artifactSet)
    {
        var now = DateTimeOffset.UtcNow;
        var import = snapshot.ImportResult!;
        var expectedGeneration = GetNextArtifactGeneration(snapshot.Workflow);
        if (artifactSet.Generation != expectedGeneration)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report artifact generation {artifactSet.Generation} does not match expected generation {expectedGeneration}.");
        }

        return snapshot with
        {
            Workflow = snapshot.Workflow with
            {
                Status = StatementReconciliationReportWorkflowStatusDto.Completed,
                Version = snapshot.Workflow.Version + 1,
                StatementRunId = import.RunId,
                EvidenceVaultIdentity = import.EvidenceVaultIdentity,
                RetainedArtifacts = artifactSet.Artifacts,
                EvidenceReferences = snapshot.Workflow.EvidenceReferences
                    .Concat(BuildEvidenceReferences(import))
                    .Concat(artifactSet.Artifacts.Select(static item =>
                        $"artifact:{item.ArtifactId}:sha256:{item.ContentHashSha256}"))
                    .Concat(BuildArtifactGenerationEvidenceReferences(
                        artifactSet.Generation,
                        artifactSet.Artifacts,
                        artifactSet.ManifestContentHashSha256))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                BreakCount = 0,
                CaseCount = 0,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                FailureReason = null,
                RecoveryAction = null,
                ArtifactGeneration = artifactSet.Generation
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
        {
            evidence.Add($"evidence-vault:{vault.VaultId}");
            evidence.AddRange(vault.Artifacts.Select(static artifact =>
                $"evidence-artifact:{artifact.Kind}:{artifact.RelativePath}:sha256:{artifact.ContentHashSha256}"));
        }
        evidence.AddRange(import.BreakIds.Select(static id => $"reconciliation-break:{id}"));
        evidence.AddRange(import.CaseIds.Select(static id => $"reconciliation-case:{id}"));
        return evidence.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
    }

    private static StatementReconciliationReportWorkflowExecution RequireExecution(WorkflowSnapshot snapshot)
        => new(
            snapshot.ImportResult,
            snapshot.Workflow);

    private static string BuildWorkflowId(StatementReconciliationReportStartCommand command, string prefix)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(command.Import.Document.Content.Span));
        var identity = string.Join('|',
            command.TenantId.Trim(),
            TextPrimitives.NormalizeOptional(command.CompanyId),
            CanonicalizeSemanticIdentity(command.Import.SourceInstitution),
            CanonicalizeSemanticIdentity(command.Import.FundAccountId),
            CanonicalizeSemanticIdentity(command.Import.ExternalAccountId),
            command.Import.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            command.Import.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CanonicalizeSemanticIdentity(command.Import.SourceKind),
            CanonicalizeSemanticIdentity(command.Import.ConnectorId),
            CanonicalizeSemanticIdentity(command.Import.Document.MappingProfileId),
            CanonicalizeSemanticIdentity(command.Import.Document.ExternalAccountId),
            CanonicalizeSemanticIdentity(command.Import.ToleranceProfileId),
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
        var scope = new StatementReconciliationReportAuthorityScope(
            command.TenantId.Trim(),
            RequireCompanyId(command.CompanyId),
            workflowId);
        return new WorkflowLocation(workflowId, GetWorkflowDirectory(workflowId), scope);
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
            || !string.Equals(snapshot.Workflow.CompanyId, TextPrimitives.NormalizeOptional(companyId), StringComparison.Ordinal))
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
            SemanticIdentityEquals(request.SourceInstitution, import.SourceInstitution)
            && SemanticIdentityEquals(request.FundAccountId, import.FundAccountId)
            && SemanticIdentityEquals(request.ExternalAccountId, import.ExternalAccountId)
            && request.PeriodStart == import.PeriodStart
            && request.PeriodEnd == import.PeriodEnd
            && SemanticIdentityEquals(request.SourceKind, import.SourceKind)
            && SemanticIdentityEquals(request.ConnectorId, import.ConnectorId)
            && SemanticIdentityEquals(request.MappingProfileId, import.Document.MappingProfileId)
            && SemanticIdentityEquals(
                request.DocumentExternalAccountId,
                import.Document.ExternalAccountId)
            && SemanticIdentityEquals(request.ToleranceProfileId, import.ToleranceProfileId);
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
        var colocatedPath = Path.Combine(
            workflowDirectory,
            "input",
            SanitizeFileName(snapshot.Request.FileName));
        if (File.Exists(colocatedPath))
        {
            var relativePath = Path.GetRelativePath(_dataRoot, colocatedPath)
                .Replace('\\', '/');
            return string.Equals(
                relativePath,
                snapshot.Request.RelativeInputPath,
                StringComparison.Ordinal)
                ? snapshot
                : snapshot with
                {
                    Request = snapshot.Request with { RelativeInputPath = relativePath }
                };
        }

        var configuredPath = ResolveDataRootPath(snapshot.Request.RelativeInputPath);
        if (File.Exists(configuredPath))
        {
            return snapshot;
        }

        return snapshot;
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

    private static string? CanonicalizeSemanticIdentity(string? value)
        => TextPrimitives.NormalizeOptional(value)?.ToUpperInvariant();

    private static bool SemanticIdentityEquals(string? left, string? right)
        => string.Equals(
            CanonicalizeSemanticIdentity(left),
            CanonicalizeSemanticIdentity(right),
            StringComparison.Ordinal);

    private static string RequireCompanyId(string? companyId)
        => string.IsNullOrWhiteSpace(companyId)
            ? throw new UnauthorizedAccessException(
                "Statement reconciliation report authority requires an exact company scope.")
            : companyId.Trim();

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
    {
        var retainedArtifacts = snapshot.Workflow.RetainedArtifacts ?? [];
        var artifactHistory = snapshot.Workflow.ArtifactHistory ?? [];
        var artifactGeneration = snapshot.Workflow.ArtifactGeneration;
        if (artifactGeneration <= 0 && retainedArtifacts.Count > 0)
            artifactGeneration = 1;
        else if (artifactGeneration <= 0 && artifactHistory.Count > 0)
            artifactGeneration = artifactHistory.Max(static item => item.Generation);

        return snapshot with
        {
            Workflow = snapshot.Workflow with
            {
                StatusRoute = BuildStatusRoute(snapshot.Workflow.WorkflowId),
                ResumeRoute = BuildResumeRoute(snapshot.Workflow.WorkflowId),
                RetainedArtifacts = retainedArtifacts
                    .Select(item => item with
                    {
                        DownloadRoute = BuildArtifactRoute(snapshot.Workflow.WorkflowId, item.ArtifactId)
                    })
                    .ToArray(),
                ArtifactGeneration = artifactGeneration,
                ArtifactHistory = artifactHistory
            }
        };
    }

    private sealed record RetainedArtifactSet(
        int Generation,
        IReadOnlyList<StatementReconciliationReportArtifactDto> Artifacts,
        string ManifestContentHashSha256);

    private sealed record ArtifactArchivePayload(
        string FileName,
        byte[] Content);

    private sealed record ArtifactGenerationArchiveReceipt(
        int SchemaVersion,
        int Generation,
        IReadOnlyList<StatementReconciliationReportArtifactDto> Artifacts,
        string ManifestFileName,
        long ManifestByteLength,
        string ManifestContentHashSha256,
        DateTimeOffset GeneratedAtUtc,
        DateTimeOffset ArchivedAtUtc,
        IReadOnlyList<string> EvidenceReferences);

    private sealed record ArtifactGenerationManifest(
        int SchemaVersion,
        int ArtifactGeneration,
        string WorkflowId,
        string StatementRunId,
        IReadOnlyList<StatementReconciliationReportArtifactDto> Artifacts,
        IReadOnlyList<string> EvidenceReferences);

    private sealed record WorkflowSnapshot(
        int SchemaVersion,
        PersistedRequest Request,
        StatementReconciliationReportWorkflowDto Workflow,
        StatementImportCommitResultDto? ImportResult,
        StatementReconciliationReportWorkflowStatusDto ResumeStatus,
        DateTimeOffset? RenderingReconciliationReportAtUtc = null);

    private sealed record CurrentReconciliationGate(
        bool IsSatisfied,
        StatementRunWorkflowResult? Reconciliation,
        int OpenBreakCount,
        int OpenCaseCount,
        string RecoveryAction)
    {
        public static CurrentReconciliationGate Satisfied(
            StatementRunWorkflowResult? reconciliation)
            => new(true, reconciliation, 0, 0, string.Empty);
    }

    private sealed record WorkflowLocation(
        string WorkflowId,
        string Directory,
        StatementReconciliationReportAuthorityScope Scope);

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
        int ArtifactGeneration,
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
