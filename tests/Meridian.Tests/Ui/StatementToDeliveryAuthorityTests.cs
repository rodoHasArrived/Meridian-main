using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.AccountingClose;
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
using Meridian.Tests.TestSupport;
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
    private static readonly DateTimeOffset ReportingNow = new(2026, 7, 31, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AsOfDate = new(2026, 6, 30);
    private static readonly Guid LedgerBookId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AccountingPeriodId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FundProfileId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrganizationId =
        Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid PriorAccountingPeriodId =
        Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly ReconciliationBreakQueueScope QueueScope =
        new("tenant-alpha", "company-alpha");
    [Fact]
    public async Task Scenario_MonthEndClose_CertifiedClientPackageReleasesAndRetainsDeliveryReceipt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var fundAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        using var statementIntake = await StartStatementWorkflowAsync(fundAccountId, ct);
        var statementRunId = statementIntake.Execution.Workflow.StatementRunId
            ?? throw new InvalidOperationException("The real statement import did not retain a run id.");
        statementIntake.Execution.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        statementIntake.Execution.Workflow.EvidenceVaultIdentity.Should().NotBeNull();
        statementIntake.Execution.Workflow.EvidenceVaultIdentity!.SubjectId.Should().Be(statementRunId);
        statementIntake.Execution.Workflow.EvidenceVaultIdentity.Artifacts
            .Should().ContainSingle("the production evidence bridge must retain the imported source bytes");
        var retainedStatementEvidence = await statementIntake.StatementEvidenceStore.FindByLinkageAsync(
            new EvidenceVaultLookupRequestDto(
                EvidenceSubject: null,
                RunId: statementRunId,
                PeriodId: null,
                ReportPackId: null,
                ReconciliationCaseId: null),
            ct);
        retainedStatementEvidence.Should().ContainSingle(vault =>
            vault.VaultId == statementIntake.Execution.Workflow.EvidenceVaultIdentity.VaultId);
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
        var queuedCase = (await statementIntake.Queue.GetAllAsync(QueueScope, ct: ct))
            .Should().ContainSingle("one retained statement variance requires governed casework")
            .Which;
        queuedCase.RunId.Should().Be(statementRunId);
        queuedCase.SourceImportId.Should().Be(statementRunId);
        queuedCase.SourceFingerprint.Should().NotBeNullOrWhiteSpace();
        var assigned = await statementIntake.Casework.ApplyAsync(
            QueueScope,
            CaseworkCommand(queuedCase, ReconciliationCaseworkAction.Assign, "assign") with
            {
                Assignee = "fund-ops"
            },
            ct);
        assigned.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var investigating = await statementIntake.Casework.ApplyAsync(
            QueueScope,
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
            QueueScope,
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
        var resolvedQueueItem = resolvedCase.Item!;
        resolvedQueueItem.Disposition.Should().Be(ReconciliationBreakDispositionDto.Resolved);
        StatementCaseworkHandoffObligation.HasPending(resolvedQueueItem).Should().BeFalse();
        StatementCaseworkHandoffObligation.HasCompleted(resolvedQueueItem).Should().BeTrue();
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
        reconciledStatement.Workflow.ArtifactGeneration.Should().Be(1);
        reconciledStatement.Workflow.EvidenceReferences.Should().Contain(reference =>
            reference.Contains(statementRunId, StringComparison.Ordinal));
        var approvedWorkflow = await PrepareOperationsWorkflowForCloseAsync(
            statementIntake.Operations,
            statementIntake.Execution.Workflow.OperationsWorkflowId.Value,
            fundAccountId,
            resolvedQueueItem,
            statementRunId,
            ct);
        approvedWorkflow.WorkflowId.Should().Be(
            statementIntake.Execution.Workflow.OperationsWorkflowId.Value,
            "posting, close, certification, and delivery must continue the workflow started by statement intake");
        var reportingParameters = Parameters(FundProfileId);
        var makerAccess = Access("report-maker");
        var softClosed = await statementIntake.JournalStore.ClosePeriodAsync(
            AccountingPeriodId,
            new CloseLedgerPeriodRequest(
                LedgerPeriodCloseKindDto.SoftClose,
                "fund-controller",
                "Complete the review window before governed hard close."),
            ct);
        softClosed.Period.Status.Should().Be(LedgerPeriodStatusDto.SoftClosed);
        Func<Task> captureBeforeHardClose = async () =>
        {
            _ = await statementIntake.AuthoritativeSource
                .CaptureAsync(reportingParameters, makerAccess, ct);
        };
        await captureBeforeHardClose.Should()
            .ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*SoftClosed*not HardClosed*");
        var evidenceStore = new InMemoryReportingReconciliationEvidenceStore();
        var evidenceRetention = new ReportingReconciliationEvidenceRetentionService(
            evidenceStore,
            statementIntake.AuthoritativeSource);
        var accountingAudit = new InMemoryAccountingActionAuditStore();
        var accountingConfiguration = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            accountingAudit,
            statementIntake.JournalStore);
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var journalWorkbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            accountingConfiguration,
            accountingAudit,
            journalStore: statementIntake.JournalStore);
        var closeRunner = new AutomatedJournalIntakeRunner(
            new AutomatedJournalDraftIntakeService(journalWorkbench, draftStore, accountingConfiguration),
            new FeeScheduleAccrualEventProducer(),
            ledgerBookService: statementIntake.JournalStore,
            timeProvider: new FixedTimeProvider(ReportingNow));
        var postingBridge = new AccountingClosePostingWorkbenchBridge(
            closeRunner,
            journalWorkbench,
            journalWorkbench,
            statementIntake.JournalStore,
            evidenceRetention,
            statementIntake.Tenancy,
            statementIntake.Queue,
            statementIntake.Operations,
            new ImmediateReportingReleaseConsistencyGate());
        var closeManagement = new AccountingCloseManagementService(
            statementIntake.Operations,
            postingBridge);
        await SignOffRequiredCloseTasksAsync(closeManagement, approvedWorkflow, ct);
        var closeCorrelationId = $"statement-delivery:{statementRunId}:hard-close";
        var closeEvidenceLinks = (resolvedQueueItem.EvidenceLinks ?? [])
            .Append(
                $"evidence:close-package:{approvedWorkflow.WorkflowId:D}:{approvedWorkflow.PeriodId}:book:{LedgerBookId:D}:period-lock")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var closeLock = await closeManagement.LockClosePeriodScopedAsync(
            new LockClosePeriodRequestDto(
                approvedWorkflow.WorkflowId,
                approvedWorkflow.Version,
                "fund-controller",
                "Commit the independently approved June statement close.",
                "statement-close-support-june-2026",
                closeEvidenceLinks,
                RequiredChecklistControlApprovals(),
                closeCorrelationId,
                ClosePackageId: "statement-close-package-june-2026",
                ClosePackageManifestId: "statement-close-manifest-june-2026",
                ClosePackageRetainedManifestRoute:
                    "/api/workstation/reporting/packages/statement-close-package-june-2026",
                ControllerRole: "Controller"),
            "fund-controller",
            "tenant-alpha",
            "company-alpha",
            ct);
        closeLock.Should().NotBeNull();
        closeLock!.IsLocked.Should().BeTrue();
        closeLock.Issues.Should().BeEmpty();
        closeLock.Transition.Should().NotBeNull();
        closeLock.Transition!.Success.Should().BeTrue();
        var closedWorkflow = closeLock.Transition.Workflow
            ?? throw new InvalidOperationException("The governed hard close did not return its Operations workflow.");
        var closeAudit = (await statementIntake.AuditStore.GetTimelineAsync(closedWorkflow.WorkflowId, ct))
            .Single(eventRecord =>
                eventRecord.EventType == "workflow-closed"
                && eventRecord.ToState == OperationsWorkflowStatusDto.Closed);
        closeAudit.CorrelationId.Should().Be(closeCorrelationId);
        resolvedQueueItem.EvidenceLinks.Should().OnlyContain(link =>
            closeAudit.References.Any(reference => reference.EvidenceId == link));
        var hardClosedPeriod = await statementIntake.JournalStore.GetPeriodAsync(
            AccountingPeriodId,
            ct);
        hardClosedPeriod.Should().NotBeNull();
        hardClosedPeriod!.Status.Should().Be("HardClosed");
        hardClosedPeriod.ClosedAt.Should().Be(ReportingNow);
        var authoritativeSource = statementIntake.AuthoritativeSource;
        var retainedSource = await authoritativeSource.CaptureAsync(
            reportingParameters,
            makerAccess,
            ct);
        var sourceCheckpoint = retainedSource.Checkpoint;
        var reconciliationEvidenceSource = new ReportingReconciliationEvidenceSource(
            evidenceStore,
            statementIntake.Queue);
        var retainedClose = await reconciliationEvidenceSource.ResolveAsync(
            reportingParameters,
            sourceCheckpoint,
            makerAccess,
            ct);
        retainedClose.BreakEvidence.Should().ContainSingle(item =>
            item.BreakId == resolvedQueueItem.BreakId
            && item.Disposition == ReconciliationBreakDispositionDto.Resolved);
        var closeCompletion = retainedClose.CloseWorkflowCompletion
            ?? throw new InvalidOperationException(
                "The production hard-close bridge did not retain its committed workflow evidence.");
        var certification = new ReportingRunCertificationService(
            authoritativeSource,
            reconciliationEvidenceSource);
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
        certified.AuthoritativeSource.SourceKind.Should().Be("durable-ledger-journal");
        certified.AuthoritativeSource.AccountingPeriodId.Should().Be(AccountingPeriodId.ToString("D"));
        certified.AuthoritativeSource.EvidenceIds.Should().ContainSingle(static evidence =>
            evidence.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        certified.Readiness.Checks
            .Single(check => check.CheckId == "exact-reconciliation-evidence")
            .EvidenceReferences.Should().Contain(
                $"operations-workflow:{closedWorkflow.WorkflowId:D}:version:{closedWorkflow.Version}:closed");
        var templateCatalog = new SingleTemplateCatalog(template);
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(statementIntake.DataRoot, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            templateCatalog,
            new DeterministicReportingSectionRenderer(),
            () => ReportingNow,
            runStore);
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
        var ledgerPresentationSource = new RecapturingLedgerPresentationSource(
            authoritativeSource,
            makerAccess);
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
                ledgerPresentationSource),
            new ReportingArtifactRetentionAuthorityProvider(),
            new GovernedReportingRestatementChangedLineResolver(),
            new UnusedRestatementCertificationInputProvider(),
            new ImmediateReportingReleaseConsistencyGate());
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
        ledgerPresentationSource.ResolveCount.Should().BeGreaterThan(0,
            "PDF/XLSX production must resolve the exact certified partners-capital report pack");
        ledgerPresentationSource.LastPresentation.Should().NotBeNull();
        ledgerPresentationSource.LastPresentation!.ReportPack.Statements.PartnersCapital
            .Should().NotBeNull();
        ledgerPresentationSource.LastPresentation.ReportPack.Statements.PartnersCapital!
            .EndingCapital.Should().Be(1_250_000m);
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
        var releasedBytesByArtifactId = new Dictionary<string, byte[]>(StringComparer.Ordinal);
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
            releasedBytesByArtifactId.Add(declaration.ArtifactId, released.Content);
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
        var deliveryStore = new MemoryDeliveryStore(accessGrantStore);
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
        var relayMessage = relay.Messages.Should().ContainSingle().Subject;
        var bearerToken = ExtractFragmentValue(relayMessage.RecipientAccessUri, "token");
        var recipientDownloads = new List<ReportingArtifactDownload>();
        foreach (var declaration in primaryDeclarations)
        {
            var download = await distribution.ExchangeGrantForDownloadAsync(
                accepted.AccessGrantId!,
                new SecureReportingGrantExchangeCommand(bearerToken, declaration.ArtifactId),
                $"statement-recipient:{statementRunId}:{declaration.ArtifactId}",
                ct);
            recipientDownloads.Add(download);
            var releasedReference = governed.Release.Artifacts
                .Single(reference => reference.ArtifactId == declaration.ArtifactId);
            download.Artifact.ContentType.Should().Be(declaration.ContentType);
            HashBytes(download.Content).Should().Be(releasedReference.ArtifactHash);
            download.Content.Should().Equal(releasedBytesByArtifactId[declaration.ArtifactId],
                "the recipient must receive the exact independently released PDF/XLSX bytes");
            artifactAudit.Events.Should().Contain(eventRecord =>
                eventRecord.EventId == download.AuditEventId
                && eventRecord.Action == ReportingArtifactAuditAction.ContentAccessed
                && eventRecord.ArtifactId == declaration.ArtifactId
                && eventRecord.ContentHashSha256 == releasedReference.ArtifactHash
                && eventRecord.CorrelationId ==
                $"statement-recipient:{statementRunId}:{declaration.ArtifactId}");
        }
        var consumedGrant = await accessGrantStore.GetAsync(accepted.AccessGrantId!, ct);
        consumedGrant.Should().NotBeNull();
        consumedGrant!.UseCount.Should().Be(2);
        consumedGrant.MaxUses.Should().Be(2);
        consumedGrant.ConsumedArtifactIds.Should().Equal(
            primaryDeclarations
                .Select(static artifact => artifact.ArtifactId)
                .OrderBy(static artifactId => artifactId, StringComparer.Ordinal));
        deliveryStore.AtomicDownloadCommitCount.Should().Be(2);
        var downloadedDelivery = await deliveryStore.GetAsync(accepted.JobId, ct);
        downloadedDelivery.Should().NotBeNull();
        downloadedDelivery!.Receipts.Select(static receipt => receipt.Kind).Should().ContainInOrder(
            ReportingDeliveryReceiptKind.Accepted,
            ReportingDeliveryReceiptKind.Delivered,
            ReportingDeliveryReceiptKind.Downloaded,
            ReportingDeliveryReceiptKind.Downloaded);
        var downloadReceipts = downloadedDelivery.Receipts
            .Where(static receipt => receipt.Kind == ReportingDeliveryReceiptKind.Downloaded)
            .ToArray();
        downloadReceipts.Should().HaveCount(2);
        downloadReceipts.Select(static receipt => receipt.EvidenceReference)
            .Should().BeEquivalentTo(recipientDownloads.Select(static download => download.AuditEventId));
        downloadReceipts.Should().OnlyContain(receipt =>
            recipientDownloads.Any(download =>
                receipt.ReceiptId == ReportingDeliveryDownloadReceiptIdentity.Create(
                    accepted.JobId,
                    download.Artifact.ArtifactId,
                    download.AuditEventId)));
        var canonicalHistory = await new ReportPackRunReadService(
                templateCatalog,
                runStore: runStore,
                canonicalDeliveryStore: deliveryStore)
            .BuildCanonicalHistoryAsync(Access("client-report-releaser"), 25, ct);
        canonicalHistory.Runs.Should().ContainSingle(run => run.RunId == governed.RunId);
        var historyDelivery = canonicalHistory.Deliveries.Should().ContainSingle().Subject;
        historyDelivery.JobId.Should().Be(accepted.JobId);
        historyDelivery.RunId.Should().Be(governed.RunId);
        historyDelivery.PackageId.Should().Be(accepted.PackageId);
        historyDelivery.ReleaseReceiptId.Should().Be(accepted.ReleaseAuthorization.ReceiptId);
        historyDelivery.ReleaseVersion.Should().Be(accepted.ReleaseAuthorization.ReleaseVersion);
        historyDelivery.ArtifactManifestHashSha256.Should().Be(
            accepted.ReleaseAuthorization.ArtifactManifestHashSha256);
        historyDelivery.State.Should().Be(nameof(ReportingDeliveryState.Delivered));
        historyDelivery.ProviderMessageId.Should().Be(accepted.ProviderMessageId);
        historyDelivery.AccessGrantId.Should().Be(accepted.AccessGrantId);
        historyDelivery.Receipts.Count(receipt =>
            receipt.Kind == nameof(ReportingDeliveryReceiptKind.Accepted)).Should().Be(1);
        historyDelivery.Receipts.Count(receipt =>
            receipt.Kind == nameof(ReportingDeliveryReceiptKind.Delivered)).Should().Be(1);
        historyDelivery.Receipts.Count(receipt =>
            receipt.Kind == nameof(ReportingDeliveryReceiptKind.Downloaded)).Should().Be(2);
        historyDelivery.Receipts.Should().Contain(receipt =>
            receipt.Kind == nameof(ReportingDeliveryReceiptKind.Delivered)
            && receipt.EvidenceReference ==
            $"operations-close-audit:{closeCompletion.CloseAuditEventId}:{closeCompletion.CloseAuditHash}");
        var queueHistory = await statementIntake.Queue.GetAuditHistoryAsync(
            QueueScope,
            queuedCase.BreakId,
            ct);
        queueHistory.Should().NotBeEmpty();
        (await statementIntake.Operations.GetTimelineAsync(closedWorkflow.WorkflowId, ct))
            .Should().Contain(eventRecord =>
                eventRecord.EventType == "workflow-closed"
                && eventRecord.CurrentHash == closeCompletion.CloseAuditHash);
        governed.AuditTrail.Should().Contain(eventRecord =>
            eventRecord.Action == ReportingGovernanceAuditAction.RunReleased
            && eventRecord.Authority.CorrelationId == releaser.CorrelationId);
        retainedClose.EvidenceIds.Should().Contain(
            $"operations-workflow:{closedWorkflow.WorkflowId:D}:version:{closedWorkflow.Version}:closed");
    }
    private static async Task<StatementChainFixture> StartStatementWorkflowAsync(
        Guid fundAccountId, CancellationToken ct)
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
                    fundAccountId,
                    FundStructureNodeKindDto.Account,
                    "Primary reporting book",
                    "USD",
                    ReportingNow.AddYears(-1),
                    ReportingNow.AddDays(-1),
                    AccountingBasis: AccountingBasisKindDto.Gaap)
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
                    Version: 1,
                    AccountingBasis: AccountingBasisKindDto.Gaap)
            ]);
        var statementStore = new JsonCanonicalStatementStore(dataRoot);
        var statementBreakStore = new JsonReconciliationBreakStore(dataRoot);
        var statementCaseStore = new JsonReconciliationCaseStore(dataRoot);
        var statementRuns = new StatementRunWorkflowService(
            statementStore,
            statementCaseStore,
            statementBreakStore,
            new CsvBrokerStatementService(statementStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));
        var profileCatalog = new StatementMappingProfileCatalog(
            new FileStatementMappingProfileStore(dataRoot));
        var importService = new StatementImportService(
            new StatementConnectorRegistry([new CsvStatementConnector(profileCatalog)]),
            profileCatalog,
            statementRuns,
            dataRoot);
        var statementEvidenceStore = new FileEvidenceArtifactStore(
            dataRoot,
            NullLogger<FileEvidenceArtifactStore>.Instance);
        var reconciliation = new ReconciliationApiService(
            statementRuns,
            accounts.Object,
            tenancy.Object);
        var derivation = new OperationsStatusDerivationService();
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var journalStore = new ScenarioLedgerJournalStore(
            FundProfileId,
            fundAccountId,
            OrganizationId,
            LedgerBookId,
            AccountingPeriodId);
        journalStore.SeedBeginningCapital(1_000_000m);
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
            reconciliation,
            queue);
        var casework = new StatementReconciliationCaseworkHandoffService(
            queue,
            statementBreakStore,
            statementCaseStore,
            statementRuns,
            operations,
            journalStore);
        var workflow = new StatementReconciliationReportWorkflowService(
            importService,
            new StatementImportEvidenceBridge(statementEvidenceStore, dataRoot),
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
                        "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\nCUSTODY-7842,,0,0,250000,cashbalance,2026-06-30,2026-06-30,USD,0,CASH-JUNE-2026"u8
                            .ToArray()),
                    ConnectorId: "csv",
                    SourceKind: "custodian",
                    SourceInstitution: "Northstar Custody",
                    FundAccountId: fundAccountId.ToString("D"),
                    ExternalAccountId: "CUSTODY-7842",
                    PeriodStart: new DateOnly(2026, 6, 1),
                    PeriodEnd: AsOfDate,
                    ToleranceProfileId: null,
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
            queue,
            statementEvidenceStore,
            journalStore,
            tenancy.Object,
            CreateAuthoritativeLedgerSource(journalStore, tenancy.Object, fundAccountId));
    }
    private static LedgerReportingAuthoritativeSource CreateAuthoritativeLedgerSource(
        ScenarioLedgerJournalStore journalStore, IFundProfileTenancyRegistry tenancy,
        Guid fundAccountId)
    {
        var structure = new Mock<IFundStructureService>(MockBehavior.Strict);
        structure
            .Setup(service => service.GetOrganizationStructureAsync(
                It.IsAny<OrganizationStructureQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationStructureGraphDto(
                Organizations:
                [
                    new OrganizationSummaryDto(
                        OrganizationId,
                        "NORTHSTAR",
                        "Northstar reporting organization",
                        "USD",
                        true,
                        ReportingNow.AddYears(-2),
                        null,
                        [])
                ],
                Businesses: [],
                Clients: [],
                Funds: [],
                Sleeves: [],
                Vehicles: [],
                Entities: [],
                InvestmentPortfolios: [],
                Accounts: [],
                Nodes:
                [
                    new FundStructureNodeDto(
                        OrganizationId,
                        FundStructureNodeKindDto.Organization,
                        "NORTHSTAR",
                        "Northstar reporting organization",
                        null,
                        true,
                        ReportingNow.AddYears(-2),
                        null),
                    new FundStructureNodeDto(
                        FundProfileId,
                        FundStructureNodeKindDto.Fund,
                        "NORTHSTAR-FUND",
                        "Northstar fund",
                        null,
                        true,
                        ReportingNow.AddYears(-2),
                        null),
                    new FundStructureNodeDto(
                        fundAccountId,
                        FundStructureNodeKindDto.Account,
                        "CUSTODY-7842",
                        "Northstar operating custody",
                        null,
                        true,
                        ReportingNow.AddYears(-1),
                        null)
                ],
                OwnershipLinks:
                [
                    new OwnershipLinkDto(
                        Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                        OrganizationId,
                        FundProfileId,
                        OwnershipRelationshipTypeDto.Owns,
                        100m,
                        true,
                        ReportingNow.AddYears(-2),
                        null,
                        null),
                    new OwnershipLinkDto(
                        Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"),
                        FundProfileId,
                        fundAccountId,
                        OwnershipRelationshipTypeDto.Owns,
                        100m,
                        true,
                        ReportingNow.AddYears(-1),
                        null,
                        null)
                ],
                Assignments: []));
        return new LedgerReportingAuthoritativeSource(
            journalStore,
            tenancy,
            structure.Object,
            new FixedTimeProvider(ReportingNow));
    }
    private static ReconciliationCaseworkCommand CaseworkCommand(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkAction action,
        string step) =>
        new(
            item.BreakId,
            action,
            "fund-ops",
            $"statement-delivery-{step}",
            $"statement-delivery:{item.RunId}",
            "statement-to-delivery-authority-scenario",
            item.Version,
            Reason: "Retain reviewed statement reconciliation evidence.",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    private static async Task<OperationsContinuityWorkflowDto> PrepareOperationsWorkflowForCloseAsync(
        OperationsContinuityWorkflowService service, Guid workflowId, Guid fundAccountId,
        ReconciliationBreakQueueItem resolvedCase, string statementRunId, CancellationToken ct)
    {
        var correlationId = $"statement-delivery:{statementRunId}:reconciliation";
        var caseEvidence = (resolvedCase.EvidenceLinks ?? [])
            .Distinct(StringComparer.Ordinal)
            .Select(link => new OperationsEvidenceLinkDto(
                link, "Resolved statement reconciliation evidence",
                link.StartsWith("/", StringComparison.Ordinal) ? link : null,
                "statement-reconciliation-casework",
                resolvedCase.LastActivityAt ?? resolvedCase.ResolvedAt ?? ReportingNow))
            .ToArray();
        var imported = await service.GetAsync(workflowId, ct)
            ?? throw new InvalidOperationException(
                "Statement intake did not retain its Operations Continuity workflow.");
        imported.FundAccountId.Should().Be(fundAccountId);
        imported.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Imported);
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            workflowId,
            new OperationsTransitionRequestDto(
                imported.Version, "statement-operations",
                "Normalized retained statement activity."),
            ct);
        var mapped = await service.ResolveSecurityMasterMappingsAsync(
            workflowId,
            new OperationsSecurityMasterResolveRequestDto(
                normalized.Workflow!.Version, "statement-operations",
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
                CorrelationId: correlationId,
                BreakCases:
                [
                    new OperationsBreakCaseDto(
                        resolvedCase.BreakId, resolvedCase.SourceBreakId ?? resolvedCase.BreakId,
                        resolvedCase.Category.ToString(), resolvedCase.Severity.ToString(),
                        resolvedCase.Disposition?.ToString() ?? "Resolved",
                        resolvedCase.AssignedTo,
                        resolvedCase.SlaDueAt is { } due
                            ? DateOnly.FromDateTime(due.UtcDateTime) : null,
                        resolvedCase.SourceSystem ?? "broker-statement",
                        "durable-ledger-journal", null, null, resolvedCase.Variance, null, null,
                        resolvedCase.ResolutionNote ?? resolvedCase.RecommendedAction,
                        caseEvidence,
                        new OperationsContinuityCorrelationKeysDto(
                            statementRunId, fundAccountId,
                            ReconciliationCaseId: resolvedCase.BreakId),
                        SlaState: resolvedCase.SlaState.ToString(),
                        SlaDueAtUtc: resolvedCase.SlaDueAt,
                        RootCauseCode: resolvedCase.RootCauseCode,
                        ApprovalState: resolvedCase.SignoffStatus,
                        BlockedOutputs: resolvedCase.BlockedOutputs,
                        Measures: resolvedCase.Measures,
                        Disposition: resolvedCase.Disposition,
                        DispositionReason: resolvedCase.DispositionReason,
                        SupersedingBreakId: resolvedCase.SupersedingBreakId,
                        DispositionApprovedBy: resolvedCase.DispositionApprovedBy,
                        DispositionApprovalReference: resolvedCase.DispositionApprovalReference,
                        DispositionEvidenceHash: resolvedCase.DispositionEvidenceHash,
                        DisposedAtUtc: resolvedCase.DisposedAt)
                ],
                EvidenceLinks: caseEvidence,
                SourceRunId: statementRunId,
                ReconciliationRunId: resolvedCase.RunId),
            ct);
        var carriedCase = reconciled.Workflow!.BreakCases.Should()
            .ContainSingle(item => item.BreakId == resolvedCase.BreakId).Subject;
        carriedCase.CorrelationKeys.Should().NotBeNull();
        carriedCase.CorrelationKeys!.RunId.Should().Be(statementRunId);
        carriedCase.CorrelationKeys.ReconciliationCaseId.Should().Be(resolvedCase.BreakId);
        carriedCase.EvidenceLinks.Select(static evidence => evidence.EvidenceId)
            .Should().BeEquivalentTo(resolvedCase.EvidenceLinks);
        var posture = await service.RefreshGatePostureAsync(
            workflowId,
            new OperationsGatePostureRequestDto(
                reconciled.Workflow!.Version,
                "statement-operations",
                CorrelationId: correlationId,
                ReportPackReady: true,
                ReportPackId: "statement-close-support-june-2026",
                Rationale: "Close support package is ready for controller review.",
                EvidenceLinks: caseEvidence),
            ct);
        var submitted = await service.SubmitForApprovalAsync(
            workflowId,
            new OperationsSubmitApprovalRequestDto(
                posture.Workflow!.Version,
                "statement-operations",
                Reviewer: "fund-controller",
                Rationale: "Submit the clean statement close for independent approval.",
                ReportPackId: "statement-close-support-june-2026",
                CorrelationId: correlationId,
                EvidenceLinks: caseEvidence,
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
                CorrelationId: correlationId,
                EvidenceLinks: caseEvidence,
                ChecklistControlApprovals: RequiredChecklistControlApprovals()),
            ct);
        approved.Success.Should().BeTrue();
        approved.Workflow!.ApprovalState.Should().Be(OperationsApprovalStateDto.Approved);
        approved.Workflow.Status.Should().Be(OperationsWorkflowStatusDto.ReadyForClose);
        return approved.Workflow;
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
    private static async Task SignOffRequiredCloseTasksAsync(
        AccountingCloseManagementService service, OperationsContinuityWorkflowDto workflow,
        CancellationToken ct)
    {
        foreach (var taskId in new[] { "reconciliation-review", "report-certification" })
        {
            await service.SignOffCloseTaskScopedAsync(
                new SignOffCloseTaskRequestDto(
                    workflow.WorkflowId, taskId, "Controller", ManualJournalEntryStatusDto.Approved,
                    "fund-controller",
                    $"Controller retained the {taskId} close control.",
                    [$"evidence:close-task:{taskId}:Controller:{workflow.PeriodId}:book:{LedgerBookId:D}:control-signoff"]),
                "fund-controller", "tenant-alpha", "company-alpha", ct);
        }
        var plan = await service.GetPeriodPlanScopedAsync(
                workflow.WorkflowId, "tenant-alpha", "company-alpha", ct)
            ?? throw new InvalidOperationException("The governed close plan was not retained.");
        var approvalTask = plan.Tasks.Single(task => task.TaskId == "close-gate-approval");
        var approvalRole = approvalTask.SignOffRequirements.Single().Role;
        await service.SignOffCloseTaskScopedAsync(
            new SignOffCloseTaskRequestDto(
                workflow.WorkflowId, approvalTask.TaskId, approvalRole,
                ManualJournalEntryStatusDto.Approved, "fund-admin",
                "Fund administrator retained the independent close approval.",
                [$"evidence:close-task:{approvalTask.TaskId}:{approvalRole}:{workflow.WorkflowId:D}:book:{LedgerBookId:D}:control-signoff"]),
            "fund-admin", "tenant-alpha", "company-alpha", ct);
    }
    private static OperationsLedgerJournalCandidateDto JournalCandidate(Guid fundAccountId)
    {
        var securityId = Guid.Parse("CB931872-F221-47C1-B922-1F61BFA93CF5");
        var dimensions = new LedgerDimensionSetDto(
            FundId: FundProfileId.ToString("D"),
            OrganizationId: OrganizationId.ToString("D"),
            BookId: LedgerBookId.ToString("D"));
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
                    Debit: 250_000m,
                    Credit: 0m,
                    Dimensions: dimensions),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Investor Capital",
                    AccountType: nameof(LedgerAccountType.Equity),
                    Debit: 0m,
                    Credit: 250_000m,
                    FinancialAccountId: "client-investor",
                    Dimensions: dimensions)
            ],
            CommandId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            SourceEventId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            AccountingBasis: AccountingBasisKindDto.Gaap,
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
    private static string HashBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string ExtractFragmentValue(string uriText, string key)
    {
        var fragment = new Uri(uriText).Fragment.TrimStart('#');
        return fragment
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Split('=', 2))
            .Where(static pair => pair.Length == 2)
            .Where(pair => string.Equals(
                Uri.UnescapeDataString(pair[0]),
                key,
                StringComparison.Ordinal))
            .Select(static pair => Uri.UnescapeDataString(pair[1]))
            .Single();
    }
    private sealed class RecapturingLedgerPresentationSource(
        LedgerReportingAuthoritativeSource source,
        ReportAccessQueryContext access) : IReportingCertifiedLedgerPresentationSource
    {
        public int ResolveCount { get; private set; }
        public ReportingCertifiedLedgerPresentationInput? LastPresentation { get; private set; }
        public async ValueTask<ReportingCertifiedLedgerPresentationInput?> ResolveExactAsync(
            ReportingOutputManifest manifest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            var parameters = manifest.ResolvedParameters
                ?? throw new InvalidOperationException("The released manifest has no retained parameters.");
            var capture = await source.CaptureAsync(
                parameters,
                access,
                new ReportingAuthoritativeSourceCaptureIntent(manifest.TemplateId)
                {
                    RequiresCertifiedLedgerPresentation = true
                },
                cancellationToken);
            capture.Checkpoint.CheckpointId.Should().Be(manifest.AuthoritativeSource!.CheckpointId);
            capture.Checkpoint.CheckpointHash.Should().Be(manifest.AuthoritativeSource.CheckpointHash);
            LastPresentation = capture.CertifiedLedgerPresentation;
            ResolveCount++;
            return LastPresentation;
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
        internal object Gate { get; } = new();
        public Task<ReportingAccessGrantRecord?> GetAsync(string grantId, CancellationToken ct = default)
        {
            lock (Gate)
                return Task.FromResult(_grants.GetValueOrDefault(grantId));
        }
        public Task<IReadOnlyList<ReportingAccessGrantRecord>> ListByPackageAsync(
            string tenantId, string packageId, CancellationToken ct = default)
        {
            lock (Gate)
                return Task.FromResult<IReadOnlyList<ReportingAccessGrantRecord>>(
                _grants.Values.Where(grant =>
                    grant.TenantId == tenantId
                    && grant.PackageId == packageId).ToArray());
        }
        public Task<bool> TryCreateAsync(ReportingAccessGrantRecord grant, CancellationToken ct = default)
        {
            lock (Gate)
                return Task.FromResult(_grants.TryAdd(grant.GrantId, grant));
        }
        public Task<bool> TryUpdateAsync(
            string grantId, long expectedVersion, ReportingAccessGrantRecord updatedGrant,
            CancellationToken ct = default)
        {
            lock (Gate)
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
        internal ReportingAccessGrantRecord? GetUnsafe(string grantId) =>
            _grants.GetValueOrDefault(grantId);
        internal void SetUnsafe(ReportingAccessGrantRecord grant) =>
            _grants[grant.GrantId] = grant;
    }
    private sealed class MemoryDeliveryStore(MemoryAccessGrantStore accessGrantStore) :
        IReportingDeliveryStore,
        IReportingDeliveryGrantDownloadCommitter
    {
        private readonly Dictionary<string, ReportingDeliveryJobRecord> _jobs =
            new(StringComparer.Ordinal);
        public int AtomicDownloadCommitCount { get; private set; }
        public Task<ReportingDeliveryJobRecord?> GetAsync(string jobId, CancellationToken ct = default) =>
            Task.FromResult(_jobs.GetValueOrDefault(jobId));
        public Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
            string idempotencyKey, CancellationToken ct = default) =>
            Task.FromResult(_jobs.Values.SingleOrDefault(job =>
                job.IdempotencyKey == idempotencyKey));
        public Task<ReportingDeliveryJobRecord?> GetByAccessGrantIdAsync(string accessGrantId, CancellationToken ct = default) =>
            Task.FromResult(_jobs.Values.SingleOrDefault(job =>
                job.AccessGrantId == accessGrantId));
        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByPackageAsync(
            string tenantId, string packageId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(
                _jobs.Values.Where(job =>
                    job.TenantId == tenantId
                    && job.PackageId == packageId)
                    .OrderBy(static job => job.JobId, StringComparer.Ordinal)
                    .ToArray());
        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByRunAsync(
            string tenantId, string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(
                _jobs.Values.Where(job =>
                    job.TenantId == tenantId
                    && job.ReleaseAuthorization.RunId == runId)
                    .OrderBy(static job => job.JobId, StringComparer.Ordinal)
                    .ToArray());
        public Task<bool> TryCreateAsync(ReportingDeliveryJobRecord job, CancellationToken ct = default)
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
            DateTimeOffset nowUtc, string leaseOwner, TimeSpan leaseDuration, int take,
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
            string jobId, long expectedVersion, ReportingDeliveryJobRecord updatedJob,
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
        public Task<ReportingDeliveryGrantDownloadCommitStatus> TryCommitAsync(
            ReportingDeliveryGrantDownloadCommit commit, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (accessGrantStore.Gate)
            {
                var currentGrant = accessGrantStore.GetUnsafe(commit.ConsumedGrant.GrantId);
                if (currentGrant is null
                    || currentGrant.Version != commit.ExpectedGrantVersion
                    || !_jobs.TryGetValue(
                        commit.DeliveryWithDownloadReceipt.JobId,
                        out var currentDelivery)
                    || currentDelivery.Version != commit.ExpectedDeliveryVersion
                    || commit.ConsumedGrant.Version != currentGrant.Version + 1
                    || commit.DeliveryWithDownloadReceipt.Version != currentDelivery.Version + 1)
                {
                    return Task.FromResult(
                        ReportingDeliveryGrantDownloadCommitStatus.ConcurrencyConflict);
                }
                var consumed = commit.ConsumedGrant;
                var updatedDelivery = commit.DeliveryWithDownloadReceipt;
                IReadOnlyList<string> expectedConsumedArtifactIds = currentGrant.ConsumedArtifactIds is null
                    ? [commit.ArtifactId]
                    : currentGrant.ConsumedArtifactIds.Contains(commit.ArtifactId, StringComparer.Ordinal)
                        ? currentGrant.ConsumedArtifactIds
                        : currentGrant.ConsumedArtifactIds
                            .Append(commit.ArtifactId)
                            .OrderBy(static artifactId => artifactId, StringComparer.Ordinal)
                            .ToArray();
                RequireInvariant(
                    consumed.GrantId == currentGrant.GrantId
                    && consumed.UseCount == currentGrant.UseCount + 1
                    && consumed.Version == currentGrant.Version + 1
                    && consumed.LastUsedAtUtc is not null
                    && consumed.ConsumedArtifactIds is not null
                    && consumed.ConsumedArtifactIds.SequenceEqual(
                        expectedConsumedArtifactIds,
                        StringComparer.Ordinal)
                    && currentDelivery.AccessGrantId == currentGrant.GrantId
                    && updatedDelivery.AccessGrantId == currentGrant.GrantId
                    && currentDelivery.TenantId == currentGrant.TenantId && updatedDelivery.TenantId == currentGrant.TenantId
                    && currentDelivery.PackageId == currentGrant.PackageId && updatedDelivery.PackageId == currentGrant.PackageId && currentDelivery.ReleaseAuthorization.PackageId == currentGrant.PackageId
                    && currentDelivery.ReleaseAuthorization.RunId == currentGrant.RunId && updatedDelivery.ReleaseAuthorization.RunId == currentGrant.RunId
                    && currentGrant.ArtifactIds.Contains(commit.ArtifactId, StringComparer.Ordinal)
                    && currentDelivery.ReleaseAuthorization.Artifacts.Any(artifact => artifact.ArtifactId == commit.ArtifactId)
                    && updatedDelivery.ReleaseAuthorization.Artifacts.Any(artifact => artifact.ArtifactId == commit.ArtifactId)
                    && updatedDelivery.Receipts.Count == currentDelivery.Receipts.Count + 1
                    && updatedDelivery.Receipts
                        .Take(currentDelivery.Receipts.Count)
                        .SequenceEqual(currentDelivery.Receipts),
                    "The access grant, delivery, package, artifact, and receipt prefix must advance atomically.");
                var receipt = updatedDelivery.Receipts[^1];
                RequireInvariant(
                    receipt.Kind == ReportingDeliveryReceiptKind.Downloaded && receipt.TransportId == currentDelivery.TransportId
                    && !string.IsNullOrWhiteSpace(receipt.EvidenceReference)
                    && receipt.OccurredAtUtc == consumed.LastUsedAtUtc
                    && receipt.ReceiptId == ReportingDeliveryDownloadReceiptIdentity.Create(
                        currentDelivery.JobId,
                        commit.ArtifactId,
                        receipt.EvidenceReference!),
                    "Grant consumption and its deterministic receipt must bind the same audited read.");
                accessGrantStore.SetUnsafe(commit.ConsumedGrant);
                _jobs[currentDelivery.JobId] = commit.DeliveryWithDownloadReceipt;
                AtomicDownloadCommitCount++;
                return Task.FromResult(ReportingDeliveryGrantDownloadCommitStatus.Committed);
            }
        }
        private static void RequireInvariant(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
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
        FileReconciliationBreakQueueRepository queue,
        FileEvidenceArtifactStore statementEvidenceStore,
        ScenarioLedgerJournalStore journalStore,
        IFundProfileTenancyRegistry tenancy,
        LedgerReportingAuthoritativeSource authoritativeSource) : IDisposable
    {
        public string DataRoot { get; } = dataRoot;
        public StatementReconciliationReportWorkflowExecution Execution { get; } = execution;
        public StatementReconciliationReportWorkflowService Workflow { get; } = workflow;
        public StatementReconciliationCaseworkHandoffService Casework { get; } = casework;
        public OperationsContinuityWorkflowService Operations { get; } = operations;
        public InMemoryOperationsWorkflowAuditStore AuditStore { get; } = auditStore;
        public FileReconciliationBreakQueueRepository Queue { get; } = queue;
        public FileEvidenceArtifactStore StatementEvidenceStore { get; } = statementEvidenceStore;
        public ScenarioLedgerJournalStore JournalStore { get; } = journalStore;
        public IFundProfileTenancyRegistry Tenancy { get; } = tenancy;
        public LedgerReportingAuthoritativeSource AuthoritativeSource { get; } = authoritativeSource;
        public void Dispose()
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }
    private sealed class ScenarioLedgerJournalStore(
        Guid authoritativeFundProfileId,
        Guid authoritativeFundAccountId,
        Guid authoritativeOrganizationId,
        Guid authoritativeLedgerBookId,
        Guid authoritativeAccountingPeriodId) : ILedgerJournalStore, ILedgerBookService
    {
        private readonly List<LedgerJournalEntryRecord> _records = [];
        private readonly LedgerBookRecord _book = new(
            authoritativeLedgerBookId,
            authoritativeFundProfileId.ToString("D"),
            authoritativeFundAccountId,
            FundStructureNodeKindDto.Account,
            "Primary statement authority book",
            "USD",
            ReportingNow.AddYears(-1),
            ReportingNow,
            AccountingBasis: AccountingBasisKindDto.Gaap);
        private LedgerAccountingPeriod _period = new(
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
            Version: 1);
        public void SeedBeginningCapital(decimal amount)
        {
            var journalId = Guid.Parse("77777777-7777-7777-7777-777777777777");
            var timestamp = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
            var dimensions = Dimensions();
            _records.Add(new LedgerJournalEntryRecord(
                new JournalEntry(
                    journalId,
                    timestamp,
                    "Retained beginning partners capital",
                    [
                        new LedgerEntry(
                            Guid.Parse("77777777-7777-7777-7777-777777777771"),
                            journalId,
                            timestamp,
                            LedgerAccounts.Cash,
                            amount,
                            0m,
                            "Retained beginning partners capital",
                            dimensions),
                        new LedgerEntry(
                            Guid.Parse("77777777-7777-7777-7777-777777777772"),
                            journalId,
                            timestamp,
                            LedgerAccounts.InvestorCapitalFor("client-investor"),
                            0m,
                            amount,
                            "Retained beginning partners capital",
                            dimensions)
                    ]),
                authoritativeFundAccountId,
                PriorAccountingPeriodId,
                CommandId: null,
                CorrelationId: null,
                GlobalSequence: 1,
                CreatedAt: timestamp,
                AccountingBasis: AccountingBasisKindDto.Gaap));
        }
        public Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default) =>
            Task.FromException<LedgerBookDto>(new NotSupportedException());
        public Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default) =>
            Task.FromResult<LedgerBookDto?>(ledgerBookId == authoritativeLedgerBookId ? BookDto() : null);
        public Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerBookDto>>(
                (string.IsNullOrWhiteSpace(query.FundProfileId)
                    || query.FundProfileId == authoritativeFundProfileId.ToString("D"))
                && (!query.FundStructureNodeId.HasValue || query.FundStructureNodeId == authoritativeFundAccountId)
                && (!query.FundStructureNodeKind.HasValue || query.FundStructureNodeKind == FundStructureNodeKindDto.Account)
                && (!query.AccountingBasis.HasValue || query.AccountingBasis == AccountingBasisKindDto.Gaap)
                    ? [BookDto()] : []);
        public Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default) =>
            Task.FromException<LedgerPeriodDto>(new NotSupportedException());
        public Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(
                (!query.LedgerBookId.HasValue || query.LedgerBookId == authoritativeLedgerBookId)
                && (string.IsNullOrWhiteSpace(query.FundProfileId)
                    || query.FundProfileId == authoritativeFundProfileId.ToString("D"))
                && (!query.FundStructureNodeId.HasValue || query.FundStructureNodeId == authoritativeFundAccountId)
                && (!query.AccountingBasis.HasValue || query.AccountingBasis == AccountingBasisKindDto.Gaap)
                && (!query.Status.HasValue || query.Status == PeriodDto().Status)
                && (!query.OpenOnly || PeriodDto().Status == LedgerPeriodStatusDto.Open)
                    ? [PeriodDto()] : []);
        public Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(
                (!ledgerBookId.HasValue || ledgerBookId == authoritativeLedgerBookId)
                && PeriodDto().Status == LedgerPeriodStatusDto.Open
                    ? [PeriodDto()] : []);
        public Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<LedgerPeriodSummaryDto?>(periodId == authoritativeAccountingPeriodId ? PeriodSummary() : null);
        public Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(
            Guid periodId,
            CloseLedgerPeriodRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (periodId != authoritativeAccountingPeriodId)
                throw new LedgerPeriodTransitionException("The accounting period was not found.");
            var currentStatus = PeriodDto().Status;
            var nextStatus = request.CloseKind == LedgerPeriodCloseKindDto.SoftClose
                ? LedgerPeriodStatusDto.SoftClosed
                : LedgerPeriodStatusDto.HardClosed;
            if (currentStatus != (request.CloseKind == LedgerPeriodCloseKindDto.SoftClose
                    ? LedgerPeriodStatusDto.Open : LedgerPeriodStatusDto.SoftClosed))
                throw new LedgerPeriodTransitionException(
                    $"Cannot {request.CloseKind} a {currentStatus} accounting period.");
            _period = _period with
            {
                Status = nextStatus.ToString(),
                ClosedAt = nextStatus == LedgerPeriodStatusDto.HardClosed
                    ? ReportingNow : ReportingNow.AddMinutes(-5),
                Version = _period.Version + 1
            };
            var period = PeriodDto();
            return Task.FromResult(new LedgerPeriodCloseResultDto(
                period,
                PeriodSummary(),
                new OperatorWorkItemDto(
                    $"ledger-period-close:{period.PeriodId:D}:{period.Version}",
                    OperatorWorkItemKindDto.LedgerPeriodClose,
                    $"{period.Label} {nextStatus}",
                    $"The governed accounting period is {nextStatus}.",
                    OperatorWorkItemToneDto.Success,
                    ReportingNow,
                    FundAccountId: authoritativeFundAccountId)));
        }
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Entry.IsBalanced)
                throw new LedgerValidationException("Journal entry must be balanced.");
            if (entry.PeriodId != authoritativeAccountingPeriodId
                || entry.LedgerBookId != authoritativeLedgerBookId
                || entry.AccountingBasis != AccountingBasisKindDto.Gaap)
            {
                throw new InvalidDataException("Journal write escaped the authoritative reporting scope.");
            }
            _records.Add(new LedgerJournalEntryRecord(
                entry.Entry,
                entry.AggregateId,
                entry.PeriodId,
                entry.CommandId,
                entry.CorrelationId,
                _records.Count + 1L,
                ReportingNow,
                entry.AccountingBasis,
                entry.AccountingPolicyId,
                entry.AccountingPolicyVersion,
                entry.RuleId,
                entry.RuleVersion,
                entry.SourceEventId,
                entry.SourceJournalEntryId,
                entry.PostingKind,
                entry.AdjustmentApproval));
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<LedgerJournalEntryRecord> selected = _records
                .Where(record =>
                    (!query.PeriodId.HasValue || record.PeriodId == query.PeriodId.Value)
                    && (!query.AggregateId.HasValue || record.AggregateId == query.AggregateId.Value)
                    && (!query.OccurredFrom.HasValue || record.Entry.Timestamp >= query.OccurredFrom.Value)
                    && (!query.OccurredTo.HasValue || record.Entry.Timestamp <= query.OccurredTo.Value)
                    && record.Entry.Lines.Any(line => DimensionsMatch(line.Dimensions, query.LineDimensions)))
                .ToArray();
            return Task.FromResult(selected);
        }
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(
            Guid periodId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _records.Where(record => record.PeriodId == periodId).ToArray());
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(
            Guid aggregateId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _records.Where(record => record.AggregateId == aggregateId).ToArray());
        public Task<LedgerAccountingPeriod?> GetPeriodAsync(
            Guid periodId,
            CancellationToken ct = default) =>
            Task.FromResult<LedgerAccountingPeriod?>(
                periodId == authoritativeAccountingPeriodId ? _period : null);
        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(
                (!ledgerBookId.HasValue || ledgerBookId.Value == authoritativeLedgerBookId)
                && (string.IsNullOrWhiteSpace(status)
                    || string.Equals(status, _period.Status, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(fundProfileId)
                    || string.Equals(
                        fundProfileId,
                        authoritativeFundProfileId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase))
                && (!fundStructureNodeId.HasValue
                    || fundStructureNodeId.Value == authoritativeFundAccountId)
                    ? [_period]
                    : []);
        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            if (_period.Version != expectedVersion)
                throw new InvalidOperationException("Unexpected period version.");
            _period = period;
            return Task.FromResult(_period);
        }
        public Task<LedgerBookRecord?> GetLedgerBookAsync(
            Guid requestedLedgerBookId,
            CancellationToken ct = default) =>
            Task.FromResult<LedgerBookRecord?>(
                requestedLedgerBookId == authoritativeLedgerBookId ? _book : null);
        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerBookRecord>>(
                (string.IsNullOrWhiteSpace(fundProfileId)
                    || string.Equals(fundProfileId, _book.FundProfileId, StringComparison.OrdinalIgnoreCase))
                && (!fundStructureNodeId.HasValue || fundStructureNodeId == _book.FundStructureNodeId)
                && (!fundStructureNodeKind.HasValue || fundStructureNodeKind == _book.FundStructureNodeKind)
                    ? [_book]
                    : []);
        public Task<LedgerBookRecord> SaveLedgerBookAsync(
            LedgerBookRecord book,
            CancellationToken ct = default) =>
            Task.FromResult(book);
        private LedgerBookDto BookDto() => new(
            _book.LedgerBookId, _book.FundProfileId, _book.FundStructureNodeId,
            _book.FundStructureNodeKind, _book.DisplayName, _book.BaseCurrency,
            _book.CreatedAt, _book.UpdatedAt, _book.Description, _book.AccountingBasis,
            _book.AccountingPolicyId, _book.AccountingPolicyVersion);
        private LedgerPeriodDto PeriodDto() => new(
            _period.PeriodId, _period.LedgerBookId!.Value, _period.FiscalYear, _period.PeriodNo,
            _period.Label, _period.StartDate, _period.EndDate,
            Enum.Parse<LedgerPeriodStatusDto>(_period.Status), _period.OpenedAt,
            _period.ClosedAt, _period.Version, AccountingBasisKindDto.Gaap);
        private LedgerPeriodSummaryDto PeriodSummary() => new(
            authoritativeAccountingPeriodId, authoritativeLedgerBookId, 2026, 6, "2026-06",
            [
                new LedgerPeriodTrialBalanceLineDto(
                    "Cash", nameof(LedgerAccountType.Asset), null, null,
                    250_000m, 0m, 250_000m, 1, AccountingBasisKindDto.Gaap),
                new LedgerPeriodTrialBalanceLineDto(
                    "Investor Capital", nameof(LedgerAccountType.Equity), null, "client-investor",
                    0m, 250_000m, -250_000m, 1, AccountingBasisKindDto.Gaap)
            ],
            250_000m, 250_000m, 0m, null, 0, LedgerPeriodSignoffStatusDto.SignedOff,
            _period.ClosedAt ?? ReportingNow, AccountingBasisKindDto.Gaap);
        private static bool DimensionsMatch(
            LedgerLineDimensionSet? actual,
            LedgerLineDimensionSet? expected) =>
            expected is null
            || actual is not null
            && Matches(actual.FundId, expected.FundId)
            && Matches(actual.OrganizationId, expected.OrganizationId)
            && Matches(actual.BookId, expected.BookId);
        private static bool Matches(string? actual, string? expected) =>
            string.IsNullOrWhiteSpace(expected)
            || string.Equals(actual, expected, StringComparison.Ordinal);
        private LedgerLineDimensionSet Dimensions() => new(
            FundId: authoritativeFundProfileId.ToString("D"),
            OrganizationId: authoritativeOrganizationId.ToString("D"),
            BookId: authoritativeLedgerBookId.ToString("D"));
    }
}
