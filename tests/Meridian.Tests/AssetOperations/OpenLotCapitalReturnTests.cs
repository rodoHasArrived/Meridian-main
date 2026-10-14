using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotCapitalReturnTests
{
    [Theory]
    [InlineData(AccountingBasisKindDto.Gaap)]
    [InlineData(AccountingBasisKindDto.Primary)]
    public void CapitalReturn_PreservesQuantityAcquisitionFxHoldingAndIndependentBases(AccountingBasisKindDto basis)
    {
        var instruction = CapitalReturnTestData.Build(OpenLotSuccessorTestData.Predecessor(), accountingBasis: basis);
        var after = OpenLotCapitalReturn.Project(instruction);
        after.OpenTransactionCostBasis.Should().Be(584.03m);
        after.OpenFunctionalCostBasis.Should().Be(642.4m);
        after.OpenQuantity.Should().Be(instruction.ExpectedLot.OpenQuantity);
        after.Acquisition.Should().BeSameAs(instruction.ExpectedLot.Acquisition);
        after.Version.Should().Be(instruction.ExpectedLot.Version + 1);
        OpenLotCapitalReturn.Project(JsonSerializer.Deserialize<OpenLotCapitalReturnInstructionDto>(
            JsonSerializer.Serialize(instruction))!).Should().BeEquivalentTo(after);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("transaction")]
    [InlineData("functional")]
    [InlineData("version")]
    [InlineData("holding")]
    [InlineData("source")]
    public void CapitalReturn_RejectsAlteredApprovedTransition(string change)
    {
        var instruction = CapitalReturnTestData.Build(OpenLotSuccessorTestData.Predecessor());
        var mutation = instruction.Projection.LotMutations!.Mutations.Single();
        mutation = change switch
        {
            "quantity" => mutation with { SourceAfter = mutation.SourceAfter! with { Quantity = 59m } },
            "transaction" => mutation with { SourceAfter = mutation.SourceAfter! with { BasisAmount = 0m } },
            "functional" => mutation with { CarryingAmount = 12m },
            "version" => mutation with { ExpectedSourceLotVersion = mutation.ExpectedSourceLotVersion + 1 },
            "holding" => mutation with { HoldingPeriodTreatment = CorporateActionHoldingPeriodTreatmentDto.PolicyDefined },
            _ => mutation with { SourceLotId = Guid.NewGuid() }
        };
        var changed = instruction with
        {
            Projection = instruction.Projection with
            { LotMutations = instruction.Projection.LotMutations with { Mutations = [mutation] } }
        };
        var apply = () => OpenLotCapitalReturn.Project(changed);
        apply.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CapitalReturn_ZeroRemainingBasisDoesNotCloseTheHolding()
    {
        var lot = OpenLotSuccessorTestData.Predecessor();
        var instruction = CapitalReturnTestData.Build(lot, lot.OpenTransactionCostBasis, lot.OpenFunctionalCostBasis);
        var after = OpenLotCapitalReturn.Project(instruction);
        after.OpenTransactionCostBasis.Should().Be(0m);
        after.OpenFunctionalCostBasis.Should().Be(0m);
        after.OpenQuantity.Should().Be(60m);
    }

    [Fact]
    public void Mapper_RequiresAndRetainsTheExactCanonicalReduction()
    {
        var instruction = CapitalReturnTestData.Build(OpenLotSuccessorTestData.Predecessor());
        var request = CapitalReturnTestData.MapRequest(instruction);
        var mapper = new CorporateActionAssetAccountingEventMapper();
        var mapped = mapper.Map(request);
        mapped.IsMapped.Should().BeTrue(string.Join("; ", mapped.Blockers.Select(item => item.Message)));
        mapped.Projection!.Event.CapitalReturn.Should().BeSameAs(instruction);
        mapped.Projection.LotMutation!.CapitalReturn.Should().BeSameAs(instruction);
        mapper.Map(request with { CapitalReturnInstruction = null }).Blockers
            .Should().Contain(item => item.Code == "corporate-action.canonical-capital-return-required");
        mapper.Map(request with { RetainedEvidence = [] }).IsMapped.Should().BeFalse();
    }

    [Fact]
    public void LegacyInstructionsKeepAbsentCapitalReturnFieldsOutOfFingerprints()
    {
        JsonSerializer.Serialize(new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.None))
            .Should().NotContain("CapitalReturn");
        JsonSerializer.Serialize(new OpenLotBasisAdjustmentDto(Guid.NewGuid(), "Amortization", 1m, 10m, 10m))
            .Should().NotContain("CapitalReturn");
    }

    [Fact]
    public void CapitalReturn_SameCurrencyCannotRetainDifferentBasisReductions()
    {
        var original = OpenLotSuccessorTestData.Predecessor();
        var lot = original with
        {
            OpenTransactionCostBasis = original.OpenFunctionalCostBasis,
            Acquisition = original.Acquisition with
            {
                AcquisitionCurrency = "USD",
                AcquisitionFxRateToFunctional = 1m,
                TransactionCostBasis = original.Acquisition.FunctionalCostBasis
            }
        };
        var instruction = CapitalReturnTestData.Build(lot);
        var apply = () => OpenLotCapitalReturn.Project(instruction);
        apply.Should().Throw<ArgumentException>().WithMessage("*both reviewed basis reductions*");
    }
    [Fact]
    public void CapitalReturn_PreservesOnlyTheExactRetainedHistoricalEvidenceDate()
    {
        var instruction = CapitalReturnTestData.Build(OpenLotSuccessorTestData.Predecessor());
        var evidence = instruction.ExpectedLot.Acquisition.Evidence.First();
        var lot = instruction.ExpectedLot;
        var date = instruction.Projection.EconomicEvent!.EffectiveDate;
        (evidence.EffectiveDate < date).Should().BeTrue();
        bool Admitted(RetainedEvidenceIdentityDto item) => AssetAccountingEvidenceSubjects.MatchesEventEvidenceDate(
            AssetAccountingEventKindDto.CorporateAction, date, lot.SecurityId, instruction.ExpectedSecurityVersion,
            item, null, requireInstruction: true, capitalReturn: instruction);
        Admitted(evidence).Should().BeTrue();
        Admitted(evidence with { ContentHashSha256 = new string('f', 64) }).Should().BeFalse();
        Admitted(evidence with { EffectiveDate = date.AddDays(1) }).Should().BeFalse();
        Admitted(evidence with { SubjectId = Guid.NewGuid().ToString("D") }).Should().BeFalse();
    }
}

