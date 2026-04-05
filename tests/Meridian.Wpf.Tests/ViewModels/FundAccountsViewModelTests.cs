#if WINDOWS
using FluentAssertions;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FundAccountsViewModelTests
{
    [Fact]
    public void ApplyProviderInsights_FiltersBindingsByScopeAndBuildsPreviewCards()
    {
        var account = new AccountSummaryDto(
            AccountId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            AccountType: AccountTypeDto.Brokerage,
            EntityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            FundId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SleeveId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            VehicleId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            AccountCode: "BRK-001",
            DisplayName: "Prime Brokerage Alpha",
            BaseCurrency: "USD",
            Institution: "Broker A",
            IsActive: true,
            EffectiveFrom: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            PortfolioId: null,
            LedgerReference: null,
            StrategyId: null,
            RunId: null);

        var matchingScope = new ProviderRouteScopeDto
        {
            Workspace = "trading",
            FundProfileId = "alpha-fund",
            EntityId = account.EntityId,
            AccountId = account.AccountId
        };

        var viewModel = new FundAccountsViewModel(
            Substitute.For<IFundAccountService>(),
            Substitute.For<IFundProfileCatalog>(),
            ProviderManagementService.Instance,
            NullLogger<FundAccountsViewModel>.Instance);

        viewModel.ApplyProviderInsights(
            account,
            connections:
            [
                new ProviderConnectionDto(
                    ConnectionId: "broker-a-live",
                    ProviderFamilyId: "broker-a",
                    DisplayName: "Broker A Live",
                    ConnectionType: "Brokerage",
                    ConnectionMode: "Live",
                    Enabled: true,
                    CredentialReference: "vault:broker-a/live",
                    InstitutionId: "broker-a",
                    ExternalAccountId: "PB-001",
                    Scope: matchingScope,
                    Tags: ["relationship-wizard"],
                    Description: "Primary execution relationship.",
                    ProductionReady: true)
            ],
            bindings:
            [
                new ProviderBindingDto(
                    BindingId: "binding-1",
                    Capability: "OrderExecution",
                    ConnectionId: "broker-a-live",
                    Target: matchingScope,
                    Priority: 10,
                    Enabled: true,
                    FailoverConnectionIds: ["broker-a-backup"],
                    SafetyModeOverride: "NoAutomaticFailover",
                    Notes: "Primary execution path."),
                new ProviderBindingDto(
                    BindingId: "binding-2",
                    Capability: "CashTransactions",
                    ConnectionId: "bank-x",
                    Target: new ProviderRouteScopeDto { AccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") },
                    Priority: 10,
                    Enabled: true,
                    FailoverConnectionIds: [],
                    SafetyModeOverride: "NoAutomaticFailover",
                    Notes: "Other account only.")
            ],
            trustSnapshots:
            [
                new ProviderTrustSnapshotDto(
                    ConnectionId: "broker-a-live",
                    ProviderFamilyId: "broker-a",
                    Score: 97,
                    IsHealthy: true,
                    HealthStatus: "Healthy",
                    IsProductionReady: true,
                    IsCertificationFresh: true,
                    Signals: ["certified", "healthy"])
            ],
            previews:
            [
                new RoutePreviewResponse(
                    Capability: "OrderExecution",
                    IsRoutable: true,
                    SelectedConnectionId: "broker-a-live",
                    SelectedProviderFamilyId: "broker-a",
                    SafetyMode: "NoAutomaticFailover",
                    RequiresManualApproval: false,
                    ReasonCodes: ["Matched explicit account binding."],
                    SkippedCandidates: [],
                    FallbackConnectionIds: ["broker-a-backup"],
                    PolicyGate: null,
                    Candidates: [])
            ]);

        viewModel.ProviderBindings.Should().ContainSingle();
        viewModel.ProviderBindings[0].Capability.Should().Be("OrderExecution");
        viewModel.ProviderBindings[0].ConnectionLabel.Should().Be("Broker A Live");
        viewModel.ProviderBindings[0].TrustLabel.Should().Contain("97");
        viewModel.ProviderBindings[0].StatusLabel.Should().Be("Production ready");

        viewModel.RoutePreviews.Should().ContainSingle();
        viewModel.RoutePreviews[0].SelectedConnectionLabel.Should().Be("Broker A Live");
        viewModel.RoutePreviews[0].StatusLabel.Should().Be("Routable");
        viewModel.RoutePreviews[0].FallbackSummary.Should().Be("broker-a-backup");

        viewModel.ProviderRoutingStatus.Should().Be("Loaded 1 binding(s) and 1 route preview(s).");
    }
}
#endif
