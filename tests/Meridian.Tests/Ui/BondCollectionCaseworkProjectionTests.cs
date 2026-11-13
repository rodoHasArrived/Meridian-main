using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class BondCollectionCaseworkProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-bond-collection-cases", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Start = new(2026, 11, 12);
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Security = Guid.Parse("b75fe802-aacd-4837-a11f-7a0dc96f2d41");
    private static readonly AccountCashForecastScopeDto Scope = new("tenant", "company", "account",
        "4c9cfc2e-eaa2-4976-8f64-af372bbd7713", "USD");
    private static readonly ReconciliationBreakQueueScope Access = new("tenant", "company");

    [Fact]
    public async Task CapturedPartialCollectionCreatesIdempotentScopedPersistentCase()
    {
        var monitor = Monitoring();
        var queue = Queue();
        var projection = new BondCollectionCaseworkProjection(queue, monitor);
        var snapshot = await monitor.CaptureAsync(Request("partial", 100m, [Payment("cash-one", 40m)], [Allocation("one", "cash-one", 40m)]));
        var ids = await projection.ProjectAsync(snapshot, "operator");
        (await projection.ProjectAsync(snapshot, "operator")).Should().Equal(ids);
        var item = (await Queue().GetAllAsync(Access)).Should().ContainSingle().Which;
        item.Variance.Should().Be(60m);
        item.SourceImportId.Should().Be(snapshot.SnapshotId);
        item.ExternalAccountId.Should().Be(Scope.AccountId);
        item.AssignedTo.Should().Be("operator");
        item.EvidenceLinks.Should().Contain($"bond-monitoring://snapshot/{snapshot.SnapshotId}");
        (await queue.GetAllAsync(new ReconciliationBreakQueueScope("other", "company"))).Should().BeEmpty();
        (await queue.GetAuditHistoryAsync(Access, item.BreakId)).Should().Contain(row => row.EventType == "CaseCreated");
    }

    [Fact]
    public async Task SourceClearingAddsEvidenceAndRecurrenceCreatesLinkedCaseWithoutOverwritingResolution()
    {
        var monitor = Monitoring();
        var queue = Queue();
        var projection = new BondCollectionCaseworkProjection(queue, monitor);
        var first = await monitor.CaptureAsync(Request("partial", 100m, [Payment("cash-one", 40m)], [Allocation("one", "cash-one", 40m)]));
        var firstCaseId = (await projection.ProjectAsync(first, "operator")).Single();
        var cleared = await monitor.CaptureAsync(Request("cleared", 100m,
            [Payment("cash-one", 40m), Payment("cash-two", 60m)],
            [Allocation("one", "cash-one", 40m), Allocation("two", "cash-two", 60m)]) with
        { PreviousSnapshotId = first.SnapshotId, CorrectionRationale = "Retain second receipt.", RecordedAtUtc = Now.AddMinutes(1) });
        (await projection.ProjectAsync(cleared, "operator")).Should().BeEmpty();
        await projection.ProjectAsync(cleared, "operator");
        var sourceCleared = (await queue.GetByIdAsync(Access, firstCaseId))!;
        sourceCleared.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        sourceCleared.Comments.Should().ContainSingle();
        sourceCleared.EvidenceLinks.Should().Contain($"bond-monitoring://snapshot/{cleared.SnapshotId}");
        var investigation = await queue.ApplyCaseworkCommandAsync(Access, new(firstCaseId,
            ReconciliationCaseworkAction.TransitionStatus, "operator", "investigate", "review", "workstation", sourceCleared.Version,
            Status: ReconciliationCaseLifecycleState.Investigating));
        investigation.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var resolution = await queue.ApplyCaseworkCommandAsync(Access, new(firstCaseId,
            ReconciliationCaseworkAction.Resolve, "operator", "resolve", "review", "workstation", investigation.Item!.Version,
            Reason: "Both independent receipts were reviewed.", RootCauseCode: "BrokerCashTiming", ResolutionCode: "EvidenceAccepted",
            EvidenceLinks: ["evidence:accepted-second-receipt"]));
        resolution.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        var recurring = await monitor.CaptureAsync(Request("revised-expectation", 110m,
            [Payment("cash-one", 40m), Payment("cash-two", 60m)],
            [Allocation("one", "cash-one", 40m), Allocation("two", "cash-two", 60m)]) with
        { PreviousSnapshotId = cleared.SnapshotId, CorrectionRationale = "Retain corrected obligation.", RecordedAtUtc = Now.AddMinutes(2) });
        var recurringId = (await projection.ProjectAsync(recurring, "operator")).Should().ContainSingle().Which;
        recurringId.Should().NotBe(firstCaseId);
        var previous = (await queue.GetByIdAsync(Access, firstCaseId))!;
        previous.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
        previous.ResolvedBy.Should().Be("operator");
        var next = (await queue.GetByIdAsync(Access, recurringId))!;
        next.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        next.EvidenceLinks.Should().Contain($"reconciliation-case:{firstCaseId}");
        next.ExplainabilitySummary.Should().Contain("recurs");
    }

    [Fact]
    public async Task UnmatchedReceiptCreatesCaseButUnretainedSnapshotsCannotCreateCasework()
    {
        var monitor = Monitoring();
        var queue = Queue();
        var projection = new BondCollectionCaseworkProjection(queue, monitor);
        var raw = BondMonitoringEngine.Build(Request("unmatched", 100m, [Payment("unknown-cash", 12m)], []));
        var unretained = () => projection.ProjectAsync(raw, "operator");
        await unretained.Should().ThrowAsync<InvalidOperationException>();
        var retained = await monitor.CaptureAsync(raw.Inputs);
        var ids = await projection.ProjectAsync(retained, "operator");
        ids.Should().HaveCount(2);
        (await queue.GetAllAsync(Access)).Should().Contain(row => row.SourceBreakId == "cash:unknown-cash");
    }

    private BondMonitoringService Monitoring() => new(new BondMonitoringFileStore(Path.Combine(_root, "monitoring")));
    private FileReconciliationBreakQueueRepository Queue() => new(Path.Combine(_root, "queue"), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
    private static BondMonitoringCaptureRequest Request(string run, decimal expected,
        IReadOnlyList<AccountCashFlowDto> payments, IReadOnlyList<AccountCashPaymentAllocationDto> allocations) =>
        new(run, Scope, Now, [new("coupon", Security, "Coupon", Start.AddDays(-1), expected, "USD", "synthetic-verified",
            [new("Terms", "terms", ContentSha256: Sha256Digest.ComputeUtf8("terms"))])], payments, allocations, [], []);
    private static AccountCashPaymentAllocationDto Allocation(string id, string cash, decimal amount) => new(id, "coupon", cash, amount, amount);
    private static AccountCashFlowDto Payment(string id, decimal amount)
    {
        var hash = Sha256Digest.ComputeUtf8(id);
        var source = new AccountCashProvenanceDto("synthetic", id, hash, AccountCashEvidenceRole.Observation,
            new(id, $"https://evidence.example.test/{id}", hash, "synthetic", id, "Accepted", "reviewer",
                Now.AddHours(-2), Start, 1, Now.AddHours(-1), "retainer", "AccountCash", Scope.AccountId));
        return new(id, id, Scope.AccountId, Scope.BookId, AccountCashFlowStatus.Actual, "Coupon", amount, "USD", Start, Start, Start, source, Security);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
