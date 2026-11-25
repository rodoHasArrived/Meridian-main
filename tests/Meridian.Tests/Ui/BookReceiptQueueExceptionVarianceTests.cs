using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class BookReceiptQueueExceptionVarianceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-receipt-variance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Fully_received_but_unposted_cash_creates_a_received_to_posted_value_break()
    {
        var exception = await CaptureExceptionAsync(received: 100m, posted: 0m);

        exception.Variance.Should().Be(-100m);
        var measure = exception.Measures.Should().ContainSingle().Subject;
        measure.Kind.Should().Be(ReconciliationBreakMeasureKindDto.Value);
        measure.Unit.Should().Be("USD");
        measure.Expected.Should().Be(100m);
        measure.Actual.Should().Be(0m);
        measure.Variance.Should().Be(-100m);
    }

    [Fact]
    public async Task An_overpayment_creates_an_expected_to_received_value_break_even_when_fully_posted()
    {
        var exception = await CaptureExceptionAsync(received: 200m, posted: 200m);

        exception.Variance.Should().Be(100m);
        var measure = exception.Measures.Should().ContainSingle().Subject;
        measure.Kind.Should().Be(ReconciliationBreakMeasureKindDto.Value);
        measure.Unit.Should().Be("USD");
        measure.Expected.Should().Be(100m);
        measure.Actual.Should().Be(200m);
        measure.Variance.Should().Be(100m);
    }

    private async Task<ReconciliationBreakQueueItem> CaptureExceptionAsync(decimal received, decimal posted)
    {
        var securityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var positionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var dueDate = new DateOnly(2026, 11, 19);
        var retainedAt = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        var evidence = new RetainedEvidenceIdentityDto("accepted-position", "evidence://holdings/accepted-position",
            new string('a', 64), "custodian", "position-1", "Accepted", "reviewer", retainedAt,
            dueDate.AddDays(-1), 1, retainedAt, "evidence-ingestion", "InstrumentPosition", positionId.ToString("D"));
        var expectation = new BookReceiptExpectationDto("coupon-1", positionId, securityId, "Treasury coupon",
            "Coupon", dueDate, "USD", 1000m, 100m,
            Guid.Parse("33333333-3333-3333-3333-333333333333"), new string('b', 64),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"), [evidence.EvidenceUri], [evidence]);
        var receipt = new BookReceiptDto("payment-1", securityId, dueDate, "USD", received, "custodian",
            "bank-transaction-1", [new("coupon-1", received)], null, ["evidence://receipts/payment-1"]);
        IReadOnlyList<BookReceiptPostingDto> postings = posted == 0m ? [] :
            [new("posting-1", "payment-1", "coupon-1", posted, "USD", "journal-1", dueDate, false,
                ["ledger://journal/journal-1"], JournalCashAmount: posted)];
        var snapshot = new BookReceiptSourceSnapshotDto([expectation], [receipt], postings,
            [new("Holdings", true), new("Projections", true), new("Receipts", true), new("Postings", true)]);
        var scope = new BookReceiptScopeDto("tenant-a", "company-a",
            Guid.Parse("66666666-6666-6666-6666-666666666666"));
        var repository = new FileReconciliationBreakQueueRepository(_root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new BookReceiptQueueService(new FixedSource(snapshot), new FileBookReceiptRunStore(_root), repository);

        var run = await service.RunAsync(new(scope, dueDate.AddDays(1)));

        var exceptionId = run.Items.Should().ContainSingle().Subject.ExceptionId;
        exceptionId.Should().NotBeNullOrWhiteSpace();
        return (await repository.GetByIdAsync(exceptionId!))!;
    }

    private sealed class FixedSource(BookReceiptSourceSnapshotDto snapshot) : IBookReceiptSource
    {
        public Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query, CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
