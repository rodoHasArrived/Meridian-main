using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class StatementReconciliationReportWorkflowServiceTests : IDisposable
{
    private static readonly Guid OperationsWorkflowId =
        Guid.Parse("6a270c31-6f85-45d8-ab86-5dbb8cbb9c7e");
    private static readonly StatementAccountingScope AccountingScope = new(
        "fund-profile-alpha",
        Guid.Parse("a05d4e98-4f6a-46a5-a42e-50d875756179"),
        Guid.Parse("f2f2b4df-8435-4b8c-a94a-51f72078dd89"),
        new DateOnly(2026, 6, 30));
    private static readonly ReconciliationBreakQueueScope QueueScope = new(
        "tenant-alpha",
        "company-alpha");
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-reconciliation-report-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_CleanStatement_RetainsReportsAndIsIdempotentAcrossRestart()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService { ReturnReconciled = true };
        var intake = new ResolvingIntakeAuthority(AccountingScope);
        var service = CreateService(imports, evidence, runs, intakeAuthority: intake);
        var command = BuildCommand();

        var first = await service.StartAsync(command);

        first.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        first.Workflow.RetainedArtifacts.Should().HaveCount(2);
        first.Workflow.EvidenceVaultIdentity.Should().NotBeNull();
        first.Workflow.AccountingScope.Should().BeEquivalentTo(
            new StatementReconciliationAccountingScopeDto(
                AccountingScope.FundProfileId,
                AccountingScope.LedgerBookId,
                AccountingScope.AccountingPeriodId,
                AccountingScope.AsOfDate));
        first.Workflow.OperationsWorkflowId.Should().Be(OperationsWorkflowId);
        first.Workflow.EvidenceReferences.Should().Contain(
            $"operations-workflow:{OperationsWorkflowId:D}");
        imports.CommitCount.Should().Be(1);
        evidence.RetainCount.Should().Be(1);
        intake.ResolveCount.Should().Be(1);
        intake.PublishCount.Should().Be(1);

        var jsonArtifact = first.Workflow.RetainedArtifacts.Single(item =>
            item.ArtifactId == "reconciliation-report-json");
        var download = await service.DownloadArtifactAsync(
            first.Workflow.WorkflowId,
            jsonArtifact.ArtifactId,
            "tenant-alpha",
            "company-alpha");
        download.Should().NotBeNull();
        Convert.ToHexString(SHA256.HashData(download!.Content))
            .Should().Be(jsonArtifact.ContentHashSha256);
        Encoding.UTF8.GetString(download.Content).Should().Contain(first.Workflow.WorkflowId);

        var restarted = CreateService(imports, evidence, runs, intakeAuthority: intake);
        var repeated = await restarted.StartAsync(command);

        repeated.Workflow.WorkflowId.Should().Be(first.Workflow.WorkflowId);
        repeated.Workflow.Version.Should().Be(first.Workflow.Version);
        imports.CommitCount.Should().Be(1, "completed content-addressed workflows must not re-import after restart");
        evidence.RetainCount.Should().Be(1);
        intake.ResolveCount.Should().Be(3,
            "artifact access and restart both revalidate the retained accounting authority");
        intake.PublishCount.Should().Be(1,
            "an authoritative completed workflow must not publish Operations continuity twice");
    }

    [Fact]
    public async Task ResumeAsync_AfterReconciliationClears_CompletesFromPersistedImport()
    {
        var imports = new FakeImportService(BuildImportResult(breakCount: 1, caseCount: 1));
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService();
        var queue = await CreateStatementQueueAsync(handoffCompleted: true);
        var service = CreateService(imports, evidence, runs, queue);

        var started = await service.StartAsync(BuildCommand());

        started.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        started.Workflow.RetainedArtifacts.Should().BeEmpty();
        started.Workflow.RecoveryAction.Should().Contain("Resolve or disposition");

        runs.ReturnReconciled = true;
        var restarted = CreateService(imports, evidence, runs, queue);
        var resumed = await restarted.ResumeAsync(
            started.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        resumed.Should().NotBeNull();
        resumed!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        resumed.Workflow.RetainedArtifacts.Should().HaveCount(2);
        imports.CommitCount.Should().Be(1, "resume must use the persisted import checkpoint");
        var report = await restarted.DownloadArtifactAsync(
            resumed.Workflow.WorkflowId,
            "reconciliation-report-json",
            "tenant-alpha",
            "company-alpha");
        Encoding.UTF8.GetString(report!.Content).Should().Contain("case-alpha",
            "the final report must retain lineage to the reconciliation case that was resolved");
    }

    [Fact]
    public async Task ResumeAsync_SourceLooksReconciledButQueueHandoffIsPartial_DoesNotRenderReconciled()
    {
        var imports = new FakeImportService(BuildImportResult(breakCount: 1, caseCount: 1));
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService();
        var queue = await CreateStatementQueueAsync(handoffCompleted: false);
        var service = CreateService(imports, evidence, runs, queue);

        var started = await service.StartAsync(BuildCommand());
        runs.ReturnReconciled = true;
        var partial = await service.ResumeAsync(
            started.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        partial.Should().NotBeNull();
        partial!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        partial.Workflow.RetainedArtifacts.Should().BeEmpty();
        partial.Workflow.RecoveryAction.Should().Contain("handoff obligation");

        var retained = await queue.GetByIdAsync(QueueScope, "queue-break-alpha");
        retained.Should().NotBeNull();
        var completionCommand = new ReconciliationCaseworkCommand(
            BreakId: retained!.BreakId,
            Action: ReconciliationCaseworkAction.LinkEvidence,
            Actor: "operations-controller",
            CommandId: StatementCaseworkHandoffObligation.CreateCompletionCommandId("resolve-alpha"),
            CorrelationId: "statement-run-alpha",
            Source: StatementCaseworkHandoffObligation.CompletionSource,
            ExpectedVersion: retained.Version,
            Reason: "Source and Operations evidence retained.",
            CausationId: "resolve-alpha",
            EvidenceLinks: [StatementCaseworkHandoffObligation.CreateCompletedMarker("resolve-alpha")])
        {
            CloseScope = new ReconciliationCaseworkCloseScopeDto(
                "fund-alpha",
                Guid.Parse("0f55a7b7-3709-4617-b493-cd852405186e"),
                Guid.Parse("9f9a040b-5138-4bd9-a401-6c7508f10110"),
                new DateOnly(2026, 6, 30))
        };
        var completion = await queue.ApplyCaseworkCommandAsync(QueueScope, completionCommand);
        completion.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);

        var completed = await service.ResumeAsync(
            started.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");
        completed!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        completed.Workflow.RetainedArtifacts.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAndResumeAsync_CompletedCaseworkReopens_SuppressesAndReplacesStaleArtifactAuthority()
    {
        var imports = new FakeImportService(BuildImportResult(breakCount: 1, caseCount: 1));
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService
        {
            ReturnReconciled = true,
            Cases = [BuildReconciliationCase("resolved-case-v1", "Resolved")]
        };
        var queue = await CreateStatementQueueAsync(handoffCompleted: true);
        var intake = new ResolvingIntakeAuthority(AccountingScope);
        var service = CreateService(imports, evidence, runs, queue, intake);
        var completed = await service.StartAsync(BuildCommand());
        var workflowPath = ResolveWorkflowSnapshotPath(completed.Workflow.WorkflowId);
        var persistedBeforeRead = await File.ReadAllBytesAsync(workflowPath);
        var originalJsonArtifact = completed.Workflow.RetainedArtifacts.Single(item =>
            item.ArtifactId == "reconciliation-report-json");
        var originalJsonEvidence =
            $"artifact:{originalJsonArtifact.ArtifactId}:sha256:{originalJsonArtifact.ContentHashSha256}";
        var retained = await queue.GetByIdAsync(QueueScope, "queue-break-alpha");
        retained.Should().NotBeNull();
        await queue.SaveAsync(retained! with
        {
            Status = ReconciliationBreakQueueStatus.Open,
            LifecycleState = ReconciliationCaseLifecycleState.Reopened,
            LastUpdatedAt = DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
            EvidenceLinks =
            [
                StatementCaseworkHandoffObligation.CreatePendingMarker("resolve-alpha")
            ],
            BlockedOutputs = ["FinalReport", "PeriodClose"]
        });
        runs.Cases = [BuildReconciliationCase("reopened-case-v2", "Open")];

        var projected = await service.GetAsync(
            completed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        projected.Should().NotBeNull();
        projected!.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        projected.RetainedArtifacts.Should().BeEmpty(
            "a read projection must not advertise artifacts invalidated by current casework");
        projected.EvidenceReferences.Should().NotContain(originalJsonEvidence);
        (await File.ReadAllBytesAsync(workflowPath)).Should().Equal(
            persistedBeforeRead,
            "GET may project current authority but must not mutate the retained checkpoint");
        (await service.DownloadArtifactAsync(
                completed.Workflow.WorkflowId,
                originalJsonArtifact.ArtifactId,
                "tenant-alpha",
                "company-alpha"))
            .Should().BeNull("a reopened workflow must suppress stale artifact downloads");

        var reopened = await service.ResumeAsync(
            completed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        reopened.Should().NotBeNull();
        reopened!.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        reopened.Workflow.RetainedArtifacts.Should().BeEmpty();
        reopened.Workflow.EvidenceReferences.Should().NotContain(originalJsonEvidence);
        intake.PublishCount.Should().Be(1,
            "reopening current casework must use the retained Operations intake authority");
        imports.CommitCount.Should().Be(1,
            "reopening current casework must use the retained statement import");

        var reopenedQueueItem = await queue.GetByIdAsync(QueueScope, "queue-break-alpha");
        await queue.SaveAsync(reopenedQueueItem! with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            LastUpdatedAt = DateTimeOffset.Parse("2026-07-01T13:00:00Z"),
            EvidenceLinks =
            [
                StatementCaseworkHandoffObligation.CreatePendingMarker("resolve-alpha"),
                StatementCaseworkHandoffObligation.CreateCompletedMarker("resolve-alpha")
            ],
            BlockedOutputs = []
        });
        runs.Cases = [BuildReconciliationCase("resolved-case-v2", "Resolved")];

        var reResolved = await service.ResumeAsync(
            completed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        reResolved.Should().NotBeNull();
        reResolved!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        reResolved.Workflow.RetainedArtifacts.Should().HaveCount(2);
        var currentJsonArtifact = reResolved.Workflow.RetainedArtifacts.Single(item =>
            item.ArtifactId == "reconciliation-report-json");
        currentJsonArtifact.ContentHashSha256.Should().NotBe(
            originalJsonArtifact.ContentHashSha256,
            "the re-resolved report carries the current reconciliation case identity");
        reResolved.Workflow.EvidenceReferences.Should().NotContain(originalJsonEvidence);
        reResolved.Workflow.EvidenceReferences.Should().Contain(
            $"artifact:{currentJsonArtifact.ArtifactId}:sha256:{currentJsonArtifact.ContentHashSha256}");
        reResolved.Workflow.EvidenceReferences
            .Count(reference => reference.StartsWith("artifact:", StringComparison.Ordinal))
            .Should().Be(2, "only the current artifact hashes remain active");
        intake.PublishCount.Should().Be(1);
        imports.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_RenderingCheckpointRetry_ReusesPersistedTimestampAndArtifactHash()
    {
        var imports = new FakeImportService(BuildImportResult(breakCount: 1, caseCount: 1));
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService();
        var queue = await CreateStatementQueueAsync(handoffCompleted: true);
        var service = CreateService(imports, evidence, runs, queue);
        var started = await service.StartAsync(BuildCommand());
        var workflowPath = ResolveWorkflowSnapshotPath(started.Workflow.WorkflowId);
        var forcedCreatedAt = DateTimeOffset.Parse("2000-01-01T00:00:00Z");
        var retainedSnapshot = JsonNode.Parse(await File.ReadAllTextAsync(workflowPath))!.AsObject();
        retainedSnapshot["workflow"]!.AsObject()["createdAtUtc"] = forcedCreatedAt;
        await File.WriteAllTextAsync(workflowPath, retainedSnapshot.ToJsonString());
        runs.ReturnReconciled = true;
        runs.Cases = [BuildReconciliationCase("resolved-case-retry", "Resolved")];
        var artifactDirectory = Path.Combine(
            Path.GetDirectoryName(workflowPath)!,
            "artifacts");
        Directory.CreateDirectory(artifactDirectory);
        var blockedCsvPath = Path.Combine(artifactDirectory, "statement-kind-summary.csv");
        Directory.CreateDirectory(blockedCsvPath);

        var failed = await service.ResumeAsync(
            started.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        failed.Should().NotBeNull();
        failed!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Failed);
        var failedSnapshot = JsonNode.Parse(await File.ReadAllTextAsync(workflowPath))!.AsObject();
        var renderingAt = failedSnapshot["renderingReconciliationReportAtUtc"]!
            .GetValue<DateTimeOffset>();
        renderingAt.Should().NotBe(forcedCreatedAt);
        var jsonArtifactPath = Path.Combine(artifactDirectory, "statement-reconciliation-report.json");
        File.Exists(jsonArtifactPath).Should().BeTrue(
            "the JSON artifact is retained before the injected CSV checkpoint failure");
        var firstJsonBytes = await File.ReadAllBytesAsync(jsonArtifactPath);
        var firstJsonHash = Convert.ToHexString(SHA256.HashData(firstJsonBytes));
        Directory.Delete(blockedCsvPath);

        var completed = await service.ResumeAsync(
            started.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        completed.Should().NotBeNull();
        completed!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        var jsonDescriptor = completed.Workflow.RetainedArtifacts.Single(item =>
            item.ArtifactId == "reconciliation-report-json");
        jsonDescriptor.RetainedAtUtc.Should().Be(renderingAt);
        jsonDescriptor.RetainedAtUtc.Should().NotBe(forcedCreatedAt,
            "artifact retention is bound to rendering rather than workflow creation");
        jsonDescriptor.ContentHashSha256.Should().Be(firstJsonHash,
            "retrying the same persisted rendering checkpoint must reproduce the same artifact");
        (await File.ReadAllBytesAsync(jsonArtifactPath)).Should().Equal(firstJsonBytes);
        var report = JsonNode.Parse(await File.ReadAllTextAsync(jsonArtifactPath))!.AsObject();
        report["retainedAtUtc"]!.GetValue<DateTimeOffset>().Should().Be(renderingAt);
        var completedSnapshot = JsonNode.Parse(await File.ReadAllTextAsync(workflowPath))!.AsObject();
        completedSnapshot["renderingReconciliationReportAtUtc"]!
            .GetValue<DateTimeOffset>()
            .Should().Be(renderingAt);
        imports.CommitCount.Should().Be(1);
        evidence.RetainCount.Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_AfterEvidenceFailure_DoesNotRepeatCommittedImport()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer { FailNext = true };
        var runs = new FakeStatementRunWorkflowService { ReturnReconciled = true };
        var service = CreateService(imports, evidence, runs);

        var failed = await service.StartAsync(BuildCommand());

        failed.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Failed);
        failed.Workflow.RecoveryAction.Should().Contain("retained import");
        imports.CommitCount.Should().Be(1);

        var restarted = CreateService(imports, evidence, runs);
        var resumed = await restarted.ResumeAsync(
            failed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        resumed!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        imports.CommitCount.Should().Be(1);
        evidence.RetainCount.Should().Be(2);
    }

    [Fact]
    public async Task ResumeAsync_PrePublicationCheckpoint_RevalidatesOpenPeriodAndFailsClosedAfterClose()
    {
        var imports = new FakeImportService(BuildImportResult()) { FailNext = true };
        var initial = CreateService(
            imports,
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true });
        var failed = await initial.StartAsync(BuildCommand());
        var intake = new ResolvingIntakeAuthority(AccountingScope)
        {
            RejectUnlessRetainedClosedPeriodBypass = true
        };
        var restarted = CreateService(
            imports,
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true },
            intakeAuthority: intake);

        var act = () => restarted.ResumeAsync(
            failed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        await act.Should().ThrowAsync<StatementReconciliationIntakeAuthorityException>()
            .WithMessage("*closed*");
        intake.PublishCount.Should().Be(0);
        intake.LastAllowClosedPeriodForRetainedWorkflow.Should().BeFalse(
            "a checkpoint that has not published Operations authority cannot bypass the period-open gate");
    }

    [Fact]
    public async Task ResumeAsync_ModifiedRetainedInput_FailsBeforeRetryingImport()
    {
        var imports = new FakeImportService(BuildImportResult()) { FailNext = true };
        var service = CreateService(
            imports,
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true });
        var failed = await service.StartAsync(BuildCommand());
        var retainedInputPath = Path.Combine(
            _root,
            "reporting",
            "statement-reconciliation-report",
            failed.Workflow.WorkflowId,
            "input",
            "broker-statement.csv");
        await File.WriteAllTextAsync(
            retainedInputPath,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA,MSFT,999,1,0,position,2026-06-30");

        var resumed = await service.ResumeAsync(
            failed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        resumed.Should().NotBeNull();
        resumed!.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.Failed);
        resumed.Workflow.FailureReason.Should().Contain("content-addressed identity");
        imports.CommitCount.Should().Be(1,
            "retained bytes must be hash-verified before a failed import is retried");
    }

    [Fact]
    public async Task GetAsync_DifferentTenant_FailsClosed()
    {
        var service = CreateService(
            new FakeImportService(BuildImportResult()),
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true });
        var completed = await service.StartAsync(BuildCommand());

        var act = () => service.GetAsync(
            completed.Workflow.WorkflowId,
            "tenant-beta",
            "company-alpha");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task StartAsync_SameContentWithDifferentMappingPolicy_CreatesDistinctWorkflow()
    {
        var imports = new FakeImportService(BuildImportResult());
        var service = CreateService(
            imports,
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true });
        var firstCommand = BuildCommand();
        var secondCommand = firstCommand with
        {
            Import = firstCommand.Import with
            {
                Document = firstCommand.Import.Document with { MappingProfileId = "mapping-profile-v2" }
            }
        };

        var first = await service.StartAsync(firstCommand);
        var second = await service.StartAsync(secondCommand);

        second.Workflow.WorkflowId.Should().NotBe(first.Workflow.WorkflowId,
            "mapping policy is part of the authoritative import identity");
        imports.CommitCount.Should().Be(2);
    }

    [Fact]
    public async Task StartAsync_LegacyPersistedWorkflow_ResumesWithoutAdvertisingLegacyRoutes()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService { ReturnReconciled = true };
        var intake = new ResolvingIntakeAuthority(AccountingScope);
        var service = CreateService(imports, evidence, runs, intakeAuthority: intake);
        var command = BuildCommand();
        var completed = await service.StartAsync(command);
        var legacyWorkflowId = completed.Workflow.WorkflowId.Replace(
            "statement-reconciliation-report-",
            "statement-report-",
            StringComparison.Ordinal);
        var currentDirectory = Path.Combine(
            _root,
            "reporting",
            "statement-reconciliation-report",
            completed.Workflow.WorkflowId);
        var legacyDirectory = Path.Combine(
            _root,
            "reporting",
            "statement-to-report",
            legacyWorkflowId);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyDirectory)!);
        Directory.Move(currentDirectory, legacyDirectory);
        var snapshotPath = Path.Combine(legacyDirectory, "workflow.json");
        var snapshot = await File.ReadAllTextAsync(snapshotPath);
        snapshot = snapshot
            .Replace(completed.Workflow.WorkflowId, legacyWorkflowId, StringComparison.Ordinal)
            .Replace("RenderingReconciliationReport", "RenderingReport", StringComparison.Ordinal)
            .Replace(
                "/api/workstation/reconciliation/statement-reconciliation-report/",
                "/api/workstation/reconciliation/statement-to-report/",
                StringComparison.Ordinal);
        await File.WriteAllTextAsync(snapshotPath, snapshot);

        var restarted = CreateService(imports, evidence, runs, intakeAuthority: intake);
        var resumed = await restarted.StartAsync(command);

        resumed.Workflow.WorkflowId.Should().Be(legacyWorkflowId);
        resumed.Workflow.StatusRoute.Should().StartWith(
            "/api/workstation/reconciliation/statement-reconciliation-report/");
        resumed.Workflow.ResumeRoute.Should().StartWith(
            "/api/workstation/reconciliation/statement-reconciliation-report/");
        resumed.Workflow.RetainedArtifacts.Should().OnlyContain(artifact =>
            artifact.DownloadRoute.StartsWith(
                "/api/workstation/reconciliation/statement-reconciliation-report/",
                StringComparison.Ordinal));
        imports.CommitCount.Should().Be(1);
        evidence.RetainCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_PreScopeSnapshot_IsDiscoveredAndBoundWithoutCreatingSecondWorkflow(
        bool useLegacyLocation)
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService { ReturnReconciled = true };
        var command = BuildCommand();
        var preScopeService = CreateService(imports, evidence, runs);
        var completed = await preScopeService.StartAsync(command);
        var expectedWorkflowId = completed.Workflow.WorkflowId;
        if (useLegacyLocation)
        {
            expectedWorkflowId = await MoveWorkflowToLegacyLocationAsync(completed.Workflow.WorkflowId);
        }
        await RemoveAuthoritativeIntakeFromSnapshotAsync(expectedWorkflowId);
        var incompleteRead = () => preScopeService.GetAsync(
            expectedWorkflowId,
            "tenant-alpha",
            "company-alpha");
        var incompleteFailure = await incompleteRead.Should()
            .ThrowAsync<StatementReconciliationIntakeAuthorityException>();
        incompleteFailure.Which.Code.Should().Be("STATEMENT_INTAKE_PUBLICATION_INCOMPLETE",
            "a legacy Completed marker must not be advertised before Operations publication is restored");

        var intake = new ResolvingIntakeAuthority(AccountingScope)
        {
            RejectUnlessRetainedClosedPeriodBypass = true
        };
        var restarted = CreateService(imports, evidence, runs, intakeAuthority: intake);

        var migrated = await restarted.StartAsync(command);
        var replayed = await restarted.StartAsync(command);

        migrated.Workflow.WorkflowId.Should().Be(expectedWorkflowId);
        migrated.Workflow.AccountingScope.Should().BeEquivalentTo(
            new StatementReconciliationAccountingScopeDto(
                AccountingScope.FundProfileId,
                AccountingScope.LedgerBookId,
                AccountingScope.AccountingPeriodId,
                AccountingScope.AsOfDate));
        migrated.Workflow.OperationsWorkflowId.Should().Be(OperationsWorkflowId);
        replayed.Workflow.WorkflowId.Should().Be(expectedWorkflowId);
        replayed.Workflow.Version.Should().Be(migrated.Workflow.Version);
        imports.CommitCount.Should().Be(1,
            "scope-aware restart must migrate the retained snapshot instead of importing a second workflow");
        evidence.RetainCount.Should().Be(1);
        intake.ResolveCount.Should().Be(2);
        intake.LastAllowClosedPeriodForRetainedWorkflow.Should().BeTrue(
            "a completed retained workflow may revalidate immutable ownership after its period closes");
        intake.PublishCount.Should().Be(1,
            "a legacy completed snapshot must publish missing Operations authority before it can remain completed");
        CountRetainedWorkflowDirectories().Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_MissingIntakeAuthority_FailsBeforeRetainingInputOrEvidence()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer();
        var service = new StatementReconciliationReportWorkflowService(
            imports,
            evidence,
            new FakeStatementRunWorkflowService { ReturnReconciled = true },
            _root);

        var act = () => service.StartAsync(BuildCommand());

        var failure = await act.Should()
            .ThrowAsync<StatementReconciliationIntakeAuthorityException>();
        failure.Which.Code.Should().Be("STATEMENT_INTAKE_AUTHORITY_UNAVAILABLE");
        imports.CommitCount.Should().Be(0);
        evidence.RetainCount.Should().Be(0);
        Directory.Exists(_root).Should().BeFalse(
            "missing intake authority must fail before input, evidence, artifacts, or completion can be retained");
    }

    [Fact]
    public void AddEvidenceWorkflowFabric_MissingIntakeAuthority_FailsServiceResolution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStatementImportCommitService>(
            new FakeImportService(BuildImportResult()));
        services.AddSingleton<IStatementImportEvidenceRetainer>(
            new FakeEvidenceRetainer());
        services.AddSingleton<IStatementRunWorkflowService>(
            new FakeStatementRunWorkflowService { ReturnReconciled = true });
        services.AddEvidenceWorkflowFabric();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<StatementReconciliationReportWorkflowService>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IStatementReconciliationIntakeAuthority*");
    }

    [Fact]
    public async Task PreRenameServiceAdapter_DirectConstructionWithoutAuthority_FailsClosedBeforeRetention()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer();
        var canonicalCommand = BuildCommand();
#pragma warning disable CS0618 // Verifies fail-closed source compatibility for pre-rename callers.
        var service = new StatementToReportWorkflowService(
            imports,
            evidence,
            new FakeStatementRunWorkflowService { ReturnReconciled = true },
            _root);

        var act = () => service.StartAsync(
            new StatementToReportStartCommand(
                canonicalCommand.Import,
                canonicalCommand.TenantId,
                canonicalCommand.CompanyId));

        var failure = await act.Should()
            .ThrowAsync<StatementReconciliationIntakeAuthorityException>();
        failure.Which.Code.Should().Be("STATEMENT_INTAKE_AUTHORITY_UNAVAILABLE");
        imports.CommitCount.Should().Be(0);
        evidence.RetainCount.Should().Be(0);
        Directory.Exists(_root).Should().BeFalse();
#pragma warning restore CS0618
    }

    private StatementReconciliationReportWorkflowService CreateService(
        IStatementImportCommitService imports,
        IStatementImportEvidenceRetainer evidence,
        IStatementRunWorkflowService runs,
        IReconciliationBreakQueueRepository? breakQueue = null,
        IStatementReconciliationIntakeAuthority? intakeAuthority = null)
        => new(
            imports,
            evidence,
            runs,
            _root,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            breakQueue,
            intakeAuthority ?? new ResolvingIntakeAuthority(AccountingScope));

    private static StatementReconciliationReportStartCommand BuildCommand()
        => new(
            new StatementImportCommitRequest(
                new StatementSourceDocument(
                    "broker-statement.csv",
                    "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA,AAPL,1,100,0,position,2026-06-30"u8.ToArray()),
                ConnectorId: "csv",
                SourceKind: "broker",
                SourceInstitution: "Broker Alpha",
                FundAccountId: "fund-account-alpha",
                ExternalAccountId: "external-alpha",
                PeriodStart: new DateOnly(2026, 6, 1),
                PeriodEnd: new DateOnly(2026, 6, 30),
                ToleranceProfileId: null,
                ImportedBy: "operator-alpha"),
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha");

    private static StatementImportCommitResultDto BuildImportResult(int breakCount = 0, int caseCount = 0)
        => new(
            RunId: "statement-run-alpha",
            Duplicate: false,
            RecordCount: 2,
            KindSummaries:
            [
                new StatementKindSummaryDto("Position", 1, []),
                new StatementKindSummaryDto("Transaction", 1, [])
            ],
            BreakCount: breakCount,
            CaseCount: caseCount,
            RetainedSourcePath: "reconciliation/statement-connector-imports/source.csv",
            RetainedCanonicalPath: "reconciliation/statement-connector-imports/canonical.csv",
            Status: "Imported",
            NextAction: "Review reconciliation.")
        {
            BreakIds = breakCount == 0 ? [] : ["break-alpha"],
            CaseIds = caseCount == 0 ? [] : ["case-alpha"]
        };

    private static ReconciliationCase BuildReconciliationCase(
        string caseId,
        string status)
        => new(
            caseId,
            "statement-run-alpha",
            status,
            "Statement casework state changed.",
            1m,
            "Current authoritative statement reconciliation state.",
            DateTimeOffset.Parse("2026-06-30T12:00:00Z"),
            []);

    private string ResolveWorkflowSnapshotPath(string workflowId)
        => Path.Combine(
            _root,
            "reporting",
            workflowId.StartsWith(
                "statement-report-",
                StringComparison.Ordinal)
                ? "statement-to-report"
                : "statement-reconciliation-report",
            workflowId,
            "workflow.json");

    private async Task<string> MoveWorkflowToLegacyLocationAsync(string workflowId)
    {
        var legacyWorkflowId = workflowId.Replace(
            "statement-reconciliation-report-",
            "statement-report-",
            StringComparison.Ordinal);
        var currentDirectory = Path.Combine(
            _root,
            "reporting",
            "statement-reconciliation-report",
            workflowId);
        var legacyDirectory = Path.Combine(
            _root,
            "reporting",
            "statement-to-report",
            legacyWorkflowId);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyDirectory)!);
        Directory.Move(currentDirectory, legacyDirectory);
        var snapshotPath = Path.Combine(legacyDirectory, "workflow.json");
        var snapshot = await File.ReadAllTextAsync(snapshotPath);
        snapshot = snapshot
            .Replace(workflowId, legacyWorkflowId, StringComparison.Ordinal)
            .Replace("RenderingReconciliationReport", "RenderingReport", StringComparison.Ordinal)
            .Replace(
                "/api/workstation/reconciliation/statement-reconciliation-report/",
                "/api/workstation/reconciliation/statement-to-report/",
                StringComparison.Ordinal);
        await File.WriteAllTextAsync(snapshotPath, snapshot);
        return legacyWorkflowId;
    }

    private async Task RemoveAuthoritativeIntakeFromSnapshotAsync(string workflowId)
    {
        var snapshotPath = new[]
            {
                Path.Combine(
                    _root,
                    "reporting",
                    "statement-reconciliation-report",
                    workflowId,
                    "workflow.json"),
                Path.Combine(
                    _root,
                    "reporting",
                    "statement-to-report",
                    workflowId,
                    "workflow.json")
            }
            .Single(File.Exists);
        var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
        snapshot["request"]!.AsObject()["accountingScope"] = null;
        var workflow = snapshot["workflow"]!.AsObject();
        workflow["accountingScope"] = null;
        workflow["operationsWorkflowId"] = null;
        var evidenceReferences = workflow["evidenceReferences"]!.AsArray();
        for (var index = evidenceReferences.Count - 1; index >= 0; index--)
        {
            if (evidenceReferences[index]?.GetValue<string>()
                .StartsWith("operations-workflow:", StringComparison.Ordinal) == true)
            {
                evidenceReferences.RemoveAt(index);
            }
        }

        await File.WriteAllTextAsync(snapshotPath, snapshot.ToJsonString());
    }

    private int CountRetainedWorkflowDirectories()
    {
        var reportingRoot = Path.Combine(_root, "reporting");
        if (!Directory.Exists(reportingRoot))
        {
            return 0;
        }

        return Directory.EnumerateDirectories(
                reportingRoot,
                "statement-*-*",
                SearchOption.AllDirectories)
            .Count(path => File.Exists(Path.Combine(path, "workflow.json")));
    }

    private async Task<FileReconciliationBreakQueueRepository> CreateStatementQueueAsync(
        bool handoffCompleted)
    {
        var queue = new FileReconciliationBreakQueueRepository(
            Path.Combine(_root, "queue", Guid.NewGuid().ToString("N")),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var evidence = new List<string>
        {
            StatementCaseworkHandoffObligation.CreatePendingMarker("resolve-alpha")
        };
        if (handoffCompleted)
        {
            evidence.Add(StatementCaseworkHandoffObligation.CreateCompletedMarker("resolve-alpha"));
        }

        await queue.CreateIfMissingAsync(QueueScope, new ReconciliationBreakQueueItem(
            BreakId: "queue-break-alpha",
            RunId: "statement-run-alpha",
            StrategyName: "Statement reconciliation",
            Category: ReconciliationBreakCategory.ExternalStatementMismatch,
            Status: ReconciliationBreakQueueStatus.Resolved,
            Variance: 10m,
            Reason: "Statement variance",
            AssignedTo: "operations-controller",
            DetectedAt: DateTimeOffset.Parse("2026-06-30T12:00:00Z"),
            LastUpdatedAt: DateTimeOffset.Parse("2026-06-30T13:00:00Z"),
            LifecycleState: ReconciliationCaseLifecycleState.Resolved,
            EvidenceLinks: evidence,
            SourceType: "statement",
            SourceImportId: "statement-run-alpha",
            SourceBreakId: "break-alpha",
            SourceFingerprint: new string('a', 64),
            FundAccountId: "09dfe63d-e359-411d-a201-791a00327a67",
            LedgerBookId: Guid.Parse("0f55a7b7-3709-4617-b493-cd852405186e"),
            AccountingPeriodId: "9f9a040b-5138-4bd9-a401-6c7508f10110",
            AsOfDate: new DateOnly(2026, 6, 30),
            Disposition: ReconciliationBreakDispositionDto.Resolved,
            DispositionEvidenceHash: new string('b', 64),
            BlockedOutputs: handoffCompleted ? [] : ["FinalReport", "PeriodClose"])
        {
            FundProfileId = "fund-alpha",
            TenantId = QueueScope.TenantId,
            CompanyId = QueueScope.CompanyId
        });
        return queue;
    }

    private sealed class FakeImportService(StatementImportCommitResultDto result) : IStatementImportCommitService
    {
        public int CommitCount { get; private set; }
        public bool FailNext { get; init; }
        private bool _failed;

        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default)
        {
            CommitCount++;
            if (FailNext && !_failed)
            {
                _failed = true;
                throw new IOException("Statement import failed before commit.");
            }

            return Task.FromResult(result);
        }

        public Task<StatementImportValidationResult> ValidateAsync(
            StatementSourceDocument document,
            string? connectorId,
            CancellationToken ct = default)
            => Task.FromResult(new StatementImportValidationResult(true, result.RecordCount, []));
    }

    private sealed class FakeEvidenceRetainer : IStatementImportEvidenceRetainer
    {
        public int RetainCount { get; private set; }
        public bool FailNext { get; set; }

        public Task<StatementImportCommitResultDto> RetainAsync(
            StatementImportCommitResultDto result,
            StatementImportEvidenceBridgeRequest request,
            CancellationToken ct = default)
        {
            RetainCount++;
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("Evidence store temporarily unavailable.");
            }

            return Task.FromResult(result with
            {
                EvidenceVaultIdentity = new EvidenceVaultIdentityDto(
                    "vault-alpha",
                    "statement-run",
                    result.RunId,
                    "evidence/manifest.json",
                    "/api/workstation/evidence/vault-alpha",
                    DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
                    new string('A', 64),
                    1,
                    "File")
            });
        }
    }

    private sealed class FakeStatementRunWorkflowService : IStatementRunWorkflowService
    {
        public bool ReturnReconciled { get; set; }
        public IReadOnlyList<ReconciliationCase> Cases { get; set; } = [];

        public Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StatementRunWorkflowResult?>(
                ReturnReconciled ? new StatementRunWorkflowResult(null!, [], Cases) : null);

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
    }

    private sealed class ResolvingIntakeAuthority(StatementAccountingScope scope)
        : IStatementReconciliationIntakeAuthority
    {
        public int ResolveCount { get; private set; }
        public int PublishCount { get; private set; }
        public bool RejectUnlessRetainedClosedPeriodBypass { get; init; }
        public bool LastAllowClosedPeriodForRetainedWorkflow { get; private set; }

        public Task<StatementAccountingScope> ResolveAccountingScopeAsync(
            StatementReconciliationIntakeScopeRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ResolveCount++;
            LastAllowClosedPeriodForRetainedWorkflow =
                request.AllowClosedPeriodForRetainedWorkflow;
            if (RejectUnlessRetainedClosedPeriodBypass
                && !request.AllowClosedPeriodForRetainedWorkflow)
            {
                throw new StatementReconciliationIntakeAuthorityException(
                    "STATEMENT_ACCOUNTING_PERIOD_CLOSED",
                    "The accounting period is closed.");
            }

            return Task.FromResult(scope);
        }

        public Task<StatementReconciliationIntakeReceipt> PublishAsync(
            string statementWorkflowId,
            StatementImportCommitResultDto import,
            StatementAccountingScope accountingScope,
            string tenantId,
            string companyId,
            string actor,
            string sourceInstitution,
            IReadOnlyList<string> evidenceReferences,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            PublishCount++;
            return Task.FromResult(new StatementReconciliationIntakeReceipt(
                accountingScope,
                OperationsWorkflowId,
                PublishedCaseCount: import.CaseCount,
                evidenceReferences
                    .Append($"operations-workflow:{OperationsWorkflowId:D}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
