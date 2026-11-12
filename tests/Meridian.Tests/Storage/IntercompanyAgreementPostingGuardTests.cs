using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class IntercompanyAgreementPostingGuardTests
{
    private static readonly Guid Entity = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Counterparty = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Agreement = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void MissingReference_DoesNotRequireAgreementAuthority()
    {
        var write = Write(new(EntityId: Entity.ToString("D")));
        IntercompanyAgreementPostingGuard.HasReferences(write.Entry).Should().BeFalse();
        AccountingPostingCommandValidator.NormalizeAndValidate(write).Should().Be(write);
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("missing-version")]
    [InlineData("empty-id")]
    [InlineData("zero-version")]
    [InlineData("self-counterparty")]
    [InlineData("invalid-entity")]
    public void ExplicitInvalidReference_CannotCrossTheSharedValidator(string defect)
    {
        var dimensions = Dimensions();
        dimensions = defect switch
        {
            "missing-id" => dimensions with { IntercompanyAgreementId = null },
            "missing-version" => dimensions with { IntercompanyAgreementVersion = null },
            "empty-id" => dimensions with { IntercompanyAgreementId = Guid.Empty },
            "zero-version" => dimensions with { IntercompanyAgreementVersion = 0 },
            "self-counterparty" => dimensions with { CounterpartyId = Entity.ToString("D") },
            _ => dimensions with { EntityId = "not-a-retained-entity" }
        };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(Write(dimensions));
        validate.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void CompatibilityTags_CannotHideAHalfReferenceOnAnOtherwiseDimensionedLine()
    {
        var write = Write(new(EntityId: Entity.ToString("D"), CounterpartyId: Counterparty.ToString("D")));
        var tags = new Dictionary<string, string>
        {
            [$"lineDimensions.{write.Entry.Lines[0].EntryId:N}.intercompanyAgreementId"] = Agreement.ToString("D")
        };
        write = write with { Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
            write.Entry.Lines, write.Entry.Metadata with { Tags = tags }) };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*supplied together*");
    }

    [Fact]
    public void OrphanReservedReferenceTags_CannotBeSilentlyDiscarded()
    {
        var write = Write(new(EntityId: Entity.ToString("D")));
        write = write with { Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
            write.Entry.Lines, write.Entry.Metadata with { Tags = new Dictionary<string, string>
            {
                [$"lineDimensions.{Guid.NewGuid():N}.intercompanyAgreementId"] = Agreement.ToString("D"),
                [$"lineDimensions.{Guid.NewGuid():N}.intercompanyAgreementVersion"] = "1"
            } }) };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*existing journal line*");
    }

    [Fact]
    public void BuildRequest_UsesAccountingDateAndTransactionCurrency()
    {
        var write = Write(Dimensions(), foreignCurrency: true);
        var book = Book();
        var request = IntercompanyAgreementPostingGuard.BuildRequest(write, book, "tenant-alpha", false);
        request.AccountingDate.Should().Be(new DateOnly(2026, 5, 15));
        request.Lines.Single().TransactionCurrency.Should().Be("EUR");
        request.BookCurrency.Should().Be("USD");
        request.IsVerifiedSourceReversal.Should().BeFalse();
    }

    [Fact]
    public void WrongBookEntity_IsRefusedBeforeAgreementLookup()
    {
        var build = () => IntercompanyAgreementPostingGuard.BuildRequest(Write(Dimensions()),
            Book() with { FundStructureNodeId = Counterparty }, "tenant-alpha", false);
        build.Should().Throw<LedgerValidationException>().WithMessage("*ledger-book entity*");
    }

    [Fact]
    public void ConflictingCommandAccountingDate_CannotSelectDifferentAgreementTerms()
    {
        var write = Write(Dimensions());
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), write.AggregateId, write.PeriodId,
            new(2026, 5, 31), write.Entry.Timestamp, "date-conflict", ApprovalState: AccountingPostingApprovalStateDto.Approved,
            OperatorRationale: "Date conflict", LedgerBookId: write.LedgerBookId);
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with { PostingCommand = command });
        validate.Should().Throw<LedgerValidationException>().WithMessage("*accounting dates must agree*");
    }

    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("account", false)]
    [InlineData("cost-center", false)]
    [InlineData("dimension-case", false)]
    [InlineData("agreement-version", false)]
    [InlineData("currency", false)]
    [InlineData("not-reversal", false)]
    public void HistoricalReversal_RequiresTheCompleteSourceInverse(string change, bool expected)
    {
        var original = Write(Dimensions() with { CostCenterId = "DeskA" }, foreignCurrency: true);
        var source = Record(original);
        var reversal = Reverse(original, change);
        IntercompanyAgreementPostingGuard.IsExactSourceReversal(reversal, source).Should().Be(expected);
    }

    [Fact]
    public async Task CallerOwnedTransaction_CannotBypassLeaseLifetime()
    {
        var store = new PostgresLedgerJournalStore(new());
        await using var connection = new NpgsqlConnection();
        var append = () => store.AppendAsync(connection, null!, Write(Dimensions()));
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*standalone append transaction*");
    }

    internal static LedgerJournalEntryWrite Write(LedgerLineDimensionSet dimensions, bool foreignCurrency = false)
    {
        var id = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-05-31T12:00:00Z");
        var lines = new[]
        {
            new LedgerEntry(Guid.NewGuid(), id, timestamp, new("Assets:Intercompany Receivable", LedgerAccountType.Asset),
                120m, 0m, "Intercompany charge", dimensions,
                foreignCurrency ? new("EUR", "USD", 100m, 0m, 1.2m) : null),
            new LedgerEntry(Guid.NewGuid(), id, timestamp, new("Revenue:Services", LedgerAccountType.Revenue),
                0m, 120m, "Intercompany charge", new(EntityId: dimensions.EntityId))
        };
        return new(new(id, timestamp, "Intercompany charge", lines,
            new(EffectiveDate: new(2026, 5, 15))), Guid.NewGuid(), Guid.NewGuid(), LedgerBookId: Guid.NewGuid());
    }

    internal static LedgerLineDimensionSet Dimensions() => new(EntityId: Entity.ToString("D"),
        CounterpartyId: Counterparty.ToString("D"))
    { IntercompanyAgreementId = Agreement, IntercompanyAgreementVersion = 1 };

    internal static LedgerBookRecord Book() => new(Guid.NewGuid(), "fund-agreement", Entity,
        FundStructureNodeKindDto.Entity, "Agreement entity book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    internal static LedgerJournalEntryRecord Record(LedgerJournalEntryWrite write) => new(write.Entry,
        write.AggregateId, write.PeriodId, write.CommandId, write.CorrelationId, 1, DateTimeOffset.UtcNow,
        write.AccountingBasis, write.AccountingPolicyId, write.AccountingPolicyVersion, SourceJournalEntryId: write.SourceJournalEntryId);

    internal static LedgerJournalEntryWrite Reverse(LedgerJournalEntryWrite source, string change = "unchanged")
    {
        var id = Guid.NewGuid();
        var lines = source.Entry.Lines.Select((line, index) => new LedgerEntry(Guid.NewGuid(), id,
            source.Entry.Timestamp, change == "account" && index == 0 ? new("Assets:Other", LedgerAccountType.Asset) : line.Account,
            line.Credit, line.Debit, "Reversal", index == 0 ? change switch
            {
                "cost-center" => line.Dimensions! with { CostCenterId = "changed" },
                "dimension-case" => line.Dimensions! with { CostCenterId = "deska" },
                "agreement-version" => line.Dimensions! with { IntercompanyAgreementVersion = 2 },
                _ => line.Dimensions
            } : line.Dimensions, line.Currency is { } currency ? new LedgerEntryCurrency(
                change == "currency" ? "GBP" : currency.TransactionCurrency, currency.FunctionalCurrency,
                currency.TransactionCredit, currency.TransactionDebit, currency.FxRateToFunctional) : null)).ToArray();
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), source.AggregateId, source.PeriodId,
            new(2026, 5, 31), source.Entry.Timestamp, "reverse:" + id,
            Intent: change == "not-reversal" ? AccountingPostingIntentDto.Adjustment : AccountingPostingIntentDto.Reversal,
            SourceJournalEntryId: source.Entry.JournalEntryId, ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "reviewed-reversal", OperatorRationale: "Reverse the immutable source", LedgerBookId: source.LedgerBookId);
        return source with { Entry = new(id, source.Entry.Timestamp, "Reversal", lines, new(EffectiveDate: new(2026, 5, 31))),
            CommandId = command.CommandId, SourceJournalEntryId = source.Entry.JournalEntryId, PostingCommand = command };
    }
}
