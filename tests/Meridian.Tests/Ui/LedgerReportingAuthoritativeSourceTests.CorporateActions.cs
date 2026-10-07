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
        receipt.Journal.AggregateId.Should().NotBe(fixture.Book.LedgerBookId,
            "journal aggregate identity is separate from the authoritative period and lot book scope");

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
        receipt.MutatedLots.Should().BeEquivalentTo(receipt.Mutations.Select(mutation => mutation.LotAfter),
            "the receipt contains only immutable mutation snapshots, never mutable current lots");
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

    public static IEnumerable<object[]> CorporateMutationTampering()
    {
        string[] fields = ["quantity-before", "quantity-delta", "quantity-after", "unit-cost", "cost-basis",
            "expected-version", "result-version", "lot-id", "security", "position", "batch", "source-event",
            "journal", "selection-evidence", "recorded-at", "ordinal-duplicate", "ordinal-gap", "mutation-empty",
            "mutation-duplicate", "correction", "relief", "policy", "evidence-missing", "evidence-changed",
            "snapshot-time", "snapshot-batch"];
        foreach (var field in fields)
        {
            yield return [field, false];
            yield return [field, true];
        }
    }

    [Theory]
    [MemberData(nameof(CorporateMutationTampering))]
    public async Task CaptureAsync_CorporateAction_RefusesAlteredMutationFacts(string field, bool successor)
    {
        var fixture = CorporateReportingFixture();
        var receipt = fixture.JournalStore.CorporateReceipts.Values.Single()!;
        var index = successor ? 1 : 0;
        var mutation = receipt.Mutations[index];
        var other = receipt.Mutations[1 - index];
        var changed = field switch
        {
            "quantity-before" => mutation with { QuantityBefore = mutation.QuantityBefore + 1m },
            "quantity-delta" => mutation with { QuantityDelta = mutation.QuantityDelta + 1m },
            "quantity-after" => mutation with { QuantityAfter = mutation.QuantityAfter + 1m },
            "unit-cost" => mutation with { UnitCost = mutation.UnitCost + 1m },
            "cost-basis" => mutation with { CostBasis = mutation.CostBasis + 1m },
            "expected-version" => mutation with { ExpectedVersion = mutation.ExpectedVersion + 1 },
            "result-version" => mutation with { ResultVersion = mutation.ResultVersion + 1 },
            "lot-id" => mutation with { LotId = "changed-lot" },
            "security" => mutation with { SecurityId = Guid.NewGuid() },
            "position" => mutation with { BookPositionId = Guid.NewGuid() },
            "batch" => mutation with { MutationBatchId = Guid.NewGuid() },
            "source-event" => mutation with { SourceEventId = Guid.NewGuid() },
            "journal" => mutation with { JournalEntryId = Guid.NewGuid() },
            "selection-evidence" => mutation with { SelectionEvidenceId = "different-retained-evidence" },
            "recorded-at" => mutation with { RecordedAt = mutation.RecordedAt.AddSeconds(1) },
            "ordinal-duplicate" => mutation with { SelectionOrdinal = other.SelectionOrdinal },
            "ordinal-gap" => mutation with { SelectionOrdinal = 9 },
            "mutation-empty" => mutation with { MutationRecordId = Guid.Empty },
            "mutation-duplicate" => mutation with { MutationRecordId = other.MutationRecordId },
            "correction" => mutation with { CorrectsMutationBatchId = Guid.NewGuid() },
            "relief" => mutation with { ReliefMethod = "FIFO" },
            "policy" => mutation with { PolicyRevision = "different-policy" },
            "evidence-missing" => mutation with { RetainedEvidence = mutation.RetainedEvidence.Skip(1).ToArray() },
            "evidence-changed" => mutation with
            {
                RetainedEvidence = [mutation.RetainedEvidence[0] with
                { ContentHashSha256 = new string('b', 64) }, .. mutation.RetainedEvidence.Skip(1)]
            },
            "snapshot-time" => mutation with { LotAfter = mutation.LotAfter with { UpdatedAt = mutation.LotAfter.UpdatedAt.AddSeconds(1) } },
            "snapshot-batch" => mutation with { LotAfter = mutation.LotAfter with { LastMutationBatchId = Guid.NewGuid() } },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var mutations = receipt.Mutations.ToArray();
        mutations[index] = changed;
        fixture.JournalStore.CorporateReceipts[receipt.Journal.Entry.JournalEntryId] = receipt with
        {
            Mutations = mutations,
            MutatedLots = mutations.Select(item => item.LotAfter).ToArray()
        };

        var capture = () => CaptureCorporatePack(fixture);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*Corporate-action journal*blocks canonical reporting*");
    }

    [Fact]
    public async Task CaptureAsync_CorporateAction_RefusesChangedPredecessorAccountTypeEvenWhenItsNameMatches()
    {
        var fixture = CorporateReportingFixture();
        var receipt = fixture.JournalStore.CorporateReceipts.Values.Single()!;
        var close = receipt.Mutations[0];
        var changedAccount = close.LotAfter.Account with { AccountType = LedgerAccountType.Liability };
        changedAccount.ToString().Should().Be(close.LotAfter.Account.ToString());
        var mutations = new[] { close with { LotBefore = close.LotBefore! with { Account = changedAccount },
            LotAfter = close.LotAfter with { Account = changedAccount } }, receipt.Mutations[1] };
        fixture.JournalStore.CorporateReceipts[receipt.Journal.Entry.JournalEntryId] = receipt with
        { Mutations = mutations, MutatedLots = mutations.Select(mutation => mutation.LotAfter).ToArray() };

        var capture = () => CaptureCorporatePack(fixture);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*Corporate-action journal*blocks canonical reporting*");
    }

    [Fact]
    public async Task CaptureAsync_CorporateAction_RefusesPartiallyStampedLotIdentities()
    {
        var fixture = CorporateReportingFixture(explicitLotIds: true);
        var receipt = fixture.JournalStore.CorporateReceipts.Values.Single()!;
        var original = receipt.Journal.Entry;
        var source = original.Lines[0];
        var changed = new LedgerEntry(source.EntryId, source.JournalEntryId, source.Timestamp,
            source.Account, source.Debit, source.Credit, source.Description, source.Dimensions! with { TaxLotId = null }, source.Currency);
        var journal = receipt.Journal with
        {
            Entry = new(original.JournalEntryId, original.Timestamp,
            original.Description, [changed, original.Lines[1]], original.Metadata)
        };
        fixture.JournalStore.Records[0] = journal;
        fixture.JournalStore.CorporateReceipts[original.JournalEntryId] = receipt with { Journal = journal };

        var capture = () => CaptureCorporatePack(fixture);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*Corporate-action journal*blocks canonical reporting*");
    }

    [Theory]
    [InlineData("batch-evidence")]
    [InlineData("required-evidence")]
    [InlineData("duplicate-evidence")]
    [InlineData("whitespace-duplicate-evidence")]
    [InlineData("batch-correction")]
    [InlineData("batch-relief")]
    [InlineData("batch-policy")]
    [InlineData("snapshots-missing")]
    [InlineData("snapshots-duplicate")]
    [InlineData("snapshots-changed")]
    public async Task CaptureAsync_CorporateAction_RefusesInconsistentBatchFacts(string field)
    {
        var fixture = CorporateReportingFixture();
        var receipt = fixture.JournalStore.CorporateReceipts.Values.Single()!;
        var evidence = receipt.RetainedEvidence.Skip(1).ToArray();
        RetainedEvidenceIdentityDto[] duplicateEvidence = [.. receipt.RetainedEvidence,
            receipt.RetainedEvidence[0] with { EvidenceId = $" {receipt.RetainedEvidence[0].EvidenceId} " }];
        fixture.JournalStore.CorporateReceipts[receipt.Journal.Entry.JournalEntryId] = field switch
        {
            "batch-evidence" => receipt with { RetainedEvidence = evidence },
            "required-evidence" => receipt with
            {
                RetainedEvidence = evidence,
                Mutations = receipt.Mutations.Select(mutation => mutation with { RetainedEvidence = evidence }).ToArray()
            },
            "duplicate-evidence" => receipt with { RetainedEvidence = [.. receipt.RetainedEvidence, receipt.RetainedEvidence[0]] },
            "whitespace-duplicate-evidence" => receipt with
            {
                RetainedEvidence = duplicateEvidence,
                Mutations = receipt.Mutations.Select(mutation => mutation with { RetainedEvidence = duplicateEvidence }).ToArray()
            },
            "batch-correction" => receipt with { CorrectsMutationBatchId = Guid.NewGuid() },
            "batch-relief" => receipt with { ReliefMethod = "FIFO" },
            "batch-policy" => receipt with { PolicyRevision = "different-policy" },
            "snapshots-missing" => receipt with { MutatedLots = [receipt.MutatedLots[0]] },
            "snapshots-duplicate" => receipt with { MutatedLots = [receipt.MutatedLots[0], receipt.MutatedLots[0]] },
            "snapshots-changed" => receipt with { MutatedLots = [receipt.MutatedLots[0], receipt.MutatedLots[1] with { UnitCost = 5m }] },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var capture = () => CaptureCorporatePack(fixture);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*Corporate-action journal*blocks canonical reporting*");
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock)]
    [InlineData(CorporateActionAccountingTypeDto.AdvanceRefunding)]
    public async Task CaptureAsync_CorporateActionBatch_CertifiesAllPartialAmortizedPredecessors(CorporateActionAccountingTypeDto action)
    {
        var fixture = CorporateReportingFixture(action, multiplePredecessors: true);
        var receipt = fixture.JournalStore.CorporateReceipts.Values.Single()!;

        var capture = await CaptureCorporatePack(fixture);

        var artifact = capture.CertifiedLedgerPresentation!.ReportPack.Artifacts.Single(item => item.Name == "corporate-action-lot-evidence.json");
        var reports = JsonSerializer.Deserialize<CanonicalCorporateActionLotReport[]>(artifact.Content)!;
        reports.Should().HaveCount(2);
        reports.Select(report => report.JournalEntryId).Distinct().Should().Equal(receipt.Journal.Entry.JournalEntryId);
        reports.Select(report => report.PredecessorBefore.TaxLotRecordId).Should().Equal(receipt.Mutations
            .Where(mutation => mutation.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose).Select(mutation => mutation.TaxLotRecordId));
        foreach (var report in reports)
        {
            report.PredecessorBefore.OpenQuantity.Should().BeLessThan(report.PredecessorBefore.OriginalQuantity);
            report.PredecessorAfter.OpenQuantity.Should().Be(0m);
            report.Successors.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(report.PredecessorBefore.OpenFunctionalCostBasis);
            report.Successors.Sum(lot => lot.Acquisition.FunctionalCostBasis).Should().BeGreaterThan(report.PredecessorBefore.OpenFunctionalCostBasis);
            report.Successors.Should().OnlyContain(lot => lot.AcquiredDate == report.PredecessorBefore.AcquiredDate
                && lot.Acquisition.HoldingPeriodStartDate == report.PredecessorBefore.Acquisition.HoldingPeriodStartDate);
        }
        reports[1].PredecessorBefore.AcquiredDate.Should().NotBe(reports[0].PredecessorBefore.AcquiredDate);
        if (action == CorporateActionAccountingTypeDto.AdvanceRefunding)
        {
            receipt.Mutations[0].QuantityBefore.Should().Be(800m);
            reports[0].PredecessorBefore.OpenQuantity.Should().Be(80000m);
            reports.SelectMany(report => report.Successors).Count(lot => lot.Acquisition.CorporateActionLineage!.ReportingTags.Contains("ScheduleD"))
                .Should().Be(2);
        }
    }

    [Theory]
    [InlineData("missing-group")]
    [InlineData("reordered-groups")]
    [InlineData("second-cost-basis")]
    [InlineData("missing-lot-dimension")]
    [InlineData("swapped-lot-dimensions")]
    [InlineData("successor-sleeve")]
    public async Task CaptureAsync_CorporateActionBatch_RefusesIncompleteOrAmbiguousProof(string field)
    {
        var fixture = CorporateReportingFixture(multiplePredecessors: true);
        var receipt = fixture.JournalStore.CorporateReceipts.Values.Single()!;
        var mutations = receipt.Mutations.ToArray();
        if (field == "missing-group")
            mutations = mutations.Take(2).ToArray();
        else if (field == "reordered-groups")
            mutations = [mutations[2] with { SelectionOrdinal = 0 }, mutations[3] with { SelectionOrdinal = 1 },
                mutations[0] with { SelectionOrdinal = 2 }, mutations[1] with { SelectionOrdinal = 3 }];
        else if (field == "second-cost-basis")
            mutations[2] = mutations[2] with { CostBasis = mutations[2].CostBasis + 1m };
        var original = receipt.Journal.Entry;
        var lines = original.Lines.Select((line, index) => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account, line.Debit, line.Credit, line.Description, field switch
            {
                "missing-lot-dimension" => line.Dimensions! with { TaxLotId = null },
                "swapped-lot-dimensions" when index == 0 => line.Dimensions! with { TaxLotId = original.Lines[1].Dimensions!.TaxLotId },
                "swapped-lot-dimensions" when index == 1 => line.Dimensions! with { TaxLotId = original.Lines[0].Dimensions!.TaxLotId },
                "successor-sleeve" when index == 1 => line.Dimensions! with { SleeveId = "different-sleeve" },
                _ => line.Dimensions
            }, line.Currency)).ToArray();
        // Bind both captured and receipt journals to the altered bytes, so the economics and
        // dimension checks must find the corruption independently of exact journal equality.
        var journal = receipt.Journal with { Entry = new(original.JournalEntryId, original.Timestamp, original.Description, lines, original.Metadata) };
        fixture.JournalStore.Records[0] = journal;
        fixture.JournalStore.CorporateReceipts[original.JournalEntryId] = receipt with
        { Journal = journal, Mutations = mutations, MutatedLots = mutations.Select(mutation => mutation.LotAfter).ToArray() };

        var capture = () => CaptureCorporatePack(fixture);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*Corporate-action journal*blocks canonical reporting*");
    }

    private static Task<ReportingAuthoritativeSourceCapture> CaptureCorporatePack(Fixture fixture)
        => fixture.Source.CaptureAsync(fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf },
            fixture.Access, new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement")).AsTask();

    private static Fixture CorporateReportingFixture(
        CorporateActionAccountingTypeDto actionType = CorporateActionAccountingTypeDto.StockSplit,
        bool multiplePredecessors = false, bool explicitLotIds = false)
    {
        var fixture = CreateFixture();
        var instruction = OpenLotCorporateActionTests.Instruction(actionType);
        instruction = instruction with
        {
            EffectiveDate = new DateOnly(2026, 7, 10),
            ExpectedLot = instruction.ExpectedLot with { LedgerBookId = fixture.Book.LedgerBookId }
        };
        if (multiplePredecessors)
            instruction = OpenLotCorporateActionTests.AddPredecessor(instruction);
        var groups = OpenLotCorporateAction.Groups(instruction);
        var projected = OpenLotCorporateAction.Project(instruction);
        var timestamp = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var recordedAt = timestamp.AddSeconds(1);
        var journalId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var account = new LedgerAccount("Investments", LedgerAccountType.Asset);
        var dimensions = new LedgerLineDimensionSet(fixture.FundId,
            CostCenterId: "cost-center-a", OrganizationId: fixture.OrganizationId.ToString("D"),
            BookId: fixture.Book.LedgerBookId.ToString("D"));
        var lines = new List<LedgerEntry>();
        var mutations = new List<LedgerTaxLotMutationRecord>();
        foreach (var group in groups)
        {
            var source = group.ExpectedLot;
            var face = source.Acquisition.QuantityBasis == LotQuantityBasis.Face;
            var scale = face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
            var before = new LedgerTaxLotRecord(source.TaxLotRecordId, source.LedgerBookId, account, source.LotId,
                source.AcquiredDate, source.OriginalQuantity / scale, source.OpenQuantity / scale,
                source.Acquisition.FunctionalCostBasis / (source.OriginalQuantity / scale), "USD", timestamp, timestamp,
                Version: source.Version, SecurityId: source.SecurityId, BookPositionId: source.BookPositionId,
                OriginalFace: face ? source.OriginalQuantity : null,
                BookedFactor: source.Acquisition.FaceValueTerms?.BookedFactor, ParBasis: source.Acquisition.FaceValueTerms?.ParBasis,
                Acquisition: source.Acquisition, BasisAdjustment: new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.Amortization,
                    source.OpenQuantity / scale, source.OpenTransactionCostBasis, source.OpenFunctionalCostBasis));
            var after = before with { OpenQuantity = 0m, Version = source.Version + 1, LastMutationBatchId = batchId, UpdatedAt = recordedAt };
            AddLine(source.TaxLotRecordId, source.SecurityId, source.BookPositionId,
                source.Acquisition, source.OpenTransactionCostBasis, source.OpenFunctionalCostBasis, false);
            AddMutation(AtomicTaxLotMutationKind.CorporateActionClose, before, after,
                source.OpenFunctionalCostBasis, instruction.SecurityEvidence.EvidenceId);
            foreach (var item in projected.Where(item => item.PredecessorTaxLotRecordId == source.TaxLotRecordId))
            {
                var target = item.Successor;
                var quantity = target.Quantity / scale;
                var successor = new LedgerTaxLotRecord(target.TaxLotRecordId, source.LedgerBookId, account, target.LotId,
                    source.AcquiredDate, quantity, quantity, item.AcquisitionFunctionalCostBasis / quantity, "USD", recordedAt, recordedAt,
                    journalId, target.AcquisitionEvidence.EvidenceId, 1, batchId, batchId,
                    target.Security.SecurityId, target.BookPositionId, face ? target.Quantity : null, before.BookedFactor, before.ParBasis,
                    source.Acquisition with
                    {
                        TransactionCostBasis = item.AcquisitionTransactionCostBasis,
                        FunctionalCostBasis = item.AcquisitionFunctionalCostBasis,
                        Evidence = OpenLotCorporateAction.Evidence(instruction),
                        CorporateActionLineage = new(instruction.CorporateActionId, instruction.ActionType, instruction.EffectiveDate,
                            source.TaxLotRecordId, source.Version, target.BasisAllocationPercent, target.Role, target.ReportingTags)
                    }, new(batchId, OpenLotBasisAdjustmentReasons.CorporateAction, quantity,
                        item.OpenTransactionCostBasis, item.OpenFunctionalCostBasis));
                AddLine(target.TaxLotRecordId, target.Security.SecurityId, target.BookPositionId,
                    source.Acquisition, item.OpenTransactionCostBasis, item.OpenFunctionalCostBasis, true);
                AddMutation(AtomicTaxLotMutationKind.CorporateActionSuccessor, null, successor,
                    item.OpenFunctionalCostBasis, target.AcquisitionEvidence.EvidenceId);
            }
        }
        var journal = new JournalEntry(journalId, timestamp, "Reviewed corporate action", lines,
            new JournalEntryMetadata(EffectiveDate: instruction.EffectiveDate, Tags: new Dictionary<string, string>
            {
                ["lotCorporateActionHash"] = OpenLotCorporateAction.Fingerprint(instruction),
                ["lotCorporateActionInputs"] = JsonSerializer.Serialize(instruction)
            }));
        var record = Record(fixture, timestamp, 11) with { Entry = journal, SourceEventId = instruction.CorporateActionId };
        fixture.JournalStore.Records.Add(record);
        fixture.JournalStore.CorporateReceipts[journalId] = new(batchId, AtomicTaxLotMutationKind.CorporateAction,
            new string('a', 64), false, record, mutations.Select(mutation => mutation.LotAfter).ToArray(),
            mutations, OpenLotCorporateAction.Evidence(instruction));
        return fixture;

        void AddLine(Guid lotId, Guid securityId, Guid positionId, OpenLotAcquisitionDto acquisition,
            decimal transaction, decimal functional, bool debit)
            => lines.Add(new LedgerEntry(Guid.NewGuid(), journalId, timestamp, account,
                debit ? functional : 0m, debit ? 0m : functional, "Reviewed corporate action",
                dimensions with
                {
                    InstrumentId = securityId,
                    PositionId = positionId,
                    TaxLotId = explicitLotIds || multiplePredecessors ? lotId.ToString("D") : null
                },
                new LedgerEntryCurrency(acquisition.AcquisitionCurrency, acquisition.FunctionalCurrency,
                    debit ? transaction : 0m, debit ? 0m : transaction, acquisition.AcquisitionFxRateToFunctional)));

        void AddMutation(AtomicTaxLotMutationKind kind, LedgerTaxLotRecord? old, LedgerTaxLotRecord current,
            decimal costBasis, string selectionEvidenceId)
            => mutations.Add(new(Guid.NewGuid(), batchId, kind, current.TaxLotRecordId, current.LotId, mutations.Count,
                old?.OpenQuantity ?? 0m, current.OpenQuantity - (old?.OpenQuantity ?? 0m), current.OpenQuantity,
                current.UnitCost, costBasis, old?.Version ?? 0, current.Version, selectionEvidenceId, journalId,
                instruction.CorporateActionId, OpenLotCorporateAction.Evidence(instruction), recordedAt, old, current,
                SecurityId: current.SecurityId, BookPositionId: current.BookPositionId));
    }
}
