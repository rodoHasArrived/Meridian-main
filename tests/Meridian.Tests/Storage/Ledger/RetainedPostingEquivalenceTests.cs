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

    [Theory]
    [InlineData("capital account")]
    [InlineData("investor")]
    [InlineData("payment intent")]
    [InlineData("fund event type")]
    [InlineData("project")]
    [InlineData("strategy")]
    [InlineData("institution")]
    [InlineData("symbol")]
    public void Matches_DifferentDurableScopeOrProvenance_IsAConflict(string field)
    {
        // Same book, source event, period, rule, and lines — but a different accounting context.
        // Booking the same amounts against a different fund or investor is a different posting.
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            metadata: metadata => field switch
            {
                "capital account" => metadata with { CapitalAccountId = "capital:other" },
                "investor" => metadata with { InvestorId = "investor:other" },
                "payment intent" => metadata with { PaymentIntentId = "payment:other" },
                "fund event type" => metadata with { FundEventType = "RedemptionAccrual" },
                "project" => metadata with { ProjectId = "project:other" },
                "strategy" => metadata with { StrategyId = "strategy:other" },
                "institution" => metadata with { Institution = "other-custodian" },
                "symbol" => metadata with { Symbol = "MSFT" },
                _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
            }));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be(field);
    }

    [Fact]
    public void Matches_TimestampTruncatedToStoredPrecision_IsStillAnExactReplay()
    {
        // PostgreSQL timestamptz keeps microseconds; a caller-supplied UtcNow carries finer ticks.
        // The retained value comes back truncated while the retry resubmits the original.
        var submitted = Timestamp.AddTicks(7);
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), timestamp: Timestamp));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), timestamp: submitted));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeTrue();
        difference.Should().BeEmpty();
    }

    [Fact]
    public void Matches_TimestampDifferingBeyondStoredPrecision_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), timestamp: Timestamp));
        var candidate = BuildWrite(BuildEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            timestamp: Timestamp.AddMilliseconds(1)));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("accounting timestamp");
    }

    [Fact]
    public void Matches_DifferentAdjustmentApproval_IsAConflict()
    {
        var entry = BuildEntry(Guid.NewGuid(), Guid.NewGuid());
        var retained = BuildRecord(entry) with { AdjustmentApproval = Approval("approval-a", "controller@meridian.local") };
        var candidate = BuildWrite(entry) with { AdjustmentApproval = Approval("approval-b", "controller@meridian.local") };

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("adjustment approval");
    }

    [Fact]
    public void Matches_AccountNameDifferingOnlyByCase_IsAConflict()
    {
        // Balances are keyed by LedgerAccount, whose Name compares ordinally, so this line lands
        // in a different bucket even though the text reads the same.
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            accountName: "accrued interest receivable"));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 account");
    }

    [Fact]
    public void Matches_DifferentLineCurrency_IsAConflict()
    {
        // Same functional debits and credits, different transaction currency and rate.
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), currency: Currency("EUR", 1.0m, 125.44m)));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), currency: Currency("GBP", 2.0m, 62.72m)));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 currency");
    }

    [Fact]
    public void Matches_RetainedCurrencyAgainstAbsentCurrency_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), currency: Currency("EUR", 1.0m, 125.44m)));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 currency");
    }

    [Fact]
    public void Matches_LineageIdentifierDifferingOnlyByCase_IsAConflict()
    {
        // Retained verbatim, and the governed posting target resolves collisions on these
        // ordinally, so a casing change is distinct lineage rather than the same policy.
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid())) with
        {
            AccountingPolicyId = "GAAP-Accrual-V1"
        };

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("accounting policy");
    }

    [Fact]
    public void Matches_DifferentCorrelationIdentity_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid()));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid())) with
        {
            CorrelationId = Guid.Parse("66666666-6666-6666-6666-666666666666")
        };

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("correlation identity");
    }

    // The line validates functional = transaction x rate, so these pairs are chosen to balance
    // against the default 125.44 functional debit.
    [Fact]
    public void Matches_AmountRoundedToStoredScale_IsStillAnExactReplay()
    {
        // Journal legs are numeric(38,10); a .NET decimal carries more fractional digits, so the
        // retained value comes back rounded while the retry resubmits the original.
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), amount: 125.4400000000m));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), amount: 125.44000000001m));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeTrue();
        difference.Should().BeEmpty();
    }

    [Fact]
    public void Matches_AmountDifferingWithinStoredScale_IsAConflict()
    {
        var retained = BuildRecord(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), amount: 125.44m));
        var candidate = BuildWrite(BuildEntry(Guid.NewGuid(), Guid.NewGuid(), amount: 125.4400000001m));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeFalse();
        difference.Should().Be("line 0 amount");
    }

    private static LedgerEntryCurrency Currency(string transactionCurrency, decimal fxRate, decimal transactionDebit)
        => new(transactionCurrency, "USD", transactionDebit, 0m, fxRate);

    private static LedgerAdjustmentApprovalMetadataDto Approval(string approvalId, string approvedBy)
        => new(
            approvalId,
            LedgerAdjustmentApprovalStatusDto.Approved,
            approvedBy,
            Timestamp,
            "reason-code-restatement");

    private const string EntryDescription = "Accrue custodian interest from retained source event";

    private static JournalEntry BuildEntry(
        Guid journalEntryId,
        Guid lineEntryId,
        decimal amount = 125.44m,
        bool swapSides = false,
        string fundId = "fund-alpha",
        string idempotencyKey = "custodian-interest:2026-05",
        IReadOnlyDictionary<string, string>? externalGlDimensions = null,
        Func<JournalEntryMetadata, JournalEntryMetadata>? metadata = null,
        DateTimeOffset? timestamp = null,
        string accountName = "Accrued Interest Receivable",
        LedgerEntryCurrency? currency = null)
        => new(
            journalEntryId,
            timestamp ?? Timestamp,
            EntryDescription,
            [
                new LedgerEntry(
                    lineEntryId,
                    journalEntryId,
                    timestamp ?? Timestamp,
                    new LedgerAccount(accountName, LedgerAccountType.Asset),
                    swapSides ? 0m : amount,
                    swapSides ? amount : 0m,
                    EntryDescription,
                    new LedgerLineDimensionSet(
                        FundId: fundId,
                        EntityId: "entity-master",
                        ExternalGlDimensions: externalGlDimensions
                            ?? new Dictionary<string, string> { ["costCentre"] = "cc-001" }),
                    currency),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp ?? Timestamp,
                    new LedgerAccount("Interest Income", LedgerAccountType.Revenue),
                    swapSides ? amount : 0m,
                    swapSides ? 0m : amount,
                    EntryDescription,
                    new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-master"))
            ],
            (metadata ?? (static value => value))(new JournalEntryMetadata
            {
                ActivityType = "interest-accrual",
                IdempotencyKey = idempotencyKey,
                EffectiveDate = new DateOnly(2026, 5, 31),
                Symbol = "AAPL",
                ProjectId = "project-interest-accrual",
                StrategyId = "strategy-income",
                Institution = "custodian-bny",
                FundEventType = "InterestAccrual",
                CapitalAccountId = "capital:fund-alpha",
                InvestorId = "investor:fund-alpha",
                PaymentIntentId = "payment:fund-alpha"
            }));

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
