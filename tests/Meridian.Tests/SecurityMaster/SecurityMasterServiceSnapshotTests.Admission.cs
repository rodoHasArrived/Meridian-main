using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterServiceSnapshotTests
{
    [Theory]
    [InlineData("""[{"callDate":"2028-01-01"}]""", "*exerciseStyle must be explicitly stated*")]
    [InlineData("""[{"callDate":"2028-01-01","exerciseStyle":"European"},{"callDate":"2028-01-01","exerciseStyle":"European"}]""", "*Duplicate callDate*")]
    public async Task AmendWithoutTermsPatch_RevalidatesRetainedScheduleSemanticsBeforeAnyMutation(
        string schedule, string refusal)
    {
        var (service, events, snapshots, store) = CanonicalAdmissionWriter();
        using var _ = service;
        var id = Guid.NewGuid();
        store.GetProjectionAsync(id, Arg.Any<CancellationToken>()).Returns(
            CreateProjection(id, "Bond", SecurityStatusDto.Active, "Retained declared bond", 2) with
            {
                AssetSpecificTerms = Parse(AdmissionBond(schedule))
            });

        var action = () => service.AmendTermsAsync(TermsAmendRequest(id, assetSpecificTermsPatch: null));

        await action.Should().ThrowExactlyAsync<SecurityMasterWriteRefusedException>().WithMessage(refusal);
        await AssertNoCapabilityMutations(events, snapshots, store);
    }

    [Fact]
    public async Task AmendWithoutTermsPatch_PreservesAValidDeclaredSchedule()
    {
        var (service, events, snapshots, store) = CanonicalAdmissionWriter();
        using var _ = service;
        var id = Guid.NewGuid();
        store.GetProjectionAsync(id, Arg.Any<CancellationToken>()).Returns(
            CreateProjection(id, "Bond", SecurityStatusDto.Active, "Retained declared bond", 2) with
            {
                AssetSpecificTerms = Parse(AdmissionBond("""[{"callDate":"2028-01-01","exerciseStyle":"European"}]"""))
            });

        var detail = await service.AmendTermsAsync(TermsAmendRequest(id, assetSpecificTermsPatch: null));

        detail.AssetSpecificTerms.GetProperty("callSchedule")[0].GetProperty("exerciseStyle").GetString()
            .Should().Be("European");
        detail.AssetSpecificTerms.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(3);
        await events.Received(1).AppendAsync(id, 2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>());
        await store.Received(1).UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>());
        await snapshots.Received(1).SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>());
    }

    private static string AdmissionBond(string schedule)
        => Stamped(BondAtVersion1, 3, terms =>
        {
            terms["isCallable"] = true;
            terms["callDate"] = "2028-01-01";
            terms["callSchedule"] = JsonNode.Parse(schedule);
        });

    private static (SecurityMasterService Service, ISecurityMasterEventStore Events,
        ISecurityMasterSnapshotStore Snapshots, ISecurityMasterStore Store) CanonicalAdmissionWriter()
    {
        var events = Substitute.For<ISecurityMasterEventStore>();
        var snapshots = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = CreateDurableProjectionStore();
        var service = new SecurityMasterService(events, snapshots, store,
            new SecurityMasterAggregateRebuilder(events, snapshots),
            new SecurityMasterOptions { SnapshotIntervalVersions = 1 }, NullLogger<SecurityMasterService>.Instance);
        return (service, events, snapshots, store);
    }
}
