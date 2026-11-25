using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

/// <summary>
/// Security Master review F2 follow-through: the governed AccountRekey batch moves a legacy
/// ticker-keyed account's balance and every open tax lot onto the SecurityId-keyed account atomically.
/// </summary>
public sealed partial class AtomicTaxLotJournalStoreTests
{
    private const string RekeyTicker = "LOT";
    private static readonly DateTimeOffset RekeyAt = DateTimeOffset.Parse("2026-05-20T15:00:00Z");

    private static LedgerAccount LegacyRekeyAccount =>
        LedgerAccounts.Securities(LedgerSecurityKey.Unresolved(RekeyTicker));

    private static LedgerAccount TargetRekeyAccount =>
        LedgerAccounts.Securities(LedgerSecurityKey.Resolved(TestSecurityId, RekeyTicker));

    [Fact]
    public void AccountRekey_ProposalFromLegacyLots_BuildsAnApprovedCommandThatPassesTheStoreRules()
    {
        var (proposal, _) = BuildRekeyProposal(Guid.NewGuid(), Guid.NewGuid(), periodVersion: 3);

        var command = LedgerSecurityKeyRekeyService.BuildCommand(proposal, RekeyApproval(), RekeyBook(proposal.LedgerBookId));

        command.MutationKind.Should().Be(AtomicTaxLotMutationKind.AccountRekey);
        command.ExpectedPeriodVersion.Should().Be(3);
        command.AccountRekey!.TargetAccount.Should().Be(TargetRekeyAccount);
        command.Journal.PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
        command.Journal.Entry.IsBalanced.Should().BeTrue();
        command.Journal.Entry.Lines.Single(line => line.Debit > 0m).Account.Should().Be(TargetRekeyAccount);
        command.Journal.Entry.Lines.Single(line => line.Credit > 0m).Account.Should().Be(LegacyRekeyAccount);
        var validate = () => LedgerAccountRekeyRules.Validate(command);
        validate.Should().NotThrow();
        command.CanonicalFingerprint.Should().Be(command.WithComputedFingerprint().CanonicalFingerprint);
    }

    [Fact]
    public void AccountRekey_IsNeverAutoApproved()
    {
        var (proposal, _) = BuildRekeyProposal(Guid.NewGuid(), Guid.NewGuid(), periodVersion: 0);
        var pending = RekeyApproval() with
        {
            Approval = RekeyApproval().Approval with { Status = LedgerAdjustmentApprovalStatusDto.Pending }
        };

        var build = () => LedgerSecurityKeyRekeyService.BuildCommand(proposal, pending, RekeyBook(proposal.LedgerBookId));

        build.Should().Throw<InvalidOperationException>().WithMessage("*never auto-approved*");
        var approved = LedgerSecurityKeyRekeyService.BuildCommand(proposal, RekeyApproval(), RekeyBook(proposal.LedgerBookId));
        var unapproved = (approved with
        {
            Journal = approved.Journal with { AdjustmentApproval = null }
        }).WithComputedFingerprint();
        var validate = () => LedgerAccountRekeyRules.Validate(unapproved);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*approved adjustment*");
    }

