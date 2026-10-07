using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Equivalency;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    private static readonly DateOnly AmortAcquired = new(2025, 1, 1);
    private static readonly DateOnly AmortAsOf = new(2026, 1, 1);
    private static readonly LedgerAccount AmortAccount = new("Investment lots", LedgerAccountType.Asset);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanonicalAmortization_ConstantYield_AgreesWithIndependentlyDiscountedCashFlows(bool premium)
    {
        // Independent annual cash-flow present value. Premium: 10/1.05 + 110/1.05^2;
        // discount: 100/1.05^2. First-year basis is price*1.05 less the first coupon.
        var coupon = premium ? 10m : 0m;
        var price = decimal.Round(coupon / 1.05m + (100m + coupon) / (1.05m * 1.05m),
            12, MidpointRounding.ToEven);
        var expected = decimal.Round(price * 1.05m - coupon, 12, MidpointRounding.ToEven);
        var instruction = AmortPureInstruction(price, coupon, BondAmortizationMethod.ConstantYield, 0.05m);

        var result = OpenLotAmortization.Project(instruction);

        result.TransactionCostBasis.Should().Be(expected);
        result.FunctionalCostBasis.Should().Be(decimal.Round(expected * 1.1m, 12, MidpointRounding.ToEven));
        result.FunctionalMovement.Should().Be(result.FunctionalCostBasis - instruction.ExpectedLot.OpenFunctionalCostBasis);
        result.TransactionMovement.Should().BeApproximately(premium ? -4.535147392290m : 4.535147392290m, 0.000000000001m);
    }

    [Fact]
    public void CanonicalAmortization_RejectsMissingTermsUnsupportedMethodStaleEvidenceAndInconsistentYield()
    {
        var instruction = AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);
        var invalid = new[]
        {
            instruction with { Security = instruction.Security with { CommonTerms = JsonSerializer.SerializeToElement(new { }) } },
            instruction with { SecurityEvidence = instruction.SecurityEvidence with { EvidenceVersion = 2 } },
            instruction with { SecurityEvidence = instruction.SecurityEvidence with { ContentHashSha256 = new string('f', 64) } },
            instruction with { ExpectedLot = instruction.ExpectedLot with { Acquisition = instruction.ExpectedLot.Acquisition with
            { FaceValueTerms = instruction.ExpectedLot.Acquisition.FaceValueTerms! with { AmortizationMethod = BondAmortizationMethod.AuctionRate } } } },
            instruction with { ExpectedLot = instruction.ExpectedLot with { Acquisition = instruction.ExpectedLot.Acquisition with
            { FaceValueTerms = new(100m, 1m, BondAmortizationMethod.ConstantYield, 0.05m) } } }
        };
        foreach (var item in invalid)
        {
            var act = () => OpenLotAmortization.Project(item);
            act.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void CanonicalAmortization_RoundsCumulativeTargetsWithoutAccumulatingRoundedPeriodDeltas()
    {
        var instruction = AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null)
            with
        { AsOfDate = new DateOnly(2025, 5, 1) };
        var first = OpenLotAmortization.Project(instruction);
        first.TransactionCostBasis.Should().Be(108.333333333333m);
        first.FunctionalCostBasis.Should().Be(119.166666666666m);
        var next = instruction with
        {
            AsOfDate = new DateOnly(2025, 9, 1),
            ExpectedLot = instruction.ExpectedLot with
            { OpenTransactionCostBasis = first.TransactionCostBasis, OpenFunctionalCostBasis = first.FunctionalCostBasis, Version = 2 }
        };
        var second = OpenLotAmortization.Project(next);
        second.TransactionCostBasis.Should().Be(106.666666666667m);
        second.FunctionalCostBasis.Should().Be(117.333333333334m);
        (first.FunctionalMovement + second.FunctionalMovement).Should().Be(second.FunctionalCostBasis - instruction.ExpectedLot.OpenFunctionalCostBasis);
        next.ExpectedLot.Acquisition.Should().BeEquivalentTo(instruction.ExpectedLot.Acquisition);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_PartialPremium_ReloadReplayAndJournalMovementTie()
        => await AssertAmortRoundTripAsync(premium: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_PartialDiscount_ReloadReplayAndJournalMovementTie()
        => await AssertAmortRoundTripAsync(premium: false);

    private static async Task AssertAmortRoundTripAsync(bool premium)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium);
        var command = fixture.Command();
        var before = command.Amortization!.ExpectedLot;
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        var reloaded = (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().ToOpenLot();

        result.IsExactReplay.Should().BeFalse();
        result.Mutations.Should().ContainSingle();
        var mutation = result.Mutations.Single();
        mutation.MutationKind.Should().Be(AtomicTaxLotMutationKind.Amortization);
        mutation.QuantityDelta.Should().Be(0m);
        mutation.ExpectedVersion.Should().Be(before.Version);
        mutation.ResultVersion.Should().Be(before.Version + 1);
        mutation.RetainedEvidence.Should().Contain(command.Amortization.SecurityEvidence);
        reloaded.OpenQuantity.Should().Be(6_000m);
        reloaded.OpenTransactionCostBasis.Should().Be(premium ? 6_300m : 5_700m);
        reloaded.OpenFunctionalCostBasis.Should().Be(premium ? 6_930m : 6_270m);
        reloaded.Acquisition.Should().BeEquivalentTo(before.Acquisition);
        reloaded.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
        var assetMovement = result.Journal.Entry.Lines.Where(line => line.Account == AmortAccount)
            .Sum(line => line.Debit - line.Credit);
        assetMovement.Should().Be(reloaded.OpenFunctionalCostBasis - before.OpenFunctionalCostBasis);
        mutation.CostBasis.Should().Be(Math.Abs(assetMovement));
        var retained = (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId))!;
        JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(retained.Mutations.Single().LotAfter.BasisAdjustment!.Amortization),
            JsonSerializer.SerializeToElement(command.Amortization)).Should().BeTrue();

        var changedInstruction = command.Amortization! with { ExpectedBookPositionVersion = 2 };
        var changedReplay = (command with
        {
            Amortization = changedInstruction,
            Journal = command.Journal with
            { PostingCommand = command.Journal.PostingCommand! with { LotAmortization = changedInstruction } }
        }).WithComputedFingerprint();
        var collide = () => fixture.Restart().AppendAssetPostingAsync(changedReplay);
        await collide.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");

        // Exact retry remains recoverable even after the period is locked.
        await fixture.LockPeriodAsync();
        await fixture.Securities.UpsertProjectionAsync(fixture.Security with { Version = 2 });
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Single().MutationRecordId.Should().Be(mutation.MutationRecordId);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().Version.Should().Be(before.Version + 1);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_LaterFifoDisposalRelievesAmortizedBasisAndReportingCertifiesIt()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var amortized = (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single();
        amortized.ToOpenLot().OpenFunctionalCostBasis.Should().Be(6_930m,
            "the premium lot carries its amortized basis, not quantity times acquisition unit cost");
        (amortized.OpenQuantity * amortized.UnitCost).Should().NotBe(6_930m);

        // A third of the remaining face relieves a third of the amortized basis.
        var partial = await fixture.CurrentBasisDisposalAsync(20m, new DateOnly(2026, 2, 1), "amort-current-partial");
        partial.Relief.FunctionalCostBasis.Should().Be(2_310m);
        partial.Relief.TransactionCostBasis.Should().Be(2_100m);
        var posted = await fixture.Store.AppendAssetPostingAsync(partial.Command);
        posted.Mutations.Single().CostBasis.Should().Be(2_310m);
        var remaining = (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().ToOpenLot();
        remaining.OpenFunctionalCostBasis.Should().Be(4_620m);
        remaining.OpenTransactionCostBasis.Should().Be(4_200m);
        remaining.Acquisition.Should().BeEquivalentTo(amortized.ToOpenLot().Acquisition);
        (await fixture.Restart().AppendAssetPostingAsync(partial.Command)).IsExactReplay.Should().BeTrue();

        // Closing relieves exactly the amortized basis that remains.
        var closing = await fixture.CurrentBasisDisposalAsync(40m, new DateOnly(2026, 3, 1), "amort-current-closing");
        closing.Relief.FunctionalCostBasis.Should().Be(4_620m);
        await fixture.Store.AppendAssetPostingAsync(closing.Command);
        (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Should().BeEmpty();

        var journals = (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId))
            .ToDictionary(static item => item.Entry.JournalEntryId, static item => item.Entry);
        var history = await fixture.Store.GetTaxLotDisposalHistoryAsync(fixture.BookId,
            [partial.Command.Journal.Entry.JournalEntryId, closing.Command.Journal.Entry.JournalEntryId]);
        foreach (var (command, relief) in new[] { partial, closing })
        {
            var record = history.Single(item => item.MutationBatchId == command.MutationBatchId);
            CanonicalDisposalHistoryProjector.Project(record, journals[command.Journal.Entry.JournalEntryId],
                fixture.BookId, "USD").CostBasis.Should().Be(relief.FunctionalCostBasis);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ReferenceChangesAndLockedPeriod_LeaveNoJournalOrMutation()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        await fixture.Securities.UpsertProjectionAsync(fixture.Security with { Version = 2 });
        var stale = () => fixture.Store.AppendAssetPostingAsync(command);
        await stale.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*stale*");
        await fixture.Securities.UpsertProjectionAsync(fixture.Security);
        await fixture.LockPeriodAsync();
        var lockedCommand = fixture.Command();
        var locked = () => fixture.Store.AppendAssetPostingAsync(lockedCommand);
        await locked.Should().ThrowAsync<LedgerValidationException>().WithMessage("*closed*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().ToOpenLot().Should().BeEquivalentTo(command.Amortization!.ExpectedLot);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_LaterMutationFailure_RollsBackJournalBasisEvidenceAndSupportsRetry()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var injection = connection.CreateCommand();
        injection.CommandText = $"""
            create function "{fixture.Options.SchemaName}".reject_test_amortization() returns trigger
            language plpgsql as $$ begin raise exception 'injected amortization mutation failure'; end $$;
            create trigger reject_test_amortization before insert on "{fixture.Options.SchemaName}".tax_lot_mutations
            for each row when (new.mutation_kind = 'Amortization')
            execute function "{fixture.Options.SchemaName}".reject_test_amortization();
            """;
        await injection.ExecuteNonQueryAsync();
        var fail = () => fixture.Store.AppendAssetPostingAsync(command);
        await fail.Should().ThrowAsync<PostgresException>().WithMessage("*injected amortization mutation failure*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().ToOpenLot().Should().BeEquivalentTo(command.Amortization!.ExpectedLot);
        injection.CommandText = $"drop trigger reject_test_amortization on \"{fixture.Options.SchemaName}\".tax_lot_mutations";
        await injection.ExecuteNonQueryAsync();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ConcurrentReviewedCommands_CommitOnceAndRejectStaleLot()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var first = fixture.Command();
        var second = fixture.Command();
        async Task<bool> AttemptAsync(AtomicTaxLotJournalCommand command)
        {
            try
            { await fixture.Restart().AppendAssetPostingAsync(command); return true; }
            catch (LedgerValidationException) { return false; }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure) { return false; }
        }
        var outcomes = await Task.WhenAll(AttemptAsync(first), AttemptAsync(second));
        outcomes.Count(value => value).Should().Be(1);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        var loser = outcomes[0] ? second : first;
        var stale = () => fixture.Restart().AppendAssetPostingAsync(loser);
        await stale.Should().ThrowAsync<LedgerValidationException>().WithMessage("*lot version or carrying basis changed*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(loser.MutationBatchId)).Should().BeNull();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_BalancedJournalWithWrongCarryingMovement_RollsBackAllWrites()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command(amountOffset: 1m);
        var fail = () => fixture.Store.AppendAssetPostingAsync(command);
        await fail.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().ToOpenLot().Should().BeEquivalentTo(command.Amortization!.ExpectedLot);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ChangedAcquisitionCurrencyFxOrTransactionMovement_LeavesNoWrites()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        var entry = command.Journal.Entry;
        var substitutions = new (string Currency, decimal FxRate, decimal TransactionOffset)[]
        {
            ("USD", 1m, 0m),
            ("EUR", 1m, 0m),
            ("EUR", 1.1m, 0.001m)
        };
        foreach (var substitution in substitutions)
        {
            // Each replacement still balances and satisfies the ordinary leg conversion
            // tolerance. Canonical amortization must bind the original acquisition evidence.
            var lines = entry.Lines.Select(line =>
            {
                var amount = (line.Debit + line.Credit) / substitution.FxRate + substitution.TransactionOffset;
                return new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                    line.Debit, line.Credit, line.Description, line.Dimensions,
                    new LedgerEntryCurrency(substitution.Currency, "USD", line.Debit > 0m ? amount : 0m,
                        line.Credit > 0m ? amount : 0m, substitution.FxRate));
            }).ToArray();
            var changed = (command with
            {
                Journal = command.Journal with
                {
                    Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata)
                }
            }).WithComputedFingerprint();
            var post = () => fixture.Restart().AppendAssetPostingAsync(changed);

            await post.Should().ThrowAsync<LedgerValidationException>();
            (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
            (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
            (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().ToOpenLot()
                .Should().BeEquivalentTo(command.Amortization!.ExpectedLot);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ChangedBookPositionVersion_RequiresFreshReviewedInputs()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        await fixture.AdvancePositionAsync();
        var stale = () => fixture.Restart().AppendAssetPostingAsync(command);
        await stale.Should().ThrowAsync<LedgerValidationException>().WithMessage("*book-position version*stale*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_MissingOrUnknownCalculationVersion_RefusesNewPostingWithoutMutation()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        foreach (var version in new string?[] { null, "unknown-amortization-model" })
        {
            var instruction = command.Amortization! with { CalculationVersion = version };
            var invalid = command with
            {
                Amortization = instruction,
                Journal = command.Journal with
                { PostingCommand = command.Journal.PostingCommand! with { LotAmortization = instruction } }
            };
            // Historical v1 still has a valid canonical fingerprint. Unknown versions fail
            // command normalization before fingerprint calculation or database access.
            if (version is null)
                invalid = invalid.WithComputedFingerprint();
            var post = () => fixture.Restart().AppendAssetPostingAsync(invalid);
            await post.Should().ThrowAsync<LedgerValidationException>().WithMessage(version is null
                ? "New amortization postings require a fresh preview using the current calculation version."
                : "Amortization calculation version is unsupported.");
            await AssertAmortizationUnchangedAsync(fixture, command);
        }
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_AliasInsertedAfterPreviewWithUnchangedSecurityVersion_RefusesPosting()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        var at = new DateTimeOffset(AmortAsOf.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        await fixture.Securities.UpsertAliasAsync(new(Guid.NewGuid(), fixture.Security.SecurityId,
            "Ticker", "AMORT-UPDATED", null, SecurityAliasScope.Operations, "Reviewed alias addition",
            "reference-reviewer", at, at, null, true));
        var current = (await fixture.Securities.GetProjectionAsync(fixture.Security.SecurityId))!;
        current.Version.Should().Be(command.Amortization!.Security.Version);
        OpenLotAmortization.SecurityHash(current).Should().NotBe(OpenLotAmortization.SecurityHash(command.Amortization.Security));

        var post = () => fixture.Restart().AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*stale*");
        await AssertAmortizationUnchangedAsync(fixture, command);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_RequiresExplicitGovernedSourceAndExactReviewedInstruction()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        var posting = command.Journal.PostingCommand!;
        var legacy = posting with
        {
            SourceEventType = null,
            BookContext = null,
            BookPositionId = null,
            EconomicEvent = null,
            ProjectionLineage = null,
            RulePackReference = null,
            Evidence = [],
            LotAmortization = null
        };
        var eventOnly = posting.Evidence.Where(item => item.SubjectType == AssetAccountingEvidenceSubjects.Event).ToArray();
        var incomeType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Income);
        AccountingPostingCommandDto?[] variants =
        [
            null,
            legacy,
            legacy with { SourceEventType = "" },
            legacy with { SourceEventType = "  " },
            legacy with { SourceEventType = "LegacyAmortization" },
            legacy with { SourceEventType = "AssetAccounting.Unknown" },
            posting with
            {
                SourceEventType = incomeType, LotAmortization = null, Evidence = eventOnly,
                EconomicEvent = posting.EconomicEvent! with { EventType = incomeType }
            },
            posting with { LotAmortization = null, Evidence = eventOnly },
            posting with { LotAmortization = command.Amortization! with { ExpectedBookPositionVersion = 2 } }
        ];
        // Even hosts permitting legacy generic journal writes must require the complete
        // governed instruction for amortization. Every variant is otherwise normalizable.
        var compatibilityStore = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions
        { ConnectionString = fixture.Options.ConnectionString, SchemaName = fixture.Options.SchemaName });
        foreach (var changed in variants)
        {
            var invalid = (command with { Journal = command.Journal with { PostingCommand = changed } }).WithComputedFingerprint();
            var post = () => compatibilityStore.AppendAssetPostingAsync(invalid);
            await post.Should().ThrowAsync<LedgerValidationException>()
                .WithMessage("The governed journal must retain the exact reviewed canonical amortization inputs.");
            await AssertAmortizationUnchangedAsync(fixture, command);
        }

        var missingAtomicInstruction = (command with { Amortization = null }).WithComputedFingerprint();
        var missing = () => fixture.Restart().AppendAssetPostingAsync(missingAtomicInstruction);
        await missing.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("Atomic amortization requires retained reviewed lot and Security Master inputs.");
        await AssertAmortizationUnchangedAsync(fixture, command);
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_RequiresExactAcquisitionAndSecurityEvidenceAtBothBoundaries()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        var posting = command.Journal.PostingCommand!;
        foreach (var reviewed in command.Amortization!.ExpectedLot.Acquisition.Evidence.Append(command.Amortization.SecurityEvidence))
        {
            AtomicTaxLotJournalCommand[] variants =
            [
                command with { RetainedEvidence = command.RetainedEvidence.Where(item => item.EvidenceId != reviewed.EvidenceId).ToArray() },
                command with { RetainedEvidence = command.RetainedEvidence.Select(item => item.EvidenceId == reviewed.EvidenceId
                    ? item with { ContentHashSha256 = new string('f', 64) } : item).ToArray() },
                command with { Journal = command.Journal with { PostingCommand = posting with
                    { Evidence = posting.Evidence.Where(item => item.EvidenceId != reviewed.EvidenceId).ToArray() } } },
                command with { Journal = command.Journal with { PostingCommand = posting with
                    { Evidence = posting.Evidence.Select(item => item.EvidenceId == reviewed.EvidenceId
                        ? item with { SourceReference = "different-reference", EffectiveDate = posting.EffectiveDate } : item).ToArray() } } },
                command with { Journal = command.Journal with { PostingCommand = posting with
                    { Evidence = posting.Evidence.Select(item => item.EvidenceId == reviewed.EvidenceId
                        ? item with { Kind = AccountingPostingEvidenceKindDto.Approval, EffectiveDate = posting.EffectiveDate } : item).ToArray() } } }
            ];
            foreach (var variant in variants)
            {
                var invalid = variant.WithComputedFingerprint();
                var post = () => fixture.Restart().AppendAssetPostingAsync(invalid);
                await post.Should().ThrowAsync<LedgerValidationException>()
                    .WithMessage("Amortization must bind the exact journal scope, effective date, acquisition and reference evidence.");
                await AssertAmortizationUnchangedAsync(fixture, command);
            }
        }
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    private static async Task AssertAmortizationUnchangedAsync(AmortFixture fixture, AtomicTaxLotJournalCommand command)
    {
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement((await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().ToOpenLot()),
            JsonSerializer.SerializeToElement(command.Amortization!.ExpectedLot)).Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_PremiumCorrection_RestoresPriorBasisAndRebooksWithMonotonicVersions()
        => await AssertAmortCorrectionRoundTripAsync(premium: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_DiscountCorrection_RestoresPriorBasisAndRebooksWithMonotonicVersions()
        => await AssertAmortCorrectionRoundTripAsync(premium: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_FirstPremiumCorrection_RestoresNullBasisAndRebooksWithMonotonicVersions()
        => await AssertAmortCorrectionRoundTripAsync(premium: true, hasPriorAdjustment: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_FirstDiscountCorrection_RestoresNullBasisAndRebooksWithMonotonicVersions()
        => await AssertAmortCorrectionRoundTripAsync(premium: false, hasPriorAdjustment: false);

    private static async Task AssertAmortCorrectionRoundTripAsync(bool premium, bool hasPriorAdjustment = true)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium);
        var first = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var original = hasPriorAdjustment
            ? await fixture.Restart().AppendAssetPostingAsync(fixture.Command(
                expectedLot: first.MutatedLots.Single().ToOpenLot(), asOf: new DateOnly(2026, 7, 1)))
            : first;
        var originalMutation = original.Mutations.Single();
        if (hasPriorAdjustment)
            originalMutation.LotBefore!.BasisAdjustment.Should().NotBeNull();
        else
            originalMutation.LotBefore!.BasisAdjustment.Should().BeNull();
        var reversalCommand = fixture.Reverse(original);

        var reversed = await fixture.Restart().AppendAssetPostingAsync(reversalCommand);
        var restored = (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single();

        reversed.CorrectsMutationBatchId.Should().Be(original.MutationBatchId);
        reversed.Journal.SourceJournalEntryId.Should().Be(original.Journal.Entry.JournalEntryId);
        reversed.Mutations.Single().CorrectsMutationBatchId.Should().Be(original.MutationBatchId);
        restored.ToOpenLot().Should().BeEquivalentTo(originalMutation.LotBefore!.ToOpenLot() with
        { Version = originalMutation.ResultVersion + 1 }, options => AmortizationSnapshotOptions(options));
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(restored.BasisAdjustment),
            JsonSerializer.SerializeToElement(originalMutation.LotBefore.BasisAdjustment)).Should().BeTrue();
        foreach (var line in original.Journal.Entry.Lines)
        {
            var inverse = reversed.Journal.Entry.Lines.Single(candidate => candidate.Account == line.Account);
            inverse.Debit.Should().Be(line.Credit);
            inverse.Credit.Should().Be(line.Debit);
            inverse.Dimensions.Should().BeEquivalentTo(line.Dimensions);
            inverse.Currency.Should().BeEquivalentTo(new LedgerEntryCurrency(line.Currency!.TransactionCurrency,
                line.Currency.FunctionalCurrency, line.Currency.TransactionCredit, line.Currency.TransactionDebit,
                line.Currency.FxRateToFunctional));
        }
        var reverseReplay = await fixture.Restart().AppendAssetPostingAsync(reversalCommand);
        reverseReplay.IsExactReplay.Should().BeTrue();
        reverseReplay.Mutations.Single().MutationRecordId.Should().Be(reversed.Mutations.Single().MutationRecordId);

        // A same-date rebook is governed by the reversal lineage, rather than being mistaken
        // for a second ordinary posting of the original period's amortization.
        var rebookCommand = fixture.Command(expectedLot: restored.ToOpenLot(),
            asOf: reversalCommand.Amortization!.AsOfDate, corrects: reversed);
        var rebooked = await fixture.Restart().AppendAssetPostingAsync(rebookCommand);
        var reloaded = (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single();
        rebooked.CorrectsMutationBatchId.Should().Be(reversed.MutationBatchId);
        rebooked.Journal.SourceJournalEntryId.Should().Be(reversed.Journal.Entry.JournalEntryId);
        reloaded.ToOpenLot().Should().BeEquivalentTo(originalMutation.LotAfter.ToOpenLot() with
        { Version = originalMutation.ResultVersion + 2 }, options => AmortizationSnapshotOptions(options));
        reloaded.BasisAdjustment!.MutationBatchId.Should().Be(rebooked.MutationBatchId);

        await fixture.LockPeriodAsync();
        (await fixture.Restart().AppendAssetPostingAsync(reversalCommand)).IsExactReplay.Should().BeTrue();
        (await fixture.Restart().AppendAssetPostingAsync(rebookCommand)).IsExactReplay.Should().BeTrue();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(hasPriorAdjustment ? 4 : 3);
        (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().Version
            .Should().Be(originalMutation.ResultVersion + 2);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_UngovernedRemovalCannotUseVersionIncrementOrAnUnrelatedRetainedBatch()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var original = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var mutationStamp in new[]
        {
            "",
            ", version = version + 1",
            ", version = version + 1, last_mutation_batch_id = @unrelated_batch"
        })
        {
            await using var removal = connection.CreateCommand();
            removal.CommandText = $"""
                update "{fixture.Options.SchemaName}".tax_lots
                set basis_adjustment = null {mutationStamp}
                where tax_lot_record_id = @lot;
                """;
            removal.Parameters.AddWithValue("lot", original.MutatedLots.Single().TaxLotRecordId);
            removal.Parameters.AddWithValue("unrelated_batch", original.Mutations.Single().LotBefore!.LastMutationBatchId!.Value);
            var remove = () => removal.ExecuteNonQueryAsync();
            await remove.Should().ThrowAsync<PostgresException>()
                .WithMessage("*Governed lot basis adjustments cannot be removed*");
            (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single()
                .Should().BeEquivalentTo(original.MutatedLots.Single(), options => AmortizationSnapshotOptions(options));
        }
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(original.MutationBatchId))!.Mutations.Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ReversalRejectsForgedRestorationWithoutChangingRetainedState()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var original = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var before = original.Mutations.Single().LotBefore!.ToOpenLot();
        var command = fixture.Reverse(original, before with
        {
            OpenTransactionCostBasis = before.OpenTransactionCostBasis + 1m,
            OpenFunctionalCostBasis = before.OpenFunctionalCostBasis + 1.1m
        });

        await AssertAmortizationRefusedAsync(fixture, command, original.MutatedLots.Single(), journalCount: 1);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ReversalRejectsStaleOriginalAfterLaterAmortization()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var original = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var later = await fixture.Restart().AppendAssetPostingAsync(fixture.Command(
            expectedLot: original.MutatedLots.Single().ToOpenLot(), asOf: new DateOnly(2026, 7, 1)));

        await AssertAmortizationRefusedAsync(fixture, fixture.Reverse(original), later.MutatedLots.Single(), journalCount: 2);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ReversalCannotSubstituteAnotherRetainedJournal()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var original = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var command = fixture.Reverse(original);
        var otherJournal = original.Mutations.Single().LotBefore!.SourceJournalEntryId!.Value;
        var instruction = command.Amortization! with
        { Reversal = command.Amortization.Reversal! with { JournalEntryId = otherJournal } };
        var forged = (command with
        {
            Amortization = instruction,
            Journal = command.Journal with
            {
                SourceJournalEntryId = otherJournal,
                PostingCommand = command.Journal.PostingCommand! with
                { SourceJournalEntryId = otherJournal, LotAmortization = instruction }
            }
        }).WithComputedFingerprint();

        var post = () => fixture.Restart().AppendAssetPostingAsync(forged);
        await post.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("Correction journal lineage must identify the journal retained by the corrected tax-lot batch.");
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(forged.MutationBatchId)).Should().BeNull();
        (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single()
            .Should().BeEquivalentTo(original.MutatedLots.Single(), options => AmortizationSnapshotOptions(options));
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_SameDateAfterReversalRequiresApprovedRebookLineage()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var original = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var reversed = await fixture.Restart().AppendAssetPostingAsync(fixture.Reverse(original));
        var restored = reversed.MutatedLots.Single();
        var ordinary = fixture.Command(expectedLot: restored.ToOpenLot());

        var post = () => fixture.Restart().AppendAssetPostingAsync(ordinary);
        await post.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("Same-date amortization after reversal requires approved rebook lineage*");
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(ordinary.MutationBatchId)).Should().BeNull();
        (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single()
            .Should().BeEquivalentTo(restored, options => AmortizationSnapshotOptions(options));
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ReversalRequiresExactOriginalOffsetAccountDimensionsAndCurrency()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: false);
        var original = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        foreach (var defect in new[] { "account", "dimensions", "currency" })
        {
            var command = fixture.Reverse(original);
            var entry = command.Journal.Entry;
            var lines = entry.Lines.Select(line => line.Account == AmortAccount ? line : new LedgerEntry(
                line.EntryId, line.JournalEntryId, line.Timestamp,
                defect == "account" ? new LedgerAccount("Other amortization income", LedgerAccountType.Revenue) : line.Account,
                line.Debit, line.Credit, line.Description,
                defect == "dimensions" ? line.Dimensions! with { FundId = "other-fund" } : line.Dimensions,
                defect == "currency" ? new LedgerEntryCurrency("GBP", line.Currency!.FunctionalCurrency,
                    line.Currency.TransactionDebit, line.Currency.TransactionCredit, line.Currency.FxRateToFunctional) : line.Currency)).ToArray();
            command = (command with
            {
                Journal = command.Journal with
                { Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) }
            }).WithComputedFingerprint();

            await AssertAmortizationRefusedAsync(fixture, command, original.MutatedLots.Single(), journalCount: 1);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalAmortization_ReversalLateFailureRollsBackJournalRestorationAndEvidenceThenRetries()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var original = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var command = fixture.Reverse(original);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var injection = connection.CreateCommand();
        injection.CommandText = $"""
            create function "{fixture.Options.SchemaName}".reject_test_amort_reversal() returns trigger
            language plpgsql as $$ begin raise exception 'injected amortization reversal failure'; end $$;
            create trigger reject_test_amort_reversal before insert on "{fixture.Options.SchemaName}".tax_lot_mutations
            for each row when (new.mutation_kind = 'Amortization')
            execute function "{fixture.Options.SchemaName}".reject_test_amort_reversal();
            """;
        await injection.ExecuteNonQueryAsync();
        var fail = () => fixture.Restart().AppendAssetPostingAsync(command);
        await fail.Should().ThrowAsync<PostgresException>().WithMessage("*injected amortization reversal failure*");
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single()
            .Should().BeEquivalentTo(original.MutatedLots.Single(), options => AmortizationSnapshotOptions(options));
        injection.CommandText = $"drop trigger reject_test_amort_reversal on \"{fixture.Options.SchemaName}\".tax_lot_mutations";
        await injection.ExecuteNonQueryAsync();
        var reversed = await fixture.Restart().AppendAssetPostingAsync(command);
        reversed.IsExactReplay.Should().BeFalse();
        reversed.MutatedLots.Single().BasisAdjustment.Should().BeNull();
        reversed.MutatedLots.Single().ToOpenLot().Should().BeEquivalentTo(original.Mutations.Single().LotBefore!.ToOpenLot() with
        { Version = original.Mutations.Single().ResultVersion + 1 }, options => AmortizationSnapshotOptions(options));
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);
    }

    private static async Task AssertAmortizationRefusedAsync(AmortFixture fixture,
        AtomicTaxLotJournalCommand command, LedgerTaxLotRecord unchangedLot, int journalCount)
    {
        var post = () => fixture.Restart().AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(journalCount);
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Restart().ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single()
            .Should().BeEquivalentTo(unchangedLot, options => AmortizationSnapshotOptions(options));
    }

    private static EquivalencyOptions<T> AmortizationSnapshotOptions<T>(EquivalencyOptions<T> options)
        => options.Using<JsonElement>(context =>
                JsonElement.DeepEquals(context.Subject, context.Expectation).Should().BeTrue())
            .WhenTypeIs<JsonElement>();

    internal static OpenLotAmortizationInstructionDto AmortPureInstruction(decimal price, decimal coupon,
        BondAmortizationMethod method, decimal? yield)
    {
        var security = AmortSecurity(TestSecurityId, coupon);
        var lotId = Guid.NewGuid();
        var evidence = BuildEvidence("amort-acquisition", 'a') with
        { EffectiveDate = AmortAcquired, SubjectType = "OpenLotAcquisition", SubjectId = lotId.ToString("D") };
        var functionalBasis = decimal.Round(price * 1.1m, 12, MidpointRounding.ToEven);
        var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "EUR", "USD", 1.1m,
            price, functionalBasis, AmortAcquired, new(100m, 1m, method, yield), [evidence]);
        return new(new OpenLotDto(lotId, TestSecurityId, TestBookPositionId, Guid.NewGuid(), "worked-lot", AmortAcquired,
            100m, 100m, price, functionalBasis, 1, acquisition), security, AmortSecurityEvidence(security), 1, AmortAsOf);
    }

    private static SecurityProjectionRecord AmortSecurity(Guid id, decimal coupon = 10m)
    {
        var empty = JsonSerializer.SerializeToElement(new { });
        return new(id, "Bond", SecurityStatusDto.Active, "Worked fixed-rate bullet", "EUR", "ISIN", "TESTAMORT",
            JsonSerializer.SerializeToElement(new
            {
                maturityDate = "2027-01-01",
                dayCountConvention = "30/360",
                couponRate = coupon,
                paymentFrequency = "annual",
                couponType = coupon == 0m ? "ZeroCoupon" : "Fixed",
                isCallable = false
            }),
            empty, empty, 1, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null, [], []);
    }

    private static RetainedEvidenceIdentityDto AmortSecurityEvidence(SecurityProjectionRecord security)
        => BuildEvidence("amort-security", 'b') with
        {
            ContentHashSha256 = OpenLotAmortization.SecurityHash(security),
            EffectiveDate = AmortAcquired,
            SubjectType = "SecurityMasterProjection",
            SubjectId = security.SecurityId.ToString("D"),
            EvidenceVersion = security.Version
        };

    private sealed class AmortFixture : IAsyncDisposable
    {
        private readonly PostgresTestServer _server;
        public LedgerJournalStoreOptions Options { get; }
        public PostgresSecurityMasterStore Securities { get; }
        public PostgresAssetOperationsProjectionStore Positions { get; }
        public PostgresLedgerJournalStore Store { get; }
        public SecurityProjectionRecord Security { get; private set; } = null!;
        public Guid BookId { get; } = Guid.NewGuid();
        public Guid PositionId { get; } = Guid.NewGuid();
        public LedgerAccountingPeriod Period { get; private set; } = null!;
        public LedgerTaxLotRecord Lot { get; private set; } = null!;
        private AccountingBookContextDto BookContext { get; set; } = null!;

        private AmortFixture(PostgresTestServer server, LedgerJournalStoreOptions options,
            PostgresSecurityMasterStore securities, PostgresAssetOperationsProjectionStore positions)
        {
            _server = server;
            Options = options;
            Securities = securities;
            Positions = positions;
            Store = Restart();
        }

        public PostgresLedgerJournalStore Restart() => new(Options, backfillSecurityMaster: () => Securities, backfillPositions: () => Positions);

        public async Task LockPeriodAsync()
            => Period = await Store.SavePeriodAsync(Period with { Status = "HardClosed", ClosedAt = DateTimeOffset.UtcNow }, Period.Version);

        public async Task AdvancePositionAsync()
        {
            var snapshot = await Positions.GetSecurityAsync(Security.SecurityId);
            var position = snapshot.BookPositions.Single();
            await Positions.UpsertAsync(snapshot.InstrumentRoles.Single(), position with { Version = position.Version + 1 },
                null, position.Version, new("independent-controller", "evidence://position-revision", "Review current position", DateTimeOffset.UtcNow));
        }

        public static async Task<AmortFixture> CreateAsync(bool premium)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING");
            var options = new LedgerJournalStoreOptions
            {
                ConnectionString = server.ConnectionString,
                SchemaName = server.CreateSchemaName("amort_ledger"),
                RequireGovernedPostingCommand = true,
                RequireExpectedVersion = true
            };
            var securityOptions = new SecurityMasterOptions
            {
                ConnectionString = server.ConnectionString,
                Schema = server.CreateSchemaName("amort_security"),
                PreloadProjectionCache = false
            };
            var assetOptions = new AssetOperationsOptions { ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("amort_asset") };
            try
            {
                await new LedgerMigrationRunner(options).EnsureMigratedAsync();
                await new SecurityMasterMigrationRunner(securityOptions).EnsureMigratedAsync();
                await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync();
                var fixture = new AmortFixture(server, options, new(securityOptions), new(assetOptions));
                await fixture.SeedAsync(premium);
                return fixture;
            }
            catch { await server.DisposeAsync(); throw; }
        }

        private async Task SeedAsync(bool premium)
        {
            var ownerId = Guid.NewGuid();
            var at = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            await Store.SaveLedgerBookAsync(new(BookId, "amort-fund", ownerId, FundStructureNodeKindDto.Fund,
                "Amortization book", "USD", at, at, AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1"));
            var acquisitionPeriod = await Store.SavePeriodAsync(new(Guid.NewGuid(), BookId, 2025, 1, "2025",
                AmortAcquired, new DateOnly(2025, 12, 31), "Open", at, null, 0), 0);
            Period = await Store.SavePeriodAsync(new(Guid.NewGuid(), BookId, 2026, 1, "2026",
                AmortAsOf, new DateOnly(2026, 12, 31), "Open", at, null, 0), 0);
            Security = AmortSecurity(Guid.NewGuid());
            await Securities.UpsertProjectionAsync(Security);
            Security = (await Securities.GetProjectionAsync(Security.SecurityId))!;
            var evidence = BuildEvidence("amort-owned-source", 'a') with { EffectiveDate = AmortAcquired };
            var origin = new EconomicEventReferenceDto(Guid.NewGuid(), "Trade", 1, AmortAcquired, at, "custodian", "trade-amort")
            { SecurityId = Security.SecurityId, BookPositionId = PositionId, RetainedEvidence = [evidence], EvidenceLinks = [evidence.EvidenceUri] };
            var roleId = Guid.NewGuid();
            var role = new InstrumentRoleDto(roleId, Security.SecurityId, "amort-fund", "Fund", InstrumentRoleKinds.Holder,
                InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, AmortAcquired, OriginEvent: origin, EvidenceLinks: [evidence.EvidenceUri]);
            var book = new AccountingBookContextDto(BookId, "amort-fund", ownerId, FundStructureNodeKindDto.Fund,
                "Amortization book", "USD", AccountingBasisKindDto.Primary, "amort-policy", "1");
            BookContext = book with { PeriodId = Period.PeriodId };
            await Positions.UpsertAsync(role, new BookPositionDto(PositionId, Security.SecurityId, roleId, book,
                BookPositionSides.Long, "Active", AmortAcquired, OriginEvent: origin, EvidenceLinks: [evidence.EvidenceUri])
            { RetainedEvidence = [evidence] }, null, 0, new("independent-controller", "evidence://position-approval", "Review owned position", at));
            var price = premium ? 110m : 90m;
            var lotId = Guid.NewGuid();
            var source = BuildEvidence("amort-acquisition", 'a') with
            { EffectiveDate = AmortAcquired, SubjectType = "OpenLotAcquisition", SubjectId = lotId.ToString("D") };
            var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "EUR", "USD", 1.1m,
                price * 100m, price * 110m, AmortAcquired, new(100m, 1m, BondAmortizationMethod.StraightLine, null), [source]);
            var batchId = Guid.NewGuid();
            var journal = Journal(acquisitionPeriod, AmortAcquired, "amort-acquisition", price * 100m, price * 110m, assetDebit: true);
            var raw = new LedgerTaxLotRecord(lotId, BookId, AmortAccount, "amort-face-lot", AmortAcquired,
                100m, 100m, price * 1.1m, "USD", at, at, journal.Entry.JournalEntryId, source.EvidenceId, 1,
                batchId, batchId, Security.SecurityId, PositionId, OriginalFace: 10_000m, BookedFactor: 1m, ParBasis: 100m, Acquisition: acquisition);
            Lot = (await Store.AppendAssetPostingAsync(AtomicTaxLotJournalCommand.Create(batchId, BookId, journal,
                journal.SourceEventId!.Value, "amort-acquisition", acquisitionPeriod.Version, AtomicTaxLotMutationKind.Acquisition,
                [source], acquisitionLot: raw))).MutatedLots.Single();
            await Store.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), BookId, AmortAccount, LedgerTaxLotReliefMethod.Fifo,
                "amort-fifo-v1", AmortAcquired, at, at));
            var disposalJournal = Journal(acquisitionPeriod, new DateOnly(2025, 6, 1), "amort-partial-disposal",
                price * 40m, price * 44m, assetDebit: false);
            Lot = (await Store.AppendAssetPostingAsync(AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), BookId, disposalJournal,
                disposalJournal.SourceEventId!.Value, "amort-partial-disposal", acquisitionPeriod.Version, AtomicTaxLotMutationKind.Disposal,
                [source], disposalSelections: [new(Lot.TaxLotRecordId, Lot.LotId, Lot.Version, 100m, 40m, 0,
                    source.EvidenceId, price * 1.1m, price * 44m)], reliefMethod: "Fifo", policyRevision: "amort-fifo-v1"))).MutatedLots.Single();
        }

        public AtomicTaxLotJournalCommand Command(decimal amountOffset = 0m, OpenLotDto? expectedLot = null,
            DateOnly? asOf = null, OpenLotAmortizationReversalDto? reversal = null,
            AtomicTaxLotJournalResult? corrects = null)
        {
            var instruction = new OpenLotAmortizationInstructionDto(expectedLot ?? Lot.ToOpenLot(), Security,
                AmortSecurityEvidence(Security), 1, asOf ?? AmortAsOf,
                CalculationVersion: OpenLotAmortization.ModelVersion, Reversal: reversal);
            var projection = OpenLotAmortization.Project(instruction);
            var key = "amort-period:" + Guid.NewGuid().ToString("N");
            var journal = Journal(Period, instruction.AsOfDate, key, Math.Abs(projection.TransactionMovement) + amountOffset,
                Math.Abs(projection.FunctionalMovement) + amountOffset * 1.1m, projection.FunctionalMovement > 0m);
            var eventId = journal.SourceEventId!.Value;
            var eventEvidence = BuildEvidence("amort-event-" + eventId.ToString("N"), 'c') with
            { EffectiveDate = instruction.AsOfDate, SubjectType = AssetAccountingEvidenceSubjects.Event, SubjectId = eventId.ToString("D") };
            RetainedEvidenceIdentityDto[] evidence = [.. instruction.ExpectedLot.Acquisition.Evidence, instruction.SecurityEvidence, eventEvidence];
            var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization);
            var economicEvent = new EconomicEventReferenceDto(eventId, eventType, 1, instruction.AsOfDate,
                journal.Entry.Timestamp, eventEvidence.SourceSystem, eventEvidence.SourceReference,
                SourceContentHash: eventEvidence.ContentHashSha256)
            {
                SecurityId = Security.SecurityId,
                BookPositionId = PositionId,
                RetainedEvidence = evidence,
                EvidenceLinks = evidence.Select(item => item.EvidenceUri).ToArray()
            };
            var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "canonical-lot-amortization",
                OpenLotAmortization.ModelVersion, "amortization-v1", "base", instruction.AsOfDate,
                journal.Entry.Timestamp, eventEvidence.SourceSystem, eventEvidence.SourceReference, economicEvent)
            { BookPositionId = PositionId, RetainedEvidence = evidence, EvidenceLinks = economicEvent.EvidenceLinks };
            var posting = journal.PostingCommand! with
            {
                SourceEventType = eventType,
                BookContext = BookContext,
                BookPositionId = PositionId,
                EconomicEvent = economicEvent,
                ProjectionLineage = lineage,
                RulePackReference = new("canonical-amortization", "1", "amortization", "1"),
                LotAmortization = instruction,
                Evidence = evidence.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                    item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                    item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference,
                    Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate,
                    EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray(),
                Intent = reversal is not null ? AccountingPostingIntentDto.Reversal
                    : corrects is not null ? AccountingPostingIntentDto.Rebook : AccountingPostingIntentDto.Adjustment,
                SourceJournalEntryId = corrects?.Journal.Entry.JournalEntryId
            };
            journal = journal with
            {
                AccountingPolicyId = BookContext.AccountingPolicyId,
                AccountingPolicyVersion = BookContext.AccountingPolicyVersion,
                RuleId = "amortization",
                RuleVersion = "1",
                PostingCommand = posting,
                SourceJournalEntryId = posting.SourceJournalEntryId,
                PostingKind = LedgerPostingKindDto.Adjustment,
                AdjustmentApproval = corrects is null ? null : new LedgerAdjustmentApprovalMetadataDto(
                    posting.ApprovalId!, LedgerAdjustmentApprovalStatusDto.Approved, "independent-controller",
                    journal.Entry.Timestamp, "amortization-correction", EvidenceLink: "evidence://amortization/correction-approval")
            };
            return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), BookId, journal, journal.SourceEventId!.Value,
                key, Period.Version, AtomicTaxLotMutationKind.Amortization,
                evidence, correctsMutationBatchId: corrects?.MutationBatchId, amortization: instruction);
        }

        public AtomicTaxLotJournalCommand Reverse(AtomicTaxLotJournalResult original,
            OpenLotDto? restoresLot = null)
        {
            var mutation = original.Mutations.Single();
            return Command(expectedLot: mutation.LotAfter.ToOpenLot(),
                asOf: mutation.LotAfter.BasisAdjustment!.Amortization!.AsOfDate,
                reversal: new(original.MutationBatchId, original.Journal.Entry.JournalEntryId,
                    restoresLot ?? mutation.LotBefore!.ToOpenLot()), corrects: original);
        }

        /// <summary>
        /// A FIFO disposal of the current (possibly amortized) lot, priced at its certified current
        /// canonical basis so the sale books no gain or loss.
        /// </summary>
        public async Task<(AtomicTaxLotJournalCommand Command, OpenLotReliefResultDto Relief)> CurrentBasisDisposalAsync(
            decimal quantity, DateOnly date, string key)
        {
            var lot = (await Store.ListOpenTaxLotsAsync(BookId, AmortAccount)).Single();
            var canonical = lot.ToOpenLot();
            var relief = new OpenLotReliefService().Select([canonical],
                quantity * LedgerTaxLotFaceValueTerms.LedgerLotParBasis, OpenLotReliefMethod.Fifo);
            var source = canonical.Acquisition.Evidence.Single();
            var journal = Journal(Period, date, key, relief.TransactionCostBasis, relief.FunctionalCostBasis, assetDebit: false);
            return (AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), BookId, journal, journal.SourceEventId!.Value, key,
                Period.Version, AtomicTaxLotMutationKind.Disposal, [source],
                disposalSelections: [new(lot.TaxLotRecordId, lot.LotId, lot.Version, lot.OpenQuantity, quantity, 0,
                    source.EvidenceId, lot.UnitCost, relief.FunctionalCostBasis)],
                reliefMethod: "Fifo", policyRevision: "amort-fifo-v1"), relief);
        }

        private LedgerJournalEntryWrite Journal(LedgerAccountingPeriod period, DateOnly date, string key,
            decimal transactionAmount, decimal functionalAmount, bool assetDebit)
        {
            var id = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            var dimensions = new LedgerLineDimensionSet(InstrumentId: Security.SecurityId) { PositionId = PositionId };
            var offset = key.StartsWith("amort-period:", StringComparison.Ordinal)
                ? new LedgerAccount("Amortization income", LedgerAccountType.Revenue)
                : new LedgerAccount("Cash", LedgerAccountType.Asset);
            LedgerEntry Leg(LedgerAccount account, bool debit) => new(Guid.NewGuid(), id, at, account,
                debit ? functionalAmount : 0m, debit ? 0m : functionalAmount, "Canonical lot movement", dimensions: dimensions,
                currency: new("EUR", "USD", debit ? transactionAmount : 0m, debit ? 0m : transactionAmount, 1.1m));
            var entry = new JournalEntry(id, at, "Canonical lot movement", [Leg(AmortAccount, assetDebit), Leg(offset, !assetDebit)],
                new(SecurityId: Security.SecurityId, EffectiveDate: date, IdempotencyKey: key, Tags: SecurityMasterLineageTags(Security.SecurityId)));
            var posting = new AccountingPostingCommandDto(Guid.NewGuid(), BookId, period.PeriodId, date, at, key,
                AccountingPostingIntentDto.Adjustment, sourceId, ExpectedVersion: period.Version,
                ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
                OperatorRationale: "Independently reviewed fixture economics and owned retained evidence.", LedgerBookId: BookId)
            { Actor = "independent-controller" };
            return new(entry, BookId, period.PeriodId, AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1",
                SourceEventId: sourceId, LedgerBookId: BookId, PostingCommand: posting);
        }

        public ValueTask DisposeAsync() => _server.DisposeAsync();
    }
}
