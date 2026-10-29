using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityScheduleAuthoringParityTests
{
    [Theory]
    [InlineData("[{\"legType\":\"Fixed\",\"currency\":12}]")]
    [InlineData("[12]")]
    [InlineData("[{\"notionalSchedule\":[{\"notional\":\"many\"}]}]")]
    public void WholeContainerEdit_RejectsMalformedRegisteredMembers(string replacement)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", "assetSpecificTerms.legs", replacement, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WholeLegacyLegs_KeepUnsetFutureBlocksTolerant_ButRefusePopulatedUnknownMembers()
    {
        var unsetUnknown = "[{\"legType\":\"Fixed\",\"currency\":\"USD\",\"scheduleConventions\":{\"futureMember\":{}}}]";
        var explicitNull = "[{\"legType\":\"Fixed\",\"currency\":\"USD\",\"scheduleConventions\":null}]";
        var populatedUnknown = "[{\"legType\":\"Fixed\",\"currency\":\"USD\",\"scheduleConventions\":{\"futureMember\":1}}]";
        var introduced = SecurityAssetTermsSchema.ElementFields("Swap", "legs")
            .Single(field => field.Key == "scheduleConventions").SinceTermsVersion;

        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", "assetSpecificTerms.legs", unsetUnknown, out _, out _)
            .Should().Be(SecurityAssetClassCatalog.GetTermsVersion("Swap") < introduced,
                "an unset readable-only container keeps legacy tolerance; its member keys become governed when writable");
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", "assetSpecificTerms.legs", explicitNull, out _, out _).Should().BeTrue();
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", "assetSpecificTerms.legs", populatedUnknown, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void VersionedMembersRequireWholeReplacements_AndClearsKeepOverlaySemantics()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.scheduleConventions.paymentLagBusinessDays", "2", out _, out _).Should().BeFalse();
        // Existing, pre-versioned leg scalar editing retains its established behavior.
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", "assetSpecificTerms.legs.0.fixedRate", "0.05", out _, out _).Should().BeTrue();
        if (SecurityAssetClassCatalog.GetTermsVersion("Bond") >= 2)
            SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.scheduleConventions.paymentLagBusinessDays", null, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void TermBlockReplacement_UsesTheSameRegisteredItemShapes()
    {
        var current = JsonSerializer.SerializeToElement(new { effectiveDate = "2026-01-01", maturityDate = "2030-01-01", legs = new object[] { } });
        Action replace = () => SecurityTermBlocks.BuildPatch("Swap", current, "legs", JsonSerializer.Deserialize<JsonElement>("[false]"));
        replace.Should().Throw<ArgumentException>().WithMessage("*JSON object*");
    }

    [Fact]
    public void IntentionalExerciseScheduleReplacement_RespectsWriteAvailabilityAndPromotesOnlyHeldTerms()
    {
        var current = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            termsFormat = "flat",
            assetClassTermsVersion = 1,
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            isCallable = true,
            callDate = "2027-01-01",
            subclass = "Corporate",
            couponType = "Fixed",
            couponRate = 4m,
            paymentFrequency = "Annual",
            par = 100m,
            dayCount = "30/360"
        });
        var block = JsonSerializer.SerializeToElement(new[] { new { callDate = "2027-01-01", exerciseStyle = "European" } });
        if (SecurityAssetClassCatalog.GetTermsVersion("Bond") < 3)
        {
            Action replace = () => SecurityTermBlocks.BuildPatch("Bond", current, "callSchedule", block);
            replace.Should().Throw<ArgumentException>().WithMessage("*cannot set it yet*");
            return;
        }
        var patch = SecurityTermBlocks.BuildPatch("Bond", current, "callSchedule", block);
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(patch).Should().Be(3);
        patch.GetProperty("callSchedule")[0].GetProperty("callDate").GetString().Should().Be("2027-01-01");
        var cleared = SecurityTermBlocks.BuildPatch("Bond", patch, "callSchedule", JsonSerializer.SerializeToElement<object?>(null));
        cleared.TryGetProperty("callSchedule", out _).Should().BeFalse();
    }
}
