using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.SecurityMaster;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The governance requirements seam: under the Standard profile each requirement follows its own
/// setting; under the Institutional profile (declared, or a production composition) all three are in
/// force and a contradictory configuration refuses startup, or is ignored on a hot reload.
/// </summary>
public sealed class SecurityMasterGovernanceRequirementsTests
{
    private const string Section = SecurityMasterWorkbenchOptions.SectionName;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void StandardProfile_EachRequirementFollowsItsOwnSetting(bool amendments, bool reviewer, bool profileApprover)
    {
        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(
            new SecurityMasterWorkbenchOptions
            {
                RequireGovernedTermAmendments = amendments,
                RequireIndependentReviewer = reviewer,
                RequireIndependentProfileApprover = profileApprover
            },
            isProductionComposition: false,
            // Every requirement key is explicitly configured, false included; only the profile key is absent.
            configuredValue: key => key switch
            {
                SecurityMasterGovernanceRequirements.RequireGovernedTermAmendmentsKey => amendments.ToString(),
                SecurityMasterGovernanceRequirements.RequireIndependentReviewerKey => reviewer.ToString(),
                SecurityMasterGovernanceRequirements.RequireIndependentProfileApproverKey => profileApprover.ToString(),
                _ => null
            });

        snapshot.Profile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        snapshot.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.Default);
        snapshot.RequireGovernedTermAmendments.Should().Be(amendments);
        snapshot.RequireIndependentReviewer.Should().Be(reviewer);
        snapshot.RequireIndependentProfileApprover.Should().Be(profileApprover);
        snapshot.GovernedTermAmendments.Should().Be(Basis(amendments));
        snapshot.IndependentReviewer.Should().Be(Basis(reviewer));
        snapshot.IndependentProfileApprover.Should().Be(Basis(profileApprover));
        snapshot.ContradictedKeys.Should().BeEmpty("an explicit false is an ordinary choice under the Standard profile");

