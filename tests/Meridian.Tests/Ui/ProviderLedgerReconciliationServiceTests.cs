using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Accounts;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.ProviderSdk;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ProviderLedgerReconciliationServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_MatchesProviderProjectionToInternalSnapshot()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.Summary.BreakCount.Should().Be(0);
            detail.Checks.Should().Contain(check => check.CheckId == "cash-balance" && check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check => check.CheckId == "securities-market-value" && check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check => check.CheckId == "security-master:AAPL" && check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            var passport = detail.SecurityMasterPassports.Should().NotBeNull().And.ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.ProviderId.Should().Be("alpaca");
            passport.ExternalAccountId.Should().Be("PA-LEDGER");
            passport.SecurityId.Should().Be(Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D"));
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Resolved);
            passport.ConfidenceScore.Should().Be(90m);
            passport.ResolutionSource.Should().Be("security-master-lookup");
            passport.ValidationIssueCodes.Should().BeEmpty();
            File.Exists(detail.Summary.DetailPath).Should().BeTrue("latest reconciliation detail must be retained as evidence");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_MatchesWhenProviderCapabilitiesAreRoutable()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountBalances" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountPositions" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:ReconciliationFeed" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_BlocksWhenProviderCapabilityIsNotRoutable()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: false));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Blocked);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "PROVIDER_CAPABILITY_UNROUTABLE" &&
                breakRow.CheckId == "provider-capability:ReconciliationFeed" &&
                breakRow.Category == ReconciliationBreakCategory.MissingPortfolioCoverage);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:ReconciliationFeed" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsCashVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, internalCash: 49_900m);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            var cashBreak = detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "CASH_BALANCE_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.CashMismatch &&
                breakRow.Variance == 100m).Subject;
            cashBreak.BreakKey.Should().NotBeNullOrWhiteSpace();
            cashBreak.Owner.Should().Be("fund-accounting");
            cashBreak.Tolerance.Should().Be(0.01m);
            cashBreak.SignOffState.Should().Be(ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            cashBreak.FirstObservedAt.Should().NotBeNull();
            cashBreak.LastObservedAt.Should().NotBeNull();
            detail.Summary.OpenBreakCount.Should().BeGreaterThan(0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_PreservesBreakAgingAndSignOffState()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, internalCash: 49_900m);

            var firstDetail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(DefaultBreakOwner: "fund-controller"));
            var firstCashBreak = firstDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            var signedOffDetail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(
                    DefaultBreakOwner: "fund-controller",
                    SignedOffBreakKeys: [firstCashBreak.BreakKey!],
                    SignedOffBy: "controller"));
            var signedOffCashBreak = signedOffDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            signedOffCashBreak.BreakKey.Should().Be(firstCashBreak.BreakKey);
            signedOffCashBreak.Owner.Should().Be("fund-controller");
            signedOffCashBreak.FirstObservedAt.Should().Be(firstCashBreak.FirstObservedAt);
            signedOffCashBreak.SignOffState.Should().Be(ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff);
            signedOffCashBreak.SignedOffBy.Should().Be("controller");
            signedOffCashBreak.SignedOffAt.Should().NotBeNull();
            signedOffDetail.Summary.SignedOffBreakCount.Should().BeGreaterThan(0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsSecuritiesMarketValueVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, internalSecuritiesMarketValue: 18_000m);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SECURITIES_MARKET_VALUE_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.AmountMismatch &&
                breakRow.Variance == 750m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_BlocksWhenRequiredSourcesAreMissing()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, runProviderSync: false, recordInternalSnapshot: false);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Blocked);
            detail.Breaks.Should().Contain(breakRow => breakRow.Code == "PROVIDER_PROJECTION_MISSING");
            detail.Breaks.Should().Contain(breakRow => breakRow.Code == "INTERNAL_LEDGER_SNAPSHOT_MISSING");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsSecurityMasterCoverageGap()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: false);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Summary.SecurityIssueCount.Should().Be(1);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SM_PROVIDER_POSITION_SECURITY_UNRESOLVED" &&
                breakRow.Symbol == "AAPL" &&
                breakRow.Category == ReconciliationBreakCategory.ClassificationGap);
            var passport = detail.SecurityMasterPassports.Should().NotBeNull().And.ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Unresolved);
            passport.ConfidenceScore.Should().Be(0m);
            passport.ResolutionSource.Should().Be("unresolved");
            passport.SecurityId.Should().BeNull();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_ProviderLedgerReconciliation_PostCreatesDetailAndGetReturnsLatest()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            await using var app = await CreateEndpointAppAsync(fixture);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconcile-ledger",
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"),
                JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await response.Content.ReadFromJsonAsync<ProviderLedgerReconciliationDetailDto>(JsonOptions);
            created.Should().NotBeNull();
            created!.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);

            var latest = await client.GetFromJsonAsync<ProviderLedgerReconciliationDetailDto>(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconciliation/latest",
                JsonOptions);
            latest.Should().NotBeNull();
            latest!.Summary.ReconciliationRunId.Should().Be(created.Summary.ReconciliationRunId);
            latest.SecurityMasterPassports.Should().NotBeNull().And.ContainSingle(item =>
                item.Symbol == "AAPL" &&
                item.Status == ProviderSecurityMasterPassportStatusDto.Resolved &&
                item.ResolutionSource == "security-master-lookup");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_ProviderLedgerReconciliation_RequiresBrokerageSyncPermissions()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            await using var app = await CreateEndpointAppAsync(fixture, UserPermission.ManageDirectLending);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconcile-ledger",
                new ProviderLedgerReconciliationRequestDto(),
                JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static async Task<TestFixture> CreateFixtureAsync(
        string root,
        bool includeSecurityLookup,
        bool runProviderSync = true,
        bool recordInternalSnapshot = true,
        decimal internalCash = 50_000m,
        decimal? internalSecuritiesMarketValue = 18_750m,
        ICapabilityRouter? capabilityRouter = null)
    {
        var accountId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "alpaca"));
        services.AddSingleton<InMemoryFundAccountService>();
        services.AddSingleton<IFundAccountService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IAccountManagementService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IAccountQueryService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IBrokeragePortfolioSync>(new FixedPortfolioAdapter());
        services.AddSingleton<IBrokerageActivitySync>(new EmptyActivityAdapter());
        if (includeSecurityLookup)
        {
            services.AddSingleton<ISecurityReferenceLookup>(new StaticSecurityReferenceLookup());
        }
        if (capabilityRouter is not null)
        {
            services.AddSingleton(capabilityRouter);
        }

        services.AddSingleton<BrokeragePortfolioSyncService>();
        services.AddSingleton<ProviderLedgerReconciliationService>();

        var provider = services.BuildServiceProvider();
        var accountService = provider.GetRequiredService<IFundAccountService>();
        await accountService.CreateAccountAsync(new CreateAccountRequest(
            accountId,
            AccountTypeDto.Brokerage,
            "BRK-LEDGER",
            "Provider Ledger Brokerage",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-10),
            "tests",
            Institution: "alpaca",
            LedgerReference: "BROKERAGE-CASH"));

        var brokerageSync = provider.GetRequiredService<BrokeragePortfolioSyncService>();
        if (runProviderSync)
        {
            await brokerageSync.RunSyncAsync(
                accountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-LEDGER", "tests"));
        }

        if (recordInternalSnapshot)
        {
            await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                accountId,
                DateOnly.FromDateTime(DateTime.UtcNow.Date),
                "USD",
                internalCash,
                "internal-ledger",
                RecordedBy: "tests",
                SecuritiesMarketValue: internalSecuritiesMarketValue,
                ExternalReference: "internal-ledger-snapshot"));
        }

        return new TestFixture(
            provider,
            accountId,
            accountService,
            brokerageSync,
            provider.GetRequiredService<ProviderLedgerReconciliationService>());
    }

    private static async Task<WebApplication> CreateEndpointAppAsync(
        TestFixture fixture,
        UserPermission permissions =
            UserPermission.ManageDirectLending |
            UserPermission.ViewTrades |
            UserPermission.ExecuteTrades)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(fixture.AccountService);
        builder.Services.AddSingleton<IAccountManagementService>(_ => (IAccountManagementService)fixture.AccountService);
        builder.Services.AddSingleton<IAccountQueryService>(_ => (IAccountQueryService)fixture.AccountService);
        builder.Services.AddSingleton(fixture.BrokerageSync);
        builder.Services.AddSingleton(fixture.Reconciliation);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "ops-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapFundAccountEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-provider-ledger-reconciliation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record TestFixture(
        ServiceProvider Services,
        Guid AccountId,
        IFundAccountService AccountService,
        BrokeragePortfolioSyncService BrokerageSync,
        ProviderLedgerReconciliationService Reconciliation) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }

    private sealed class FixedPortfolioAdapter : IBrokeragePortfolioSync
    {
        public string ProviderId => "alpaca";

        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
            string externalAccountId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokeragePortfolioSnapshotDto(
                new BrokerageExternalAccountDto("alpaca", externalAccountId, "Provider Ledger Account", "active", "USD", retrievedAt),
                new BrokerageBalanceSnapshotDto(50_000m, 68_750m, 70_000m, "USD"),
                [
                    new BrokeragePositionSnapshotDto(
                        "AAPL",
                        100m,
                        150m,
                        187.5m,
                        18_750m,
                        3_750m,
                        "Equity",
                        "Apple Inc.",
                        "pos-aapl",
                        "USD")
                ],
                retrievedAt));
        }
    }

    private sealed class EmptyActivityAdapter : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                "alpaca",
                externalAccountId,
                DateTimeOffset.UtcNow,
                Orders: [],
                Fills: [],
                CashTransactions: []));
        }
    }

    private sealed class StaticSecurityReferenceLookup : ISecurityReferenceLookup
    {
        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var normalized = symbol.Trim().ToUpperInvariant();
            return Task.FromResult<WorkstationSecurityReference?>(new WorkstationSecurityReference(
                Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D"),
                $"{normalized} security",
                "Equity",
                "USD",
                SecurityStatusDto.Active,
                normalized,
                MatchedIdentifierKind: "Ticker",
                MatchedIdentifierValue: normalized,
                MatchedProvider: "test"));
        }
    }

    private sealed record FixedCapabilityRouter(bool IsRoutable) : ICapabilityRouter
    {
        public ValueTask<ProviderRouteResult> RouteAsync(ProviderRouteContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRoutable)
            {
                return ValueTask.FromResult(new ProviderRouteResult(
                    context,
                    SelectedDecision: null,
                    Candidates: [],
                    SkippedCandidates: [$"Provider fixture does not support capability '{context.Capability}'."],
                    PolicyGate: $"Capability '{context.Capability}' requires an account-scoped provider binding."));
            }

            var decision = new ProviderRouteDecision(
                ConnectionId: $"fixture-{context.Capability}",
                ProviderFamilyId: "alpaca",
                Capability: context.Capability,
                SafetyMode: ProviderSafetyMode.NoAutomaticFailover,
                ScopeRank: 500,
                Priority: 100,
                IsHealthy: true,
                ReasonCodes: [$"Capability '{context.Capability}' is supported by the fixture."],
                FallbackConnectionIds: []);

            return ValueTask.FromResult(new ProviderRouteResult(
                context,
                SelectedDecision: decision,
                Candidates: [decision],
                SkippedCandidates: []));
        }
    }
}
