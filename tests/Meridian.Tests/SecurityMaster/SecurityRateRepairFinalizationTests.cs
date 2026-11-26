using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Moq;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityRateRepairFinalizationTests
{
    [Theory]
    [InlineData(SecurityMasterRevisionStateDto.Approved)]
    [InlineData(SecurityMasterRevisionStateDto.Submitted)]
    [InlineData(SecurityMasterRevisionStateDto.Published)]
    public async Task Publish_HoldsAuthorityThroughCommit_AndPreservesInapplicableOutcomes(SecurityMasterRevisionStateDto initialState)
    {
        var events = new List<string>();
        var batchId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        var request = new CreateSecurityRateRepairBatchRequest(batchId,
            new(Guid.NewGuid(), bookId, "fund", periodId.ToString("D")),
            new("vendor", "assetSpecificTerms.couponRate", "v1", "AnnualRate"), [new(securityId, 7)],
            "Percent", now, "retained-evidence", "Unit reviewed", true);
        var manifest = new SecurityRateRepairManifest("tenant", "company", "maker", now,
            new string('a', 64), new string('b', 64), request, [new(securityId, 7, "CD", "CertificateOfDeposit", "5.25")]);
        var priorError = initialState == SecurityMasterRevisionStateDto.Submitted ? "ImpactBookCoverageUnavailable" : null;
        var progress = new SecurityRateRepairProgress(securityId, initialState.ToString(), workflowId, revisionId, "impact", "approval",
            ErrorCode: priorError, Error: priorError is null ? null : "Approval was refused because book coverage was unavailable.");
        var batch = new SecurityRateRepairRetainedBatch(manifest, 1, [progress]);
        var revision = new SecurityMasterRevisionRecord(revisionId, securityId, initialState,
            "checker", now, now, workflowId)
        { DraftedBy = "maker", RepairBatchId = batchId };
        var held = false;
        var store = new Mock<ISecurityRateRepairStore>();
        store.Setup(s => s.GetAsync(batchId, "tenant", It.IsAny<CancellationToken>())).ReturnsAsync(() => batch);
        store.Setup(s => s.AcquireCommandLeaseAsync(batchId, "tenant", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lease(() => { }));
        store.Setup(s => s.ExecuteItemAsync(batchId, "tenant", securityId,
                It.IsAny<Func<SecurityRateRepairProgress, Task<SecurityRateRepairProgress>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, string _, Guid _, Func<SecurityRateRepairProgress, Task<SecurityRateRepairProgress>> operation, CancellationToken _) =>
            {
                held.Should().BeTrue("ledger authority must precede the Security Master mutation");
                events.Add("item-begin");
                var prior = progress;
                progress = await operation(progress);
                await Task.Yield();
                held.Should().BeTrue("the workbench returning does not yet commit the enclosing item mutation");
                if (progress != prior)
                    batch = batch with { Version = 2, Items = [progress] };
                events.Add("item-committed");
            });
        var impacts = new Mock<ISecurityMasterImpactPreviewService>();
        impacts.Setup(i => i.AcquireFinalizationLeaseAsync(It.IsAny<SecurityMasterRevisionRecord>(),
                It.IsAny<SecurityMasterImpactScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                held = true;
                events.Add("ledger-acquired");
                return new Lease(() => { held = false; events.Add("ledger-released"); });
            });
        var revisions = new Mock<ISecurityMasterRevisionStore>();
        revisions.Setup(s => s.GetAsync(revisionId, It.IsAny<CancellationToken>())).ReturnsAsync(() => revision);
        var workbench = new Mock<ISecurityMasterWorkbenchCommandService>();
        workbench.Setup(w => w.PublishRevisionAsync(It.IsAny<PublishSecurityMasterRevisionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                held.Should().BeTrue();
                events.Add("published");
                revision = revision with { State = SecurityMasterRevisionStateDto.Published };
                return new SecurityMasterPublishResultDto(securityId, revisionId, 8, false, [], []);
            });
        var evidence = new Mock<ISecurityRateRepairEvidenceResolver>();
        evidence.Setup(e => e.ResolveAsync(request.EvidenceId, "tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRateRepairEvidence(request.EvidenceId, manifest.EvidenceSha256));
        var ledger = new Mock<ILedgerJournalStore>();
        ledger.Setup(l => l.GetLedgerBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerBookRecord(bookId, "fund", Guid.NewGuid(), (FundStructureNodeKindDto)0, "Book", "USD", now, now));
        ledger.Setup(l => l.GetPeriodAsync(periodId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerAccountingPeriod(periodId, bookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", now, null, 1));
        var service = new SecurityRateRepairService(store.Object, Mock.Of<ISecurityMasterQueryService>(),
            Mock.Of<ISecurityMasterStore>(), revisions.Object, workbench.Object, Mock.Of<IOperationsContinuityWorkflowService>(),
            evidence.Object, impacts.Object, ledger.Object);

        var result = await service.ExecuteAsync(batchId, "Publish", new(1), new("tenant", "company", "publisher", true));

        result.Items.Single().State.Should().Be(initialState == SecurityMasterRevisionStateDto.Submitted ? "Submitted" : "Published");
        result.Items.Single().ErrorCode.Should().Be(priorError);
        result.Version.Should().Be(initialState == SecurityMasterRevisionStateDto.Approved ? 2 : 1,
            "unchanged outcomes retain their batch version, including exact completed retries");
        if (initialState == SecurityMasterRevisionStateDto.Approved)
            events.Should().Equal("ledger-acquired", "item-begin", "published", "item-committed", "ledger-released");
        else
        {
            events.Should().Equal("ledger-acquired", "item-begin", "item-committed", "ledger-released");
            workbench.Verify(workbench => workbench.PublishRevisionAsync(It.IsAny<PublishSecurityMasterRevisionRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        held.Should().BeFalse();
    }

    private sealed class Lease(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }
}
