using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage.Ledger;

/// <summary>
/// Guards the comparison that decides whether a posting-identity collision is an exact replay or
/// a conflict. Both directions matter: a missed difference acknowledges a posting the books will
/// never contain, and a false difference turns an ordinary operator retry into a hard failure.
/// </summary>
public sealed class RetainedPostingEquivalenceTests
{
    private static readonly Guid AggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PeriodId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceEventId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CommandId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-05-31T21:00:00Z");

    [Fact]
    public void Matches_RebuiltPostingWithFreshGeneratedIdentities_IsAnExactReplay()
    {
        // Separate journal and line ids, and separate dimension-set instances, are exactly what a
        // rebuild of the same request produces. None of that makes it a different posting.
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeTrue();
        difference.Should().BeEmpty();
    }

    [Fact]
    public void Matches_DifferentAmount_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), amount: 999_999m));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 amount");
    }

    [Fact]
    public void Matches_SwappedLineSides_IsAConflict()
    {
        // The multiset of amounts is unchanged; the accounting is reversed.
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), swapSides: true));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 amount");
    }

    [Fact]
    public void Matches_DifferentDimensionValue_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), fundId: "fund-beta"));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 dimensions");
    }

    [Fact]
    public void Matches_DifferentExternalGlDimension_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            externalGlDimensions: new Dictionary<string, string> { ["costCentre"] = "cc-999" }));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 dimensions");
    }

    [Fact]
    public void Matches_DifferentPeriod_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid())) with
        {
            PeriodId = Guid.Parse("99999999-9999-9999-9999-999999999999")
        };

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("accounting period");
    }

    [Fact]
    public void Matches_DifferentPostingCommandIdentity_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid())) with
        {
            CommandId = Guid.Parse("55555555-5555-5555-5555-555555555555")
        };

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("posting command identity");
    }

    [Fact]
    public void Matches_DifferentIdempotencyKey_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), idempotencyKey: "other-key"));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("idempotency key");
    }

    private const string EntryDescription = "Accrue custodian interest from retained source event";

    private static JournalEntry BuildEntry(
        Guid journalEntryId,
        Guid lineEntryId,
        decimal amount = 125.44m,
        bool swapSides = false,
        string fundId = "fund-alpha",
        string idempotencyKey = "custodian-interest:2026-05",
        IReadOnlyDictionary<string, string>? externalGlDimensions = null)
        => new(
            journalEntryId,
            Timestamp,
            EntryDescription,
            [
                new LedgerEntry(
                    lineEntryId,
                    journalEntryId,
                    Timestamp,
                    new LedgerAccount("Accrued Interest Receivable", LedgerAccountType.Asset),
                    swapSides ? 0m : amount,
                    swapSides ? amount : 0m,
                    EntryDescription,
                    new LedgerLineDimensionSet(
                        FundId: fundId,
                        EntityId: "entity-master",
                        ExternalGlDimensions: externalGlDimensions
                            ?? new Dictionary<string, string> { ["costCentre"] = "cc-001" })),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    Timestamp,
                    new LedgerAccount("Interest Income", LedgerAccountType.Revenue),
                    swapSides ? amount : 0m,
                    swapSides ? 0m : amount,
                    EntryDescription,
                    new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-master"))
            ],
            new JournalEntryMetadata
            {
                ActivityType = "interest-accrual",
                IdempotencyKey = idempotencyKey,
                EffectiveDate = new DateOnly(2026, 5, 31)
            });

    private static LedgerJournalEntryRecord BuildRecord(JournalEntry entry)
        => new(
            entry,
            AggregateId,
            PeriodId,
            CommandId,
            CorrelationId: null,
            GlobalSequence: 1,
            CreatedAt: Timestamp,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-accrual-v1",
            AccountingPolicyVersion: "v1",
            RuleId: "posting.interest-accrual",
            RuleVersion: "v1",
            SourceEventId: SourceEventId);

    private static LedgerJournalEntryWrite BuildWrite(JournalEntry entry)
        => new(
            entry,
            AggregateId,
            PeriodId,
            CommandId,
            CorrelationId: null,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-accrual-v1",
            AccountingPolicyVersion: "v1",
            RuleId: "posting.interest-accrual",
            RuleVersion: "v1",
            SourceEventId: SourceEventId);
}
