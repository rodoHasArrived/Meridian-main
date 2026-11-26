using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityAssetTermsSchemaRoundTripTests
{
    [Fact]
    public void EveryVersionedRootTerm_HasAnExplicitHistoricalOrCompanionReaderTestOwner()
    {
        // The v5 workflow's separate admission suite owns typed round-trip, inert older stamps
        // and the governed publish boundary. Name the actual methods so the ownership cannot drift.
        const string workflowReader = nameof(Workbench.BondWorkflowTermsAdmissionTests.BondWorkflowVersionFiveSurvivesTypedRecordAndSnapshotCodec);
        const string workflowHistory = nameof(Workbench.BondWorkflowTermsAdmissionTests.BondWorkflowOlderStampResidueCannotActivateOrChangeHistoricalCodecBytes);
        const string workflowPublisher = nameof(Workbench.SecurityMasterWorkbenchCommandServiceTests.BondWorkflowExplicitGovernedEditStagesPublishesAndPersistsVersionFive);
        const string cdReader = nameof(CertificateOfDepositVersionSixAnchors_RoundTripTypedValues_AndStayInertAtVersionFive);
        const string cdPublisher = nameof(SecurityMasterServiceSnapshotTests.UndeclaredCdScalars_RequireACompleteReviewedReplacementOrExplicitClear);
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var floor = SecurityAssetClassCatalog.GetStampFloor(assetClass);
            SecurityAssetTermsSchema.Fields(assetClass)
                .Concat(SecurityAssetTermsSchema.Fields(assetClass).SelectMany(field => SecurityAssetTermsSchema.ElementFields(assetClass, field.Key)))
                .Where(field => field.SinceTermsVersion > floor)
                .Should().OnlyContain(field => field.Key == ScheduleConventionsKey || field.Key == CallScheduleKey
                    || field.Key == PutScheduleKey || field.Key == SecurityContractualCashFlowTerms.Key
                    || field.Key == SecurityRecordRateUnit.FieldKey || assetClass == "Bond" && field.Key == "bondWorkflow"
                    || assetClass == "CertificateOfDeposit" && field.SinceTermsVersion == 6
                        && (field.Key == "issueDate" || field.Key == "principal" || field.Key == "paymentFrequency"),
                    $"a new readable term must add explicit typed-reader, round-trip and write-boundary coverage; Bond.bondWorkflow is owned by {workflowReader}, {workflowHistory} and {workflowPublisher}; CD v6 anchors are owned by {cdReader} and {cdPublisher}");
        }
    }

    [Fact]
    public void CertificateOfDepositVersionSixAnchors_RoundTripTypedValues_AndStayInertAtVersionFive()
    {
        var declared = SecurityRecurringAccrualPolicyTests.Security().AssetSpecificTerms;
        var record = SecurityMasterMapping.ToRecord(LegacyProjection("CertificateOfDeposit", declared));
        var terms = ((SecurityKind.CertificateOfDeposit)record.Kind).Item;
        terms.IssueDate.Should().NotBeNull();
        terms.IssueDate!.Value.Should().Be(new DateOnly(2025, 12, 31));
        terms.Principal.Should().NotBeNull();
        terms.Principal!.Value.Should().Be(100_000m);
        terms.PaymentFrequency.Should().NotBeNull();
        var written = Parse(new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        written.GetProperty("issueDate").GetString().Should().Be("2025-12-31");
        written.GetProperty("principal").GetDecimal().Should().Be(100_000m);
        written.GetProperty("paymentFrequency").GetString().Should().Be("AtMaturity");
        written.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(6);
        ReadAndRewrite("CertificateOfDeposit", written).Should().Be(written.GetRawText());

        var retained = SecurityRecurringAccrualPolicyTests.Security("assetClassTermsVersion", "5").AssetSpecificTerms;
        var oldRecord = SecurityMasterMapping.ToRecord(LegacyProjection("CertificateOfDeposit", retained));
        var oldTerms = ((SecurityKind.CertificateOfDeposit)oldRecord.Kind).Item;
        oldTerms.IssueDate.Should().BeNull();
        oldTerms.Principal.Should().BeNull();
        oldTerms.PaymentFrequency.Should().BeNull();
        var historical = Parse(new SecurityMasterSnapshotWrapper(oldRecord).AssetSpecificTermsJson);
        historical.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(5);
        foreach (var key in new[] { "issueDate", "principal", "paymentFrequency" })
            historical.TryGetProperty(key, out _).Should().BeFalse("pre-v6 residue remains undeclared");
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
