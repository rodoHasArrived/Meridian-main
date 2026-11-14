using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReportingOpenPositions_NonzeroSecuritiesJournalWithoutLotsHasCoverageBlocker()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var bookId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "missing-reporting-lots", Guid.NewGuid(),
            FundStructureNodeKindDto.Account, "Missing lots", "USD", at, at));
        var period = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), bookId, 2026, 5, "May",
            new(2026, 5, 1), new(2026, 5, 31), "Open", at, null, 0), 0);
        var write = BuildJournalWrite(bookId, period.PeriodId, Guid.NewGuid(), "journal-without-canonical-lots");
        var entry = write.Entry;
        var securityId = entry.Metadata.SecurityId!.Value;
        var tags = entry.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        // This fixture changes the generic lot account into a symbol-bearing securities account.
        // Its mapping token must identify that actual resolved instrument, not "investment-lots".
        tags["securityMasterLineage"] = $"LOT:{securityId:N}:ledger-map:{securityId:N}:sm-approval:lot-controller:security-status:active:{tags["securityMasterProvenance"]}";
        write = write with
        {
            Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
                entry.Lines.Select(line => line.Debit > 0m
                    ? new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
                        new("Securities", LedgerAccountType.Asset, "LOT", "account-test"),
                        line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency) : line).ToArray(),
                entry.Metadata with { Symbol = "LOT", Tags = tags })
        };
        await database.JournalStore.AppendAsync(write);
        var snapshot = await database.JournalStore.CaptureOpenPositionReportingSnapshotAsync(
            new(LedgerBookId: bookId, PeriodId: period.PeriodId), period.PeriodId);
        snapshot.OpenPositions!.Lots.Should().BeEmpty();
        snapshot.OpenPositions.CoverageBlockers.Should().Contain(blocker => blocker.Reason.Contains("does not reconcile"));
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReportingOpenPositions_RestoresHistoricalQuantityAndBasisAfterLaterDisposal()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var bookId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "historical-reporting", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Historical reporting", "USD", at, at));
        var may = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), bookId, 2026, 5, "May",
            new(2026, 5, 1), new(2026, 5, 31), "Open", at, null, 0), 0);
        var june = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), bookId, 2026, 6, "June",
            new(2026, 6, 1), new(2026, 6, 30), "Open", at, null, 0), 0);
        var authority = await database.SeedLotReferenceAuthorityAsync(bookId, TestSecurityId, TestBookPositionId);
        await database.JournalStore.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), bookId,
            new("Investment lots", LedgerAccountType.Asset), LedgerTaxLotReliefMethod.Fifo,
            "tax-policy-v1", new(2026, 5, 1), at, at));
        var acquisition = (BuildAcquisitionCommand(bookId, may.PeriodId, may.Version) with
        { ReferenceVersions = authority.Versions }).WithComputedFingerprint();
        var lot = (await database.JournalStore.AppendAssetPostingAsync(acquisition)).MutatedLots.Single();
        var eventId = Guid.NewGuid();
        var write = BuildJournalWrite(bookId, june.PeriodId, eventId, "june-reporting-disposal", "Cash", "Investment lots", 2500m);
        var soldAt = DateTimeOffset.Parse("2026-06-10T14:00:00Z");
        var entry = write.Entry;
        write = write with
        {
            Entry = new JournalEntry(entry.JournalEntryId, soldAt, entry.Description,
                entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, soldAt, line.Account,
                    line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency)).ToArray(),
                entry.Metadata with { EffectiveDate = new DateOnly(2026, 6, 10) })
        };
        var evidence = BuildEvidence("june-reporting-disposal", 'f') with { EffectiveDate = new(2026, 6, 10) };
        var disposal = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), bookId, write, eventId,
            "june-reporting-disposal", june.Version, AtomicTaxLotMutationKind.Disposal, [evidence],
            disposalSelections: [new(lot.TaxLotRecordId, lot.LotId, lot.Version, 100m, 25m, 0,
                evidence.EvidenceId, 100m, 2500m)], reliefMethod: "Fifo", policyRevision: "tax-policy-v1",
            referenceVersions: authority.Versions);
        await database.JournalStore.AppendAssetPostingAsync(disposal);

        var restarted = database.RestartJournalStore();
        var historical = await restarted.CaptureOpenPositionReportingSnapshotAsync(new(LedgerBookId: bookId, PeriodId: may.PeriodId), may.PeriodId);
        historical.OpenPositions.Should().NotBeNull();
        historical.OpenPositions!.Blockers.Should().BeEmpty();
        var original = historical.OpenPositions.Lots.Should().ContainSingle().Which.ToOpenLot();
        original.OpenQuantity.Should().Be(100m);
        original.OpenFunctionalCostBasis.Should().Be(10000m);
        var later = await restarted.CaptureOpenPositionReportingSnapshotAsync(new(LedgerBookId: bookId, PeriodId: june.PeriodId), june.PeriodId);
        later.OpenPositions!.Blockers.Should().BeEmpty();
        var remaining = later.OpenPositions.Lots.Should().ContainSingle().Which.ToOpenLot();
        remaining.OpenQuantity.Should().Be(75m);
        remaining.OpenFunctionalCostBasis.Should().Be(7500m);
        original.OpenFunctionalCostBasis.Should().Be(10000m);

        var beforeDisposal = soldAt.AddMinutes(-1);
        var intraday = await restarted.CaptureOpenPositionReportingSnapshotAsync(new(LedgerBookId: bookId,
            EffectiveTo: new(2026, 6, 10), OccurredTo: beforeDisposal), june.PeriodId);
        intraday.OpenPositions!.CutoffUtc.Should().Be(beforeDisposal);
        intraday.OpenPositions.Blockers.Should().BeEmpty();
        intraday.OpenPositions.Lots.Single().ToOpenLot().OpenFunctionalCostBasis.Should().Be(10000m);
        intraday.OpenPositions.Lots.Single().OpenQuantity.Should().Be(100m);
        var afterDisposal = await restarted.CaptureOpenPositionReportingSnapshotAsync(new(LedgerBookId: bookId,
            EffectiveTo: new(2026, 6, 10), OccurredTo: soldAt.AddMinutes(1)), june.PeriodId);
        afterDisposal.OpenPositions!.Lots.Single().OpenQuantity.Should().Be(75m);
    }
}
