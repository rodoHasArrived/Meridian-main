using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityAssetTermsSchemaRoundTripTests
{
    [Fact]
    public void EveryVersionedRootTerm_HasAnExplicitHistoricalOrCompanionReaderTestOwner()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var floor = SecurityAssetClassCatalog.GetStampFloor(assetClass);
            SecurityAssetTermsSchema.Fields(assetClass)
                .Concat(SecurityAssetTermsSchema.Fields(assetClass).SelectMany(field => SecurityAssetTermsSchema.ElementFields(assetClass, field.Key)))
                .Where(field => field.SinceTermsVersion > floor)
                .Should().OnlyContain(field => field.Key == ScheduleConventionsKey || field.Key == CallScheduleKey
                    || field.Key == PutScheduleKey || field.Key == SecurityContractualCashFlowTerms.Key,
                    "a new readable term must add explicit typed-reader, round-trip and write-boundary coverage");
        }
    }

    [Theory]
    [InlineData("Bond", 4)]
    [InlineData("CertificateOfDeposit", 4)]
    [InlineData("StructuredCredit", 5)]
    [InlineData("Swap", 3)]
    [InlineData("DirectLoan", 2)]
    public void CalculationCompanion_RoundTripsThroughItsTypedReaderAndEconomicReplay_AndCannotReachAnOlderWriter(string assetClass, int version)
    {
        var projection = CurrentProjection(assetClass);
        var contract = new ContractualCashFlowContract(new(2026, 1, 1), new(2027, 1, 1), projection.Currency,
            [new("main", 1000, 3, DayCountConvention.Actual365, .05m, RateSteps: [new(new(2026, 7, 1), .06m)],
                PrincipalSchedule: [new(new(2026, 10, 1), 200)], PikFraction: .25m)],
            [new(CashFlowExerciseSide.Call, new(2026, 4, 1), null, CashFlowExerciseStyle.Bermudan, 100, "main",
                ExerciseMonths: 3, PartialRedemption: "WholeOnly", MinimumNoticeDays: 30, NoticeDayType: "Calendar")]);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var payload = JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())!.AsObject();
        payload[SecurityTermsEnvelope.AssetClassTermsVersionKey] = version;
        payload[SecurityContractualCashFlowTerms.Key] = JsonSerializer.SerializeToNode(contract, json);
        projection = projection with { AssetSpecificTerms = JsonSerializer.SerializeToElement(payload) };
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic);
        JsonNode.DeepEquals(payload[SecurityContractualCashFlowTerms.Key],
            JsonNode.Parse(replayed.AssetSpecificTerms.GetProperty(SecurityContractualCashFlowTerms.Key).GetRawText())).Should().BeTrue();
        var decoded = SecurityContractualCashFlowTerms.Read(SecurityMasterMapping.ToDetail(replayed));
        ContractualCashFlowEngine.Hash(decoded).Should().Be(ContractualCashFlowEngine.Hash(contract));
        var create = () => SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, projection.AssetSpecificTerms));
        var amend = () => SecurityMasterMapping.ToAmendCommand(AmendRequest(projection.AssetSpecificTerms), CurrentProjection(assetClass));
        create.Should().Throw<InvalidOperationException>();
        amend.Should().Throw<InvalidOperationException>();
    }
}
