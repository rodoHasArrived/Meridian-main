using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityAssetProfileUsagePostgresTests(SecurityMasterDatabaseFixture fixture) : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task Usage_CountsSecurityEnvelopesIncludingZeroFieldsAndSeparatesMalformedReferences()
    {
        var store = new PostgresSecurityMasterStore(fixture.Options);
        var profileId = $"usage-{Guid.NewGuid():N}";
        async Task Save(string json, SecurityStatusDto status = SecurityStatusDto.Active, string assetClass = "CustomAsset")
            => await store.UpsertProjectionAsync((CustomAssetProfileFieldProjectionTests.Record(assetClass, json, Guid.NewGuid(), 1) with { Status = status }).WithDeclaredTermsFormat());
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{"memo":"private-record-value","x":42}}""", SecurityStatusDto.Inactive);
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":2,"profileFields":{}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":2147483648,"profileFields":{}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":"1","profileFields":{}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{}}""", assetClass: "OtherSecurity");

        var counts = (await store.GetProfileUsageCountsAsync()).Where(row => row.ProfileId == profileId).ToArray();

        counts.Single(row => row.ProfileVersion == 1 && row.EnvelopeIssue is null).Should().BeEquivalentTo(
            new { ProfileId = profileId, ProfileVersion = 1, ActiveRecordCount = 1L, InactiveRecordCount = 1L, UnknownStatusRecordCount = 0L });
        counts.Single(row => row.ProfileVersion == 2).ActiveRecordCount.Should().Be(1);
        counts.Single(row => row.ProfileVersion is null).ActiveRecordCount.Should().Be(2);
        counts.Single(row => row.EnvelopeIssue == "Missing or malformed profile fields envelope").ActiveRecordCount.Should().Be(1);
        counts.Sum(static row => row.ActiveRecordCount + row.InactiveRecordCount).Should().Be(6);
    }

    [SecurityMasterDatabaseFact]
    public async Task Usage_PresenceUsesApprovedRequiredKeysInTheSamePopulationWithoutDoubleCountingMissingFields()
    {
        var store = new PostgresSecurityMasterStore(fixture.Options);
        var profileId = $"required-{Guid.NewGuid():N}";
        var zeroId = $"zero-{Guid.NewGuid():N}";
        async Task Save(string json, SecurityStatusDto status = SecurityStatusDto.Active)
            => await store.UpsertProjectionAsync((CustomAssetProfileFieldProjectionTests.Record("CustomAsset", json, Guid.NewGuid(), 1) with { Status = status }).WithDeclaredTermsFormat());
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{"amount":0,"memo":"present"}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{"amount":null,"memo":"\t\u00a0"}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{"amount":"wrong-type","memo":"present"}}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1}""");
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{}}""", SecurityStatusDto.Inactive);
        await Save($$$"""{"customProfileId":"{{{profileId}}}","profileVersion":2,"profileFields":{}}""");
        await Save($$$"""{"customProfileId":"{{{zeroId}}}","profileVersion":1,"profileFields":{}}""");

        var result = await store.GetProfileUsageSnapshotAsync([
            new(profileId, 1, ["amount", "memo"]), new(zeroId, 1, [])]);
        var counts = result.Where(row => row.ProfileId == profileId && row.ProfileVersion == 1).ToArray();

        counts.Sum(static row => row.ActiveRecordCount).Should().Be(5);
        counts.Sum(static row => row.CompleteActiveRecordCount).Should().Be(2, "zero is present, and presence alone is not type validation");
        counts.Sum(static row => row.IncompleteActiveRecordCount).Should().Be(2, "a record missing two fields is one incomplete record");
        counts.Sum(static row => row.UnknownActiveRecordCount).Should().Be(1);
        counts.Sum(static row => row.MissingRequiredFieldCounts!.GetValueOrDefault("amount")).Should().Be(2);
        counts.Sum(static row => row.MissingRequiredFieldCounts!.GetValueOrDefault("memo")).Should().Be(2);
        result.Single(row => row.ProfileId == zeroId).CompleteActiveRecordCount.Should().Be(1);
        result.Single(row => row.ProfileId == profileId && row.ProfileVersion == 2).UnknownActiveRecordCount.Should().Be(1);
    }
}
