using FluentAssertions;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class BookReceiptQueueServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-book-receipts-" + Guid.NewGuid().ToString("N"));
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PositionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly AsOfDate = new(2026, 10, 6);
    private static readonly DateOnly DueDate = new(2026, 9, 30);
    private static readonly BookReceiptScopeDto Scope = new("tenant-a", "company-a",
        Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly BookReceiptQueryDto Query = new(Scope, AsOfDate);

    [Fact]
    public async Task Separate_payments_settle_one_obligation_without_conflating_receipt_and_posting()
    {
        var source = Source(receipts: [Receipt("payment-40", 40m), Receipt("payment-60", 60m)]);

        var item = (await Service(source).RunAsync(Query)).Items.Should().ContainSingle().Subject;

        item.ExpectedAmount.Should().Be(100m);
        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.RemainingAmount.Should().Be(0m);
        item.Status.Should().Be("ReceivedButUnposted");
        item.Receipts.Should().HaveCount(2);
    }

    [Fact]
    public async Task One_payment_can_allocate_exactly_once_across_coupon_and_principal()
    {
        var source = Source(
            expectations: [Expectation("coupon", 40m), Expectation("principal", 60m) with { FlowType = "Principal" }],
            receipts: [Receipt("payment-100", 100m, [new("coupon", 40m), new("principal", 60m)])],
            postings: [Posting("coupon-post", "payment-100", 40m, "coupon"), Posting("principal-post", "payment-100", 60m, "principal")]);

        var run = await Service(source).RunAsync(Query);

        run.Items.Single(item => item.ObligationKey == "coupon").ReceivedAmount.Should().Be(40m);
        run.Items.Single(item => item.ObligationKey == "principal").ReceivedAmount.Should().Be(60m);
        run.Items.Sum(item => item.ReceivedAmount).Should().Be(100m);
        run.Items.Sum(item => item.PostedAmount).Should().Be(100m);
        run.Items.Should().OnlyContain(item => item.Status == "Reconciled" && item.EvidenceComplete);
    }

    [Fact]
    public async Task A_payment_overallocated_across_obligations_is_excluded_from_all_totals()
    {
        var source = Source(
            expectations: [Expectation("coupon", 60m), Expectation("principal", 60m)],
            receipts: [Receipt("payment", 100m, [new("coupon", 60m), new("principal", 60m)])]);

        var run = await Service(source).RunAsync(Query);

        run.Items.Sum(item => item.ReceivedAmount).Should().Be(0m);
        run.Items.Should().OnlyContain(item => !item.EvidenceComplete && item.Status != "Reconciled");
        run.Items.Should().OnlyContain(item => item.EvidenceIssues.Count > 0);
    }

    [Fact]
    public async Task Ambiguous_automatic_splits_do_not_choose_between_equal_obligations()
    {
        var source = Source(
            expectations: [Expectation("first-40", 40m), Expectation("first-60", 60m),
                Expectation("second-40", 40m), Expectation("second-60", 60m)],
            receipts: [Receipt("payment", 100m, [])]);

        var run = await Service(source).RunAsync(Query);

        run.Items.Sum(item => item.ReceivedAmount).Should().Be(0m);
        run.Items.Should().OnlyContain(item => !item.EvidenceComplete && item.EvidenceIssues.Count > 0);
        run.Items.Sum(item => item.RemainingAmount).Should().Be(200m);
    }

    [Fact]
    public async Task Canonical_source_payment_reimported_with_another_id_is_counted_once()
    {
        var payment = Receipt("import-one", 100m) with { SourceReference = "bank-transaction-123" };
        var source = Source(receipts: [payment, payment with { ReceiptId = "import-two" }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.RemainingAmount.Should().Be(0m);
    }

    [Fact]
    public async Task A_reversal_referencing_a_reimport_alias_reverses_the_canonical_payment()
    {
        var payment = Receipt("z-import", 100m) with { SourceReference = "bank-transaction-123" };
        var source = Source(receipts:
        [
            payment,
            payment with { ReceiptId = "a-import" },
            Receipt("reversal", 100m, []) with { ReversesReceiptId = "z-import" }
        ]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(0m);
        item.RemainingAmount.Should().Be(100m);
        item.EvidenceComplete.Should().BeTrue();
        item.Status.Should().Be("Overdue");
    }

    [Fact]
    public async Task Duplicate_reversal_imports_can_reference_different_aliases_of_the_same_payment()
    {
        var payment = Receipt("payment-a", 100m) with { SourceReference = "bank-transaction-123" };
        var reversal = Receipt("reversal-a", 100m, []) with
        {
            SourceReference = "bank-reversal-123",
            ReversesReceiptId = "payment-a"
        };
        var source = Source(receipts:
        [
            payment,
            payment with { ReceiptId = "payment-z" },
            reversal,
            reversal with { ReceiptId = "reversal-z", ReversesReceiptId = "payment-z" }
        ]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(0m);
        item.RemainingAmount.Should().Be(100m);
        item.EvidenceComplete.Should().BeTrue();
        item.Receipts.Count(receipt => receipt.ReversesReceiptId is not null).Should().Be(1);
        item.Status.Should().Be("Overdue");
    }

    [Fact]
    public async Task Postings_link_through_receipt_aliases_without_duplicating_the_journal_allocation()
    {
        var payment = Receipt("z-import", 100m) with { SourceReference = "bank-transaction-123" };
        var posting = Posting("posting-one", "z-import", 100m) with { JournalEntryId = "ledger-entry-123" };
        var source = Source(receipts: [payment, payment with { ReceiptId = "a-import" }],
            postings: [posting, posting with { PostingId = "posting-two", ReceiptId = "a-import" }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(100m);
        item.EvidenceComplete.Should().BeTrue();
        item.Status.Should().Be("Reconciled");
    }

    [Fact]
    public async Task Conflicting_reimports_of_the_same_source_payment_fail_closed()
    {
        var payment = Receipt("import-one", 100m) with { SourceReference = "bank-transaction-123" };
        var conflicting = Receipt("import-two", 60m) with { SourceReference = payment.SourceReference };
        var source = Source(receipts: [payment, conflicting]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(0m);
        item.RemainingAmount.Should().Be(100m);
        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().NotBeEmpty();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Reusing_a_receipt_id_for_different_source_payments_cannot_double_count_cash()
    {
        var payment = Receipt("payment", 100m);
        var source = Source(receipts: [payment, payment with { SourceReference = "different-bank-transaction" }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Expected_received_and_posted_balances_remain_independent()
    {
        var source = Source(receipts: [Receipt("payment", 60m)], postings: [Posting("journal", "payment", 20m)]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ExpectedAmount.Should().Be(100m);
        item.ReceivedAmount.Should().Be(60m);
        item.PostedAmount.Should().Be(20m);
        item.RemainingAmount.Should().Be(40m);
        item.AgingDays.Should().Be(6);
        item.Status.Should().Be("PartiallyReceived");
    }

    [Theory]
    [InlineData(0, "Due", 0)]
    [InlineData(6, "Overdue", 6)]
    [InlineData(-1, "Scheduled", 0)]
    public async Task Unpaid_obligations_age_from_the_due_date(int daysAfterDue, string expectedStatus, int expectedAge)
    {
        var query = Query with { AsOfDate = DueDate.AddDays(daysAfterDue) };

        var item = (await Service(Source()).RunAsync(query)).Items.Single();

        item.Status.Should().Be(expectedStatus);
        item.AgingDays.Should().Be(expectedAge);
        item.RemainingAmount.Should().Be(100m);
    }

    [Theory]
    [InlineData("Holdings")]
    [InlineData("Projections")]
    [InlineData("Receipts")]
    [InlineData("Postings")]
    public async Task Missing_provider_coverage_cannot_produce_a_reconciled_obligation(string missingArea)
    {
        var source = Source(receipts: [Receipt("payment", 100m)], postings: [Posting("journal", "payment", 100m)]);
        source.Snapshot = source.Snapshot with
        {
            Coverage = CompleteCoverage().Select(coverage => coverage.Area == missingArea
                ? coverage with { IsComplete = false, Reason = "Provider evidence is unavailable." }
                : coverage).ToArray()
        };

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().NotBeEmpty();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Omitted_coverage_is_incomplete_even_if_observed_amounts_match()
    {
        var source = Source(receipts: [Receipt("payment", 100m)], postings: [Posting("journal", "payment", 100m)]);
        source.Snapshot = source.Snapshot with { Coverage = [] };

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.EvidenceComplete.Should().BeFalse();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task A_source_link_without_retained_accepted_evidence_cannot_establish_an_obligation()
    {
        var source = Source(expectations: [Expectation() with { RetainedEvidence = [] }],
            receipts: [Receipt("payment", 100m)], postings: [Posting("journal", "payment", 100m)]);

        var run = await Service(source).RunAsync(Query);

        run.Items.Should().BeEmpty();
        run.Coverage.Should().Contain(coverage => !coverage.IsComplete);
    }

    [Fact]
    public async Task A_posting_cannot_exceed_its_linked_cash_allocation()
    {
        var source = Source(receipts: [Receipt("payment", 40m)], postings: [Posting("journal", "payment", 100m)]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(40m);
        item.PostedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Canonical_journal_allocation_reimported_with_another_id_is_counted_once()
    {
        var posting = Posting("posting-import-one", "payment", 100m) with { JournalEntryId = "ledger-entry-123" };
        var source = Source(receipts: [Receipt("payment", 100m)],
            postings: [posting, posting with { PostingId = "posting-import-two" }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.PostedAmount.Should().Be(100m);
        item.ReceivedAmount.Should().Be(100m);
        item.Status.Should().Be("Reconciled");
    }

    [Fact]
    public async Task Separate_receipts_can_share_one_journal_with_retained_cash_capacity()
    {
        var source = SharedJournalSource(40m, 60m, 100m, 100m);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(100m);
        item.EvidenceComplete.Should().BeTrue();
        item.Status.Should().Be("Reconciled");
    }

    [Fact]
    public async Task Separate_receipt_postings_cannot_exceed_the_shared_journal_cash_capacity()
    {
        var source = SharedJournalSource(60m, 60m, 100m, 100m);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(120m);
        item.PostedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task A_journal_linked_to_separate_receipts_requires_retained_cash_capacity()
    {
        var source = SharedJournalSource(40m, 60m, null, null);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Conflicting_cash_capacity_evidence_for_one_journal_is_rejected()
    {
        var source = SharedJournalSource(40m, 60m, 100m, 120m);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Conflicting_amounts_for_the_same_journal_allocation_are_not_posted()
    {
        var posting = Posting("import-one", "payment", 40m) with { JournalEntryId = "ledger-entry-123" };
        var source = Source(receipts: [Receipt("payment", 100m)],
            postings: [posting, posting with { PostingId = "import-two", Amount = 60m }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Postings_for_a_different_currency_cannot_settle_the_receipt()
    {
        var source = Source(receipts: [Receipt("payment", 100m)],
            postings: [Posting("journal", "payment", 100m) with { Currency = "EUR" }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Reversed_journal_entries_do_not_count_as_posted_receipts()
    {
        var source = Source(receipts: [Receipt("payment", 100m)],
            postings: [Posting("journal", "payment", 100m) with { IsReversed = true }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.Status.Should().Be("ReceivedButUnposted");
    }

    [Fact]
    public async Task Partial_reversal_preserves_exact_split_allocation_totals()
    {
        var payment = Receipt("payment", 100m, [new("coupon", 40m), new("principal", 60m)]);
        var reversal = Receipt("reversal", 50m, []) with { ReversesReceiptId = payment.ReceiptId };
        var source = Source(expectations: [Expectation("coupon", 40m), Expectation("principal", 60m)],
            receipts: [payment, reversal]);

        var run = await Service(source).RunAsync(Query);

        run.Items.Single(item => item.ObligationKey == "coupon").ReceivedAmount.Should().Be(20m);
        run.Items.Single(item => item.ObligationKey == "principal").ReceivedAmount.Should().Be(30m);
        run.Items.Sum(item => item.ReceivedAmount).Should().Be(50m);
        run.Items.Sum(item => item.RemainingAmount).Should().Be(50m);
    }

    [Theory]
    [InlineData("JPY", "100", "1", "0", "1", 0)]
    [InlineData("KWD", "1", "0.003", "0.002", "0.001", 3)]
    public async Task Derived_reversal_splits_preserve_currency_minor_units_and_the_exact_total(
        string currency, string legValue, string reversalValue, string firstReversedValue, string secondReversedValue, int precision)
    {
        var legAmount = decimal.Parse(legValue, CultureInfo.InvariantCulture);
        var reversalAmount = decimal.Parse(reversalValue, CultureInfo.InvariantCulture);
        var firstReversed = decimal.Parse(firstReversedValue, CultureInfo.InvariantCulture);
        var secondReversed = decimal.Parse(secondReversedValue, CultureInfo.InvariantCulture);
        var source = Source(
            expectations: [Expectation("coupon-a", legAmount) with { Currency = currency },
                Expectation("coupon-b", legAmount) with { Currency = currency }],
            receipts:
            [
                Receipt("payment", 2m * legAmount, [new("coupon-a", legAmount), new("coupon-b", legAmount)]) with { Currency = currency },
                Receipt("reversal", reversalAmount, []) with { Currency = currency, ReversesReceiptId = "payment" }
            ]);

        var run = await Service(source).RunAsync(Query);

        run.Items.Single(item => item.ObligationKey == "coupon-a").ReceivedAmount.Should().Be(legAmount - firstReversed);
        run.Items.Single(item => item.ObligationKey == "coupon-b").ReceivedAmount.Should().Be(legAmount - secondReversed);
        run.Items.Sum(item => item.RemainingAmount).Should().Be(reversalAmount);
        run.Items.Sum(item => item.ReceivedAmount).Should().Be(2m * legAmount - reversalAmount);
        run.Items.Should().OnlyContain(item => decimal.Round(item.RemainingAmount, precision) == item.RemainingAmount);
        run.Items.Should().OnlyContain(item => item.EvidenceComplete);
    }

    [Fact]
    public async Task Canonical_json_preserves_decimal_strings_beyond_javascript_integer_precision()
    {
        const decimal expected = 9007199254740993.03m;
        const decimal received = 9007199254740993.02m;
        const decimal posted = 9007199254740993.01m;
        const decimal quantity = 1234567890123.0001m;
        var source = Source(expectations: [Expectation(amount: expected) with { Quantity = quantity }],
            receipts: [Receipt("payment", received)],
            postings: [Posting("journal", "payment", posted) with { JournalCashAmount = received }]);
        var run = await Service(source).RunAsync(Query);

        var json = JsonSerializer.Serialize(run, BookReceiptJsonContext.Default.BookReceiptRunDto);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("items")[0];
        var snapshot = document.RootElement.GetProperty("sourceSnapshot");

        foreach (var (field, amount) in new[] { ("expectedAmount", expected), ("receivedAmount", received),
            ("postedAmount", posted), ("remainingAmount", 0.01m) })
        {
            item.GetProperty(field).ValueKind.Should().Be(JsonValueKind.String);
            item.GetProperty(field).GetString().Should().Be(amount.ToString(CultureInfo.InvariantCulture));
        }
        snapshot.GetProperty("expectations")[0].GetProperty("quantity").GetString()
            .Should().Be(quantity.ToString(CultureInfo.InvariantCulture));
        snapshot.GetProperty("receipts")[0].GetProperty("allocations")[0].GetProperty("amount").GetString()
            .Should().Be(received.ToString(CultureInfo.InvariantCulture));
        snapshot.GetProperty("postings")[0].GetProperty("journalCashAmount").GetString()
            .Should().Be(received.ToString(CultureInfo.InvariantCulture));
        JsonSerializer.Deserialize(json, BookReceiptJsonContext.Default.BookReceiptRunDto).Should().BeEquivalentTo(run);
        (await Service(source).GetRunAsync(Scope, run.RunId))!.Items.Single().RemainingAmount.Should().Be(0.01m);
    }

    [Fact]
    public async Task A_partial_receipt_reversal_cannot_reconcile_cash_while_the_old_larger_posting_remains()
    {
        var source = Source(receipts:
        [
            Receipt("payment", 150m),
            Receipt("reversal", 50m, []) with { ReversesReceiptId = "payment" }
        ], postings: [Posting("journal", "payment", 150m)]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ExpectedAmount.Should().Be(100m);
        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(150m);
        item.RemainingAmount.Should().Be(0m);
        item.Status.Should().Be("Disputed");
    }

    [Fact]
    public async Task A_receipt_reversal_cannot_reduce_cash_below_the_original_payment()
    {
        var source = Source(receipts:
        [
            Receipt("payment", 100m),
            Receipt("first-reversal", 60m, []) with { ReversesReceiptId = "payment" },
            Receipt("second-reversal", 60m, []) with { ReversesReceiptId = "payment" }
        ]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().BeGreaterThanOrEqualTo(0m);
        item.ReceivedAmount.Should().BeLessThanOrEqualTo(100m);
        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Future_receipts_and_postings_cannot_change_an_earlier_daily_observation()
    {
        var source = Source(receipts: [Receipt("payment", 100m) with { EffectiveDate = AsOfDate.AddDays(1) }],
            postings: [Posting("journal", "payment", 100m) with { EffectiveDate = AsOfDate.AddDays(1) }]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ReceivedAmount.Should().Be(0m);
        item.PostedAmount.Should().Be(0m);
        item.RemainingAmount.Should().Be(100m);
        item.Status.Should().Be("Overdue");
    }

    [Fact]
    public async Task A_reversal_reopens_the_same_durable_exception_and_preserves_its_owner()
    {
        var repository = Repository();
        var payment = Receipt("payment", 100m);
        var source = Source();
        var service = Service(source, repository);
        var first = await service.RunAsync(Query);
        var exceptionId = first.Items.Single().ExceptionId;
        exceptionId.Should().NotBeNullOrWhiteSpace();
        await SeedResolvedCaseAsync(repository, exceptionId!);
        source.Snapshot = source.Snapshot with
        {
            Receipts = [payment],
            Postings = [Posting("journal", "payment", 100m)]
        };
        var settled = await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });
        settled.Items.Single().Status.Should().Be("Reconciled");
        source.Snapshot = source.Snapshot with
        {
            Receipts = [payment, Receipt("reversal", 100m, []) with
            {
                ReversesReceiptId = payment.ReceiptId,
                EffectiveDate = AsOfDate.AddDays(2)
            }],
            Postings = [Posting("journal", "payment", 100m) with { IsReversed = true }]
        };

        var reversed = await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(2) });
        var reopened = (await repository.GetByIdAsync(exceptionId!))!;

        reversed.Items.Single().ExceptionId.Should().Be(exceptionId);
        reversed.Items.Single().ReceivedAmount.Should().Be(0m);
        reversed.Items.Single().RemainingAmount.Should().Be(100m);
        reopened.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        reopened.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        reopened.AssignedTo.Should().Be("cash-operations-owner");
        (await repository.GetAuditHistoryAsync(exceptionId!)).Should().Contain(entry =>
            entry.NewLifecycleState == ReconciliationCaseLifecycleState.Reopened);
        (await repository.GetAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_posting_reversal_reopens_the_original_exception_without_reversing_the_received_cash()
    {
        var repository = Repository();
        var source = Source();
        var service = Service(source, repository);
        var first = await service.RunAsync(Query);
        var exceptionId = first.Items.Single().ExceptionId;
        exceptionId.Should().NotBeNullOrWhiteSpace();
        source.Snapshot = source.Snapshot with
        {
            Receipts = [Receipt("payment", 100m)],
            Postings = [Posting("journal", "payment", 100m)]
        };
        var settled = await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });
        settled.Items.Single().Status.Should().Be("Reconciled");
        await SeedResolvedCaseAsync(repository, exceptionId!);
        source.Snapshot = source.Snapshot with
        {
            Postings = [Posting("journal", "payment", 100m) with { IsReversed = true }]
        };

        var reversed = await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(2) });
        var item = reversed.Items.Single();
        var reopened = (await repository.GetByIdAsync(exceptionId!))!;

        item.ExceptionId.Should().Be(exceptionId);
        item.ExpectedAmount.Should().Be(100m);
        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.RemainingAmount.Should().Be(0m);
        item.Status.Should().Be("ReceivedButUnposted");
        item.Owner.Should().Be("Cash Operations Owner");
        reopened.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
        reopened.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        reopened.AssignedTo.Should().Be("cash-operations-owner");
        (await repository.GetAuditHistoryAsync(exceptionId!)).Should().Contain(entry =>
            entry.NewLifecycleState == ReconciliationCaseLifecycleState.Reopened);
        (await repository.GetAllAsync()).Should().ContainSingle();
        (await service.GetRunAsync(Scope, settled.RunId))!.Items.Single().PostedAmount.Should().Be(100m);
    }

    [Fact]
    public async Task A_previously_observed_reversal_respects_later_operator_disposition_until_a_new_reversal_arrives()
    {
        var repository = Repository();
        var source = Source();
        var service = Service(source, repository);
        var first = await service.RunAsync(Query);
        var exceptionId = first.Items.Single().ExceptionId!;
        exceptionId.Should().NotBeNullOrWhiteSpace();
        var payment = Receipt("payment", 100m);
        source.Snapshot = source.Snapshot with
        {
            Receipts = [payment],
            Postings = [Posting("journal", "payment", 100m)]
        };
        await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });
        await SeedResolvedCaseAsync(repository, exceptionId);
        var firstReversal = Receipt("reversal-one", 40m, []) with
        {
            ReversesReceiptId = "payment",
            EffectiveDate = AsOfDate.AddDays(2)
        };
        source.Snapshot = source.Snapshot with { Receipts = [payment, firstReversal] };
        await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(2) });
        (await repository.GetByIdAsync(exceptionId))!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        (await repository.GetAuditHistoryAsync(exceptionId)).Should()
            .ContainSingle(entry => entry.EventType == "ReceiptReversalReopened");

        // Seed a later retained operator disposition; authorization for that action is tested by casework tests.
        var afterFirstReversal = (await repository.GetByIdAsync(exceptionId))!;
        await repository.SaveAsync(afterFirstReversal with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            ResolvedBy = "controller",
            ResolvedAt = new DateTimeOffset(AsOfDate.AddDays(3).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            ResolutionNote = "Operator disposition after the retained partial reversal."
        });
        var resolved = (await repository.GetByIdAsync(exceptionId))!;
        var nextDay = await Service(source, Repository()).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(3) });

        nextDay.Items.Single().ReceivedAmount.Should().Be(60m);
        (await repository.GetByIdAsync(exceptionId))!.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
        (await repository.GetByIdAsync(exceptionId))!.Version.Should().Be(resolved.Version);
        (await repository.GetAuditHistoryAsync(exceptionId)).Should()
            .ContainSingle(entry => entry.EventType == "ReceiptReversalReopened");
        source.Snapshot = source.Snapshot with
        {
            Receipts = [payment, firstReversal, Receipt("reversal-two", 60m, []) with
            {
                ReversesReceiptId = "payment", EffectiveDate = AsOfDate.AddDays(4)
            }]
        };

        var newReversal = await Service(source, Repository()).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(4) });

        newReversal.Items.Single().ReceivedAmount.Should().Be(0m);
        newReversal.Items.Single().ExceptionId.Should().Be(exceptionId);
        var reopened = (await repository.GetByIdAsync(exceptionId))!;
        reopened.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        reopened.AssignedTo.Should().Be("cash-operations-owner");
        (await repository.GetAuditHistoryAsync(exceptionId)).Count(entry => entry.EventType == "ReceiptReversalReopened")
            .Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_daily_run_save_does_not_lose_the_reversal_or_repeat_its_exception_mutation_on_retry(
        bool laterOperatorDisposition)
    {
        var repository = Repository();
        var source = Source();
        var store = new FailNextSaveRunStore(new FileBookReceiptRunStore(_root));
        var service = new BookReceiptQueueService(source, store, repository);
        var first = await service.RunAsync(Query);
        var exceptionId = first.Items.Single().ExceptionId!;
        exceptionId.Should().NotBeNullOrWhiteSpace();
        var payment = Receipt("payment", 100m);
        source.Snapshot = source.Snapshot with
        {
            Receipts = [payment],
            Postings = [Posting("journal", "payment", 100m)]
        };
        await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });
        await SeedResolvedCaseAsync(repository, exceptionId);
        source.Snapshot = source.Snapshot with
        {
            Receipts = [payment, Receipt("reversal", 100m, []) with
            {
                ReversesReceiptId = "payment", EffectiveDate = AsOfDate.AddDays(2)
            }],
            Postings = [Posting("journal", "payment", 100m) with { IsReversed = true }]
        };
        var query = Query with { AsOfDate = AsOfDate.AddDays(2) };
        store.FailNextSave = true;

        var capture = () => service.RunAsync(query);
        await capture.Should().ThrowAsync<IOException>().WithMessage("Injected receipt run save failure.");

        (await service.GetRunsAsync(Scope)).Should().HaveCount(2);
        (await Repository().GetByIdAsync(exceptionId))!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
        if (laterOperatorDisposition)
            await SeedResolvedCaseAsync(Repository(), exceptionId);
        var retainedCase = (await Repository().GetByIdAsync(exceptionId))!;
        var restarted = Service(source, Repository());
        var retry = await restarted.RunAsync(query);
        var replay = await restarted.RunAsync(query);
        retry.Items.Single().ReceivedAmount.Should().Be(0m);
        retry.Items.Single().RemainingAmount.Should().Be(100m);
        retry.Items.Single().ExceptionId.Should().Be(exceptionId);
        replay.RunId.Should().Be(retry.RunId);
        (await restarted.GetRunsAsync(Scope)).Should().HaveCount(3);
        var caseAfterRetry = (await Repository().GetByIdAsync(exceptionId))!;
        caseAfterRetry.Status.Should().Be(retainedCase.Status);
        caseAfterRetry.LifecycleState.Should().Be(retainedCase.LifecycleState);
        caseAfterRetry.Version.Should().Be(retainedCase.Version);
        caseAfterRetry.AssignedTo.Should().Be(retainedCase.AssignedTo);
        (await Repository().GetAuditHistoryAsync(exceptionId)).Should()
            .ContainSingle(entry => entry.EventType == "ReceiptReversalReopened");
    }

    [Fact]
    public async Task An_investigated_reconciliation_case_surfaces_as_disputed_with_its_existing_owner()
    {
        var repository = Repository();
        var source = Source();
        var service = Service(source, repository);
        var first = await service.RunAsync(Query);
        var exceptionId = first.Items.Single().ExceptionId;
        exceptionId.Should().NotBeNullOrWhiteSpace();
        var exception = (await repository.GetByIdAsync(exceptionId!))!;
        await repository.SaveAsync(exception with
        {
            AssignedTo = "controller",
            LifecycleState = ReconciliationCaseLifecycleState.Investigating
        });

        var disputed = (await service.RunAsync(Query)).Items.Single();

        disputed.Status.Should().Be("Disputed");
        disputed.Owner.Should().Be("controller");
        disputed.ExceptionId.Should().Be(exceptionId);
        disputed.EvidenceLinks.Should().Contain("evidence://holdings/accepted-position");
        disputed.EvidenceLinks.Should().Contain("evidence://projections/accepted-run");
    }

    [Fact]
    public async Task Revised_terms_preserve_previous_expectations_and_immutable_daily_observations()
    {
        var original = Expectation();
        var source = Source(expectations: [original]);
        var service = Service(source);
        var first = await service.RunAsync(Query);
        source.Snapshot = source.Snapshot with { Expectations = [RevisedExpectation(original, 120m)] };

        var second = await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });
        var retainedFirst = await Service(source).GetRunAsync(Scope, first.RunId);

        second.Items.Single().ExpectedAmount.Should().Be(120m);
        second.Items.Single().ExpectationHistory.Select(version => version.Expectation.ExpectedAmount)
            .Should().BeEquivalentTo(new[] { 100m, 120m });
        retainedFirst!.Items.Single().ExpectedAmount.Should().Be(100m);
        retainedFirst.Items.Single().ExpectationHistory.Should().ContainSingle();
        retainedFirst.SourceSnapshot.Expectations.Single().TermsVersionId.Should().Be(original.TermsVersionId);
        (await service.GetRunsAsync(Scope)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Restored_earlier_terms_remain_current_when_the_next_run_has_no_projection_evidence()
    {
        var original = Expectation();
        var source = Source(expectations: [original]);
        var service = Service(source);
        await service.RunAsync(Query);
        source.Snapshot = source.Snapshot with { Expectations = [RevisedExpectation(original, 120m)] };
        await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });
        source.Snapshot = source.Snapshot with { Expectations = [original] };
        var restored = await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(2) });
        restored.Items.Single().ExpectedAmount.Should().Be(100m);
        source.Snapshot = new BookReceiptSourceSnapshotDto([], [], [],
            [new("Projections", false, "Projection provider unavailable.")]);

        var item = (await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(3) })).Items.Single();

        item.ExpectedAmount.Should().Be(100m);
        item.ExpectationHistory.Should().HaveCount(2);
        item.ExpectationHistory.Select(version => version.Expectation.ExpectedAmount)
            .Should().BeEquivalentTo(new[] { 100m, 120m });
        item.ExpectationHistory.Last().Expectation.TermsVersionId.Should().Be(original.TermsVersionId);
        item.EvidenceComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Provider_outage_retains_prior_expectations_and_marks_them_incomplete()
    {
        var source = Source();
        var service = Service(source);
        await service.RunAsync(Query);
        source.Snapshot = new BookReceiptSourceSnapshotDto([], [], [],
            [new("Holdings", false, "Holdings provider unavailable."), new("Projections", false, "Projection provider unavailable.")]);

        var item = (await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) })).Items.Single();

        item.ExpectedAmount.Should().Be(100m);
        item.ExpectationHistory.Should().ContainSingle();
        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().Contain(issue => issue.Contains("provider unavailable", StringComparison.Ordinal));
        item.RemainingAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Backdated_processing_does_not_erase_later_receipts_during_a_provider_outage()
    {
        var source = Source(receipts: [Receipt("payment", 100m) with { EffectiveDate = AsOfDate.AddDays(1) }],
            postings: [Posting("journal", "payment", 100m) with { EffectiveDate = AsOfDate.AddDays(1) }]);
        var service = Service(source);
        var laterRun = await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(2) });
        laterRun.Items.Single().Status.Should().Be("Reconciled");
        source.Snapshot = source.Snapshot with { Receipts = [], Postings = [] };
        var backdatedRun = await service.RunAsync(Query);
        backdatedRun.Items.Single().ReceivedAmount.Should().Be(0m);
        source.Snapshot = new BookReceiptSourceSnapshotDto([], [], [],
            [new("Receipts", false, "Receipt provider unavailable."), new("Postings", false, "Posting provider unavailable.")]);

        var item = (await service.RunAsync(Query with { AsOfDate = AsOfDate.AddDays(3) })).Items.Single();

        item.ExpectedAmount.Should().Be(100m);
        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(100m);
        item.RemainingAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Reordered_source_evidence_and_process_restart_reproduce_the_same_run()
    {
        var source = Source(receipts: [Receipt("a", 40m), Receipt("b", 60m)],
            postings: [Posting("journal-a", "a", 40m), Posting("journal-b", "b", 60m)]);
        var first = await Service(source).RunAsync(Query);
        source.Snapshot = source.Snapshot with
        {
            Receipts = source.Snapshot.Receipts.Reverse().ToArray(),
            Postings = source.Snapshot.Postings.Reverse().ToArray(),
            Coverage = source.Snapshot.Coverage.Reverse().ToArray()
        };

        var replay = await Service(source).RunAsync(Query);

        replay.RunId.Should().Be(first.RunId);
        replay.InputHash.Should().Be(first.InputHash);
        replay.Should().BeEquivalentTo(first);
        (await Service(source).GetRunsAsync(Scope)).Should().ContainSingle();
    }

    [Fact]
    public async Task Reordered_nested_allocations_and_evidence_reproduce_the_same_run_without_new_expectation_versions()
    {
        var retained = Evidence();
        var source = Source(
            expectations:
            [
                Expectation("coupon", 40m) with
                {
                    RetainedEvidence = [retained, retained with { EvidenceId = "accepted-projection", SourceReference = "projection-123" }]
                },
                Expectation("principal", 60m)
            ],
            receipts: [Receipt("payment", 100m, [new("coupon", 40m), new("principal", 60m)]) with
            {
                EvidenceLinks = ["evidence://receipts/payment", "evidence://statements/payment"]
            }],
            postings:
            [
                Posting("journal-coupon", "payment", 40m, "coupon") with
                {
                    EvidenceLinks = ["ledger://journal/coupon", "evidence://posting/coupon"]
                },
                Posting("journal-principal", "payment", 60m, "principal")
            ]);
        var first = await Service(source).RunAsync(Query);
        source.Snapshot = source.Snapshot with
        {
            Expectations = source.Snapshot.Expectations.Select(expectation => expectation with
            {
                EvidenceLinks = expectation.EvidenceLinks.Reverse().ToArray(),
                RetainedEvidence = expectation.RetainedEvidence.Reverse().ToArray()
            }).Reverse().ToArray(),
            Receipts = source.Snapshot.Receipts.Select(receipt => receipt with
            {
                Allocations = receipt.Allocations.Reverse().ToArray(),
                EvidenceLinks = receipt.EvidenceLinks.Reverse().ToArray()
            }).ToArray(),
            Postings = source.Snapshot.Postings.Select(posting => posting with
            {
                EvidenceLinks = posting.EvidenceLinks.Reverse().ToArray()
            }).Reverse().ToArray()
        };

        var replay = await Service(source).RunAsync(Query);

        replay.RunId.Should().Be(first.RunId);
        replay.InputHash.Should().Be(first.InputHash);
        replay.Items.Should().OnlyContain(item => item.ExpectationHistory.Count == 1 && item.Status == "Reconciled");
        (await Service(source).GetRunsAsync(Scope)).Should().ContainSingle();
    }

    [Fact]
    public async Task Concurrent_daily_runs_do_not_duplicate_observations()
    {
        var source = Source(receipts: [Receipt("payment", 100m)]);

        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Service(source).RunAsync(Query)));

        runs.Select(run => run.RunId).Distinct().Should().ContainSingle();
        (await Service(source).GetRunsAsync(Scope)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_retained_run_cannot_be_overwritten_with_changed_balances()
    {
        var run = await Service(Source()).RunAsync(Query);
        var store = new FileBookReceiptRunStore(_root);
        var changed = run with { Items = [run.Items.Single() with { ReceivedAmount = 100m, RemainingAmount = 0m }] };

        var overwrite = () => store.SaveAsync(changed);

        await overwrite.Should().ThrowAsync<InvalidOperationException>();
        (await store.ReadAsync(Scope)).Single().Items.Single().ReceivedAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Retained_runs_are_isolated_by_tenant_company_and_book()
    {
        var service = Service(Source());
        var run = await service.RunAsync(Query);
        var otherScopes = new[]
        {
            Scope with { TenantId = "tenant-b" },
            Scope with { CompanyId = "company-b" },
            Scope with { LedgerBookId = Guid.Parse("44444444-4444-4444-4444-444444444444") }
        };

        foreach (var otherScope in otherScopes)
        {
            (await service.GetRunAsync(otherScope, run.RunId)).Should().BeNull();
            (await service.GetRunsAsync(otherScope)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task An_observed_automatic_split_survives_later_expectation_changes()
    {
        var firstExpectation = Expectation("coupon-a", 40m);
        var secondExpectation = Expectation("coupon-b", 60m);
        var source = Source(expectations: [firstExpectation, secondExpectation],
            receipts: [Receipt("payment", 100m, [])]);
        var service = Service(source);
        var original = await service.RunAsync(Query);
        original.Items.Single(item => item.ObligationKey == "coupon-a").ReceivedAmount.Should().Be(40m);
        original.Items.Single(item => item.ObligationKey == "coupon-b").ReceivedAmount.Should().Be(60m);
        original.SourceSnapshot.Receipts.Single().Allocations.Should().BeEmpty();
        source.Snapshot = source.Snapshot with
        {
            Expectations = [RevisedExpectation(firstExpectation, 50m), secondExpectation]
        };

        var revised = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });
        var firstItem = revised.Items.Single(item => item.ObligationKey == "coupon-a");
        var secondItem = revised.Items.Single(item => item.ObligationKey == "coupon-b");

        firstItem.ExpectedAmount.Should().Be(50m);
        firstItem.ReceivedAmount.Should().Be(40m);
        firstItem.RemainingAmount.Should().Be(10m);
        firstItem.Status.Should().Be("PartiallyReceived");
        secondItem.ReceivedAmount.Should().Be(60m);
        secondItem.RemainingAmount.Should().Be(0m);
        revised.Items.Sum(item => item.ReceivedAmount).Should().Be(100m);
        revised.Items.Should().OnlyContain(item => item.EvidenceComplete);
        (await service.GetRunAsync(Scope, original.RunId))!.SourceSnapshot.Receipts.Single().Allocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(40, 60, true)]
    [InlineData(50, 50, false)]
    public async Task Provider_allocation_enrichment_must_agree_with_the_previously_observed_split(
        int firstAmount, int secondAmount, bool agreesWithObservedSplit)
    {
        var payment = Receipt("payment", 100m, []);
        var source = Source(expectations: [Expectation("coupon-a", 40m), Expectation("coupon-b", 60m)],
            receipts: [payment]);
        await Service(source).RunAsync(Query);
        source.Snapshot = source.Snapshot with
        {
            Receipts = [payment with { Allocations = [new("coupon-a", firstAmount), new("coupon-b", secondAmount)] }]
        };

        var enriched = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });

        enriched.Items.Sum(item => item.ReceivedAmount).Should().Be(agreesWithObservedSplit ? 100m : 0m);
        enriched.Items.Should().OnlyContain(item => item.EvidenceComplete == agreesWithObservedSplit);
        if (agreesWithObservedSplit)
        {
            enriched.Items.Single(item => item.ObligationKey == "coupon-a").ReceivedAmount.Should().Be(40m);
            enriched.Items.Single(item => item.ObligationKey == "coupon-b").ReceivedAmount.Should().Be(60m);
        }
        else
        {
            enriched.Items.Should().OnlyContain(item => item.EvidenceIssues.Count > 0);
        }
    }

    [Fact]
    public async Task A_second_distinct_payment_for_the_only_obligation_remains_visible_as_an_overpayment()
    {
        var source = Source(receipts: [Receipt("payment-one", 100m, []), Receipt("payment-two", 100m, [])]);

        var item = (await Service(source).RunAsync(Query)).Items.Single();

        item.ExpectedAmount.Should().Be(100m);
        item.ReceivedAmount.Should().Be(200m);
        item.RemainingAmount.Should().Be(0m);
        item.Status.Should().Be("Disputed");
        item.EvidenceComplete.Should().BeTrue();
        item.Receipts.Should().HaveCount(2);
    }

    [Fact]
    public async Task Inferred_posting_splits_remain_pinned_when_terms_change_and_still_honor_journal_reversals()
    {
        var source = SplitSource();
        var service = Service(source);
        var original = await service.RunAsync(Query);
        original.Items.Should().OnlyContain(item => item.Status == "Reconciled");
        source.Snapshot = source.Snapshot with
        {
            Expectations = source.Snapshot.Expectations.Select(expectation => expectation with { ExpectedAmount = 50m }).ToArray(),
            Postings = source.Snapshot.Postings.Select(posting => posting with { Amount = 50m }).ToArray()
        };

        var revised = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });

        revised.Items.Single(item => item.ObligationKey == "coupon-a").ReceivedAmount.Should().Be(40m);
        revised.Items.Single(item => item.ObligationKey == "coupon-b").ReceivedAmount.Should().Be(60m);
        revised.Items.Single(item => item.ObligationKey == "coupon-a").PostedAmount.Should().Be(40m);
        revised.Items.Single(item => item.ObligationKey == "coupon-b").PostedAmount.Should().Be(60m);
        revised.Items.Should().OnlyContain(item => !item.EvidenceComplete && item.EvidenceIssues.Count > 0);
        revised.SourceSnapshot.Postings.Should().HaveCount(2);
        revised.SourceSnapshot.Postings.Should().OnlyContain(posting => posting.Amount == 50m && posting.IsInferredAllocation);
        (await service.GetRunAsync(Scope, original.RunId))!.SourceSnapshot.Postings
            .Select(posting => posting.Amount).Should().BeEquivalentTo(new[] { 40m, 60m });
        source.Snapshot = source.Snapshot with
        {
            Postings = source.Snapshot.Postings.Select(posting => posting with { IsReversed = true }).ToArray()
        };

        var reversed = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(2) });

        reversed.Items.Sum(item => item.ReceivedAmount).Should().Be(100m);
        reversed.Items.Should().OnlyContain(item => item.PostedAmount == 0m);
        reversed.SourceSnapshot.Postings.Should().OnlyContain(posting => posting.IsReversed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unallocated_journal_reversal_restores_prior_posting_legs_when_current_projections_no_longer_match(
        bool omitSecondProjection)
    {
        var source = SplitSource();
        var firstExpectation = source.Snapshot.Expectations[0];
        var secondExpectation = source.Snapshot.Expectations[1];
        var original = await Service(source).RunAsync(Query);
        original.Items.Should().OnlyContain(item => item.Status == "Reconciled");
        source.Snapshot = source.Snapshot with
        {
            Expectations = omitSecondProjection
                ? [firstExpectation]
                : [firstExpectation with { ExpectedAmount = 50m }, secondExpectation],
            Postings = [Posting("unallocated-journal-observation", "payment", 100m, string.Empty) with
            {
                JournalEntryId = "shared-journal", JournalCashAmount = 100m,
                IsInferredAllocation = true, IsReversed = true
            }]
        };

        var reversed = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });

        reversed.Items.Should().HaveCount(2);
        reversed.Items.Single(item => item.ObligationKey == "coupon-a").ReceivedAmount.Should().Be(40m);
        reversed.Items.Single(item => item.ObligationKey == "coupon-b").ReceivedAmount.Should().Be(60m);
        reversed.Items.Should().OnlyContain(item => item.PostedAmount == 0m);
        reversed.Items.Single(item => item.ObligationKey == "coupon-a").Postings
            .Should().ContainSingle(posting => posting.Amount == 40m && posting.IsReversed);
        reversed.Items.Single(item => item.ObligationKey == "coupon-b").Postings
            .Should().ContainSingle(posting => posting.Amount == 60m && posting.IsReversed);
        reversed.SourceSnapshot.Postings.Should().ContainSingle(posting =>
            posting.ObligationKey == string.Empty && posting.Amount == 100m && posting.IsReversed);
    }

    [Fact]
    public async Task Changed_explicit_posting_allocations_do_not_rewrite_a_previously_observed_journal()
    {
        var source = SplitSource(inferredPosting: false);
        await Service(source).RunAsync(Query);
        source.Snapshot = source.Snapshot with
        {
            Postings = source.Snapshot.Postings.Select(posting => posting with { Amount = 50m }).ToArray()
        };

        var revised = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });

        revised.Items.Sum(item => item.ReceivedAmount).Should().Be(100m);
        revised.Items.Should().OnlyContain(item => item.PostedAmount == 0m && !item.EvidenceComplete);
        revised.SourceSnapshot.Postings.Should().OnlyContain(posting => posting.Amount == 50m && !posting.IsInferredAllocation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_journal_cash_capacity_revision_cannot_rewrite_retained_posting_evidence(bool inferredAllocation)
    {
        var source = Source(receipts: [Receipt("payment", 100m)],
            postings: [Posting("posting", "payment", 100m) with
            {
                JournalCashAmount = 100m, IsInferredAllocation = inferredAllocation
            }]);
        await Service(source).RunAsync(Query);
        source.Snapshot = source.Snapshot with
        {
            Postings = source.Snapshot.Postings.Select(posting => posting with { JournalCashAmount = 120m }).ToArray()
        };

        var item = (await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) })).Items.Single();

        item.ReceivedAmount.Should().Be(100m);
        item.PostedAmount.Should().Be(0m);
        item.EvidenceComplete.Should().BeFalse();
        item.EvidenceIssues.Should().NotBeEmpty();
        item.Status.Should().NotBe("Reconciled");
    }

    [Fact]
    public async Task Inferred_posting_history_cannot_hide_new_allocations_exceeding_the_retained_journal_cash()
    {
        var source = SplitSource();
        await Service(source).RunAsync(Query);
        source.Snapshot = source.Snapshot with
        {
            Postings = source.Snapshot.Postings.Select(posting => posting with { Amount = 60m }).ToArray()
        };

        var revised = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });

        revised.Items.Sum(item => item.ReceivedAmount).Should().Be(100m);
        revised.Items.Should().OnlyContain(item => item.PostedAmount == 0m && !item.EvidenceComplete);
        revised.Items.Should().OnlyContain(item => item.EvidenceIssues.Count > 0);
        revised.SourceSnapshot.Postings.Sum(posting => posting.Amount).Should().Be(120m);
    }

    [Fact]
    public async Task Inferred_posting_history_cannot_hide_conflicting_amounts_under_one_posting_id()
    {
        var source = SplitSource();
        var firstPosting = source.Snapshot.Postings[0];
        var secondPosting = source.Snapshot.Postings[1];
        await Service(source).RunAsync(Query);
        source.Snapshot = source.Snapshot with
        {
            Postings = [firstPosting with { Amount = 10m }, firstPosting with { Amount = 30m }, secondPosting]
        };

        var revised = await Service(source).RunAsync(Query with { AsOfDate = AsOfDate.AddDays(1) });

        revised.Items.Sum(item => item.ReceivedAmount).Should().Be(100m);
        revised.Items.Should().OnlyContain(item => item.PostedAmount == 0m && !item.EvidenceComplete);
        revised.Items.Should().OnlyContain(item => item.EvidenceIssues.Count > 0);
        revised.SourceSnapshot.Postings.Should().HaveCount(3);
        revised.SourceSnapshot.Postings.Sum(posting => posting.Amount).Should().Be(100m);
    }

    private static async Task SeedResolvedCaseAsync(FileReconciliationBreakQueueRepository repository, string exceptionId)
    {
        var exception = (await repository.GetByIdAsync(exceptionId))!;
        exception.Should().NotBeNull();
        await repository.SaveAsync(exception with
        {
            AssignedTo = "cash-operations-owner",
            AssigneeDisplayName = "Cash Operations Owner",
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            ResolvedBy = "controller",
            ResolvedAt = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero),
            ResolutionNote = "Cash receipt and posting verified."
        });
    }

    private BookReceiptQueueService Service(MutableSource source, IReconciliationBreakQueueRepository? exceptions = null)
        => new(source, new FileBookReceiptRunStore(_root), exceptions);

    private FileReconciliationBreakQueueRepository Repository()
        => new(_root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);

    private static MutableSource Source(
        IReadOnlyList<BookReceiptExpectationDto>? expectations = null,
        IReadOnlyList<BookReceiptDto>? receipts = null,
        IReadOnlyList<BookReceiptPostingDto>? postings = null)
        => new(new(expectations ?? [Expectation()], receipts ?? [], postings ?? [], CompleteCoverage()));

    private static MutableSource SharedJournalSource(decimal firstAmount, decimal secondAmount, decimal? firstCapacity, decimal? secondCapacity)
        => Source(expectations: [Expectation(amount: firstAmount + secondAmount)],
            receipts: [Receipt("payment-one", firstAmount), Receipt("payment-two", secondAmount)],
            postings:
            [
                Posting("posting-one", "payment-one", firstAmount) with { JournalEntryId = "shared-journal", JournalCashAmount = firstCapacity },
                Posting("posting-two", "payment-two", secondAmount) with { JournalEntryId = "shared-journal", JournalCashAmount = secondCapacity }
            ]);

    private static MutableSource SplitSource(bool inferredPosting = true)
        => Source(expectations: [Expectation("coupon-a", 40m), Expectation("coupon-b", 60m)],
            receipts: [Receipt("payment", 100m, [])],
            postings:
            [
                Posting("posting-a", "payment", 40m, "coupon-a") with
                { JournalEntryId = "shared-journal", JournalCashAmount = 100m, IsInferredAllocation = inferredPosting },
                Posting("posting-b", "payment", 60m, "coupon-b") with
                { JournalEntryId = "shared-journal", JournalCashAmount = 100m, IsInferredAllocation = inferredPosting }
            ]);

    private static BookReceiptExpectationDto Expectation(string key = "coupon", decimal amount = 100m)
        => new(key, PositionId, SecurityId, "Treasury 4.25%", "Coupon", DueDate, "USD", 1000m, amount,
            Guid.Parse("55555555-5555-5555-5555-555555555555"), new string('a', 64),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            ["evidence://holdings/accepted-position", "evidence://projections/accepted-run"], [Evidence()]);

    private static BookReceiptExpectationDto RevisedExpectation(BookReceiptExpectationDto original, decimal amount)
        => original with
        {
            ExpectedAmount = amount,
            TermsVersionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            TermsHash = new string('b', 64),
            ProjectionRunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
        };

    private static RetainedEvidenceIdentityDto Evidence()
        => new("accepted-position", "evidence://holdings/accepted-position", new string('a', 64),
            "custodian", "position-123", "Accepted", "reviewer",
            new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero), DueDate.AddDays(-1), 1,
            new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero), "evidence-ingestion",
            "InstrumentPosition", PositionId.ToString("D"));

    private static BookReceiptDto Receipt(string id, decimal amount, IReadOnlyList<BookReceiptAllocationDto>? allocations = null)
        => new(id, SecurityId, DueDate, "USD", amount, "custodian", id,
            allocations ?? [new("coupon", amount)], null, [$"evidence://receipts/{id}"]);

    private static BookReceiptPostingDto Posting(string id, string receiptId, decimal amount, string obligationKey = "coupon")
        => new(id, receiptId, obligationKey, amount, "USD", id, DueDate, false, [$"ledger://journal/{id}"]);

    private static IReadOnlyList<BookReceiptCoverageDto> CompleteCoverage()
        => [new("Holdings", true), new("Projections", true), new("Receipts", true), new("Postings", true)];

    private sealed class MutableSource(BookReceiptSourceSnapshotDto snapshot) : IBookReceiptSource
    {
        public BookReceiptSourceSnapshotDto Snapshot { get; set; } = snapshot;

        public Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query, CancellationToken ct = default)
            => Task.FromResult(Snapshot);
    }

    private sealed class FailNextSaveRunStore(IBookReceiptRunStore inner) : IBookReceiptRunStore
    {
        public bool FailNextSave { get; set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(BookReceiptScopeDto scope, CancellationToken ct = default)
            => inner.AcquireAsync(scope, ct);

        public Task<IReadOnlyList<BookReceiptRunDto>> ReadAsync(BookReceiptScopeDto scope, CancellationToken ct = default)
            => inner.ReadAsync(scope, ct);

        public Task SaveAsync(BookReceiptRunDto run, CancellationToken ct = default)
        {
            if (!FailNextSave)
                return inner.SaveAsync(run, ct);
            FailNextSave = false;
            return Task.FromException(new IOException("Injected receipt run save failure."));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