internal static class CapitalReturnTestData
{
    internal static OpenLotCapitalReturnInstructionDto Build(OpenLotDto lot,
        decimal transactionReduction = 9.97m, decimal functionalReduction = 11m,
        long positionVersion = 1, long securityVersion = 1, string? securityHash = null,
        Guid? periodId = null, long periodVersion = 1, string fund = "fund-alpha",
        DateOnly? date = null, Guid? actionId = null, AccountingBasisKindDto accountingBasis = AccountingBasisKindDto.Gaap)
    {
        var effective = date ?? OpenLotSuccessorTestData.EffectiveDate;
        var observed = new DateTimeOffset(effective.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);
        var sourceId = actionId ?? Guid.NewGuid();
        var positionSnapshot = Guid.NewGuid();
        var lotSnapshot = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        CorporateActionProjectionEvidenceDependencyDto Dependency(CorporateActionProjectionEvidenceRoleDto role,
            Guid id, long version, string subject, char hash)
            => new(role, role.ToString(), $"evidence://capital-return/{id:D}", new string(hash, 64), version, subject, id.ToString("D"));
        var request = new CorporateActionAccountingProjectionRequest(sourceId, 1, CorporateActionAccountingTypeDto.ReturnOfCapital,
            accountingBasis, lot.SecurityId, lot.BookPositionId, positionVersion, positionVersion,
            effective, effective, observed, lot.Acquisition.FunctionalCurrency, "SecurityMaster", sourceId.ToString("D"), new string('a', 64),
            new CorporateActionEconomicsDto(PositionQuantity: lot.OpenQuantity, CarryingAmount: lot.OpenFunctionalCostBasis,
                GrossCashConsideration: functionalReduction),
            EvidenceManifest:
            [
                Dependency(CorporateActionProjectionEvidenceRoleDto.SourceEvent, sourceId, 1, "SecurityMasterCorporateAction", 'a'),
                Dependency(CorporateActionProjectionEvidenceRoleDto.PositionSnapshot, positionSnapshot, positionVersion, "PositionSnapshot", 'b'),
                Dependency(CorporateActionProjectionEvidenceRoleDto.LotSnapshot, lotSnapshot, lot.Version, "LotSnapshot", 'c'),
                Dependency(CorporateActionProjectionEvidenceRoleDto.PolicyDecision, policyId, 2, "CorporateActionPolicyDecision", 'd')
            ],
            CaseId: Guid.NewGuid(), CaseVersion: 3, PolicyDecisionVersion: 2, PositionSnapshotId: positionSnapshot,
            AccountingScope: new CorporateActionAccountingProjectionScopeDto("tenant-alpha", "company-alpha", fund,
                lot.LedgerBookId, periodId ?? Guid.NewGuid(), periodVersion, "US"),
            LotSnapshotId: lotSnapshot, LotSnapshotVersion: lot.Version, PolicyDecisionId: policyId);
        var service = new CorporateActionAccountingProjectionService();
        var intent = service.Project(request);
        intent.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        var mutation = intent.LotMutations!.Mutations.Single() with
        {
            SourceLotId = lot.TaxLotRecordId,
            ExpectedSourceLotVersion = lot.Version,
            SourceBefore = new(lot.OpenQuantity, lot.OpenFunctionalCostBasis, lot.OpenTransactionCostBasis),
            SourceAfter = new(lot.OpenQuantity, lot.OpenFunctionalCostBasis - functionalReduction,
                lot.OpenTransactionCostBasis - transactionReduction),
            BasisAmount = transactionReduction
        };
        var projection = service.Project(request with { AuthoritativeLotMutations = [mutation] });
        projection.CanPreparePostingCandidate.Should().BeTrue(string.Join("; ", projection.Blockers.Select(item => item.Message)));
        return new(projection, lot, securityVersion, securityHash ?? Sha256Digest.ComputeUtf8("source-security"));
    }

