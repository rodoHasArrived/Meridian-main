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
            .WithMessage("*interactive-brokers-setup.md*")
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

        message.Should().Contain("interactive-brokers-setup.md");
        message.Should().Contain(IBApiVersionValidator.MinSupportedServerVersion.ToString());
        message.Should().Contain(IBApiVersionValidator.MaxTestedServerVersion.ToString());
    }
}
