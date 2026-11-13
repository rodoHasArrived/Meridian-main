using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The read-only promotion report: CustomAsset records pinned to a reclassifying profile, each
/// dry-run under the profile's target class.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CustomAssetPromotionReportTests
{
    private static (SecurityMasterProfilePromotionService Service, ISecurityMasterStore Store) BuildSut()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        var service = new SecurityMasterProfilePromotionService(
            store,
            NullLogger<SecurityMasterProfilePromotionService>.Instance,
            StaticSecurityAssetProfileCatalog.CreateDefault());
        return (service, store);
    }

    [Fact]
    public async Task BuildReportAsync_ClassifiesPinnedCustomAssets_AndCountsAlreadyPromotedRecords()
    {
        var (sut, store) = BuildSut();
        var ready = CustomAssetPromotionRoundTripTests.CustomAssetRecord(Guid.NewGuid(), CustomAssetPromotionRoundTripTests.PrivateFundInterestEnvelope(), 2);
        var blocked = CustomAssetPromotionRoundTripTests.CustomAssetRecord(Guid.NewGuid(), WithoutField(CustomAssetPromotionRoundTripTests.PrivateFundInterestEnvelope(), "gpSponsor"), 3);
        var promotedSource = CustomAssetPromotionRoundTripTests.CustomAssetRecord(Guid.NewGuid(), CustomAssetPromotionRoundTripTests.PrivateFundInterestEnvelope(), 5);
        var promoted = promotedSource with { AssetClass = "PrivateFundInterest" };
        var otherProfile = CustomAssetPromotionRoundTripTests.CustomAssetRecord(Guid.NewGuid(), CustomAssetPromotionRoundTripTests.CoInvestSpvEnvelope(), 1);
        var inactive = ready with { SecurityId = Guid.NewGuid(), Status = SecurityStatusDto.Inactive };
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { blocked, ready, promoted, otherProfile, inactive });

        var report = await sut.BuildReportAsync("private-fund-interest");

        report.ProfileId.Should().Be("private-fund-interest");
        report.TargetAssetClass.Should().Be("PrivateFundInterest");
        report.SecuritiesScanned.Should().Be(4, "inactive records are not scanned");
        report.AlreadyPromotedCount.Should().Be(1);
        report.CandidateCount.Should().Be(2, "a record pinned to another profile is not a candidate");
        report.ReadyCount.Should().Be(1);
        report.BlockedCount.Should().Be(1);

        report.Candidates.Select(static candidate => candidate.SecurityId).Should().Equal(
            new[] { ready.SecurityId, blocked.SecurityId }, "ready candidates are listed first");
        var readyCandidate = report.Candidates[0];
        readyCandidate.Disposition.Should().Be(SecurityMasterProfilePromotionDisposition.Ready);
        readyCandidate.SourceAssetClass.Should().Be("CustomAsset");
        readyCandidate.TargetAssetClass.Should().Be("PrivateFundInterest");
        readyCandidate.ProfileVersion.Should().Be(1);
        readyCandidate.Version.Should().Be(2);
        readyCandidate.Blockers.Should().BeEmpty();

        var blockedCandidate = report.Candidates[1];
        blockedCandidate.Disposition.Should().Be(SecurityMasterProfilePromotionDisposition.Blocked);
        blockedCandidate.Blockers.Should().Contain(
            static blocker => blocker.Code == SecurityMasterProfilePromotionService.TargetDecodeFailed
                && blocker.Message.Contains("gpSponsor"),
            "a fund interest without its GP sponsor does not decode as PrivateFundInterest");
        blockedCandidate.Blockers.Should().NotContain(
            static blocker => blocker.Code == SecurityMasterProfilePromotionService.SourceTermsUnreadable,
            "the stored CustomAsset envelope itself still reads");
    }

    [Fact]
    public async Task BuildReportAsync_BlocksStoredTermsThatDoNotReadAsCustomAsset()
    {
        var (sut, store) = BuildSut();
        var node = JsonNode.Parse(CustomAssetPromotionRoundTripTests.PrivateFundInterestEnvelope().GetRawText())!.AsObject();
        // A terms version this node does not read: the stored row is refused before it is decoded.
        node[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 99;
        var unreadable = CustomAssetPromotionRoundTripTests.CustomAssetRecord(Guid.NewGuid(), JsonSerializer.SerializeToElement(node), 1);
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { unreadable });

        var report = await sut.BuildReportAsync("private-fund-interest");

        var candidate = report.Candidates.Should().ContainSingle().Subject;
        candidate.Disposition.Should().Be(SecurityMasterProfilePromotionDisposition.Blocked);
        candidate.Blockers.Should().Contain(static blocker => blocker.Code == SecurityMasterProfilePromotionService.SourceTermsUnreadable);
    }

    [Fact]
    public async Task BuildReportAsync_BlocksAContradictingClassification()
    {
        var (sut, store) = BuildSut();
        var node = JsonNode.Parse(CustomAssetPromotionRoundTripTests.PrivateFundInterestEnvelope().GetRawText())!.AsObject();
        node["category"] = "RealEstate";
        var contradicting = CustomAssetPromotionRoundTripTests.CustomAssetRecord(Guid.NewGuid(), JsonSerializer.SerializeToElement(node), 1);
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { contradicting });

        var report = await sut.BuildReportAsync("private-fund-interest");

        report.Candidates.Should().ContainSingle().Which.Blockers
            .Should().Contain(static blocker => blocker.Code == SecurityMasterProfilePromotionService.ClassificationConflict);
    }

    [Theory]
    [InlineData("co-invest-spv")]
    [InlineData("not-a-profile")]
    [InlineData(" ")]
    public async Task BuildReportAsync_RefusesAProfileThatDoesNotReclassify(string profileId)
    {
        var (sut, store) = BuildSut();

        var act = () => sut.BuildReportAsync(profileId);

        await act.Should().ThrowAsync<ArgumentException>();
        await store.DidNotReceiveWithAnyArgs().LoadActiveAsync(default);
    }

    private static JsonElement WithoutField(JsonElement envelope, string fieldKey)
    {
        var node = JsonNode.Parse(envelope.GetRawText())!.AsObject();
        node["profileFields"]!.AsObject().Remove(fieldKey);
        return JsonSerializer.SerializeToElement(node);
    }
}
