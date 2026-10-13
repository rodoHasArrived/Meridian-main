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
    public async Task BrokerageSyncDiscoveryRoute_ShouldAcceptViewTradesWithoutManagementPermission()
    {
        await using var app = await CreateAppAsync(
            [],
            [],
            UserPermission.ViewTrades,
            includeTenantScope: true);

        var response = await app.GetTestClient()
            .GetAsync("/api/fund-accounts/brokerage-sync/accounts");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotImplemented,
            "the discovery handler should be reached even when the caller has no fund-account management permission");
    }

    [Fact]
    public async Task BrokerageSyncDiscoveryRoute_ShouldRequireTenantAndCompanyScope()
    {
        await using var app = await CreateAppAsync(
            [],
            [],
            UserPermission.ViewTrades);

        var response = await app.GetTestClient()
            .GetAsync("/api/fund-accounts/brokerage-sync/accounts");

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "brokerage discovery must not expose provider accounts without server-resolved tenant and company scope");
    }

    [Fact]
    public async Task BrokerageHouseholdRoute_ShouldRequireTenantAndCompanyScope()
    {
        var fundId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [BuildAccount(accountId, fundId, "HOUSEHOLD-BROKERAGE")],
            [(AccessScopeKindDto.Account, accountId)],
            UserPermission.ViewTrades);

        var response = await app.GetTestClient().GetAsync("/api/portfolio/household");

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "a cross-account projection must not run without server-resolved tenant and company scope");
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

    [Theory]
    [InlineData("null-lines")]
    [InlineData("null-line")]
    [InlineData("line-account")]
    [InlineData("line-batch")]
    [InlineData("line-date")]
    public async Task CustodianStatementRoute_ShouldRejectInvalidLinesWithoutRetainingEvidence(string invalidInput)
    {
        var accountId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 6, 16);
        var request = BuildCustodianStatement(accountId, asOfDate);
        var line = request.Lines[0];
        request = request with
        {
            Lines = invalidInput switch
            {
                "null-lines" => null!,
                "null-line" => [line, null!],
                "line-account" => [line, line with { LineId = Guid.NewGuid(), AccountId = Guid.NewGuid() }],
                "line-batch" => [line, line with { LineId = Guid.NewGuid(), BatchId = Guid.NewGuid() }],
                "line-date" => [line, line with { LineId = Guid.NewGuid(), AsOfDate = asOfDate.AddDays(1) }],
                _ => throw new ArgumentOutOfRangeException(nameof(invalidInput))
            }
        };
        await using var app = await CreateAppAsync(
            [BuildAccount(accountId, fundId, "CUSTODIAN-INTAKE")],
            [(AccessScopeKindDto.Account, accountId)]);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/fund-accounts/{accountId:D}/custodian-statements", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var query = app.Services.GetRequiredService<IAccountQueryService>();
        (await query.GetCustodianPositionsAsync(accountId, asOfDate)).Should().BeEmpty();
        (await query.GetCustodianPositionsAsync(accountId, asOfDate.AddDays(1))).Should().BeEmpty();
        var management = app.Services.GetRequiredService<IAccountManagementService>();
        var reconciliation = await management.ReconcileAccountAsync(new ReconcileAccountRequest(
            accountId, asOfDate, "endpoint-auth-test"));
        reconciliation.TotalChecks.Should().Be(0, "rejected intake must not retain a statement batch header");
    }

    [Theory]
    [InlineData("null-lines")]
    [InlineData("null-line")]
    [InlineData("line-account")]
    [InlineData("line-batch")]
    public async Task BankStatementRoute_ShouldRejectInvalidLinesWithoutRetainingEvidence(string invalidInput)
    {
        var accountId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var request = BuildBankStatement(accountId, new DateOnly(2026, 6, 16));
        var line = request.Lines[0];
        request = request with
        {
            Lines = invalidInput switch
            {
                "null-lines" => null!,
                "null-line" => [line, null!],
                "line-account" => [line, line with { LineId = Guid.NewGuid(), AccountId = Guid.NewGuid() }],
                "line-batch" => [line, line with { LineId = Guid.NewGuid(), BatchId = Guid.NewGuid() }],
                _ => throw new ArgumentOutOfRangeException(nameof(invalidInput))
            }
        };
        await using var app = await CreateAppAsync(
            [BuildAccount(accountId, fundId, "BANK-INTAKE")],
            [(AccessScopeKindDto.Account, accountId)]);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/fund-accounts/{accountId:D}/bank-statements", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var query = app.Services.GetRequiredService<IAccountQueryService>();
        (await query.GetBankStatementLinesAsync(accountId)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("custodian-statements")]
    [InlineData("bank-statements")]
    public async Task StatementRoutes_ShouldRejectInvalidJsonFieldsWithoutRetainingEvidence(string statementRoute)
    {
        var accountId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [BuildAccount(accountId, fundId, "INVALID-STATEMENT")],
            [(AccessScopeKindDto.Account, accountId)]);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/fund-accounts/{accountId:D}/{statementRoute}",
            new { accountId, batchId = "not-a-guid", lines = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var query = app.Services.GetRequiredService<IAccountQueryService>();
        (await query.GetCustodianPositionsAsync(accountId, new DateOnly(2026, 6, 16))).Should().BeEmpty();
        (await query.GetBankStatementLinesAsync(accountId)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("custodian-statements")]
    [InlineData("bank-statements")]
    public async Task StatementRoutes_ShouldAuthorizeAccountBeforeValidatingLines(string statementRoute)
    {
        var accountId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        await using var app = await CreateAppAsync(
            [BuildAccount(accountId, fundId, "DENIED-STATEMENT")],
            []);
        object request = statementRoute == "custodian-statements"
            ? BuildCustodianStatement(accountId, new DateOnly(2026, 6, 16)) with { Lines = null! }
            : BuildBankStatement(accountId, new DateOnly(2026, 6, 16)) with { Lines = null! };

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/fund-accounts/{accountId:D}/{statementRoute}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var query = app.Services.GetRequiredService<IAccountQueryService>();
        (await query.GetCustodianPositionsAsync(accountId, new DateOnly(2026, 6, 16))).Should().BeEmpty();
        (await query.GetBankStatementLinesAsync(accountId)).Should().BeEmpty();
    }

    [Fact]
    public async Task StatementRoutes_ShouldRetainAuthorizedMatchingLines()
    {
        var accountId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 6, 16);
        await using var app = await CreateAppAsync(
            [BuildAccount(accountId, fundId, "VALID-STATEMENT")],
            [(AccessScopeKindDto.Account, accountId)]);
        var custodianRequest = BuildCustodianStatement(accountId, asOfDate);
        var bankRequest = BuildBankStatement(accountId, asOfDate);
        var client = app.GetTestClient();

        var custodianResponse = await client.PostAsJsonAsync(
            $"/api/fund-accounts/{accountId:D}/custodian-statements", custodianRequest);
        var bankResponse = await client.PostAsJsonAsync(
            $"/api/fund-accounts/{accountId:D}/bank-statements", bankRequest);

        custodianResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        bankResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var query = app.Services.GetRequiredService<IAccountQueryService>();
        (await query.GetCustodianPositionsAsync(accountId, asOfDate)).Should().Equal(custodianRequest.Lines);
        (await query.GetBankStatementLinesAsync(accountId)).Should().Equal(bankRequest.Lines);
    }

    private static IngestCustodianStatementRequest BuildCustodianStatement(Guid accountId, DateOnly asOfDate)
    {
        var batchId = Guid.NewGuid();
        return new IngestCustodianStatementRequest(
            batchId, accountId, asOfDate, "Custodian", "csv", null,
            [new CustodianPositionLineDto(
                Guid.NewGuid(), batchId, accountId, asOfDate, "AAPL", "Ticker", 10m, 1_000m,
                "USD", "Apple Inc.", "Equity", IsShort: false)],
            "endpoint-auth-test");
    }

    private static IngestBankStatementRequest BuildBankStatement(Guid accountId, DateOnly statementDate)
    {
        var batchId = Guid.NewGuid();
        return new IngestBankStatementRequest(
            batchId, accountId, statementDate, "Bank", null,
            [new BankStatementLineDto(
                Guid.NewGuid(), batchId, accountId, statementDate, statementDate, 100m, "USD", "Deposit",
                "Capital contribution", "BANK-001", 100m)],
            "endpoint-auth-test");
    }

    private static async Task<WebApplication> CreateAppAsync(
        IReadOnlyList<CreateAccountRequest> accounts,
        IReadOnlyCollection<(AccessScopeKindDto Kind, Guid Id)> allowedScopes,
        UserPermission permissions = UserPermission.ManageDirectLending,
        bool includeTenantScope = false)
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
            if (includeTenantScope)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
            }

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
