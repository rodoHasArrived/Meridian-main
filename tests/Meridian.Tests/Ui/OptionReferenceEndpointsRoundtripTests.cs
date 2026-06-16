using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Options;
using Meridian.Identity.Auth;
using Meridian.Instruments.Options;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class OptionReferenceEndpointsRoundtripTests
{
    [Fact]
    public async Task ChainImport_Projection_EndpointRoundtrip()
    {
        var projectionStore = new InMemoryProjectionStore();
        var optionService = new OptionProjectionService(projectionStore);

        await using var app = await CreateAppAsync(optionService, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();
        var snapshot = CreateSnapshot("TST", new DateOnly(2026, 12, 19));

        using var importResponse = await client.PostAsJsonAsync("/api/reference-data/options/chains/import", snapshot);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var seriesResponse = await client.GetAsync("/api/reference-data/options/series/TST-20261219?expiryDate=2026-12-19");
        seriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var series = await seriesResponse.Content.ReadFromJsonAsync<OptionSeriesDto>();
        series.Should().NotBeNull();
        series!.Contracts.Should().HaveCount(4);

        using var linkageResponse = await client.GetAsync("/api/reference-data/options/contracts/TST20261219C100/underlying-linkage");
        linkageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var linkage = await linkageResponse.Content.ReadFromJsonAsync<OptionContractReferenceDto>();
        linkage.Should().NotBeNull();
        linkage!.UnderlyingSymbol.Should().Be("TST");
    }


    [Fact]
    public async Task ChainImport_WithoutPermissions_ReturnsForbidden()
    {
        var projectionStore = new InMemoryProjectionStore();
        var optionService = new OptionProjectionService(projectionStore);

        await using var app = await CreateAppAsync(optionService);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/api/reference-data/options/chains/import", CreateSnapshot("TST", new DateOnly(2026, 12, 19)));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChainImport_WithViewOnlyPermission_ReturnsForbidden()
    {
        var projectionStore = new InMemoryProjectionStore();
        var optionService = new OptionProjectionService(projectionStore);

        await using var app = await CreateAppAsync(optionService, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/api/reference-data/options/chains/import", CreateSnapshot("TST", new DateOnly(2026, 12, 19)));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateAppAsync(OptionProjectionService optionService, UserPermission? permissions = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IOptionReferenceService>(optionService);
        builder.Services.AddSingleton<IOptionChainImportService>(optionService);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (permissions.HasValue)
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions.Value;
            }

            await next().ConfigureAwait(false);
        });
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        app.MapOptionReferenceEndpoints(json);
        app.MapOptionChainEndpoints(json);
        await app.StartAsync();
        return app;
    }

    private static OptionChainSnapshot CreateSnapshot(string underlying, DateOnly expiry)
    {
        var strikes = new decimal[] { 100m, 105m };
        return new OptionChainSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            UnderlyingSymbol: underlying,
            UnderlyingPrice: 102m,
            Expiration: expiry,
            Strikes: strikes,
            Calls:
            [
                CreateQuote(underlying, expiry, OptionRight.Call, 100m, "TST20261219C100"),
                CreateQuote(underlying, expiry, OptionRight.Call, 105m, "TST20261219C105")
            ],
            Puts:
            [
                CreateQuote(underlying, expiry, OptionRight.Put, 100m, "TST20261219P100"),
                CreateQuote(underlying, expiry, OptionRight.Put, 105m, "TST20261219P105")
            ]);
    }

    private static OptionQuote CreateQuote(string underlying, DateOnly expiry, OptionRight right, decimal strike, string symbol)
        => new(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Contract: new OptionContractSpec(
                UnderlyingSymbol: underlying,
                Strike: strike,
                Expiration: expiry,
                Right: right,
                OccSymbol: symbol),
            BidPrice: 1.00m,
            BidSize: 10,
            AskPrice: 1.20m,
            AskSize: 12,
            UnderlyingPrice: 102m);

    private sealed class InMemoryProjectionStore : IOptionReferenceProjectionStore
    {
        private readonly Dictionary<string, OptionContractProjectionRow> _rows =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<OptionContractProjectionRow?> GetContractAsync(string contractSymbol, CancellationToken ct = default)
        {
            _rows.TryGetValue(contractSymbol, out var row);
            return Task.FromResult(row);
        }

        public Task<IReadOnlyList<OptionContractProjectionRow>> GetSeriesContractsAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OptionContractProjectionRow>>(
                _rows.Values
                    .Where(row => string.Equals(row.OptionChainId, optionChainId, StringComparison.OrdinalIgnoreCase) && row.ExpiryDate == expiryDate)
                    .OrderBy(row => row.Strike)
                    .ThenBy(row => row.PutCall, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        public Task<IReadOnlyList<OptionContractProjectionRow>> GetUnderlyingContractsAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OptionContractProjectionRow>>(
                _rows.Values
                    .Where(row => string.Equals(row.UnderlyingSymbol, underlyingSymbol, StringComparison.OrdinalIgnoreCase) && row.ExpiryDate == expiryDate)
                    .ToArray());

        public Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DateOnly>>(
                _rows.Values
                    .Where(row => string.Equals(row.UnderlyingSymbol, underlyingSymbol, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.ExpiryDate)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToArray());

        public Task UpsertChainSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default)
        {
            var optionChainId = $"{snapshot.UnderlyingSymbol.Trim().ToUpperInvariant()}-{snapshot.Expiration:yyyyMMdd}";
            UpsertSide(snapshot, optionChainId, snapshot.Calls, OptionRight.Call);
            UpsertSide(snapshot, optionChainId, snapshot.Puts, OptionRight.Put);
            return Task.CompletedTask;
        }

        private void UpsertSide(OptionChainSnapshot snapshot, string optionChainId, IReadOnlyList<OptionQuote> quotes, OptionRight right)
        {
            foreach (var quote in quotes)
            {
                var symbol = quote.Symbol.Trim().ToUpperInvariant();
                _rows[symbol] = new OptionContractProjectionRow(
                    symbol,
                    null,
                    optionChainId,
                    snapshot.UnderlyingSymbol.Trim().ToUpperInvariant(),
                    null,
                    right.ToString(),
                    quote.Contract.Strike,
                    quote.Contract.Expiration,
                    quote.Contract.Multiplier,
                    false,
                    snapshot.Expiration,
                    "Active",
                    snapshot.Timestamp,
                    _rows.TryGetValue(symbol, out var existing) ? existing.Version + 1 : 1);
            }
        }
    }
}