    internal static CorporateActionAssetAccountingEventMapRequest MapRequest(OpenLotCapitalReturnInstructionDto instruction)
    {
        var projection = instruction.Projection;
        var lot = instruction.ExpectedLot;
        var scope = projection.AccountingScope!;
        var economicEvent = projection.EconomicEvent!;
        var lineage = projection.ProjectionLineage!;
        var eventScope = new AssetAccountingEventScopeDto(lot.SecurityId, instruction.ExpectedSecurityVersion,
            lot.BookPositionId, projection.LotMutations!.ExpectedPositionVersion, lot.LedgerBookId,
            scope.PeriodId, projection.Treatment.AccountingBasis, scope.FundProfileId, scope.TenantId, scope.CompanyId);
        var dimensions = new LedgerDimensionSetDto(FundId: scope.FundProfileId, InstrumentId: lot.SecurityId,
            BookId: lot.LedgerBookId.ToString("D"), TaxLotId: lot.LotId)
        { PositionId = lot.BookPositionId };
        var amount = projection.EventAmount;
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion,
            economicEvent.EffectiveDate, amount, amount, lot.Acquisition.FunctionalCurrency,
            [new("Assets:Cash", amount, 0m, lot.Acquisition.FunctionalCurrency, Dimensions: dimensions),
             new("Assets:Investment", 0m, amount, lot.Acquisition.FunctionalCurrency, Dimensions: dimensions)]);
        var mapped = CorporateActionMappedAccountingEffectAttestor.Create(projection, eventScope, effect,
            new AccountingRulePackReferenceDto("pack-asset", "v7", "rule-valuation", "v2"),
            [new(0, CorporateActionPostingComponentKindDto.Cash, [new(0, amount)], "cash-return"),
             new(1, CorporateActionPostingComponentKindDto.ReturnOfCapital, [new(1, amount)], "basis-reduction")]);
        var observed = lineage.GeneratedAtUtc.AddHours(1);
        var evidence = projection.EvidenceManifest.Select(item => new RetainedEvidenceIdentityDto(
            item.EvidenceId, item.EvidenceUri, item.ContentHashSha256, economicEvent.SourceDomain, item.EvidenceId,
            "Accepted", "controller", observed, economicEvent.EffectiveDate, item.EvidenceVersion,
            observed.AddMinutes(1), "evidence-vault", item.SubjectType, item.SubjectId))
            .Concat(lot.Acquisition.Evidence).ToList();
        evidence.Add(new("capital-return-event", "evidence://capital-return/event", economicEvent.SourceContentHash!,
            economicEvent.SourceDomain, economicEvent.SourceEntityId!, "Accepted", "controller", observed,
            economicEvent.EffectiveDate, economicEvent.EventVersion, observed.AddMinutes(1), "evidence-vault",
            AssetAccountingEvidenceSubjects.Event, economicEvent.EventId.ToString("D")));
        return new(projection, eventScope, mapped, scope.ExpectedPeriodVersion, "projector", observed.AddHours(1), evidence,
            CapitalReturnInstruction: instruction);
    }
}
