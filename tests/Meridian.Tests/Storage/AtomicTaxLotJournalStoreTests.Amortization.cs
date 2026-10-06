using System.Text.Json;
using FluentAssertions;
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

        // Exact retry remains recoverable even after the period is locked.
        await fixture.LockPeriodAsync();
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Single().MutationRecordId.Should().Be(mutation.MutationRecordId);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Store.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Single().Version.Should().Be(before.Version + 1);
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
        private Guid PositionId { get; } = Guid.NewGuid();
        public LedgerAccountingPeriod Period { get; private set; } = null!;
        private LedgerTaxLotRecord Lot { get; set; } = null!;

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
            await Store.SaveLedgerBookAsync(new(BookId, "amort-fund", ownerId, FundStructureNodeKindDto.Fund, "Amortization book", "USD", at, at));
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

        public AtomicTaxLotJournalCommand Command(decimal amountOffset = 0m)
        {
            var instruction = new OpenLotAmortizationInstructionDto(Lot.ToOpenLot(), Security, AmortSecurityEvidence(Security), 1, AmortAsOf);
            var projection = OpenLotAmortization.Project(instruction);
            var key = "amort-period:" + Guid.NewGuid().ToString("N");
            var journal = Journal(Period, AmortAsOf, key, Math.Abs(projection.TransactionMovement) + amountOffset,
                Math.Abs(projection.FunctionalMovement) + amountOffset * 1.1m, projection.FunctionalMovement > 0m);
            return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), BookId, journal, journal.SourceEventId!.Value,
                key, Period.Version, AtomicTaxLotMutationKind.Amortization,
                [.. instruction.ExpectedLot.Acquisition.Evidence, instruction.SecurityEvidence], amortization: instruction);
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
            return new(entry, BookId, period.PeriodId, SourceEventId: sourceId, LedgerBookId: BookId, PostingCommand: posting);
        }

        public ValueTask DisposeAsync() => _server.DisposeAsync();
    }
}
