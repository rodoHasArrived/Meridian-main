using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

/// <summary>
/// Normalization rebuilds journal lines to settle their dimensions. Anything it does not carry
/// across that rebuild is discarded on the way to the store, so a leg's currency detail has to
/// survive it — losing it silently drops the transaction currency, both transaction-side amounts,
/// and the FX rate from what is persisted.
/// </summary>
public sealed class AccountingPostingCommandValidatorCurrencyTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid JournalId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid AggregateId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid PeriodId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid CommandId = Guid.Parse("20000000-0000-0000-0000-000000000004");

    [Fact]
    public void NormalizeAndValidate_LineCarryingCurrency_PreservesItThroughDimensionRebuild()
    {
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(BuildWrite());

        var line = normalized.Entry.Lines.Should().HaveCount(2).And.Subject.First();
        line.Dimensions?.BookId.Should().Be(AggregateId.ToString("D"), "the rebuild is what settles dimensions");
        line.Currency.Should().NotBeNull("the same rebuild must not discard the leg's currency");
        line.Currency!.TransactionCurrency.Should().Be("EUR");
        line.Currency.FunctionalCurrency.Should().Be("USD");
        line.Currency.TransactionDebit.Should().Be(50m);
        line.Currency.FxRateToFunctional.Should().Be(2m);
    }

    private static LedgerJournalEntryWrite BuildWrite()
    {
        var dimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-master",
            BookId: AggregateId.ToString("D"));
        var entry = new JournalEntry(
            JournalId,
            OccurredAt,
            "Acquire asset",
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    JournalId,
                    OccurredAt,
                    new LedgerAccount("Investments", LedgerAccountType.Asset),
                    100m,
                    0m,
                    "Acquire asset",
                    dimensions,
                    new LedgerEntryCurrency("EUR", "USD", 50m, 0m, 2m)),
                new LedgerEntry(
                    Guid.NewGuid(),
                    JournalId,
                    OccurredAt,
                    new LedgerAccount("Cash", LedgerAccountType.Asset),
                    0m,
                    100m,
                    "Acquire asset",
                    dimensions,
                    new LedgerEntryCurrency("EUR", "USD", 0m, 50m, 2m))
            ],
            new JournalEntryMetadata
            {
                ActivityType = "asset-acquisition",
                LedgerBook = AggregateId.ToString("D"),
                EffectiveDate = new DateOnly(2026, 7, 21)
            });

        return new LedgerJournalEntryWrite(
            entry,
            AggregateId,
            PeriodId,
            CommandId,
            CorrelationId: null,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-v1",
            AccountingPolicyVersion: "v1",
            LedgerBookId: AggregateId,
            PostingCommand: new AccountingPostingCommandDto(
                CommandId,
                AggregateId,
                PeriodId,
                new DateOnly(2026, 7, 21),
                OccurredAt,
                "asset-acquisition:2026-07-21",
                ApprovalState: AccountingPostingApprovalStateDto.Approved,
                ApprovalId: "approval-1",
                OperatorRationale: "Recorded against the custodian acquisition statement.",
                LedgerBookId: AggregateId));
    }
}
