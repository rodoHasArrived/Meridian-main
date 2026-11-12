using System.Text.Json;
using System.Text;
using Meridian.Contracts.FixedIncome;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Identity.Auth;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class FixedIncomeHttpPrecisionTests
{
    [Fact]
    public async Task Settlement_BindsExactDecimalStrings_FromBrowserRequest()
    {
        var service = Substitute.For<IBondSettlementConsiderationService>();
        service.PrepareAsync(Arg.Any<BondSettlementConsiderationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BondSettlementPreparationResult(null, [])));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewSecurityMaster;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "precision-test";
            await next();
        });
        app.MapBondAnalyticsEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        var securityId = Guid.NewGuid();
        var body = $$"""
            {"analytics":{"securityId":"{{securityId}}","context":{"asOf":"2026-11-12T00:00:00Z","settlementDate":"2026-11-13"},"cleanPrice":"98.50000000000000000000000001"},
             "originalFace":"9007199254740993.123456789","bookedFactor":"0.9876543210987654321098765432","fees":"0","observedDirtyCash":"98500",
             "sourceEvidenceId":"retained-source","cashAccountId":"cash","accruedInterestAccountId":"interest"}
            """;
        var response = await app.GetTestClient().PostAsync("/api/reference-data/bonds/settlement-consideration",
            new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        await service.Received(1).PrepareAsync(Arg.Is<BondSettlementConsiderationRequest>(request =>
            request.OriginalFace == 9007199254740993.123456789m
            && request.BookedFactor == 0.9876543210987654321098765432m
            && request.Analytics.CleanPrice == 98.50000000000000000000000001m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Analytics_EmitsExactDecimalStrings_WithoutChangingCanonicalSerialization()
    {
        var securityId = Guid.NewGuid();
        var asOf = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        const decimal price = 98.50000000000000000000000001m;
        const decimal rate = 0.0500000000000000000000000001m;
        var result = new BondAnalyticsResult(securityId, "USD", "precision-test",
            new BondCalculationContext(asOf, new DateOnly(2026, 11, 13)),
            BondYieldConvention.NominalPeriodic, price, price, 0m, rate, rate,
            null, null, null, null, null,
            [new BondAnalyticsCashFlow(Guid.NewGuid(), new DateOnly(2027, 5, 13),
                new DateOnly(2026, 11, 13), new DateOnly(2027, 5, 13), rate, 100m, 0.5m)],
            [], [], [], [], []);
        var canonicalOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var before = JsonSerializer.Serialize(result, canonicalOptions);
        var service = Substitute.For<IBondAnalyticsService>();
        service.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BondAnalyticsResult?>(result));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewSecurityMaster;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "precision-test";
            await next();
        });
        app.MapBondAnalyticsEndpoints(canonicalOptions);
        app.MapGet("/precision-legacy", () => Microsoft.AspNetCore.Http.Results.Json(result, canonicalOptions));
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{securityId}/analytics?asOf=2026-11-12T00:00:00Z&settlementDate=2026-11-13&cleanPrice=98.50000000000000000000000001");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("cleanPrice").ValueKind);
        Assert.Equal("98.50000000000000000000000001", json.RootElement.GetProperty("cleanPrice").GetString());
        Assert.Equal("0.0500000000000000000000000001", json.RootElement.GetProperty("yieldToMaturity").GetString());
        Assert.Equal("0.0500000000000000000000000001", json.RootElement.GetProperty("cashFlows")[0].GetProperty("interest").GetString());
        // Exercise the actual shared typed client used by desktop hosts against both wire forms.
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => app.GetTestClient());
        using var sharedClient = new ApiClientService(factory);
        foreach (var path in new[]
        {
            $"/api/reference-data/bonds/{securityId}/analytics?asOf=2026-11-12T00:00:00Z&settlementDate=2026-11-13&cleanPrice=98.50000000000000000000000001",
            "/precision-legacy"
        })
        {
            var typed = await sharedClient.GetWithResponseAsync<BondAnalyticsResult>(path);
            Assert.True(typed.Success, typed.ErrorMessage);
            Assert.Equal(price, typed.Data!.CleanPrice);
            Assert.Equal(rate, typed.Data.YieldToMaturity);
            Assert.Equal(rate, typed.Data.CashFlows[0].Interest);
            Assert.Null(typed.Data.Dv01);
        }
        Assert.Equal(before, JsonSerializer.Serialize(result, DesktopJsonOptions.Api));
        Assert.Equal(before, JsonSerializer.Serialize(result, canonicalOptions));
        using var canonical = JsonDocument.Parse(before);
        Assert.Equal(JsonValueKind.Number, canonical.RootElement.GetProperty("cleanPrice").ValueKind);
    }

    [Fact]
    public void HttpOptions_KeepVersionsNumeric_AndReadExactStringDecimals()
    {
        var canonical = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var http = FixedIncomeHttpJson.Options(canonical);
        var serialized = JsonSerializer.Serialize(new { amount = 9007199254740993.123456789m, version = 17L, rate = (decimal?)null }, http);
        using var json = JsonDocument.Parse(serialized);
        Assert.Equal("9007199254740993.123456789", json.RootElement.GetProperty("amount").GetString());
        Assert.Equal(17, json.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("rate").ValueKind);
        Assert.Equal(9007199254740993.123456789m, JsonSerializer.Deserialize<decimal>("\"9007199254740993.123456789\"", http));
        Assert.Equal(98.5m, JsonSerializer.Deserialize<decimal>("98.5", http));
    }
}
