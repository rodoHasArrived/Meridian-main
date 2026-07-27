using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the authoritative month-end statement-to-delivery scenario where retained statement
/// intake starts the existing Operations Continuity workflow and that same workflow remains
/// correlated through close, certified ClientPackage release, and verified delivery receipts.
/// </summary>
[Trait("Category", "Scenario")]
public sealed class StatementToDeliveryAuthorityTests
{
    private const string StatementRunId = "statement-run-june-2026";
    private const string StatementBreakId = "statement-break-june-2026";
    private const string StatementCaseId = "case:statement-break-june-2026";
    private static readonly DateTimeOffset ReportingNow =
        new(2026, 7, 31, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AsOfDate = new(2026, 6, 30);
    private static readonly Guid LedgerBookId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AccountingPeriodId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FundProfileId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Scenario_MonthEndClose_CertifiedClientPackageReleasesAndRetainsDeliveryReceipt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var fundAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using var statementIntake = await StartStatementWorkflowAsync(fundAccountId, ct);
        statementIntake.Execution.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        statementIntake.Execution.Workflow.EvidenceVaultIdentity.Should().NotBeNull();
        statementIntake.Execution.Workflow.AccountingScope.Should().BeEquivalentTo(
            new StatementAccountingScope(
                FundProfileId.ToString("D"),
                LedgerBookId,
                AccountingPeriodId,
                AsOfDate));
        statementIntake.Execution.Workflow.OperationsWorkflowId.Should().NotBeNull();
        statementIntake.Execution.Workflow.RetainedArtifacts.Should().BeEmpty(
            "the statement reconciliation report cannot render before its exact casework handoff completes");
        var importedOperations = await statementIntake.Operations.GetAsync(
            statementIntake.Execution.Workflow.OperationsWorkflowId!.Value,
            ct);
        importedOperations.Should().NotBeNull();
        importedOperations!.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Imported);

