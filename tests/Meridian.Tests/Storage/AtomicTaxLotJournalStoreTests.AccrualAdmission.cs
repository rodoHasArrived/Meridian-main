using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AccrualAdmission_NativeLongPreservesRetainedNoAmortizationPolicy()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var lot = fixture.Lot;

        var snapshots = await fixture.Restart().ListTaxLotAccrualSnapshotsAsync(fixture.BookId,
            lot.SecurityId, lot.BookPositionId, AmortAcquired, AmortAcquired.AddDays(1));

        snapshots.Should().HaveCount(2);
        snapshots.Should().OnlyContain(snapshot => snapshot.AccrualFace == 950_000m
            && snapshot.Lot.PricePercentOfPar == 101.5m
            && snapshot.AmortizationMethod == BondAmortizationMethod.NoAmortization);
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [lot.TaxLotRecordId]))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(lot);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AccrualAdmission_ConvertedFunctionalBasisPreservesNativeQuoteAndFxEvidence()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var lot = fixture.Lot;
        lot.Currency.Should().Be("USD");
        lot.Acquisition!.AcquisitionCurrency.Should().Be("EUR");
        lot.Acquisition.FunctionalCurrency.Should().Be("USD");
        lot.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);

        var snapshots = await fixture.Restart().ListTaxLotAccrualSnapshotsAsync(fixture.BookId,
            lot.SecurityId, lot.BookPositionId, new(2025, 6, 2), new(2025, 6, 2));

        var snapshot = snapshots.Should().ContainSingle().Subject;
        snapshot.AccrualFace.Should().Be(6_000m);
        snapshot.Lot.PricePercentOfPar.Should().Be(121m,
            "the retained quote is the already converted USD basis, not the EUR acquisition quote");
        snapshot.AmortizationMethod.Should().Be(BondAmortizationMethod.StraightLine);
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [lot.TaxLotRecordId]))
            .Should().ContainSingle().Which.Acquisition.Should().BeEquivalentTo(lot.Acquisition);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AccrualAdmission_ForeignBasisRefusesWholeMixedPopulationBeforeCurrencyIsErased()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var bookId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var date = new DateOnly(2026, 5, 1);
        var at = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "accrual-admission", Guid.NewGuid(),
            FundStructureNodeKindDto.Entity, "Synthetic native accrual book", "USD", at, at));
        var lots = new List<LedgerTaxLotRecord>();
        foreach (var currency in new[] { "USD", "EUR" })
        {
            // These are supported pristine compatibility records, not a database-tampering fixture.
            var saved = await database.JournalStore.SaveTaxLotAsync(new(Guid.NewGuid(), bookId,
                new LedgerAccount("Face holdings", LedgerAccountType.Asset), "native-admission-" + currency,
                date, 100m, 100m, 101.5m, currency, at, at, Version: 1,
                SecurityId: securityId, BookPositionId: positionId,
                OriginalFace: 10_000m, BookedFactor: 1m, ParBasis: 100m));
            lots.Add(saved);
        }
        var restarted = database.RestartJournalStore();
        (await restarted.GetTaxLotsByIdsAsync(bookId, lots.Select(lot => lot.TaxLotRecordId).ToArray()))
            .Should().Contain(lot => lot.Currency == "EUR" && lot.OpenQuantity == lot.OriginalQuantity);

        var read = () => restarted.ListTaxLotAccrualSnapshotsAsync(bookId, securityId, positionId, date, date);

        await read.Should().ThrowAsync<LedgerValidationException>().WithMessage("*functional currency*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AccrualAdmission_GovernedShortFaceLotCannotBecomePositiveHolderIncome()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (original, authority) = await PrepareReferencePostingAsync(database, false, BookPositionSides.Short);
        await authority.Securities.UpsertProjectionAsync(AmortSecurity(TestSecurityId) with
        { Currency = "USD", Version = authority.Security.Version + 1 });
        var security = (await authority.Securities.GetProjectionAsync(TestSecurityId))!;
        var liability = original.AcquisitionLot!.Account with { AccountType = LedgerAccountType.Liability };
        var opening = (original with
        {
            Direction = OpenLotDirection.Short,
            ReferenceVersions = new(security.Version, authority.Position.Version),
            AcquisitionLot = original.AcquisitionLot with
            {
                Account = liability,
                OriginalFace = 10_000m,
                BookedFactor = 0.8m,
                ParBasis = 100m,
                Acquisition = original.AcquisitionLot.Acquisition! with
                {
                    Direction = OpenLotDirection.Short,
                    QuantityBasis = LotQuantityBasis.Face,
                    FaceValueTerms = new(100m, 0.8m, BondAmortizationMethod.NoAmortization, null)
                }
            },
            Journal = WithShortLines(original.Journal,
                ShortLotJournalProjector.Open(liability, 10_000m), original.ExpectedPeriodVersion)
        }).WithComputedFingerprint();
        var posted = await database.JournalStore.AppendAssetPostingAsync(opening);
        var lot = posted.MutatedLots.Should().ContainSingle().Subject;
        lot.Acquisition!.Direction.Should().Be(OpenLotDirection.Short);
        lot.HasFaceValueTerms.Should().BeTrue();
        lot.Account.AccountType.Should().Be(LedgerAccountType.Liability);

        var read = () => database.RestartJournalStore().ListTaxLotAccrualSnapshotsAsync(lot.LedgerBookId,
            lot.SecurityId, lot.BookPositionId, lot.AcquiredDate, lot.AcquiredDate);

        await read.Should().ThrowAsync<LedgerValidationException>().WithMessage("*long asset*");
        (await database.JournalStore.GetTaxLotsByIdsAsync(lot.LedgerBookId, [lot.TaxLotRecordId]))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(lot);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AccrualAdmission_CapitalReturnRemainsUnsupportedAfterDisposalMasksLatestReason()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var capitalReturn = await CapitalReturnCommandAsync(fixture);
        var date = capitalReturn.CapitalReturn!.Projection.EconomicEvent!.EffectiveDate;
        await fixture.Store.AppendAssetPostingAsync(capitalReturn);
        var disposal = await fixture.CurrentBasisDisposalAsync(30m, date, "accrual-admission-after-capital-return");
        var disposed = await fixture.Store.AppendAssetPostingAsync(disposal.Command);
        disposed.MutatedLots.Single().BasisAdjustment!.Reason.Should().Be(OpenLotBasisAdjustmentReasons.DisposalRelief);
        var restarted = fixture.Restart();

        var before = await restarted.ListTaxLotAccrualSnapshotsAsync(fixture.BookId,
            fixture.Lot.SecurityId, fixture.Lot.BookPositionId, date.AddDays(-1), date.AddDays(-1));
        before.Should().ContainSingle().Which.AccrualFace.Should().Be(6_000m);
        var read = () => restarted.ListTaxLotAccrualSnapshotsAsync(fixture.BookId,
            fixture.Lot.SecurityId, fixture.Lot.BookPositionId, date, date);

        await read.Should().ThrowAsync<LedgerValidationException>().WithMessage("*historical basis restatement*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AccrualAdmission_SeparateAtomicAmortizationDoesNotRewriteEarlierHistoricalQuote()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var restarted = fixture.Restart();
        var before = await restarted.ListTaxLotAccrualSnapshotsAsync(fixture.BookId,
            fixture.Lot.SecurityId, fixture.Lot.BookPositionId, AmortAsOf.AddDays(-1), AmortAsOf.AddDays(-1));
        before.Should().ContainSingle().Which.Lot.PricePercentOfPar.Should().Be(121m);
        var read = () => restarted.ListTaxLotAccrualSnapshotsAsync(fixture.BookId,
            fixture.Lot.SecurityId, fixture.Lot.BookPositionId, AmortAsOf, AmortAsOf);

        await read.Should().ThrowAsync<LedgerValidationException>().WithMessage("*historical basis restatement*");
    }
}
