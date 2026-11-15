using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityAssetProfilePromotionReadServiceTests
{
    [Fact]
    public void ApprovedDefinitionFingerprint_RetainsTheCanonicalExportIdentity()
        => SecurityAssetProfilePromotionReadService.Fingerprint(Profile())
            .Should().Be("fb86b8b4c7efb50d84c1f080aa33057005fd48fa71883c59757f4d250278b0a0");

    [Fact]
    public async Task Assessment_CountsExactApprovedVersionsAndSeparatesUnresolvedReferences()
    {
        var profile = Profile();
        var profiles = Profiles(profile);
        var usage = Substitute.For<ISecurityAssetProfileUsageStore>();
        usage.GetProfileUsageSnapshotAsync(Arg.Any<IReadOnlyList<SecurityAssetProfileUsageRequirements>>(), Arg.Any<CancellationToken>()).Returns([
            new(profile.ProfileId, 1, null, 37, 2, 1) { CompleteActiveRecordCount = 31, IncompleteActiveRecordCount = 6,
                UnknownActiveRecordCount = 0, MissingRequiredFieldCounts = new Dictionary<string, long> { ["royaltyRate"] = 6 } },
            new(profile.ProfileId, 2, null, 9, 0, 0),
            new(profile.ProfileId, 1, "Missing or malformed profile fields envelope", 3, 0, 0)
                { CompleteActiveRecordCount = 0, IncompleteActiveRecordCount = 0, UnknownActiveRecordCount = 3 },
            new(null, null, "Missing or malformed profile identity", 4, 0, 0)]);
        var result = await new SecurityAssetProfilePromotionReadService(profiles, usage).GetAssessmentsAsync();

        var observed = result.Profiles.Should().ContainSingle().Which.Usage;
        observed.ActiveRecordCount.Should().Be(40);
        observed.CompleteActiveRecordCount.Should().Be(31);
        observed.IncompleteActiveRecordCount.Should().Be(6);
        observed.UnknownActiveRecordCount.Should().Be(3);
        observed.MissingRequiredFieldCounts!["royaltyRate"].Should().Be(6);
        result.UnresolvedUsage.Should().HaveCount(2);
        result.UnresolvedUsage.Should().Contain(row => row.Version == 2 && row.ActiveRecordCount == 9
            && row.UnresolvedReason!.Contains("not currently approved"));
        result.Scope.Should().Contain("reference catalog").And.Contain("not holdings");
    }

    [Fact]
    public async Task Export_RetainsApprovedDefinitionAndEvidenceWithoutRegisteringAClassOrInferringDefaults()
    {
        var profile = Profile();
        var profiles = Profiles(profile);
        var usage = Substitute.For<ISecurityAssetProfileUsageStore>();
        usage.GetProfileUsageSnapshotAsync(Arg.Any<IReadOnlyList<SecurityAssetProfileUsageRequirements>>(), Arg.Any<CancellationToken>()).Returns([]);
        var before = SecurityAssetClassCatalog.AssetClasses.ToArray();
        var packs = SecurityAssetPackRegistry.All.ToArray();
        var export = await new SecurityAssetProfilePromotionReadService(profiles, usage).ExportCandidateAsync(
            profile.ProfileId, profile.Version, SecurityAssetProfilePromotionReadService.Fingerprint(profile), "trusted-operator");

        export.SourceProfile.Should().BeSameAs(profile);
        export.ExportedBy.Should().Be("trusted-operator");
        export.SourceProfile.ApprovalReference.Should().Be("approval-1");
        export.Candidate.AssetClasses.Should().BeEmpty();
        export.Candidate.PlannedAssetClasses.Should().ContainSingle();
        export.Candidate.AccountingRules.JournalTemplates.Should().BeEmpty();
        export.Candidate.AutomationDepth.Should().Be(AssetPackAutomationDepth.WideCapture);
        export.DescriptorValidation.IsValid.Should().BeFalse("unimplemented lifecycle behavior stays an explicit review task");
        export.DeveloperReviewTasks.Should().Contain(task => task.Contains("no defaults"));
        SecurityAssetClassCatalog.AssetClasses.Should().Equal(before);
        SecurityAssetPackRegistry.All.Should().Equal(packs);
        await profiles.DidNotReceiveWithAnyArgs().DraftProfileAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(SecurityAssetProfileStatusDto.Draft)]
    [InlineData(SecurityAssetProfileStatusDto.Superseded)]
    [InlineData(SecurityAssetProfileStatusDto.Retired)]
    public async Task Export_RefusesVersionsThatAreNotCurrentlyApproved(SecurityAssetProfileStatusDto status)
    {
        var profile = Profile() with { Status = status };
        var usage = Substitute.For<ISecurityAssetProfileUsageStore>();
        usage.GetProfileUsageSnapshotAsync(Arg.Any<IReadOnlyList<SecurityAssetProfileUsageRequirements>>(), Arg.Any<CancellationToken>()).Returns([]);
        var service = new SecurityAssetProfilePromotionReadService(Profiles(profile), usage);
        var export = () => service.ExportCandidateAsync(profile.ProfileId, 1, SecurityAssetProfilePromotionReadService.Fingerprint(profile), "operator");
        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("*currently approved*");
        (await service.GetAssessmentsAsync()).Profiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_RefusesAStaleFingerprintBeforeReadingUsage()
    {
        var profile = Profile();
        var usage = Substitute.For<ISecurityAssetProfileUsageStore>();
        var service = new SecurityAssetProfilePromotionReadService(Profiles(profile), usage);
        var export = () => service.ExportCandidateAsync(profile.ProfileId, 1, "stale", "operator");
        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Refresh*");
        await usage.DidNotReceiveWithAnyArgs().GetProfileUsageSnapshotAsync(default!, default);
    }

    internal static SecurityAssetProfileDefinitionDto Profile() => new(
        "promotion-synthetic", 1, "Synthetic royalty", "Other", null, SecurityAssetProfileStatusDto.Approved,
        [new("royaltyRate", "Royalty rate", SecurityAssetProfileFieldTypeDto.Decimal, true, [], "Declared percent", 0, 100, false, false)],
        [], [], [], [], new DateOnly(2026, 1, 1), null, "independent-reviewer", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        "Synthetic approved definition", "approval-1", "preparer");

    internal static ISecurityAssetProfileGovernanceService Profiles(SecurityAssetProfileDefinitionDto profile)
    {
        var profiles = Substitute.For<ISecurityAssetProfileGovernanceService>();
        profiles.GetAllProfiles().Returns([profile]);
        profiles.GetPromotionCandidates().Returns([]);
        profiles.TryGetProfile(profile.ProfileId, profile.Version, out Arg.Any<SecurityAssetProfileDefinitionDto>())
            .Returns(call => { call[2] = profile; return true; });
        return profiles;
    }
}
