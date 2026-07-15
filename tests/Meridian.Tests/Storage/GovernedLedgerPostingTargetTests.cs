using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.Storage;

public sealed class GovernedLedgerPostingTargetTests
{
    private static readonly Guid LedgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PeriodId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AggregateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 8, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostAsync_CrashAfterAppendThenAlteredEvidence_FailsClosedWithoutSecondAppend()
    {
        var retained = new List<LedgerJournalEntryRecord>();
        var appendCount = 0;
        var store = BuildStore(retained, write =>
        {
            appendCount++;
            retained.Add(ToRecord(write));
            throw new IOException("response lost after durable append");
        });
        using var target = new DurableLedgerPostingTarget(store.Object);
        var write = BuildWrite();

        Func<Task> firstPost = async () => await target.PostAsync(write);
        await firstPost.Should().ThrowAsync<IOException>();

        var alteredEvidence = CloneEntry(
            write.Entry,
            write.Entry.Metadata with
            {
                EvidenceReferences =
                [
                    new JournalEvidenceReference(
                        "price-close",
                        "evidence://provider/AAPL/2026-07-08/corrected",
                        "Source",
                        "trusted-close",
                        OccurredAt,
                        "valuation-worker",
                        ContentHash: "sha256:changed")
                ]
            });
        Func<Task> retry = async () => await target.PostAsync(write with { Entry = alteredEvidence });

        await retry.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*already retained with different accounting content*");
        appendCount.Should().Be(1, "a crash retry with altered evidence must never append another fact");
    }

