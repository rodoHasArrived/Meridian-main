#if WINDOWS
using FluentAssertions;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.RuleEvaluation;
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
    public void SelectedAccountInspectorProperties_SurfaceScopeWorkflowAndSharedDataAccess()
    {
        var account = new AccountSummaryDto(
            AccountId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            AccountType: AccountTypeDto.Brokerage,
            EntityId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            FundId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            SleeveId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            VehicleId: Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            AccountCode: "BRK-002",
            DisplayName: "Operations Brokerage",
            BaseCurrency: "USD",
            Institution: "Broker B",
            IsActive: true,
            EffectiveFrom: new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            PortfolioId: "portfolio-alpha",
            LedgerReference: "ledger-alpha",
            StrategyId: "strategy-alpha",
            RunId: "run-alpha",
            SharedDataAccess: new FundStructureSharedDataAccessDto(
                new SecurityMasterAccessSummaryDto(
                    IsAvailable: true,
                    AvailabilityDescription: "Security master runtime is configured.",
                    InstrumentDefinitionsAccessible: true,
                    EconomicDefinitionsAccessible: true,
                    TradingParametersAccessible: true),
                new HistoricalPriceAccessSummaryDto(
                    IsAvailable: true,
                    HasStoredData: true,
                    AvailableSymbolCount: 12,
                    SampleSymbols: ["AAPL", "MSFT"],
                    AvailabilityDescription: "Historical price cache is available."),
                new BackfillAccessSummaryDto(
                    IsAvailable: true,
                    IsActive: true,
                    ProviderCount: 2,
                    LastProvider: "Alpaca",
                    LastFrom: new DateOnly(2026, 4, 1),
                    LastTo: new DateOnly(2026, 4, 4),
                    LastCompletedUtc: new DateTimeOffset(2026, 4, 4, 18, 30, 0, TimeSpan.Zero),
                    LastRunSucceeded: true,
                    SymbolCheckpointCount: 42,
                    SymbolBarCountCount: 1250,
                    AvailabilityDescription: "Backfill is current.")));

        var viewModel = new FundAccountsViewModel(
            Substitute.For<IFundAccountService>(),
            Substitute.For<IFundProfileCatalog>(),
            ProviderManagementService.Instance,
            NullLogger<FundAccountsViewModel>.Instance)
        {
            SelectedAccount = account
        };

        viewModel.SelectedAccountLifecycleText.Should().Contain("Active");
        viewModel.SelectedAccountScopeText.Should().Contain("Entity");
        viewModel.SelectedAccountWorkflowLinkText.Should().Contain("Portfolio portfolio-alpha");
        viewModel.SelectedAccountSecurityMasterText.Should().Contain("Security Master ready");
        viewModel.SelectedAccountHistoricalPriceText.Should().Contain("Historical prices ready");
        viewModel.SelectedAccountBackfillText.Should().Contain("Backfill available");
    }

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
                    Capability: "AccountBalances",
                    ConnectionId: "broker-a-live",
                    Target: new ProviderRouteScopeDto
                    {
                        Workspace = "research",
                        FundProfileId = "alpha-fund",
                        AccountId = account.AccountId
                    },
                    Priority: 20,
                    Enabled: true,
                    FailoverConnectionIds: [],
                    SafetyModeOverride: "NoAutomaticFailover",
                    Notes: "Other workspace only."),
                new ProviderBindingDto(
                    BindingId: "binding-3",
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
                    Signals: ["certified", "healthy"],
                    Decision: new DecisionResult<double>(
                        Score: 97,
                        Reasons:
                        [
                            new DecisionReason(
                                RuleId: "provider-trust.health-status",
                                Weight: 0,
                                ReasonCode: "HEALTHY",
                                HumanExplanation: "Connection is healthy.")
                        ],
                        Trace: new DecisionTrace(
                            SchemaVersion: "1.0.0",
                            KernelVersion: "test-kernel",
                            EvaluatedAt: DateTimeOffset.UtcNow)))
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
            ],
            workspaceId: "trading",
            fundProfileId: "alpha-fund");

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