        static SecurityMasterGovernanceRequirementBasis Basis(bool on)
            => on ? SecurityMasterGovernanceRequirementBasis.Setting : SecurityMasterGovernanceRequirementBasis.NotInForce;
    }

    [Fact]
    public void CodeDefaults_AreTheStandardProfileWithEveryRequirementOff()
    {
        var options = new SecurityMasterWorkbenchOptions();

        options.GovernanceProfile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        options.RequireGovernedTermAmendments.Should().BeFalse();
        options.RequireIndependentReviewer.Should().BeFalse();
        options.RequireIndependentProfileApprover.Should().BeFalse();

        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(options, isProductionComposition: false);
        snapshot.Profile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        snapshot.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.Default);
        snapshot.RequireGovernedTermAmendments.Should().BeFalse();
        snapshot.RequireIndependentReviewer.Should().BeFalse();
        snapshot.RequireIndependentProfileApprover.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, SecurityMasterGovernanceProfileSource.Configuration)]
    [InlineData(true, SecurityMasterGovernanceProfileSource.ProductionComposition)]
    public void InstitutionalProfile_PutsAllThreeRequirementsInForce(
        bool byProductionComposition, SecurityMasterGovernanceProfileSource expectedSource)
    {
        var snapshot = GovernanceRequirementsFor.Institutional(byProductionComposition).Current;

        snapshot.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional);
        snapshot.ProfileSource.Should().Be(expectedSource);
        snapshot.RequireGovernedTermAmendments.Should().BeTrue();
        snapshot.RequireIndependentReviewer.Should().BeTrue();
        snapshot.RequireIndependentProfileApprover.Should().BeTrue();
        snapshot.GovernedTermAmendments.Should().Be(SecurityMasterGovernanceRequirementBasis.Profile);
        snapshot.IndependentReviewer.Should().Be(SecurityMasterGovernanceRequirementBasis.Profile);
        snapshot.IndependentProfileApprover.Should().Be(SecurityMasterGovernanceRequirementBasis.Profile);
        snapshot.ContradictedKeys.Should().BeEmpty();
    }

    [Fact]
    public void InstitutionalProfile_WithASettingAlsoOn_ReportsTheProfileAsItsBasis()
    {
        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(
            new SecurityMasterWorkbenchOptions
            {
                GovernanceProfile = SecurityMasterGovernanceProfile.Institutional,
                RequireIndependentReviewer = true
            },
            isProductionComposition: false,
            configuredValue: static key => key switch
            {
                SecurityMasterGovernanceRequirements.GovernanceProfileKey => "Institutional",
                SecurityMasterGovernanceRequirements.RequireIndependentReviewerKey => "true",
                _ => "false"
            });

        snapshot.IndependentReviewer.Should().Be(
            SecurityMasterGovernanceRequirementBasis.Profile, "turning the setting off would not turn the requirement off");
        snapshot.ContradictedKeys.Should().Equal(
            [
                SecurityMasterGovernanceRequirements.RequireGovernedTermAmendmentsKey,
                SecurityMasterGovernanceRequirements.RequireIndependentProfileApproverKey
            ],
            "only the keys explicitly set to false contradict the profile");
    }

    [Theory]
    [InlineData(false, "RequireGovernedTermAmendments")]
    [InlineData(false, "RequireIndependentReviewer")]
    [InlineData(false, "RequireIndependentProfileApprover")]
    [InlineData(true, "RequireGovernedTermAmendments")]
    [InlineData(true, "RequireIndependentReviewer")]
    [InlineData(true, "RequireIndependentProfileApprover")]
    public void InstitutionalProfile_AnExplicitFalse_IsContradictedAndStaysInForce(bool byProductionComposition, string setting)
    {
        var options = new SecurityMasterWorkbenchOptions
        {
            GovernanceProfile = byProductionComposition
                ? SecurityMasterGovernanceProfile.Standard
                : SecurityMasterGovernanceProfile.Institutional,
            RequireGovernedTermAmendments = setting != "RequireGovernedTermAmendments",
            RequireIndependentReviewer = setting != "RequireIndependentReviewer",
            RequireIndependentProfileApprover = setting != "RequireIndependentProfileApprover"
        };

        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(
            options,
            byProductionComposition,
            // The three requirement keys are configured; the profile key is configured only where it says Institutional.
            configuredValue: key => key == SecurityMasterGovernanceRequirements.GovernanceProfileKey
                ? (byProductionComposition ? null : "Institutional")
                : (key == $"{Section}:{setting}" ? "false" : "true"));

        snapshot.RequireGovernedTermAmendments.Should().BeTrue();
        snapshot.RequireIndependentReviewer.Should().BeTrue();
        snapshot.RequireIndependentProfileApprover.Should().BeTrue();
        snapshot.ContradictedKeys.Should().Equal($"{Section}:{setting}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstitutionalProfile_AFalseSettingWithNoKnownConfiguration_StaysInForceAndReportsNothing(bool byProductionComposition)
    {
        // An options value alone cannot show whether false was written or is the default, so with no
        // configuration to consult nothing is called a contradiction. The requirement holds either way.
        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(
            new SecurityMasterWorkbenchOptions
            {
                GovernanceProfile = byProductionComposition
                    ? SecurityMasterGovernanceProfile.Standard
                    : SecurityMasterGovernanceProfile.Institutional,
                RequireGovernedTermAmendments = false,
                RequireIndependentReviewer = false,
                RequireIndependentProfileApprover = false
            },
            byProductionComposition);

        snapshot.RequireGovernedTermAmendments.Should().BeTrue();
        snapshot.RequireIndependentReviewer.Should().BeTrue();
        snapshot.RequireIndependentProfileApprover.Should().BeTrue();
        snapshot.ContradictedKeys.Should().BeEmpty();
    }

    [Fact]
    public void ProductionComposition_WithAnExplicitStandardProfile_IsContradictedAndStaysInstitutional()
    {
        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(
            new SecurityMasterWorkbenchOptions { GovernanceProfile = SecurityMasterGovernanceProfile.Standard },
            isProductionComposition: true,
            configuredValue: static key => key == SecurityMasterGovernanceRequirements.GovernanceProfileKey ? "standard" : null);

        snapshot.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional);
        snapshot.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.ProductionComposition);
        snapshot.RequireGovernedTermAmendments.Should().BeTrue();
        snapshot.ContradictedKeys.Should().Equal(SecurityMasterGovernanceRequirements.GovernanceProfileKey);
    }

    [Fact]
    public void OrdinaryHost_WithAnExplicitStandardProfile_IsNotAContradiction()
    {
        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(
            new SecurityMasterWorkbenchOptions { GovernanceProfile = SecurityMasterGovernanceProfile.Standard },
            isProductionComposition: false,
            configuredValue: static key => key == SecurityMasterGovernanceRequirements.GovernanceProfileKey ? "Standard" : null);

        snapshot.Profile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        snapshot.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.Configuration);
        snapshot.ContradictedKeys.Should().BeEmpty();
    }

    [Fact]
    public void AContradiction_IsWhatConfigurationSays_NotWhatTheOptionsHappenToHold()
    {
        // A host that composes the seam without binding the section holds the default false while its
        // configuration says true. That agrees with the profile and must not be refused as a false.
        var unbound = SecurityMasterGovernanceRequirements.Evaluate(
            new SecurityMasterWorkbenchOptions(),
            isProductionComposition: true,
            configuredValue: static key => key == SecurityMasterGovernanceRequirements.GovernanceProfileKey ? "Institutional" : "true");

        unbound.RequireGovernedTermAmendments.Should().BeTrue();
        unbound.ContradictedKeys.Should().BeEmpty();
    }

    [Fact]
    public void AnUndefinedProfileValue_IsTreatedAsInstitutional()
    {
        var snapshot = SecurityMasterGovernanceRequirements.Evaluate(
            new SecurityMasterWorkbenchOptions { GovernanceProfile = (SecurityMasterGovernanceProfile)7 },
            isProductionComposition: false);

        snapshot.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional, "an unknown value must not select the weaker profile");
        snapshot.RequireGovernedTermAmendments.Should().BeTrue();
    }

    [Fact]
    public void StandardProfile_HotReload_FollowsEachSetting()
    {
        var options = new MutableWorkbenchOptionsMonitor(new SecurityMasterWorkbenchOptions());
        var requirements = GovernanceRequirementsFor.NonProduction(options);
        requirements.Current.RequireGovernedTermAmendments.Should().BeFalse();

        options.Set(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = true, RequireIndependentReviewer = true });
        requirements.Current.RequireGovernedTermAmendments.Should().BeTrue();
        requirements.Current.RequireIndependentReviewer.Should().BeTrue();
        requirements.Current.RequireIndependentProfileApprover.Should().BeFalse();

        options.Set(new SecurityMasterWorkbenchOptions { RequireIndependentProfileApprover = true });
        requirements.Current.RequireGovernedTermAmendments.Should().BeFalse("the Standard profile keeps hot reload in both directions");
        requirements.Current.RequireIndependentReviewer.Should().BeFalse();
        requirements.Current.RequireIndependentProfileApprover.Should().BeTrue();
    }

    [Fact]
    public void StandardProfile_HotReloadToInstitutional_TakesEffectAtTheNextRead()
    {
        var options = new MutableWorkbenchOptionsMonitor(new SecurityMasterWorkbenchOptions());
        var requirements = GovernanceRequirementsFor.NonProduction(options);

        options.Set(new SecurityMasterWorkbenchOptions { GovernanceProfile = SecurityMasterGovernanceProfile.Institutional });

        requirements.Current.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional);
        requirements.Current.RequireIndependentProfileApprover.Should().BeTrue();
    }

    [Fact]
    public void OrdinaryHost_AReloadChangingTheDeclaredProfile_TakesEffectAndIsLogged()
    {
        // The declared profile is configuration, so on a host that is not a production composition a
        // reload can change it in either direction. What a reload cannot do is keep the Institutional
        // profile and switch one of its requirements off. Relaxing to Standard is logged as a warning.
        var options = new MutableWorkbenchOptionsMonitor(new SecurityMasterWorkbenchOptions());
        var log = new RecordingLogger<SecurityMasterGovernanceRequirements>();
        var requirements = GovernanceRequirementsFor.NonProduction(options, log);
        requirements.Current.Profile.Should().Be(SecurityMasterGovernanceProfile.Standard);

        options.Set(new SecurityMasterWorkbenchOptions { GovernanceProfile = SecurityMasterGovernanceProfile.Institutional });
        requirements.Current.RequireGovernedTermAmendments.Should().BeTrue();
        log.Entries.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Information);

        options.Set(new SecurityMasterWorkbenchOptions());
        requirements.Current.Profile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        requirements.Current.RequireGovernedTermAmendments.Should().BeFalse();
        log.Entries.Should().HaveCount(2);
        log.Entries[1].LogLevel.Should().Be(LogLevel.Warning);
        log.Entries[1].Message.Should().Contain("from Institutional to Standard");
    }

    [Theory]
    [InlineData(false, "RequireGovernedTermAmendments")]
    [InlineData(false, "RequireIndependentReviewer")]
    [InlineData(false, "RequireIndependentProfileApprover")]
    [InlineData(true, "RequireGovernedTermAmendments")]
    [InlineData(true, "RequireIndependentReviewer")]
    [InlineData(true, "RequireIndependentProfileApprover")]
    public void InstitutionalProfile_ConfigurationReloadToAnExplicitFalse_KeepsTheRequirementAndWarnsOnce(
        bool byProductionComposition, string setting)
    {
        var configuration = byProductionComposition
            ? Configuration()
            : Configuration(($"{Section}:GovernanceProfile", "Institutional"));
        var log = new RecordingLogger<SecurityMasterGovernanceRequirements>();
        var services = Compose(configuration);
        services.AddSingleton<ILogger<SecurityMasterGovernanceRequirements>>(log);
        if (byProductionComposition)
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        }

        using var provider = services.BuildServiceProvider();
        var requirements = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>();
        requirements.Current.ContradictedKeys.Should().BeEmpty();
        log.Entries.Should().BeEmpty();

        configuration[$"{Section}:{setting}"] = "false";
        configuration.Reload();

        // Logged by the reload itself, before anything reads the requirement again.
        var warning = log.Entries.Should().ContainSingle().Subject;
        warning.LogLevel.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain($"{Section}:{setting}");

        var current = requirements.Current;
        current.RequireGovernedTermAmendments.Should().BeTrue("a contradictory reload never relaxes a requirement");
        current.RequireIndependentReviewer.Should().BeTrue();
        current.RequireIndependentProfileApprover.Should().BeTrue();
        current.ContradictedKeys.Should().Equal($"{Section}:{setting}");
        provider.GetRequiredService<IGovernedTermAmendmentPolicy>().RefusalReason.Should().NotBeNull();
        _ = requirements.Current;
        log.Entries.Should().ContainSingle("the warning is logged once per reload, not once per read");
    }

    [Fact]
    public void ProductionComposition_ConfigurationReloadToAnExplicitStandardProfile_StaysInstitutional()
    {
        var configuration = Configuration();
        var log = new RecordingLogger<SecurityMasterGovernanceRequirements>();
        var services = Compose(configuration);
        services.AddSingleton<ILogger<SecurityMasterGovernanceRequirements>>(log);
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        using var provider = services.BuildServiceProvider();
        var requirements = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>();
        requirements.Current.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional);

        configuration[$"{Section}:GovernanceProfile"] = "Standard";
        configuration.Reload();

        requirements.Current.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional);
        requirements.Current.RequireGovernedTermAmendments.Should().BeTrue();
        requirements.Current.ContradictedKeys.Should().Equal(SecurityMasterGovernanceRequirements.GovernanceProfileKey);
        log.Entries.Should().ContainSingle()
            .Which.Message.Should().Contain(SecurityMasterGovernanceRequirements.GovernanceProfileKey);
    }

    [Fact]
    public void ComposedHost_ConfigurationReload_FollowsTheStandardProfileAndCannotRelaxTheInstitutionalOne()
    {
        var configuration = Configuration();
        using var provider = Compose(configuration).BuildServiceProvider();
        var requirements = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>();
        var policy = provider.GetRequiredService<IGovernedTermAmendmentPolicy>();
        policy.RefusalReason.Should().BeNull();

        configuration[$"{Section}:RequireGovernedTermAmendments"] = "true";
        configuration.Reload();
        policy.RefusalReason.Should().NotBeNull("the Standard profile hot-reloads through the real options monitor");

        configuration[$"{Section}:RequireGovernedTermAmendments"] = "false";
        configuration.Reload();
        policy.RefusalReason.Should().BeNull();
        requirements.Current.ContradictedKeys.Should().BeEmpty("an explicit false is an ordinary choice under the Standard profile");

        configuration[$"{Section}:GovernanceProfile"] = "Institutional";
        configuration.Reload();
        policy.RefusalReason.Should().NotBeNull("the reload declared the Institutional profile, so the explicit false no longer applies");
        requirements.Current.ContradictedKeys.Should().Equal(SecurityMasterGovernanceRequirements.RequireGovernedTermAmendmentsKey);
        requirements.Current.RequireIndependentReviewer.Should().BeTrue();
        requirements.Current.RequireIndependentProfileApprover.Should().BeTrue();
    }

    [Fact]
    public void ComposedHost_AnAbsentKeyIsNotAnExplicitFalse()
    {
        // The binder writes false for an absent key exactly as for an explicit one, so the seam asks
        // the configuration which keys are present. Absent keys never contradict the profile.
        using var provider = Compose(Configuration(($"{Section}:GovernanceProfile", "Institutional"))).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<SecurityMasterWorkbenchOptions>>().CurrentValue;
        var current = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current;

        options.RequireGovernedTermAmendments.Should().BeFalse("the profile forces the requirement; it does not rewrite the setting");
        current.RequireGovernedTermAmendments.Should().BeTrue();
        current.RequireIndependentReviewer.Should().BeTrue();
        current.RequireIndependentProfileApprover.Should().BeTrue();
        current.ContradictedKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData("RequireGovernedTermAmendments")]
    [InlineData("RequireIndependentReviewer")]
    [InlineData("RequireIndependentProfileApprover")]
    public async Task Startup_InstitutionalByOption_WithAnExplicitFalse_IsRefusedNamingTheKey(string setting)
    {
        var services = Compose(Configuration(
            ($"{Section}:GovernanceProfile", "Institutional"),
            ($"{Section}:{setting}", "false")));
        await using var provider = services.BuildServiceProvider();

        var refusal = await StartHostedServices(provider).Should().ThrowAsync<StartupRefusedException>();

        refusal.Which.Message.Should().Be(
            "Security Master governance configuration contradicts the Institutional governance profile, which is in force "
            + $"because '{Section}:GovernanceProfile' is Institutional. '{Section}:{setting}' is explicitly false; remove the key "
            + "or set it to true. Under the Institutional profile governed term amendments, independent field-edit review and "
            + "independent profile approval are always in force and cannot be turned off.");
        provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current.RequireGovernedTermAmendments
            .Should().BeTrue("the requirement is in force whether or not the host got as far as refusing");
    }

    [Theory]
    [InlineData("RequireGovernedTermAmendments")]
    [InlineData("RequireIndependentReviewer")]
    [InlineData("RequireIndependentProfileApprover")]
    public async Task Startup_ProductionComposition_WithAnExplicitFalse_IsRefusedNamingTheKey(string setting)
    {
        var services = Compose(Configuration(($"{Section}:{setting}", "false")));
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        await using var provider = services.BuildServiceProvider();

        var refusal = await StartHostedServices(provider).Should().ThrowAsync<StartupRefusedException>();

        refusal.Which.Message.Should().Be(
            "Security Master governance configuration contradicts the Institutional governance profile, which is in force "
            + $"because this host is a production composition. '{Section}:{setting}' is explicitly false; remove the key "
            + "or set it to true. Under the Institutional profile governed term amendments, independent field-edit review and "
            + "independent profile approval are always in force and cannot be turned off.");
    }

    [Fact]
    public async Task Startup_ProductionComposition_WithAnExplicitStandardProfile_IsRefusedNamingTheKey()
    {
        var services = Compose(Configuration(($"{Section}:GovernanceProfile", "Standard")));
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        await using var provider = services.BuildServiceProvider();

        var refusal = await StartHostedServices(provider).Should().ThrowAsync<StartupRefusedException>();

        refusal.Which.Message.Should().Be(
            "Security Master governance configuration contradicts the Institutional governance profile, which is in force "
            + $"because this host is a production composition. '{Section}:GovernanceProfile' is explicitly Standard, but a "
            + "production composition always runs the Institutional profile; remove the key or set it to Institutional. "
            + "Under the Institutional profile governed term amendments, independent field-edit review and "
            + "independent profile approval are always in force and cannot be turned off.");
    }

    [Fact]
    public async Task Startup_EveryContradictedKeyIsNamedInOneRefusal()
    {
        var services = Compose(Configuration(
            ($"{Section}:GovernanceProfile", "Standard"),
            ($"{Section}:RequireGovernedTermAmendments", "false"),
            ($"{Section}:RequireIndependentReviewer", "true"),
            ($"{Section}:RequireIndependentProfileApprover", "False")));
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        await using var provider = services.BuildServiceProvider();

        var message = (await StartHostedServices(provider).Should().ThrowAsync<StartupRefusedException>()).Which.Message;

        message.Should().Contain($"'{Section}:GovernanceProfile' is explicitly Standard");
        message.Should().Contain($"'{Section}:RequireGovernedTermAmendments' is explicitly false");
        message.Should().Contain($"'{Section}:RequireIndependentProfileApprover' is explicitly false");
        message.Should().NotContain($"'{Section}:RequireIndependentReviewer'", "a requirement set to true agrees with the profile");
    }

    [Theory]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Production")]
    [InlineData("MERIDIAN_MODE", "Live")]
    [InlineData("MERIDIAN_API_DEPLOYMENT_MODE", "ProductionApi")]
    public async Task Startup_ProductionByEnvironment_IsInstitutionalAndRefusesAnExplicitFalse(string variable, string value)
    {
        var services = Compose(Configuration(
            (variable, value),
            ($"{Section}:RequireIndependentProfileApprover", "false")));
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current.ProfileSource
            .Should().Be(SecurityMasterGovernanceProfileSource.ProductionComposition);
        (await StartHostedServices(provider).Should().ThrowAsync<StartupRefusedException>())
            .Which.Message.Should().Contain($"'{Section}:RequireIndependentProfileApprover' is explicitly false");
    }

    [Fact]
    public async Task Startup_APostureDeclaredAfterRegistration_IsStillHonoured()
    {
        // The production fact is decided from the final collection, as the ADR-019 final-graph guard
        // decides it, so declaring the posture late cannot leave the Standard profile in force.
        var services = Compose(Configuration(($"{Section}:RequireGovernedTermAmendments", "false")));
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IGovernedTermAmendmentPolicy>().RefusalReason.Should().NotBeNull();
        await StartHostedServices(provider).Should().ThrowAsync<StartupRefusedException>();
    }

    [Fact]
    public async Task Startup_GenericHost_FailsToStartOnAContradiction()
    {
        using var host = new Microsoft.Extensions.Hosting.HostBuilder()
            .ConfigureServices(services =>
            {
                Compose(
                    Configuration(
                        ($"{Section}:GovernanceProfile", "Institutional"),
                        ($"{Section}:RequireIndependentReviewer", "false")),
                    services);
            })
            .Build();

        (await host.Invoking(h => h.StartAsync()).Should().ThrowAsync<StartupRefusedException>())
            .Which.Message.Should().Contain($"'{Section}:RequireIndependentReviewer' is explicitly false");
    }

    [Fact]
    public async Task Preflight_RefusesAContradictionAheadOfAShell()
    {
        var services = Compose(Configuration(
            ($"{Section}:GovernanceProfile", "Institutional"),
            ($"{Section}:RequireGovernedTermAmendments", "false")));
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider);

        (await preflight.Should().ThrowAsync<StartupRefusedException>())
            .Which.Message.Should().Contain($"'{Section}:RequireGovernedTermAmendments' is explicitly false");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "false")]
    [InlineData("Standard", "false")]
    [InlineData("Institutional", null)]
    [InlineData("Institutional", "true")]
    [InlineData("institutional", "TRUE")]
    public async Task Startup_ConsistentConfiguration_Starts(string? profile, string? requireAmendments)
    {
        var services = Compose(Configuration(
            ($"{Section}:GovernanceProfile", profile),
            ($"{Section}:RequireGovernedTermAmendments", requireAmendments)));
        await using var provider = services.BuildServiceProvider();

        await StartHostedServices(provider).Should().NotThrowAsync();
        await StartHostedServices(provider).Should().NotThrowAsync("a refusal guard must be safe to run twice");
    }

    [Fact]
    public async Task Startup_ProductionComposition_WithNothingConfigured_StartsUnderTheInstitutionalProfile()
    {
        var services = Compose(Configuration());
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        await using var provider = services.BuildServiceProvider();

        await StartHostedServices(provider).Should().NotThrowAsync();
        var current = provider.GetRequiredService<ISecurityMasterGovernanceRequirements>().Current;
        current.Profile.Should().Be(SecurityMasterGovernanceProfile.Institutional);
        current.RequireGovernedTermAmendments.Should().BeTrue();
        current.RequireIndependentReviewer.Should().BeTrue();
        current.RequireIndependentProfileApprover.Should().BeTrue();
        provider.GetRequiredService<IGovernedTermAmendmentPolicy>().RefusalReason.Should().NotBeNull();
    }

    [Fact]
    public async Task Startup_AnUnknownProfileValue_IsRefusedNamingTheKey()
    {
        var services = Compose(Configuration(($"{Section}:GovernanceProfile", "Strict")));
        await using var provider = services.BuildServiceProvider();

        var refusal = await StartHostedServices(provider).Should().ThrowAsync<StartupRefusedException>();

        refusal.Which.Message.Should().Contain($"'{Section}:GovernanceProfile' accepts Standard or Institutional");
        refusal.Which.InnerException.Should().NotBeNull("the binder's own failure is kept as the cause");
    }

    [Fact]
    public void SampleConfiguration_IsAStandardProfile_ThatAProductionCompositionWouldRefuse()
    {
        // The sample keeps RequireGovernedTermAmendments explicitly false, which suits the
        // single-operator workstation it is written for. Copied unchanged onto a production posture it
        // is a contradiction, so that host refuses to start naming the key.
        var sample = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryRoot(), "config", "appsettings.sample.json"), optional: false)
            .Build();
        var options = new SecurityMasterWorkbenchOptions();
        sample.GetSection(Section).Bind(options);

        var workstation = SecurityMasterGovernanceRequirements.Evaluate(
            options, isProductionComposition: false, key => sample[key]);
        workstation.Profile.Should().Be(SecurityMasterGovernanceProfile.Standard);
        workstation.ProfileSource.Should().Be(SecurityMasterGovernanceProfileSource.Default, "the sample only documents the key");
        workstation.IndependentReviewer.Should().Be(SecurityMasterGovernanceRequirementBasis.Setting);
        workstation.RequireGovernedTermAmendments.Should().BeFalse();
        workstation.RequireIndependentProfileApprover.Should().BeFalse();
        workstation.ContradictedKeys.Should().BeEmpty();

        var production = SecurityMasterGovernanceRequirements.Evaluate(
            options, isProductionComposition: true, key => sample[key]);
        production.RequireGovernedTermAmendments.Should().BeTrue();
        production.ContradictedKeys.Should().Equal(SecurityMasterGovernanceRequirements.RequireGovernedTermAmendmentsKey);
    }

    [Fact]
    public void Registration_IsIdempotent_AndSharesOneGuardAcrossBothRoles()
    {
        var services = Compose(Configuration());
        services.AddSecurityMasterGovernanceRequirements();
        using var provider = services.BuildServiceProvider();

        services.Count(static d => d.ServiceType == typeof(SecurityMasterGovernanceProfileGuard)).Should().Be(1);
        services.Count(static d => d.ServiceType == typeof(ISecurityMasterGovernanceRequirements)).Should().Be(1);
        var guard = provider.GetRequiredService<SecurityMasterGovernanceProfileGuard>();
        provider.GetServices<IStartupRefusalGuard>().Should().ContainSingle().Which.Should().BeSameAs(guard);
        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeSameAs(guard);
    }

    private static string RepositoryRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName ?? throw new InvalidOperationException("Meridian.sln was not found above the test directory.");
    }

    private static Func<Task> StartHostedServices(IServiceProvider provider)
        => async () =>
        {
            foreach (var hosted in provider.GetServices<IHostedService>())
            {
                await hosted.StartAsync(CancellationToken.None);
            }
        };

    private static IConfigurationRoot Configuration(params (string Key, string? Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings
                .Where(static setting => setting.Value is not null)
                .Select(static setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    /// <summary>
    /// The registrations a host makes: the section bound from configuration and the seam. Posture
    /// variables are read from the same explicit configuration, never the process environment, so
    /// these tests neither depend on nor change the machine they run on.
    /// </summary>
    private static IServiceCollection Compose(IConfigurationRoot configuration, IServiceCollection? services = null)
    {
        services ??= new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new CompositionConfiguration(configuration));
        services.AddLogging();
        services.AddOptions<SecurityMasterWorkbenchOptions>().BindConfiguration(Section);
        services.AddSecurityMasterGovernanceRequirements();
        return services;
    }
}
