using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class TradingBrokerageRecoveryEndpointTests
{
    private const UserPermission TradingPermissions = UserPermission.ViewTrades | UserPermission.ExecuteTrades;
    private const string LinkedExternalAccount = "PA-RETAINED-LINK";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"fundAccountId\":null}")]
    [InlineData("{\"fundAccountId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task Recovery_RequiresAnExplicitNonemptyAccount_BeforeBrokerIo(string body)
    {
        await using var harness = await Harness.CreateAsync();

        using var response = await harness.App.GetTestClient().PostAsync(
            UiApiRoutes.WorkstationTradingBrokerageRecovery,
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.AssertNoBrokerIo();
    }

    [Theory]
    [InlineData(UserPermission.ViewTrades)]
    [InlineData(UserPermission.ExecuteTrades)]
    [InlineData(UserPermission.ManageOrders)]
    public async Task Recovery_RequiresViewTradesAndAWritePermission_BeforeBrokerIo(UserPermission permissions)
    {
        await using var harness = await Harness.CreateAsync(permissions: permissions);

        using var response = await harness.RecoverAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        harness.AssertNoBrokerIo();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Recovery_RequiresTenantAndCompanyScope_BeforeBrokerIo(bool hasTenant, bool hasCompany)
    {
        await using var harness = await Harness.CreateAsync(hasTenant: hasTenant, hasCompany: hasCompany);

        using var response = await harness.RecoverAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        harness.AssertNoBrokerIo();
    }

    [Fact]
    public async Task Recovery_RejectsForeignAccountGrant_BeforeBrokerIo()
    {
        await using var harness = await Harness.CreateAsync(hasAccountGrant: false);

        using var response = await harness.RecoverAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        harness.AssertNoBrokerIo();
    }

    [Fact]
    public async Task Recovery_RejectsReadOnlyAccountGrant_BeforeBrokerIo()
    {
        await using var harness = await Harness.CreateAsync(scopedPermissions: UserPermission.ViewTrades);

        using var response = await harness.RecoverAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        harness.AssertNoBrokerIo();
    }

    [Fact]
    public async Task Recovery_RejectsMissingAccountDespiteGrant_BeforeBrokerIo()
    {
        await using var harness = await Harness.CreateAsync(accountExists: false);

        using var response = await harness.RecoverAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        harness.AssertNoBrokerIo();
    }

    [Theory]
    [InlineData("robinhood")]
    [InlineData("interactive-brokers")]
    public async Task Recovery_RejectsUnsupportedRetainedProvider_BeforeBrokerIo(string provider)
    {
        await using var harness = await Harness.CreateAsync(linkedProvider: provider);

        using var response = await harness.RecoverAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        harness.AssertNoBrokerIo();
    }

    [Theory]
    [InlineData(UserPermission.ExecuteTrades)]
    [InlineData(UserPermission.ManageOrders)]
    public async Task Recovery_UsesRetainedAccountLinkAndReturnsScopedReadiness(UserPermission writePermission)
    {
        var permissions = UserPermission.ViewTrades | writePermission;
        await using var harness = await Harness.CreateAsync(
            permissions: permissions,
            scopedPermissions: permissions);

        // Additional client fields cannot choose a different brokerage account or provider.
        using var response = await harness.App.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.WorkstationTradingBrokerageRecovery,
            new
            {
                fundAccountId = harness.AccountId,
                externalAccountId = "PA-FOREIGN-CLIENT-INPUT",
                providerId = "robinhood"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await response.Content.ReadFromJsonAsync<TradingOperatorReadinessDto>(JsonOptions);
        readiness.Should().NotBeNull();
        readiness!.BrokerageSync.Should().NotBeNull();
        readiness.BrokerageSync!.FundAccountId.Should().Be(harness.AccountId);
        readiness.BrokerageSync.ExternalAccountId.Should().Be(LinkedExternalAccount);
        var status = harness.App.Services.GetRequiredService<LiveBrokeragePortfolioSyncService>()
            .GetStatus(harness.AccountId);
        status.IsReady.Should().BeTrue();
        status.ExternalAccountId.Should().Be(LinkedExternalAccount);
        await harness.Portfolio.Received(2).GetPortfolioSnapshotAsync(
            LinkedExternalAccount, Arg.Any<CancellationToken>());
        await harness.Portfolio.DidNotReceive().GetPortfolioSnapshotAsync(
            "PA-FOREIGN-CLIENT-INPUT", Arg.Any<CancellationToken>());
        harness.AssertNoOrderMutation();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recovery_ExpiresAtTheOldestAccountObservationInsteadOfTheLatestFetch(bool olderAccountAsOf)
    {
        await using var harness = await Harness.CreateAsync();
        var snapshot = await harness.Portfolio.GetPortfolioSnapshotAsync(LinkedExternalAccount);
        var oldestObservation = snapshot.RetrievedAt.AddSeconds(-10);
        snapshot = olderAccountAsOf
            ? snapshot with { AccountSnapshot = snapshot.AccountSnapshot! with { AsOf = oldestObservation } }
            : snapshot with { Account = snapshot.Account with { RetrievedAt = oldestObservation } };
        harness.Portfolio.GetPortfolioSnapshotAsync(LinkedExternalAccount, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        using var response = await harness.RecoverAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await response.Content.ReadFromJsonAsync<TradingOperatorReadinessDto>(JsonOptions);
        var portfolio = readiness!.BrokerageRecovery!.Portfolio!;
        var maximumAge = harness.App.Services.GetRequiredService<LiveBrokeragePortfolioSyncService>().MaximumAge;
        portfolio.IsFresh.Should().BeTrue();
        portfolio.ObservedAt.Should().Be(snapshot.RetrievedAt);
        portfolio.ExpiresAt.Should().Be(oldestObservation + maximumAge,
            "a newer overall fetch cannot extend authority beyond the oldest constituent account evidence");
        portfolio.ExpiresAt.Should().BeBefore(snapshot.RetrievedAt + maximumAge);
    }

    [Theory]
    [InlineData(UiApiRoutes.WorkstationTradingReadiness, false, true)]
    [InlineData(UiApiRoutes.WorkstationTradingReadiness, true, false)]
    [InlineData(UiApiRoutes.WorkstationTrading, false, true)]
    [InlineData(UiApiRoutes.WorkstationTrading, true, false)]
    [InlineData(UiApiRoutes.WorkstationOperatorInbox, false, true)]
    [InlineData(UiApiRoutes.WorkstationOperatorInbox, true, false)]
    public async Task Readiness_RejectsUnauthorizedOrMissingAccount_BeforeBrokerIo(
        string route, bool hasAccountGrant, bool accountExists)
    {
        await using var harness = await Harness.CreateAsync(
            hasAccountGrant: hasAccountGrant,
            accountExists: accountExists);

        using var response = await harness.App.GetTestClient().GetAsync(
            $"{route}?fundAccountId={harness.AccountId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        harness.AssertNoBrokerIo();
    }

    [Theory]
    [InlineData("/api/execution/account")]
    [InlineData("/api/execution/positions")]
    [InlineData("/api/execution/portfolio")]
    [InlineData("/api/execution/accounts")]
    [InlineData("/api/execution/portfolio/aggregate")]
    public async Task LegacyBrokerPortfolioReads_RequireTheObservedAccountGrant(string route)
    {
        await using var harness = await Harness.CreateAsync(hasAccountGrant: false, registerBrokerPortfolio: true);
        await harness.App.Services.GetRequiredService<LiveBrokeragePortfolioSyncService>()
            .SynchronizeAsync(harness.AccountId, LinkedExternalAccount);
        harness.Gateway.ClearReceivedCalls();

        using var response = await harness.App.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // The scope decision reads retained local identity, without a broker request.
        await harness.Portfolio.DidNotReceive().GetPortfolioSnapshotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await harness.Gateway.DidNotReceive().GetOpenOrdersAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trading_HidesBrokerBookUntilItsOwningAccountIsSelected(bool selectOtherAuthorizedAccount)
    {
        var otherAccount = Guid.NewGuid();
        await using var harness = await Harness.CreateAsync(
            registerBrokerPortfolio: true, additionalAuthorizedAccount: otherAccount);
        await harness.App.Services.GetRequiredService<LiveBrokeragePortfolioSyncService>()
            .SynchronizeAsync(harness.AccountId, LinkedExternalAccount);

        var route = UiApiRoutes.WorkstationTrading
            + (selectOtherAuthorizedAccount ? $"?fundAccountId={otherAccount:D}" : string.Empty);
        using var response = await harness.App.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<WorkstationTradingPayload>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Positions.Should().NotContain(position => position.Symbol == "AAPL");
        payload.OpenOrders.Should().BeEmpty();
        payload.Fills.Should().BeEmpty();
        payload.Metrics.Should().Contain(metric => metric.Id == "trading-cash" && metric.Value == "—");
    }

    [Fact]
    public async Task Trading_ShowsBrokerHoldingsForTheSelectedAuthorizedAccount()
    {
        await using var harness = await Harness.CreateAsync(registerBrokerPortfolio: true);
        await harness.App.Services.GetRequiredService<LiveBrokeragePortfolioSyncService>()
            .SynchronizeAsync(harness.AccountId, LinkedExternalAccount);

        using var response = await harness.App.GetTestClient().GetAsync(
            $"{UiApiRoutes.WorkstationTrading}?fundAccountId={harness.AccountId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<WorkstationTradingPayload>(JsonOptions);
        payload!.Positions.Should().ContainSingle(position => position.Symbol == "AAPL");
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _root;

        private Harness(WebApplication app, Guid accountId, IBrokerageGateway gateway, string root)
        {
            App = app;
            AccountId = accountId;
            Gateway = gateway;
            _root = root;
        }

        public WebApplication App { get; }
        public Guid AccountId { get; }
        public IBrokerageGateway Gateway { get; }
        public IBrokeragePortfolioSync Portfolio => (IBrokeragePortfolioSync)Gateway;

        public Task<HttpResponseMessage> RecoverAsync() => App.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.WorkstationTradingBrokerageRecovery,
            new TradingBrokerageRecoveryRequestDto(AccountId));

        public void AssertNoBrokerIo()
        {
            Gateway.ReceivedCalls().Should().BeEmpty("authorization must finish before any broker access");
        }

        public void AssertNoOrderMutation()
        {
            Gateway.ReceivedCalls().Should().NotContain(call =>
                call.GetMethodInfo().Name.StartsWith("Submit", StringComparison.Ordinal)
                || call.GetMethodInfo().Name.StartsWith("Cancel", StringComparison.Ordinal)
                || call.GetMethodInfo().Name.StartsWith("Replace", StringComparison.Ordinal)
                || call.GetMethodInfo().Name.StartsWith("Modify", StringComparison.Ordinal),
                "portfolio recovery must only read and reconcile broker state");
        }

        public static async Task<Harness> CreateAsync(
            UserPermission permissions = TradingPermissions,
            UserPermission scopedPermissions = TradingPermissions,
            bool hasAccountGrant = true,
            bool accountExists = true,
            bool hasTenant = true,
            bool hasCompany = true,
            string linkedProvider = "alpaca",
            bool registerBrokerPortfolio = false,
            Guid? additionalAuthorizedAccount = null)
        {
            var accountId = Guid.NewGuid();
            var accountService = new InMemoryFundAccountService();
            if (accountExists)
            {
                await accountService.CreateAccountAsync(new CreateAccountRequest(
                    accountId, AccountTypeDto.Brokerage, "RECOVERY-LOCAL", "Recovery account", "USD",
                    DateTimeOffset.UtcNow, "endpoint-test", Institution: linkedProvider,
                    PortfolioId: LinkedExternalAccount));
            }
            if (additionalAuthorizedAccount is { } additionalAccount)
            {
                await accountService.CreateAccountAsync(new CreateAccountRequest(
                    additionalAccount, AccountTypeDto.Brokerage, "OTHER-LOCAL", "Other account", "USD",
                    DateTimeOffset.UtcNow, "endpoint-test", Institution: "alpaca", PortfolioId: "PA-OTHER"));
            }

            var now = DateTimeOffset.UtcNow;
            var snapshot = new BrokeragePortfolioSnapshotDto(
                new BrokerageExternalAccountDto("alpaca", LinkedExternalAccount, "Paper account", "active", "USD", now),
                new BrokerageBalanceSnapshotDto(10_000m, 10_500m, 20_000m, "USD"),
                [new BrokeragePositionSnapshotDto("AAPL", 5m, 100m, 100m, 500m, 0m, "us_equity", Currency: "USD")], now,
                new BrokerageAccountSnapshotDto("alpaca", LinkedExternalAccount, now, "USD", "active",
                    BrokerageMarginRegime.RegulationT, 10_000m, 10_500m, 20_000m),
                IsComplete: true);
            var gateway = Substitute.For<IBrokerageGateway, IBrokeragePortfolioSync>();
            gateway.GatewayId.Returns("alpaca");
            gateway.BrokerDisplayName.Returns("Alpaca Markets");
            gateway.IsConnected.Returns(true);
            gateway.CheckHealthAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(BrokerHealthStatus.Healthy("ready")));
            gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<BrokerOrder>>([]));
            ((IBrokeragePortfolioSync)gateway).ProviderId.Returns("alpaca");
            ((IBrokeragePortfolioSync)gateway).GetPortfolioSnapshotAsync(
                    Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(snapshot));
            var oms = Substitute.For<IOrderManager>();
            oms.GetOpenOrders().Returns(Array.Empty<OrderState>());
            oms.GetCompletedOrders(Arg.Any<int>()).Returns(Array.Empty<OrderState>());
            oms.GetExposureReservingOrders().Returns(Array.Empty<OrderState>());
            var root = Path.Combine(Path.GetTempPath(), "meridian-recovery-endpoint-tests", Guid.NewGuid().ToString("N"));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IAccountQueryService>(accountService);
            builder.Services.AddSingleton<IFundAccountService>(accountService);
            builder.Services.AddSingleton<IScopedAuthorizationService>(new AccountAuthorization(
                hasAccountGrant ? accountId : Guid.NewGuid(), scopedPermissions, additionalAuthorizedAccount));
            builder.Services.AddSingleton<IBrokerageGateway>(gateway);
            builder.Services.AddSingleton<IOrderManager>(oms);
            builder.Services.AddSingleton(new BrokerageExecutionReconciliationService(
                NullLogger<BrokerageExecutionReconciliationService>.Instance));
            builder.Services.AddSingleton(provider => new BrokeragePortfolioSyncService(
                new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "alpaca"),
                [], [], [], provider, NullLogger<BrokeragePortfolioSyncService>.Instance));
            builder.Services.AddSingleton(provider => new LiveBrokeragePortfolioSyncService(
                () => gateway, () => oms, provider.GetRequiredService<BrokerageExecutionReconciliationService>()));
            if (registerBrokerPortfolio)
            {
                builder.Services.AddSingleton<BrokeragePortfolioState>();
                builder.Services.AddSingleton<IPortfolioState>(provider => provider.GetRequiredService<BrokeragePortfolioState>());
            }
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "recovery-operator";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                if (hasTenant)
                    context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "recovery-tenant";
                else
                    // An omitted tenant inherits the authenticated company by design. An
                    // explicit unresolved tenant must not be replaced by that fallback.
                    context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "all";
                if (hasCompany)
                    context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "recovery-company";
                await next();
            });
            app.MapWorkstationEndpoints(JsonOptions);
            if (registerBrokerPortfolio)
                app.MapExecutionEndpoints(JsonOptions);
            await app.StartAsync();
            gateway.ClearReceivedCalls();
            return new Harness(app, accountId, gateway, root);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class AccountAuthorization(Guid allowedAccount, UserPermission permissions, Guid? additionalAccount = null) : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            var allowed = scopeKind == AccessScopeKindDto.Account && scopeId.HasValue
                && (scopeId == allowedAccount || scopeId == additionalAccount)
                && permissions.HasFlag(requiredPermission);
            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                allowed, actor, requiredPermission, scopeKind, scopeId,
                allowed ? "The account grant permits this action." : "The account grant denies this action."));
        }
    }
}