    [Fact]
    public async Task PostAsync_RetainedRetry_ComparesEveryDurableIdentityAndMetadataEnvelope()
    {
        var original = BuildWrite();
        var retained = new List<LedgerJournalEntryRecord> { ToRecord(original) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        var mutations = new (string Name, LedgerJournalEntryWrite Write)[]
        {
            ("period", original with { PeriodId = Guid.NewGuid() }),
            ("command", original with { CommandId = Guid.NewGuid() }),
            ("correlation", original with { CorrelationId = Guid.NewGuid() }),
            ("basis", original with { AccountingBasis = AccountingBasisKindDto.Tax }),
            ("policy id", original with { AccountingPolicyId = "fair-value-policy-2" }),
            ("policy version", original with { AccountingPolicyVersion = "v2" }),
            ("rule id", original with { RuleId = "mark-rule-2" }),
            ("rule version", original with { RuleVersion = "v2" }),
            ("source event", original with { SourceEventId = Guid.NewGuid() }),
            ("source journal", original with { SourceJournalEntryId = Guid.NewGuid() }),
            ("posting kind", original with { PostingKind = LedgerPostingKindDto.Originating }),
            ("ledger book", WithLedgerBook(original, Guid.NewGuid())),
            ("approval", original with
            {
                AdjustmentApproval = original.AdjustmentApproval! with { EvidenceLink = "evidence://approval/changed" }
            }),
            ("metadata", original with
            {
                Entry = CloneEntry(original.Entry, original.Entry.Metadata with
                {
                    Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["batchId"] = "valuation-batch-changed"
                    }
                })
            }),
            ("line dimensions", original with
            {
                Entry = CloneFirstLine(
                    original.Entry,
                    original.Entry.Lines[0].Dimensions! with { FundId = "fund-beta" })
            })
        };

        foreach (var mutation in mutations)
        {
            Func<Task> retry = async () => await target.PostAsync(mutation.Write);
            await retry.Should().ThrowAsync<LedgerValidationException>(mutation.Name)
                .WithMessage("*already retained with different accounting content*");
        }

        store.Verify(
            candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAsync_LedgerBookFieldConflictsWithMetadata_FailsBeforeStoreLookup()
    {
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        using var target = new DurableLedgerPostingTarget(store.Object);
        var write = BuildWrite() with { LedgerBookId = Guid.NewGuid() };

        Func<Task> post = async () => await target.PostAsync(write);

        await post.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*conflicts with journal metadata ledger book*");
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PostAsync_EquivalentRetry_AllowsEvidenceReorderingWithoutAppending()
    {
        var original = BuildWrite();
        var secondEvidence = new JournalEvidenceReference(
            "approval",
            "evidence://approval/valuation-batch",
            "Approval",
            "accounting-workbench",
            OccurredAt.AddMinutes(1),
            "controller");
        original = original with
        {
            Entry = CloneEntry(
                original.Entry,
                original.Entry.Metadata with
                {
                    EvidenceReferences = [.. original.Entry.Metadata.EvidenceReferences, secondEvidence]
                })
        };
        var retained = new List<LedgerJournalEntryRecord> { ToRecord(original) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        var reordered = CloneEntry(
            original.Entry,
            original.Entry.Metadata with
            {
                EvidenceReferences = original.Entry.Metadata.EvidenceReferences.Reverse().ToArray()
            });

        var result = await target.PostAsync(original with { Entry = reordered });

        result.WasAppended.Should().BeFalse();
        result.JournalEntryId.Should().Be(original.Entry.JournalEntryId);
    }

    private static Mock<ILedgerJournalStore> BuildStore(
        List<LedgerJournalEntryRecord> retained,
        Action<LedgerJournalEntryWrite> append)
    {
        var store = new Mock<ILedgerJournalStore>();
        store.Setup(candidate => candidate.GetByAggregateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => retained.ToArray());
        store.Setup(candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
            .Returns<LedgerJournalEntryWrite, CancellationToken>((write, _) =>
            {
                append(write);
                return Task.CompletedTask;
            });
        return store;
    }

    private static LedgerJournalEntryWrite BuildWrite()
    {
        var journalEntryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        const string description = "Daily fair-value adjustment for AAPL";
        var dimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            InstrumentId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["desk"] = "valuation"
            });
        var metadata = new JournalEntryMetadata(
            ActivityType: "FairValueMarkAdjustment",
            Symbol: "AAPL",
            SecurityId: dimensions.InstrumentId,
            LedgerBook: LedgerBookId.ToString("D"),
            EffectiveDate: new DateOnly(2026, 7, 8),
            IdempotencyKey: "fair-value|fund-alpha|2026-07-08|AAPL",
            Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["batchId"] = "valuation-batch-1"
            },
            EvidenceReferences:
            [
                new JournalEvidenceReference(
                    "price-close",
                    "evidence://provider/AAPL/2026-07-08",
                    "Source",
                    "trusted-close",
                    OccurredAt,
                    "valuation-worker",
                    ContentHash: "sha256:original")
            ]);
        var entry = new JournalEntry(
            journalEntryId,
            OccurredAt,
            description,
            [
                new LedgerEntry(
                    Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    journalEntryId,
                    OccurredAt,
                    LedgerAccounts.Securities("AAPL", "broker-alpha"),
                    100m,
                    0m,
                    description,
                    dimensions),
                new LedgerEntry(
                    Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    journalEntryId,
                    OccurredAt,
                    LedgerAccounts.UnrealizedGainFor("broker-alpha"),
                    0m,
                    100m,
                    description,
                    dimensions)
            ],
            metadata);

        return new LedgerJournalEntryWrite(
            entry,
            AggregateId,
            PeriodId,
            CommandId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
            CorrelationId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "fair-value-policy",
            AccountingPolicyVersion: "v1",
            RuleId: "mark-rule",
            RuleVersion: "v1",
            SourceEventId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            SourceJournalEntryId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: new LedgerAdjustmentApprovalMetadataDto(
                "approval-1",
                LedgerAdjustmentApprovalStatusDto.Approved,
                "controller",
                OccurredAt.AddMinutes(2),
                "daily-valuation",
                EvidenceLink: "evidence://approval/valuation-batch"),
            LedgerBookId: LedgerBookId);
    }

    private static LedgerJournalEntryRecord ToRecord(LedgerJournalEntryWrite write)
        => new(
            write.Entry,
            write.AggregateId,
            write.PeriodId,
            write.CommandId,
            write.CorrelationId,
            1,
            OccurredAt,
            write.AccountingBasis,
            write.AccountingPolicyId,
            write.AccountingPolicyVersion,
            write.RuleId,
            write.RuleVersion,
            write.SourceEventId,
            write.SourceJournalEntryId,
            write.PostingKind,
            write.AdjustmentApproval);

    private static JournalEntry CloneEntry(JournalEntry entry, JournalEntryMetadata metadata)
        => new(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines, metadata);

    private static JournalEntry CloneFirstLine(JournalEntry entry, LedgerLineDimensionSet dimensions)
    {
        var first = entry.Lines[0];
        var lines = entry.Lines.ToArray();
        lines[0] = new LedgerEntry(
            first.EntryId,
            first.JournalEntryId,
            first.Timestamp,
            first.Account,
            first.Debit,
            first.Credit,
            first.Description,
            dimensions);
        return new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata);
    }

    private static LedgerJournalEntryWrite WithLedgerBook(LedgerJournalEntryWrite write, Guid ledgerBookId)
        => write with
        {
            LedgerBookId = ledgerBookId,
            Entry = CloneEntry(
                write.Entry,
                write.Entry.Metadata with { LedgerBook = ledgerBookId.ToString("D") })
        };
}
