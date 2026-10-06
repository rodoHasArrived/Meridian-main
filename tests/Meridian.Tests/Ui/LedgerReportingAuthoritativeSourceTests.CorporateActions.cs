using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class LedgerReportingAuthoritativeSourceTests
{
    [Fact]
    public async Task CaptureAsync_CorporateAction_RetainsSignedImmutableLotProof()
    {
        var fixture = CorporateReportingFixture();
        var receipt = fixture.JournalStore.CorporateReceipts.Values.Single()!;

        var capture = await CaptureCorporatePack(fixture);

        var pack = capture.CertifiedLedgerPresentation!.ReportPack;
        var artifact = pack.Artifacts.Single(a => a.Name == "corporate-action-lot-evidence.json");
        var report = JsonSerializer.Deserialize<CanonicalCorporateActionLotReport[]>(artifact.Content)!.Single();
        report.JournalEntryId.Should().Be(receipt.Journal.Entry.JournalEntryId);
        report.PredecessorBefore.OpenQuantity.Should().Be(80m);
        report.PredecessorAfter.OpenQuantity.Should().Be(0m);
        report.Successors.Should().ContainSingle().Which.OpenQuantity.Should().Be(160m);
        report.Successors[0].OpenTransactionCostBasis.Should().Be(720m);
        report.Successors[0].OpenFunctionalCostBasis.Should().Be(900m);
        report.Successors[0].Acquisition.FunctionalCostBasis.Should().Be(1000m);
        artifact.ChecksumSha256.Should().Be(Sha256Digest.ComputeUtf8(artifact.Content));
        pack.Artifacts.Single(a => a.Name.Contains("manifest", StringComparison.Ordinal))
            .Content.Should().Contain($"{artifact.Name},{artifact.ContentType},{artifact.ChecksumSha256}");
        var payload = string.Join("\n", pack.Artifacts.OrderBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => $"{a.Name}:{a.ChecksumSha256}"));
        pack.Signature.PayloadChecksumSha256.Should().Be(Sha256Digest.ComputeUtf8(payload));
        fixture.JournalStore.RequestedCorporateJournals.Should().Equal(receipt.Journal.Entry.JournalEntryId);
        receipt.MutatedLots.Should().BeEmpty("reporting must consume immutable mutation snapshots, never mutable current lots");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("journal")]
    [InlineData("basis")]
    [InlineData("successor")]
    [InlineData("book")]
    [InlineData("lineage")]
    public async Task CaptureAsync_CorporateAction_RefusesMissingOrChangedAtomicEvidence(string changed)
    {
        var fixture = CorporateReportingFixture();
        var journalId = fixture.JournalStore.Records.Single().Entry.JournalEntryId;
        var receipt = fixture.JournalStore.CorporateReceipts[journalId]!;
        var successor = receipt.Mutations.Single(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor);
        fixture.JournalStore.CorporateReceipts[journalId] = changed switch
        {
            "missing" => null,
            "journal" => receipt with { Journal = receipt.Journal with { GlobalSequence = 12 } },
            "successor" => receipt with { Mutations = [receipt.Mutations[0]] },
            _ => receipt with
            {
                Mutations = [receipt.Mutations[0], successor with
                {
                    LotAfter = changed switch
                    {
                        "basis" => successor.LotAfter with
                        { BasisAdjustment = successor.LotAfter.BasisAdjustment! with { FunctionalCostBasis = 901m } },
                        "book" => successor.LotAfter with { LedgerBookId = Guid.NewGuid() },
                        "lineage" => successor.LotAfter with
                        { Acquisition = successor.LotAfter.Acquisition! with
                            { CorporateActionLineage = successor.LotAfter.Acquisition!.CorporateActionLineage! with { CorporateActionId = Guid.NewGuid() } } },
                        _ => throw new ArgumentOutOfRangeException(nameof(changed))
                    }
                }]
            }
        };

        var capture = () => CaptureCorporatePack(fixture);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*Corporate-action journal*blocks canonical reporting*");
    }

    [Fact]
    public async Task CaptureAsync_WithoutCorporateActions_LeavesArtifactSetAndReceiptReadsUnchanged()
    {
        var fixture = CreateFixture();
        fixture.JournalStore.Records.Add(Record(fixture, CutoffUtc.AddDays(-1), 11));

        var capture = await CaptureCorporatePack(fixture);

        capture.CertifiedLedgerPresentation!.ReportPack.Artifacts
            .Should().NotContain(a => a.Name == "corporate-action-lot-evidence.json");
        fixture.JournalStore.RequestedCorporateJournals.Should().BeEmpty();
    }

    private static Task<ReportingAuthoritativeSourceCapture> CaptureCorporatePack(Fixture fixture)
        => fixture.Source.CaptureAsync(fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf },
            fixture.Access, new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement")).AsTask();

    private static Fixture CorporateReportingFixture()
    {
        var fixture = CreateFixture();
        var instruction = OpenLotCorporateActionTests.Instruction(CorporateActionAccountingTypeDto.StockSplit);
        instruction = instruction with
        {
            EffectiveDate = new DateOnly(2026, 7, 10),
            ExpectedLot = instruction.ExpectedLot with { LedgerBookId = fixture.Book.LedgerBookId }
        };
        var source = instruction.ExpectedLot;
        var projected = OpenLotCorporateAction.Project(instruction).Single();
        var target = projected.Successor;
        var timestamp = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var journalId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var account = new LedgerAccount("Investments", LedgerAccountType.Asset);
        var dimensions = new LedgerLineDimensionSet(fixture.FundId,
            CostCenterId: "cost-center-a", OrganizationId: fixture.OrganizationId.ToString("D"),
            BookId: fixture.Book.LedgerBookId.ToString("D"));
        var journal = new JournalEntry(journalId, timestamp, "Reviewed stock split",
        [
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, account, 0m, 900m, "Reviewed stock split",
                dimensions with { InstrumentId = source.SecurityId, PositionId = source.BookPositionId },
                new LedgerEntryCurrency("EUR", "USD", 0m, 720m, 1.25m)),
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, account, 900m, 0m, "Reviewed stock split",
                dimensions with { InstrumentId = target.Security.SecurityId, PositionId = target.BookPositionId },
                new LedgerEntryCurrency("EUR", "USD", 720m, 0m, 1.25m))
        ], new JournalEntryMetadata(EffectiveDate: instruction.EffectiveDate, Tags: new Dictionary<string, string>
        {
            ["lotCorporateActionHash"] = OpenLotCorporateAction.Fingerprint(instruction),
            ["lotCorporateActionInputs"] = JsonSerializer.Serialize(instruction)
        }));
        var record = Record(fixture, timestamp, 11) with { Entry = journal, SourceEventId = instruction.CorporateActionId };
        var before = new LedgerTaxLotRecord(source.TaxLotRecordId, source.LedgerBookId, account, source.LotId,
            source.AcquiredDate, source.OriginalQuantity, source.OpenQuantity, 12.5m, "USD", timestamp, timestamp,
            Version: source.Version, SecurityId: source.SecurityId, BookPositionId: source.BookPositionId,
            Acquisition: source.Acquisition, BasisAdjustment: new(batchId, "prior-adjustment", 80m, 720m, 900m));
        var after = before with { OpenQuantity = 0m, Version = source.Version + 1 };
        var successor = new LedgerTaxLotRecord(target.TaxLotRecordId, source.LedgerBookId, account, target.LotId,
            source.AcquiredDate, target.Quantity, target.Quantity, 6.25m, "USD", timestamp, timestamp,
            Version: 1, SecurityId: target.Security.SecurityId, BookPositionId: target.BookPositionId,
            Acquisition: source.Acquisition with
            {
                TransactionCostBasis = projected.AcquisitionTransactionCostBasis,
                FunctionalCostBasis = projected.AcquisitionFunctionalCostBasis,
                Evidence = OpenLotCorporateAction.Evidence(instruction),
                CorporateActionLineage = new(instruction.CorporateActionId, instruction.ActionType, instruction.EffectiveDate,
                    source.TaxLotRecordId, source.Version, 100m, target.Role, target.ReportingTags)
            }, BasisAdjustment: new(batchId, OpenLotBasisAdjustmentReasons.CorporateAction, 160m, 720m, 900m));
        LedgerTaxLotMutationRecord Mutation(AtomicTaxLotMutationKind kind, LedgerTaxLotRecord? old, LedgerTaxLotRecord current, int ordinal)
            => new(Guid.NewGuid(), batchId, kind, current.TaxLotRecordId, current.LotId, ordinal,
                old?.OpenQuantity ?? 0m, current.OpenQuantity - (old?.OpenQuantity ?? 0m), current.OpenQuantity,
                current.UnitCost, 900m, old?.Version ?? 0, current.Version, "reviewed-lot-evidence", journalId,
                instruction.CorporateActionId, OpenLotCorporateAction.Evidence(instruction), timestamp, old, current,
                SecurityId: current.SecurityId, BookPositionId: current.BookPositionId);
        fixture.JournalStore.Records.Add(record);
        fixture.JournalStore.CorporateReceipts[journalId] = new(batchId, AtomicTaxLotMutationKind.CorporateAction,
            "retained-fingerprint", false, record, [],
            [Mutation(AtomicTaxLotMutationKind.CorporateActionClose, before, after, 0),
                Mutation(AtomicTaxLotMutationKind.CorporateActionSuccessor, null, successor, 1)],
            OpenLotCorporateAction.Evidence(instruction));
        return fixture;
    }
}
