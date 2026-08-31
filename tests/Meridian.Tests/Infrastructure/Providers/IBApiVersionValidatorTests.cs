using FluentAssertions;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class IBApiVersionValidatorTests
{
    [Fact]
    public void ValidateServerVersion_BelowMinimum_ThrowsMismatchExceptionWithSetupGuidance()
    {
        var act = () => IBApiVersionValidator.ValidateServerVersion(
            serverVersion: IBApiVersionValidator.MinSupportedServerVersion - 1,
            clientVersion: IBApiVersionValidator.MinSupportedClientVersion);

        act.Should().Throw<IBApiVersionMismatchException>()
            .WithMessage("*provider-onboarding-interactive-brokers.md*")
            .WithMessage($"*{IBApiVersionValidator.MinSupportedServerVersion}*");
    }

    [Fact]
    public void ValidateServerVersion_AtMinimum_DoesNotThrow()
    {
        var act = () => IBApiVersionValidator.ValidateServerVersion(
            serverVersion: IBApiVersionValidator.MinSupportedServerVersion,
            clientVersion: IBApiVersionValidator.MinSupportedClientVersion);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateServerVersion_AboveMaxTested_AllowsBoundedForwardCompatibility()
    {
        var act = () => IBApiVersionValidator.ValidateServerVersion(
            serverVersion: IBApiVersionValidator.MaxTestedServerVersion + 1,
            clientVersion: IBApiVersionValidator.MinSupportedClientVersion);

        act.Should().NotThrow(
            "Wave 1 keeps higher server versions explicitly bounded rather than blocking startup outright");
    }

    [Fact]
    public void BuildVersionRequirementsMessage_ReferencesSetupGuideAndBounds()
    {
        var message = IBApiVersionValidator.BuildVersionRequirementsMessage();

        message.Should().Contain("provider-onboarding-interactive-brokers.md");
        message.Should().Contain(IBApiVersionValidator.MinSupportedServerVersion.ToString());
        message.Should().Contain(IBApiVersionValidator.MaxTestedServerVersion.ToString());
    }

    [Fact]
    public void CompatibilityBounds_DescribeTheOfficialSdkReleaseEvidence()
    {
        IBApiVersionValidator.MinSupportedClientVersion.Should().Be(178);
        IBApiVersionValidator.BuildVersionRequirementsMessage().Should().Contain("TWS 10.19");
    }
}
