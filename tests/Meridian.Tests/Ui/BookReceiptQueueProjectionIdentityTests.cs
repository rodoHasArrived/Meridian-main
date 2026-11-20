using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class BookReceiptQueueProjectionIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-receipt-projection-identity-" + Guid.NewGuid().ToString("N"));
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PositionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OldFlowId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid NewFlowId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly DueDate = new(2026, 10, 1);
    private static readonly BookReceiptScopeDto Scope = new("tenant-a", "company-a",
        Guid.Parse("55555555-5555-5555-5555-555555555555"));
    private static readonly BookReceiptQueryDto Query = new(Scope, DueDate.AddDays(1));
    private const string ObligationKey = "position-a-coupon";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_projection_identity_keeps_receipt_and_reversed_journal_linked_to_the_same_exception(
        bool currentProjectionMissing)
    {
        var original = Expectation();
        var source = new MutableSource(new([original], [], [], CompleteCoverage()));
        var repository = Repository();
        var service = Service(source, repository);
        var overdue = await service.RunAsync(Query);
        var exceptionId = overdue.Items.Single().ExceptionId!;
        exceptionId.Should().NotBeNullOrWhiteSpace();
        var marker = Marker(OldFlowId);
        source.Snapshot = source.Snapshot with
        {
            Receipts = [Receipt(marker)],
            Postings = [Posting(marker)]
        };

        var paid = await service.RunAsync(Query with { AsOfDate = Query.AsOfDate.AddDays(1) });

        paid.Items.Single().Status.Should().Be("Reconciled");
        paid.Items.Single().ReceivedAmount.Should().Be(100m);
        paid.Items.Single().PostedAmount.Should().Be(100m);
        paid.Items.Single().Receipts.Single().Allocations.Should().ContainSingle(allocation => allocation.ObligationKey == ObligationKey);
        paid.Items.Single().Postings.Should().ContainSingle(posting => posting.ObligationKey == ObligationKey);
        var closed = (await repository.GetByIdAsync(exceptionId))!;
        await repository.SaveAsync(closed with
        {
            AssignedTo = "cash-owner",
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            ResolvedBy = "controller",
            ResolvedAt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
            ResolutionNote = "Receipt and posted journal verified."
        });
        source.Snapshot = source.Snapshot with
        {
            Expectations = currentProjectionMissing ? [] : [original with
            {
                ProjectedCashFlowId = NewFlowId,
                TermsVersionId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                TermsHash = new string('b', 64),
                ProjectionRunId = Guid.Parse("77777777-7777-7777-7777-777777777777")
            }],
            Postings = [Posting(marker) with { IsReversed = true }],
            Coverage = currentProjectionMissing
                ? CompleteCoverage().Select(coverage => coverage.Area == "Projections"
                    ? coverage with { IsComplete = false, Reason = "Current projection provider unavailable." }
                    : coverage).ToArray()
                : CompleteCoverage()
        };

        var reversed = await Service(source, Repository()).RunAsync(Query with { AsOfDate = Query.AsOfDate.AddDays(2) });

        var item = reversed.Items.Single();
        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.Status.Should().Be("ReceivedButUnposted");
        item.ExceptionId.Should().Be(exceptionId);
        item.ExpectationHistory.Should().HaveCount(currentProjectionMissing ? 1 : 2);
        item.Postings.Should().ContainSingle(posting => posting.ObligationKey == ObligationKey && posting.IsReversed);
        reversed.SourceSnapshot.Postings.Should().ContainSingle(posting => posting.ObligationKey == marker && posting.IsReversed);
        reversed.SourceSnapshot.Receipts.Single().Allocations.Should().ContainSingle(allocation => allocation.ObligationKey == marker);
        var reopened = (await Repository().GetByIdAsync(exceptionId))!;
        reopened.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        reopened.AssignedTo.Should().Be("cash-owner");
        (await Repository().GetAuditHistoryAsync(exceptionId)).Should()
            .ContainSingle(entry => entry.EventType == "ReceiptReversalReopened");
    }

    [Fact]
    public async Task An_unknown_projection_identity_cannot_fall_back_to_the_only_matching_obligation()
    {
        var marker = Marker(NewFlowId);
        var source = new MutableSource(new([Expectation()], [Receipt(marker)], [Posting(marker)], CompleteCoverage()));

        var run = await Service(source).RunAsync(Query);

        var item = run.Items.Single();
        item.ReceivedAmount.Should().Be(0m);
        item.PostedAmount.Should().Be(0m);
        item.RemainingAmount.Should().Be(100m);
        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().NotBeEmpty();
        run.SourceSnapshot.Receipts.Single().Allocations.Should().ContainSingle(allocation => allocation.ObligationKey == marker);
    }

    [Fact]
    public async Task A_projection_identity_shared_by_two_positions_cannot_choose_one_obligation()
    {
        var first = Expectation();
        var secondPositionId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var secondEvidence = first.RetainedEvidence.Single() with
        {
            EvidenceId = "accepted-position-b",
            EvidenceUri = "evidence://holdings/position-b",
            SourceReference = "position-b",
            SubjectId = secondPositionId.ToString("D")
        };
        var second = first with
        {
            ObligationKey = "position-b-coupon",
            PositionId = secondPositionId,
            RetainedEvidence = [secondEvidence],
            EvidenceLinks = [secondEvidence.EvidenceUri]
        };
        var marker = Marker(OldFlowId);
        var source = new MutableSource(new([first, second], [Receipt(marker)], [Posting(marker)], CompleteCoverage()));

        var run = await Service(source).RunAsync(Query);

        run.Items.Should().HaveCount(2);
        run.Items.Sum(item => item.ReceivedAmount).Should().Be(0m);
        run.Items.Sum(item => item.PostedAmount).Should().Be(0m);
        run.Items.Sum(item => item.RemainingAmount).Should().Be(200m);
        run.Items.Should().OnlyContain(item => !item.EvidenceComplete && item.EvidenceIssues.Count > 0);
    }

    private BookReceiptQueueService Service(MutableSource source, IReconciliationBreakQueueRepository? repository = null)
        => new(source, new FileBookReceiptRunStore(_root), repository);

    private FileReconciliationBreakQueueRepository Repository()
        => new(_root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);

    private static string Marker(Guid flowId) => $"projected-cash-flow:{flowId:D}";

    private static BookReceiptDto Receipt(string marker)
        => new("payment", SecurityId, DueDate, "USD", 100m, "custodian", "bank-transaction",
            [new(marker, 100m)], null, ["evidence://receipts/payment"]);

    private static BookReceiptPostingDto Posting(string marker)
        => new("posting", "payment", marker, 100m, "USD", "journal", DueDate, false,
            ["ledger://journal/posted-cash"], JournalCashAmount: 100m);

    private static BookReceiptExpectationDto Expectation()
    {
        var observedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var evidence = new RetainedEvidenceIdentityDto("accepted-position", "evidence://holdings/position-a",
            new string('a', 64), "custodian", "position-a", "Accepted", "reviewer", observedAt,
            DueDate.AddDays(-1), 1, observedAt, "evidence-ingestion", "InstrumentPosition", PositionId.ToString("D"));
        return new(ObligationKey, PositionId, SecurityId, "Treasury coupon", "Coupon", DueDate, "USD", 1000m, 100m,
            Guid.Parse("99999999-9999-9999-9999-999999999999"), new string('a', 64),
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), OldFlowId, [evidence.EvidenceUri], [evidence]);
    }

    private static IReadOnlyList<BookReceiptCoverageDto> CompleteCoverage()
        => [new("Holdings", true), new("Projections", true), new("Receipts", true), new("Postings", true)];

    private sealed class MutableSource(BookReceiptSourceSnapshotDto snapshot) : IBookReceiptSource
    {
        public BookReceiptSourceSnapshotDto Snapshot { get; set; } = snapshot;
        public Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query, CancellationToken ct = default)
            => Task.FromResult(Snapshot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
