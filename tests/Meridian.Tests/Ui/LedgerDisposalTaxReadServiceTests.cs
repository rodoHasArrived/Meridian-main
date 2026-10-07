using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerDisposalTaxReadServiceTests
{
    private const string RetainedRevision = "us-tax-policy:revision-7";
    private static readonly DateOnly SaleDate = new(2026, 7, 10);
    private static readonly DateTimeOffset RecordedAt = new(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2025-07-10", "2026-07-09", "ShortTerm", 364)]
    [InlineData("2025-07-10", "2026-07-10", "ShortTerm", 365)]
    [InlineData("2025-07-10", "2026-07-11", "LongTerm", 366)]
    [InlineData("2023-03-01", "2024-03-01", "ShortTerm", 366)]
    [InlineData("2023-03-01", "2024-03-02", "LongTerm", 367)]
    [InlineData("2024-02-29", "2025-02-28", "ShortTerm", 365)]
    [InlineData("2024-02-29", "2025-03-01", "LongTerm", 366)]
    public void Project_UsesCalendarAnniversaryAndJournalEffectiveDate(
        string acquired, string sold, string character, int days)
    {
        var acquiredDate = DateOnly.Parse(acquired);
        var effectiveDate = DateOnly.Parse(sold);
        var fixture = Create(effectiveDate, [Lot(1, acquiredDate, 100m)], proceeds: 120m);

        // The retained journal timestamp is deliberately different from its effective sale date.
        var result = Project(fixture, new DateOnly(2030, 1, 1));

        result.SaleDate.Should().Be(effectiveDate);
        result.Character.Should().Be(character);
        result.State.Should().Be("Settled");
        result.CanChange.Should().BeFalse();
        result.Parcels.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            AcquiredDate = acquiredDate,
            HoldingPeriodStart = acquiredDate,
            HoldingPeriodDays = days,
            HoldingPeriodCarried = false,
            Character = character,
            EconomicGainOrLoss = "20",
            RecognizedGainOrLoss = "20",
            DeferredLoss = "0"
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_CarriesEarlierHoldingDateFromCanonicalOrRetainedDisposalEvidence(bool canonicalCarry)
    {
        var acquired = new DateOnly(2026, 6, 15);
        var carried = new DateOnly(2024, 2, 29);
        var lot = Lot(1, acquired, 100m);
        if (canonicalCarry)
            lot = lot with { Acquisition = lot.Acquisition! with { HoldingPeriodStartDate = carried } };
        var fixture = Create(SaleDate, [lot], proceeds: 120m);
        if (!canonicalCarry)
            fixture = fixture with
            {
                Disposal = fixture.Disposal with
                {
                    Lots = [fixture.Disposal.Lots[0] with { HoldingPeriodStart = carried }]
                }
            };

        var result = Project(fixture, SaleDate);

        result.Character.Should().Be("LongTerm");
        result.Parcels.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            AcquiredDate = acquired,
            HoldingPeriodStart = carried,
            HoldingPeriodCarried = true,
            HoldingPeriodDays = 862,
            Character = "LongTerm"
        });
    }

    [Fact]
    public void Project_NetGainWithALossParcelRemainsProvisionalAndPreservesMixedCharacter()
    {
        var fixture = Create(SaleDate,
            [Lot(1, new(2024, 1, 1), 40m), Lot(2, new(2026, 1, 2), 120m)],
            proceeds: 200m, deferred: 10m, matchedQuantity: 0.5m);

        var result = Project(fixture, SaleDate);

        result.State.Should().Be("Provisional", "a net gain must not hide a loss parcel's replacement exposure");
        result.Character.Should().Be("Mixed");
        result.CanChange.Should().BeTrue();
        result.EconomicGainOrLoss.Should().Be("40");
        result.RecognizedGainOrLoss.Should().Be("50");
        result.DeferredLoss.Should().Be("10");
        result.Parcels.Select(parcel => parcel.EconomicGainOrLoss).Should().Equal("60", "-20");
        result.Parcels.Select(parcel => parcel.RecognizedGainOrLoss).Should().Equal("60", "-10");
        result.Parcels.Select(parcel => parcel.DeferredLoss).Should().Equal("0", "10");
        result.Parcels.Select(parcel => parcel.Character).Should().Equal("LongTerm", "ShortTerm");
    }

    [Theory]
    [InlineData(10, 0.5, "Provisional", true)]
    [InlineData(20, 0.5, "Provisional", true)]
    [InlineData(20, 1, "Settled", false)]
    public void Project_OnlyCompleteLossAndQuantityCoverageSettlesReplacementExposure(
        decimal deferred, decimal quantity, string state, bool canChange)
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)],
            proceeds: 80m, deferred: deferred, matchedQuantity: quantity);

        var result = Project(fixture, SaleDate);

        result.State.Should().Be(state);
        result.CanChange.Should().Be(canChange);
        result.ReEvaluationRequired.Should().BeFalse();
        result.ReplacementWindowEnd.Should().Be(new DateOnly(2026, 8, 9));
        result.EconomicGainOrLoss.Should().Be("-20");
        decimal.Parse(result.RecognizedGainOrLoss!, CultureInfo.InvariantCulture).Should().Be(-20m + deferred);
        decimal.Parse(result.DeferredLoss!, CultureInfo.InvariantCulture).Should().Be(deferred);
    }

    [Fact]
    public void Project_FaceLotsCompareReplacementCoverageInTheSameQuantityBasis()
    {
        var lot = Lot(1, new(2025, 1, 1), 100m);
        lot = lot with
        {
            OriginalFace = 1000m,
            ParBasis = 100m,
            BookedFactor = 0.9m,
            Acquisition = lot.Acquisition! with
            {
                QuantityBasis = LotQuantityBasis.Face,
                FaceValueTerms = new(100m, 0.9m, BondAmortizationMethod.ConstantYield, 0.05m)
            }
        };
        var fixture = Create(SaleDate, [lot], proceeds: 80m, deferred: 20m, matchedQuantity: 1m);

        var result = Project(fixture, SaleDate);

        result.State.Should().Be("Settled");
        result.CanChange.Should().BeFalse();
        result.Parcels.Should().ContainSingle().Which.Quantity.Should().Be("100");
        result.DeferredLoss.Should().Be("20");
        result.RecognizedGainOrLoss.Should().Be("0");
    }

    [Theory]
    [InlineData(10, 1.0001, false)]
    [InlineData(20, 1.0001, false)]
    [InlineData(20, 1.0001, true)]
    public void Project_OverstatedReplacementQuantityCannotCertifyTaxFigures(
        decimal deferred, decimal matchedQuantity, bool faceLot)
    {
        var lot = Lot(1, new(2025, 1, 1), 100m);
        if (faceLot)
            lot = lot with
            {
                OriginalFace = 1000m,
                ParBasis = 100m,
                BookedFactor = 0.9m,
                Acquisition = lot.Acquisition! with
                {
                    QuantityBasis = LotQuantityBasis.Face,
                    FaceValueTerms = new(100m, 0.9m, BondAmortizationMethod.ConstantYield, 0.05m)
                }
            };
        var fixture = Create(SaleDate, [lot], proceeds: 80m, deferred: deferred, matchedQuantity: matchedQuantity);

        var result = Project(fixture, SaleDate);

        result.State.Should().Be("MissingEvidence");
        result.CanChange.Should().BeTrue();
        result.EconomicGainOrLoss.Should().BeNull();
        result.RecognizedGainOrLoss.Should().BeNull();
        result.DeferredLoss.Should().BeNull();
        result.Parcels.Should().BeEmpty();
    }

    [Fact]
    public void Project_ReplacementQuantityCannotIncludeGainParcels()
    {
        var fixture = Create(SaleDate,
            [Lot(1, new(2024, 1, 1), 40m), Lot(2, new(2026, 1, 2), 120m)],
            proceeds: 200m, deferred: 20m, matchedQuantity: 2m);

        Project(fixture, SaleDate).State.Should().Be("MissingEvidence");

        var gainOnly = Create(SaleDate, [Lot(1, new(2024, 1, 1), 40m)], proceeds: 100m,
            matchedQuantity: decimal.MaxValue);
        Project(gainOnly, SaleDate).State.Should().Be("MissingEvidence");
    }

    [Theory]
    [InlineData("2026-08-09", false)]
    [InlineData("2026-08-10", true)]
    [InlineData("2027-07-10", true)]
    public void Project_InclusiveWindowEndAndElapsedTimeNeverInventACompletedReEvaluation(string readDate, bool requiresReview)
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)],
            proceeds: 80m, deferred: 10m, matchedQuantity: 0.5m);

        var result = Project(fixture, DateOnly.Parse(readDate));

        result.State.Should().Be("Provisional");
        result.CanChange.Should().BeTrue();
        result.ReEvaluationRequired.Should().Be(requiresReview);
        result.ReplacementWindowEnd.Should().Be(new DateOnly(2026, 8, 9));
        result.RecognizedGainOrLoss.Should().Be("-10", "a read must preserve the retained recognized result");
        result.RecordedAt.Should().Be(RecordedAt);
    }

    [Fact]
    public void Project_UsesExactRetainedRevisionAndWindowRatherThanAssumingThirtyDays()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)],
            proceeds: 80m, deferred: 10m, matchedQuantity: 0.5m, windowDays: 45);

        var result = Project(fixture, new DateOnly(2026, 8, 10));

        result.PolicyRevision.Should().Be(RetainedRevision);
        result.ReliefMethod.Should().Be("Fifo");
        result.ReplacementWindowEnd.Should().Be(new DateOnly(2026, 8, 24));
        result.ReEvaluationRequired.Should().BeFalse();
        result.State.Should().Be("Provisional");
    }

    [Fact]
    public void Project_MissingCanonicalEvidenceDoesNotEmitCertifiedZerosOrCharacter()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)], proceeds: 120m);
        fixture = fixture with { Disposal = fixture.Disposal with { CanonicalLots = null } };

        var result = Project(fixture, SaleDate);

        result.State.Should().Be("MissingEvidence");
        result.CanChange.Should().BeTrue();
        result.Character.Should().BeNull();
        result.EconomicGainOrLoss.Should().BeNull();
        result.RecognizedGainOrLoss.Should().BeNull();
        result.DeferredLoss.Should().BeNull();
        result.Parcels.Should().BeEmpty();
    }

    [Fact]
    public void Project_AbsentExactRevisionKeepsCertifiedEconomicsButBlocksSettledStatus()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)], proceeds: 120m);
        fixture = fixture with { Disposal = fixture.Disposal with { PolicyRevision = null } };

        var result = Project(fixture, SaleDate);

        result.State.Should().Be("MissingEvidence");
        result.StateReason.Should().Contain("exact applied policy revision");
        result.PolicyRevision.Should().BeNull();
        result.EconomicGainOrLoss.Should().Be("20");
        result.RecognizedGainOrLoss.Should().Be("20");
    }

    [Fact]
    public void Project_LossWithoutRetainedPolicySettingsCannotEstablishFinalityAfterWindowElapsed()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)], proceeds: 80m);

        var result = Project(fixture, new DateOnly(2027, 7, 10));

        result.State.Should().Be("MissingEvidence");
        result.CanChange.Should().BeTrue();
        result.ReplacementWindowEnd.Should().BeNull();
        result.PolicyRevision.Should().Be(RetainedRevision);
        result.RecognizedGainOrLoss.Should().Be("-20");
    }

    [Fact]
    public void Project_DisagreedDeferralPolicyRevisionCannotCertifyFinality()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)],
            proceeds: 80m, deferred: 20m, matchedQuantity: 1m);
        fixture = fixture with { Disposal = fixture.Disposal with { PolicyRevision = "different-revision" } };

        var result = Project(fixture, SaleDate);

        result.State.Should().Be("MissingEvidence");
        result.CanChange.Should().BeTrue();
        result.StateReason.Should().Contain("exact revision");
    }

    [Fact]
    public void Project_MultipleLossParcelsRequireSourceAttributionInsteadOfSpreadingAggregateDeferral()
    {
        var fixture = Create(SaleDate,
            [Lot(1, new(2024, 1, 1), 120m), Lot(2, new(2026, 1, 2), 140m)],
            proceeds: 200m, deferred: 30m, matchedQuantity: 1.25m);

        var missing = Project(fixture, SaleDate);

        missing.State.Should().Be("MissingEvidence");
        missing.EconomicGainOrLoss.Should().Be("-60");
        missing.RecognizedGainOrLoss.Should().Be("-30");
        missing.DeferredLoss.Should().Be("30");
        missing.Parcels.Should().OnlyContain(parcel => parcel.DeferredLoss == null && parcel.RecognizedGainOrLoss == null);

        var selections = CanonicalDisposalHistoryProjector.Project(fixture.Disposal, fixture.Journal, fixture.BookId, "USD").Selections;
        fixture = fixture with
        {
            Disposal = fixture.Disposal with
            {
                WashSaleBasisIncreases = [fixture.Disposal.WashSaleBasisIncreases[0] with
            {
                SourceAllocations = [new(selections[0], 1m, 20m), new(selections[1], 0.25m, 10m)]
            }]
            }
        };

        var attributed = Project(fixture, SaleDate);

        attributed.State.Should().Be("Provisional");
        attributed.Parcels.Select(parcel => parcel.DeferredLoss).Should().Equal("20", "10");
        attributed.Parcels.Select(parcel => parcel.RecognizedGainOrLoss).Should().Equal("0", "-30");
        attributed.Parcels.Sum(parcel => decimal.Parse(parcel.RecognizedGainOrLoss!, CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(attributed.RecognizedGainOrLoss!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Project_FullDeferralOfMixedCharacterLossParcelsHasUnambiguousAttribution()
    {
        var fixture = Create(SaleDate,
            [Lot(1, new(2024, 1, 1), 120m), Lot(2, new(2026, 1, 2), 140m)],
            proceeds: 200m, deferred: 60m, matchedQuantity: 2m);
        fixture.Disposal.WashSaleBasisIncreases[0].SourceAllocations.Should().BeEmpty();

        var result = Project(fixture, SaleDate);

        result.State.Should().Be("Settled");
        result.CanChange.Should().BeFalse();
        result.Character.Should().Be("Mixed");
        result.EconomicGainOrLoss.Should().Be("-60");
        result.RecognizedGainOrLoss.Should().Be("0");
        result.DeferredLoss.Should().Be("60");
        result.Parcels.Select(parcel => parcel.Character).Should().Equal("LongTerm", "ShortTerm");
        result.Parcels.Select(parcel => parcel.DeferredLoss).Should().Equal("20", "40");
        result.Parcels.Select(parcel => parcel.RecognizedGainOrLoss).Should().Equal("0", "0");
    }

    [Fact]
    public void Project_PreservesLargeMonetaryCentsAsCanonicalStringsUnderNonEnglishCulture()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 9007199254740993.0100m)],
            proceeds: 18014398509481986.0300m);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // CurrentCulture is scoped to this execution context; never change the process default.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var result = Project(fixture, SaleDate);

            result.State.Should().Be("Settled");
            result.EconomicGainOrLoss.Should().Be("9007199254740993.02");
            result.RecognizedGainOrLoss.Should().Be("9007199254740993.02");
            result.DeferredLoss.Should().Be("0");
            var parcel = result.Parcels.Should().ContainSingle().Which;
            parcel.Quantity.Should().Be("1");
            parcel.Proceeds.Should().Be("18014398509481986.03");
            parcel.CostBasis.Should().Be("9007199254740993.01");
            parcel.EconomicGainOrLoss.Should().Be("9007199254740993.02");
            parcel.RecognizedGainOrLoss.Should().Be("9007199254740993.02");
            parcel.DeferredLoss.Should().Be("0");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task ReadAsync_RefreshReadsNewlyRetainedPolicyEvidenceWithoutRewritingJournal()
    {
        var lot = Lot(1, new(2025, 1, 1), 100m);
        var complete = Create(SaleDate, [lot], proceeds: 80m, deferred: 20m, matchedQuantity: 1m);
        var history = new RetainedHistory { Records = [complete.Disposal with { PolicyRevision = null }] };
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var service = new LedgerDisposalTaxReadService(history, new FixedClock(now));
        var periodId = Guid.NewGuid();

        var first = await service.ReadAsync(complete.BookId, periodId, complete.Journal, "USD");
        history.Records = [complete.Disposal];
        var refreshed = await service.ReadAsync(complete.BookId, periodId, complete.Journal, "USD");

        first.Disposals.Should().ContainSingle().Which.State.Should().Be("MissingEvidence");
        first.Disposals[0].PolicyRevision.Should().BeNull();
        refreshed.Disposals.Should().ContainSingle().Which.State.Should().Be("Settled");
        refreshed.Disposals[0].RecognizedGainOrLoss.Should().Be("0");
        refreshed.Disposals[0].DeferredLoss.Should().Be("20");
        refreshed.Disposals[0].CanChange.Should().BeFalse();
        refreshed.Disposals[0].PolicyRevision.Should().Be(RetainedRevision);
        refreshed.Disposals[0].EconomicGainOrLoss.Should().Be(first.Disposals[0].EconomicGainOrLoss);
        refreshed.Disposals[0].RecognizedGainOrLoss.Should().Be(first.Disposals[0].RecognizedGainOrLoss);
        refreshed.Disposals[0].DeferredLoss.Should().Be(first.Disposals[0].DeferredLoss);
        refreshed.Disposals[0].Character.Should().Be(first.Disposals[0].Character);
        refreshed.EvaluatedAt.Should().Be(now);
        refreshed.LedgerBookId.Should().Be(complete.BookId);
        refreshed.PeriodId.Should().Be(periodId);
        history.ReadCount.Should().Be(2);
        history.LastBookId.Should().Be(complete.BookId);
        history.LastJournalIds.Should().Equal(complete.Journal.JournalEntryId);
    }

    [Fact]
    public async Task ReadAsync_OneUncertifiableDisposalDoesNotHideAnotherRetainedResult()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)], proceeds: 120m);
        var missing = fixture.Disposal with { MutationBatchId = Guid.NewGuid(), CanonicalLots = null };
        var history = new RetainedHistory { Records = [missing, fixture.Disposal] };

        var result = await new LedgerDisposalTaxReadService(history)
            .ReadAsync(fixture.BookId, Guid.NewGuid(), fixture.Journal, "USD");

        result.EvidenceState.Should().Be("Available");
        result.Disposals.Should().HaveCount(2);
        result.Disposals.Select(disposal => disposal.State).Should().Equal("MissingEvidence", "Settled");
        result.Disposals[1].PolicyRevision.Should().Be(RetainedRevision);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedHistoryIsMissingEvidenceAndEmptyHistoryMeansNoDisposal()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)], proceeds: 120m);
        var periodId = Guid.NewGuid();

        var unavailable = await new LedgerDisposalTaxReadService(null)
            .ReadAsync(fixture.BookId, periodId, fixture.Journal, "USD");
        var absent = await new LedgerDisposalTaxReadService(new RetainedHistory())
            .ReadAsync(fixture.BookId, periodId, fixture.Journal, "USD");

        unavailable.EvidenceState.Should().Be("MissingEvidence");
        unavailable.Disposals.Should().BeEmpty();
        absent.EvidenceState.Should().Be("Available");
        absent.Message.Should().Contain("No retained tax-lot disposal");
        absent.Disposals.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_HistoryForAnotherJournalFailsClosed()
    {
        var fixture = Create(SaleDate, [Lot(1, new(2025, 1, 1), 100m)], proceeds: 120m);
        var history = new RetainedHistory
        {
            Records = [fixture.Disposal with { JournalEntryId = Guid.NewGuid() }]
        };

        var result = await new LedgerDisposalTaxReadService(history)
            .ReadAsync(fixture.BookId, Guid.NewGuid(), fixture.Journal, "USD");

        result.EvidenceState.Should().Be("MissingEvidence");
        result.Disposals.Should().BeEmpty();
        result.Message.Should().Contain("requested journal");
    }

    private static LedgerDisposalTaxResultDto Project(Fixture fixture, DateOnly asOf)
        => LedgerDisposalTaxReadService.Project(fixture.Disposal, fixture.Journal, fixture.BookId, "USD", asOf);

    private static LedgerTaxLotRecord Lot(int ordinal, DateOnly acquired, decimal unitCost)
    {
        var lot = CanonicalOpenLotConsumerTests.DurableLot(ordinal, unitCost, 1m);
        return lot with
        {
            AcquiredDate = acquired,
            Acquisition = lot.Acquisition! with
            {
                HoldingPeriodStartDate = acquired,
                Evidence = lot.Acquisition.Evidence.Select(evidence => evidence with { EffectiveDate = acquired }).ToArray()
            }
        };
    }

    private static Fixture Create(DateOnly saleDate, IReadOnlyList<LedgerTaxLotRecord> lots,
        decimal proceeds, decimal deferred = 0m, decimal matchedQuantity = 0m, int windowDays = 30)
    {
        var id = Guid.NewGuid();
        var first = lots[0];
        var basis = lots.Sum(lot => lot.UnitCost);
        var recognized = proceeds - basis + deferred;
        var dimensions = new LedgerLineDimensionSet(InstrumentId: first.SecurityId) { PositionId = first.BookPositionId };
        const string description = "Retained tax disposal";
        List<LedgerEntry> lines =
        [
            new(Guid.NewGuid(), id, RecordedAt, LedgerAccounts.Cash, proceeds, 0m, description, dimensions),
            new(Guid.NewGuid(), id, RecordedAt, first.Account, 0m, basis, description, dimensions)
        ];
        if (recognized != 0m)
            lines.Add(new(Guid.NewGuid(), id, RecordedAt, recognized < 0m ? LedgerAccounts.RealizedLoss : LedgerAccounts.RealizedGain,
                Math.Max(-recognized, 0m), Math.Max(recognized, 0m), description, dimensions));
        if (deferred > 0m)
            lines.Add(new(Guid.NewGuid(), id, RecordedAt, new LedgerAccount("Replacement investment", LedgerAccountType.Asset),
                deferred, 0m, description, dimensions));
        var journal = new JournalEntry(id, RecordedAt, description, lines, new(EffectiveDate: saleDate));
        journal.IsBalanced.Should().BeTrue();
        var increases = deferred == 0m ? Array.Empty<WashSaleBasisIncrease>()
            : new[] { new WashSaleBasisIncrease("replacement-1", deferred, first.AcquiredDate)
            {
                AppliedPolicy = new(true, windowDays, WashSaleReplacementScope.LedgerBook) { PolicyId = RetainedRevision }
            } };
        var disposal = new LedgerTaxLotDisposalHistoryRecord(Guid.NewGuid(), id, first.Account, LedgerTaxLotReliefMethod.Fifo,
            lots.Select(lot => new LedgerTaxLotDisposalHistoryLot(lot.LotId, lot.AcquiredDate, lot.AcquiredDate,
                1m, lot.UnitCost, lot.UnitCost)).ToArray(), increases, matchedQuantity,
            lots.Select(lot => lot.ToOpenLot()).ToArray(),
            ProceedsAllocationVersion: LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion,
            SalePrice: proceeds / lots.Count, PolicyRevision: RetainedRevision, RecordedAt: RecordedAt,
            DeferralRecipients: deferred == 0m ? null :
                [new(Guid.NewGuid(), "replacement-1", new LedgerAccount("Replacement investment", LedgerAccountType.Asset),
                    first.SecurityId, first.BookPositionId, deferred)]);
        return new(first.LedgerBookId, journal, disposal);
    }

    private sealed record Fixture(Guid BookId, JournalEntry Journal, LedgerTaxLotDisposalHistoryRecord Disposal);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RetainedHistory : ILedgerTaxLotDisposalHistory
    {
        public IReadOnlyList<LedgerTaxLotDisposalHistoryRecord> Records { get; set; } = [];
        public int ReadCount { get; private set; }
        public Guid LastBookId { get; private set; }
        public IReadOnlyList<Guid> LastJournalIds { get; private set; } = [];

        public Task<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>> GetTaxLotDisposalHistoryAsync(
            Guid ledgerBookId, IReadOnlyList<Guid> journalEntryIds, CancellationToken ct = default)
        {
            ReadCount++;
            LastBookId = ledgerBookId;
            LastJournalIds = journalEntryIds.ToArray();
            return Task.FromResult(Records);
        }
    }
}
