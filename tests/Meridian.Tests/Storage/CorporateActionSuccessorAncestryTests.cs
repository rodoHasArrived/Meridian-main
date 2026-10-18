using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;

namespace Meridian.Tests.Storage;

public sealed partial class CorporateActionSuccessorAncestryTests
{
    private static readonly LedgerAccount Investment = new("Split investments", LedgerAccountType.Asset);
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierSourceAction_CannotRepeatAfterAnInterveningActionEvenWithFreshCaseAndVersion(bool legacy)
    {
        var chain = BuildChain(legacy);
        var repeated = Split(chain.Current, chain.ActionA, 2m, 2, legacy);
        var validateImmediateOrigin = () => OpenLotSuccessors.Validate(repeated);
        validateImmediateOrigin.Should().NotThrow("the current lot's immediate origin is action B");
        repeated.Projection.CaseId.Should().NotBe(chain.First.Projection.CaseId);
        repeated.Projection.EconomicEvent!.EventId.Should().NotBe(chain.First.Projection.EconomicEvent!.EventId);
        repeated.Projection.EconomicEvent.EventVersion.Should().Be(2);
        OpenLotSuccessors.GetSourceCorporateActionId(repeated.Projection).Should().Be(chain.ActionA);
        if (legacy)
        {
            chain.Current.Acquisition!.CorporateActionLineage.Should().BeNull();
            chain.First.Projection.SourceCorporateActionId.Should().BeNull();
            repeated.Projection.SourceCorporateActionId.Should().BeNull();
        }
        var loaded = new List<Guid>();

        var validateAncestry = () => CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, repeated,
            id => { loaded.Add(id); return Task.FromResult(chain.Receipts.GetValueOrDefault(id)); });

        await validateAncestry.Should().ThrowAsync<LedgerValidationException>().WithMessage("*repeat a source corporate action*");
        loaded.Should().Equal(chain.SecondBatch, chain.FirstBatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctSourceAction_TraversesBothSuccessorBirthReceiptsAndAcceptsOriginalAcquisition(bool legacy)
    {
        var chain = BuildChain(legacy);
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(2));
        OpenLotSuccessors.Validate(instruction);
        var loaded = new List<Guid>();

        await CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => { loaded.Add(id); return Task.FromResult(chain.Receipts.GetValueOrDefault(id)); });

        loaded.Should().Equal(chain.SecondBatch, chain.FirstBatch, chain.AcquisitionBatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalLineageWithoutStableSourceId_IsCertifiedWithoutChangingRetainedInstructions(bool suppliedTargetOrigins)
    {
        var chain = BuildChain(legacy: true, retainLegacyLineage: true, suppliedTargetOrigins: suppliedTargetOrigins);
        var origin = chain.Current.Acquisition!.CorporateActionLineage!;
        origin.Should().NotBeNull();
        origin.SourceCorporateActionId.Should().BeNull();
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy: false,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(2));
        OpenLotSuccessors.Validate(instruction);
        var retained = JsonSerializer.Serialize(chain.Receipts);
        var fingerprints = chain.Receipts.Values.Where(receipt => receipt.CorporateAction is not null)
            .ToDictionary(receipt => receipt.MutationBatchId, receipt => OpenLotSuccessors.Fingerprint(receipt.CorporateAction!));

        await CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => Task.FromResult(chain.Receipts.GetValueOrDefault(id)));

        JsonSerializer.Serialize(chain.Receipts).Should().Be(retained);
        foreach (var receipt in chain.Receipts.Values.Where(receipt => receipt.CorporateAction is not null))
            OpenLotSuccessors.Fingerprint(receipt.CorporateAction!).Should().Be(fingerprints[receipt.MutationBatchId]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitSourceIdentity_RequiresThatIdentityInRetainedLineage(bool suppliedTargetOrigins)
    {
        var chain = BuildChain(legacy: true, retainLegacyLineage: true, suppliedTargetOrigins: suppliedTargetOrigins);
        var receipts = chain.Receipts.ToDictionary(pair => pair.Key, pair => pair.Value.CorporateAction is not { } ancestor
            ? pair.Value : pair.Value with
            {
                CorporateAction = ancestor with
                {
                    Projection = ancestor.Projection with
                    { SourceCorporateActionId = OpenLotSuccessors.GetSourceCorporateActionId(ancestor.Projection) }
                }
            });
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy: false,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(2));

        var certify = () => CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => Task.FromResult(receipts.GetValueOrDefault(id)));

