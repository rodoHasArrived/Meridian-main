using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Ui;

/// <summary>
/// Phase 4 configuration binding: the workbench source-precedence ladder (and the rest of the options)
/// must bind from the <c>SecurityMasterWorkbench</c> configuration section so the conflict-authority
/// policy ranks the deployment's real source systems rather than the empty default.
/// </summary>
public sealed class SecurityMasterWorkbenchOptionsBindingTests
{
    [Fact]
    public void AddWorkstationSharedServices_BindsWorkbenchOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecurityMasterWorkbench:SourcePrecedence:0"] = "golden-copy",
                ["SecurityMasterWorkbench:SourcePrecedence:1"] = "vendor-a",
                ["SecurityMasterWorkbench:SourcePrecedence:2"] = "vendor-b",
                ["SecurityMasterWorkbench:GoldenCopySource"] = "golden-copy",
                ["SecurityMasterWorkbench:MaxBulkResolveBatch"] = "50",
                ["SecurityMasterWorkbench:RequireIndependentReviewer"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<SecurityMasterWorkbenchOptions>>().CurrentValue;

        options.SourcePrecedence.Should().Equal("golden-copy", "vendor-a", "vendor-b");
        options.GoldenCopySource.Should().Be("golden-copy");
        options.MaxBulkResolveBatch.Should().Be(50);
        options.RequireIndependentReviewer.Should().BeFalse();
    }

    [Fact]
    public void AddWorkstationSharedServices_WithNoSection_LeavesSourcePrecedenceEmpty()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<SecurityMasterWorkbenchOptions>>().CurrentValue;

        // No configured ladder ⇒ the policy applies no presumptive ranking (falls through to freshness
        // then confidence) rather than ranking against a placeholder.
        options.SourcePrecedence.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Institutional", SecurityMasterGovernanceProfile.Institutional)]
    [InlineData("institutional", SecurityMasterGovernanceProfile.Institutional)]
    [InlineData("Standard", SecurityMasterGovernanceProfile.Standard)]
    public void AddWorkstationSharedServices_BindsGovernanceProfileFromConfiguration(
        string configured, SecurityMasterGovernanceProfile expected)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SecurityMasterWorkbench:GovernanceProfile"] = configured,
        });

        var options = provider.GetRequiredService<IOptionsMonitor<SecurityMasterWorkbenchOptions>>().CurrentValue;
        var effective = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current;

        options.GovernanceProfile.Should().Be(expected);
        effective.Profile.Should().Be(expected);
        effective.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.Configuration);
        var institutional = expected == SecurityMasterGovernanceProfile.Institutional;
        effective.RequireGovernedTermAmendments.Should().Be(institutional);
        effective.RequireIndependentReviewer.Should().Be(institutional);
        effective.RequireIndependentProfileApprover.Should().Be(institutional);
        options.RequireGovernedTermAmendments.Should().BeFalse("the profile forces the requirement; it does not rewrite the setting");
    }

    [Fact]
    public void AddWorkstationSharedServices_WithNoSection_IsTheStandardProfileWithEveryRequirementOff()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptionsMonitor<SecurityMasterWorkbenchOptions>>().CurrentValue;
        var effective = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current;

        options.GovernanceProfile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        effective.Profile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        effective.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.Default);
        effective.RequireGovernedTermAmendments.Should().BeFalse();
        effective.RequireIndependentReviewer.Should().BeFalse();
        effective.RequireIndependentProfileApprover.Should().BeFalse();
        provider.GetRequiredService<IGovernedTermAmendmentPolicy>().RefusalReason.Should().BeNull();
    }

    [Fact]
    public void AddWorkstationSharedServices_BindsEachRequirementAndTheSeamFollowsIt()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SecurityMasterWorkbench:RequireGovernedTermAmendments"] = "true",
            ["SecurityMasterWorkbench:RequireIndependentProfileApprover"] = "true",
            ["SecurityMasterWorkbench:RequireIndependentReviewer"] = "false",
        });

        var effective = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current;

        effective.GovernedTermAmendments.Should().Be(SecurityMasterGovernanceRequirementBasis.Setting);
        effective.IndependentProfileApprover.Should().Be(SecurityMasterGovernanceRequirementBasis.Setting);
        effective.IndependentReviewer.Should().Be(SecurityMasterGovernanceRequirementBasis.NotInForce);
        effective.ContradictedKeys.Should().BeEmpty();
        provider.GetRequiredService<IGovernedTermAmendmentPolicy>().RefusalReason.Should().NotBeNull();
    }

    [Fact]
    public void AddWorkstationSharedServices_ProductionComposition_IsInstitutionalWithNothingConfigured()
    {
        // Declared after the workstation registrations: a production workstation composition needs
        // its durable stores to register at all, and the seam decides the posture from the final
        // collection, so the registration order does not matter to it.
        using var provider = BuildProvider(
            [], services => services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi));

        var effective = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current;

        effective.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional);
        effective.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.ProductionComposition);
        effective.GovernedTermAmendments.Should().Be(SecurityMasterGovernanceRequirementBasis.Profile);
        effective.IndependentReviewer.Should().Be(SecurityMasterGovernanceRequirementBasis.Profile);
        effective.IndependentProfileApprover.Should().Be(SecurityMasterGovernanceRequirementBasis.Profile);
        provider.GetRequiredService<IGovernedTermAmendmentPolicy>().RefusalReason.Should().NotBeNull();
    }

    /// <summary>
    /// The workstation registrations over explicit configuration. Posture variables are read from
    /// that configuration, not the process environment, so the result does not depend on the machine.
    /// </summary>
    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new CompositionConfiguration(configuration));
        services.AddWorkstationSharedServices();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
