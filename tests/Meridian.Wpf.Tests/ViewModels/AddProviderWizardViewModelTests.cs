#if WINDOWS
using System.Windows;
using FluentAssertions;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AddProviderWizardViewModelTests
{
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
}
#endif
