using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Composition.Features;
using Meridian.Core.Config;

namespace Meridian.Tests.Application.Composition;

public sealed class BackfillFeatureRegistrationTests
{
    [Fact]
    public void CreateAutoGapRemediationPolicy_MapsConfiguredProviderAndLimits()
    {
        var config = new AutoGapRemediationConfig(
            MinimumGapDurationSeconds: 30,
            MinimumGapSize: 4,
            SymbolCooldownSeconds: 90,
            ProviderCooldownSeconds: 15,
            MaxConcurrentRemediations: 3,
            DefaultProvider: " polygon ");

        var policy = BackfillFeatureRegistration.CreateAutoGapRemediationPolicy(config);

        policy.MinimumGapDuration.Should().Be(TimeSpan.FromSeconds(30));
        policy.MinimumGapSize.Should().Be(4);
        policy.SymbolCooldown.Should().Be(TimeSpan.FromSeconds(90));
        policy.ProviderCooldown.Should().Be(TimeSpan.FromSeconds(15));
        policy.MaxConcurrentRemediations.Should().Be(3);
        policy.DefaultProvider.Should().Be("polygon");
    }

    [Fact]
    public void CreateAutoGapRemediationPolicy_InvalidBounds_FailsSafeToMinimumsAndDefaultProvider()
    {
        var config = new AutoGapRemediationConfig(
            MinimumGapDurationSeconds: -1,
            MinimumGapSize: 0,
            SymbolCooldownSeconds: -1,
            ProviderCooldownSeconds: -1,
            MaxConcurrentRemediations: 0,
            DefaultProvider: " ");

        var policy = BackfillFeatureRegistration.CreateAutoGapRemediationPolicy(config);

        policy.MinimumGapDuration.Should().Be(TimeSpan.Zero);
        policy.MinimumGapSize.Should().Be(1);
        policy.SymbolCooldown.Should().Be(TimeSpan.Zero);
        policy.ProviderCooldown.Should().Be(TimeSpan.Zero);
        policy.MaxConcurrentRemediations.Should().Be(1);
        policy.DefaultProvider.Should().Be(AutoGapRemediationPolicy.Default.DefaultProvider);
    }
}