        await certify.Should().ThrowAsync<LedgerValidationException>().WithMessage("*ancestry cannot be certified*");
    }

    [Fact]
    public async Task ExplicitSourceIdentity_RejectsEntirelyMissingRetainedOrigin()
    {
        var chain = BuildChain(legacy: true);
        var receipts = chain.Receipts.ToDictionary(pair => pair.Key, pair => pair.Value.CorporateAction is not { } ancestor
            ? pair.Value : pair.Value with
            {
                CorporateAction = ancestor with
                {
                    Projection = ancestor.Projection with
                    { SourceCorporateActionId = OpenLotSuccessors.GetSourceCorporateActionId(ancestor.Projection) }
                }
            });
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy: false,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(2));

        var certify = () => CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => Task.FromResult(receipts.GetValueOrDefault(id)));

        await certify.Should().ThrowAsync<LedgerValidationException>().WithMessage("*ancestry cannot be certified*");
    }

    [Fact]
    public async Task LegacyAverageCostSurvivor_TraversesMixedDisposalAndBasisRedistributionReceipt()
    {
        var chain = BuildChain(legacy: false);
        var pristine = chain.Receipts[chain.AcquisitionBatch].MutatedLots.Single() with
        {
            OriginatingMutationBatchId = null,
            LastMutationBatchId = null
        };
        var batch = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var current = pristine with
        {
            Version = 2,
            LastMutationBatchId = batch,
            BasisAdjustment = new(batch, OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                60m, 630m, 693m)
        };
        var otherId = Guid.NewGuid();
        var other = pristine with
        {
            TaxLotRecordId = otherId,
            LotId = "selected-average-cost-lot",
            UnitCost = 12.1m,
            Acquisition = pristine.Acquisition! with
            {
                TransactionCostBasis = 660m,
                FunctionalCostBasis = 726m,
                Evidence = [pristine.Acquisition!.Evidence[0] with
                {
                    EvidenceId = "selected-average-cost-acquisition",
                    SubjectId = otherId.ToString("D")
                }]
            }
        };
        var closed = other with { OpenQuantity = 0m, Version = 2, LastMutationBatchId = batch };
        var survivorMutation = new LedgerTaxLotMutationRecord(Guid.NewGuid(), batch, AtomicTaxLotMutationKind.BasisRedistribution,
            current.TaxLotRecordId, current.LotId, 1, 60m, 0m, 60m, current.UnitCost, 33m, 1, 2,
            current.Acquisition!.Evidence[0].EvidenceId, Guid.NewGuid(), eventId, current.Acquisition.Evidence,
            RecordedAt, pristine, current, ReliefMethod: "AverageCost", SecurityId: current.SecurityId, BookPositionId: current.BookPositionId);
        var disposal = new LedgerTaxLotMutationRecord(Guid.NewGuid(), batch, AtomicTaxLotMutationKind.Disposal,
            other.TaxLotRecordId, other.LotId, 0, 60m, -60m, 0m, other.UnitCost, 693m, 1, 2,
            other.Acquisition!.Evidence[0].EvidenceId, Guid.NewGuid(), eventId, other.Acquisition.Evidence,
            RecordedAt, other, closed, ReliefMethod: "AverageCost", SecurityId: other.SecurityId, BookPositionId: other.BookPositionId);
        var receipt = Receipt(batch, AtomicTaxLotMutationKind.Disposal, [disposal, survivorMutation]);
        var instruction = Split(current, Guid.NewGuid(), 2m, 1, legacy: false);
        OpenLotSuccessors.Validate(instruction);
        var loaded = new List<Guid>();

        await CorporateActionSuccessorAncestry.ValidateAsync(current, instruction,
            id => { loaded.Add(id); return Task.FromResult<AtomicTaxLotJournalResult?>(id == batch ? receipt : null); });

        loaded.Should().Equal(batch);
        current.BasisAdjustment!.TransactionCostBasis.Should().Be(630m);
        pristine.BasisAdjustment.Should().BeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cyclic")]
    [InlineData("cross-book")]
    public async Task UncertifiableBirthReceipt_RefusesFreshAction(string corruption)
    {
        var chain = BuildChain(legacy: false);
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy: false);
        var receipts = new Dictionary<Guid, AtomicTaxLotJournalResult>(chain.Receipts);
        var second = receipts[chain.SecondBatch];
        if (corruption == "missing")
            receipts.Remove(chain.FirstBatch);
        else if (corruption == "cyclic")
        {
            var predecessor = second.Mutations.Single(item => item.LotBefore is not null);
            receipts[chain.SecondBatch] = second with
            {
                Mutations = second.Mutations.Select(item => item == predecessor
                    ? item with { LotBefore = item.LotBefore! with { OriginatingMutationBatchId = chain.SecondBatch } }
                    : item).ToArray()
            };
        }
        else
        {
            var opening = second.Mutations.Single(item => item.LotBefore is null);
            receipts[chain.SecondBatch] = second with
            {
                Mutations = second.Mutations.Select(item => item == opening
                    ? item with { LotAfter = item.LotAfter with { LedgerBookId = Guid.NewGuid() } }
                    : item).ToArray()
            };
        }

        var validate = () => CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => Task.FromResult(receipts.GetValueOrDefault(id)));

        await validate.Should().ThrowAsync<LedgerValidationException>().WithMessage("*ancestry cannot be certified*");
    }

}
