using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderViewModelTests
{
    [Fact]
    public void BuildSelectedProviderTooltips_ShouldExplainMissingSelectionLoadingAndReadyStates()
    {
        var selected = BuildProvider();

        ProviderViewModel.CanUseSelectedProvider(null, isLoading: false).Should().BeFalse();
        ProviderViewModel.CanUseSelectedProvider(selected, isLoading: true).Should().BeFalse();
        ProviderViewModel.CanUseSelectedProvider(selected, isLoading: false).Should().BeTrue();

        ProviderViewModel.BuildSaveSelectedProviderTooltip(null, isLoading: false)
            .Should().Contain("Select a backfill provider");
        ProviderViewModel.BuildSaveSelectedProviderTooltip(selected, isLoading: true)
            .Should().Contain("Wait for provider settings");
        ProviderViewModel.BuildSaveSelectedProviderTooltip(selected, isLoading: false)
            .Should().Contain("Polygon.io");

        ProviderViewModel.BuildResetSelectedProviderTooltip(null, isLoading: false)
            .Should().Contain("Select a backfill provider");
        ProviderViewModel.BuildResetSelectedProviderTooltip(selected, isLoading: false)
            .Should().Contain("Reset Polygon.io");
    }

    [Fact]
    public void BuildBackfillPostureState_ShouldFailClosedUntilProviderCoverageIsEnabled()
    {
        var empty = ProviderViewModel.BuildBackfillPostureState(
            providerCount: 0,
            enabledProviderCount: 0,
            fallbackCount: 0,
            isLoading: false,
            validationText: "",
            hasValidationResult: false);
        var blocked = ProviderViewModel.BuildBackfillPostureState(
            providerCount: 2,
            enabledProviderCount: 0,
            fallbackCount: 0,
            isLoading: false,
            validationText: "Configuration valid.",
            hasValidationResult: true);
        var ready = ProviderViewModel.BuildBackfillPostureState(
            providerCount: 2,
            enabledProviderCount: 1,
            fallbackCount: 1,
            isLoading: false,
            validationText: "Configuration valid.",
            hasValidationResult: true);

        empty.Title.Should().Be("Backfill providers not loaded");
        empty.ActionText.Should().Be("Refresh providers");
        blocked.Title.Should().Be("Backfill provider setup blocked");
        blocked.ActionText.Should().Be("Enable provider");
        ready.Title.Should().Be("Backfill provider setup ready");
        ready.EvidenceText.Should().Be("Configuration valid.");
    }

    [Fact]
    public void BuildInspectors_ShouldExposeSelectedProviderFallbackDryRunAndAuditFacts()
    {
        var providerInspector = ProviderViewModel.BuildProviderSettingsInspector(BuildProvider());
        var actionInspector = ProviderViewModel.BuildProviderActionInspector(
            BuildProvider(),
            isLoading: false,
            saveTooltip: "Save Polygon.io.",
            resetTooltip: "Reset Polygon.io.",
            validationText: "Configuration valid.");
        var fallbackInspector = ProviderViewModel.BuildFallbackInspector(new FallbackChainViewModel
        {
            Priority = "1",
            DisplayName = "Polygon.io",
            DataTypes = "quotes, trades",
            HealthStatus = "healthy",
            RateLimitUsage = "5/100/min",
            ConfigSource = "user"
        });
        var dryRunInspector = ProviderViewModel.BuildDryRunInspector(new DryRunResultViewModel
        {
            Symbol = "AAPL",
            SelectedProvider = "polygon",
            FallbackSequence = "(polygon > yahoo)"
        });
        var auditInspector = ProviderViewModel.BuildAuditInspector(new AuditLogViewModel
        {
            Timestamp = "2026-06-08 12:00:00",
            ProviderId = "polygon",
            Action = "save",
            Summary = "Priority changed"
        });

        providerInspector.Title.Should().Be("Polygon.io");
        providerInspector.Badge!.Value.Should().Be("Enabled");
        providerInspector.Facts.Should().Contain(fact => fact.Label == "Credentials" && fact.Value == "API key required");
        actionInspector.Badge!.Value.Should().Be("Ready");
        actionInspector.Facts.Should().Contain(fact => fact.Label == "Mutation" && fact.Value == "Selected provider only");
        fallbackInspector.Title.Should().Be("Polygon.io");
        fallbackInspector.Facts.Should().Contain(fact => fact.Label == "Rate usage" && fact.Value == "5/100/min");
        dryRunInspector.Title.Should().Be("AAPL");
        dryRunInspector.Facts.Should().Contain(fact => fact.Label == "Mutation" && fact.Value == "None");
        auditInspector.Title.Should().Be("polygon");
        auditInspector.Facts.Should().Contain(fact => fact.Label == "Summary" && fact.Value == "Priority changed");
    }

    private static ProviderSettingsViewModel BuildProvider()
        => new()
        {
            ProviderId = "polygon",
            DisplayName = "Polygon.io",
            IsEnabled = true,
            Priority = 1,
            DataTypes = "quotes, trades",
            ConfigSource = "user",
            HealthStatus = "healthy",
            RequiresApiKey = true,
            FreeTier = true,
            RateLimitPerMinute = "5",
            RateLimitPerHour = "100"
        };
}

public sealed class ProviderSetupStateProjectionTests
{
    [Fact]
    public void Project_ShouldRequireVerificationAfterSavingCredentials()
    {
        var projection = Meridian.Contracts.Configuration.ProviderSetupStateProjection.Project(
            Meridian.Contracts.Configuration.ProviderCredentialStateDto.Configured,
            Meridian.Contracts.Configuration.ProviderVerificationStateDto.NotVerified,
            environment: "paper",
            connectionMode: "Paper",
            bindingEnabled: false);

        projection.State.Should().Be(Meridian.Contracts.Configuration.ProviderSetupStateDto.CredentialsSavedVerificationRequired);
        projection.Explanation.Should().Be("Credentials saved. Verify before using this provider.");
        projection.RoutingDisabledReason.Should().Be("Routing disabled until saved credentials are verified.");
    }

    [Fact]
    public void Project_ShouldKeepLiveProviderDisabledUntilSeparateApproval()
    {
        var projection = Meridian.Contracts.Configuration.ProviderSetupStateProjection.Project(
            Meridian.Contracts.Configuration.ProviderCredentialStateDto.Configured,
            Meridian.Contracts.Configuration.ProviderVerificationStateDto.Verified,
            environment: "live",
            connectionMode: "Live",
            bindingEnabled: true,
            liveApproved: false);

        projection.State.Should().Be(Meridian.Contracts.Configuration.ProviderSetupStateDto.LiveRequiresApproval);
        projection.Explanation.Should().Be("Live endpoint selected. Routing remains disabled until approval.");
        projection.RoutingDisabledReason.Should().Be("Live endpoint selected. Routing remains disabled until approval.");
    }

    [Fact]
    public void Project_ShouldMarkPaperProviderReadyAfterVerificationAndEnabledBinding()
    {
        var projection = Meridian.Contracts.Configuration.ProviderSetupStateProjection.Project(
            Meridian.Contracts.Configuration.ProviderCredentialStateDto.Configured,
            Meridian.Contracts.Configuration.ProviderVerificationStateDto.Verified,
            environment: "paper",
            connectionMode: "Paper",
            bindingEnabled: true);

        projection.State.Should().Be(Meridian.Contracts.Configuration.ProviderSetupStateDto.ReadyPaper);
        projection.Explanation.Should().Be("Provider verified for paper workflows.");
        projection.RoutingDisabledReason.Should().BeNull();
    }
}
