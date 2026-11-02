using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

// A stored declaration that predates a schedule-terms gap rule stays amendable: a patch-less amendment
// keeps it under the rules it was stored under, while a patch that changes the container is judged in full.
public sealed partial class SecurityMasterServiceSnapshotTests
{
    private const string RetainedEuropeanWithEnd =
        """[{"callDate":"2028-01-01","callEndDate":"2028-06-01","exerciseStyle":"European"}]""";

    [Fact]
    public async Task AmendWithoutTermsPatch_KeepsARetainedDeclarationThatPredatesAGapRule()
    {
        var (service, events, _, store) = CanonicalAdmissionWriter();
        using var _ = service;
        var id = Guid.NewGuid();
        store.GetProjectionAsync(id, Arg.Any<CancellationToken>()).Returns(
            CreateProjection(id, "Bond", SecurityStatusDto.Active, "Retained declared bond", 2) with
            {
                AssetSpecificTerms = Parse(AdmissionBond(RetainedEuropeanWithEnd))
            });

        var detail = await service.AmendTermsAsync(TermsAmendRequest(id, assetSpecificTermsPatch: null));

        detail.AssetSpecificTerms.GetProperty("callSchedule")[0].GetProperty("callEndDate").GetString().Should().Be("2028-06-01");
        await events.Received(1).AppendAsync(id, 2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendWithAPatchThatChangesTheContainer_JudgesItUnderTheGapRules()
    {
        var (service, events, snapshots, store) = CanonicalAdmissionWriter();
        using var _ = service;
        var id = Guid.NewGuid();
        store.GetProjectionAsync(id, Arg.Any<CancellationToken>()).Returns(
            CreateProjection(id, "Bond", SecurityStatusDto.Active, "Retained declared bond", 2) with
            {
                AssetSpecificTerms = Parse(AdmissionBond("""[{"callDate":"2028-01-01","exerciseStyle":"European"}]"""))
            });
        var patch = JsonSerializer.Deserialize<JsonElement>(AdmissionBond(RetainedEuropeanWithEnd));

        var action = () => service.AmendTermsAsync(TermsAmendRequest(id, assetSpecificTermsPatch: patch));

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Declared callSchedule[0] is European, which exercises on its start date only, so it cannot declare a callEndDate.");
        await AssertNoCapabilityMutations(events, snapshots, store);
    }

    [Fact]
    public async Task AmendWithAPatchThatEchoesTheRetainedContainer_IsAccepted()
    {
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        var stored = AdmissionBond(RetainedEuropeanWithEnd);
        harness.Seed(securityId, "Bond", stored);

        await harness.Service.AmendTermsAsync(AmendRequest(securityId, Parse(stored)));

        harness.Appended.Should().NotBeEmpty("an echo states nothing new, so the rules the row was stored under apply");
    }

    [Fact]
    public async Task AmendTermBlockAsync_OverARetainedDeclarationThatPredatesAGapRule_IsAccepted()
    {
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", AdmissionBond(RetainedEuropeanWithEnd));

        await harness.Service.AmendTermBlockAsync(
            securityId,
            "principalSchedule",
            new AmendSecurityTermBlockRequest(
                ExpectedVersion: 2,
                Block: Parse("""[{"paymentDate":"2029-01-01","amount":500}]"""),
                EffectiveFrom: DateTimeOffset.UtcNow,
                SourceSystem: "test",
                UpdatedBy: "codex",
                SourceRecordId: null,
                Reason: "replace the principal schedule"));

        harness.Appended.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("scheduleConventions", """{"BusinessDayConvention":null}""", "scheduleConventions.BusinessDayConvention")]
    [InlineData("callSchedule", """[{"callDate":"2028-01-01","exerciseStyle":"European"},{"calDate":null}]""", "callSchedule.calDate")]
    public async Task AmendWithAnUnsetContainerHoldingAnUnknownName_IsRefusedByR30(string key, string container, string member)
    {
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        var stored = AdmissionBond("""[{"callDate":"2028-01-01","exerciseStyle":"European"}]""");
        harness.Seed(securityId, "Bond", stored);
        var patch = System.Text.Json.Nodes.JsonNode.Parse(stored)!.AsObject();
        patch[key] = System.Text.Json.Nodes.JsonNode.Parse(container);

        await harness.Service.Invoking(service => service.AmendTermsAsync(AmendRequest(securityId, Parse(patch.ToJsonString()))))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Unknown, case-variant or repeated member '{member}'.*");

        harness.Appended.Should().BeEmpty();
    }
}