    [Fact]
    public void AccountRekey_RulesRefuseAnotherTickerAPartialAmountOrBasisThatDoesNotCoverTheBalance()
    {
        var (proposal, _) = BuildRekeyProposal(Guid.NewGuid(), Guid.NewGuid(), periodVersion: 0);
        var instruction = proposal.Instruction;

        var otherTicker = () => LedgerAccountRekeyRules.ValidateInstruction(instruction with
        {
            TargetAccount = LedgerAccounts.Securities(LedgerSecurityKey.Resolved(TestSecurityId, "OTHER"))
        }, Meridian.Contracts.Accounting.Lots.OpenLotDirection.Long);
        otherTicker.Should().Throw<LedgerValidationException>().WithMessage("*same account keyed by SecurityId*");

        var uncovered = () => LedgerAccountRekeyRules.ValidateInstruction(instruction with
        {
            LegacyBalance = instruction.LegacyBalance + 1m
        }, Meridian.Contracts.Accounting.Lots.OpenLotDirection.Long);
        uncovered.Should().Throw<LedgerValidationException>().WithMessage("*lot basis must equal the legacy balance*");

        var command = LedgerSecurityKeyRekeyService.BuildCommand(proposal, RekeyApproval(), RekeyBook(proposal.LedgerBookId));
        var entry = command.Journal.Entry;
        var partial = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
            entry.Lines.Select(line => RebuildLine(line, line.Account,
                line.Debit == 0m ? 0m : line.Debit - 1m,
                line.Credit == 0m ? 0m : line.Credit - 1m)).ToArray(), entry.Metadata);
        var shape = () => LedgerAccountRekeyRules.ValidateJournalShape(partial, instruction);
        shape.Should().Throw<LedgerValidationException>().WithMessage("*whole legacy balance*");
    }

    [Fact]
    public void AccountRekey_ProposalIsBlockedForAmbiguousTickersAndUnpositionedLots()
    {
        var bookId = Guid.NewGuid();
        var period = RekeyPeriod(bookId, Guid.NewGuid(), version: 0);
        var lot = LegacyRekeyLot(bookId, Guid.NewGuid());
        var ambiguous = new LedgerSecurityKeyMappingRow(LegacyRekeyAccount, 10_000m,
            CanonicalSecurityResolution.FromCandidates(new(SecurityIdentifierKind.Ticker, RekeyTicker), RekeyAt,
                [TestSecurityId, Guid.NewGuid()]), TargetAccount: null);

        LedgerSecurityKeyRekeyService.TryPropose(bookId, period, RekeyAt, ambiguous, [lot], out var ambiguousReason)
            .Should().BeNull();
        ambiguousReason.Should().Contain("Ambiguous");

        var unpositioned = LegacyRekeyLot(bookId, Guid.NewGuid()) with { BookPositionId = Guid.Empty };
        LedgerSecurityKeyRekeyService.TryPropose(bookId, period, RekeyAt, ResolvedRekeyRow(10_000m), [unpositioned],
                out var unpositionedReason)
            .Should().BeNull();
        unpositionedReason.Should().Contain("no book position");
    }

    [Fact]
    public void AccountRekey_MultiPositionAccount_MovesEveryPositionInOneBatchWithAReclassPairPerPosition()
    {
        var bookId = Guid.NewGuid();
        var period = RekeyPeriod(bookId, Guid.NewGuid(), version: 0);
        var otherPosition = Guid.Parse("f2f2f2f2-0000-4000-8000-0000000000e1");
        var split = new[]
        {
            LegacyRekeyLot(bookId, Guid.NewGuid()) with { OriginalQuantity = 60m, OpenQuantity = 60m },
            LegacyRekeyLot(bookId, Guid.NewGuid()) with
            {
                LotId = "lot-other-position", OriginalQuantity = 40m, OpenQuantity = 40m, BookPositionId = otherPosition
            }
        };

        var proposal = LedgerSecurityKeyRekeyService.TryPropose(bookId, period, RekeyAt, ResolvedRekeyRow(10_000m), split,
            out var reason);

        proposal.Should().NotBeNull(reason);
        proposal!.BookPositionIds.Should().BeEquivalentTo([TestBookPositionId, otherPosition]);
        proposal.Instruction.Lots.Should().HaveCount(2);
        var command = LedgerSecurityKeyRekeyService.BuildCommand(proposal, RekeyApproval(), RekeyBook(bookId));
        var lines = command.Journal.Entry.Lines;
        lines.Should().HaveCount(4);
        command.Journal.Entry.IsBalanced.Should().BeTrue();
        lines.Where(line => line.Dimensions?.PositionId == TestBookPositionId && line.Account == TargetRekeyAccount)
            .Should().ContainSingle().Which.Debit.Should().Be(6_000m);
        lines.Where(line => line.Dimensions?.PositionId == otherPosition && line.Account == LegacyRekeyAccount)
            .Should().ContainSingle().Which.Credit.Should().Be(4_000m);
        lines.Should().OnlyContain(line => line.Dimensions!.InstrumentId == TestSecurityId);
        var validate = () => LedgerAccountRekeyRules.Validate(command);
        validate.Should().NotThrow();

        // Collapsing the per-position pairs into one unattributed pair no longer matches the lots.
        var instruction = proposal.Instruction;
        var merged = new JournalEntry(command.Journal.Entry.JournalEntryId, command.Journal.Entry.Timestamp,
            command.Journal.Entry.Description,
            [
                RebuildLine(lines[0], TargetRekeyAccount, 10_000m, 0m),
                RebuildLine(lines[1], LegacyRekeyAccount, 0m, 10_000m)
            ], command.Journal.Entry.Metadata);
        var shape = () => LedgerAccountRekeyRules.ValidateJournalShape(merged, instruction);
        shape.Should().Throw<LedgerValidationException>().WithMessage("*per book position*");
    }

    [Fact]
    public void AccountRekey_LabelTakenOnTheSecurityIdAccount_IsRelabeledToItsFirstFreeRekeyForm()
    {
        var bookId = Guid.NewGuid();
        var period = RekeyPeriod(bookId, Guid.NewGuid(), version: 0);
        var lot = LegacyRekeyLot(bookId, Guid.NewGuid());
        var occupied = new HashSet<string>(StringComparer.Ordinal)
        {
            lot.LotId,
            LedgerAccountRekeyRules.RelabelCandidate(lot.LotId, 1)
        };

        var proposal = LedgerSecurityKeyRekeyService.TryPropose(bookId, period, RekeyAt, ResolvedRekeyRow(10_000m), [lot],
            out var reason, occupied);

        proposal.Should().NotBeNull(reason);
        var moved = proposal!.Instruction.Lots.Should().ContainSingle().Subject;
        moved.LotId.Should().Be(lot.LotId, "the instruction keeps the reviewed original label");
        moved.TargetLotId.Should().Be(lot.LotId + "~rekey2");
        LedgerAccountRekeyRules.FinalLotId(moved).Should().Be(lot.LotId + "~rekey2");
        var validate = () => LedgerAccountRekeyRules.Validate(
            LedgerSecurityKeyService_BuildCommand(proposal));
        validate.Should().NotThrow();
    }

    [Fact]
    public void AccountRekey_FreeLabel_IsKept_AndArbitraryOrDuplicateRelabelsAreRefused()
    {
        var bookId = Guid.NewGuid();
        var period = RekeyPeriod(bookId, Guid.NewGuid(), version: 0);
        var lot = LegacyRekeyLot(bookId, Guid.NewGuid());
        var proposal = LedgerSecurityKeyRekeyService.TryPropose(bookId, period, RekeyAt, ResolvedRekeyRow(10_000m), [lot],
            out _, new HashSet<string>(StringComparer.Ordinal) { "some-other-label" });
        proposal!.Instruction.Lots.Single().TargetLotId.Should().BeNull();

        var instruction = proposal.Instruction;
        var arbitrary = () => LedgerAccountRekeyRules.ValidateInstruction(instruction with
        {
            Lots = [instruction.Lots[0] with { TargetLotId = "renamed-lot" }]
        }, Meridian.Contracts.Accounting.Lots.OpenLotDirection.Long);
        arbitrary.Should().Throw<LedgerValidationException>().WithMessage("*~rekey*");

        var half = instruction.Lots[0] with { ExpectedCostBasis = 5_000m };
        var other = half with { TaxLotRecordId = Guid.NewGuid(), LotId = half.LotId + "~rekey" };
        var duplicate = () => LedgerAccountRekeyRules.ValidateInstruction(instruction with
        {
            Lots = [half with { TargetLotId = half.LotId + "~rekey" }, other]
        }, Meridian.Contracts.Accounting.Lots.OpenLotDirection.Long);
        duplicate.Should().Throw<LedgerValidationException>().WithMessage("*same label*");
    }

    [Fact]
    public void AccountRekey_AllRelabelFormsTaken_IsBlockedWithAReason()
    {
        var bookId = Guid.NewGuid();
        var lot = LegacyRekeyLot(bookId, Guid.NewGuid());
        var occupied = Enumerable.Range(1, LedgerAccountRekeyRules.MaxRelabelAttempts)
            .Select(attempt => LedgerAccountRekeyRules.RelabelCandidate(lot.LotId, attempt))
            .Append(lot.LotId)
            .ToHashSet(StringComparer.Ordinal);

        LedgerSecurityKeyRekeyService.TryPropose(bookId, RekeyPeriod(bookId, Guid.NewGuid(), version: 0), RekeyAt,
                ResolvedRekeyRow(10_000m), [lot], out var reason, occupied)
            .Should().BeNull();
        reason.Should().Contain("relabel forms are all taken");
    }

    private static AtomicTaxLotJournalCommand LedgerSecurityKeyService_BuildCommand(LedgerSecurityKeyRekeyProposal proposal)
        => LedgerSecurityKeyRekeyService.BuildCommand(proposal, RekeyApproval(), RekeyBook(proposal.LedgerBookId));

    [Fact]
    public void AccountRekey_FingerprintOfOtherBatchKindsIsUnchanged()
    {
        var acquisition = BuildAcquisitionCommand();
        var serialized = System.Text.Json.JsonSerializer.Serialize(acquisition);

        serialized.Should().NotContain("accountRekey").And.NotContain("AccountRekey");
        acquisition.WithComputedFingerprint().CanonicalFingerprint.Should().Be(acquisition.CanonicalFingerprint);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AppendAssetPostingAsync_AccountRekeyMovesBalanceAndLotsAtomically()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var book = new LedgerBookRecord(ledgerBookId, "fund-rekey", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Re-key Primary Book", "USD", openedAt, openedAt);
        await database.JournalStore.SaveLedgerBookAsync(book);
        await database.JournalStore.SavePeriodAsync(RekeyPeriod(ledgerBookId, periodId, version: 0), expectedVersion: 0);
        var authority = await database.SeedLotReferenceAuthorityAsync(ledgerBookId, TestSecurityId, TestBookPositionId);

        // A holding booked before F2: its acquisition journal and lot are on the ticker-keyed account.
        var acquisition = BuildAcquisitionCommand(ledgerBookId, periodId, expectedPeriodVersion: 0,
            idempotencyKey: "atomic-acquisition:legacy-ticker");
        var entry = acquisition.Journal.Entry;
        var legacyLot = acquisition.AcquisitionLot! with { Account = LegacyRekeyAccount };
        var acquisitionEvidence = BuildEvidence("evidence-acquisition", 'a') with
        {
            SubjectType = "OpenLotAcquisition",
            SubjectId = legacyLot.TaxLotRecordId.ToString("D"),
            EffectiveDate = legacyLot.AcquiredDate
        };
        acquisition = (acquisition with
        {
            ReferenceVersions = authority.Versions,
            AcquisitionLot = legacyLot with
            {
                Acquisition = new(Meridian.Contracts.Accounting.Lots.LotQuantityBasis.Units, "USD", "USD", 1m,
                    10_000m, 10_000m, legacyLot.AcquiredDate, null, [acquisitionEvidence])
            },
            Journal = acquisition.Journal with
            {
                Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
                    [RebuildLine(entry.Lines[0], LegacyRekeyAccount, entry.Lines[0].Debit, entry.Lines[0].Credit), entry.Lines[1]],
                    entry.Metadata with { Symbol = RekeyTicker })
            }
        }).WithComputedFingerprint();
        await database.JournalStore.AppendAssetPostingAsync(acquisition);

        var period = (await database.JournalStore.GetPeriodAsync(periodId))!;
        var openLots = await database.JournalStore.ListOpenTaxLotsAsync(ledgerBookId, LegacyRekeyAccount);
        var proposal = LedgerSecurityKeyRekeyService.TryPropose(ledgerBookId, period, RekeyAt,
            ResolvedRekeyRow(10_000m), openLots, out var reason);
        proposal.Should().NotBeNull(reason);
        var command = LedgerSecurityKeyRekeyService.BuildCommand(proposal!, RekeyApproval(), book);

        // A reviewed state that no longer matches the lot is refused without side effects.
        var stale = (command with
        {
            AccountRekey = command.AccountRekey! with
            {
                Lots = [command.AccountRekey.Lots[0] with { ExpectedVersion = command.AccountRekey.Lots[0].ExpectedVersion + 1 }]
            }
        }).WithComputedFingerprint();
        var staleAct = () => database.JournalStore.AppendAssetPostingAsync(stale);
        await staleAct.Should().ThrowAsync<LedgerValidationException>().WithMessage("*changed after review*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();

        var result = await database.JournalStore.AppendAssetPostingAsync(command);

        result.MutationKind.Should().Be(AtomicTaxLotMutationKind.AccountRekey);
        result.IsExactReplay.Should().BeFalse();
        result.AccountRekey.Should().NotBeNull();
        var moved = result.MutatedLots.Should().ContainSingle().Subject;
        moved.Account.Should().Be(TargetRekeyAccount);
        moved.TaxLotRecordId.Should().Be(legacyLot.TaxLotRecordId);
        moved.Version.Should().Be(legacyLot.Version + 1);
        moved.OpenQuantity.Should().Be(100m);
        result.Mutations.Should().ContainSingle().Which.CostBasis.Should().Be(10_000m);

        (await database.JournalStore.ListOpenTaxLotsAsync(ledgerBookId, LegacyRekeyAccount)).Should().BeEmpty();
        (await database.JournalStore.ListOpenTaxLotsAsync(ledgerBookId, TargetRekeyAccount))
            .Should().ContainSingle().Which.TaxLotRecordId.Should().Be(legacyLot.TaxLotRecordId);
        // As-of reads rebuild from the current row, so the lot's earlier history reads under the new key.
        (await database.JournalStore.ListOpenTaxLotsByAssetScopeAsync(ledgerBookId, TestSecurityId, TestBookPositionId,
                new DateOnly(2026, 5, 15)))
            .Should().ContainSingle().Which.Account.Should().Be(TargetRekeyAccount);

        var balances = await database.JournalStore.ReadTrialBalanceAsOfAsync(ledgerBookId, RekeyAt.AddDays(1));
        balances.GetValueOrDefault(LegacyRekeyAccount).Should().Be(0m);
        balances.GetValueOrDefault(TargetRekeyAccount).Should().Be(10_000m);

        var replay = await database.JournalStore.AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.MutatedLots.Should().ContainSingle().Which.Version.Should().Be(moved.Version);
    }

    private static LedgerEntry RebuildLine(LedgerEntry line, LedgerAccount account, decimal debit, decimal credit)
        => new(line.EntryId, line.JournalEntryId, line.Timestamp, account, debit, credit, line.Description, line.Dimensions,
            line.Currency is null ? null : new LedgerEntryCurrency(line.Currency.TransactionCurrency,
                line.Currency.FunctionalCurrency, debit, credit, line.Currency.FxRateToFunctional));

    private static (LedgerSecurityKeyRekeyProposal Proposal, LedgerTaxLotRecord Lot) BuildRekeyProposal(
        Guid bookId, Guid periodId, long periodVersion)
    {
        var lot = LegacyRekeyLot(bookId, Guid.NewGuid());
        var proposal = LedgerSecurityKeyRekeyService.TryPropose(bookId, RekeyPeriod(bookId, periodId, periodVersion),
            RekeyAt, ResolvedRekeyRow(10_000m), [lot], out var reason);
        proposal.Should().NotBeNull(reason);
        return (proposal!, lot);
    }

    private static LedgerSecurityKeyMappingRow ResolvedRekeyRow(decimal balance)
        => new(LegacyRekeyAccount, balance,
            CanonicalSecurityResolution.FromCandidates(new(SecurityIdentifierKind.Ticker, RekeyTicker), RekeyAt, [TestSecurityId]),
            TargetRekeyAccount);

    private static LedgerTaxLotRecord LegacyRekeyLot(Guid bookId, Guid lotId)
        => new(lotId, bookId, LegacyRekeyAccount, "lot-legacy-20260512", new DateOnly(2026, 5, 12),
            OriginalQuantity: 100m, OpenQuantity: 100m, UnitCost: 100m, Currency: "USD",
            CreatedAt: RekeyAt.AddDays(-8), UpdatedAt: RekeyAt.AddDays(-8), Version: 1,
            SecurityId: TestSecurityId, BookPositionId: TestBookPositionId);

    private static LedgerAccountingPeriod RekeyPeriod(Guid bookId, Guid periodId, long version)
        => new(periodId, bookId, 2026, 5, "2026-05", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "Open",
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"), ClosedAt: null, Version: version);

    private static LedgerBookRecord RekeyBook(Guid bookId)
        => new(bookId, "fund-rekey", Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Re-key Primary Book", "USD",
            RekeyAt.AddDays(-30), RekeyAt.AddDays(-30));

    private static LedgerSecurityKeyRekeyApproval RekeyApproval()
    {
        var tags = SecurityMasterLineageTags(TestSecurityId);
        return new LedgerSecurityKeyRekeyApproval(
            new LedgerAdjustmentApprovalMetadataDto("approval-rekey-1", LedgerAdjustmentApprovalStatusDto.Approved,
                "fund-controller", RekeyAt.AddHours(-1), "security-key-rekey", GovernanceCaseId: "F2-rekey"),
            tags["securityMasterProvenance"],
            tags["securityMasterLineage"],
            BuildEvidence("evidence-rekey-mapping", 'd') with { SubjectType = LedgerAccountRekeyRules.EvidenceSubjectType });
    }
}
