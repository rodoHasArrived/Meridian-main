using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Documents;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunCertificationServiceTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountingPeriodId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly ReportingAsOfDate = new(2026, 7, 31);

    [Fact]
    public async Task CertifyAsync_SameAuthoritativeCheckpointProducesStableSnapshotAndNormalizedScope()
    {
        var source = new StubAuthoritativeSource(Rows());
        var sut = new ReportingRunCertificationService(source, new StubReconciliationSource());

        var first = await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-one", CapturedAt.AddHours(1)),
            Access());
        var second = await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-two", CapturedAt.AddHours(2)),
            Access());

        second.Snapshot.SnapshotHash.Should().Be(first.Snapshot.SnapshotHash);
        second.Snapshot.SnapshotId.Should().Be(first.Snapshot.SnapshotId);
        second.Snapshot.SourceCheckpointId.Should().Be(first.AuthoritativeSource.CheckpointId);
        second.Snapshot.ReconciliationCheckpointId.Should().NotBe(second.Snapshot.SourceCheckpointId);
        second.DatasetRows.Should().Equal(first.DatasetRows);
        first.Snapshot.RequiresCertifiedLedgerPresentation.Should().BeFalse(
            "non-client-package reports must retain the legacy immutable snapshot binding");
        first.Snapshot.ParametersCanonicalJson.Should().NotContain(
            "requiresCertifiedLedgerPresentation",
            "the false case must preserve the pre-upgrade canonical parameter hash");
        JsonSerializer.Serialize(first.Snapshot).Should().NotContain(
            "RequiresCertifiedLedgerPresentation",
            "the false case must preserve pre-upgrade manifest and store payload hashes");
        first.OperationalScope.TenantId.Should().Be("tenant-a");
        first.OperationalScope.CompanyId.Should().Be("company-a");
        first.Readiness.ResolvedParameters.LedgerBook.LedgerBookId.Should().Be(StubAuthoritativeSource.BookId);
        first.Readiness.ResolvedParameters.PeriodId.Should().Be(StubAuthoritativeSource.PeriodId.ToString("D"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RevalidateForReleaseAsync_CapitalAccountClientPackageRejectsMissingOrChangedPresentationEvidence(
        bool removeEvidence)
    {
        var template = CapitalAccountTemplate();
        var reportPack = BuildCanonicalCapitalReportPack();
        var source = new StubAuthoritativeSource(CapitalAccountRows(), reportPack);
        var sut = new ReportingRunCertificationService(source, new StubReconciliationSource());
        var certified = await sut.CertifyAsync(
            template,
            Readiness(
                "evaluation-capital-revalidation",
                CapturedAt,
                ReportingOutputFormatDto.ClientPackage,
                new VersionedReportTemplateIdDto(template.TemplateId, 1)),
            Access());
        if (removeEvidence)
        {
            source.OmitCertifiedLedgerPresentationEvidence = true;
        }
        else
        {
            source.CertifiedLedgerPresentationEvidenceOverride =
                $"ledger-report-pack:changed:{new string('f', 64)}";
        }

        Func<Task> revalidate = async () => await sut.RevalidateForReleaseAsync(
            certified.Readiness.ResolvedParameters,
            template.TemplateId,
            certified.AuthoritativeSource,
            certified.Snapshot,
            Access());

        await revalidate.Should()
            .ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*authoritative ledger source changed after certification*");
    }

    [Fact]
    public async Task CertifyAsync_CapitalAccountClientPackageWithoutPresentationEvidenceFailsClosed()
    {
        var template = CapitalAccountTemplate();
        var sut = new ReportingRunCertificationService(
            new StubAuthoritativeSource(CapitalAccountRows()),
            new StubReconciliationSource());

        Func<Task> certify = async () => await sut.CertifyAsync(
            template,
            Readiness(
                "evaluation-capital-no-presentation-evidence",
                CapturedAt,
                ReportingOutputFormatDto.ClientPackage,
                new VersionedReportTemplateIdDto(template.TemplateId, 1)),
            Access());

        await certify.Should()
            .ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*did not retain one signed canonical ledger-presentation checksum*");
    }

    [Fact]
    public async Task CertifyAsync_MissingRetainedReconciliationEvidenceFailsClosed()
    {
        var sut = new ReportingRunCertificationService(
            new StubAuthoritativeSource(Rows()),
            reconciliationEvidenceSource: null);

        Func<Task> certify = async () => await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-one", CapturedAt),
            Access());

        await certify.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*reconciliation*configured*");
    }

    [Fact]
    public async Task CertifyAsync_HealthyStoreMissingExactReceiptReturnsStructuredReadinessBlocker()
    {
        var sut = new ReportingRunCertificationService(
            new StubAuthoritativeSource(Rows()),
            new ReportingReconciliationEvidenceSource(new EmptyReconciliationStore()));

        Func<Task> certify = async () => await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-one", CapturedAt),
            Access());

        var exception = (await certify.Should().ThrowAsync<ReportingRunReadinessBlockedException>()).Which;
        exception.Readiness.Status.Should().Be(ReportingRunReadinessStatusDto.Blocked);
        exception.Readiness.CanGenerateDraft.Should().BeFalse();
        exception.Readiness.CanGenerateFinal.Should().BeFalse();
        exception.Readiness.Checks.Should().ContainSingle(check =>
            check.CheckId == "exact-reconciliation-evidence"
            && check.Status == ReportingRunReadinessStatusDto.Blocked
            && check.BlocksDraft
            && check.BlocksFinal
            && check.EvidenceReferences.Contains(
                "reporting-source-checkpoint:ledger-checkpoint-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:" + new string('b', 64)));
        exception.Readiness.BlockingReasons.Should().ContainSingle(reason =>
            reason.Contains("No retained reconciliation/close checkpoint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CertifyAsync_OpenBreakReceiptSurfacesExactMeasuresBlockedOutputsAndEvidenceHash()
    {
        var source = new StubAuthoritativeSource(Rows());
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        var capture = await source.CaptureAsync(parameters, Access());
        var now = CapturedAt;
        var breakItem = new ReconciliationBreakQueueItem(
            BreakId: "break-cost-basis-1",
            RunId: "reconciliation-run-1",
            StrategyName: "Fund reconciliation",
            Category: ReconciliationBreakCategory.ExternalStatementMismatch,
            Status: ReconciliationBreakQueueStatus.InReview,
            Variance: 12m,
            Reason: "Cost-basis mismatch",
            AssignedTo: "controller-a",
            DetectedAt: now,
            LastUpdatedAt: now,
            EvidenceLinks: ["evidence:statement-1"],
            Measures:
            [
                new(ReconciliationBreakMeasureKindDto.Value, 100m, 112m, 12m, 1m, "USD"),
                new(ReconciliationBreakMeasureKindDto.Quantity, 10m, 11m, 1m, 0m, "units"),
                new(ReconciliationBreakMeasureKindDto.CostBasis, 80m, 84m, 4m, 1m, "USD")
            ],
            BlockedOutputs: ["FinalReport", "PeriodClose"]);
        var exactBreak = ReportingReconciliationEvidenceValidation.CreateBreakEvidence(breakItem);
        var receipt = ReportingReconciliationEvidenceValidation.CreateReceipt(
            capture.Checkpoint,
            new ReportingReconciliationCompletionEvidence(
                "reconciliation-completion-open-1",
                new string('c', 64),
                now,
                HasOpenBreaks: true,
                ["reconciliation-run:1"],
                [exactBreak]));
        var store = new InMemoryReportingReconciliationEvidenceStore();
        await store.RetainAsync(receipt);
        var sut = new ReportingRunCertificationService(
            source,
            new ReportingReconciliationEvidenceSource(store));

        Func<Task> certify = async () => await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-open-break", now),
            Access());

        var exception = (await certify.Should().ThrowAsync<ReportingRunReadinessBlockedException>()).Which;
        var check = exception.Readiness.Checks.Single(item => item.CheckId == "exact-reconciliation-evidence");
        check.IssueCount.Should().Be(1);
        check.Summary.Should().Contain("break-cost-basis-1");
        check.EvidenceReferences.Should().Contain("reconciliation-break:break-cost-basis-1");
        check.EvidenceReferences.Should().Contain($"reconciliation-break:break-cost-basis-1:evidence-sha256:{exactBreak.EvidenceHashSha256}");
        check.EvidenceReferences.Should().Contain("reconciliation-break:break-cost-basis-1:blocked-output:FinalReport");
        check.EvidenceReferences.Should().Contain(reference =>
            reference.Contains("measure:CostBasis:expected=80:actual=84:variance=4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReconciliationEvidence_CrossScopeReceiptFailsWithoutExposingOpenBreakDetail()
    {
        var source = new StubAuthoritativeSource(Rows());
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        var capture = await source.CaptureAsync(parameters, Access());
        var mismatchedCheckpointId = "secret-source-checkpoint-dddddddddddddddddddddddddddddddd";
        var mismatchedCheckpointHash = new string('d', 64);
        var mismatchedSource = capture.Checkpoint with
        {
            TenantId = "tenant-secret",
            OrganizationId = "organization-secret",
            CompanyId = "company-secret",
            FundId = "fund-secret",
            LedgerBookId = Guid.NewGuid().ToString("D"),
            AccountingPeriodId = Guid.NewGuid().ToString("D"),
            CheckpointId = mismatchedCheckpointId,
            CheckpointHash = mismatchedCheckpointHash,
            EvidenceIds = [$"reporting-source-checkpoint:{mismatchedCheckpointId}:{mismatchedCheckpointHash}"]
        };
        var sensitiveBreak = ReportingReconciliationEvidenceValidation.CreateBreakEvidence(
            new ReconciliationBreakQueueItem(
                BreakId: "secret-break-cost-basis-987",
                RunId: "secret-run",
                StrategyName: "Secret fund reconciliation",
                Category: ReconciliationBreakCategory.ExternalStatementMismatch,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 987654m,
                Reason: "Secret cost-basis mismatch",
                AssignedTo: "secret-controller",
                DetectedAt: CapturedAt,
                LastUpdatedAt: CapturedAt,
                EvidenceLinks: ["secret-evidence:statement-987"],
                Measures:
                [
                    new(ReconciliationBreakMeasureKindDto.Value, 1m, 987655m, 987654m, 0m, "USD"),
                    new(ReconciliationBreakMeasureKindDto.Quantity, null, null, null, null, "units", "Secret quantity unavailable."),
                    new(ReconciliationBreakMeasureKindDto.CostBasis, 1m, 987655m, 987654m, 0m, "USD")
                ],
                BlockedOutputs: ["SecretFinalReport"]));
        var mismatchedReceipt = ReportingReconciliationEvidenceValidation.CreateReceipt(
            mismatchedSource,
            new ReportingReconciliationCompletionEvidence(
                "secret-reconciliation-completion",
                new string('e', 64),
                CapturedAt,
                HasOpenBreaks: true,
                ["secret-completion-evidence"],
                [sensitiveBreak]));
        var sut = new ReportingReconciliationEvidenceSource(
            new ReturningReconciliationStore(mismatchedReceipt));

        Func<Task> resolve = async () => await sut.ResolveAsync(
            parameters,
            capture.Checkpoint,
            Access());

        var exception = (await resolve.Should()
            .ThrowAsync<ReportingReconciliationEvidenceInvalidException>()).Which;
        exception.Message.Should().Contain("exact requested reporting tenant/fund/book/period/source scope");
        exception.Message.Should().NotContain("secret-break");
        exception.Message.Should().NotContain("987654");
        exception.Message.Should().NotContain("secret-evidence");
        exception.EvidenceReferences.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconciliationEvidence_ResolvedAtCloseThenReopenedBlocksFinalReportingAgainstCurrentQueue()
    {
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        var sourceProvider = new StubAuthoritativeSource(Rows());
        var source = (await sourceProvider.CaptureAsync(parameters, Access())).Checkpoint;
        var resolved = ResolvedScopedBreak(
            "break-reopened-after-close",
            source,
            parameters.AsOfDate);
        var receipt = ClosedReceipt(
            source,
            resolved,
            "hard-close-before-reopen");
        var store = new InMemoryReportingReconciliationEvidenceStore();
        await store.RetainAsync(receipt);
        var reopened = resolved with
        {
            Status = ReconciliationBreakQueueStatus.Open,
            LifecycleState = ReconciliationCaseLifecycleState.Reopened,
            Disposition = null,
            DispositionReason = null,
            ResolvedBy = null,
            ResolvedAt = null,
            DispositionApprovalReference = null,
            DispositionApprovedBy = null,
            DispositionEvidenceHash = null,
            DisposedAt = null,
            Version = 3,
            ReopenReason = "New custodian evidence invalidated the resolution."
        };
        var queue = Substitute.For<IReconciliationBreakQueueRepository>();
        queue.GetAllAsync(
                Arg.Is<ReconciliationBreakQueueScope>(scope =>
                    scope.TenantId == source.TenantId
                    && scope.CompanyId == source.CompanyId),
                Arg.Any<ReconciliationBreakQueueStatus?>(),
                Arg.Any<CancellationToken>())
            .Returns([reopened]);
        var sut = new ReportingReconciliationEvidenceSource(store, queue);

        Func<Task> resolve = async () => await sut.ResolveAsync(parameters, source, Access());

        var exception = (await resolve.Should()
            .ThrowAsync<ReportingReconciliationEvidenceInvalidException>()).Which;
        exception.Message.Should().Contain("changed after the retained close receipt");
        exception.Message.Should().Contain(reopened.BreakId);
        exception.Message.Should().Contain("Re-run reconciliation and close");
        await queue.Received(1).GetAllAsync(
            Arg.Is<ReconciliationBreakQueueScope>(scope =>
                scope.TenantId == source.TenantId
                && scope.CompanyId == source.CompanyId),
            Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>());
        await queue.DidNotReceive().GetAllAsync(
            Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconciliationEvidence_CrossTenantCollidingQueueRowCannotChangeCertificationEvidence()
    {
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        var sourceProvider = new StubAuthoritativeSource(Rows());
        var source = (await sourceProvider.CaptureAsync(parameters, Access())).Checkpoint;
        var resolved = ResolvedScopedBreak(
            "same-break-id-across-tenants",
            source,
            parameters.AsOfDate);
        var receipt = ClosedReceipt(
            source,
            resolved,
            "hard-close-before-cross-tenant-collision");
        var store = new InMemoryReportingReconciliationEvidenceStore();
        await store.RetainAsync(receipt);
        var crossTenantCollision = resolved with
        {
            TenantId = "tenant-b",
            CompanyId = source.CompanyId,
            Status = ReconciliationBreakQueueStatus.Open,
            LifecycleState = ReconciliationCaseLifecycleState.Reopened,
            Disposition = null,
            DispositionReason = null,
            ResolvedBy = null,
            ResolvedAt = null,
            DispositionEvidenceHash = null,
            DisposedAt = null,
            Version = 3,
            ReopenReason = "A different tenant reopened its colliding case."
        };
        var queue = Substitute.For<IReconciliationBreakQueueRepository>();
        queue.GetAllAsync(
                Arg.Is<ReconciliationBreakQueueScope>(scope =>
                    scope.TenantId == source.TenantId
                    && scope.CompanyId == source.CompanyId),
                Arg.Any<ReconciliationBreakQueueStatus?>(),
                Arg.Any<CancellationToken>())
            .Returns([resolved]);
        queue.GetAllAsync(
                Arg.Any<ReconciliationBreakQueueStatus?>(),
                Arg.Any<CancellationToken>())
            .Returns([crossTenantCollision]);
        var sut = new ReportingReconciliationEvidenceSource(store, queue);

        var actual = await sut.ResolveAsync(parameters, source, Access());

        actual.Should().Be(receipt);
        await queue.Received(1).GetAllAsync(
            Arg.Is<ReconciliationBreakQueueScope>(scope =>
                scope.TenantId == source.TenantId
                && scope.CompanyId == source.CompanyId),
            Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>());
        await queue.DidNotReceive().GetAllAsync(
            Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconciliationEvidence_MissingAuthoritativeSourceQueueScopeFailsClosedBeforeQueueRead()
    {
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        var sourceProvider = new StubAuthoritativeSource(Rows());
        var captured = (await sourceProvider.CaptureAsync(parameters, Access())).Checkpoint;
        var sourceWithoutCompany = captured with { CompanyId = null };
        var queue = Substitute.For<IReconciliationBreakQueueRepository>();
        var sut = new ReportingReconciliationEvidenceSource(
            new EmptyReconciliationStore(),
            queue);

        Func<Task> resolve = async () =>
            await sut.ResolveAsync(parameters, sourceWithoutCompany, Access());

        var exception = (await resolve.Should()
            .ThrowAsync<ReportingReconciliationEvidenceInvalidException>()).Which;
        exception.Message.Should().Contain("authoritative reporting source");
        exception.Message.Should().Contain("exact tenant and company scope");
        queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ReconciliationEvidence_MissingReceiptQueueScopeFailsClosedBeforeQueueRead()
    {
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        var sourceProvider = new StubAuthoritativeSource(Rows());
        var source = (await sourceProvider.CaptureAsync(parameters, Access())).Checkpoint;
        var resolved = ResolvedScopedBreak(
            "break-before-missing-receipt-scope",
            source,
            parameters.AsOfDate);
        var receiptWithoutCompany = ClosedReceipt(
            source,
            resolved,
            "hard-close-before-missing-receipt-scope") with
        {
            CompanyId = null
        };
        var queue = Substitute.For<IReconciliationBreakQueueRepository>();
        var sut = new ReportingReconciliationEvidenceSource(
            new ReturningReconciliationStore(receiptWithoutCompany),
            queue);

        Func<Task> resolve = async () =>
            await sut.ResolveAsync(parameters, source, Access());

        var exception = (await resolve.Should()
            .ThrowAsync<ReportingReconciliationEvidenceInvalidException>()).Which;
        exception.Message.Should().Contain("retained reconciliation receipt");
        exception.Message.Should().Contain("exact tenant and company scope");
        queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void CloseEvidence_StaleZeroBreakSummaryFailsAgainstCanonicalQueue()
    {
        var bookId = Guid.NewGuid();
        var exact = ScopedBreak("exact-open", "fund-a", bookId);

        var act = () => AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [exact],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 0);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*reports 0 open reconciliation break(s), but 1 exact scoped case(s)*");
    }

    [Fact]
    public void CloseEvidence_UsesCompositeFundAndLedgerIdentityWithoutUnionSubstitution()
    {
        var bookId = Guid.NewGuid();
        var exact = ScopedBreak("exact-open", "fund-a", bookId);
        var sameFundWrongBook = ScopedBreak("wrong-book", "fund-a", Guid.NewGuid());
        var sameBookWrongFund = ScopedBreak("wrong-fund", "fund-b", bookId);

        var evidence = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [sameFundWrongBook, sameBookWrongFund, exact],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 1);

        evidence.Should().ContainSingle().Which.BreakId.Should().Be("exact-open");
        var noExact = () => AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [sameFundWrongBook, sameBookWrongFund],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 1);
        noExact.Should().Throw<InvalidOperationException>()
            .WithMessage("*reports 1 open reconciliation break(s), but 0 exact scoped case(s)*");
    }

    [Fact]
    public void CloseEvidence_RetainsTerminalDispositionHistoryForExactPeriod()
    {
        var bookId = Guid.NewGuid();
        var active = ScopedBreak("active", "fund-a", bookId);
        var resolved = ScopedBreak("resolved", "fund-a", bookId) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "Matched to retained custodian evidence.",
            ResolvedBy = "fund-accountant",
            DispositionEvidenceHash = new string('d', 64),
            BlockedOutputs = []
        };
        var wrongPeriod = ScopedBreak("wrong-period", "fund-a", bookId) with
        {
            AccountingPeriodId = Guid.NewGuid().ToString("D")
        };

        var evidence = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [active, resolved, wrongPeriod],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 1);

        evidence.Select(static item => item.BreakId).Should().Equal("active", "resolved");
        evidence.Single(static item => item.BreakId == "resolved").Disposition
            .Should().Be(ReconciliationBreakDispositionDto.Resolved);
    }

    [Fact]
    public void CloseEvidence_RejectsStatusDispositionContradictionsBeforeRetention()
    {
        var bookId = Guid.NewGuid();
        var openWithStaleDisposition = ScopedBreak("open-with-stale-disposition", "fund-a", bookId) with
        {
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "Stale terminal state.",
            ResolvedBy = "controller-a",
            DispositionEvidenceHash = new string('d', 64)
        };
        var resolvedWithoutDisposition = ScopedBreak("resolved-without-disposition", "fund-a", bookId) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            BlockedOutputs = []
        };
        var activeLifecycleWithTerminalStatus = ScopedBreak(
            "active-lifecycle-terminal-status",
            "fund-a",
            bookId) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Investigating,
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "Contradictory retained terminal disposition.",
            ResolvedBy = "controller-b",
            DispositionEvidenceHash = new string('e', 64),
            BlockedOutputs = []
        };
        var terminalLifecycleWithOpenStatus = ScopedBreak(
            "terminal-lifecycle-open-status",
            "fund-a",
            bookId) with
        {
            LifecycleState = ReconciliationCaseLifecycleState.Resolved
        };

        var staleDispositionAct = () => AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [openWithStaleDisposition],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 0);
        var missingDispositionAct = () => AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [resolvedWithoutDisposition],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 0);
        var activeLifecycleAct = () => AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [activeLifecycleWithTerminalStatus],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 0);
        var terminalLifecycleAct = () => AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [terminalLifecycleWithOpenStatus],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 1);

        staleDispositionAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*open-with-stale-disposition*contradictory lifecycle*queue status*disposition*");
        missingDispositionAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*resolved-without-disposition*contradictory lifecycle*queue status*disposition*");
        activeLifecycleAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*active-lifecycle-terminal-status*contradictory*");
        terminalLifecycleAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*terminal-lifecycle-open-status*contradictory*");
    }

    [Fact]
    public void CloseGate_BlocksOpenExactBreakWithRecoveryGuidance_AndAllowsDisposedEvidence()
    {
        var bookId = Guid.NewGuid();
        var period = new LedgerPeriodDto(
            AccountingPeriodId,
            bookId,
            2026,
            7,
            "2026-07",
            new DateOnly(2026, 7, 1),
            ReportingAsOfDate,
            LedgerPeriodStatusDto.SoftClosed,
            CapturedAt,
            ClosedAt: null,
            Version: 1);
        var openEvidence = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [ScopedBreak("open-exact", "fund-a", bookId)],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 1);
        var resolvedBreak = ScopedBreak("resolved-exact", "fund-a", bookId) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "Matched to retained custodian evidence.",
            ResolvedBy = "fund-controller",
            DispositionEvidenceHash = new string('d', 64),
            BlockedOutputs = []
        };
        var disposedEvidence = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [resolvedBreak],
            "fund-a",
            bookId,
            AccountingPeriodId,
            ReportingAsOfDate,
            expectedOpenBreakCount: 0);

        var blockedAct = () => AccountingClosePostingWorkbenchBridge.EnsureNoOpenReportingBreaks(
            openEvidence,
            period);
        var allowedAct = () => AccountingClosePostingWorkbenchBridge.EnsureNoOpenReportingBreaks(
            disposedEvidence,
            period);

        blockedAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*open-exact*Assign and resolve, waive, or supersede each case, then retry hard close.*");
        allowedAct.Should().NotThrow();
    }

    [Theory]
    [InlineData("entity-scope-enum")]
    [InlineData("accounting-basis-enum")]
    [InlineData("consolidation-enum")]
    [InlineData("output-format-enum")]
    [InlineData("finality-enum")]
    [InlineData("scope-consolidation-mismatch")]
    [InlineData("missing-entity")]
    [InlineData("missing-ledger-book")]
    [InlineData("missing-period")]
    [InlineData("missing-currency")]
    [InlineData("missing-final-evidence-appendix")]
    public async Task CertifyAsync_MalformedParameterMatrixFailsBeforeAuthoritativeCapture(string scenario)
    {
        var source = new StubAuthoritativeSource(Rows());
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        parameters = scenario switch
        {
            "entity-scope-enum" => parameters with
            {
                Scope = parameters.Scope with { EntityScopeKind = (ReportingEntityScopeKindDto)999 }
            },
            "accounting-basis-enum" => parameters with
            {
                AccountingBasis = (ReportingAccountingBasisDto)999
            },
            "consolidation-enum" => parameters with
            {
                ConsolidationLevel = (ReportingConsolidationLevelDto)999
            },
            "output-format-enum" => parameters with
            {
                OutputFormat = (ReportingOutputFormatDto)999
            },
            "finality-enum" => parameters with
            {
                Finality = (ReportingFinalityDto)999
            },
            "scope-consolidation-mismatch" => parameters with
            {
                Scope = parameters.Scope with
                {
                    EntityScopeKind = ReportingEntityScopeKindDto.Entity,
                    EntityId = "entity-a"
                },
                ConsolidationLevel = ReportingConsolidationLevelDto.Fund
            },
            "missing-entity" => parameters with
            {
                Scope = parameters.Scope with
                {
                    EntityScopeKind = ReportingEntityScopeKindDto.Entity,
                    EntityId = null
                },
                ConsolidationLevel = ReportingConsolidationLevelDto.Entity
            },
            "missing-ledger-book" => parameters with
            {
                LedgerBook = new ReportingLedgerBookSelectionDto()
            },
            "missing-period" => parameters with { PeriodId = " " },
            "missing-currency" => parameters with { PresentationCurrency = " " },
            "missing-final-evidence-appendix" => parameters with { IncludeEvidenceAppendix = false },
            _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'.")
        };
        var readiness = Readiness("evaluation-one", CapturedAt) with
        {
            ResolvedParameters = parameters
        };
        var sut = new ReportingRunCertificationService(source, new StubReconciliationSource());

        Func<Task> certify = async () => await sut.CertifyAsync(Template(), readiness, Access());

        await certify.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
        source.CaptureCount.Should().Be(0);
    }

    [Fact]
    public void Certify_CallerSuppliedRowsCompatibilityPathFailsClosed()
    {
#pragma warning disable CS0618
        var act = () => new ReportingRunCertificationService().Certify(
            Template(),
            Readiness("evaluation-one", CapturedAt),
            Rows(),
            "custom-request-dataset",
            Access());
#pragma warning restore CS0618

        act.Should().Throw<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*durable authoritative source*");
    }

    [Fact]
    public async Task EvaluateManifest_CrossTenantAdministratorIsDeniedBeforeOverride()
    {
        var certified = await new ReportingRunCertificationService(
                new StubAuthoritativeSource(Rows()),
                new StubReconciliationSource())
            .CertifyAsync(Template(), Readiness("evaluation-one", CapturedAt), Access());
        var manifest = BuildManifest("run-access", Template(), certified);

        var result = ReportAccessPolicyEvaluator.Evaluate(
            manifest,
            new ReportAccessQueryContext(
                "admin-b",
                CompanyId: "company-b",
                HasGlobalOverride: true,
                TenantId: "tenant-b",
                RequireBoundScope: true));

        result.IsAccessible.Should().BeFalse();
        result.Reason.Should().Contain("another tenant");
    }

    [Fact]
    public async Task ProduceAsync_CsvPreservesCertifiedBusinessValuesAndNeutralizesSpreadsheetFormulas()
    {
        var production = await ProduceAsync("run-csv", ReportingOutputFormatDto.Csv);
        var csv = Encoding.UTF8.GetString(production.Artifacts
            .Single(artifact => artifact.FileName == "run-csv.csv")
            .Content.Span);

        csv.Should().Contain("account");
        csv.Should().Contain("123.45");
        csv.Should().Contain("-23.45");
        csv.Should().Contain("'=2+3");
        csv.Should().Contain("-12.5", "valid negative numeric values must remain numeric");
        csv.Should().NotContain(",=2+3");
    }

    [Fact]
    public async Task ProduceAsync_RetainsDeterministicPreviewWithPackageHashAndDownloadIdentity()
    {
        var production = await ProduceAsync("run-preview", ReportingOutputFormatDto.Pdf);
        var preview = production.Artifacts.Single(item => item.FileName == "run-preview.preview.json");
        preview.ContentType.Should().Be("application/vnd.meridian.reporting-preview+json");
        var document = JsonNode.Parse(preview.Content.Span)!;
        document["schemaVersion"]!.GetValue<string>().Should().Be("meridian.reporting.retained-preview.v1");
        document["certifiedDatasetRowCount"]!.GetValue<int>().Should().Be(Rows().Length);
        document["previewRowCount"]!.GetValue<int>().Should().Be(Rows().Length);
        document["rows"]!.AsArray().Should().HaveCount(Rows().Length);
        document["primaryArtifact"]!["artifactId"]!.GetValue<string>().Should().Be("run-preview.pdf");

        var retainedManifestArtifact = production.Artifacts.Single(item => item.ArtifactId == production.ManifestArtifactId);
        var retainedManifest = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(
            retainedManifestArtifact.Content.Span);
        document["certifiedDatasetHashSha256"]!.GetValue<string>()
            .Should().Be(retainedManifest.CertifiedDatasetHashSha256);
        var descriptor = retainedManifest.Artifacts.Single(item => item.ArtifactId == preview.ArtifactId);
        descriptor.FileName.Should().Be(preview.FileName);
        descriptor.ContentType.Should().Be(preview.ContentType);
        descriptor.ByteLength.Should().Be(preview.Content.Length);
        descriptor.ContentHashSha256.Should().Be(
            Convert.ToHexString(SHA256.HashData(preview.Content.Span)).ToLowerInvariant());
    }

    [Fact]
    public async Task ProduceAsync_PreUpgradeCompletedManifestWithoutPreviewPreservesItsDeclarationSet()
    {
        var template = Template(reportWriterGrid: false);
        var certified = await new ReportingRunCertificationService(
                new StubAuthoritativeSource(Rows()),
                new StubReconciliationSource())
            .CertifyAsync(
                template,
                Readiness("evaluation-legacy-artifact-set", CapturedAt),
                Access());
        var current = BuildManifest("run-pre-preview", template, certified);
        var preUpgrade = current with
        {
            Artifacts = current.Artifacts
                .Where(static artifact => !artifact.EndsWith(".preview.json", StringComparison.Ordinal))
                .ToImmutableArray()
        };

        var production = await new DeterministicReportingCertifiedArtifactProducer()
            .ProduceAsync(preUpgrade);

        production.Artifacts.Select(static artifact => artifact.ArtifactId)
            .Should().BeEquivalentTo(preUpgrade.Artifacts);
        production.Artifacts.Should().NotContain(static artifact =>
            artifact.FileName.EndsWith(".preview.json", StringComparison.Ordinal));
    }

    [Fact]
    public void ArtifactDeclarations_RejectGridFileNameCollisionsAfterNormalization()
    {
        var template = Template(reportWriterGrid: false) with
        {
            ReportWriterGrids =
            [
                new ReportWriterGridDefinitionDto(
                    "tax/a",
                    "Tax A",
                    ReportWriterGridKindDto.Detail,
                    RowFields: [],
                    Metrics: []),
                new ReportWriterGridDefinitionDto(
                    "tax-a",
                    "Tax A alternate",
                    ReportWriterGridKindDto.Detail,
                    RowFields: [],
                    Metrics: [])
            ]
        };

        var action = () => ReportingArtifactDeclaration.Build(
            "run-grid-collision",
            template,
            Parameters(ReportingOutputFormatDto.Pdf));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*colliding artifact file names*");
    }

    [Fact]
    public async Task ProduceAsync_EvidenceVaultBytesAreStableAcrossRowDictionaryInsertionOrder()
    {
        var rows = Rows();
        var reversedRows = rows
            .Select(static row => (IReadOnlyDictionary<string, string>)row
                .Reverse()
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal))
            .ToImmutableArray();

        var first = await ProduceAsync(
            "run-evidence-vault-order",
            ReportingOutputFormatDto.EvidenceVault,
            rows);
        var second = await ProduceAsync(
            "run-evidence-vault-order",
            ReportingOutputFormatDto.EvidenceVault,
            reversedRows);
        var firstArtifact = first.Artifacts.Single(item =>
            item.FileName == "run-evidence-vault-order.evidence-vault.json");
        var secondArtifact = second.Artifacts.Single(item =>
            item.FileName == "run-evidence-vault-order.evidence-vault.json");

        firstArtifact.Content.ToArray().Should().Equal(secondArtifact.Content.ToArray());
    }

    [Fact]
    public async Task ProduceAsync_XlsxUsesInlineTextAndPreservesExactCertifiedValuesWithoutFormulaCells()
    {
        var production = await ProduceAsync("run-xlsx", ReportingOutputFormatDto.Xlsx);
        var artifact = production.Artifacts.Single(item => item.FileName == "run-xlsx.xlsx");
        using var stream = new MemoryStream(artifact.Content.ToArray());
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(
            archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(),
            Encoding.UTF8);
        var sheet = await reader.ReadToEndAsync();

        sheet.Should().Contain("123.45");
        sheet.Should().Contain("-23.45");
        sheet.Should().Contain("=2+3");
        sheet.Should().Contain("t=\"inlineStr\"");
        sheet.Should().NotContain("<f>");
    }

    [Fact]
    public async Task ProduceAsync_XlsxRemovesXml10ForbiddenControlsAndProducesParseableWorksheetXml()
    {
        const string safeCellValue = "Cash & <reserve> \"quoted\" 'apostrophe' 💰";
        var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            Row(
                ("account", $"Cash\u0001\u000B & <reserve> \"quoted\" 'apostrophe' 💰"),
                ("netAmount", "123.45")));
        var production = await ProduceAsync(
            "run-xlsx-controls",
            ReportingOutputFormatDto.Xlsx,
            rows);
        var artifact = production.Artifacts.Single(item => item.FileName == "run-xlsx-controls.xlsx");
        using var stream = new MemoryStream(artifact.Content.ToArray());
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        await using var worksheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();

        var document = await XDocument.LoadAsync(
            worksheet,
            LoadOptions.PreserveWhitespace,
            CancellationToken.None);
        var values = document
            .Descendants(XName.Get("t", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))
            .Select(static element => element.Value)
            .ToArray();

        values.Should().Contain(safeCellValue);
        values.Should().OnlyContain(static value =>
            !value.Contains('\u0001')
            && !value.Contains('\u000B'));
    }

    [Fact]
    public async Task ProduceAsync_PdfIncludesCertifiedAccountTotalsAndRowHash()
    {
        var production = await ProduceAsync("run-pdf", ReportingOutputFormatDto.Pdf);
        var pdf = Encoding.ASCII.GetString(production.Artifacts
            .Single(artifact => artifact.FileName == "run-pdf.pdf")
            .Content.Span);

        pdf.Should().StartWith("%PDF-1.4");
        pdf.Should().Contain("Certified ledger rows: 2");
        pdf.Should().Contain("Cash: 123.45 / 0 / 123.45");
        pdf.Should().Contain("Payable: 0 / 23.45 / -23.45");
        pdf.Should().Contain("Certified row hash:");
    }

    [Fact]
    public async Task ProduceAsync_ClientGradePdfIsValidAndByteStable()
    {
        var manifest = await BuildCertifiedManifestAsync("run-cg-pdf", ReportingOutputFormatDto.Pdf);
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer());

        var first = await producer.ProduceAsync(manifest);
        var second = await producer.ProduceAsync(manifest);

        var firstPdf = first.Artifacts.Single(artifact => artifact.FileName == "run-cg-pdf.pdf").Content.ToArray();
        var secondPdf = second.Artifacts.Single(artifact => artifact.FileName == "run-cg-pdf.pdf").Content.ToArray();

        Encoding.ASCII.GetString(firstPdf, 0, 5).Should().Be("%PDF-");
        firstPdf.Should().Equal(secondPdf, "client-grade PDF bytes must be deterministic for hash verification");
    }

    [Fact]
    public async Task ProduceAsync_ClientGradeXlsxIsValidWorkbookAndByteStable()
    {
        var manifest = await BuildCertifiedManifestAsync("run-cg-xlsx", ReportingOutputFormatDto.Xlsx);
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer());

        var first = await producer.ProduceAsync(manifest);
        var second = await producer.ProduceAsync(manifest);

        var firstXlsx = first.Artifacts.Single(artifact => artifact.FileName == "run-cg-xlsx.xlsx").Content.ToArray();
        var secondXlsx = second.Artifacts.Single(artifact => artifact.FileName == "run-cg-xlsx.xlsx").Content.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(firstXlsx), ZipArchiveMode.Read))
        {
            archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
            archive.Entries.Count(entry =>
                    entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal))
                .Should().BeGreaterThan(0);
        }

        firstXlsx.Should().Equal(secondXlsx, "client-grade XLSX bytes must be deterministic for hash verification");
    }

    [Fact]
    public async Task ProduceAsync_ClientPackageRetainsDeterministicPdfAndXlsxWithDeclaredHashesAndSizes()
    {
        var manifest = await BuildCertifiedManifestAsync(
            "run-client-package",
            ReportingOutputFormatDto.ClientPackage);
        var declarations = ReportingArtifactDeclaration.Build(manifest);
        var primaryDeclarations = declarations
            .Where(static artifact => artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput)
            .ToArray();
        var presentationSource = new StubCertifiedLedgerPresentationSource(
            input: null,
            throwOnResolve: true);
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer(),
            presentationSource);

        var first = await producer.ProduceAsync(manifest);
        var second = await producer.ProduceAsync(manifest);

        presentationSource.ResolveCount.Should().Be(
            0,
            "an unrelated client package must not depend on complete-history partners-capital resolution");
        primaryDeclarations.Should().HaveCount(2);
        primaryDeclarations.Select(static artifact => artifact.FileName)
            .Should().Equal("run-client-package.pdf", "run-client-package.xlsx");
        var firstPrimary = primaryDeclarations
            .Select(declaration => first.Artifacts.Single(artifact =>
                string.Equals(artifact.ArtifactId, declaration.ArtifactId, StringComparison.Ordinal)))
            .ToArray();
        var secondPrimary = primaryDeclarations
            .Select(declaration => second.Artifacts.Single(artifact =>
                string.Equals(artifact.ArtifactId, declaration.ArtifactId, StringComparison.Ordinal)))
            .ToArray();

        Encoding.ASCII.GetString(firstPrimary[0].Content.Span[..5]).Should().Be("%PDF-");
        using (var archive = new ZipArchive(
                   new MemoryStream(firstPrimary[1].Content.ToArray()),
                   ZipArchiveMode.Read))
        {
            archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
        }

        for (var index = 0; index < firstPrimary.Length; index++)
        {
            firstPrimary[index].Content.Should().NotBeEmpty();
            firstPrimary[index].Content.ToArray().Should().Equal(
                secondPrimary[index].Content.ToArray(),
                "every primary document in one certified client package must be reproducible");
        }

        var retainedManifestArtifact = first.Artifacts.Single(artifact =>
            string.Equals(artifact.ArtifactId, first.ManifestArtifactId, StringComparison.Ordinal));
        var retainedManifest = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(
            retainedManifestArtifact.Content.Span);
        foreach (var artifact in firstPrimary)
        {
            var descriptor = retainedManifest.Artifacts.Single(item =>
                string.Equals(item.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal));
            descriptor.ByteLength.Should().Be(artifact.Content.Length);
            descriptor.ContentHashSha256.Should().Be(
                Convert.ToHexString(SHA256.HashData(artifact.Content.Span)).ToLowerInvariant());
        }

        var preview = first.Artifacts.Single(static artifact =>
            artifact.ContentType == "application/vnd.meridian.reporting-preview+json");
        var previewDocument = JsonNode.Parse(preview.Content.Span)!;
        previewDocument["primaryArtifact"]!["artifactId"]!.GetValue<string>()
            .Should().Be("run-client-package.pdf", "the PDF is the deterministic preview anchor");
    }

    [Fact]
    public async Task ResolveExactAsync_NonCapitalClientPackageDoesNotRequireDurableLedgerGraph()
    {
        var manifest = await BuildCertifiedManifestAsync(
            "run-client-package-no-ledger-graph",
            ReportingOutputFormatDto.ClientPackage);
        using var services = new ServiceCollection().BuildServiceProvider();
        var source = new ServiceProviderReportingAuthoritativeSource(services);

        var presentation = await source.ResolveExactAsync(manifest);

        source.IsConfigured.Should().BeFalse();
        presentation.Should().BeNull();
    }

    [Fact]
    public async Task ResolveExactAsync_CapitalAccountClientPackageWithoutRetainedPresentationEvidenceFailsClosed()
    {
        var reportPack = BuildCanonicalCapitalReportPack();
        var manifest = RemoveCertifiedLedgerPresentationEvidence(
            await BuildCertifiedManifestAsync(
                "run-capital-account-no-retained-evidence",
                ReportingOutputFormatDto.ClientPackage,
                CapitalAccountRows(),
                CapitalAccountTemplate(),
                reportPack));
        using var services = new ServiceCollection().BuildServiceProvider();
        var source = new ServiceProviderReportingAuthoritativeSource(services);

        Func<Task> resolve = async () => await source.ResolveExactAsync(manifest);

        await resolve.Should()
            .ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*no unique retained signed ledger-presentation checksum*");
    }

    [Fact]
    public async Task ProduceAsync_CapitalAccountClientPackageContainsCanonicalPartnersCapitalInPdfAndWorkbook()
    {
        var reportPack = BuildCanonicalCapitalReportPack();
        var manifest = await BuildCertifiedManifestAsync(
            "run-capital-account",
            ReportingOutputFormatDto.ClientPackage,
            CapitalAccountRows(),
            CapitalAccountTemplate(),
            reportPack);
        var presentationSource = new StubCertifiedLedgerPresentationSource(
            BindCanonicalPresentation(manifest, reportPack));
        var renderer = new DocumentsReportingPrimaryDocumentRenderer();
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            renderer,
            presentationSource);

        var first = await producer.ProduceAsync(manifest);
        var second = await producer.ProduceAsync(manifest);

        var firstPdf = first.Artifacts.Single(static artifact =>
            artifact.ContentType == "application/pdf").Content.ToArray();
        var secondPdf = second.Artifacts.Single(static artifact =>
            artifact.ContentType == "application/pdf").Content.ToArray();
        var firstWorkbook = first.Artifacts.Single(static artifact =>
            artifact.ContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Content.ToArray();
        var secondWorkbook = second.Artifacts.Single(static artifact =>
            artifact.ContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Content.ToArray();
        var authoritativeDocumentRenderer = new FinancialReportDocumentRenderer();

        presentationSource.ResolveCount.Should().Be(2, "the producer resolves one exact presentation per package");
        Encoding.ASCII.GetString(firstPdf, 0, 5).Should().Be("%PDF-");
        firstPdf.Should().Equal(secondPdf);
        firstPdf.Should().Equal(
            authoritativeDocumentRenderer.RenderPdf(reportPack),
            "Reporting must reuse the existing Documents renderer over the exact certified ledger pack");
        var unboundRender = () => renderer.RenderPdf(manifest);
        unboundRender.Should().Throw<ReportingGovernanceException>()
            .WithMessage("*exact checkpoint-bound canonical ledger presentation*");
        firstWorkbook.Should().Equal(secondWorkbook);
        firstWorkbook.Should().Equal(
            authoritativeDocumentRenderer.RenderWorkbook(reportPack),
            "Reporting must not rebuild a parallel workbook model from display rows");

        using (var archive = new ZipArchive(new MemoryStream(firstWorkbook), ZipArchiveMode.Read))
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            workbookEntry.Should().NotBeNull();
            using var workbookStream = workbookEntry!.Open();
            var workbook = XDocument.Load(workbookStream);
            XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            workbook.Descendants(spreadsheet + "sheet")
                .Select(static element => element.Attribute("name")?.Value)
                .Should().Contain(static name =>
                    name != null
                    && name.StartsWith("Statement of Changes in", StringComparison.Ordinal));
            using var reader = new StreamReader(
                archive.GetEntry("xl/sharedStrings.xml")!.Open(),
                Encoding.UTF8);
            var sharedStrings = await reader.ReadToEndAsync();
            sharedStrings.Should().Contain("1,000,000.00");
            sharedStrings.Should().Contain("250,000.00");
        }
    }

    [Fact]
    public async Task ProduceAsync_CapitalAccountClientPackageWithoutCanonicalPresentationFailsClosed()
    {
        var reportPack = BuildCanonicalCapitalReportPack();
        var manifest = await BuildCertifiedManifestAsync(
            "run-capital-account-unavailable",
            ReportingOutputFormatDto.ClientPackage,
            CapitalAccountRows(),
            CapitalAccountTemplate(),
            reportPack);
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer());

        Func<Task> produce = async () => await producer.ProduceAsync(manifest);

        await produce.Should()
            .ThrowAsync<ReportingGovernanceException>()
            .WithMessage(
                "*no exact checkpoint-bound canonical ledger presentation*not recalculated from incomplete certified display rows*");
    }

    [Fact]
    public async Task ProduceAsync_GovernedCapitalAccountAliasCannotUseCheckpointUnboundProjectionFallback()
    {
        var aliasTemplate = CapitalAccountTemplate() with
        {
            TemplateId = "custom-partner-capital-package",
            Name = "Custom Partner Capital Package"
        };
        var reportPack = BuildCanonicalCapitalReportPack();
        var manifest = await BuildCertifiedManifestAsync(
            "run-capital-account-alias",
            ReportingOutputFormatDto.ClientPackage,
            CapitalAccountRows(),
            aliasTemplate,
            reportPack,
            SamplePartnersCapital());
        var renderer = new DocumentsReportingPrimaryDocumentRenderer();
        var producer = new DeterministicReportingCertifiedArtifactProducer(renderer);

        manifest.CertifiedSnapshot!.RequiresCertifiedLedgerPresentation.Should().BeTrue(
            "the governed CapitalAccountStatement family, not a built-in template id, owns the requirement");
        Action renderWithoutBoundPresentation = () => renderer.RenderPdf(manifest);
        Func<Task> produceWithoutBoundPresentation = async () => await producer.ProduceAsync(manifest);
        var preUpgradeAlias = manifest with
        {
            CertifiedSnapshot = manifest.CertifiedSnapshot with
            {
                RequiresCertifiedLedgerPresentation = false
            }
        };
        Action renderPreUpgradeAlias = () => renderer.RenderPdf(preUpgradeAlias);

        renderWithoutBoundPresentation.Should()
            .Throw<ReportingGovernanceException>()
            .WithMessage("*cannot use a checkpoint-unbound partners-capital projection*");
        renderPreUpgradeAlias.Should()
            .Throw<ReportingGovernanceException>()
            .WithMessage("*cannot use a checkpoint-unbound partners-capital projection*");
        await produceWithoutBoundPresentation.Should()
            .ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*no exact checkpoint-bound canonical ledger presentation*");
    }

    [Fact]
    public async Task ProduceAsync_CapitalAccountClientPackageRejectsPresentationWithMismatchedDatasetHash()
    {
        var reportPack = BuildCanonicalCapitalReportPack();
        var manifest = await BuildCertifiedManifestAsync(
            "run-capital-account-mismatched",
            ReportingOutputFormatDto.ClientPackage,
            CapitalAccountRows(),
            CapitalAccountTemplate(),
            reportPack);
        var presentation = BindCanonicalPresentation(
            manifest,
            reportPack) with
        {
            CertifiedDatasetHashSha256 = new string('f', 64)
        };
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer(),
            new StubCertifiedLedgerPresentationSource(presentation));

        Func<Task> produce = async () => await producer.ProduceAsync(manifest);

        await produce.Should()
            .ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*not bound to the exact certified source checkpoint and dataset hash*");
    }

    [Fact]
    public async Task ProduceAsync_CapitalAccountClientPackageWithoutCertifiedPresentationEvidenceFailsClosed()
    {
        var reportPack = BuildCanonicalCapitalReportPack();
        var manifest = RemoveCertifiedLedgerPresentationEvidence(
            await BuildCertifiedManifestAsync(
                "run-capital-account-no-evidence",
                ReportingOutputFormatDto.ClientPackage,
                CapitalAccountRows(),
                CapitalAccountTemplate(),
                reportPack));
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer(),
            new StubCertifiedLedgerPresentationSource(
                BindCanonicalPresentation(manifest, reportPack)));

        Func<Task> produce = async () => await producer.ProduceAsync(manifest);

        await produce.Should()
            .ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*signed ledger presentation checksum is missing from or changed relative to the certified source evidence*");
    }

    [Fact]
    public async Task ProduceAsync_CapitalAccountClientPackageWithChangedPresentationEvidenceFailsClosed()
    {
        var reportPack = BuildCanonicalCapitalReportPack();
        var manifest = await BuildCertifiedManifestAsync(
            "run-capital-account-changed-evidence",
            ReportingOutputFormatDto.ClientPackage,
            CapitalAccountRows(),
            CapitalAccountTemplate(),
            reportPack);
        var source = manifest.AuthoritativeSource!;
        manifest = manifest with
        {
            AuthoritativeSource = source with
            {
                EvidenceIds = source.EvidenceIds
                    .Where(static evidence =>
                        !evidence.StartsWith("ledger-report-pack:", StringComparison.Ordinal))
                    .Append($"ledger-report-pack:changed:{new string('f', 64)}")
                    .ToImmutableArray()
            }
        };
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer(),
            new StubCertifiedLedgerPresentationSource(
                BindCanonicalPresentation(manifest, reportPack)));

        Func<Task> produce = async () => await producer.ProduceAsync(manifest);

        await produce.Should()
            .ThrowAsync<ReportingGovernanceException>()
            .WithMessage("*signed ledger presentation checksum is missing from or changed relative to the certified source evidence*");
    }

    [Fact]
    public async Task ProduceAsync_ClientGradePreservesDeclarationsDescriptorsAndRetainedManifest()
    {
        var manifest = await BuildCertifiedManifestAsync("run-cg-parity", ReportingOutputFormatDto.Pdf);
        var plain = await new DeterministicReportingCertifiedArtifactProducer().ProduceAsync(manifest);
        var clientGrade = await new DeterministicReportingCertifiedArtifactProducer(
                new DocumentsReportingPrimaryDocumentRenderer())
            .ProduceAsync(manifest);

        clientGrade.Artifacts.Select(artifact => artifact.ArtifactId)
            .Should().Equal(plain.Artifacts.Select(artifact => artifact.ArtifactId));
        clientGrade.ManifestArtifactId.Should().Be(plain.ManifestArtifactId);

        var retainedManifest = clientGrade.Artifacts
            .Single(artifact => artifact.ArtifactId == clientGrade.ManifestArtifactId)
            .Content;
        var retained = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(retainedManifest.Span);
        retained.Artifacts.Should().OnlyContain(descriptor =>
            descriptor.ContentHashSha256 == null || descriptor.ContentHashSha256.Length == 64);
    }

    [Fact]
    public async Task ProduceAsync_ClientGradePdfRendersUnicodeAccountNamesWithoutRejection()
    {
        var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            Row(
                ("account", "Café Ünïcode 資本"),
                ("debit", "100.00"),
                ("credit", "0"),
                ("netAmount", "100.00")));

        var production = await ProduceClientGradeAsync("run-cg-unicode", ReportingOutputFormatDto.Pdf, rows);

        var pdf = production.Artifacts.Single(artifact => artifact.FileName == "run-cg-unicode.pdf").Content.ToArray();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task ProduceAsync_ClientGradePartnersCapitalPdfIsValidAndByteStable()
    {
        var manifest = await BuildCertifiedManifestAsync(
            "run-pc-pdf",
            ReportingOutputFormatDto.Pdf,
            partnersCapital: SamplePartnersCapital());
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer());

        var first = await producer.ProduceAsync(manifest);
        var second = await producer.ProduceAsync(manifest);

        var firstPdf = first.Artifacts.Single(artifact => artifact.FileName == "run-pc-pdf.pdf").Content.ToArray();
        var secondPdf = second.Artifacts.Single(artifact => artifact.FileName == "run-pc-pdf.pdf").Content.ToArray();

        Encoding.ASCII.GetString(firstPdf, 0, 5).Should().Be("%PDF-");
        firstPdf.Should().Equal(secondPdf, "partners-capital PDF bytes must be deterministic for hash verification");
    }

    [Fact]
    public async Task ProduceAsync_ClientGradePartnersCapitalXlsxRendersRollForwardRows()
    {
        var manifest = await BuildCertifiedManifestAsync(
            "run-pc-xlsx",
            ReportingOutputFormatDto.Xlsx,
            partnersCapital: SamplePartnersCapital());
        var producer = new DeterministicReportingCertifiedArtifactProducer(
            new DocumentsReportingPrimaryDocumentRenderer());

        var first = await producer.ProduceAsync(manifest);
        var second = await producer.ProduceAsync(manifest);

        var firstXlsx = first.Artifacts.Single(artifact => artifact.FileName == "run-pc-xlsx.xlsx").Content.ToArray();
        var secondXlsx = second.Artifacts.Single(artifact => artifact.FileName == "run-pc-xlsx.xlsx").Content.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(firstXlsx), ZipArchiveMode.Read))
        {
            archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
            using var reader = new StreamReader(
                archive.GetEntry("xl/sharedStrings.xml")!.Open(),
                Encoding.UTF8);
            var sharedStrings = await reader.ReadToEndAsync();
            sharedStrings.Should().Contain("Partner");
            sharedStrings.Should().Contain("LP One (investor-1)");
            sharedStrings.Should().Contain("860.00");
            sharedStrings.Should().Contain("Total");
        }

        firstXlsx.Should().Equal(secondXlsx, "partners-capital XLSX bytes must be deterministic for hash verification");
    }

    private static CertifiedPartnersCapitalProjection SamplePartnersCapital() => new(
        new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
        BeginningCapital: 1000m,
        Contributions: 500m,
        Distributions: 200m,
        AllocatedResult: 100m,
        OtherMovements: 0m,
        EndingCapital: 1400m,
        ReconciliationVariance: 0m,
        IsReconciled: true,
        Accounts: new[]
        {
            new CertifiedPartnersCapitalAccount("LP One", "investor-1", 600m, 300m, 100m, 60m, 0m, 860m, 0m),
            new CertifiedPartnersCapitalAccount("LP Two", "investor-2", 400m, 200m, 100m, 40m, 0m, 540m, 0m),
        });

    [Fact]
    public async Task ProduceAsync_PdfPaginatesEveryAccountSummaryWithinVisiblePageBounds()
    {
        const int accountCount = 120;
        const string finalAccountPrefix = "Account-119-FINAL-";
        var accountNames = Enumerable.Range(0, accountCount)
            .Select(index => index == accountCount - 1
                ? finalAccountPrefix + new string('X', 120)
                : $"Account-{index:D3}")
            .ToArray();
        var rows = accountNames
            .Select((account, index) => Row(
                ("account", account),
                ("debit", index.ToString()),
                ("credit", "0"),
                ("netAmount", index.ToString())))
            .Cast<IReadOnlyDictionary<string, string>>()
            .ToImmutableArray();

        var production = await ProduceAsync(
            "run-pdf-many-accounts",
            ReportingOutputFormatDto.Pdf,
            rows);
        var pdf = Encoding.ASCII.GetString(production.Artifacts
            .Single(artifact => artifact.FileName == "run-pdf-many-accounts.pdf")
            .Content.Span);

        var pagesRoot = Regex.Match(
            pdf,
            @"<< /Type /Pages /Kids \[(?<kids>[^\]]+)\] /Count (?<count>\d+) >>");
        pagesRoot.Success.Should().BeTrue();
        pagesRoot.Groups["count"].Value.Should().Be("3");
        Regex.Matches(pagesRoot.Groups["kids"].Value, @"\d+ 0 R").Count.Should().Be(3);
        Regex.Matches(
            pdf,
            @"(?m)^\d+ 0 obj\r?\n<< /Type /Page /Parent 2 0 R ").Count.Should().Be(3);

        var pageStreams = Regex.Matches(
            pdf,
            @"stream\r?\n(?<content>.*?)endstream",
            RegexOptions.Singleline);
        pageStreams.Count.Should().Be(3);
        foreach (Match pageStream in pageStreams)
        {
            Regex.Matches(pageStream.Groups["content"].Value, @"\) Tj T\*").Count
                .Should().BeLessThanOrEqualTo(48);
        }

        foreach (var account in accountNames.Take(accountCount - 1))
        {
            pdf.Should().Contain($"{account}:");
        }

        pdf.Should().Contain(finalAccountPrefix);
        pdf.Should().Contain("119 / 0 / 119");
    }

    [Fact]
    public async Task ProduceAsync_PdfRetainsEveryNonAccountCertifiedRowBeyondLegacyPreviewLimit()
    {
        const int rowCount = 65;
        var rows = Enumerable.Range(0, rowCount)
            .Select(index => Row(
                ("positionId", $"position-{index:D3}"),
                ("quantity", (1000 + index).ToString(CultureInfo.InvariantCulture))))
            .Cast<IReadOnlyDictionary<string, string>>()
            .ToImmutableArray();

        var production = await ProduceAsync(
            "run-pdf-non-account-complete",
            ReportingOutputFormatDto.Pdf,
            rows);
        var pdf = Encoding.ASCII.GetString(production.Artifacts
            .Single(artifact => artifact.FileName == "run-pdf-non-account-complete.pdf")
            .Content.Span);

        pdf.Should().Contain("Certified ledger rows: 65");
        pdf.Should().Contain("positionId=position-040");
        pdf.Should().Contain("positionId=position-064");
    }

    [Fact]
    public async Task ProduceAsync_PdfRejectsInvalidLedgerNumericDataInsteadOfCoercingItToZero()
    {
        var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            Row(
                ("account", "Cash"),
                ("debit", "not-a-decimal"),
                ("credit", "0"),
                ("netAmount", "0")));

        Func<Task> act = async () => await ProduceAsync(
            "run-pdf-invalid-decimal",
            ReportingOutputFormatDto.Pdf,
            rows);

        var exception = (await act.Should().ThrowAsync<ReportingGovernanceException>()).Which;
        exception.Message.Should().Contain("row 1")
            .And.Contain("debit")
            .And.Contain("cannot be coerced to zero")
            .And.Contain("recertify");
    }

    [Fact]
    public void ReconciliationEvidence_RejectsUndefinedTerminalDispositionEvenWithMatchingEvidenceHash()
    {
        var invalidBreak = ScopedBreak("invalid-disposition", "fund-a", StubAuthoritativeSource.BookId) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = (ReconciliationBreakDispositionDto)byte.MaxValue,
            DispositionReason = "Unknown disposition must not close a report gate.",
            ResolvedBy = "controller-a",
            BlockedOutputs = []
        };
        var exactBreak = ReportingReconciliationEvidenceValidation.CreateBreakEvidence(invalidBreak);
        var completion = new ReportingReconciliationCompletionEvidence(
            "completion-invalid-disposition",
            new string('a', 64),
            CapturedAt,
            HasOpenBreaks: false,
            ["reconciliation:invalid-disposition"],
            [exactBreak]);

        Action act = () => ReportingReconciliationEvidenceValidation.ValidateCompletion(completion);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*undefined disposition value*255*");
    }

    [Theory]
    [InlineData("Trésorerie", "U+00E9")]
    [InlineData("現金", "U+73FE")]
    public async Task ProduceAsync_PdfUnicodeAccountNamesFailBeforeLossyBytesAndIdentifyScalar(
        string accountName,
        string expectedScalar)
    {
        var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            Row(
                ("account", accountName),
                ("debit", "10"),
                ("credit", "0"),
                ("netAmount", "10")));
        ReportingGovernedArtifactProduction? production = null;
        Func<Task> act = async () =>
        {
            production = await ProduceAsync(
                "run-pdf-unicode",
                ReportingOutputFormatDto.Pdf,
                rows);
        };

        var exception = (await act.Should().ThrowAsync<ReportingGovernanceException>()).Which;

        production.Should().BeNull("the producer must fail before returning any lossy artifact bytes");
        exception.Message.Should().Contain(expectedScalar)
            .And.Contain("No lossy PDF bytes were emitted or retained")
            .And.Contain("XLSX, CSV, or Evidence Vault")
            .And.Contain("embedded Unicode PDF font");
    }

    [Fact]
    public async Task FileRunStore_RestartRejectsCertifiedRowMutationEvenWhenStorageChecksumsAreRehashed()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            Guid.NewGuid().ToString("N"),
            "reporting-row-binding");
        Directory.CreateDirectory(root);
        try
        {
            var template = Template(reportWriterGrid: false);
            var certified = await new ReportingRunCertificationService(
                    new StubAuthoritativeSource(Rows()),
                    new StubReconciliationSource())
                .CertifyAsync(
                    template,
                    Readiness("evaluation-row-binding", CapturedAt),
                    Access());
            var manifest = BuildManifest("run-row-binding", template, certified);
            var store = new FileReportingRunStore(
                new ReportingRunStoreOptions(root),
                NullLogger<FileReportingRunStore>.Instance);
            await store.SaveAsync(manifest, []);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            var snapshotPath = Path.Combine(root, "reporting-runs.json");
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
            var retainedRun = envelope["runs"]!.AsArray()[0]!.AsObject();
            var retainedManifest = retainedRun["manifest"]!.AsObject();
            retainedManifest["certifiedDatasetRows"]!.AsArray()[0]!.AsObject()["netAmount"] = "999.99";

            var rehydratedManifest = retainedManifest.Deserialize<ReportingOutputManifest>(options)!;
            retainedRun["certifiedDatasetHashSha256"] =
                FileReportingRunStore.ComputeCertifiedRowsHash(rehydratedManifest.CertifiedDatasetRows);
            retainedRun["manifestHashSha256"] = ComputeSha256(
                JsonSerializer.Serialize(rehydratedManifest, options));
            var rehydratedRuns = envelope["runs"]!
                .Deserialize<IReadOnlyList<ReportingRunSnapshot>>(options)!;
            envelope["payloadHashSha256"] = ComputeSha256(
                JsonSerializer.Serialize(rehydratedRuns, options));
            await File.WriteAllTextAsync(snapshotPath, envelope.ToJsonString(options));

            var restarted = new FileReportingRunStore(
                new ReportingRunStoreOptions(root),
                NullLogger<FileReportingRunStore>.Instance);
            var load = () => restarted.ListRuns();

            var exception = load.Should().Throw<ReportingStateCorruptionException>().Which;
            exception.StatePath.Should().Be(snapshotPath);
            exception.InnerException!.Message.Should().Contain("snapshot hash");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ReportingGovernedArtifactProduction> ProduceAsync(
        string runId,
        ReportingOutputFormatDto outputFormat,
        ImmutableArray<IReadOnlyDictionary<string, string>> certifiedRows = default)
        => await new DeterministicReportingCertifiedArtifactProducer()
            .ProduceAsync(await BuildCertifiedManifestAsync(runId, outputFormat, certifiedRows));

    private static async Task<ReportingGovernedArtifactProduction> ProduceClientGradeAsync(
        string runId,
        ReportingOutputFormatDto outputFormat,
        ImmutableArray<IReadOnlyDictionary<string, string>> certifiedRows = default)
        => await new DeterministicReportingCertifiedArtifactProducer(
                new DocumentsReportingPrimaryDocumentRenderer())
            .ProduceAsync(await BuildCertifiedManifestAsync(runId, outputFormat, certifiedRows));

    private static async Task<ReportingOutputManifest> BuildCertifiedManifestAsync(
        string runId,
        ReportingOutputFormatDto outputFormat,
        ImmutableArray<IReadOnlyDictionary<string, string>> certifiedRows = default,
        ReportingTemplateMetadata? selectedTemplate = null,
        LedgerFinancialReportPack? certifiedLedgerReportPack = null,
        CertifiedPartnersCapitalProjection? partnersCapital = null)
    {
        var rows = certifiedRows.IsDefault ? Rows() : certifiedRows;
        var template = selectedTemplate ?? Template(reportWriterGrid: false);
        var certified = await new ReportingRunCertificationService(
                new StubAuthoritativeSource(rows, certifiedLedgerReportPack),
                new StubReconciliationSource())
            .CertifyAsync(
                template,
                Readiness(
                    "evaluation-artifact",
                    CapturedAt,
                    outputFormat,
                    new VersionedReportTemplateIdDto(
                        template.TemplateId,
                        int.Parse(
                            template.Version.Split('.', 2, StringSplitOptions.TrimEntries)[0],
                            CultureInfo.InvariantCulture))),
                Access());
        return BuildManifest(runId, template, certified, partnersCapital);
    }

    private static ReportingOutputManifest BuildManifest(
        string runId,
        ReportingTemplateMetadata template,
        CertifiedReportingRunContext certified,
        CertifiedPartnersCapitalProjection? partnersCapital = null)
    {
        var declarations = ReportingArtifactDeclaration.Build(
            runId,
            template,
            certified.Readiness.ResolvedParameters,
            includeCertifiedSourceSchedule: true);
        return new ReportingOutputManifest(
            runId,
            template.TemplateId,
            certified.Readiness.ResolvedParameters.AsOfDate,
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            declarations.Select(static declaration => declaration.ArtifactId).ToImmutableArray(),
            AttemptCount: 1,
            Trigger: ReportingRunTrigger.AdHoc,
            RunSeriesId: runId,
            ResolvedTemplate: certified.Readiness.ResolvedTemplate,
            ResolvedParameters: certified.Readiness.ResolvedParameters,
            Readiness: certified.Readiness,
            OperationalScope: certified.OperationalScope,
            ImmutableAccessScope: certified.AccessScope,
            CertifiedSnapshot: certified.Snapshot,
            AuthoritativeSource: certified.AuthoritativeSource,
            CertifiedDatasetRows: certified.DatasetRows,
            CertifiedPartnersCapital: partnersCapital);
    }

    private static ReconciliationBreakQueueItem ResolvedScopedBreak(
        string breakId,
        ReportingAuthoritativeSourceCheckpoint source,
        DateOnly asOfDate) =>
        ScopedBreak(breakId, source.FundId, StubAuthoritativeSource.BookId) with
        {
            TenantId = source.TenantId,
            CompanyId = source.CompanyId,
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "The exact value difference was corrected.",
            ResolvedBy = "reconciliation-operator",
            ResolvedAt = CapturedAt,
            DispositionEvidenceHash = new string('d', 64),
            DisposedAt = CapturedAt,
            Version = 2,
            AccountingPeriodId = StubAuthoritativeSource.PeriodId.ToString("D"),
            AsOfDate = asOfDate
        };

    private static ReportingReconciliationEvidenceReceipt ClosedReceipt(
        ReportingAuthoritativeSourceCheckpoint source,
        ReconciliationBreakQueueItem resolved,
        string completionCheckpointId)
    {
        var closeBreakEvidence = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
            [resolved],
            source.FundId,
            StubAuthoritativeSource.BookId,
            StubAuthoritativeSource.PeriodId,
            source.AsOfDate,
            expectedOpenBreakCount: 0);
        return ReportingReconciliationEvidenceValidation.CreateReceipt(
            source,
            new ReportingReconciliationCompletionEvidence(
                completionCheckpointId,
                new string('c', 64),
                CapturedAt,
                HasOpenBreaks: false,
                ["period-close:hard-closed"],
                closeBreakEvidence));
    }

    private static ReconciliationBreakQueueItem ScopedBreak(string breakId, string fundId, Guid bookId) => new(
        BreakId: breakId,
        RunId: "reconciliation-run",
        StrategyName: "Fund reconciliation",
        Category: ReconciliationBreakCategory.AmountMismatch,
        Status: ReconciliationBreakQueueStatus.Open,
        Variance: -10m,
        Reason: "Scoped mismatch",
        AssignedTo: null,
        DetectedAt: CapturedAt,
        LastUpdatedAt: CapturedAt,
        FundAccountId: fundId,
        EvidenceLinks: [$"evidence:{breakId}"],
        SourceFingerprint: new string('c', 64),
        LedgerBookId: bookId,
        Measures:
        [
            new(ReconciliationBreakMeasureKindDto.Value, 100m, 90m, -10m, 1m, "USD"),
            new(ReconciliationBreakMeasureKindDto.Quantity, null, null, null, null, "units", "Quantity was not supplied."),
            new(ReconciliationBreakMeasureKindDto.CostBasis, null, null, null, null, "USD", "Cost basis was not supplied.")
        ],
        BlockedOutputs: ["FinalReport", "PeriodClose"],
        AccountingPeriodId: AccountingPeriodId.ToString("D"),
        AsOfDate: ReportingAsOfDate)
    {
        FundProfileId = fundId
    };

    private static ReportingTemplateMetadata Template(bool reportWriterGrid = true) => new(
        "test-report",
        ReportingTemplateFamily.CustomReport,
        "Test report",
        "2.0.0",
        ["detail"],
        ImmutableDictionary<string, string>.Empty,
        ReportWriterGrids: reportWriterGrid
            ?
            [
                new ReportWriterGridDefinitionDto(
                    "detail",
                    "Detail",
                    ReportWriterGridKindDto.Detail,
                    RowFields: ["account"],
                    Metrics:
                    [
                        new ReportWriterMetricDefinitionDto(
                            "amount",
                            "amount",
                            ReportWriterAggregateFunctionDto.Sum)
                    ])
            ]
            : [],
        AccessPolicy: new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: "company-a"));

    private static ReportingTemplateMetadata CapitalAccountTemplate() => new(
        "capital-account-statement",
        ReportingTemplateFamily.CapitalAccountStatement,
        "Capital Account Statement",
        "1.0.0",
        ["cover", "capital-balance", "flows", "allocation"],
        ImmutableDictionary<string, string>.Empty,
        ReportWriterGrids: [],
        AccessPolicy: new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: "company-a"));

    private static ReportingRunReadinessDto Readiness(
        string evaluationId,
        DateTimeOffset evaluatedAt,
        ReportingOutputFormatDto outputFormat = ReportingOutputFormatDto.Pdf,
        VersionedReportTemplateIdDto? resolvedTemplate = null) =>
        new(
            evaluationId,
            evaluatedAt,
            resolvedTemplate ?? new VersionedReportTemplateIdDto("test-report", 2),
            Parameters(outputFormat),
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
                    EvidenceReferences: ["source:checkpoint-a"])
            ],
            BlockingReasons: [],
            EvidenceHash: new string('a', 64));

    private static ReportingRunParametersDto Parameters(ReportingOutputFormatDto outputFormat) =>
        new(
            new ReportingRunScopeDto("fund-a"),
            "2026-06",
            new DateOnly(2026, 6, 30),
            new ReportingLedgerBookSelectionDto(StubAuthoritativeSource.BookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            outputFormat,
            ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);

    private static ReportAccessQueryContext Access() => new(
        "operator-a",
        ["report-reviewers"],
        "company-a",
        TenantId: "tenant-a",
        RequireBoundScope: true);

    private static ImmutableArray<IReadOnlyDictionary<string, string>> Rows() =>
    [
        Row(
            ("account", "Cash"),
            ("debit", "123.45"),
            ("credit", "0"),
            ("netAmount", "123.45"),
            ("formulaText", "=2+3"),
            ("negativeAmount", "-12.5"),
            ("entryId", "11111111-1111-1111-1111-111111111111")),
        Row(
            ("account", "Payable"),
            ("debit", "0"),
            ("credit", "23.45"),
            ("netAmount", "-23.45"),
            ("formulaText", "+SUM(A1:A2)"),
            ("negativeAmount", "-1"),
            ("entryId", "22222222-2222-2222-2222-222222222222"))
    ];

    private static ImmutableArray<IReadOnlyDictionary<string, string>> CapitalAccountRows() =>
    [
        Row(
            ("account", "Cash"),
            ("accountType", "Asset"),
            ("debit", "250000"),
            ("credit", "0"),
            ("netAmount", "250000"),
            ("entryId", "11111111-1111-1111-1111-111111111111")),
        Row(
            ("account", "Investor Capital - investor-a"),
            ("accountType", "Equity"),
            ("debit", "0"),
            ("credit", "250000"),
            ("netAmount", "-250000"),
            ("investorId", "investor-a"),
            ("capitalAccountId", "capital-account-a"),
            ("entryId", "22222222-2222-2222-2222-222222222222"))
    ];

    private static LedgerFinancialReportPack BuildCanonicalCapitalReportPack()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "opening capital",
            [
                (LedgerAccounts.Cash, 1_000_000m, 0m),
                (LedgerAccounts.InvestorCapitalFor("investor-a"), 0m, 1_000_000m),
            ]);
        ledger.PostLines(
            new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero),
            "period capital contribution",
            [
                (LedgerAccounts.Cash, 250_000m, 0m),
                (LedgerAccounts.InvestorCapitalFor("investor-a"), 0m, 250_000m),
            ]);
        var request = new LedgerReportPackRequest(
            "capital-account-report",
            "fund-a",
            StubAuthoritativeSource.PeriodId.ToString("D"),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            "USD",
            "controller",
            CapturedAt);
        return LedgerReportPackBuilder.Build(ledger, request);
    }

    private static ReportingCertifiedLedgerPresentationInput BindCanonicalPresentation(
        ReportingOutputManifest manifest,
        LedgerFinancialReportPack reportPack) =>
        new(
            manifest.AuthoritativeSource!.CheckpointId,
            manifest.AuthoritativeSource.CheckpointHash,
            DeterministicReportingCertifiedArtifactProducer.ComputeCertifiedRowsHash(
                manifest.CertifiedDatasetRows),
            reportPack);

    private static ReportingOutputManifest RemoveCertifiedLedgerPresentationEvidence(
        ReportingOutputManifest manifest)
    {
        var source = manifest.AuthoritativeSource!;
        return manifest with
        {
            AuthoritativeSource = source with
            {
                EvidenceIds = source.EvidenceIds
                    .Where(static evidence =>
                        !evidence.StartsWith("ledger-report-pack:", StringComparison.Ordinal))
                    .ToImmutableArray()
            }
        };
    }

    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class StubAuthoritativeSource : IReportingAuthoritativeSource
    {
        public static readonly Guid BookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid PeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private readonly ImmutableArray<IReadOnlyDictionary<string, string>> _rows;
        private readonly LedgerFinancialReportPack? _certifiedLedgerReportPack;

        public StubAuthoritativeSource(
            ImmutableArray<IReadOnlyDictionary<string, string>> rows,
            LedgerFinancialReportPack? certifiedLedgerReportPack = null)
        {
            _rows = rows;
            _certifiedLedgerReportPack = certifiedLedgerReportPack;
        }

        public int CaptureCount { get; private set; }
        public bool OmitCertifiedLedgerPresentationEvidence { get; set; }
        public string? CertifiedLedgerPresentationEvidenceOverride { get; set; }

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureCount++;
            var hash = new string('b', 64);
            var checkpointId = "ledger-checkpoint-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var evidence = ImmutableArray.CreateBuilder<string>();
            evidence.Add($"reporting-source-checkpoint:{checkpointId}:{hash}");
            evidence.Add("ledger-sequence:42");
            if (_certifiedLedgerReportPack is not null
                && !OmitCertifiedLedgerPresentationEvidence)
            {
                evidence.Add(CertifiedLedgerPresentationEvidenceOverride
                    ?? $"ledger-report-pack:{_certifiedLedgerReportPack.Request.ReportId}:{_certifiedLedgerReportPack.Signature.PayloadChecksumSha256}");
            }
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{BookId:D}:{PeriodId:D}",
                "tenant-a",
                "55555555-5555-5555-5555-555555555555",
                "company-a",
                "fund-a",
                BookId.ToString("D"),
                PeriodId.ToString("D"),
                "Gaap",
                parameters.AsOfDate,
                new DateTimeOffset(
                    parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                HighestGlobalSequence: 42,
                JournalEntryCount: 2,
                LedgerLineCount: _rows.Length,
                checkpointId,
                hash,
                CapturedAt,
                evidence.ToImmutable());
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, _rows));
        }
    }

    private sealed class StubCertifiedLedgerPresentationSource(
        ReportingCertifiedLedgerPresentationInput? input,
        bool throwOnResolve = false) :
        IReportingCertifiedLedgerPresentationSource
    {
        public int ResolveCount { get; private set; }

        public ValueTask<ReportingCertifiedLedgerPresentationInput?> ResolveExactAsync(
            ReportingOutputManifest manifest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCount++;
            if (throwOnResolve)
            {
                throw new InvalidOperationException(
                    "The complete-history presentation source must not be consulted.");
            }
            return ValueTask.FromResult(input);
        }
    }

    private sealed class StubReconciliationSource : IReportingReconciliationEvidenceSource
    {
        public ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
            ReportingRunParametersDto parameters,
            ReportingAuthoritativeSourceCheckpoint source,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReportingReconciliationEvidenceValidation.CreateReceipt(
                source,
                new ReportingReconciliationCompletionEvidence(
                    "hard-close-44444444444444444444444444444444-v7",
                    new string('c', 64),
                    CapturedAt,
                    HasOpenBreaks: false,
                    ["ledger-period:44444444-4444-4444-4444-444444444444:hard-closed"],
                    CloseWorkflowCompletion: new ReportingCloseWorkflowCompletionEvidence(
                        "66666666-6666-6666-6666-666666666666",
                        7,
                        "77777777-7777-7777-7777-777777777777",
                        source.LedgerBookId,
                        source.AccountingPeriodId,
                        "close-approval-1",
                        new string('d', 64),
                        new string('e', 64),
                        "close-package-1",
                        new string('f', 64),
                        "88888888-8888-8888-8888-888888888888",
                        new string('a', 64)))));
        }
    }

    private sealed class EmptyReconciliationStore : IReportingReconciliationEvidenceStore
    {
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
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReportingReconciliationEvidenceReceipt?>(null);
    }

    private sealed class ReturningReconciliationStore(ReportingReconciliationEvidenceReceipt receipt) :
        IReportingReconciliationEvidenceStore
    {
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
            return ValueTask.FromResult<ReportingReconciliationEvidenceReceipt?>(receipt);
        }
    }
}