        var queuedCase = (await statementIntake.Queue.GetAllAsync(ct: ct))
            .Should().ContainSingle("one retained statement variance requires governed casework")
            .Which;
        var assigned = await statementIntake.Casework.ApplyAsync(
            CaseworkCommand(queuedCase, ReconciliationCaseworkAction.Assign, "assign") with
            {
                Assignee = "fund-ops"
            },
            ct);
        assigned.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var investigating = await statementIntake.Casework.ApplyAsync(
            CaseworkCommand(
                assigned.Item!,
                ReconciliationCaseworkAction.TransitionStatus,
                "investigate") with
            {
                Status = ReconciliationCaseLifecycleState.Investigating
            },
            ct);
        investigating.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var resolvedCase = await statementIntake.Casework.ApplyAsync(
            CaseworkCommand(
                investigating.Item!,
                ReconciliationCaseworkAction.Resolve,
                "resolve") with
            {
                RootCauseCode = "BrokerCashTiming",
                ResolutionCode = "LedgerAdjusted",
                Note = "Corrected the statement timing difference with retained ledger evidence.",
                EvidenceLinks = (investigating.Item!.EvidenceLinks ?? [])
                    .Append("ledger-event:statement-june-2026-adjustment")
                    .ToArray(),
                ApprovalActor = "fund-controller",
                ApprovalReference = "approval://statement-june-2026"
            },
            ct);
        resolvedCase.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        resolvedCase.Item.Should().NotBeNull();
        resolvedCase.Item!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Resolved);
        StatementCaseworkHandoffObligation.HasPending(resolvedCase.Item).Should().BeFalse();
        StatementCaseworkHandoffObligation.HasCompleted(resolvedCase.Item).Should().BeTrue();
        (await statementIntake.Operations.GetTimelineAsync(
                statementIntake.Execution.Workflow.OperationsWorkflowId.Value,
                ct))
            .SelectMany(static entry => entry.References)
            .Should().ContainSingle(reference =>
                reference.Source == "statement-reconciliation-casework");

        var reconciledStatement = await statementIntake.Workflow.ResumeAsync(
            statementIntake.Execution.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha",
            ct);
        reconciledStatement.Should().NotBeNull();
        reconciledStatement!.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.Completed);
        reconciledStatement.Workflow.OperationsWorkflowId.Should().Be(
            statementIntake.Execution.Workflow.OperationsWorkflowId);
        reconciledStatement.Workflow.RetainedArtifacts.Should().HaveCount(2,
            "the existing statement reconciliation report workflow owns the retained intake report");

        var (closedWorkflow, closeAudit) = await CloseOperationsWorkflowAsync(
            statementIntake.Operations,
            statementIntake.AuditStore,
            statementIntake.Execution.Workflow.OperationsWorkflowId.Value,
            fundAccountId,
            ct);
        closedWorkflow.WorkflowId.Should().Be(
            statementIntake.Execution.Workflow.OperationsWorkflowId.Value,
            "posting, close, certification, and delivery must continue the workflow started by statement intake");
        var closeCompletion = BuildCloseCompletion(closedWorkflow, closeAudit);

        var authoritativeSource = new ScenarioAuthoritativeSource(
            FundProfileId,
            LedgerBookId,
            AccountingPeriodId);
        var evidenceStore = new InMemoryReportingReconciliationEvidenceStore();
        var evidenceRetention = new ReportingReconciliationEvidenceRetentionService(
            evidenceStore,
            authoritativeSource);
        var reportingParameters = Parameters(FundProfileId);
        var makerAccess = Access("report-maker");
        var retainedBreakEvidence =
            AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
                await statementIntake.Queue.GetAllAsync(ct: ct),
                FundProfileId.ToString("D"),
                LedgerBookId,
                AccountingPeriodId,
                AsOfDate,
                expectedOpenBreakCount: 0);
        retainedBreakEvidence.Should().ContainSingle(item =>
            item.Disposition == ReconciliationBreakDispositionDto.Resolved);
        var retainedClose = await evidenceRetention.RetainCompletionAsync(
            reportingParameters,
            makerAccess,
            new ReportingReconciliationCompletionEvidence(
                $"operations-close-{closedWorkflow.WorkflowId:N}-v{closedWorkflow.Version}",
                HashObject(new
                {
                    closedWorkflow.WorkflowId,
                    closedWorkflow.Version,
                    closeCompletion.ApprovalId,
                    closeCompletion.ClosePackageId,
                    closeCompletion.CloseAuditEventId
                }),
                closeAudit.OccurredAtUtc.ToUniversalTime(),
                HasOpenBreaks: false,
                [
                    $"operations-workflow:{closedWorkflow.WorkflowId:D}"
                ],
                BreakEvidence: retainedBreakEvidence,
                CloseWorkflowCompletion: closeCompletion),
            ct);

        var certification = new ReportingRunCertificationService(
            authoritativeSource,
            new ReportingReconciliationEvidenceSource(evidenceStore, statementIntake.Queue));
        var template = Template();
        var certified = await certification.CertifyAsync(
            template,
            Readiness(reportingParameters),
            makerAccess,
            ct);

        certified.Snapshot.ReconciliationCheckpointId
            .Should().Be(retainedClose.ReconciliationCheckpointId);
        certified.Snapshot.RequiresCertifiedLedgerPresentation.Should().BeTrue(
            "the authoritative client package must reuse the checkpoint-bound partners-capital presentation");
        certified.AuthoritativeSource.EvidenceIds.Should().ContainSingle(static evidence =>
            evidence.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        certified.Readiness.Checks
            .Single(check => check.CheckId == "exact-reconciliation-evidence")
            .EvidenceReferences.Should().Contain(
                $"operations-workflow:{closedWorkflow.WorkflowId:D}:version:{closedWorkflow.Version}:closed");

        var orchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => ReportingNow);
        var manifest = await orchestration.ExecuteAsync(
            Job(template, certified),
            ct);
        var primaryDeclarations = ReportingArtifactDeclaration.Build(manifest)
            .Where(static artifact => artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput)
            .OrderBy(static artifact => artifact.ContentType, StringComparer.Ordinal)
            .ToArray();

        var governanceRepository = new MemoryGovernanceRepository();
        var artifactStore = new MemoryArtifactStore(ReportingNow);
        var artifactCatalog = new MemoryArtifactCatalog();
        var artifactAudit = new RecordingArtifactAuditStore();
        var clock = new FixedTimeProvider(ReportingNow);
        var artifactVault = new ReportingArtifactVaultService(
            artifactStore,
            artifactCatalog,
            artifactAudit,
            clock);
        var coordinator = new ReportingGovernanceCoordinatorService(
            new ReportingGovernanceService(
                governanceRepository,
                clock,
                prefix => $"{prefix}-{Guid.NewGuid():N}"),
            governanceRepository,
            certification,
            orchestration,
            artifactVault,
            new DeterministicReportingCertifiedArtifactProducer(
                new DocumentsReportingPrimaryDocumentRenderer(),
                authoritativeSource),
            new ReportingArtifactRetentionAuthorityProvider(),
            new GovernedReportingRestatementChangedLineResolver(),
            new UnusedRestatementCertificationInputProvider());
        var maker = GovernanceCaller("report-maker");
        var approver = GovernanceCaller("fund-controller");
        var releaser = GovernanceCaller("client-report-releaser");

        var governed = await coordinator.CreateFromCompletedCertifiedManifestAsync(
            manifest.RunId,
            maker,
            ct);
        governed = await coordinator.ValidateAsync(governed.RunId, governed.Version, maker, ct);
        governed = await coordinator.SubmitAsync(governed.RunId, governed.Version, maker, ct);
        governed = await coordinator.ApproveAsync(
            governed.RunId,
            governed.Version,
            "Controller reviewed the exact close receipt and both client documents.",
            approver,
            ct);
        governed = await coordinator.ReleaseAsync(
            governed.RunId,
            governed.Version,
            releaser,
            ct);

        governed.GovernanceState.Should().Be(GovernedReportingState.Released);
        governed.Approval!.Authority.ActorId.Should().Be("fund-controller");
        governed.Release!.Authority.ActorId.Should().Be("client-report-releaser");
        governed.Release.Authority.ActorId.Should().NotBe(governed.Approval.Authority.ActorId);
        authoritativeSource.PresentationResolveCount.Should().BeGreaterThan(0,
            "PDF/XLSX production must resolve the exact certified partners-capital report pack");
        ReportingGovernanceAuditChain.Verify(governed.AuditTrail).Should().BeTrue();
        governed.Release.EvidenceIds.Should().Contain(
        [
            $"reconciliation:{retainedClose.ReconciliationCheckpointId}",
            $"operations-workflow:{closedWorkflow.WorkflowId:D}:version:{closedWorkflow.Version}:closed",
            $"operations-close-package:{closeCompletion.ClosePackageId}:{closeCompletion.ClosePackageEvidenceHash}",
            $"operations-close-audit:{closeCompletion.CloseAuditEventId}:{closeCompletion.CloseAuditHash}"
        ]);

        primaryDeclarations.Should().HaveCount(2);
        primaryDeclarations.Select(static artifact => artifact.ContentType).Should().BeEquivalentTo(
        [
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        ]);
        foreach (var declaration in primaryDeclarations)
        {
            var released = await coordinator.DownloadRetainedArtifactAsync(
                governed.RunId,
                declaration.ArtifactId,
                releaser,
                ct);
            released.Descriptor.ContentType.Should().Be(declaration.ContentType);
            released.Descriptor.ContentHashSha256.Should().Be(HashBytes(released.Content));
            governed.Release.Artifacts.Should().Contain(reference =>
                reference.ArtifactId == declaration.ArtifactId
                && reference.ArtifactHash == released.Descriptor.ContentHashSha256
                && reference.ByteLength == released.Content.LongLength);
        }

        artifactAudit.Events.Should().Contain(eventRecord =>
            eventRecord.Action == ReportingArtifactAuditAction.ArtifactRetained
            && eventRecord.PackageId == ReportingArtifactPackageIdentity.Create(governed));
        artifactAudit.Events.Should().Contain(eventRecord =>
            eventRecord.Action == ReportingArtifactAuditAction.ContentAccessed
            && primaryDeclarations.Any(declaration =>
                declaration.ArtifactId == eventRecord.ArtifactId));
        artifactAudit.Receipts.Select(static receipt => receipt.Sequence)
            .Should().Equal(Enumerable.Range(1, artifactAudit.Receipts.Count).Select(static value => (long)value));
        artifactAudit.Receipts.Skip(1).Should().OnlyContain(receipt =>
            receipt.PreviousHash != null);

        var accessGrantStore = new MemoryAccessGrantStore();
        var accessGrantService = new ReportingAccessGrantService(accessGrantStore, clock);
        var deliveryStore = new MemoryDeliveryStore();
        var releaseVerifier = new GovernanceReportingReleaseAuthorizationVerifier(
            governanceRepository,
            new ReportingReleasedArtifactIntegrityGate(artifactCatalog, artifactStore));
        var relay = new AcceptingRelayClient();
        var relayTransport = new HttpRelayReportingDeliveryTransport(
            relay,
            accessGrantService,
            new HmacReportingDeliveryGrantCredentialDeriver(
                Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray()),
            clock);
        var dispatcher = new ReportingDeliveryDispatcher(
            deliveryStore,
            [relayTransport],
            releaseVerifier,
            clock);
        var distribution = new ReportingSecureDistributionApplicationService(
            governanceRepository,
            artifactCatalog,
            dispatcher,
            deliveryStore,
            accessGrantService,
            accessGrantStore,
            artifactVault,
            releaseVerifier,
            new AcceptingProviderReceiptAuthenticator(),
            clock,
            SecureReportingDistributionOptions.Default with
            {
                ExternalAccessBaseUri = "https://reports.example.test/portal/reporting/access",
                WorkerId = "statement-delivery-test-worker"
            },
            new ConfiguredReportingRecipientDestinationResolver(
            [
                new ReportingRecipientDestinationBinding(
                    "tenant-alpha",
                    "company-alpha",
                    "client-report-recipients",
                    "http-relay",
                    "client-operations@example.test",
                    ReportingAccessPrincipalKind.Group)
            ]));
        var distributionAuthority = new ReportingDistributionAuthority(
            "client-report-releaser",
            "tenant-alpha",
            "company-alpha",
            ["client-report-releaser", "client-report-recipients"],
            CanView: true,
            CanDeliver: true,
            CanAdminister: false,
            CorrelationId: $"statement-delivery:{retainedClose.ReconciliationCheckpointId}");
        var queued = await distribution.QueueDeliveryAsync(
            new SecureReportingDeliveryQueueCommand(
                governed.RunId,
                DistributionId: $"statement-delivery:{governed.RunId}",
                TransportId: "http-relay",
                RecipientPrincipalId: "client-report-recipients",
                Destination: string.Empty,
                Subject: "June 2026 client package",
                Body: "Your independently approved month-end client package is ready.",
                ArtifactIds: primaryDeclarations.Select(static artifact => artifact.ArtifactId).ToArray(),
                GrantLifetimeSeconds: 900,
                GrantMaxUses: 2,
                RecipientPrincipalKind: ReportingAccessPrincipalKind.Group),
            distributionAuthority,
            ct);

        var dispatched = await dispatcher.DispatchDueAsync(
            "statement-delivery-test-worker",
            ct);
        var accepted = dispatched.Should().ContainSingle().Subject;
        accepted.JobId.Should().Be(queued.JobId);
        accepted.PackageId.Should().Be(ReportingArtifactPackageIdentity.Create(governed));
        accepted.ReleaseAuthorization.RunId.Should().Be(governed.RunId);
        accepted.ReleaseAuthorization.ArtifactManifestHashSha256
            .Should().Be(governed.Release.ManifestHash);
        accepted.State.Should().Be(ReportingDeliveryState.Sent);
        accepted.Receipts.Should().ContainSingle(receipt =>
            receipt.Kind == ReportingDeliveryReceiptKind.Accepted
            && receipt.ProviderReference == "provider-message-statement-package");
        accepted.AccessGrantId.Should().NotBeNullOrWhiteSpace();
        relay.Messages.Should().ContainSingle(message =>
            message.PackageId == accepted.PackageId
            && message.DeliveryJobId == accepted.JobId
            && message.RecipientAccessUri.Contains(accepted.AccessGrantId!, StringComparison.Ordinal));

        var delivered = await distribution.RecordVerifiedProviderReceiptAsync(
            "http-relay",
            accepted.JobId,
            new SecureReportingDeliveryReceiptCommand(
                ProviderEventId: "provider-delivered-statement-package",
                Kind: ReportingDeliveryReceiptKind.Delivered,
                OccurredAtUtc: ReportingNow,
                ProviderReference: "provider-message-statement-package",
                EvidenceReference:
                    $"operations-close-audit:{closeCompletion.CloseAuditEventId}:{closeCompletion.CloseAuditHash}",
                Detail: "Recipient provider confirmed delivery."),
            new ReportingProviderReceiptAuthentication("timestamp", "signature"),
            ct);

        delivered.State.Should().Be(ReportingDeliveryState.Delivered);
        delivered.DistributionId.Should().Be($"statement-delivery:{governed.RunId}");
        delivered.Receipts.Select(static receipt => receipt.Kind).Should().ContainInOrder(
            ReportingDeliveryReceiptKind.Accepted,
            ReportingDeliveryReceiptKind.Delivered);
        delivered.Receipts.Should().Contain(receipt =>
            receipt.Kind == ReportingDeliveryReceiptKind.Delivered
            && receipt.EvidenceReference ==
            $"operations-close-audit:{closeCompletion.CloseAuditEventId}:{closeCompletion.CloseAuditHash}");
    }

    private static async Task<StatementChainFixture> StartStatementWorkflowAsync(
        Guid fundAccountId,
        CancellationToken ct)
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-to-delivery-authority",
            Guid.NewGuid().ToString("N"));
        var accounts = new Mock<IAccountQueryService>(MockBehavior.Strict);
        accounts
            .Setup(service => service.GetAccountAsync(
                fundAccountId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountSummaryDto(
                fundAccountId,
                AccountTypeDto.Custody,
                EntityId: null,
                FundId: FundProfileId,
                SleeveId: null,
                VehicleId: null,
                AccountCode: "CUSTODY-7842",
                DisplayName: "Northstar operating custody",
                BaseCurrency: "USD",
                Institution: "Northstar Custody",
                IsActive: true,
                EffectiveFrom: ReportingNow.AddYears(-1),
                EffectiveTo: null,
                PortfolioId: null,
                LedgerReference: null,
                StrategyId: null,
                RunId: null));
        var tenancy = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        tenancy
            .Setup(registry => registry.ResolveAsync(
                FundProfileId.ToString("D"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(
                FundProfileId.ToString("D"),
                "tenant-alpha",
                "company-alpha"));
        var ledgerBooks = new Mock<ILedgerBookService>(MockBehavior.Strict);
        ledgerBooks
            .Setup(service => service.ListBooksAsync(
                It.IsAny<LedgerBookQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LedgerBookDto(
                    LedgerBookId,
                    FundProfileId.ToString("D"),
                    FundProfileId,
                    FundStructureNodeKindDto.Fund,
                    "Primary reporting book",
                    "USD",
                    ReportingNow.AddYears(-1),
                    ReportingNow.AddDays(-1),
                    AccountingBasis: AccountingBasisKindDto.Primary)
            ]);
        ledgerBooks
            .Setup(service => service.ListPeriodsAsync(
                It.IsAny<LedgerPeriodQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LedgerPeriodDto(
                    AccountingPeriodId,
                    LedgerBookId,
                    FiscalYear: 2026,
                    PeriodNo: 6,
                    Label: "2026-06",
                    StartDate: new DateOnly(2026, 6, 1),
                    EndDate: AsOfDate,
                    Status: LedgerPeriodStatusDto.Open,
                    OpenedAt: ReportingNow.AddMonths(-1),
                    ClosedAt: null,
                    Version: 1)
            ]);

        var importResult = CleanStatementImportResult();
        var statementRun = CleanStatementRun(fundAccountId);
        var statementBreakStore = new JsonReconciliationBreakStore(dataRoot);
        var statementCaseStore = new JsonReconciliationCaseStore(dataRoot);
        await statementBreakStore.WriteAsync(statementRun.Breaks, ct);
        foreach (var statementCase in statementRun.Cases)
        {
            await statementCaseStore.SaveAsync(statementCase, ct);
        }
        var statementRuns = new ScenarioStatementRunWorkflowService(
            statementRun.Import,
            statementBreakStore,
            statementCaseStore);
        var reconciliation = new Mock<IReconciliationApiService>(MockBehavior.Strict);
        reconciliation
            .Setup(service => service.ListOpenStatementBreaksAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([StatementBreak()]);

        var derivation = new OperationsStatusDerivationService();
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var journalStore = new RecordingLedgerJournalStore(
            FundProfileId,
            LedgerBookId,
            AccountingPeriodId);
        var operations = new OperationsContinuityWorkflowService(
            new InMemoryOperationsContinuityRepository(derivation),
            auditStore,
            derivation,
            journalStore);
        var queue = new FileReconciliationBreakQueueRepository(
            Path.Combine(dataRoot, "reconciliation-casework"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var authority = new StatementReconciliationIntakeAuthority(
            accounts.Object,
            tenancy.Object,
            ledgerBooks.Object,
            operations,
            statementRuns,
            reconciliation.Object,
            queue);
        var casework = new StatementReconciliationCaseworkHandoffService(
            queue,
            statementBreakStore,
            statementCaseStore,
            statementRuns,
            operations,
            journalStore);
        var workflow = new StatementReconciliationReportWorkflowService(
            new CleanStatementImportService(importResult),
            new CleanStatementEvidenceRetainer(),
            statementRuns,
            dataRoot,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            queue,
            authority);
        var execution = await workflow.StartAsync(
            new StatementReconciliationReportStartCommand(
                new StatementImportCommitRequest(
                    new StatementSourceDocument(
                        "northstar-june-2026.csv",
                        "account,currency,amount,bookAmount\nCUSTODY-7842,USD,125000,125000"u8
                            .ToArray()),
                    ConnectorId: "csv",
                    SourceKind: "custodian",
                    SourceInstitution: "Northstar Custody",
                    FundAccountId: fundAccountId.ToString("D"),
                    ExternalAccountId: "CUSTODY-7842",
                    PeriodStart: new DateOnly(2026, 6, 1),
                    PeriodEnd: AsOfDate,
                    ToleranceProfileId: "month-end-cash",
                    ImportedBy: "statement-operations"),
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha"),
            ct);
        return new StatementChainFixture(
            dataRoot,
            execution,
            workflow,
            casework,
            operations,
            auditStore,
            queue);
    }

    private static StatementImportCommitResultDto CleanStatementImportResult()
        => new(
            RunId: StatementRunId,
            Duplicate: false,
            RecordCount: 1,
            KindSummaries: [new StatementKindSummaryDto("Cash", 1, [])],
            BreakCount: 1,
            CaseCount: 1,
            RetainedSourcePath: "reconciliation/statements/northstar-june-2026.csv",
            RetainedCanonicalPath: "reconciliation/statements/northstar-june-2026.canonical.json",
            Status: "ReviewRequired",
            NextAction: "Resolve the retained statement reconciliation case.")
        {
            BreakIds = [StatementBreakId],
            CaseIds = [StatementCaseId],
            ReconciliationCaseLinks =
            [
                new StatementImportReconciliationCaseLinkDto(
                    StatementCaseId,
                    StatementBreakId,
                    $"/api/workstation/reconciliation/cases/{StatementCaseId}",
                    "June cash timing review",
                    "Open",
                    "High",
                    "Statement cash differs from the retained ledger activity.",
                    "Review the retained statement and ledger evidence.")
            ]
        };

    private static StatementRunWorkflowResult CleanStatementRun(Guid fundAccountId)
        => new(
            new CanonicalStatementImport(
                StatementRunId,
                "Northstar Custody",
                AsOfDate,
                ReportingNow,
                "reconciliation/statements/northstar-june-2026.csv",
                new string('a', 64),
                RawRowCount: 1,
                NormalizedRowCount: 1)
            {
                SourceInstitution = "Northstar Custody",
                FundAccountId = fundAccountId.ToString("D"),
                ExternalAccountId = "CUSTODY-7842",
                StatementPeriodStart = new DateOnly(2026, 6, 1),
                StatementPeriodEnd = AsOfDate,
                OriginalFileName = "northstar-june-2026.csv",
                ImportedBy = "statement-operations",
                AccountingScope = new StatementAccountingScope(
                    FundProfileId.ToString("D"),
                    LedgerBookId,
                    AccountingPeriodId,
                    AsOfDate)
            },
            Breaks:
            [
                new ReconciliationBreakRecord(
                    StatementBreakId,
                    StatementRunId,
                    StatementRunId,
                    $"{StatementRunId}:1",
                    "AMOUNT_MISMATCH",
                    "cash",
                    100m,
                    1m,
                    ToleranceBreached: true,
                    ReportingNow.AddMinutes(-45),
                    "Open")
                {
                    EvidenceLink =
                        $"/api/workstation/reconciliation/statement-runs/{StatementRunId}#row-1"
                }
            ],
            Cases:
            [
                new ReconciliationCase(
                    StatementCaseId,
                    StatementRunId,
                    "Open",
                    "Statement cash differs from the retained ledger activity.",
                    0.5m,
                    "Manual review is required before accounting close.",
                    ReportingNow.AddMinutes(-44),
                    [])
                {
                    Owner = "fund-ops",
                    Priority = "High",
                    LastUpdatedAtUtc = ReportingNow.AddMinutes(-44),
                    LastUpdatedBy = "statement-operations",
                    Disposition = "NeedsInvestigation",
                    EvidenceReferences =
                    [
                        $"/api/workstation/reconciliation/statement-runs/{StatementRunId}#row-1"
                    ]
                }
            ]);

    private static StatementBreakDto StatementBreak() => new(
        StatementBreakId,
        StatementBreakType.CashBalanceMismatch,
        StatementValidationSeverity.Error,
        Meridian.Contracts.Workstation.StatementMatchTier.Unmatched,
        $"{StatementRunId}:1",
        "Statement cash differs from the retained ledger activity.",
        StatementAmount: 125_000m,
        BookAmount: 124_900m,
        Delta: 100m,
        Tolerance: 1m,
        Currency: "USD",
        CreatedAtUtc: ReportingNow.AddMinutes(-45),
        Status: "Open",
        InternalReference: StatementRunId,
        Owner: "fund-ops",
        LastObservedAtUtc: ReportingNow.AddMinutes(-40),
        RecommendedAction: "ReviewAndResolve",
        EvidenceLink:
            $"/api/workstation/reconciliation/statement-runs/{StatementRunId}#row-1");

    private static ReconciliationCaseworkCommand CaseworkCommand(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkAction action,
        string step) =>
        new(
            item.BreakId,
            action,
            "fund-ops",
            $"statement-delivery-{step}",
            $"statement-delivery:{StatementRunId}",
            "statement-to-delivery-authority-scenario",
            item.Version,
            Reason: "Retain reviewed statement reconciliation evidence.",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);

    private static async Task<(OperationsContinuityWorkflowDto Workflow, OperationsWorkflowAuditDto CloseAudit)>
        CloseOperationsWorkflowAsync(
            OperationsContinuityWorkflowService service,
            InMemoryOperationsWorkflowAuditStore auditStore,
            Guid workflowId,
            Guid fundAccountId,
            CancellationToken ct)
    {
        var imported = await service.GetAsync(workflowId, ct)
            ?? throw new InvalidOperationException(
                "Statement intake did not retain its Operations Continuity workflow.");
        imported.FundAccountId.Should().Be(fundAccountId);
        imported.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Imported);
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            workflowId,
            new OperationsTransitionRequestDto(
                imported.Version,
                "statement-operations",
                "Normalized retained statement activity."),
            ct);
        var mapped = await service.ResolveSecurityMasterMappingsAsync(
            workflowId,
            new OperationsSecurityMasterResolveRequestDto(
                normalized.Workflow!.Version,
                "statement-operations",
                "Resolved all statement securities."),
            ct);
        var drafted = await service.BuildLedgerDraftAsync(
            workflowId,
            new OperationsLedgerDraftRequestDto(
                mapped.Workflow!.Version,
                "statement-operations",
                PreviewId: "statement-ledger-preview-june-2026",
                IsBalanced: true,
                Rationale: "Built balanced statement-derived journal preview."),
            ct);
        var validated = await service.ValidateLedgerDraftAsync(
            workflowId,
            new OperationsLedgerValidationRequestDto(
                drafted.Workflow!.Version,
                "statement-operations",
                IsBalanced: true,
                PeriodOpen: true,
                Rationale: "Validated statement-derived journals before posting."),
            ct);
        var posted = await service.PostLedgerEntriesAsync(
            workflowId,
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "statement-operations",
                LedgerBatchId: "statement-ledger-batch-june-2026",
                PostingKind: "period-close",
                PeriodOpen: true,
                Rationale: "Posted approved statement-derived accounting journals.",
                JournalCandidate: JournalCandidate(fundAccountId)),
            ct);
        var reconciled = await service.RunReconciliationAsync(
            workflowId,
            new OperationsReconciliationRunRequestDto(
                posted.Workflow!.Version,
                "statement-operations",
                "All retained statement breaks were resolved or approved.",
                BreakCases: []),
            ct);
        var posture = await service.RefreshGatePostureAsync(
            workflowId,
            new OperationsGatePostureRequestDto(
                reconciled.Workflow!.Version,
                "statement-operations",
                ReportPackReady: true,
                ReportPackId: "statement-close-support-june-2026",
                Rationale: "Close support package is ready for controller review."),
            ct);
        var submitted = await service.SubmitForApprovalAsync(
            workflowId,
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version,
                "statement-operations",
                Reviewer: "fund-controller",
                Rationale: "Submit the clean statement close for independent approval.",
                ReportPackId: "statement-close-support-june-2026",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()),
            ct);
        var approved = await service.ApproveWorkflowAsync(
            workflowId,
            new OperationsApprovalDecisionRequestDto(
                submitted.Workflow!.Version,
                "fund-controller",
                Reviewer: "fund-controller",
                Rationale: "Controller independently approved the retained statement and close evidence.",
                ReportPackId: "statement-close-support-june-2026",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()),
            ct);
        var closed = await service.CloseWorkflowAsync(
            workflowId,
            new OperationsCloseWorkflowRequestDto(
                approved.Workflow!.Version,
                "statement-operations",
                Rationale: "Commit the approved June statement close.",
                ReportPackId: "statement-close-support-june-2026",
                ChecklistControlApprovals: RequiredChecklistControlApprovals()),
            ct);

        closed.Success.Should().BeTrue();
        closed.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Closed);
        closed.Workflow.ApprovalState.Should().Be(OperationsApprovalStateDto.Approved);
        closed.Workflow.ClosePackage.Should().NotBeNull();
        var closeAudit = (await auditStore.GetTimelineAsync(closed.Workflow.WorkflowId, ct))
            .Single(eventRecord =>
                eventRecord.EventType == "workflow-closed"
                && eventRecord.ToState == OperationsWorkflowStatusDto.Closed);
        return (closed.Workflow, closeAudit);
    }

    private static ReportingCloseWorkflowCompletionEvidence BuildCloseCompletion(
        OperationsContinuityWorkflowDto workflow,
        OperationsWorkflowAuditDto closeAudit)
    {
        var approval = workflow.Approvals
            .Where(static item => item.Status == OperationsApprovalStateDto.Approved)
            .OrderBy(static item => item.DecidedAtUtc)
            .Last();
        var closePackage = workflow.ClosePackage
            ?? throw new InvalidOperationException("Closed workflow has no close support package.");
        return new ReportingCloseWorkflowCompletionEvidence(
            workflow.WorkflowId.ToString("D"),
            workflow.Version,
            workflow.FundAccountId.ToString("D"),
            LedgerBookId.ToString("D"),
            AccountingPeriodId.ToString("D"),
            approval.ApprovalId,
            HashObject(approval),
            HashObject(new
            {
                workflow.CloseChecklist,
                closePackage.ChecklistControlApprovals
            }),
            closePackage.ClosePackageId,
            closePackage.EvidenceHash,
            closeAudit.AuditId.ToString("D"),
            closeAudit.CurrentHash);
    }

    private static ReportingTemplateMetadata Template() => new(
        "capital-account-statement",
        ReportingTemplateFamily.CapitalAccountStatement,
        "Month-end capital account statement",
        "1.0.0",
        ["cover", "capital-balance", "flows", "allocation"],
        ImmutableDictionary<string, string>.Empty,
        ReportWriterGrids: [],
        AccessPolicy: new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            Principals:
            [
                new ReportAccessPrincipalDto(
                    ReportAccessPrincipalKindDto.Group,
                    "client-report-recipients",
                    "Client report recipients")
            ],
            CompanyId: "company-alpha"));

    private static ReportingRunParametersDto Parameters(Guid fundProfileId) => new(
        new ReportingRunScopeDto(fundProfileId.ToString("D")),
        AccountingPeriodId.ToString("D"),
        AsOfDate,
        new ReportingLedgerBookSelectionDto(LedgerBookId),
        ReportingAccountingBasisDto.Gaap,
        "USD",
        ReportingConsolidationLevelDto.Fund,
        ReportingOutputFormatDto.ClientPackage,
        ReportingFinalityDto.Final,
        IncludeSupportingSchedules: true,
        IncludeEvidenceAppendix: true);

    private static ReportingRunReadinessDto Readiness(ReportingRunParametersDto parameters) => new(
        "month-end-client-package-readiness",
        ReportingNow.AddMinutes(-20),
        new VersionedReportTemplateIdDto("capital-account-statement", 1),
        parameters,
        ReportingRunReadinessStatusDto.Ready,
        CanGenerateDraft: true,
        CanGenerateFinal: true,
        Checks: [],
        BlockingReasons: [],
        EvidenceHash: new string('a', 64));

    private static ReportAccessQueryContext Access(string actor) => new(
        actor,
        ["client-report-recipients"],
        "company-alpha",
        TenantId: "tenant-alpha",
        RequireBoundScope: true);

    private static ReportingJobContract Job(
        ReportingTemplateMetadata template,
        CertifiedReportingRunContext certified) => new(
        "month-end-client-package-job",
        template.TemplateId,
        certified.Readiness.ResolvedParameters.AsOfDate,
        ReportingRunTrigger.AdHoc,
        MaxRetries: 0,
        RequestedBy: "report-maker",
        RequestedAtUtc: ReportingNow,
        DatasetRows: certified.DatasetRows,
        ReportWriterDatasetSourceId: certified.AuthoritativeSource.SourceId,
        ReportWriterDatasetSourceLabel: "Certified durable ledger journal",
        AccessPolicy: template.AccessPolicy,
        ResolvedTemplate: certified.Readiness.ResolvedTemplate,
        ResolvedParameters: certified.Readiness.ResolvedParameters,
        Readiness: certified.Readiness,
        OperationalScope: certified.OperationalScope,
        ImmutableAccessScope: certified.AccessScope,
        CertifiedSnapshot: certified.Snapshot,
        AuthoritativeSource: certified.AuthoritativeSource);

    private static ReportingGovernanceCallerContext GovernanceCaller(string actor) => new(
        actor,
        "tenant-alpha",
        "company-alpha",
        UserPermission.AdminMaintenance,
        ReportingCommandOrigin.HumanOperator,
        $"statement-delivery:{actor}",
        [actor, "client-report-recipients"]);

    private static IReadOnlyList<OperationsChecklistControlApprovalDto>
        RequiredChecklistControlApprovals() =>
    [
        new("close-gate-brokeringest", "operations-lead", ReportingNow.AddMinutes(-30)),
        new("close-gate-securitymaster", "security-master-lead", ReportingNow.AddMinutes(-29)),
        new("close-gate-ledgerposting", "ledger-lead", ReportingNow.AddMinutes(-28)),
        new("close-gate-reconciliation", "reconciliation-lead", ReportingNow.AddMinutes(-27)),
        new("close-gate-approval", "controller", ReportingNow.AddMinutes(-26)),
        new("close-gate-approval", "fund-admin", ReportingNow.AddMinutes(-25))
    ];

    private static OperationsLedgerJournalCandidateDto JournalCandidate(Guid fundAccountId)
    {
        var securityId = Guid.Parse("CB931872-F221-47C1-B922-1F61BFA93CF5");
        return new OperationsLedgerJournalCandidateDto(
            JournalEntryId: null,
            AggregateId: fundAccountId,
            PeriodId: AccountingPeriodId,
            Timestamp: new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero),
            Description: "Statement-driven month-end close posting",
            Lines:
            [
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Cash",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 100m,
                    Credit: 0m),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Interest income",
                    AccountType: nameof(LedgerAccountType.Revenue),
                    Debit: 0m,
                    Credit: 100m)
            ],
            CommandId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            SourceEventId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: "statement-close-policy",
            AccountingPolicyVersion: "1",
            RuleId: "statement-close-rule",
            RuleVersion: "1",
            PostingKind: LedgerPostingKindDto.Originating,
            Metadata: new OperationsJournalEntryMetadataDto(
                ActivityType: "statement-close",
                Symbol: "CASH",
                SecurityId: securityId,
                LedgerBook: "primary"),
            IdempotencyKey: "statement-june-2026-close-posting",
            SecurityMasterProvenance: $"security-master:{securityId:N};snapshot:statement-june-2026",
            ExpectedLedgerVersion: 1);
    }

    private static string HashObject<T>(T value) =>
        HashBytes(JsonSerializer.SerializeToUtf8Bytes(value));

    private static string HashBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ScenarioAuthoritativeSource(
        Guid fundProfileId,
        Guid ledgerBookId,
        Guid accountingPeriodId) :
        IReportingAuthoritativeSource,
        IReportingCertifiedLedgerPresentationSource
    {
        private LedgerFinancialReportPack? _canonicalReportPack;
        private ReportingCertifiedLedgerPresentationInput? _presentation;

        public int PresentationResolveCount { get; private set; }

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default) =>
            CaptureCore(parameters, accessContext, includeLedgerPresentation: false, cancellationToken);

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            ReportingAuthoritativeSourceCaptureIntent intent,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(intent);
            return CaptureCore(
                parameters,
                accessContext,
                intent.RequiresCertifiedLedgerPresentation
                    && parameters.OutputFormat == ReportingOutputFormatDto.ClientPackage,
                cancellationToken);
        }

        public ValueTask<ReportingCertifiedLedgerPresentationInput?> ResolveExactAsync(
            ReportingOutputManifest manifest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            cancellationToken.ThrowIfCancellationRequested();
            PresentationResolveCount++;
            return ValueTask.FromResult(_presentation);
        }

        private ValueTask<ReportingAuthoritativeSourceCapture> CaptureCore(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            bool includeLedgerPresentation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parameters.Scope.FundProfileId.Should().Be(fundProfileId.ToString("D"));
            parameters.LedgerBook.LedgerBookId.Should().Be(ledgerBookId);
            parameters.PeriodId.Should().Be(accountingPeriodId.ToString("D"));
            accessContext.TenantId.Should().Be("tenant-alpha");
            accessContext.CompanyId.Should().Be("company-alpha");

            var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = "Cash",
                    ["amount"] = "100.00",
                    ["entryId"] = "77777777-7777-7777-7777-777777777777",
                    ["source"] = "operations-continuity-close"
                });
            const string checkpointId = "ledger-checkpoint-month-end-june-2026";
            var checkpointHash = HashBytes(
                Encoding.UTF8.GetBytes("ledger-checkpoint-month-end-june-2026"));
            var reportPack = includeLedgerPresentation
                ? _canonicalReportPack ??= BuildCanonicalCapitalReportPack()
                : null;
            var evidenceIds = ImmutableArray.CreateBuilder<string>();
            evidenceIds.Add($"reporting-source-checkpoint:{checkpointId}:{checkpointHash}");
            if (reportPack is not null)
            {
                evidenceIds.Add(
                    $"ledger-report-pack:{reportPack.Request.ReportId}:{reportPack.Signature.PayloadChecksumSha256}");
            }
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{ledgerBookId:D}:{accountingPeriodId:D}",
                "tenant-alpha",
                "organization-alpha",
                "company-alpha",
                fundProfileId.ToString("D"),
                ledgerBookId.ToString("D"),
                accountingPeriodId.ToString("D"),
                "Gaap",
                AsOfDate,
                new DateTimeOffset(
                    AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                HighestGlobalSequence: 17,
                JournalEntryCount: 1,
                LedgerLineCount: rows.Length,
                checkpointId,
                checkpointHash,
                ReportingNow.AddMinutes(-10),
                evidenceIds.ToImmutable());
            _presentation = reportPack is null
                ? null
                : new ReportingCertifiedLedgerPresentationInput(
                    checkpointId,
                    checkpointHash,
                    DeterministicReportingCertifiedArtifactProducer.ComputeCertifiedRowsHash(rows),
                    reportPack);
            return ValueTask.FromResult(
                new ReportingAuthoritativeSourceCapture(checkpoint, rows, _presentation));
        }

        private LedgerFinancialReportPack BuildCanonicalCapitalReportPack()
        {
            var ledger = new Meridian.Ledger.Ledger();
            ledger.PostLines(
                new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
                "opening capital",
                [
                    (LedgerAccounts.Cash, 1_000_000m, 0m),
                    (LedgerAccounts.InvestorCapitalFor("client-investor"), 0m, 1_000_000m)
                ]);
            ledger.PostLines(
                new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero),
                "period capital contribution",
                [
                    (LedgerAccounts.Cash, 250_000m, 0m),
                    (LedgerAccounts.InvestorCapitalFor("client-investor"), 0m, 250_000m)
                ]);
            return LedgerReportPackBuilder.Build(
                ledger,
                new LedgerReportPackRequest(
                    "statement-delivery-capital-account",
                    fundProfileId.ToString("D"),
                    accountingPeriodId.ToString("D"),
                    new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
                    new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
                    "USD",
                    "report-maker",
                    ReportingNow));
        }
    }

    private sealed class SingleTemplateCatalog(ReportingTemplateMetadata template) :
        IReportingTemplateCatalog
    {
        public ReportingTemplateMetadata Get(string templateId) =>
            templateId == template.TemplateId
                ? template
                : throw new KeyNotFoundException(templateId);

        public IReadOnlyList<ReportingTemplateMetadata> ListTemplates() => [template];
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class UnusedRestatementCertificationInputProvider :
        IReportingRestatementCertificationInputProvider
    {
        public ValueTask<ReportingRestatementCertificationInput> ResolveAsync(
            ReportingRestatementRequest request,
            GovernedReportingRun releasedPredecessor,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ReportingRestatementCertificationInput>(
                new NotSupportedException("Restatement is outside this authority-chain scenario."));
    }

    private sealed class MemoryGovernanceRepository :
        IReportingGovernanceRepository,
        IReportingGovernanceTransaction
    {
        private readonly Dictionary<(string TenantId, string RunId), GovernedReportingRun> _runs = [];
        private readonly Dictionary<(string TenantId, string RequestId), ReportingRestatementRequest>
            _restatements = [];

        public async ValueTask<TResult> ExecuteTransactionAsync<TResult>(
            Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            await operation(this, cancellationToken);

        public ValueTask<GovernedReportingRun?> GetRunAsync(
            string tenantId,
            string runId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_runs.GetValueOrDefault((tenantId, runId)));

        public ValueTask<IReadOnlyList<GovernedReportingRun>> ListRunsBySeriesAsync(
            string tenantId,
            string seriesId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GovernedReportingRun>>(
                _runs.Values.Where(run =>
                    run.Scope.TenantId == tenantId
                    && run.SeriesId == seriesId).ToArray());

        public ValueTask AddRunAsync(
            GovernedReportingRun run,
            CancellationToken cancellationToken = default)
        {
            _runs.Add((run.Scope.TenantId, run.RunId), run);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceRunAsync(
            GovernedReportingRun run,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            var key = (run.Scope.TenantId, run.RunId);
            if (!_runs.TryGetValue(key, out var current) || current.Version != expectedVersion)
                throw new InvalidOperationException("Unexpected governance version conflict.");
            _runs[key] = run;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ReportingRestatementRequest?> GetRestatementRequestAsync(
            string tenantId,
            string requestId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_restatements.GetValueOrDefault((tenantId, requestId)));

        public ValueTask<IReadOnlyList<ReportingRestatementRequest>>
            ListRestatementRequestsBySeriesAsync(
                string tenantId,
                string seriesId,
                CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ReportingRestatementRequest>>(
                _restatements.Values.Where(request =>
                    request.RequestedBy.TenantId == tenantId
                    && request.SeriesId == seriesId).ToArray());

        public ValueTask AddRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            CancellationToken cancellationToken = default)
        {
            _restatements.Add((tenantId, request.RequestId), request);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            _restatements[(tenantId, request.RequestId)] = request;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryArtifactStore(DateTimeOffset storedAtUtc) : IReportingArtifactStore
    {
        private readonly Dictionary<ReportingArtifactIdentity, byte[]> _content = [];

        public Task<ReportingArtifactWriteResult> StoreAsync(
            ReportingArtifactWriteRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = request.Content.ToArray();
            var identity = new ReportingArtifactIdentity(request.TenantId, HashBytes(bytes));
            var existed = _content.ContainsKey(identity);
            _content.TryAdd(identity, bytes);
            return Task.FromResult(new ReportingArtifactWriteResult(
                identity,
                bytes.LongLength,
                storedAtUtc,
                existed));
        }

        public Task<ReportingArtifactReadResult> ReadAsync(
            ReportingArtifactIdentity identity,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_content.TryGetValue(identity, out var retained))
                throw new ReportingArtifactNotFoundException(identity);
            var bytes = retained.ToArray();
            return Task.FromResult(new ReportingArtifactReadResult(
                identity,
                bytes.LongLength,
                storedAtUtc,
                bytes));
        }
    }

    private sealed class MemoryArtifactCatalog : IReportingArtifactCatalog
    {
        private readonly Dictionary<(string TenantId, string PackageId), ReportingRetainedArtifactPackage>
            _packages = [];

        public ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
            ReportingRetainedArtifactPackage package,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tenantId = package.Artifacts
                .Select(static artifact => artifact.Scope.TenantId)
                .Distinct(StringComparer.Ordinal)
                .Single();
            var key = (tenantId, package.PackageId);
            if (_packages.TryGetValue(key, out var existing))
            {
                if (!JsonSerializer.Serialize(existing).Equals(
                        JsonSerializer.Serialize(package),
                        StringComparison.Ordinal))
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        "Attempted to replace immutable artifact metadata.");
                }

                return ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(true));
            }

            _packages.Add(key, package);
            return ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(false));
        }

        public ValueTask<ReportingRetainedArtifactPackage?> GetPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_packages.GetValueOrDefault((tenantId, packageId)));

        public ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
            string tenantId,
            string packageId,
            string artifactId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                _packages.GetValueOrDefault((tenantId, packageId))?.Artifacts
                    .SingleOrDefault(artifact => artifact.ArtifactId == artifactId));
    }

    private sealed class RecordingArtifactAuditStore : IReportingArtifactAuditStore
    {
        public List<ReportingArtifactAuditEvent> Events { get; } = [];
        public List<ReportingArtifactAuditReceipt> Receipts { get; } = [];

        public ValueTask<ReportingArtifactAuditReceipt> AppendAsync(
            ReportingArtifactAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previousHash = Receipts.LastOrDefault()?.Hash;
            var hash = HashBytes(Encoding.UTF8.GetBytes(string.Join(
                '|',
                previousHash,
                auditEvent.EventId,
                auditEvent.Action,
                auditEvent.PackageId,
                auditEvent.ArtifactId,
                auditEvent.ContentHashSha256,
                auditEvent.CorrelationId)));
            var receipt = new ReportingArtifactAuditReceipt(
                auditEvent.EventId,
                Receipts.Count + 1,
                previousHash,
                hash);
            Events.Add(auditEvent);
            Receipts.Add(receipt);
            return ValueTask.FromResult(receipt);
        }
    }

    private sealed class MemoryAccessGrantStore : IReportingAccessGrantStore
    {
        private readonly Dictionary<string, ReportingAccessGrantRecord> _grants =
            new(StringComparer.Ordinal);

        public Task<ReportingAccessGrantRecord?> GetAsync(
            string grantId,
            CancellationToken ct = default) =>
            Task.FromResult(_grants.GetValueOrDefault(grantId));

        public Task<IReadOnlyList<ReportingAccessGrantRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingAccessGrantRecord>>(
                _grants.Values.Where(grant =>
                    grant.TenantId == tenantId
                    && grant.PackageId == packageId).ToArray());

        public Task<bool> TryCreateAsync(
            ReportingAccessGrantRecord grant,
            CancellationToken ct = default) =>
            Task.FromResult(_grants.TryAdd(grant.GrantId, grant));

        public Task<bool> TryUpdateAsync(
            string grantId,
            long expectedVersion,
            ReportingAccessGrantRecord updatedGrant,
            CancellationToken ct = default)
        {
            if (!_grants.TryGetValue(grantId, out var current)
                || current.Version != expectedVersion
                || updatedGrant.Version != expectedVersion + 1)
            {
                return Task.FromResult(false);
            }

            _grants[grantId] = updatedGrant;
            return Task.FromResult(true);
        }
    }

    private sealed class MemoryDeliveryStore : IReportingDeliveryStore
    {
        private readonly Dictionary<string, ReportingDeliveryJobRecord> _jobs =
            new(StringComparer.Ordinal);

        public Task<ReportingDeliveryJobRecord?> GetAsync(
            string jobId,
            CancellationToken ct = default) =>
            Task.FromResult(_jobs.GetValueOrDefault(jobId));

        public Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken ct = default) =>
            Task.FromResult(_jobs.Values.SingleOrDefault(job =>
                job.IdempotencyKey == idempotencyKey));

        public Task<ReportingDeliveryJobRecord?> GetByAccessGrantIdAsync(
            string accessGrantId,
            CancellationToken ct = default) =>
            Task.FromResult(_jobs.Values.SingleOrDefault(job =>
                job.AccessGrantId == accessGrantId));

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(
                _jobs.Values.Where(job =>
                    job.TenantId == tenantId
                    && job.PackageId == packageId).ToArray());

        public Task<bool> TryCreateAsync(
            ReportingDeliveryJobRecord job,
            CancellationToken ct = default)
        {
            if (_jobs.ContainsKey(job.JobId)
                || _jobs.Values.Any(existing =>
                    existing.IdempotencyKey == job.IdempotencyKey))
            {
                return Task.FromResult(false);
            }

            _jobs.Add(job.JobId, job);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ClaimDueAsync(
            DateTimeOffset nowUtc,
            string leaseOwner,
            TimeSpan leaseDuration,
            int take,
            CancellationToken ct = default)
        {
            var claimed = _jobs.Values
                .Where(job =>
                    job.State is ReportingDeliveryState.Queued or ReportingDeliveryState.RetryScheduled
                    && job.NextAttemptAtUtc <= nowUtc
                    || job.State == ReportingDeliveryState.Dispatching
                    && job.LeaseExpiresAtUtc <= nowUtc)
                .OrderBy(static job => job.CreatedAtUtc)
                .Take(take)
                .Select(job => job with
                {
                    State = ReportingDeliveryState.Dispatching,
                    UpdatedAtUtc = nowUtc,
                    LeaseOwner = leaseOwner,
                    LeaseExpiresAtUtc = nowUtc.Add(leaseDuration),
                    Version = job.Version + 1
                })
                .ToArray();
            foreach (var job in claimed)
                _jobs[job.JobId] = job;
            return Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(claimed);
        }

        public Task<bool> TryUpdateAsync(
            string jobId,
            long expectedVersion,
            ReportingDeliveryJobRecord updatedJob,
            CancellationToken ct = default)
        {
            if (!_jobs.TryGetValue(jobId, out var current)
                || current.Version != expectedVersion
                || updatedJob.Version != expectedVersion + 1)
            {
                return Task.FromResult(false);
            }

            _jobs[jobId] = updatedJob;
            return Task.FromResult(true);
        }
    }

    private sealed class AcceptingRelayClient : IReportingHttpRelayClient
    {
        public List<ReportingHttpRelayMessage> Messages { get; } = [];

        public Task<ReportingHttpRelayResult> SendAsync(
            ReportingHttpRelayMessage message,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.FromResult(new ReportingHttpRelayResult(
                IsSuccess: true,
                IsTransientFailure: false,
                Code: "ACCEPTED",
                ProviderMessageId: "provider-message-statement-package"));
        }
    }

    private sealed class AcceptingProviderReceiptAuthenticator :
        IReportingProviderReceiptAuthenticator
    {
        public ValueTask<bool> AuthenticateAsync(
            ReportingProviderReceiptAuthenticationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class StatementChainFixture(
        string dataRoot,
        StatementReconciliationReportWorkflowExecution execution,
        StatementReconciliationReportWorkflowService workflow,
        StatementReconciliationCaseworkHandoffService casework,
        OperationsContinuityWorkflowService operations,
        InMemoryOperationsWorkflowAuditStore auditStore,
        FileReconciliationBreakQueueRepository queue) : IDisposable
    {
        public StatementReconciliationReportWorkflowExecution Execution { get; } = execution;
        public StatementReconciliationReportWorkflowService Workflow { get; } = workflow;
        public StatementReconciliationCaseworkHandoffService Casework { get; } = casework;
        public OperationsContinuityWorkflowService Operations { get; } = operations;
        public InMemoryOperationsWorkflowAuditStore AuditStore { get; } = auditStore;
        public FileReconciliationBreakQueueRepository Queue { get; } = queue;

        public void Dispose()
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    private sealed class ScenarioStatementRunWorkflowService(
        CanonicalStatementImport import,
        IReconciliationBreakStore breakStore,
        IReconciliationCaseStore caseStore) : IStatementRunWorkflowService
    {
        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([import]);
        }

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(runId, import.ImportId, StringComparison.Ordinal))
            {
                return null;
            }

            return new StatementRunWorkflowResult(
                import,
                await breakStore.ListOpenAsync(cancellationToken),
                await caseStore.ListAsync(cancellationToken));
        }

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(
            CancellationToken cancellationToken = default) =>
            breakStore.ListOpenAsync(cancellationToken);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(
            CancellationToken cancellationToken = default) =>
            caseStore.ListAsync(cancellationToken);
    }

    private sealed class CleanStatementImportService(StatementImportCommitResultDto result)
        : IStatementImportCommitService
    {
        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            request.AccountingScope.Should().Be(new StatementAccountingScope(
                FundProfileId.ToString("D"),
                LedgerBookId,
                AccountingPeriodId,
                AsOfDate));
            return Task.FromResult(result);
        }

        public Task<StatementImportValidationResult> ValidateAsync(
            StatementSourceDocument document,
            string? connectorId,
            CancellationToken ct = default)
            => Task.FromResult(new StatementImportValidationResult(true, 1, []));
    }

    private sealed class CleanStatementEvidenceRetainer : IStatementImportEvidenceRetainer
    {
        public Task<StatementImportCommitResultDto> RetainAsync(
            StatementImportCommitResultDto result,
            StatementImportEvidenceBridgeRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(result with
            {
                EvidenceVaultIdentity = new EvidenceVaultIdentityDto(
                    "statement-vault-june-2026",
                    "statement-run",
                    result.RunId,
                    "evidence/statement-vault-june-2026/manifest.json",
                    "/api/workstation/evidence/statement-vault-june-2026",
                    ReportingNow,
                    new string('b', 64),
                    SchemaVersion: 1,
                    StorageKind: "File")
            });
        }
    }

    private sealed class EmptyReconciliationBreakQueueRepository :
        IReconciliationBreakQueueRepository
    {
        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(
            ReconciliationBreakQueueStatus? status = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>([]);

        public Task<ReconciliationBreakQueueItem?> GetByIdAsync(
            string breakId,
            CancellationToken ct = default) =>
            Task.FromResult<ReconciliationBreakQueueItem?>(null);

        public Task<bool> CreateIfMissingAsync(
            ReconciliationBreakQueueItem item,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            ReconciliationBreakQueueItem item,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            string breakId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(
            ReviewReconciliationBreakRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(
            ResolveReconciliationBreakRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(
            string breakId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationBreakQueueAuditEvent>>([]);
    }

    private sealed class RecordingLedgerJournalStore(
        Guid authoritativeFundProfileId,
        Guid authoritativeLedgerBookId,
        Guid authoritativeAccountingPeriodId) : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Entry.IsBalanced)
                throw new LedgerValidationException("Journal entry must be balanced.");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(
            Guid periodId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(
            Guid aggregateId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(
            Guid periodId,
            CancellationToken ct = default) =>
            Task.FromResult<LedgerAccountingPeriod?>(
                periodId == authoritativeAccountingPeriodId
                    ? new LedgerAccountingPeriod(
                        authoritativeAccountingPeriodId,
                        authoritativeLedgerBookId,
                        2026,
                        6,
                        "2026-06",
                        new DateOnly(2026, 6, 1),
                        AsOfDate,
                        "Open",
                        ReportingNow.AddMonths(-1),
                        ClosedAt: null,
                        Version: 1)
                    : null);

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(
                !ledgerBookId.HasValue || ledgerBookId.Value == authoritativeLedgerBookId
                    ?
                    [
                        new LedgerAccountingPeriod(
                            authoritativeAccountingPeriodId,
                            authoritativeLedgerBookId,
                            2026,
                            6,
                            "2026-06",
                            new DateOnly(2026, 6, 1),
                            AsOfDate,
                            "Open",
                            ReportingNow.AddMonths(-1),
                            ClosedAt: null,
                            Version: 1)
                    ]
                    : []);

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            Task.FromResult(period);

        public Task<LedgerBookRecord?> GetLedgerBookAsync(
            Guid requestedLedgerBookId,
            CancellationToken ct = default) =>
            Task.FromResult<LedgerBookRecord?>(
                requestedLedgerBookId == authoritativeLedgerBookId
                    ? new LedgerBookRecord(
                        authoritativeLedgerBookId,
                        authoritativeFundProfileId.ToString("D"),
                        authoritativeFundProfileId,
                        FundStructureNodeKindDto.Fund,
                        "Primary statement authority book",
                        "USD",
                        ReportingNow.AddYears(-1),
                        ReportingNow)
                    : null);

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerBookRecord>>(
            [
                new LedgerBookRecord(
                    authoritativeLedgerBookId,
                    authoritativeFundProfileId.ToString("D"),
                    authoritativeFundProfileId,
                    FundStructureNodeKindDto.Fund,
                    "Primary statement authority book",
                    "USD",
                    ReportingNow.AddYears(-1),
                    ReportingNow)
            ]);

        public Task<LedgerBookRecord> SaveLedgerBookAsync(
            LedgerBookRecord book,
            CancellationToken ct = default) =>
            Task.FromResult(book);
    }
}
