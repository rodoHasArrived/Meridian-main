using FluentAssertions;
using Meridian.Contracts.FundStructure;
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

    [Fact]
    public async Task PostAsync_SameCommandWithRegeneratedJournalAndLineIds_ReturnsRetainedJournalId()
    {
        var original = BuildCommandWrite();
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(original);
        normalized.Entry.Metadata.Tags.Should()
            .ContainKey(AccountingPostingCommandValidator.PostingCommandFingerprintTag);
        normalized.Entry.Metadata.Tags![AccountingPostingCommandValidator.PostingCommandFingerprintTag]
            .Should().StartWith("sha256:");
        var retained = new List<LedgerJournalEntryRecord> { ToRecord(normalized) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        var regenerated = original with { Entry = RegenerateEntryIds(original.Entry) };

        var result = await target.PostAsync(regenerated);

        result.WasAppended.Should().BeFalse();
        result.JournalEntryId.Should().Be(normalized.Entry.JournalEntryId);
        result.JournalEntryId.Should().NotBe(regenerated.Entry.JournalEntryId);
        store.Verify(
            candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAsync_FullPostingCommandFingerprint_RejectsSemanticMutations()
    {
        var original = BuildCommandWrite();
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(original);
        var retained = new List<LedgerJournalEntryRecord> { ToRecord(normalized) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        var command = original.PostingCommand!;
        var mutations = new (string Name, AccountingPostingCommandDto Command)[]
        {
            ("causation", command with { CausationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd") }),
            ("posting date", command with { PostingDate = command.PostingDate.AddMinutes(1) }),
            ("expected version", command with { ExpectedVersion = command.ExpectedVersion + 1 }),
            ("operator rationale", command with { OperatorRationale = "Controller approved corrected rationale" }),
            ("book context", command with
            {
                BookContext = command.BookContext! with { DisplayName = "GAAP valuation book - amended" }
            })
        };

        foreach (var mutation in mutations)
        {
            Func<Task> retry = async () =>
                await target.PostAsync(original with { PostingCommand = mutation.Command });

            await retry.Should().ThrowAsync<LedgerValidationException>(mutation.Name)
                .WithMessage("*already retained with different accounting content*");
        }

        store.Verify(
            candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAsync_BookContextMustMatchWriteBookPeriodBasisAndPolicy()
    {
        var original = BuildCommandWrite();
        var command = original.PostingCommand!;
        var context = command.BookContext!;
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        using var target = new DurableLedgerPostingTarget(store.Object);
        var mutations = new (string Name, AccountingBookContextDto Context)[]
        {
            ("book", context with { LedgerBookId = Guid.NewGuid() }),
            ("period", context with { PeriodId = Guid.NewGuid() }),
            ("missing period", context with { PeriodId = null }),
            ("basis", context with { AccountingBasis = AccountingBasisKindDto.Tax }),
            ("policy", context with { AccountingPolicyId = "fair-value-policy-changed" }),
            ("policy version", context with { AccountingPolicyVersion = "v2" })
        };

        foreach (var mutation in mutations)
        {
            Func<Task> post = async () => await target.PostAsync(original with
            {
                PostingCommand = command with { BookContext = mutation.Context }
            });

            await post.Should().ThrowAsync<LedgerValidationException>(mutation.Name)
                .WithMessage("*book context*");
        }

        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PostAsync_GlobalCommandCollision_RejectsAggregateMutationWithRegeneratedIds()
    {
        var original = BuildCommandWrite();
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(original);
        var retained = new List<LedgerJournalEntryRecord> { ToRecord(normalized) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        var changedAggregateId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var retry = original with
        {
            AggregateId = changedAggregateId,
            Entry = RegenerateEntryIds(original.Entry),
            PostingCommand = original.PostingCommand! with { AggregateId = changedAggregateId }
        };

        Func<Task> post = async () => await target.PostAsync(retry);

        await post.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*already retained with different accounting content*");
        store.Verify(
            candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAsync_ValidatesEveryRetainedIdentityCollisionBeforeAcceptingRetry()
    {
        var original = BuildCommandWrite();
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(original);
        var regenerated = RegenerateEntryIds(normalized.Entry);
        var conflicting = normalized with
        {
            Entry = CloneEntry(
                regenerated,
                regenerated.Metadata with
                {
                    EvidenceReferences =
                    [
                        new JournalEvidenceReference(
                            "price-close",
                            "evidence://provider/AAPL/2026-07-08/conflicting",
                            "Source",
                            "trusted-close",
                            OccurredAt,
                            "valuation-worker",
                            ContentHash: "sha256:conflicting")
                    ]
                })
        };
        var retained = new List<LedgerJournalEntryRecord>
        {
            ToRecord(normalized, globalSequence: 1),
            ToRecord(conflicting, globalSequence: 2)
        };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);

        Func<Task> retry = async () => await target.PostAsync(original);

        await retry.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*already retained with different accounting content*");
        store.Verify(
            candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GlobalPostingCommandIdentityMigration_ReplacesAggregateCommandIndex()
    {
        var sql = ReadMigration("V_ledger_025__global_posting_command_identity.sql");

        sql.Should().Contain("drop index if exists __SCHEMA__.ux_journal_entries_aggregate_command");
        sql.Should().Contain("create unique index if not exists ux_journal_entries_command");
        sql.Should().Contain("on __SCHEMA__.journal_entries (command_id)");
        sql.Should().Contain("where command_id is not null");
    }

    [Fact]
    public void PostingIdentityCollisionScope_IsGlobalForJournalAndCommandButAggregateScopedForSourceAndIdempotency()
    {
        var write = BuildWrite();
        var identity = LedgerPostingIdentity.FromWrite(write);
        var otherAggregate = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var otherJournal = Guid.Parse("13131313-1313-1313-1313-131313131313");
        var record = ToRecord(write);

        LedgerPostingIdentityCollisionLookupExtensions.IsCollision(
                record with { AggregateId = otherAggregate },
                identity)
            .Should().BeTrue("journal entry identity is global");
        LedgerPostingIdentityCollisionLookupExtensions.IsCollision(
                record with
                {
                    AggregateId = otherAggregate,
                    Entry = RegenerateEntryIds(record.Entry)
                },
                identity)
            .Should().BeTrue("posting command identity is global");

        var sourceOnlyIdentity = identity with
        {
            JournalEntryId = otherJournal,
            CommandId = Guid.NewGuid(),
            IdempotencyKey = null
        };
        LedgerPostingIdentityCollisionLookupExtensions.IsCollision(record, sourceOnlyIdentity)
            .Should().BeTrue();
        LedgerPostingIdentityCollisionLookupExtensions.IsCollision(
                record with { AggregateId = otherAggregate },
                sourceOnlyIdentity)
            .Should().BeFalse("source-event identity is aggregate scoped");

        var idempotencyOnlyIdentity = sourceOnlyIdentity with
        {
            SourceEventId = Guid.NewGuid(),
            IdempotencyKey = write.Entry.Metadata.IdempotencyKey
        };
        LedgerPostingIdentityCollisionLookupExtensions.IsCollision(record, idempotencyOnlyIdentity)
            .Should().BeTrue();
        LedgerPostingIdentityCollisionLookupExtensions.IsCollision(
                record with { AggregateId = otherAggregate },
                idempotencyOnlyIdentity)
            .Should().BeFalse("idempotency identity is aggregate scoped");
    }

    [Fact]
    public void ProductionAppendBoundary_RejectsWriteWithoutTypedPostingCommand()
    {
        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(
            BuildWrite(),
            requirePostingCommand: true,
            requireExpectedVersion: true);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*typed accounting posting command*");
    }

    [Fact]
    public void ProductionAppendBoundary_RejectsTypedCommandWithoutExpectedVersion()
    {
        var write = BuildCommandWrite();
        write = write with
        {
            PostingCommand = write.PostingCommand! with { ExpectedVersion = null }
        };

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(
            write,
            requirePostingCommand: true,
            requireExpectedVersion: true);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*expected version is required*");
    }

    [Fact]
    public void JournalLegSchema_KeepsTimingAndAmountsAtAPrecisionBelowTheClr()
    {
        var sql = ReadMigration("V_ledger_001__journal_entries.sql");

        // The two facts the replay comparison has to respect: timestamptz is microsecond-resolution
        // while a CLR tick is 100ns, and numeric(38, 10) is ten fractional digits while a decimal
        // carries up to 28. Both reductions happen to the retained side and to it only.
        sql.Should().Contain("occurred_at timestamptz not null");
        sql.Should().Contain("debit numeric(38, 10) not null");
        sql.Should().Contain("credit numeric(38, 10) not null");
    }

    [Fact]
    public async Task PostAsync_ReplayCarryingSubMicrosecondTiming_IsAReplayNotAConflict()
    {
        // Anything derived from DateTimeOffset.UtcNow carries sub-microsecond ticks.
        var write = WithTiming(BuildWrite(), OccurredAt.AddTicks(7));
        var retained = new List<LedgerJournalEntryRecord> { ToStoredRecord(write) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);

        var result = await target.PostAsync(write);

        result.WasAppended.Should().BeFalse();
        result.JournalEntryId.Should().Be(write.Entry.JournalEntryId);
    }

    [Fact]
    public async Task PostAsync_ReplayOfAPreEpochJournalCarryingSubMicrosecondTiming_IsAReplayNotAConflict()
    {
        // Npgsql truncates the signed microsecond delta from 2000-01-01 toward that epoch, so an
        // instant seven ticks before it is stored as the epoch itself, not as the microsecond
        // below. Normalizing on absolute ticks would floor past the value the store returned and
        // report a historical journal's exact replay as a conflict.
        var beforeEpoch = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-7);
        var write = WithTiming(BuildWrite(), beforeEpoch);
        var retained = new List<LedgerJournalEntryRecord> { ToStoredRecord(write) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);

        var result = await target.PostAsync(write);

        result.WasAppended.Should().BeFalse();
    }

    [Fact]
    public async Task PostAsync_ReplayCarryingAmountBeyondStoredScale_IsAReplayNotAConflict()
    {
        var write = WithLineAmount(BuildWrite(), 100.00000000004m);
        var retained = new List<LedgerJournalEntryRecord> { ToStoredRecord(write) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);

        var result = await target.PostAsync(write);

        result.WasAppended.Should().BeFalse();
        result.JournalEntryId.Should().Be(write.Entry.JournalEntryId);
    }

    [Fact]
    public async Task PostAsync_RetryDeclaringDifferentTransactionCurrency_FailsClosed()
    {
        // Same functional USD 100 on both sides; the transaction currency, the amount in it, and
        // the rate that ties them together all differ. The books can only hold one of these.
        var original = WithLegCurrency(BuildWrite(), "EUR", 92m, 1.0869565217m);
        var retained = new List<LedgerJournalEntryRecord> { ToStoredRecord(original) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        var divergent = WithLegCurrency(BuildWrite(), "GBP", 80m, 1.25m);

        Func<Task> retry = async () => await target.PostAsync(divergent);

        await retry.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*already retained with different accounting content*");
        store.Verify(
            candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAsync_ReplayOfABackfilledLegByACurrencyBlindCaller_IsAReplayNotAConflict()
    {
        // V_ledger_029 repairs a currency-blind leg by stamping the identity translation — same
        // currency both sides, transaction amounts equal to the functional ones, rate 1 — and
        // deliberately invents nothing else. Most posting paths still build legs with no currency
        // detail at all, so replaying one of those postings after the repair compares a stamped
        // retained leg against a blind candidate. Treating the stamp as a difference would make
        // exactly the legacy postings the backfill exists to heal permanently unreplayable.
        var retained = new List<LedgerJournalEntryRecord>
        {
            ToStoredRecord(WithLegCurrency(BuildWrite(), "USD", 100m, 1m))
        };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);

        var result = await target.PostAsync(BuildWrite());

        result.WasAppended.Should().BeFalse();
    }

    [Fact]
    public async Task PostAsync_ReplayDeclaringAnIdentityTranslationOverABlindLeg_IsAReplayNotAConflict()
    {
        // The same equivalence in the other direction, for a book repaired after the journal was
        // retained rather than before.
        var retained = new List<LedgerJournalEntryRecord> { ToStoredRecord(BuildWrite()) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);

        var result = await target.PostAsync(WithLegCurrency(BuildWrite(), "USD", 100m, 1m));

        result.WasAppended.Should().BeFalse();
    }

    [Fact]
    public async Task PostAsync_RetryDeclaringForeignCurrencyOverABlindLeg_FailsClosed()
    {
        // A blind leg asserts no conversion. A candidate claiming EUR at a rate asserts one the
        // books never recorded, which is a different posting rather than a missing label.
        var retained = new List<LedgerJournalEntryRecord> { ToStoredRecord(BuildWrite()) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("append must not run"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        var divergent = WithLegCurrency(BuildWrite(), "EUR", 92m, 1.0869565217m);

        Func<Task> retry = async () => await target.PostAsync(divergent);

        await retry.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*already retained with different accounting content*");
    }

    private static Mock<ILedgerJournalStore> BuildStore(
        List<LedgerJournalEntryRecord> retained,
        Action<LedgerJournalEntryWrite> append)
    {
        var store = new Mock<ILedgerJournalStore>();
        store.Setup(candidate => candidate.GetByAggregateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => retained.ToArray());
        store.As<ILedgerPostingIdentityCollisionLookup>()
            .Setup(candidate => candidate.FindPostingIdentityCollisionsAsync(
                It.IsAny<LedgerPostingIdentity>(),
                It.IsAny<CancellationToken>()))
            .Returns((LedgerPostingIdentity identity, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                    retained
                        .Where(record => LedgerPostingIdentityCollisionLookupExtensions.IsCollision(record, identity))
                        .ToArray()));
        store.Setup(candidate => candidate.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
            .Returns<LedgerJournalEntryWrite, CancellationToken>((write, _) =>
            {
                append(write);
                return Task.CompletedTask;
            });
        return store;
    }

    private static LedgerJournalEntryWrite BuildCommandWrite()
    {
        var write = BuildWrite();
        var command = new AccountingPostingCommandDto(
            write.CommandId!.Value,
            write.AggregateId,
            write.PeriodId,
            new DateOnly(2026, 7, 8),
            OccurredAt,
            write.Entry.Metadata.IdempotencyKey!,
            Intent: AccountingPostingIntentDto.Adjustment,
            SourceEventId: write.SourceEventId,
            CorrelationId: write.CorrelationId,
            CausationId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            SourceJournalEntryId: write.SourceJournalEntryId,
            ExpectedVersion: 7,
            SourceEventType: "FairValueMarkAdjustment",
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "approval-1",
            OperatorRationale: "Controller approved daily valuation",
            Evidence:
            [
                new AccountingPostingEvidenceReferenceDto(
                    "price-close",
                    "evidence://provider/AAPL/2026-07-08",
                    AccountingPostingEvidenceKindDto.Source,
                    "trusted-close",
                    OccurredAt,
                    "valuation-worker",
                    ContentHash: "sha256:original")
            ],
            LedgerBookId: LedgerBookId)
        {
            BookContext = new AccountingBookContextDto(
                LedgerBookId,
                "fund-alpha",
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                FundStructureNodeKindDto.Fund,
                "GAAP valuation book",
                "USD",
                AccountingBasisKindDto.Gaap,
                write.AccountingPolicyId,
                write.AccountingPolicyVersion,
                write.PeriodId)
        };

        return write with { PostingCommand = command };
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

    private static LedgerJournalEntryRecord ToRecord(
        LedgerJournalEntryWrite write,
        long globalSequence = 1)
        => new(
            write.Entry,
            write.AggregateId,
            write.PeriodId,
            write.CommandId,
            write.CorrelationId,
            globalSequence,
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

    /// <summary>
    /// Builds the record the durable store would hand back, rather than one built straight from the
    /// in-memory write. Journal and leg timing lands in <c>timestamptz</c> and every leg amount
    /// lands in <c>numeric(38, 10)</c>, so a replay always compares a full-precision candidate
    /// against a value the store has already reduced. <see cref="ToRecord"/> skips that reduction
    /// and so cannot show what a real retry is compared against.
    /// </summary>
    private static LedgerJournalEntryRecord ToStoredRecord(
        LedgerJournalEntryWrite write,
        long globalSequence = 1)
    {
        var lines = write.Entry.Lines
            .Select(line => new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                ToStoredTimestamp(line.Timestamp),
                line.Account,
                ToStoredAmount(line.Debit),
                ToStoredAmount(line.Credit),
                line.Description,
                line.Dimensions,
                line.Currency is null
                    ? null
                    : new LedgerEntryCurrency(
                        line.Currency.TransactionCurrency,
                        line.Currency.FunctionalCurrency,
                        ToStoredAmount(line.Currency.TransactionDebit),
                        ToStoredAmount(line.Currency.TransactionCredit),
                        ToStoredAmount(line.Currency.FxRateToFunctional))))
            .ToArray();
        var entry = new JournalEntry(
            write.Entry.JournalEntryId,
            ToStoredTimestamp(write.Entry.Timestamp),
            write.Entry.Description,
            lines,
            write.Entry.Metadata);
        return ToRecord(write with { Entry = entry }, globalSequence);
    }

    /// <summary>
    /// Npgsql encodes a timestamptz as a signed microsecond delta from 2000-01-01 using integer
    /// division, so sub-microsecond ticks are truncated toward that epoch rather than rounded —
    /// downward after it and upward before it.
    /// </summary>
    private static DateTimeOffset ToStoredTimestamp(DateTimeOffset value)
    {
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var micros = (value.UtcDateTime.Ticks - epoch.Ticks) / TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(epoch.AddTicks(micros * TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
    }

    /// <summary>PostgreSQL rounds <c>numeric</c> half away from zero.</summary>
    private static decimal ToStoredAmount(decimal value)
        => Math.Round(value, 10, MidpointRounding.AwayFromZero);

    private static LedgerJournalEntryWrite WithTiming(
        LedgerJournalEntryWrite write,
        DateTimeOffset timestamp)
        => WithLines(
            write,
            line => new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                line.Dimensions,
                line.Currency),
            timestamp);

    private static LedgerJournalEntryWrite WithLineAmount(LedgerJournalEntryWrite write, decimal amount)
        => WithLines(
            write,
            line => new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit > 0m ? amount : 0m,
                line.Credit > 0m ? amount : 0m,
                line.Description,
                line.Dimensions,
                line.Currency));

    private static LedgerJournalEntryWrite WithLegCurrency(
        LedgerJournalEntryWrite write,
        string transactionCurrency,
        decimal transactionAmount,
        decimal fxRateToFunctional)
        => WithLines(
            write,
            line => new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                line.Dimensions,
                new LedgerEntryCurrency(
                    transactionCurrency,
                    "USD",
                    line.Debit > 0m ? transactionAmount : 0m,
                    line.Credit > 0m ? transactionAmount : 0m,
                    fxRateToFunctional)));

    private static LedgerJournalEntryWrite WithLines(
        LedgerJournalEntryWrite write,
        Func<LedgerEntry, LedgerEntry> project,
        DateTimeOffset? timestamp = null)
    {
        var entry = write.Entry;
        return write with
        {
            Entry = new JournalEntry(
                entry.JournalEntryId,
                timestamp ?? entry.Timestamp,
                entry.Description,
                entry.Lines.Select(project).ToArray(),
                entry.Metadata)
        };
    }

    private static JournalEntry RegenerateEntryIds(JournalEntry entry)
    {
        var journalEntryId = Guid.NewGuid();
        var lines = entry.Lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                line.Dimensions))
            .Reverse()
            .ToArray();
        return new JournalEntry(
            journalEntryId,
            entry.Timestamp,
            entry.Description,
            lines,
            entry.Metadata);
    }

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

    private static string ReadMigration(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName,
                "src",
                "Meridian.Storage",
                "Ledger",
                "Migrations",
                fileName);
            if (File.Exists(path))
                return File.ReadAllText(path);

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
