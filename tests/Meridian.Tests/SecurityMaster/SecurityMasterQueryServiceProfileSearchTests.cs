using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterQueryServiceProfileSearchTests
{
    [Fact]
    public async Task SearchAsync_FiltersProfileBackedSecurities_ByProfileAndProjectedField()
    {
        var expectedSecurityId = Guid.NewGuid();
        var expected = CreateProfileBackedProjection(
            expectedSecurityId,
            "Private Fund Alpha",
            "private-fund-interest",
            1,
            new
            {
                gpSponsor = "GP Capital",
                strategy = "Private Credit",
                navDate = "2026-04-30"
            });
        var otherProfile = CreateProfileBackedProjection(
            Guid.NewGuid(),
            "Real Estate Beta",
            "real-estate-holding",
            1,
            new
            {
                sponsor = "GP Capital",
                valuationDate = "2026-04-30"
            });
        var inactive = CreateProfileBackedProjection(
            Guid.NewGuid(),
            "Private Fund Inactive",
            "private-fund-interest",
            1,
            new
            {
                gpSponsor = "GP Capital",
                strategy = "Private Credit",
                navDate = "2026-04-30"
            }) with
        {
            Status = SecurityStatusDto.Inactive
        };

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns([otherProfile, inactive, expected]);

        var service = CreateQueryService(store);

        var results = await service.SearchAsync(new SecuritySearchRequest(
            Query: "private",
            ActiveOnly: true,
            CustomProfileId: "private-fund-interest",
            ProfileVersion: 1,
            ProfileFieldKey: "gpSponsor",
            ProfileFieldValue: "GP Capital"));

        results.Should().ContainSingle();
        results[0].SecurityId.Should().Be(expectedSecurityId);
        results[0].AssetClass.Should().Be("CustomAsset");
        results[0].PrimaryIdentifier.Should().Be("InternalCode:PF-ALPHA");
        _ = store.Received(1).LoadAllAsync(Arg.Any<CancellationToken>());
        _ = store.DidNotReceive().SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_DelegatesToStore_WhenNoProfileCriteriaArePresent()
    {
        var expected = new SecuritySummaryDto(
            Guid.NewGuid(),
            "Equity",
            SecurityStatusDto.Active,
            "Apple Inc.",
            "Ticker:AAPL",
            "USD",
            12);

        var store = Substitute.For<ISecurityMasterStore>();
        store.SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([expected]));

        var service = CreateQueryService(store);

        var results = await service.SearchAsync(new SecuritySearchRequest("AAPL"));

        results.Should().ContainSingle().Which.Should().Be(expected);
        _ = store.Received(1).SearchAsync(
            Arg.Is<SecuritySearchRequest>(request => request.Query == "AAPL"),
            Arg.Any<CancellationToken>());
        _ = store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
    }

    private static SecurityMasterQueryService CreateQueryService(ISecurityMasterStore store)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterQueryService(eventStore, store, rebuilder);
    }

    private static SecurityProjectionRecord CreateProfileBackedProjection(
        Guid securityId,
        string displayName,
        string profileId,
        int profileVersion,
        object profileFields)
        => new(
            SecurityId: securityId,
            AssetClass: "CustomAsset",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "PF-ALPHA",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName,
                currency = "USD",
                lotSize = 1,
                tickSize = 0.01m
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
                customProfileId = profileId,
                profileVersion,
                category = "PrivateFunds",
                profileFields,
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero),
                    approvalReference = "seeded-profile"
                }
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = DateTimeOffset.UtcNow
            }),
            Version: 3,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.InternalCode,
                    "PF-ALPHA",
                    true,
                    DateTimeOffset.UtcNow.AddDays(-1),
                    null,
                    null)
            ],
            Aliases: []);
}
