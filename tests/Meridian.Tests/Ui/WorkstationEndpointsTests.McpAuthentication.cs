using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Mcp.Services;
using Meridian.Mcp.Tools;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private const string McpFixturePassword = "synthetic-mcp-sign-in-password";
    private const string McpFixtureApiKey = "synthetic-mcp-login-transport-key";
    private static readonly string McpFixturePasswordHash = PasswordHashing.HashPassword(McpFixturePassword);

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public async Task McpLedgerAuthentication_UsesRealSessionForRunAndSessionOnlyReads_AndRefusesForeignScope(string scheme)
    {
        var loginCalls = new int[1];
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        await using var app = await CreateMcpAuthenticationHostAsync(bookId, periodId, loginCalls, requireApiKey: true, localWorkstation: scheme == "http");
        using var http = app.GetTestClient();
        http.BaseAddress = new Uri($"{scheme}://localhost/");
        var alice = new LedgerTools(new LedgerApiClient(http, new("alice", McpFixturePassword, apiKey: McpFixtureApiKey)));

        using var local = JsonDocument.Parse(await alice.GetJournalAsync("mcp-company-a"));
        local.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        using var running = JsonDocument.Parse(await alice.GetPostedRunningBalanceAsync(periodId, "Cash"));
        running.RootElement.GetProperty("points").GetArrayLength().Should().BeGreaterThan(0,
            "the real session-only endpoint must pass authentication and return authoritative journal data");
        using var control = JsonDocument.Parse(await alice.GetCompletenessAsync(bookId, periodId, bookId, "broker", "Fill"));
        control.RootElement.GetProperty("scope").GetProperty("ledgerBookId").GetGuid().Should().Be(bookId);
        loginCalls[0].Should().Be(1);

        var hiddenRun = await Assert.ThrowsAsync<HttpRequestException>(() => alice.GetJournalAsync("mcp-company-b"));
        hiddenRun.StatusCode.Should().Be(HttpStatusCode.NotFound);
        loginCalls[0].Should().Be(1, "a scope refusal must never sign in as another identity");

        using var bobHttp = app.GetTestClient();
        bobHttp.BaseAddress = http.BaseAddress;
        var bob = new LedgerTools(new LedgerApiClient(bobHttp, new("bob", McpFixturePassword, apiKey: McpFixtureApiKey)));
        using var ownRun = JsonDocument.Parse(await bob.GetJournalAsync("mcp-company-b"));
        ownRun.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        var foreignBook = await Assert.ThrowsAsync<HttpRequestException>(() => bob.GetPostedRunningBalanceAsync(periodId, "Cash"));
        foreignBook.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        loginCalls[0].Should().Be(2);

        // An API key, including caller-supplied scope labels, still cannot manufacture a session.
        using var keyOnly = app.GetTestClient();
        keyOnly.BaseAddress = http.BaseAddress;
        keyOnly.DefaultRequestHeaders.Add("X-Api-Key", McpFixtureApiKey);
        keyOnly.DefaultRequestHeaders.Add("X-Tenant-Id", "company-a");
        keyOnly.DefaultRequestHeaders.Add("X-Company-Id", "company-a");
        (await keyOnly.GetAsync("/api/workstation/runs/mcp-company-a/ledger/entries")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await keyOnly.GetAsync($"/api/ledger/periods/{periodId:D}/accounts/running-balance?account=Cash")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpLedgerAuthentication_IssuedSessionWorksAndRevocationRefusesWithoutCredentialFallback()
    {
        var loginCalls = new int[1];
        await using var app = await CreateMcpAuthenticationHostAsync(Guid.NewGuid(), Guid.NewGuid(), loginCalls);
        var sessions = app.Services.GetRequiredService<LoginSessionService>();
        var token = sessions.CreateSession("alice", McpFixturePassword)!;
        using var http = app.GetTestClient();
        http.BaseAddress = new Uri("https://localhost/");
        var tools = new LedgerTools(new LedgerApiClient(http, new(sessionToken: token)));
        (await tools.GetAccountsAsync("mcp-company-a")).Should().Contain("Cash");
        sessions.RevokeSessionsForUser("alice");
        var expired = await Assert.ThrowsAsync<HttpRequestException>(() => tools.GetAccountsAsync("mcp-company-a"));
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        expired.Message.Should().NotContain(token);
        loginCalls[0].Should().Be(0);
    }

    [Fact]
    public async Task McpLedgerAuthentication_ConcurrentReadsRenewRevokedSessionOnce_AndRejectBadCredentials()
    {
        var loginCalls = new int[1];
        await using var app = await CreateMcpAuthenticationHostAsync(Guid.NewGuid(), Guid.NewGuid(), loginCalls);
        using var http = app.GetTestClient();
        http.BaseAddress = new Uri("https://localhost/");
        var tools = new LedgerTools(new LedgerApiClient(http, new("alice", McpFixturePassword)));
        await tools.GetAccountsAsync("mcp-company-a");
        app.Services.GetRequiredService<LoginSessionService>().RevokeSessionsForUser("alice");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => tools.GetAccountsAsync("mcp-company-a")));
        loginCalls[0].Should().Be(2);
        using var wrongHttp = app.GetTestClient();
        wrongHttp.BaseAddress = http.BaseAddress;
        var wrong = new LedgerTools(new LedgerApiClient(wrongHttp, new("alice", "incorrect-synthetic-password")));
        var denied = await Assert.ThrowsAsync<HttpRequestException>(() => wrong.GetAccountsAsync("mcp-company-a"));
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        denied.Message.Should().NotContain("incorrect-synthetic-password");
        loginCalls[0].Should().Be(3);
    }

    [Fact]
    public async Task McpLedgerAuthentication_RefusesSecureCookieOverHttpWithoutDowngradingServerPolicy()
    {
        var loginCalls = new int[1];
        await using var app = await CreateMcpAuthenticationHostAsync(Guid.NewGuid(), Guid.NewGuid(), loginCalls);
        using var http = app.GetTestClient();
        var tools = new LedgerTools(new LedgerApiClient(http, new("alice", McpFixturePassword)));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => tools.GetAccountsAsync("mcp-company-a"));
        refused.Message.Should().Contain("Secure session cookie").And.Contain("HTTPS");
        refused.Message.Should().NotContain(McpFixturePassword);
        loginCalls[0].Should().Be(1);
    }

    private static async Task<WebApplication> CreateMcpAuthenticationHostAsync(Guid bookId, Guid periodId,
        int[] loginCalls, bool requireApiKey = false, bool localWorkstation = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.DeclareMeridianDeploymentPosture(localWorkstation
            ? MeridianDeploymentPosture.LocalWorkstation : MeridianDeploymentPosture.ProductionApi);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MDC_AUTH_MODE"] = "required",
            ["MDC_API_KEY"] = requireApiKey ? McpFixtureApiKey : null,
            ["MDC_API_KEY_ROLE"] = "Admin",
            ["MDC_USERS"] = $$"""
                [{"username":"alice","passwordHash":"{{McpFixturePasswordHash}}","role":"Admin","companyId":"company-a"},
                 {"username":"bob","passwordHash":"{{McpFixturePasswordHash}}","role":"Admin","companyId":"company-b"}]
                """
        }).Build();
        var authentication = new AuthenticationConfiguration(settings);
        builder.Services.AddSingleton(authentication);
        builder.Services.AddSingleton(new UserProfileRegistry(null, null, TenantScopeEnforcementOptions.FailClosed, authentication));
        builder.Services.AddSingleton<LoginSessionService>();
        builder.Services.AddSingleton(Mock.Of<IRolePermissionProfileStore>());
        builder.Services.AddSingleton(Mock.Of<IUserAccountStore>());
        builder.Services.AddSingleton(Mock.Of<IScopedAccessAssignmentService>());
        RegisterRunReadServices(builder.Services);
        builder.Services.AddSingleton<IFundProfileTenancyRegistry>(new TestCurrentScopeFundProfileTenancyRegistry("company-a", "company-a"));
        var book = new LedgerBookDto(bookId, "mcp-owned-fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Synthetic MCP book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var books = new Mock<ILedgerBookService>();
        books.Setup(service => service.GetBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        builder.Services.AddSingleton(books.Object);
        var source = BuildReconciliationReadyRun("mcp-posted-source");
        var records = source.Metrics!.Ledger.Journal.Select((entry, index) => new LedgerJournalEntryRecord(entry,
            bookId, periodId, null, null, index + 1, entry.Timestamp)).ToArray();
        var period = new LedgerAccountingPeriod(periodId, bookId, 2026, 3, "Synthetic March", new(2026, 3, 1), new(2026, 3, 31),
            "Open", source.StartedAt, null, 1);
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(store => store.GetPeriodAsync(periodId, It.IsAny<CancellationToken>())).ReturnsAsync(period);
        journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(records);
        builder.Services.AddSingleton(journals.Object);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            // TestServer has no socket. Supply the real loopback connection facts required by
            // LocalWorkstation's existing cookie policy; never change that policy in the client.
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            if (context.Request.Path == "/api/auth/login")
                Interlocked.Increment(ref loginCalls[0]);
            await next();
        });
        app.UseLoginSessionAuthentication();
        app.UseApiKeyAuthentication();
        app.UseCookieCsrfProtection();
        app.MapAuthEndpoints();
        app.MapWorkstationEndpoints(ServerJsonOptions);
        app.MapLedgerEndpoints(ServerJsonOptions);
        await app.StartAsync();
        var repository = app.Services.GetRequiredService<IStrategyRepository>();
        foreach (var company in new[] { "company-a", "company-b" })
        {
            var run = BuildReconciliationReadyRun("mcp-" + company) with
            { ParameterSet = new Dictionary<string, string> { ["workstationTenantId"] = company, ["workstationCompanyId"] = company } };
            await repository.RecordRunAsync(run);
        }
        return app;
    }
}
