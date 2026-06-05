using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.CryptoCurrency;
using Meridian.Application.Deposits;
using Meridian.Application.Futures;
using Meridian.Identity.Auth;
using Meridian.Contracts.CryptoCurrency;
using Meridian.Contracts.Deposits;
using Meridian.Contracts.Futures;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards against low-privilege reference-data reads leaking operational Security Master details.
/// </summary>
public sealed class ReferenceDataEndpointAuthorizationTests
{
    [Fact]
    public async Task CryptoReference_WhenNoPermissionContext_ReturnsUnauthorizedAndDoesNotQueryService()
    {
        var crypto = new RecordingCryptoReferenceService();
        await using var app = await CreateAppAsync(crypto, new RecordingDepositReferenceService(), new RecordingFutureReferenceService(), permissions: null);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/crypto/by-network?network=ethereum");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        crypto.QueryCount.Should().Be(0);
    }

    [Fact]
    public async Task CryptoReference_WhenUserLacksSecurityMasterRead_ReturnsForbiddenAndDoesNotQueryService()
    {
        var crypto = new RecordingCryptoReferenceService();
        await using var app = await CreateAppAsync(crypto, new RecordingDepositReferenceService(), new RecordingFutureReferenceService(), UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/crypto/by-network?network=ethereum");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        crypto.QueryCount.Should().Be(0);
    }

    [Fact]
    public async Task CryptoReference_WithSecurityMasterReadPermission_ReturnsReferenceRows()
    {
        var crypto = new RecordingCryptoReferenceService();
        await using var app = await CreateAppAsync(crypto, new RecordingDepositReferenceService(), new RecordingFutureReferenceService(), UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/crypto/by-network?network=ethereum");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<CryptoReferenceDto[]>(JsonOptions);
        rows.Should().ContainSingle(row => row.BaseCurrency == "ETH");
        crypto.QueryCount.Should().Be(1);
    }

    [Fact]
    public async Task DepositReference_WhenUserLacksSecurityMasterRead_ReturnsForbiddenAndDoesNotQueryService()
    {
        var deposits = new RecordingDepositReferenceService();
        await using var app = await CreateAppAsync(new RecordingCryptoReferenceService(), deposits, new RecordingFutureReferenceService(), UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/deposits/by-institution?institutionName=TestBank");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deposits.QueryCount.Should().Be(0);
    }

    [Fact]
    public async Task DepositReference_WithSecurityMasterReadPermission_ReturnsReferenceRows()
    {
        var deposits = new RecordingDepositReferenceService();
        await using var app = await CreateAppAsync(new RecordingCryptoReferenceService(), deposits, new RecordingFutureReferenceService(), UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/deposits/by-institution?institutionName=TestBank");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<DepositReferenceDto[]>(JsonOptions);
        rows.Should().ContainSingle(row => row.InstitutionName == "TestBank");
        deposits.QueryCount.Should().Be(1);
    }

    [Fact]
    public async Task FutureReference_WhenUserLacksSecurityMasterRead_ReturnsForbiddenAndDoesNotQueryService()
    {
        var futures = new RecordingFutureReferenceService();
        await using var app = await CreateAppAsync(new RecordingCryptoReferenceService(), new RecordingDepositReferenceService(), futures, UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/futures/front-month?rootSymbol=ES");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        futures.QueryCount.Should().Be(0);
    }

    [Fact]
    public async Task FutureReference_WithSecurityMasterReadPermission_ReturnsFrontMonth()
    {
        var futures = new RecordingFutureReferenceService();
        await using var app = await CreateAppAsync(new RecordingCryptoReferenceService(), new RecordingDepositReferenceService(), futures, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/reference-data/futures/front-month?rootSymbol=ES");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await response.Content.ReadFromJsonAsync<FutureReferenceDto>(JsonOptions);
        row.Should().NotBeNull();
        row!.RootSymbol.Should().Be("ES");
        futures.QueryCount.Should().Be(1);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<WebApplication> CreateAppAsync(
        ICryptoReferenceService cryptoService,
        IDepositReferenceService depositService,
        IFutureReferenceService futureService,
        UserPermission? permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(cryptoService);
        builder.Services.AddSingleton(depositService);
        builder.Services.AddSingleton(futureService);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (permissions is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions.Value;
            }

            await next();
        });
        app.MapCryptoReferenceEndpoints(JsonOptions);
        app.MapDepositReferenceEndpoints(JsonOptions);
        app.MapFutureReferenceEndpoints(JsonOptions);

        await app.StartAsync();
        return app;
    }

    private sealed class RecordingCryptoReferenceService : ICryptoReferenceService
    {
        public int QueryCount { get; private set; }

        public Task<CryptoReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<CryptoReferenceDto?>(CreateCrypto(securityId));
        }

        public Task<IReadOnlyList<CryptoReferenceDto>> GetByNetworkAsync(string network, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<IReadOnlyList<CryptoReferenceDto>>([CreateCrypto(Guid.NewGuid())]);
        }

        public Task<IReadOnlyList<CryptoReferenceDto>> GetByBaseCurrencyAsync(string baseCurrency, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<IReadOnlyList<CryptoReferenceDto>>([CreateCrypto(Guid.NewGuid())]);
        }

        private static CryptoReferenceDto CreateCrypto(Guid securityId) =>
            new(
                securityId,
                "Ethereum",
                "ETH",
                "USD",
                "ethereum",
                "ETH",
                1);
    }

    private sealed class RecordingDepositReferenceService : IDepositReferenceService
    {
        public int QueryCount { get; private set; }

        public Task<DepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<DepositReferenceDto?>(CreateDeposit(securityId));
        }

        public Task<IReadOnlyList<DepositReferenceDto>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<IReadOnlyList<DepositReferenceDto>>([CreateDeposit(Guid.NewGuid())]);
        }

        public Task<IReadOnlyList<DepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<IReadOnlyList<DepositReferenceDto>>([CreateDeposit(Guid.NewGuid())]);
        }

        private static DepositReferenceDto CreateDeposit(Guid securityId) =>
            new(
                securityId,
                "Test Bank Deposit",
                "USD",
                "TermDeposit",
                "TestBank",
                new DateOnly(2026, 12, 31),
                0.045m,
                "ACT/365",
                false,
                "DEP-TEST",
                1);
    }

    private sealed class RecordingFutureReferenceService : IFutureReferenceService
    {
        public int QueryCount { get; private set; }

        public Task<FutureReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<FutureReferenceDto?>(CreateFuture(securityId));
        }

        public Task<IReadOnlyList<FutureReferenceDto>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<IReadOnlyList<FutureReferenceDto>>([CreateFuture(Guid.NewGuid())]);
        }

        public Task<IReadOnlyList<FutureReferenceDto>> GetExpiryLadderAsync(string rootSymbol, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<IReadOnlyList<FutureReferenceDto>>([CreateFuture(Guid.NewGuid())]);
        }

        public Task<FutureReferenceDto?> GetFrontMonthAsync(string rootSymbol, CancellationToken ct = default)
        {
            QueryCount++;
            return Task.FromResult<FutureReferenceDto?>(CreateFuture(Guid.NewGuid()));
        }

        private static FutureReferenceDto CreateFuture(Guid securityId) =>
            new(
                securityId,
                "E-mini S&P 500 Sep 2026",
                "USD",
                "ES",
                "Sep26",
                new DateOnly(2026, 9, 18),
                50m,
                "Cash",
                false,
                5,
                new DateOnly(2026, 9, 18),
                null,
                null,
                null,
                FutureLifecycleStat.Active,
                "ESU6",
                1);
    }
}
