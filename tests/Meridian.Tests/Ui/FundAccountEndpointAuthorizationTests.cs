using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class FundAccountEndpointAuthorizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FundAccountFundRoute_ShouldRequireScopedFundAccess()
    {
        var allowedFundId = Guid.NewGuid();
        var deniedFundId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [
                BuildAccount(Guid.NewGuid(), allowedFundId, "ALLOWED-CASH"),
                BuildAccount(Guid.NewGuid(), deniedFundId, "DENIED-CASH")
            ],
            [(AccessScopeKindDto.Fund, allowedFundId)]);

        var client = app.GetTestClient();

        var allowed = await client.GetAsync($"/api/fund-accounts/fund/{allowedFundId:D}");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        var denied = await client.GetAsync($"/api/fund-accounts/fund/{deniedFundId:D}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FundAccountQuery_ShouldDenyUnscopedListForNonAdmin()
    {
        var fundId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [BuildAccount(Guid.NewGuid(), fundId, "TENANT-CASH")],
            [(AccessScopeKindDto.Fund, fundId)]);

        var response = await app.GetTestClient().GetAsync("/api/fund-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FundAccountQuery_ShouldRequireScopedFundAccessForFundFilter()
    {
        var allowedFundId = Guid.NewGuid();
        var deniedFundId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [
                BuildAccount(Guid.NewGuid(), allowedFundId, "ALLOWED-CASH"),
                BuildAccount(Guid.NewGuid(), deniedFundId, "DENIED-CASH")
            ],
            [(AccessScopeKindDto.Fund, allowedFundId)]);

        var client = app.GetTestClient();

        var allowed = await client.GetFromJsonAsync<AccountSummaryDto[]>(
            $"/api/fund-accounts?fundId={allowedFundId:D}",
            JsonOptions);
        allowed.Should().ContainSingle(account => account.FundId == allowedFundId);

        var denied = await client.GetAsync($"/api/fund-accounts?fundId={deniedFundId:D}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BrokerageSyncAccountRoute_ShouldRequireScopedAccountAccess()
    {
        var fundId = Guid.NewGuid();
        var allowedAccountId = Guid.NewGuid();
        var deniedAccountId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [
                BuildAccount(allowedAccountId, fundId, "ALLOWED-BROKERAGE"),
                BuildAccount(deniedAccountId, fundId, "DENIED-BROKERAGE")
            ],
            [(AccessScopeKindDto.Account, allowedAccountId)],
            UserPermission.ManageDirectLending | UserPermission.ViewTrades);

        var client = app.GetTestClient();

        var allowed = await client.GetAsync($"/api/fund-accounts/{allowedAccountId:D}/brokerage-sync/status");
        allowed.StatusCode.Should().Be(HttpStatusCode.NotImplemented);

        var denied = await client.GetAsync($"/api/fund-accounts/{deniedAccountId:D}/brokerage-sync/status");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationalAccountRoutes_ShouldRequireScopedAccountAccess()
    {
        var fundId = Guid.NewGuid();
        var allowedAccountId = Guid.NewGuid();
        var deniedAccountId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [
                BuildAccount(allowedAccountId, fundId, "ALLOWED-OPS"),
                BuildAccount(deniedAccountId, fundId, "DENIED-OPS")
            ],
            [(AccessScopeKindDto.Account, allowedAccountId)]);

        var client = app.GetTestClient();
        var managementService = app.Services.GetRequiredService<IAccountManagementService>();
        var deniedRun = await managementService.ReconcileAccountAsync(new ReconcileAccountRequest(
            deniedAccountId,
            new DateOnly(2026, 6, 16),
            "endpoint-auth-test"));
        var deniedRoutes = new[]
        {
            $"/api/fund-accounts/{deniedAccountId:D}/close-readiness",
            $"/api/fund-accounts/{deniedAccountId:D}/balance-snapshots",
            $"/api/fund-accounts/{deniedAccountId:D}/balance-snapshots/latest",
            $"/api/fund-accounts/{deniedAccountId:D}/sync-history",
            $"/api/fund-accounts/{deniedAccountId:D}/readiness",
            $"/api/fund-accounts/{deniedAccountId:D}/custodian-positions?asOfDate=2026-06-16",
            $"/api/fund-accounts/{deniedAccountId:D}/bank-statement-lines",
            $"/api/fund-accounts/{deniedAccountId:D}/reconciliation-runs",
            $"/api/fund-accounts/{deniedAccountId:D}/reconciliation-queue-status",
            $"/api/fund-accounts/reconciliation-runs/{deniedRun.ReconciliationRunId:D}/results"
        };

        foreach (var route in deniedRoutes)
        {
            using var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, route);
        }
    }

    [Fact]
    public async Task OperationalWriteRoutes_ShouldRejectMismatchedBodyAccountId()
    {
        var fundId = Guid.NewGuid();
        var routeAccountId = Guid.NewGuid();
        var bodyAccountId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [BuildAccount(routeAccountId, fundId, "ROUTE-OPS")],
            [(AccessScopeKindDto.Account, routeAccountId)]);
        var client = app.GetTestClient();

        var balance = await client.PostAsJsonAsync(
            $"/api/fund-accounts/{routeAccountId:D}/balance-snapshots",
            new RecordAccountBalanceSnapshotRequest(
                bodyAccountId,
                new DateOnly(2026, 6, 16),
                "USD",
                100m,
                "endpoint-auth-test"));
        var custodian = await client.PostAsJsonAsync(
            $"/api/fund-accounts/{routeAccountId:D}/custodian-statements",
            new IngestCustodianStatementRequest(
                Guid.NewGuid(),
                bodyAccountId,
                new DateOnly(2026, 6, 16),
                "Custodian",
                "csv",
                null,
                [
                    new CustodianPositionLineDto(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        bodyAccountId,
                        new DateOnly(2026, 6, 16),
                        "AAPL",
                        "Ticker",
                        10m,
                        1_000m,
                        "USD",
                        "Apple Inc.",
                        "Equity",
                        IsShort: false)
                ],
                "endpoint-auth-test"));
        var bank = await client.PostAsJsonAsync(
            $"/api/fund-accounts/{routeAccountId:D}/bank-statements",
            new IngestBankStatementRequest(
                Guid.NewGuid(),
                bodyAccountId,
                new DateOnly(2026, 6, 16),
                "Bank",
                null,
                [
                    new BankStatementLineDto(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        bodyAccountId,
                        new DateOnly(2026, 6, 16),
                        new DateOnly(2026, 6, 16),
                        100m,
                        "USD",
                        "Deposit",
                        "Capital contribution",
                        "BANK-001",
                        100m)
                ],
                "endpoint-auth-test"));
        var reconcile = await client.PostAsJsonAsync(
            $"/api/fund-accounts/{routeAccountId:D}/reconcile",
            new ReconcileAccountRequest(
                bodyAccountId,
                new DateOnly(2026, 6, 16),
                "endpoint-auth-test"));

        balance.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        custodian.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        bank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reconcile.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<WebApplication> CreateAppAsync(
        IReadOnlyList<CreateAccountRequest> accounts,
        IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id)> allowedScopes,
        UserPermission permissions = UserPermission.ManageDirectLending)
    {
        var accountService = new InMemoryFundAccountService();
        foreach (var account in accounts)
        {
            await accountService.CreateAccountAsync(account);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(accountService);
        builder.Services.AddSingleton<IAccountManagementService>(accountService);
        builder.Services.AddSingleton<IAccountQueryService>(accountService);
        builder.Services.AddSingleton<IScopedAuthorizationService>(
            new TestScopedAuthorizationService(allowedScopes));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "fund-ops-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapFundAccountEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static CreateAccountRequest BuildAccount(Guid accountId, Guid fundId, string accountCode)
        => new(
            accountId,
            AccountTypeDto.Custody,
            accountCode,
            $"{accountCode} account",
            "USD",
            DateTimeOffset.UtcNow,
            "test-operator",
            FundId: fundId);

    private sealed class TestScopedAuthorizationService : IScopedAuthorizationService
    {
        private readonly HashSet<(AccessScopeKindDto Kind, Guid Id)> _allowedScopes;

        public TestScopedAuthorizationService(IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id)> allowedScopes)
        {
            _allowedScopes = allowedScopes.ToHashSet();
        }

        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            var allowed = scopeId.HasValue && _allowedScopes.Contains((scopeKind, scopeId.Value));
            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                allowed,
                actor,
                requiredPermission,
                scopeKind,
                scopeId,
                allowed ? "Test scope grants access." : "Test scope denies access."));
        }
    }
}
