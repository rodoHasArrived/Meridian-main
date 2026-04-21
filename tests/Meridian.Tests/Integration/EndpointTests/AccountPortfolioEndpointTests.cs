using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Contract tests for the new account-level and aggregate portfolio REST endpoints
/// introduced in Phase 3.
///
/// Endpoint set under test:
///   GET /api/execution/accounts
///   GET /api/execution/accounts/{accountId}
///   GET /api/execution/accounts/{accountId}/positions
///   GET /api/execution/portfolio/aggregate
/// </summary>
public sealed class AccountPortfolioEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    // ─── GET /api/execution/accounts ─────────────────────────────────────────

    [Fact]
    public async Task GetAccounts_WhenPortfolioNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(services => { /* intentionally empty */ });

        var response = await app.GetTestClient().GetAsync("/api/execution/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetAccounts_WithRegisteredPortfolio_ReturnsAccountList()
    {
        var portfolio = BuildMultiAccountPortfolio();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IMultiAccountPortfolioState>(portfolio);
        });

        var response = await app.GetTestClient().GetAsync("/api/execution/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var accounts = JsonSerializer.Deserialize<List<AccountSummaryDto>>(body, JsonOpts);

        accounts.Should().HaveCount(2);
        accounts!.Select(a => a.AccountId).Should().Contain(["broker-1", "cash-1"]);
    }

    // ─── GET /api/execution/accounts/{accountId} ──────────────────────────────

    [Fact]
    public async Task GetAccount_UnknownId_Returns404()
    {
        var portfolio = BuildMultiAccountPortfolio();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IMultiAccountPortfolioState>(portfolio);
        });

        var response = await app.GetTestClient().GetAsync("/api/execution/accounts/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAccount_KnownId_ReturnsAccountDetail()
    {
        var portfolio = BuildMultiAccountPortfolio();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IMultiAccountPortfolioState>(portfolio);
        });

        var response = await app.GetTestClient().GetAsync("/api/execution/accounts/broker-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<AccountDetailDto>(body, JsonOpts);

        detail.Should().NotBeNull();
        detail!.AccountId.Should().Be("broker-1");
        detail.Kind.Should().Be(nameof(AccountKind.Brokerage));
    }

    // ─── GET /api/execution/accounts/{accountId}/positions ───────────────────

    [Fact]
    public async Task GetAccountPositions_KnownId_ReturnsPositionList()
    {
        var portfolio = BuildMultiAccountPortfolio();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IMultiAccountPortfolioState>(portfolio);
        });

        var response = await app.GetTestClient().GetAsync("/api/execution/accounts/broker-1/positions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── GET /api/execution/portfolio/aggregate ───────────────────────────────

    [Fact]
    public async Task GetAggregatePortfolio_WhenPortfolioNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(services => { /* intentionally empty */ });

        var response = await app.GetTestClient().GetAsync("/api/execution/portfolio/aggregate");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetAggregatePortfolio_WithPortfolio_ReturnsSnapshot()
    {
        var portfolio = BuildMultiAccountPortfolio();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IMultiAccountPortfolioState>(portfolio);
        });

        var response = await app.GetTestClient().GetAsync("/api/execution/portfolio/aggregate");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static PaperTradingPortfolio BuildMultiAccountPortfolio()
    {
        return new PaperTradingPortfolio(
        [
            new AccountDefinition("broker-1", "IB Account", AccountKind.Brokerage, 100_000m),
            new AccountDefinition("cash-1",   "Cash Sweep",  AccountKind.Bank,       20_000m),
        ]);
    }

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        configure?.Invoke(builder.Services);

        // ExecutionEndpoints resolves IPortfolioState; tests register IMultiAccountPortfolioState.
        // Forward IPortfolioState → IMultiAccountPortfolioState when the latter was registered.
        if (builder.Services.Any(sd => sd.ServiceType == typeof(IMultiAccountPortfolioState)))
        {
            builder.Services.AddSingleton<IPortfolioState>(
                sp => sp.GetRequiredService<IMultiAccountPortfolioState>());
        }

        var app = builder.Build();
        app.MapExecutionEndpoints(JsonOpts);

        await app.StartAsync();
        return app;
    }

    // Local projection DTOs — mirrors the endpoint response shape.
    private sealed class AccountSummaryDto
    {
        public string AccountId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Kind { get; set; }
    }

    private sealed class AccountDetailDto
    {
        public string AccountId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Kind { get; set; }
        public decimal Cash { get; set; }
    }
}
