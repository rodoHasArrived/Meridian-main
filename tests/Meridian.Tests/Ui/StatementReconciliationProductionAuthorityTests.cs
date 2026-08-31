using System.Security.Cryptography;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Reporting;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class StatementReconciliationProductionAuthorityTests : IDisposable
{
    private static readonly Guid OperationsWorkflowId =
        Guid.Parse("35995e0d-4726-41c2-9b18-275129868c6a");
    private static readonly StatementAccountingScope AccountingScope = new(
        "fund-profile-alpha",
        Guid.Parse("5dca0d66-b576-44d9-a24a-9f2651bf163c"),
        Guid.Parse("44a5b4ac-7068-4775-8978-d76138a9428e"),
        new DateOnly(2026, 6, 30));
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-production-authority-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddEvidenceWorkflowFabric_ProductionNonDurableOverride_OmitsWorkflowRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStatementReconciliationReportAuthorityStore>(
            new FileStatementReconciliationReportAuthorityStore(_root));

        services.AddEvidenceWorkflowFabric(isProductionComposition: true);

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(StatementReconciliationReportWorkflowService));
        using var provider = services.BuildServiceProvider();
        provider.GetService<StatementReconciliationReportWorkflowService>().Should().BeNull();
    }

    [Fact]
    public async Task FileAuthority_FileLink_IsRejectedByReadsWritesAndListingWithoutTouchingTarget()
    {
        var authority = new FileStatementReconciliationReportAuthorityStore(_root);
        var scope = AuthorityScope();
        await authority.WriteDocumentAsync(
            scope,
            "workflow.json",
            "{}"u8.ToArray(),
            isImmutable: false);
        var workflowDirectory = Path.Combine(
            _root,
            "reporting",
            "statement-reconciliation-report",
            scope.WorkflowId);
        var evidenceDirectory = Path.Combine(workflowDirectory, "evidence");
        var linkPath = Path.Combine(evidenceDirectory, "linked-source.csv");
        var externalFile = _root + "-file-authority-external.csv";
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(externalFile, "external,statement\n");
        if (!TryCreateFileLink(linkPath, externalFile))
        {
            File.Delete(externalFile);
            return;
        }

        try
        {
            var exists = () => authority.DocumentExistsAsync(
                    scope,
                    "evidence/linked-source.csv")
                .AsTask();
            var read = () => authority.TryReadDocumentAsync(
                    scope,
                    "evidence/linked-source.csv")
                .AsTask();
            var write = () => authority.WriteDocumentAsync(
                    scope,
                    "evidence/linked-source.csv",
                    "replacement"u8.ToArray(),
                    isImmutable: true)
                .AsTask();
            var list = () => authority.ListDocumentKeysAsync(scope, string.Empty).AsTask();

            await exists.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*symbolic link or reparse point*");
            await read.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*symbolic link or reparse point*");
            await write.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*symbolic link or reparse point*");
            await list.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*symbolic link or reparse point*");
            (await File.ReadAllTextAsync(externalFile)).Should().Be("external,statement\n");
        }
        finally
        {
            File.Delete(linkPath);
            File.Delete(externalFile);
        }
    }

    [Fact]
    public async Task ResumeAsync_DurableRestart_RemovesUncheckpointedWorkspaceOrphanBeforeRepersisting()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var firstRuns = new FakeStatementRuns();
        var first = CreateWorkflow(authority, firstRuns);
        var completed = await first.StartAsync(BuildCommand());
        completed.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.Completed);

        var workflowDirectory = Path.Combine(
            _root,
            "runtime",
            "statement-reconciliation-authority-workspace",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "statement-reconciliation-report",
            completed.Workflow.WorkflowId);
        var orphanPath = Path.Combine(workflowDirectory, "artifacts", "orphan-after-crash.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(orphanPath)!);
        await File.WriteAllBytesAsync(orphanPath, "must-not-be-authority"u8.ToArray());

        var openCase = new ReconciliationCase(
            "case-open",
            "statement-run-alpha",
            "Open",
            "Reconciliation changed after the first report.",
            1m,
            "Current reconciliation state.",
            DateTimeOffset.Parse("2026-07-27T12:00:00Z"),
            []);
        var restarted = CreateWorkflow(
            authority,
            new FakeStatementRuns { Cases = [openCase] });

        var resumed = await restarted.ResumeAsync(
            completed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        resumed.Should().NotBeNull();
        resumed!.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        File.Exists(orphanPath).Should().BeFalse(
            "durable hydration must reconstruct an exact cache before any continuation");
        authority.ListKeys(
                new StatementReconciliationReportAuthorityScope(
                    "tenant-alpha",
                    "company-alpha",
                    completed.Workflow.WorkflowId))
            .Should().NotContain("artifacts/orphan-after-crash.bin");
        authority.WriteOrder.Last().DocumentKey.Should().Be("workflow.json",
            "the snapshot mapping must be the final authoritative checkpoint write");
    }

    [Fact]
    public async Task ResumeAsync_WorkspaceDirectoryLink_IsRejectedWithoutTouchingExternalTarget()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var completed = await CreateWorkflow(authority, new FakeStatementRuns())
            .StartAsync(BuildCommand());
        var workflowDirectory = ResolveDurableWorkspace(completed.Workflow.WorkflowId);
        var externalDirectory = _root + "-external";
        var sentinel = Path.Combine(externalDirectory, "sentinel.txt");
        var linkPath = Path.Combine(workflowDirectory, "uncheckpointed-link");
        Directory.CreateDirectory(externalDirectory);
        await File.WriteAllTextAsync(sentinel, "external-authority");
        if (!TryCreateDirectoryLink(linkPath, externalDirectory))
        {
            Directory.Delete(externalDirectory, recursive: true);
            return;
        }

        try
        {
            var restarted = CreateWorkflow(authority, new FakeStatementRuns());

            var act = () => restarted.ResumeAsync(
                completed.Workflow.WorkflowId,
                "tenant-alpha",
                "company-alpha");

            await act.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*symbolic link or reparse point*");
            File.Exists(sentinel).Should().BeTrue();
            (await File.ReadAllTextAsync(sentinel)).Should().Be("external-authority");
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }

            Directory.Delete(externalDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ResumeAsync_WindowsCaseCollidingAuthorityKeys_AreRejectedBeforeHydration()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var authority = new InMemoryDurableStatementAuthority();
        var completed = await CreateWorkflow(authority, new FakeStatementRuns())
            .StartAsync(BuildCommand());
        var scope = new StatementReconciliationReportAuthorityScope(
            "tenant-alpha",
            "company-alpha",
            completed.Workflow.WorkflowId);
        await authority.WriteDocumentAsync(
            scope,
            "Workflow.json",
            """{"collision":true}"""u8.ToArray(),
            isImmutable: false);
        var restarted = CreateWorkflow(authority, new FakeStatementRuns());

        var act = () => restarted.ResumeAsync(
            completed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*collide in the local workspace*");
    }

    [Fact]
    public async Task AuthorityDocuments_AreNotVisibleAcrossTenantOrCompanyScope()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var scope = new StatementReconciliationReportAuthorityScope(
            "tenant-alpha",
            "company-alpha",
            "statement-reconciliation-report-0123456789abcdef0123456789abcdef");
        await authority.WriteDocumentAsync(
            scope,
            "workflow.json",
            "{}"u8.ToArray(),
            isImmutable: false);

        var otherTenant = scope with { TenantId = "tenant-beta" };
        var otherCompany = scope with { CompanyId = "company-beta" };

        (await authority.TryReadDocumentAsync(otherTenant, "workflow.json")).Should().BeNull();
        (await authority.TryReadDocumentAsync(otherCompany, "workflow.json")).Should().BeNull();
        (await authority.ListDocumentKeysAsync(otherTenant, string.Empty)).Should().BeEmpty();
        (await authority.ListDocumentKeysAsync(otherCompany, string.Empty)).Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceRetainer_LegacyFileIdentity_MigratesSourceIntoDurableAuthority()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var source = WriteRetainedSource();
        var legacy = BuildImportResult() with
        {
            EvidenceVaultIdentity = new EvidenceVaultIdentityDto(
                "legacy-vault",
                "statement-run",
                "statement-run-alpha",
                "evidence/legacy/manifest.json",
                "/accounting/evidence",
                DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
                new string('a', 64),
                1,
                "File")
        };

        var retained = await new ReportingStatementImportEvidenceRetainer(authority, _root)
            .RetainAsync(legacy with { RetainedSourcePath = source }, BuildEvidenceRequest());

        retained.EvidenceVaultIdentity.Should().NotBeNull();
        retained.EvidenceVaultIdentity!.StorageKind.Should().Be(authority.StorageKind);
        retained.EvidenceVaultIdentity.VaultId.Should().NotBe("legacy-vault");
        retained.EvidenceVaultIdentity.Artifacts.Select(static item => item.Kind).Should().BeEquivalentTo(
            ["statement-source", "statement-canonical", "statement-run-evidence"]);
        authority.ListKeys(AuthorityScope()).Should().HaveCount(4);
    }

    [Fact]
    public async Task EvidenceRetainer_ProductionProjection_RetainsQueryableStatementDocumentWithReviewAndLinks()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var evidenceStore = new FileEvidenceArtifactStore(
            _root,
            NullLogger<FileEvidenceArtifactStore>.Instance);
        var source = WriteRetainedSource();
        var result = BuildImportResult() with
        {
            RetainedSourcePath = source,
            BreakCount = 1,
            CaseCount = 1,
            BreakIds = ["break-alpha"],
            CaseIds = ["case:break-alpha"],
            ReconciliationCaseLinks =
            [
                new StatementImportReconciliationCaseLinkDto(
                    "case:break-alpha",
                    "break-alpha",
                    "/accounting/reconciliation/match?runId=statement-run-alpha&caseId=case%3Abreak-alpha&breakId=break-alpha",
                    "Reconciliation case break-alpha",
                    "Open",
                    "High",
                    "Statement import created a reconciliation break.",
                    "Review the linked statement evidence.")
            ]
        };

        var retained = await new ReportingStatementImportEvidenceRetainer(
                authority,
                _root,
                evidenceStore)
            .RetainAsync(result, BuildEvidenceRequest());

        retained.EvidenceWorkbenchRoute.Should().Be(
            "/reporting/evidence?subjectKind=statement-run&subjectId=statement-run-alpha&documentClassification=Statement");
        retained.EvidenceVaultIdentity.Should().NotBeNull();
        retained.EvidenceVaultIdentity!.StorageKind.Should().Be(authority.StorageKind,
            "the Reporting authority remains canonical while Evidence Workbench is a projection");

        var entries = await evidenceStore.ListDocumentsAsync(
            new EvidenceVaultDocumentQueryDto(
                Classification: EvidenceDocumentClassificationDto.Statement,
                ChannelKind: EvidenceDocumentIntakeChannelDto.ImportedFileReference,
                SubjectKind: "statement-run",
                SubjectId: result.RunId,
                TenantId: "tenant-alpha",
                Scope: "company-alpha"));
        var document = entries.Should().ContainSingle().Subject.Document;
        var authoritySource = retained.EvidenceVaultIdentity.Artifacts.Single(artifact =>
            artifact.Kind == "statement-source");
        document.SourceHashSha256.Should().Be(authoritySource.ContentHashSha256);
        document.SourceRecord.Should().NotBeNull();
        document.SourceRecord!.SourceHashSha256.Should().Be(authoritySource.ContentHashSha256);
        document.SourceRecord.ReceiptHash.Should().Be(authoritySource.ContentHashSha256);
        document.SourceRecord.SourceReference.Should().Be(result.RetainedSourcePath);
        document.SourceRecord.SourceSystem.Should().Be("Broker Alpha");
        document.SourceRecord.Actor.Should().Be("operator-alpha");
        document.SourceRecord.TenantId.Should().Be("tenant-alpha");
        document.SourceRecord.Scope.Should().Be("company-alpha");
        document.ExtractionStatus.Should().Be(EvidenceExtractionStatusDto.NeedsReview);
        document.ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.NeedsReview);
        document.AuditTrail.Should().ContainSingle(audit =>
            audit.Action == "DocumentIntakeRetained"
            && audit.Actor == "operator-alpha");
        document.ExtractedFields.Should().Contain(field =>
            field.FieldName == "recordCount"
            && field.ExtractedValue == "1");
        document.ExtractedFields.Should().Contain(field =>
            field.FieldName == "breakCount"
            && field.ExtractedValue == "1"
            && field.ValidationStatus == EvidenceStatusDto.ReviewRequired);
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.StatementRun
            && link.ObjectId == result.RunId);
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Account
            && link.ObjectId == "fund-account-alpha");
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Period
            && link.ObjectId == "2026-06-01..2026-06-30");
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.StatementImport
            && link.ObjectId == result.RunId);
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.ReconciliationCase
            && link.ObjectId == "case:break-alpha");
    }

    [Theory]
    [InlineData(EvidenceDocumentReviewStatusDto.Accepted)]
    [InlineData(EvidenceDocumentReviewStatusDto.Rejected)]
    public async Task EvidenceRetainer_RetryAfterOperatorReview_ReusesProjectionAndPreservesReviewState(
        EvidenceDocumentReviewStatusDto reviewStatus)
    {
        var authority = new InMemoryDurableStatementAuthority();
        var evidenceStore = new FileEvidenceArtifactStore(
            _root,
            NullLogger<FileEvidenceArtifactStore>.Instance);
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root, evidenceStore);
        var result = BuildImportResult() with { RetainedSourcePath = WriteRetainedSource() };
        var request = BuildEvidenceRequest();
        var retained = await retainer.RetainAsync(result, request);
        var query = new EvidenceVaultDocumentQueryDto(
            Classification: EvidenceDocumentClassificationDto.Statement,
            ChannelKind: EvidenceDocumentIntakeChannelDto.ImportedFileReference,
            SubjectKind: "statement-run",
            SubjectId: result.RunId,
            TenantId: "tenant-alpha",
            Scope: "company-alpha");
        var originalEntry = (await evidenceStore.ListDocumentsAsync(query))
            .Should().ContainSingle().Subject;
        IReadOnlyList<EvidenceDocumentConfirmedFieldDto> confirmedFields =
            reviewStatus == EvidenceDocumentReviewStatusDto.Accepted
                ? [
                    new EvidenceDocumentConfirmedFieldDto(
                        "recordCount",
                        "1",
                        "controller-alpha",
                        default,
                        "recordCount",
                        "Confirmed against the authority-retained statement source.")
                ]
                : [];
        var reviewRequest = new EvidenceVaultDocumentReviewRequestDto(
            reviewStatus,
            "controller-alpha",
            "Statement projection reviewed before workflow resume.",
            CorrelationId: $"review-{reviewStatus.ToString().ToLowerInvariant()}")
        {
            ConfirmedFields = confirmedFields
        };
        var review = await evidenceStore.ReviewDocumentAsync(
            originalEntry.VaultId,
            originalEntry.Document.DocumentId,
            "tenant-alpha",
            "company-alpha",
            reviewRequest);
        review.Should().NotBeNull();

        var retried = await retainer.RetainAsync(retained, request);

        var entries = await evidenceStore.ListDocumentsAsync(query);
        var entry = entries.Should().ContainSingle().Subject;
        entry.VaultId.Should().Be(originalEntry.VaultId);
        entry.Document.ReviewerState.Status.Should().Be(reviewStatus);
        entry.Document.ReviewerState.Reviewer.Should().Be("controller-alpha");
        entry.Document.ExtractionStatus.Should().Be(
            reviewStatus == EvidenceDocumentReviewStatusDto.Accepted
                ? EvidenceExtractionStatusDto.Accepted
                : EvidenceExtractionStatusDto.Rejected);
        entry.Document.AuditTrail.Should().ContainSingle(audit =>
            audit.Action == "DocumentReviewRecorded"
            && audit.Actor == "controller-alpha");
        retried.EvidenceVaultIdentity!.VaultId.Should().Be(retained.EvidenceVaultIdentity!.VaultId);
    }

    [Fact]
    public async Task EvidenceRetainer_ConcurrentResumeAfterProjectionLoss_CreatesSingleProjection()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var evidenceStore = new FileEvidenceArtifactStore(
            _root,
            NullLogger<FileEvidenceArtifactStore>.Instance);
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root, evidenceStore);
        var result = BuildImportResult() with { RetainedSourcePath = WriteRetainedSource() };
        var request = BuildEvidenceRequest();
        var retained = await retainer.RetainAsync(result, request);
        var vaultDirectory = Path.Combine(_root, "workstation", "evidence", "_vault");
        Directory.Exists(vaultDirectory).Should().BeTrue();
        Directory.Delete(vaultDirectory, recursive: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var retries = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => retainer.RetainAsync(retained, request, cts.Token)));

        retries.Should().OnlyContain(retry =>
            retry.EvidenceVaultIdentity!.VaultId == retained.EvidenceVaultIdentity!.VaultId);
        var entries = await evidenceStore.ListDocumentsAsync(
            new EvidenceVaultDocumentQueryDto(
                Classification: EvidenceDocumentClassificationDto.Statement,
                ChannelKind: EvidenceDocumentIntakeChannelDto.ImportedFileReference,
                SubjectKind: "statement-run",
                SubjectId: result.RunId,
                TenantId: "tenant-alpha",
                Scope: "company-alpha"),
            cts.Token);
        entries.Should().ContainSingle();
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("artifact")]
    public async Task EvidenceRetainer_OrphanedProjectionIndex_RebuildsVerifiedWorkbenchContent(
        string corruptedPart)
    {
        var authority = new InMemoryDurableStatementAuthority();
        var evidenceStore = new FileEvidenceArtifactStore(
            _root,
            NullLogger<FileEvidenceArtifactStore>.Instance);
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root, evidenceStore);
        var retainedSourcePath = WriteRetainedSource();
        var result = BuildImportResult() with { RetainedSourcePath = retainedSourcePath };
        var request = BuildEvidenceRequest();
        var retained = await retainer.RetainAsync(result, request);
        var query = new EvidenceVaultDocumentQueryDto(
            Classification: EvidenceDocumentClassificationDto.Statement,
            ChannelKind: EvidenceDocumentIntakeChannelDto.ImportedFileReference,
            SubjectKind: "statement-run",
            SubjectId: result.RunId,
            TenantId: "tenant-alpha",
            Scope: "company-alpha");
        var originalEntry = (await evidenceStore.ListDocumentsAsync(query))
            .Should().ContainSingle().Subject;
        var originalIdentity = await evidenceStore.TryGetVaultIdentityAsync(
            originalEntry.VaultId,
            "tenant-alpha",
            "company-alpha");
        originalIdentity.Should().NotBeNull();
        var verifiedOriginalIdentity = originalIdentity!;
        var originalArtifact = verifiedOriginalIdentity.Artifacts.Should().ContainSingle(artifact =>
            artifact.Document != null
            && artifact.Document.DocumentId == originalEntry.Document.DocumentId).Subject;
        var manifestPath = Path.Combine(
            _root,
            verifiedOriginalIdentity.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var artifactPath = Path.Combine(
            _root,
            originalArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var indexPath = Path.Combine(
            _root,
            "workstation",
            "evidence",
            "_vault",
            $"{verifiedOriginalIdentity.VaultId}.json");

        if (corruptedPart == "manifest")
        {
            await File.WriteAllTextAsync(manifestPath, "{not-a-valid-manifest");
        }
        else
        {
            var corruptedBytes = await File.ReadAllBytesAsync(artifactPath);
            corruptedBytes[0] ^= 0xff;
            await File.WriteAllBytesAsync(artifactPath, corruptedBytes);
        }

        File.Exists(indexPath).Should().BeTrue("the stale index is the recovery scenario");
        File.Delete(Path.Combine(
            _root,
            retainedSourcePath.Replace('/', Path.DirectorySeparatorChar)));

        await retainer.RetainAsync(retained, request);

        var rebuiltEntry = (await evidenceStore.ListDocumentsAsync(query))
            .Should().ContainSingle().Subject;
        rebuiltEntry.VaultId.Should().NotBe(originalEntry.VaultId);
        rebuiltEntry.Document.SourceHashSha256.Should().Be(
            retained.EvidenceVaultIdentity!.Artifacts.Single(artifact =>
                artifact.Kind == "statement-source").ContentHashSha256);
        var rebuiltIdentity = await evidenceStore.TryGetVaultIdentityAsync(
            rebuiltEntry.VaultId,
            "tenant-alpha",
            "company-alpha");
        rebuiltIdentity.Should().NotBeNull();
        var verifiedRebuiltIdentity = rebuiltIdentity!;
        var rebuiltArtifact = verifiedRebuiltIdentity.Artifacts.Should().ContainSingle(artifact =>
            artifact.Document != null
            && artifact.Document.DocumentId == rebuiltEntry.Document.DocumentId).Subject;
        var rebuiltArtifactPath = Path.Combine(
            _root,
            rebuiltArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var rebuiltBytes = await File.ReadAllBytesAsync(rebuiltArtifactPath);
        Convert.ToHexString(SHA256.HashData(rebuiltBytes)).ToLowerInvariant()
            .Should().Be(rebuiltEntry.Document.SourceHashSha256);

        var manifest = await evidenceStore.TryOpenManifestByVaultIdAsync(
            rebuiltEntry.VaultId,
            "tenant-alpha",
            "company-alpha");
        manifest.Should().NotBeNull();
        await manifest!.Content.DisposeAsync();
    }

    [Fact]
    public async Task EvidenceRetainer_ProjectionLossAfterLocalSourceRemoval_RebuildsFromVerifiedAuthorityBytes()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var evidenceStore = new FileEvidenceArtifactStore(
            _root,
            NullLogger<FileEvidenceArtifactStore>.Instance);
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root, evidenceStore);
        var retainedSourcePath = WriteRetainedSource();
        var result = BuildImportResult() with { RetainedSourcePath = retainedSourcePath };
        var request = BuildEvidenceRequest();
        var retained = await retainer.RetainAsync(result, request);
        var sourceArtifact = retained.EvidenceVaultIdentity!.Artifacts.Single(artifact =>
            artifact.Kind == "statement-source");
        var vaultDirectory = Path.Combine(_root, "workstation", "evidence", "_vault");
        Directory.Delete(vaultDirectory, recursive: true);
        File.Delete(Path.Combine(
            _root,
            retainedSourcePath.Replace('/', Path.DirectorySeparatorChar)));

        var retried = await retainer.RetainAsync(retained, request);

        retried.EvidenceVaultIdentity!.VaultId.Should().Be(retained.EvidenceVaultIdentity.VaultId);
        retried.EvidenceVaultIdentity.TenantId.Should().Be("tenant-alpha");
        retried.EvidenceVaultIdentity.Scope.Should().Be("company-alpha");
        var entries = await evidenceStore.ListDocumentsAsync(
            new EvidenceVaultDocumentQueryDto(
                Classification: EvidenceDocumentClassificationDto.Statement,
                ChannelKind: EvidenceDocumentIntakeChannelDto.ImportedFileReference,
                SubjectKind: "statement-run",
                SubjectId: result.RunId,
                TenantId: "tenant-alpha",
                Scope: "company-alpha"));
        var document = entries.Should().ContainSingle().Subject.Document;
        document.TenantId.Should().Be("tenant-alpha");
        document.Scope.Should().Be("company-alpha");
        document.SourceHashSha256.Should().Be(sourceArtifact.ContentHashSha256);
        document.SourceRecord!.SourceReference.Should().Be(retainedSourcePath);
        File.Exists(Path.Combine(
            _root,
            retainedSourcePath.Replace('/', Path.DirectorySeparatorChar))).Should().BeFalse();
    }

    [Fact]
    public async Task EvidenceRetainer_MissingDurableDocuments_RebuildsFromRetainedSource()
    {
        var firstAuthority = new InMemoryDurableStatementAuthority();
        var source = WriteRetainedSource();
        var first = await new ReportingStatementImportEvidenceRetainer(firstAuthority, _root)
            .RetainAsync(
                BuildImportResult() with { RetainedSourcePath = source },
                BuildEvidenceRequest());
        var restartedAuthority = new InMemoryDurableStatementAuthority();

        var rebuilt = await new ReportingStatementImportEvidenceRetainer(
                restartedAuthority,
                _root)
            .RetainAsync(first, BuildEvidenceRequest());

        rebuilt.EvidenceVaultIdentity.Should().NotBeNull();
        restartedAuthority.ListKeys(AuthorityScope()).Should().HaveCount(4);
        var manifest = await restartedAuthority.TryReadDocumentAsync(
            AuthorityScope(),
            rebuilt.EvidenceVaultIdentity!.ManifestPath);
        manifest.Should().NotBeNull();
    }

    [Fact]
    public async Task EvidenceRetainer_CorruptDurableSource_FailsClosed()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var source = WriteRetainedSource();
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root);
        var retained = await retainer.RetainAsync(
            BuildImportResult() with { RetainedSourcePath = source },
            BuildEvidenceRequest());
        var artifact = retained.EvidenceVaultIdentity!.Artifacts.Single(item =>
            item.Kind == "statement-source");
        authority.Corrupt(AuthorityScope(), artifact.RelativePath);

        var act = () => retainer.RetainAsync(retained, BuildEvidenceRequest());

        await act.Should().ThrowAsync<ReportingArtifactIntegrityException>();
    }

    [Fact]
    public async Task EvidenceRetainer_CorruptDurableCanonicalArtifact_FailsClosed()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var source = WriteRetainedSource();
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root);
        var retained = await retainer.RetainAsync(
            BuildImportResult() with { RetainedSourcePath = source },
            BuildEvidenceRequest());
        var canonical = retained.EvidenceVaultIdentity!.Artifacts.Single(item =>
            item.Kind == "statement-canonical");
        authority.Corrupt(AuthorityScope(), canonical.RelativePath);

        var act = () => retainer.RetainAsync(retained, BuildEvidenceRequest());

        await act.Should().ThrowAsync<ReportingArtifactIntegrityException>();
    }

    [Fact]
    public async Task EvidenceRetainer_DurableManifestForDifferentAccountAndPeriod_FailsClosed()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var source = WriteRetainedSource();
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root);
        var retained = await retainer.RetainAsync(
            BuildImportResult() with { RetainedSourcePath = source },
            BuildEvidenceRequest());
        var mismatchedRequest = BuildEvidenceRequest() with
        {
            ExternalAccountId = "external-beta",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31)
        };

        var act = () => retainer.RetainAsync(retained, mismatchedRequest);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Immutable authority document cannot be replaced*");
    }

    [Fact]
    public async Task EvidenceRetainer_WrongSubjectLinkage_IsNotAcceptedAsVerifiedIdentity()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var source = WriteRetainedSource();
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root);
        var retained = await retainer.RetainAsync(
            BuildImportResult() with { RetainedSourcePath = source },
            BuildEvidenceRequest());
        var identity = retained.EvidenceVaultIdentity!;
        var wrongSubject = retained with
        {
            EvidenceVaultIdentity = identity with
            {
                SubjectKind = "other-subject",
                Artifacts = identity.Artifacts
                    .Select(static artifact => artifact with
                    {
                        CanonicalSubjectKind = "other-subject",
                        CanonicalSubjectId = "other-run"
                    })
                    .ToArray()
            }
        };
        var writesBeforeRetry = authority.WriteOrder.Count;

        var repaired = await retainer.RetainAsync(wrongSubject, BuildEvidenceRequest());

        repaired.EvidenceVaultIdentity!.SubjectKind.Should().Be("statement-run");
        repaired.EvidenceVaultIdentity.SubjectId.Should().Be("statement-run-alpha");
        repaired.EvidenceVaultIdentity.Artifacts.Should().OnlyContain(item =>
            item.CanonicalSubjectKind == "statement-run"
            && item.CanonicalSubjectId == "statement-run-alpha");
        authority.WriteOrder.Should().HaveCount(writesBeforeRetry + 4,
            "invalid subject linkage must be reverified and rebuilt rather than returned blindly");
    }

    [Fact]
    public async Task EvidenceRetainer_UnmanifestedArtifactMetadata_IsNotAcceptedAsVerifiedIdentity()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var source = WriteRetainedSource();
        var request = BuildEvidenceRequest();
        var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root);
        var retained = await retainer.RetainAsync(
            BuildImportResult() with { RetainedSourcePath = source },
            request);
        var identity = retained.EvidenceVaultIdentity!;
        var artifact = identity.Artifacts[0];
        EvidenceVaultArtifactDto[] tamperedArtifacts =
        [
            artifact with
            {
                Capture = new EvidenceArtifactCaptureDto(
                    "tampered",
                    "unmanifested",
                    DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
                    "operator",
                    "source",
                    new string('b', 64),
                    EvidenceDocumentIntakeChannelDto.Api)
            },
            artifact with
            {
                ExtractedFields =
                [
                    new EvidenceArtifactExtractionFieldDto(
                        "tampered",
                        "value",
                        null,
                        1m,
                        "Unreviewed",
                        EvidenceStatusDto.Ready,
                        null,
                        null,
                        null)
                ]
            },
            artifact with
            {
                Document = new EvidenceDocumentDto(
                    "tampered-document",
                    "tampered.pdf",
                    EvidenceDocumentClassificationDto.Statement,
                    new string('c', 64),
                    DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
                    "api",
                    null,
                    "tenant-alpha",
                    "company-alpha",
                    EvidenceExtractionStatusDto.NotExtracted,
                    [],
                    new EvidenceDocumentReviewStateDto(
                        EvidenceDocumentReviewStatusDto.Unreviewed),
                    [])
            }
        ];

        foreach (var tamperedArtifact in tamperedArtifacts)
        {
            var artifacts = identity.Artifacts.ToArray();
            artifacts[0] = tamperedArtifact;
            var tampered = retained with
            {
                EvidenceVaultIdentity = identity with { Artifacts = artifacts }
            };

            (await retainer.HasVerifiedCanonicalRunEvidenceAsync(tampered, request))
                .Should().BeFalse(
                    "canonical recovery must reject artifact metadata not retained by its manifest");
        }
    }

    [Fact]
    public async Task EvidenceRetainer_RetainedSourceFileLink_IsRejectedWithoutReadingExternalTarget()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var externalFile = _root + "-external-source.csv";
        var linkDirectory = Path.Combine(_root, "reconciliation");
        var linkPath = Path.Combine(linkDirectory, "linked-source.csv");
        Directory.CreateDirectory(linkDirectory);
        await File.WriteAllTextAsync(externalFile, "external,statement\n");
        if (!TryCreateFileLink(linkPath, externalFile))
        {
            File.Delete(externalFile);
            return;
        }

        try
        {
            var retainer = new ReportingStatementImportEvidenceRetainer(authority, _root);
            var result = BuildImportResult() with
            {
                RetainedSourcePath = "reconciliation/linked-source.csv"
            };

            var act = () => retainer.RetainAsync(result, BuildEvidenceRequest());

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*symbolic link or reparse point*");
            (await File.ReadAllTextAsync(externalFile)).Should().Be("external,statement\n");
            authority.ListKeys(AuthorityScope()).Should().BeEmpty();
        }
        finally
        {
            File.Delete(linkPath);
            File.Delete(externalFile);
        }
    }

    [Fact]
    public async Task GetAsync_PostEvidenceHostFailoverWithNullLocalRun_UsesVerifiedDurableRunEvidence()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var imports = new WritingImportService(_root, BuildImportResult());
        var runs = new FakeStatementRuns();
        var firstRetainer = new ReportingStatementImportEvidenceRetainer(authority, _root);
        var first = new StatementReconciliationReportWorkflowService(
            imports,
            firstRetainer,
            runs,
            _root,
            authority,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            breakQueue: null,
            new FakeIntakeAuthority());

        var completed = await first.StartAsync(BuildCommand());

        completed.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.Completed);
        completed.Workflow.EvidenceVaultIdentity!.Artifacts.Select(static item => item.Kind)
            .Should().BeEquivalentTo(
                ["statement-source", "statement-canonical", "statement-run-evidence"]);
        completed.Workflow.EvidenceReferences.Should().Contain(reference =>
            reference.Contains("evidence-artifact:statement-canonical:", StringComparison.Ordinal));

        runs.ReturnNull = true;
        var restarted = new StatementReconciliationReportWorkflowService(
            imports,
            new ReportingStatementImportEvidenceRetainer(authority, _root),
            runs,
            _root,
            authority,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            breakQueue: null,
            new FakeIntakeAuthority());

        var recovered = await restarted.GetAsync(
            completed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        recovered.Should().NotBeNull();
        recovered!.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.Completed,
            "verified durable run evidence and a vacuously exact empty queue must survive node-local run loss");
        recovered.RetainedArtifacts.Should().HaveCount(2);
    }

    [Fact]
    public async Task StartAsync_OneShotEvidenceOutage_FreshDataRootReimportsFromAuthoritativeInput()
    {
        var authority = new InMemoryDurableStatementAuthority();
        var firstRoot = Path.Combine(_root, "host-a");
        var secondRoot = Path.Combine(_root, "host-b");
        var firstImports = new WritingImportService(firstRoot, BuildImportResult());
        var outageEvidence = new OneShotUnavailableEvidenceRetainer();
        var first = new StatementReconciliationReportWorkflowService(
            firstImports,
            outageEvidence,
            new FakeStatementRuns(),
            firstRoot,
            authority,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            breakQueue: null,
            new FakeIntakeAuthority());
        var command = BuildCommand();

        var outage = () => first.StartAsync(command);

        await outage.Should()
            .ThrowAsync<StatementReconciliationReportAuthorityUnavailableException>()
            .WithMessage("*one-shot*");
        firstImports.CommitCount.Should().Be(1);
        outageEvidence.RetainCount.Should().Be(1);
        Directory.Delete(firstRoot, recursive: true);

        var secondImports = new WritingImportService(secondRoot, BuildImportResult());
        var restarted = new StatementReconciliationReportWorkflowService(
            secondImports,
            new ReportingStatementImportEvidenceRetainer(authority, secondRoot),
            new FakeStatementRuns(),
            secondRoot,
            authority,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            breakQueue: null,
            new FakeIntakeAuthority());

        var recovered = await restarted.StartAsync(command);

        recovered.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.Completed);
        recovered.ImportResult.Should().NotBeNull();
        recovered.ImportResult!.EvidenceVaultIdentity!.Artifacts.Should().HaveCount(3);
        secondImports.CommitCount.Should().Be(1,
            "the fresh host must recreate node-local raw and canonical import outputs");
        firstImports.CommitCount.Should().Be(1);
    }

    private StatementReconciliationReportWorkflowService CreateWorkflow(
        InMemoryDurableStatementAuthority authority,
        FakeStatementRuns runs) =>
        new(
            new FakeImportService(BuildImportResult()),
            new FakeEvidenceRetainer(),
            runs,
            _root,
            authority,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            breakQueue: null,
            new FakeIntakeAuthority());

    private string ResolveDurableWorkspace(string workflowId) =>
        Path.Combine(
            _root,
            "runtime",
            "statement-reconciliation-authority-workspace",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "statement-reconciliation-report",
            workflowId);

    private string WriteRetainedSource()
    {
        var relative = "reconciliation/statement-connector-imports/source.csv";
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(
            path,
            "account,symbol,quantity\nA,AAPL,1\n"u8.ToArray());
        var canonicalPath = Path.Combine(
            _root,
            "reconciliation/statement-connector-imports/canonical.csv"
                .Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllBytes(
            canonicalPath,
            "kind,account,symbol,quantity\nPosition,A,AAPL,1\n"u8.ToArray());
        return relative;
    }

    private static StatementReconciliationReportStartCommand BuildCommand() =>
        new(
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

    private static StatementImportCommitResultDto BuildImportResult() =>
        new(
            RunId: "statement-run-alpha",
            Duplicate: false,
            RecordCount: 1,
            KindSummaries: [new StatementKindSummaryDto("Position", 1, [])],
            BreakCount: 0,
            CaseCount: 0,
            RetainedSourcePath: "reconciliation/statement-connector-imports/source.csv",
            RetainedCanonicalPath: "reconciliation/statement-connector-imports/canonical.csv",
            Status: "Imported",
            NextAction: "Review reconciliation.");

    private static StatementImportEvidenceBridgeRequest BuildEvidenceRequest() =>
        new(
            "broker",
            "Broker Alpha",
            "fund-account-alpha",
            "external-alpha",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "operator-alpha")
        {
            TenantId = "tenant-alpha",
            CompanyId = "company-alpha",
            WorkflowId =
                "statement-reconciliation-report-0123456789abcdef0123456789abcdef"
        };

    private static StatementReconciliationReportAuthorityScope AuthorityScope() =>
        new(
            "tenant-alpha",
            "company-alpha",
            "statement-reconciliation-report-0123456789abcdef0123456789abcdef");

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeImportService(StatementImportCommitResultDto result)
        : IStatementImportCommitService
    {
        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default) => Task.FromResult(result);

        public Task<StatementImportValidationResult> ValidateAsync(
            StatementSourceDocument document,
            string? connectorId,
            CancellationToken ct = default) =>
            Task.FromResult(new StatementImportValidationResult(true, result.RecordCount, []));
    }

    private sealed class WritingImportService(
        string dataRoot,
        StatementImportCommitResultDto result)
        : IStatementImportCommitService
    {
        public int CommitCount { get; private set; }

        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CommitCount++;
            WriteRetainedFile(
                result.RetainedSourcePath,
                request.Document.Content.ToArray());
            WriteRetainedFile(
                result.RetainedCanonicalPath,
                "kind,account,symbol,quantity\nPosition,A,AAPL,1\n"u8.ToArray());
            return Task.FromResult(result);
        }

        public Task<StatementImportValidationResult> ValidateAsync(
            StatementSourceDocument document,
            string? connectorId,
            CancellationToken ct = default) =>
            Task.FromResult(new StatementImportValidationResult(true, result.RecordCount, []));

        private void WriteRetainedFile(string relativePath, byte[] content)
        {
            var path = Path.Combine(
                dataRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }
    }

    private sealed class FakeEvidenceRetainer : IStatementImportEvidenceRetainer
    {
        public Task<StatementImportCommitResultDto> RetainAsync(
            StatementImportCommitResultDto result,
            StatementImportEvidenceBridgeRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(result with
            {
                EvidenceVaultIdentity = new EvidenceVaultIdentityDto(
                    "vault-alpha",
                    "statement-run",
                    result.RunId,
                    "evidence/manifest.json",
                    "/api/workstation/evidence/vault-alpha",
                    DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
                    new string('a', 64),
                    1,
                    "test")
            });
    }

    private sealed class OneShotUnavailableEvidenceRetainer
        : IStatementImportEvidenceRetainer
    {
        public int RetainCount { get; private set; }

        public Task<StatementImportCommitResultDto> RetainAsync(
            StatementImportCommitResultDto result,
            StatementImportEvidenceBridgeRequest request,
            CancellationToken ct = default)
        {
            RetainCount++;
            throw new StatementReconciliationReportAuthorityUnavailableException(
                "Statement evidence authority hit a one-shot outage.");
        }
    }

    private sealed class FakeStatementRuns : IStatementRunWorkflowService
    {
        public IReadOnlyList<ReconciliationCase> Cases { get; init; } = [];
        public bool ReturnNull { get; set; }

        public Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StatementRunWorkflowResult?>(
                ReturnNull
                    ? null
                    : new StatementRunWorkflowResult(null!, [], Cases));

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Cases);
    }

    private sealed class FakeIntakeAuthority : IStatementReconciliationIntakeAuthority
    {
        public Task<StatementAccountingScope> ResolveAccountingScopeAsync(
            StatementReconciliationIntakeScopeRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(AccountingScope);

        public Task<StatementReconciliationIntakeReceipt> PublishAsync(
            string statementWorkflowId,
            StatementImportCommitResultDto import,
            StatementAccountingScope accountingScope,
            string tenantId,
            string companyId,
            string actor,
            string sourceInstitution,
            IReadOnlyList<string> evidenceReferences,
            CancellationToken ct = default) =>
            Task.FromResult(new StatementReconciliationIntakeReceipt(
                accountingScope,
                OperationsWorkflowId,
                import.CaseCount,
                evidenceReferences
                    .Append($"operations-workflow:{OperationsWorkflowId:D}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()));
    }

    private sealed class InMemoryDurableStatementAuthority
        : IStatementReconciliationReportAuthorityStore
    {
        private readonly Dictionary<AuthorityKey, Entry> _entries = [];

        public bool IsDurableAuthority => true;
        public string StorageKind => "test-durable-statement-authority";
        public List<AuthorityKey> WriteOrder { get; } = [];

        public ValueTask<IAsyncDisposable> AcquireWorkflowLeaseAsync(
            StatementReconciliationReportAuthorityScope scope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IAsyncDisposable>(NoopLease.Instance);

        public ValueTask<bool> DocumentExistsAsync(
            StatementReconciliationReportAuthorityScope scope,
            string documentKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_entries.ContainsKey(ToKey(scope, documentKey)));

        public ValueTask<StatementReconciliationReportAuthorityDocument?> GetDocumentAsync(
            StatementReconciliationReportAuthorityScope scope,
            string documentKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                _entries.TryGetValue(ToKey(scope, documentKey), out var entry)
                    ? entry.Document
                    : null);

        public ValueTask<byte[]?> TryReadDocumentAsync(
            StatementReconciliationReportAuthorityScope scope,
            string documentKey,
            CancellationToken cancellationToken = default)
        {
            if (!_entries.TryGetValue(ToKey(scope, documentKey), out var entry))
            {
                return ValueTask.FromResult<byte[]?>(null);
            }

            Verify(entry);
            return ValueTask.FromResult<byte[]?>(entry.Content.ToArray());
        }

        public ValueTask<StatementReconciliationReportAuthorityDocument> WriteDocumentAsync(
            StatementReconciliationReportAuthorityScope scope,
            string documentKey,
            ReadOnlyMemory<byte> content,
            bool isImmutable,
            CancellationToken cancellationToken = default)
        {
            var key = ToKey(scope, documentKey);
            var bytes = content.ToArray();
            var hash = Hash(bytes);
            var now = DateTimeOffset.UtcNow;
            if (_entries.TryGetValue(key, out var existing))
            {
                Verify(existing);
                if (existing.Document.IsImmutable != isImmutable)
                {
                    throw new InvalidOperationException("Retention policy cannot change.");
                }

                if (isImmutable)
                {
                    if (!string.Equals(
                            existing.Document.Identity.ContentHashSha256,
                            hash,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Immutable authority document cannot be replaced.");
                    }

                    WriteOrder.Add(key);
                    return ValueTask.FromResult(existing.Document);
                }

                var updated = existing.Document with
                {
                    Identity = new ReportingArtifactIdentity(scope.TenantId, hash),
                    ByteSize = bytes.LongLength,
                    Version = existing.Document.Version + 1,
                    UpdatedAtUtc = now
                };
                _entries[key] = new Entry(updated, bytes);
                WriteOrder.Add(key);
                return ValueTask.FromResult(updated);
            }

            var document = new StatementReconciliationReportAuthorityDocument(
                scope,
                documentKey,
                new ReportingArtifactIdentity(scope.TenantId, hash),
                bytes.LongLength,
                isImmutable,
                1,
                now,
                now);
            _entries.Add(key, new Entry(document, bytes));
            WriteOrder.Add(key);
            return ValueTask.FromResult(document);
        }

        public ValueTask<IReadOnlyList<string>> ListDocumentKeysAsync(
            StatementReconciliationReportAuthorityScope scope,
            string documentKeyPrefix,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<string>>(ListKeys(scope)
                .Where(key => key.StartsWith(documentKeyPrefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray());

        public ValueTask ProbeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public IReadOnlyList<string> ListKeys(
            StatementReconciliationReportAuthorityScope scope) =>
            _entries.Keys
                .Where(key =>
                    string.Equals(key.TenantId, scope.TenantId, StringComparison.Ordinal)
                    && string.Equals(key.CompanyId, scope.CompanyId, StringComparison.Ordinal)
                    && string.Equals(key.WorkflowId, scope.WorkflowId, StringComparison.Ordinal))
                .Select(static key => key.DocumentKey)
                .Order(StringComparer.Ordinal)
                .ToArray();

        public void Corrupt(
            StatementReconciliationReportAuthorityScope scope,
            string documentKey)
        {
            var key = ToKey(scope, documentKey);
            var entry = _entries[key];
            _entries[key] = entry with { Content = "corrupt"u8.ToArray() };
        }

        private static AuthorityKey ToKey(
            StatementReconciliationReportAuthorityScope scope,
            string documentKey) =>
            new(scope.TenantId, scope.CompanyId, scope.WorkflowId, documentKey);

        private static void Verify(Entry entry)
        {
            var actual = Hash(entry.Content);
            if (entry.Content.LongLength != entry.Document.ByteSize
                || !string.Equals(
                    actual,
                    entry.Document.Identity.ContentHashSha256,
                    StringComparison.Ordinal))
            {
                throw new ReportingArtifactIntegrityException(
                    entry.Document.Identity,
                    "test authority bytes do not match retained metadata");
            }
        }

        private static string Hash(byte[] content) =>
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        public sealed record AuthorityKey(
            string TenantId,
            string CompanyId,
            string WorkflowId,
            string DocumentKey);

        private sealed record Entry(
            StatementReconciliationReportAuthorityDocument Document,
            byte[] Content);

        private sealed class NoopLease : IAsyncDisposable
        {
            public static NoopLease Instance { get; } = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
