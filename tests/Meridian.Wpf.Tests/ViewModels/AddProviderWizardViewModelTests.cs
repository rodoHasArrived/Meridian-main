#if WINDOWS
using System.Windows;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using ProviderCatalogEntry = Meridian.Ui.Services.Services.ProviderCatalogEntry;
using ProviderCredentialStatus = Meridian.Ui.Services.Services.ProviderCredentialStatus;
using CredentialState = Meridian.Ui.Services.Services.CredentialState;
using ProviderTier = Meridian.Ui.Services.Services.ProviderTier;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AddProviderWizardViewModelTests
{
    [Fact]
    public void LoadProviderCatalog_DefaultsToAllProvidersAndProjectsScope()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new AddProviderWizardViewModel();

            viewModel.LoadProviderCatalog(CreateProviders(), CreateCredentialStatuses());

            viewModel.ProviderCatalog.Should().HaveCount(3);
            viewModel.ProviderCatalog.Select(provider => provider.Id)
                .Should().Equal("alpaca", "polygon", "yahoo");
            viewModel.ProviderCatalogScopeText.Should().Be("3 providers available.");
            viewModel.ProviderCatalogVisibility.Should().Be(Visibility.Visible);
            viewModel.ProviderCatalogEmptyVisibility.Should().Be(Visibility.Collapsed);
            viewModel.ProviderCatalogRecoveryVisibility.Should().Be(Visibility.Collapsed);
            viewModel.IsAllProviderFilterActive.Should().BeTrue();
            viewModel.FindProvider("polygon")?.DisplayName.Should().Be("Polygon");
        });
    }

    [Fact]
    public void SelectProviderFilterCommand_FiltersCatalogAndUpdatesEmptyState()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new AddProviderWizardViewModel();
            viewModel.LoadProviderCatalog(CreateProviders(), CreateCredentialStatuses());

            viewModel.SelectProviderFilterCommand.Execute("streaming");

            viewModel.ProviderCatalog.Should().ContainSingle();
            viewModel.ProviderCatalog[0].DisplayName.Should().Be("Alpaca");
            viewModel.ProviderCatalogScopeText.Should().Be("1 provider matches streaming.");
            viewModel.IsStreamingProviderFilterActive.Should().BeTrue();

            viewModel.SelectProviderFilterCommand.Execute("free");

            viewModel.ProviderCatalog.Select(provider => provider.Id)
                .Should().Equal("alpaca", "yahoo");
            viewModel.ProviderCatalogScopeText.Should().Be("2 providers match free.");

            viewModel.SelectProviderFilterCommand.Execute("not-a-real-filter");

            viewModel.ProviderCatalog.Should().HaveCount(3);
            viewModel.IsAllProviderFilterActive.Should().BeTrue();
        });
    }

    [Fact]
    public void SelectProviderFilterCommand_WhenFilterHasNoMatches_ProjectsRecoveryState()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new AddProviderWizardViewModel();
            viewModel.LoadProviderCatalog(
                new[]
                {
                    CreateProvider("offline-csv", "Offline CSV", ProviderTier.Free, streaming: false, historical: false),
                },
                Array.Empty<ProviderCredentialStatus>());

            viewModel.SelectProviderFilterCommand.Execute("streaming");

            viewModel.ProviderCatalog.Should().BeEmpty();
            viewModel.ProviderCatalogVisibility.Should().Be(Visibility.Collapsed);
            viewModel.ProviderCatalogEmptyVisibility.Should().Be(Visibility.Visible);
            viewModel.ProviderCatalogRecoveryVisibility.Should().Be(Visibility.Visible);
            viewModel.ProviderCatalogEmptyTitle.Should().Be("No streaming providers");
            viewModel.ProviderCatalogEmptyDetail.Should().Contain("All providers");
        });
    }

    [Fact]
    public void ApplyRelationshipSummary_PopulatesOperatorFacingSummaryFields()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new AddProviderWizardViewModel();

            viewModel.ApplyRelationshipSummary(
                providerName: "Northern Trust",
                providerDescription: "Primary custody relationship.",
                connectionType: "Custodian",
                operatingMode: "ReadOnly",
                scopeSummary: "ops | alpha-fund | Account 1234abcd",
                capabilitiesSummary: "Account Positions, Reconciliation Feed",
                credentialSummary: "vault:providers/northern-trust",
                presetSummary: "Multi-Broker Fund Ops",
                certificationSummary: "Not certified");

            viewModel.SelectedProviderName.Should().Be("Northern Trust");
            viewModel.SelectedProviderDescription.Should().Be("Primary custody relationship.");
            viewModel.SummaryConnectionTypeText.Should().Be("Custodian");
            viewModel.SummaryOperatingModeText.Should().Be("ReadOnly");
            viewModel.SummaryScopeText.Should().Be("ops | alpha-fund | Account 1234abcd");
            viewModel.SummaryCapabilitiesText.Should().Contain("Account Positions");
            viewModel.SummaryCredentialText.Should().Be("vault:providers/northern-trust");
            viewModel.SummaryPresetText.Should().Be("Multi-Broker Fund Ops");
            viewModel.SummaryCertificationText.Should().Be("Not certified");
            viewModel.DetailsVisibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void SetCertificationStatus_UpdatesSummaryAndStatusTone()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new AddProviderWizardViewModel();

            viewModel.SetCertificationStatus("Passed all route and credential checks.", success: true);

            viewModel.CertificationStatusText.Should().Be("Passed all route and credential checks.");
            viewModel.SummaryCertificationText.Should().Be("Passed");

            viewModel.SetCertificationStatus("Manual approval still required.", success: false);

            viewModel.CertificationStatusText.Should().Be("Manual approval still required.");
            viewModel.SummaryCertificationText.Should().Be("Needs attention");
        });
    }

    [Fact]
    public void AddProviderWizardPageSource_BindsProviderCatalogAndStatusState()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AddProviderWizardPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AddProviderWizardPage.xaml.cs"));

        xaml.Should().Contain("ItemsSource=\"{Binding ProviderCatalog}\"");
        xaml.Should().Contain("Command=\"{Binding SelectProviderFilterCommand}\"");
        xaml.Should().Contain("AddProviderCatalogScopeText");
        xaml.Should().Contain("AddProviderCatalogEmptyStatePanel");
        xaml.Should().Contain("{Binding ProviderCatalogVisibility}");
        xaml.Should().Contain("{Binding ProviderCatalogEmptyVisibility}");
        xaml.Should().Contain("{Binding ProviderCatalogRecoveryVisibility}");
        xaml.Should().Contain("{Binding CredentialsInfoText}");
        xaml.Should().Contain("{Binding NoCredentialsVisibility}");
        xaml.Should().Contain("{Binding ConnectionTestDotBrush}");
        xaml.Should().Contain("{Binding ConnectionTestStatusText}");
        xaml.Should().Contain("{Binding SaveStatusText}");
        xaml.Should().Contain("{Binding Step1Fill}");
        xaml.Should().Contain("{Binding Step2Fill}");
        xaml.Should().Contain("{Binding Step3Fill}");
        xaml.Should().Contain("{Binding Step4Fill}");
        xaml.Should().NotContain("Click=\"FilterAll_Click\"");
        xaml.Should().NotContain("Click=\"FilterFree_Click\"");
        xaml.Should().NotContain("Click=\"FilterStreaming_Click\"");
        xaml.Should().NotContain("Click=\"FilterHistorical_Click\"");
        codeBehind.Should().Contain("_viewModel.LoadProviderCatalog");
        codeBehind.Should().Contain("_viewModel.FindProvider");
        codeBehind.Should().NotContain("ProviderCatalogList.ItemsSource");
        codeBehind.Should().NotContain("private void ApplyFilter");
    }

    private static ProviderCatalogEntry[] CreateProviders()
    {
        return new[]
        {
            CreateProvider(
                "alpaca",
                "Alpaca",
                ProviderTier.FreeWithAccount,
                true,
                true,
                new CredentialFieldInfo("apiKey", "ALPACA_KEY", "API key", true)),
            CreateProvider("polygon", "Polygon", ProviderTier.LimitedFree, streaming: false, historical: true),
            CreateProvider("yahoo", "Yahoo Finance", ProviderTier.Free, streaming: false, historical: true),
        };
    }

    private static ProviderCatalogEntry CreateProvider(
        string id,
        string name,
        ProviderTier tier,
        bool streaming,
        bool historical,
        params CredentialFieldInfo[] credentialFields)
    {
        return new ProviderCatalogEntry(
            id,
            name,
            tier,
            SupportsStreaming: streaming,
            SupportsHistorical: historical,
            SupportsSymbolSearch: historical,
            Description: $"{name} market data",
            RateLimitPerMinute: 60,
            CredentialFields: credentialFields);
    }

    private static ProviderCredentialStatus[] CreateCredentialStatuses()
    {
        return new[]
        {
            new ProviderCredentialStatus("alpaca", "Alpaca", CredentialState.Configured, "Configured", Array.Empty<string>()),
            new ProviderCredentialStatus("polygon", "Polygon", CredentialState.Missing, "Missing", new[] { "POLYGON_API_KEY" }),
            new ProviderCredentialStatus("yahoo", "Yahoo Finance", CredentialState.NotRequired, "No credentials required", Array.Empty<string>()),
        };
    }
}
#endif
