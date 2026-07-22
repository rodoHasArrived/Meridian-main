using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Reporting;

public sealed class ReportingGovernanceCanonicalValidationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GovernedLifecycle_AllowsReadinessEvaluationBeforeLaterCertifiedSnapshotCapture()
    {
        var scenario = NewScenario(snapshotCapturedAtUtc: Now.AddMinutes(-1));
        var run = await CreateValidatedRunAsync(
            scenario,
            readinessEvaluatedAtUtc: Now.AddMinutes(-2));

        var validate = () =>
            ReportingGovernanceCanonicalValidation.ValidateGovernedRun(run);

        validate.Should().NotThrow();
        run.Readiness!.EvaluatedAtUtc.Should().BeBefore(run.Snapshot.CapturedAtUtc);
        run.AuditTrail[^1].OccurredAtUtc.Should().BeOnOrAfter(run.Readiness.EvaluatedAtUtc);
    }

    [Fact]
    public async Task GovernedLifecycle_RejectsReceiptScopeAndMakerCheckerDrift()
    {
        var scenario = NewScenario();
        var released = await CreateReleasedRunAsync(scenario);
        var driftedReadiness = released.Readiness! with { OrganizationId = "other-organization" };
        driftedReadiness = driftedReadiness with
        {
            ReceiptHash = ReportingGovernanceCanonicalValidation.ComputeReadinessReceiptHash(driftedReadiness)
        };
        var wrongReadinessScope = released with
        {
            Readiness = driftedReadiness
        };
        var sameActorRelease = released with
        {
            Release = released.Release! with { Authority = released.Approval!.Authority }
        };

        Action scopeValidation = () =>
            ReportingGovernanceCanonicalValidation.ValidateGovernedRun(wrongReadinessScope);
        Action makerCheckerValidation = () =>
            ReportingGovernanceCanonicalValidation.ValidateGovernedRun(sameActorRelease);

        scopeValidation.Should().Throw<ReportingGovernanceException>()
            .WithMessage("*exact run, tenant, organization, company*");
        makerCheckerValidation.Should().Throw<ReportingGovernanceException>()
            .WithMessage("*independent human release authority*");
    }

    [Fact]
    public async Task DraftFinality_CanReachApprovedButCannotReleaseOrAdvertiseReleaseAction()
    {
        var scenario = NewScenario(finality: ReportingFinalityDto.Draft);
        var approved = await CreateValidatedRunAsync(scenario);
        approved = await scenario.Service.SubmitAsync(
            approved.RunId,
            approved.Version,
            scenario.Creator);
        approved = await scenario.Service.ApproveAsync(
            approved.RunId,
            approved.Version,
            "Draft reviewed without final promotion",
            scenario.Approver);

        Func<Task> release = async () => await scenario.Service.ReleaseAsync(
            approved.RunId,
            approved.Version,
            new ReportingReleaseEvidence(
                "manifest-draft",
                new string('4', 64),
                [new ReportingArtifactReference("artifact-draft", new string('3', 64), 128)],
                ["evidence-draft"]),
            scenario.Releaser);
        var projected = ReportingGovernanceApiProjector.ProjectRun(
            approved,
            new ReportingGovernanceCallerContext(
                scenario.Releaser.ActorId,
                approved.Scope.TenantId,
                approved.Scope.CompanyId,
                UserPermission.DeliverReporting,
                ReportingCommandOrigin.HumanOperator,
                "correlation-release",
                [scenario.Releaser.ActorId]));

        await release.Should().ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*Draft-certified bytes cannot be released*");
        projected.ActionAvailability.Single(action => action.Action == "ReleaseRun")
            .IsAllowed.Should().BeFalse();
        projected.ActionAvailability.Single(action => action.Action == "ReleaseRun")
            .BlockedReason.Should().Contain("Draft-certified bytes");
    }

    [Fact]
    public async Task AuditChain_RejectsPrincipalAudienceTamperingWithoutReseal()
    {
        var scenario = NewScenario();
        var created = await scenario.Service.CreateRunAsync(
            scenario.Request,
            scenario.Creator);
        var retained = created.AuditTrail[0];
        var tampered = retained with
        {
            Authority = retained.Authority with
            {
                PrincipalIds = retained.Authority.PrincipalIds.Add("injected-audience")
            }
        };

        ReportingGovernanceAuditChain.Verify([retained]).Should().BeTrue();
        ReportingGovernanceAuditChain.Verify([tampered]).Should().BeFalse();
    }

    [Fact]
    public async Task RestatementBinding_RequiresHumanIndependenceChangedLineEvidenceAndExactNewRevisionLineage()
    {
        var scenario = NewScenario();
        var predecessor = await CreateReleasedRunAsync(scenario);
        var request = await scenario.Service.RequestRestatementAsync(
            new ReportingRestatementRequestCommand(
                predecessor.RunId,
                predecessor.Version,
                "Correct a retained NAV line",
                [new ReportingRestatementChangedLine(
                    "nav.total",
                    "100",
                    "101",
                    ["evidence-nav-correction"])]),
            scenario.Creator);
        var replacementSnapshot = scenario.Request.Snapshot with
        {
            SnapshotId = "snapshot-restated",
            SnapshotHash = new string('7', 64),
            CapturedAtUtc = Now
        };
        var approved = await scenario.Service.ApproveRestatementAsync(
            new ReportingRestatementApprovalCommand(
                request.RequestId,
                request.Version,
                replacementSnapshot,
                "run-restated",
                request.ChangedLines),
            scenario.RestatementApprover);
        var revised = await scenario.Service.BeginExecutionAsync(
            approved.DraftRun.RunId,
            approved.DraftRun.Version,
            scenario.RestatementApprover);
        revised = await scenario.Service.CompleteExecutionAsync(
            revised.RunId,
            revised.Version,
            scenario.RestatementApprover);
        var revisedReadiness = BuildReadiness(revised, Now);
        revised = await scenario.Service.ValidateAsync(
            revised.RunId,
            revised.Version,
            revisedReadiness,
            scenario.RestatementApprover);
        revised = await scenario.Service.SubmitAsync(
            revised.RunId,
            revised.Version,
            scenario.RestatementApprover);
        Func<Task> makerApprove = async () => await scenario.Service.ApproveAsync(
            revised.RunId,
            revised.Version,
            "Maker cannot approve own restatement draft",
            scenario.RestatementApprover);
        await makerApprove.Should().ThrowAsync<ReportingGovernanceAuthorizationException>()
            .WithMessage("*creator cannot approve*");
        var independentlyApproved = await scenario.Service.ApproveAsync(
            revised.RunId,
            revised.Version,
            "Independent restatement review complete",
            scenario.Approver);

        Action valid = () => ReportingGovernanceCanonicalValidation.ValidateRestatementBinding(
            approved.Request,
            predecessor,
            approved.DraftRun);
        Action missingEvidence = () => ReportingGovernanceCanonicalValidation.ValidateRestatementRequest(
            approved.Request with
            {
                ChangedLines =
                [
                    approved.Request.ChangedLines[0] with
                    {
                        EvidenceIds = []
                    }
                ]
            });
        Action sameActor = () => ReportingGovernanceCanonicalValidation.ValidateRestatementRequest(
            approved.Request with { ApprovedBy = approved.Request.RequestedBy });
        Action wrongLineage = () => ReportingGovernanceCanonicalValidation.ValidateRestatementBinding(
            approved.Request,
            predecessor,
            approved.DraftRun with { RestatementOfRunId = "other-predecessor" });

        valid.Should().NotThrow();
        independentlyApproved.GovernanceState.Should().Be(GovernedReportingState.Approved);
        missingEvidence.Should().Throw<ReportingGovernanceException>()
            .WithMessage("*evidence*");
        sameActor.Should().Throw<ReportingGovernanceException>()
            .WithMessage("*independent human operators*");
        wrongLineage.Should().Throw<ReportingGovernanceException>();
    }

    [Fact]
    public void ReconciliationEvidence_RejectsDefaultCompletionAndReceiptTimestamps()
    {
        var source = NewSourceCheckpoint();
        var validCompletion = new ReportingReconciliationCompletionEvidence(
            "completion-1",
            new string('5', 64),
            Now,
            HasOpenBreaks: false,
            ["evidence-close"]);
        var receipt = ReportingReconciliationEvidenceValidation.CreateReceipt(
            source,
            validCompletion);

        Action defaultCompletion = () =>
            ReportingReconciliationEvidenceValidation.ValidateCompletion(
                validCompletion with { CompletedAtUtc = default });
        Action defaultReceipt = () =>
            ReportingReconciliationEvidenceValidation.Validate(
                receipt with { ReconciledAtUtc = default });

        defaultCompletion.Should().Throw<ArgumentException>()
            .WithMessage("*UTC timestamp*");
        defaultReceipt.Should().Throw<ArgumentException>()
            .WithMessage("*UTC timestamp*");
    }

    [Fact]
    public async Task Coordinator_EndToEndRetainsExactBytesRestartsAndEnforcesRestatementAndManifestIntegrity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            Guid.NewGuid().ToString("N"),
            "reporting-coordinator-restart");
        Directory.CreateDirectory(root);
        try
        {
            await ExerciseCoordinatorRestartAsync(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Coordinator_PreUpgradePackageWithoutPreviewRemainsGovernableAndDiscoverableAfterRelease()
    {
        var template = CoordinatorTemplate();
        var source = new VersionedCoordinatorSource();
        var certification = new ReportingRunCertificationService(
            source,
            new CoordinatorReconciliationSource());
        var buildOrchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => Now);
        var certified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-pre-preview"),
            CoordinatorAccess("maker-a"));
        var currentManifest = await buildOrchestration.ExecuteAsync(
            CoordinatorJob("job-pre-preview", template, certified),
            CancellationToken.None);
        var preUpgradeManifest = currentManifest with
        {
            Artifacts = currentManifest.Artifacts
                .Where(static artifact => !artifact.EndsWith(".preview.json", StringComparison.Ordinal))
                .ToImmutableArray()
        };
        var repository = new MemoryGovernanceRepository();
        var coordinator = CreateCoordinator(
            repository,
            certification,
            new FixedManifestOrchestration(preUpgradeManifest),
            new RestartableArtifactStore(Now),
            new RestartableArtifactCatalog(),
            new RestartableArtifactAuditStore(),
            new DeterministicReportingCertifiedArtifactProducer(),
            template);
        var maker = CoordinatorCaller("maker-a");
        var approver = CoordinatorCaller("approver-b");
        var releaser = CoordinatorCaller("releaser-c");

        var run = await coordinator.CreateFromCompletedCertifiedManifestAsync(
            preUpgradeManifest.RunId,
            maker);
        (await coordinator.ListRetainedArtifactsAsync(run.RunId, maker)).Should().BeEmpty(
            "pre-upgrade packages had no preview and final bytes remain release-gated");
        run = await coordinator.ValidateAsync(run.RunId, run.Version, maker);
        run = await coordinator.SubmitAsync(run.RunId, run.Version, maker);
        run = await coordinator.ApproveAsync(
            run.RunId,
            run.Version,
            "Independent legacy-package review complete",
            approver);
        run = await coordinator.ReleaseAsync(run.RunId, run.Version, releaser);

        var released = await coordinator.ListRetainedArtifactsAsync(run.RunId, releaser);
        released.Select(static artifact => artifact.ArtifactId)
            .Should().BeEquivalentTo(preUpgradeManifest.Artifacts);
        released.Should().NotContain(static artifact => artifact.IsPreview);
    }

    [Fact]
    public async Task Coordinator_FinalReleaseRevalidatesAndBlocksWhenCanonicalQueueReopensAfterCertification()
    {
        var template = CoordinatorTemplate();
        var source = new VersionedCoordinatorSource();
        var reconciliation = new CoordinatorReconciliationSource();
        var certification = new ReportingRunCertificationService(source, reconciliation);
        var orchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => Now);
        var certified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-release-revalidation"),
            CoordinatorAccess("maker-a"));
        var manifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-release-revalidation", template, certified),
            CancellationToken.None);
        var repository = new MemoryGovernanceRepository();
        var coordinator = CreateCoordinator(
            repository,
            certification,
            orchestration,
            new RestartableArtifactStore(Now),
            new RestartableArtifactCatalog(),
            new RestartableArtifactAuditStore(),
            new DeterministicReportingCertifiedArtifactProducer(),
            template);
        var maker = CoordinatorCaller("maker-a");
        var approver = CoordinatorCaller("approver-b");
        var releaser = CoordinatorCaller("releaser-c");
        var run = await coordinator.CreateFromCompletedCertifiedManifestAsync(manifest.RunId, maker);
        run = await coordinator.ValidateAsync(run.RunId, run.Version, maker);
        run = await coordinator.SubmitAsync(run.RunId, run.Version, maker);
        run = await coordinator.ApproveAsync(
            run.RunId,
            run.Version,
            "Independent review completed before the later queue change",
            approver);
        reconciliation.CanonicalQueueReopened = true;

        Func<Task> release = () => coordinator.ReleaseAsync(run.RunId, run.Version, releaser);

        await release.Should().ThrowAsync<ReportingReconciliationEvidenceInvalidException>()
            .WithMessage("*reopened after certification*");
        (await coordinator.GetAsync(run.RunId, maker)).GovernanceState
            .Should().Be(GovernedReportingState.Approved);
    }

    [Fact]
    public async Task Coordinator_MalformedRenderedManifestFailsBeforeRetentionAndCorrectedRetrySucceeds()
    {
        var template = CoordinatorTemplate();
        var source = new VersionedCoordinatorSource();
        var certification = new ReportingRunCertificationService(
            source,
            new CoordinatorReconciliationSource());
        var orchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => Now);
        var certified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-malformed-renderer-manifest"),
            CoordinatorAccess("maker-a"));
        var manifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-malformed-renderer-manifest", template, certified),
            CancellationToken.None);
        var repository = new MemoryGovernanceRepository();
        var artifactCatalog = new RestartableArtifactCatalog();
        var coordinator = CreateCoordinator(
            repository,
            certification,
            orchestration,
            new RestartableArtifactStore(Now),
            artifactCatalog,
            new RestartableArtifactAuditStore(),
            new MalformedManifestOnceProducer(),
            template);
        var maker = CoordinatorCaller("maker-a");

        Func<Task> malformed = () => coordinator.CreateFromCompletedCertifiedManifestAsync(
            manifest.RunId,
            maker);

        await malformed.Should().ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*pre-retention integrity validation*");
        var failed = await coordinator.GetAsync(manifest.RunId, maker);
        failed.ExecutionState.Should().Be(GovernedReportingExecutionState.Failed);
        Action noImmutablePackage = () => artifactCatalog.RequirePackage(
            failed.Scope.TenantId,
            ReportingArtifactPackageIdentity.Create(failed));
        noImmutablePackage.Should().Throw<KeyNotFoundException>();

        var recovered = await coordinator.CreateFromCompletedCertifiedManifestAsync(
            manifest.RunId,
            maker);
        recovered.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);
        artifactCatalog.RequirePackage(
                recovered.Scope.TenantId,
                ReportingArtifactPackageIdentity.Create(recovered))
            .Artifacts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Coordinator_SelfConsistentNonCanonicalArtifactDescriptorsFailBeforeRetentionAndCorrectedRetrySucceeds()
    {
        var template = CoordinatorTemplate();
        var source = new VersionedCoordinatorSource();
        var certification = new ReportingRunCertificationService(
            source,
            new CoordinatorReconciliationSource());
        var orchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => Now);
        var certified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-noncanonical-artifact-descriptor"),
            CoordinatorAccess("maker-a"));
        var manifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-noncanonical-artifact-descriptor", template, certified),
            CancellationToken.None);
        var repository = new MemoryGovernanceRepository();
        var artifactStore = new RestartableArtifactStore(Now);
        var artifactCatalog = new RestartableArtifactCatalog();
        var coordinator = CreateCoordinator(
            repository,
            certification,
            orchestration,
            artifactStore,
            artifactCatalog,
            new RestartableArtifactAuditStore(),
            new CanonicalDescriptorDriftOnceProducer(),
            template);
        var maker = CoordinatorCaller("maker-a");

        Func<Task> drifted = () => coordinator.CreateFromCompletedCertifiedManifestAsync(
            manifest.RunId,
            maker);

        await drifted.Should().ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*conflicts with its canonical id, filename, content type*");
        var failed = await coordinator.GetAsync(manifest.RunId, maker);
        failed.ExecutionState.Should().Be(GovernedReportingExecutionState.Failed);
        artifactStore.StoredArtifactCount.Should().Be(0,
            "canonical descriptor validation must run before the vault writes any bytes");
        Action noImmutablePackage = () => artifactCatalog.RequirePackage(
            failed.Scope.TenantId,
            ReportingArtifactPackageIdentity.Create(failed));
        noImmutablePackage.Should().Throw<KeyNotFoundException>();

        var recovered = await coordinator.CreateFromCompletedCertifiedManifestAsync(
            manifest.RunId,
            maker);
        recovered.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);
        artifactStore.StoredArtifactCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Coordinator_OmittedCanonicalSectionFailsBeforeRetentionAndCorrectedRetrySucceeds()
    {
        var template = CoordinatorTemplate();
        var source = new VersionedCoordinatorSource();
        var certification = new ReportingRunCertificationService(
            source,
            new CoordinatorReconciliationSource());
        var orchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => Now);
        var certified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-omitted-canonical-section"),
            CoordinatorAccess("maker-a"));
        var manifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-omitted-canonical-section", template, certified),
            CancellationToken.None);
        var repository = new MemoryGovernanceRepository();
        var artifactStore = new RestartableArtifactStore(Now);
        var artifactCatalog = new RestartableArtifactCatalog();
        var coordinator = CreateCoordinator(
            repository,
            certification,
            orchestration,
            artifactStore,
            artifactCatalog,
            new RestartableArtifactAuditStore(),
            new CanonicalSectionDriftOnceProducer(),
            template);
        var maker = CoordinatorCaller("maker-a");

        Func<Task> drifted = () => coordinator.CreateFromCompletedCertifiedManifestAsync(
            manifest.RunId,
            maker);

        await drifted.Should().ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*exact canonical section identities, hashes, and lineage*");
        var failed = await coordinator.GetAsync(manifest.RunId, maker);
        failed.ExecutionState.Should().Be(GovernedReportingExecutionState.Failed);
        artifactStore.StoredArtifactCount.Should().Be(0,
            "canonical section validation must run before the vault writes any bytes");
        Action noImmutablePackage = () => artifactCatalog.RequirePackage(
            failed.Scope.TenantId,
            ReportingArtifactPackageIdentity.Create(failed));
        noImmutablePackage.Should().Throw<KeyNotFoundException>();

        var recovered = await coordinator.CreateFromCompletedCertifiedManifestAsync(
            manifest.RunId,
            maker);
        recovered.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);
        artifactStore.StoredArtifactCount.Should().BeGreaterThan(0);
    }

    private static async Task ExerciseCoordinatorRestartAsync(string root)
    {
        var template = CoordinatorTemplate();
        var source = new VersionedCoordinatorSource();
        var certification = new ReportingRunCertificationService(
            source,
            new CoordinatorReconciliationSource());
        var initialRunStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => Now,
            initialRunStore);
        var initialCertified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-initial"),
            CoordinatorAccess("maker-a"));
        var manifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-initial", template, initialCertified),
            CancellationToken.None);

        var repository = new MemoryGovernanceRepository();
        var artifactStore = new RestartableArtifactStore(Now);
        var artifactCatalog = new RestartableArtifactCatalog();
        var artifactAudit = new RestartableArtifactAuditStore();
        var producer = new DeterministicReportingCertifiedArtifactProducer();
        var coordinator = CreateCoordinator(
            repository,
            certification,
            orchestration,
            artifactStore,
            artifactCatalog,
            artifactAudit,
            producer,
            template);
        var maker = CoordinatorCaller("maker-a");
        var approver = CoordinatorCaller("approver-b");
        var releaser = CoordinatorCaller("releaser-c");

        var run = await coordinator.CreateFromCompletedCertifiedManifestAsync(manifest.RunId, maker);
        var expectedProduction = await producer.ProduceAsync(manifest);
        var retainedPackage = artifactCatalog.RequirePackage(
            run.Scope.TenantId,
            ReportingArtifactPackageIdentity.Create(run));
        retainedPackage.Artifacts.Should().HaveCount(expectedProduction.Artifacts.Length);
        foreach (var expectedArtifact in expectedProduction.Artifacts)
        {
            var retainedRecord = retainedPackage.Artifacts.Single(record =>
                record.ArtifactId == expectedArtifact.ArtifactId);
            var retainedBytes = await artifactStore.ReadAsync(retainedRecord.Identity);
            retainedBytes.Content.Should().Equal(expectedArtifact.Content.ToArray());
            retainedRecord.RunId.Should().Be(run.RunId);
            retainedRecord.Scope.Should().Be(run.Scope);
            retainedRecord.Access.Should().BeEquivalentTo(run.Access, options => options.WithStrictOrdering());
            retainedRecord.Snapshot.Should().Be(run.Snapshot);
        }

        var discoverable = await coordinator.ListRetainedArtifactsAsync(run.RunId, maker);
        discoverable.Should().ContainSingle();
        var preview = discoverable.Should().ContainSingle(static artifact => artifact.IsPreview).Subject;
        preview.ContentHashSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        preview.DownloadRoute.Should().MatchRegex(
            $"^/api/fund-structure/reporting/runs/{Regex.Escape(run.RunId)}/artifacts/b64_[A-Za-z0-9_-]+$");
        preview.DownloadRoute.Should().NotContain(preview.ArtifactId);
        var previewDownload = await coordinator.DownloadRetainedArtifactAsync(
            run.RunId,
            preview.ArtifactId,
            maker);
        previewDownload.Content.Should().NotBeEmpty();
        HashBytes(previewDownload.Content).Should().Be(preview.ContentHashSha256);
        previewDownload.AccessAuditEventId.Should().NotBeNullOrWhiteSpace();
        var primary = ReportingArtifactDeclaration.Build(manifest)
            .Should().ContainSingle(static artifact =>
                artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput)
            .Subject;
        Func<Task> downloadUnreleasedPrimary = () => coordinator.DownloadRetainedArtifactAsync(
            run.RunId,
            primary.ArtifactId,
            maker);
        await downloadUnreleasedPrimary.Should().ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*release-gated*");

        var failedCertified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-retention-failure"),
            CoordinatorAccess("maker-a"));
        var failedManifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-retention-failure", template, failedCertified),
            CancellationToken.None);
        artifactStore.FailWrites = true;
        Func<Task> failRetention = async () =>
            await coordinator.CreateFromCompletedCertifiedManifestAsync(failedManifest.RunId, maker);
        await failRetention.Should().ThrowAsync<IOException>();
        var interrupted = await repository.GetRunAsync("tenant-a", failedManifest.RunId);
        interrupted.Should().NotBeNull();
        interrupted!.ExecutionState.Should().Be(GovernedReportingExecutionState.Failed);
        artifactStore.FailWrites = false;
        var recovered = await coordinator.CreateFromCompletedCertifiedManifestAsync(failedManifest.RunId, maker);
        recovered.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);

        var corruptReadCertified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-readback-failure"),
            CoordinatorAccess("maker-a"));
        var corruptReadManifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-readback-failure", template, corruptReadCertified),
            CancellationToken.None);
        artifactStore.CorruptAllReads = true;
        Func<Task> failReadBack = async () =>
            await coordinator.CreateFromCompletedCertifiedManifestAsync(corruptReadManifest.RunId, maker);
        await failReadBack.Should().ThrowAsync<ReportingArtifactIntegrityException>();
        var readBackFailure = await repository.GetRunAsync("tenant-a", corruptReadManifest.RunId);
        readBackFailure.Should().NotBeNull();
        readBackFailure!.ExecutionState.Should().Be(GovernedReportingExecutionState.Failed);
        artifactStore.CorruptAllReads = false;
        var readBackRecovered = await coordinator.CreateFromCompletedCertifiedManifestAsync(
            corruptReadManifest.RunId,
            maker);
        readBackRecovered.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);

        var missingCatalogCertified = await certification.CertifyAsync(
            template,
            CoordinatorReadiness("evaluation-catalog-readback-failure"),
            CoordinatorAccess("maker-a"));
        var missingCatalogManifest = await orchestration.ExecuteAsync(
            CoordinatorJob("job-catalog-readback-failure", template, missingCatalogCertified),
            CancellationToken.None);
        artifactCatalog.LoseCatalogReads = true;
        Func<Task> failCatalogReadBack = async () =>
            await coordinator.CreateFromCompletedCertifiedManifestAsync(missingCatalogManifest.RunId, maker);
        await failCatalogReadBack.Should().ThrowAsync<ReportingArtifactCatalogIntegrityException>();
        var catalogReadBackFailure = await repository.GetRunAsync("tenant-a", missingCatalogManifest.RunId);
        catalogReadBackFailure.Should().NotBeNull();
        catalogReadBackFailure!.ExecutionState.Should().Be(GovernedReportingExecutionState.Failed);
        artifactCatalog.LoseCatalogReads = false;
        var catalogReadBackRecovered = await coordinator.CreateFromCompletedCertifiedManifestAsync(
            missingCatalogManifest.RunId,
            maker);
        catalogReadBackRecovered.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);

        run = await coordinator.ValidateAsync(run.RunId, run.Version, maker);
        run = await coordinator.SubmitAsync(run.RunId, run.Version, maker);
        run = await coordinator.ApproveAsync(
            run.RunId,
            run.Version,
            "Independent review complete",
            approver);
        run = await coordinator.ReleaseAsync(run.RunId, run.Version, releaser);
        run.GovernanceState.Should().Be(GovernedReportingState.Released);
        run.Release!.ManifestId.Should().Be(expectedProduction.ManifestArtifactId);
        run.Release.Artifacts.Select(static artifact => artifact.ArtifactId)
            .Should().BeEquivalentTo(expectedProduction.Artifacts.Select(static artifact => artifact.ArtifactId));
        var releasedArtifacts = await coordinator.ListRetainedArtifactsAsync(run.RunId, maker);
        releasedArtifacts.Should().HaveCount(expectedProduction.Artifacts.Length);
        var viewer = maker with { Permissions = UserPermission.ViewReporting };
        Func<Task> viewerDownloadsPrimary = () => coordinator.DownloadRetainedArtifactAsync(
            run.RunId,
            primary.ArtifactId,
            viewer);
        await viewerDownloadsPrimary.Should().ThrowAsync<ReportingGovernanceAuthorizationException>()
            .WithMessage("*DeliverReporting*");
        var releasedPrimary = await coordinator.DownloadRetainedArtifactAsync(
            run.RunId,
            primary.ArtifactId,
            maker);
        releasedPrimary.Content.Should().NotBeEmpty();
        HashBytes(releasedPrimary.Content).Should().Be(releasedPrimary.Descriptor.ContentHashSha256);

        var restartedRunStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var rehydratedManifest = restartedRunStore.GetManifest(run.Scope.TenantId, run.RunId);
        rehydratedManifest.Should().NotBeNull();
        rehydratedManifest.Should().NotBeSameAs(manifest);
        rehydratedManifest.Should().BeEquivalentTo(manifest);
        var restartedOrchestration = new ReportingOrchestrationService(
            new SingleTemplateCatalog(template),
            new DeterministicReportingSectionRenderer(),
            () => Now,
            restartedRunStore);
        var restarted = CreateCoordinator(
            repository,
            certification,
            restartedOrchestration,
            artifactStore,
            artifactCatalog,
            artifactAudit,
            producer,
            template);
        var afterRestart = await restarted.GetAsync(run.RunId, releaser);
        afterRestart.Should().Be(run);
        var afterRestartArtifacts = await restarted.ListRetainedArtifactsAsync(run.RunId, releaser);
        afterRestartArtifacts.Should().HaveCount(expectedProduction.Artifacts.Length);
        var afterRestartPreview = afterRestartArtifacts.Single(static artifact => artifact.IsPreview);
        var afterRestartPreviewDownload = await restarted.DownloadRetainedArtifactAsync(
            run.RunId,
            afterRestartPreview.ArtifactId,
            releaser);
        HashBytes(afterRestartPreviewDownload.Content)
            .Should().Be(afterRestartPreview.ContentHashSha256);

        var request = await restarted.RequestRestatementAsync(
            run.RunId,
            run.Version,
            "Correct the retained certified amount",
            maker);
        source.Version = 2;
        var restatement = await restarted.ApproveRestatementAsync(
            request.RequestId,
            request.Version,
            approver);
        var revision = restatement.DraftRun;
        revision.Revision.Should().Be(2);
        revision.RestatementOfRunId.Should().Be(run.RunId);
        revision.RestatementRequestId.Should().Be(request.RequestId);
        revision.Scope.Should().Be(run.Scope);
        revision.Access.Should().BeEquivalentTo(
            run.Access,
            options => options.WithStrictOrdering());
        revision.Snapshot.SnapshotHash.Should().NotBe(run.Snapshot.SnapshotHash);
        revision.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);

        revision = await restarted.ValidateAsync(revision.RunId, revision.Version, approver);
        revision = await restarted.SubmitAsync(revision.RunId, revision.Version, approver);
        Func<Task> makerApprovesOwnRevision = () => restarted.ApproveAsync(
            revision.RunId,
            revision.Version,
            "Maker must not approve the restatement revision",
            approver);
        await makerApprovesOwnRevision.Should().ThrowAsync<ReportingGovernanceAuthorizationException>()
            .WithMessage("*creator cannot approve*");
        revision = await restarted.ApproveAsync(
            revision.RunId,
            revision.Version,
            "Independent revision review complete",
            releaser);

        var revisionPackage = artifactCatalog.RequirePackage(
            revision.Scope.TenantId,
            ReportingArtifactPackageIdentity.Create(revision));
        var retainedManifest = revisionPackage.Artifacts.Single(record =>
            record.ArtifactId == record.ManifestId);
        artifactStore.CorruptOnRead(retainedManifest.Identity);
        Func<Task> releaseCorruptManifest = () => restarted.ReleaseAsync(
            revision.RunId,
            revision.Version,
            maker);
        await releaseCorruptManifest.Should().ThrowAsync<ReportingArtifactIntegrityException>();
        (await restarted.GetAsync(revision.RunId, maker)).GovernanceState
            .Should().Be(GovernedReportingState.Approved);
    }

    private static async Task<GovernedReportingRun> CreateValidatedRunAsync(
        Scenario scenario,
        DateTimeOffset? readinessEvaluatedAtUtc = null)
    {
        var run = await scenario.Service.CreateRunAsync(scenario.Request, scenario.Creator);
        run = await scenario.Service.BeginExecutionAsync(run.RunId, run.Version, scenario.Creator);
        run = await scenario.Service.CompleteExecutionAsync(run.RunId, run.Version, scenario.Creator);
        var readiness = BuildReadiness(
            run,
            readinessEvaluatedAtUtc ?? Now);
        return await scenario.Service.ValidateAsync(
            run.RunId,
            run.Version,
            readiness,
            scenario.Creator);
    }

    private static ReportingReadinessReceipt BuildReadiness(
        GovernedReportingRun run,
        DateTimeOffset evaluatedAtUtc)
    {
        var readiness = new ReportingReadinessReceipt(
            "readiness-1",
            string.Empty,
            run.RunId,
            run.Scope.TenantId,
            run.Scope.OrganizationId,
            run.Scope.CompanyId,
            run.Snapshot.SnapshotId,
            run.Snapshot.SnapshotHash,
            evaluatedAtUtc,
            [new ReportingReadinessCheck(
                "exact-source-and-close",
                true,
                ["evidence-source", "evidence-close"])]);
        readiness = readiness with
        {
            ReceiptHash = ReportingGovernanceCanonicalValidation.ComputeReadinessReceiptHash(readiness)
        };
        return readiness;
    }

    private static async Task<GovernedReportingRun> CreateReleasedRunAsync(Scenario scenario)
    {
        var run = await CreateValidatedRunAsync(scenario);
        run = await scenario.Service.SubmitAsync(run.RunId, run.Version, scenario.Creator);
        run = await scenario.Service.ApproveAsync(
            run.RunId,
            run.Version,
            "Reviewed independently",
            scenario.Approver);
        return await scenario.Service.ReleaseAsync(
            run.RunId,
            run.Version,
            new ReportingReleaseEvidence(
                "manifest-1",
                new string('4', 64),
                [new ReportingArtifactReference("artifact-1", new string('3', 64), 128)],
                ["evidence-release"]),
            scenario.Releaser);
    }

    private static Scenario NewScenario(
        DateTimeOffset? snapshotCapturedAtUtc = null,
        ReportingFinalityDto finality = ReportingFinalityDto.Final)
    {
        var scope = new ReportingOperationalScope(
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-a",
            "book-a",
            "period-a");
        var parametersJson = CanonicalParametersJson(scope, finality);
        var parametersHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(parametersJson)))
            .ToLowerInvariant();
        var request = new ReportingRunCreationRequest(
            "run-1",
            "series-1",
            "template-1",
            "1",
            scope,
            new ReportingAccessScope(
                "policy-1",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: [],
                PolicyHash: new string('a', 64)),
            new ReportingCertifiedSnapshotScope(
                scope.TenantId,
                scope.OrganizationId,
                scope.CompanyId,
                scope.FundId,
                scope.BookId,
                scope.PeriodId,
                "snapshot-1",
                new string('b', 64),
                "reconciliation-1",
                snapshotCapturedAtUtc ?? Now.AddMinutes(-1),
                SourceCheckpointId: "source-1",
                SourceCheckpointHash: new string('c', 64),
                ReconciliationCheckpointHash: new string('d', 64),
                ParametersCanonicalJson: parametersJson,
                ParametersHash: parametersHash));
        var repository = new MemoryGovernanceRepository();
        var service = new ReportingGovernanceService(
            repository,
            new FixedTimeProvider(Now),
            prefix => prefix switch
            {
                "restatement" => "restatement-1",
                "report-audit" => $"audit-{Guid.NewGuid():N}",
                _ => $"{prefix}-{Guid.NewGuid():N}"
            });
        return new Scenario(
            request,
            service,
            Authority("creator", scope),
            Authority("approver", scope),
            Authority("releaser", scope),
            Authority("restatement-approver", scope));
    }

    private static ReportingAuthorityScope Authority(
        string actor,
        ReportingOperationalScope scope) =>
        new(
            actor,
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            Enum.GetValues<ReportingGovernancePermission>().ToImmutableArray(),
            ReportingCommandOrigin.HumanOperator,
            $"correlation-{actor}",
            [actor]);

    private static string CanonicalParametersJson(
        ReportingOperationalScope scope,
        ReportingFinalityDto finality) =>
        JsonSerializer.Serialize(new
        {
            scope = new
            {
                fundProfileId = scope.FundId,
                entityScopeKind = ReportingEntityScopeKindDto.AllEntities.ToString(),
                entityId = (string?)null,
                portfolioId = (string?)null,
                investorId = (string?)null,
                dimensions = (object?)null
            },
            periodId = scope.PeriodId,
            asOfDate = "2026-07-15",
            ledgerBookId = (string?)null,
            ledgerBookCode = scope.BookId,
            accountingBasis = ReportingAccountingBasisDto.Gaap.ToString(),
            presentationCurrency = "USD",
            consolidationLevel = ReportingConsolidationLevelDto.Fund.ToString(),
            outputFormat = ReportingOutputFormatDto.Pdf.ToString(),
            finality = finality.ToString(),
            includeSupportingSchedules = true,
            includeEvidenceAppendix = finality == ReportingFinalityDto.Final,
            templateParameters = new Dictionary<string, string>()
        });

    private static ReportingGovernanceCoordinatorService CreateCoordinator(
        MemoryGovernanceRepository repository,
        ReportingRunCertificationService certification,
        IReportingOrchestrationService orchestration,
        RestartableArtifactStore artifactStore,
        RestartableArtifactCatalog artifactCatalog,
        RestartableArtifactAuditStore artifactAudit,
        IReportingCertifiedArtifactProducer producer,
        ReportingTemplateMetadata template) =>
        new(
            new ReportingGovernanceService(
                repository,
                new FixedTimeProvider(Now),
                prefix => $"{prefix}-{Guid.NewGuid():N}"),
            repository,
            certification,
            orchestration,
            new ReportingArtifactVaultService(
                artifactStore,
                artifactCatalog,
                artifactAudit,
                new FixedTimeProvider(Now)),
            producer,
            new ReportingArtifactRetentionAuthorityProvider(),
            new GovernedReportingRestatementChangedLineResolver(),
            new CoordinatorRestatementCertificationInputProvider(template));

    private static ReportingTemplateMetadata CoordinatorTemplate() => new(
        "coordinator-report",
        ReportingTemplateFamily.CustomReport,
        "Coordinator report",
        "1.0.0",
        ["detail"],
        ImmutableDictionary<string, string>.Empty,
        ReportWriterGrids: [],
        AccessPolicy: new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            Principals:
            [
                new ReportAccessPrincipalDto(
                    ReportAccessPrincipalKindDto.Group,
                    "report-reviewers",
                    "Report reviewers")
            ],
            CompanyId: "company-a"));

    private static ReportingRunReadinessDto CoordinatorReadiness(string evaluationId) => new(
        evaluationId,
        Now.AddMinutes(-20),
        new VersionedReportTemplateIdDto("coordinator-report", 1),
        CoordinatorParameters(),
        ReportingRunReadinessStatusDto.Ready,
        CanGenerateDraft: true,
        CanGenerateFinal: true,
        Checks:
        [
            new ReportingRunReadinessCheckDto(
                "source",
                "Source",
                ReportingRunReadinessStatusDto.Ready,
                "Source is ready.",
                0,
                BlocksDraft: false,
                BlocksFinal: false,
                EvidenceReferences: ["source:coordinator"])
        ],
        BlockingReasons: [],
        EvidenceHash: new string('a', 64));

    private static ReportingRunParametersDto CoordinatorParameters() => new(
        new ReportingRunScopeDto("fund-a"),
        "2026-06",
        new DateOnly(2026, 6, 30),
        new ReportingLedgerBookSelectionDto(VersionedCoordinatorSource.BookId),
        ReportingAccountingBasisDto.Gaap,
        "USD",
        ReportingConsolidationLevelDto.Fund,
        ReportingOutputFormatDto.Pdf,
        ReportingFinalityDto.Final,
        IncludeSupportingSchedules: true,
        IncludeEvidenceAppendix: true);

    private static ReportAccessQueryContext CoordinatorAccess(string actorId) => new(
        actorId,
        ["report-reviewers"],
        "company-a",
        TenantId: "tenant-a",
        RequireBoundScope: true);

    private static ReportingGovernanceCallerContext CoordinatorCaller(string actorId) => new(
        actorId,
        "tenant-a",
        "company-a",
        UserPermission.AdminMaintenance,
        ReportingCommandOrigin.HumanOperator,
        $"correlation-{actorId}",
        [actorId, "report-reviewers"]);

    private static ReportingJobContract CoordinatorJob(
        string jobId,
        ReportingTemplateMetadata template,
        CertifiedReportingRunContext certified) =>
        new(
            jobId,
            template.TemplateId,
            certified.Readiness.ResolvedParameters.AsOfDate,
            ReportingRunTrigger.AdHoc,
            MaxRetries: 0,
            RequestedBy: "maker-a",
            RequestedAtUtc: Now,
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

    private static string HashBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] SerializeRetainedManifest(ReportingRetainedManifestDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(
            document,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static ReportingAuthoritativeSourceCheckpoint NewSourceCheckpoint() =>
        new(
            "durable-ledger-journal",
            "ledger:book-a:period-a",
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-a",
            "book-a",
            "period-a",
            "Gaap",
            new DateOnly(2026, 7, 15),
            Now,
            1,
            1,
            2,
            "source-1",
            new string('1', 64),
            Now,
            ["evidence-source"]);

    private sealed record Scenario(
        ReportingRunCreationRequest Request,
        ReportingGovernanceService Service,
        ReportingAuthorityScope Creator,
        ReportingAuthorityScope Approver,
        ReportingAuthorityScope Releaser,
        ReportingAuthorityScope RestatementApprover);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SingleTemplateCatalog(ReportingTemplateMetadata template) :
        IReportingTemplateCatalog
    {
        public ReportingTemplateMetadata Get(string templateId) =>
            string.Equals(templateId, template.TemplateId, StringComparison.Ordinal)
                ? template
                : throw new KeyNotFoundException(templateId);

        public IReadOnlyList<ReportingTemplateMetadata> ListTemplates() => [template];
    }

    private sealed class VersionedCoordinatorSource : IReportingAuthoritativeSource
    {
        public static readonly Guid BookId =
            Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid PeriodId =
            Guid.Parse("44444444-4444-4444-4444-444444444444");

        public int Version { get; set; } = 1;

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkpointHash = new string(Version == 1 ? 'b' : 'e', 64);
            var checkpointId = $"ledger-checkpoint-{Version}";
            var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = "Cash",
                    ["amount"] = Version == 1 ? "100.00" : "101.00",
                    ["entryId"] = "11111111-1111-1111-1111-111111111111"
                });
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{BookId:D}:{PeriodId:D}",
                "tenant-a",
                "organization-a",
                "company-a",
                "fund-a",
                BookId.ToString("D"),
                PeriodId.ToString("D"),
                "Gaap",
                parameters.AsOfDate,
                new DateTimeOffset(
                    parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                HighestGlobalSequence: Version,
                JournalEntryCount: 1,
                LedgerLineCount: rows.Length,
                checkpointId,
                checkpointHash,
                Now.AddMinutes(-10 + Version),
                [$"reporting-source-checkpoint:{checkpointId}:{checkpointHash}"]);
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, rows));
        }
    }

    private sealed class CoordinatorReconciliationSource : IReportingReconciliationEvidenceSource
    {
        public bool CanonicalQueueReopened { get; set; }

        public ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
            ReportingRunParametersDto parameters,
            ReportingAuthoritativeSourceCheckpoint source,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CanonicalQueueReopened)
            {
                throw new ReportingReconciliationEvidenceInvalidException(
                    "The canonical reconciliation queue reopened after certification; re-run reconciliation and close before final release.");
            }
            return ValueTask.FromResult(ReportingReconciliationEvidenceValidation.CreateReceipt(
                source,
                new ReportingReconciliationCompletionEvidence(
                    $"hard-close-{source.CheckpointId}",
                    new string('c', 64),
                    source.CapturedAtUtc,
                    HasOpenBreaks: false,
                    [$"close:{source.CheckpointId}"])));
        }
    }

    private sealed class CoordinatorRestatementCertificationInputProvider(
        ReportingTemplateMetadata template) : IReportingRestatementCertificationInputProvider
    {
        public ValueTask<ReportingRestatementCertificationInput> ResolveAsync(
            ReportingRestatementRequest request,
            GovernedReportingRun releasedPredecessor,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ReportingRestatementCertificationInput(
                template,
                CoordinatorReadiness($"evaluation-{request.RequestId}"),
                CoordinatorAccess(caller.ActorId)));
        }
    }

    private sealed class RestartableArtifactStore(DateTimeOffset storedAtUtc) :
        IReportingArtifactStore
    {
        private readonly Dictionary<ReportingArtifactIdentity, byte[]> _content = [];
        private readonly HashSet<ReportingArtifactIdentity> _corruptReads = [];

        public bool FailWrites { get; set; }
        public bool CorruptAllReads { get; set; }
        public int StoredArtifactCount => _content.Count;

        public Task<ReportingArtifactWriteResult> StoreAsync(
            ReportingArtifactWriteRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailWrites)
            {
                throw new IOException("Injected artifact-retention failure.");
            }
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
            {
                throw new ReportingArtifactNotFoundException(identity);
            }

            var bytes = retained.ToArray();
            if (CorruptAllReads || _corruptReads.Contains(identity))
            {
                bytes[0] ^= 0xff;
            }
            return Task.FromResult(new ReportingArtifactReadResult(
                identity,
                bytes.LongLength,
                storedAtUtc,
                bytes));
        }

        public void CorruptOnRead(ReportingArtifactIdentity identity) =>
            _corruptReads.Add(identity);
    }

    private sealed class RestartableArtifactCatalog : IReportingArtifactCatalog
    {
        private readonly Dictionary<(string TenantId, string PackageId), ReportingRetainedArtifactPackage>
            _packages = [];

        public bool LoseCatalogReads { get; set; }

        public ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
            ReportingRetainedArtifactPackage package,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tenantId = package.Artifacts.Select(static artifact => artifact.Scope.TenantId)
                .Distinct(StringComparer.Ordinal)
                .Single();
            var key = (tenantId, package.PackageId);
            if (_packages.TryGetValue(key, out var existing))
            {
                if (!string.Equals(
                        JsonSerializer.Serialize(existing),
                        JsonSerializer.Serialize(package),
                        StringComparison.Ordinal))
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        "Attempted to replace immutable package metadata.");
                }
                return ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(true));
            }

            _packages.Add(key, DurableRoundTrip(package));
            return ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(false));
        }

        public ValueTask<ReportingRetainedArtifactPackage?> GetPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReportingRetainedArtifactPackage?>(
                !LoseCatalogReads && _packages.TryGetValue((tenantId, packageId), out var package)
                    ? DurableRoundTrip(package)
                    : null);

        public ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
            string tenantId,
            string packageId,
            string artifactId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReportingRetainedArtifactRecord?>(
                !LoseCatalogReads && _packages.TryGetValue((tenantId, packageId), out var package)
                    ? package.Artifacts
                        .Where(artifact => artifact.ArtifactId == artifactId)
                        .Select(DurableRoundTrip)
                        .FirstOrDefault()
                    : null);

        public ReportingRetainedArtifactPackage RequirePackage(string tenantId, string packageId) =>
            _packages.TryGetValue((tenantId, packageId), out var package)
                ? DurableRoundTrip(package)
                : throw new KeyNotFoundException(packageId);

        private static T DurableRoundTrip<T>(T value) =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
            ?? throw new InvalidDataException($"Durable artifact catalog round-trip produced null {typeof(T).Name}.");
    }

    private sealed class FixedManifestOrchestration(ReportingOutputManifest manifest) :
        IReportingOrchestrationService
    {
        public Task<ReportingOutputManifest> ExecuteAsync(
            ReportingJobContract contract,
            CancellationToken cancellationToken) => Task.FromResult(manifest);

        public Task<IReadOnlyList<ReportingOutputManifest>> ExecuteDueSchedulesAsync(
            IEnumerable<ReportingScheduleContract> schedules,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportingOutputManifest>>([]);

        public ReportingOutputManifest? GetManifest(string runId) =>
            string.Equals(runId, manifest.RunId, StringComparison.Ordinal) ? manifest : null;

        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId) => [];

        public Task<bool> TransitionApprovalAsync(
            string runId,
            ReportingRunStatus target,
            string actor,
            string role,
            string notes,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class MalformedManifestOnceProducer : IReportingCertifiedArtifactProducer
    {
        private readonly DeterministicReportingCertifiedArtifactProducer _inner = new();
        private int _remainingMalformedAttempts = 1;

        public async ValueTask<ReportingGovernedArtifactProduction> ProduceAsync(
            ReportingOutputManifest manifest,
            CancellationToken cancellationToken = default)
        {
            var production = await _inner.ProduceAsync(manifest, cancellationToken);
            if (Interlocked.Exchange(ref _remainingMalformedAttempts, 0) == 0)
            {
                return production;
            }

            return production with
            {
                Artifacts = production.Artifacts
                    .Select(artifact => string.Equals(
                        artifact.ArtifactId,
                        production.ManifestArtifactId,
                        StringComparison.Ordinal)
                            ? artifact with { Content = Encoding.UTF8.GetBytes("{}") }
                            : artifact)
                    .ToImmutableArray()
            };
        }
    }

    private sealed class CanonicalDescriptorDriftOnceProducer : IReportingCertifiedArtifactProducer
    {
        private readonly DeterministicReportingCertifiedArtifactProducer _inner = new();
        private int _remainingDriftedAttempts = 1;

        public async ValueTask<ReportingGovernedArtifactProduction> ProduceAsync(
            ReportingOutputManifest manifest,
            CancellationToken cancellationToken = default)
        {
            var production = await _inner.ProduceAsync(manifest, cancellationToken);
            if (Interlocked.Exchange(ref _remainingDriftedAttempts, 0) == 0)
            {
                return production;
            }

            var primary = ReportingArtifactDeclaration.Build(manifest)
                .Single(static artifact => artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput);
            var nonCanonicalFileName = $"renamed-{primary.FileName}";
            const string nonCanonicalContentType = "application/octet-stream";
            var renderedManifest = production.Artifacts.Single(artifact => string.Equals(
                artifact.ArtifactId,
                production.ManifestArtifactId,
                StringComparison.Ordinal));
            var retainedDocument = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(
                renderedManifest.Content.Span);
            var driftedDocument = retainedDocument with
            {
                Artifacts = retainedDocument.Artifacts
                    .Select(descriptor => string.Equals(
                        descriptor.ArtifactId,
                        primary.ArtifactId,
                        StringComparison.Ordinal)
                            ? descriptor with
                            {
                                FileName = nonCanonicalFileName,
                                ContentType = nonCanonicalContentType
                            }
                            : descriptor)
                    .ToImmutableArray()
            };
            var driftedManifestBytes = SerializeRetainedManifest(driftedDocument);
            return production with
            {
                Artifacts = production.Artifacts
                    .Select(artifact => string.Equals(
                        artifact.ArtifactId,
                        primary.ArtifactId,
                        StringComparison.Ordinal)
                            ? artifact with
                            {
                                FileName = nonCanonicalFileName,
                                ContentType = nonCanonicalContentType
                            }
                            : string.Equals(
                                artifact.ArtifactId,
                                production.ManifestArtifactId,
                                StringComparison.Ordinal)
                                ? artifact with { Content = driftedManifestBytes }
                                : artifact)
                    .ToImmutableArray()
            };
        }
    }

    private sealed class CanonicalSectionDriftOnceProducer : IReportingCertifiedArtifactProducer
    {
        private readonly DeterministicReportingCertifiedArtifactProducer _inner = new();
        private int _remainingDriftedAttempts = 1;

        public async ValueTask<ReportingGovernedArtifactProduction> ProduceAsync(
            ReportingOutputManifest manifest,
            CancellationToken cancellationToken = default)
        {
            var production = await _inner.ProduceAsync(manifest, cancellationToken);
            if (Interlocked.Exchange(ref _remainingDriftedAttempts, 0) == 0)
            {
                return production;
            }

            var renderedManifest = production.Artifacts.Single(artifact => string.Equals(
                artifact.ArtifactId,
                production.ManifestArtifactId,
                StringComparison.Ordinal));
            var retainedDocument = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(
                renderedManifest.Content.Span);
            var driftedDocument = retainedDocument with
            {
                Sections = retainedDocument.Sections.Skip(1).ToImmutableArray()
            };
            var driftedManifestBytes = SerializeRetainedManifest(driftedDocument);
            return production with
            {
                Artifacts = production.Artifacts
                    .Select(artifact => string.Equals(
                        artifact.ArtifactId,
                        production.ManifestArtifactId,
                        StringComparison.Ordinal)
                            ? artifact with { Content = driftedManifestBytes }
                            : artifact)
                    .ToImmutableArray()
            };
        }
    }

    private sealed class RestartableArtifactAuditStore : IReportingArtifactAuditStore
    {
        private readonly List<ReportingArtifactAuditReceipt> _receipts = [];

        public ValueTask<ReportingArtifactAuditReceipt> AppendAsync(
            ReportingArtifactAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previousHash = _receipts.LastOrDefault()?.Hash;
            var hash = HashBytes(Encoding.UTF8.GetBytes(string.Join(
                '|',
                previousHash,
                auditEvent.EventId,
                auditEvent.OccurredAtUtc.ToString("O"),
                auditEvent.Action,
                auditEvent.ActorId,
                auditEvent.ActorTenantId,
                auditEvent.TargetTenantId,
                auditEvent.PackageId,
                auditEvent.ArtifactId,
                auditEvent.ContentHashSha256,
                auditEvent.CorrelationId,
                auditEvent.Reason)));
            var receipt = new ReportingArtifactAuditReceipt(
                auditEvent.EventId,
                _receipts.Count + 1,
                previousHash,
                hash);
            _receipts.Add(receipt);
            return ValueTask.FromResult(receipt);
        }
    }

    private sealed class MemoryGovernanceRepository :
        IReportingGovernanceRepository,
        IReportingGovernanceTransaction
    {
        private readonly Dictionary<(string TenantId, string RunId), GovernedReportingRun> _runs = [];
        private readonly Dictionary<(string TenantId, string RequestId), ReportingRestatementRequest> _requests = [];

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
            ValueTask.FromResult<IReadOnlyList<GovernedReportingRun>>(_runs.Values
                .Where(run => run.Scope.TenantId == tenantId && run.SeriesId == seriesId)
                .OrderBy(run => run.Revision)
                .ToArray());

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
            _runs[(run.Scope.TenantId, run.RunId)] = run;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ReportingRestatementRequest?> GetRestatementRequestAsync(
            string tenantId,
            string requestId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_requests.GetValueOrDefault((tenantId, requestId)));

        public ValueTask<IReadOnlyList<ReportingRestatementRequest>> ListRestatementRequestsBySeriesAsync(
            string tenantId,
            string seriesId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ReportingRestatementRequest>>(_requests.Values
                .Where(request => request.RequestedBy.TenantId == tenantId && request.SeriesId == seriesId)
                .ToArray());

        public ValueTask AddRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            CancellationToken cancellationToken = default)
        {
            _requests.Add((tenantId, request.RequestId), request);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            _requests[(tenantId, request.RequestId)] = request;
            return ValueTask.CompletedTask;
        }
    }
}
