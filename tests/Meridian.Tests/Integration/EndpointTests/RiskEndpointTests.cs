using System.Net;
using System.Net.Http.Json;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class RiskEndpointTests : EndpointIntegrationTestBase, IDisposable
{
    private readonly HttpClient _riskClient;

    public RiskEndpointTests(EndpointTestFixture fixture)
        : base(fixture)
    {
        _riskClient = fixture.CreatePermittedClient(
            UserPermission.ViewTrades,
            UserPermission.ManageOrders);
    }

    public void Dispose() => _riskClient.Dispose();

    [Fact]
    public void RuntimeOptions_UseFixturePrivateDataRoot()
    {
        var options = Fixture.Services.GetRequiredService<RiskRuleRuntimeOptions>();

        Assert.Equal(Path.Combine(Fixture.DataRoot, "risk-rules.json"), options.SnapshotPath);
    }

    [Fact]
    public async Task GetRiskRules_ReturnsKnownRuleSet()
    {
        var response = await _riskClient.GetAsync("/api/risk/rules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<RiskRuleStatusDto>>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Contains(payload!, rule => string.Equals(rule.RuleName, "PositionLimit", StringComparison.Ordinal));
        Assert.Contains(payload!, rule => string.Equals(rule.RuleName, "DrawdownCircuitBreaker", StringComparison.Ordinal));
        Assert.Contains(payload!, rule => string.Equals(rule.RuleName, "OrderRateThrottle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DrawdownConfig_CanBeReadAndUpdated()
    {
        var beforeResponse = await _riskClient.GetAsync("/api/risk/rules/DrawdownCircuitBreaker/config");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        var before = await beforeResponse.Content.ReadFromJsonAsync<RiskRuleConfigDto>(JsonOptions);
        Assert.NotNull(before);

        try
        {
            var updateResponse = await _riskClient.PutAsJsonAsync(
                "/api/risk/rules/DrawdownCircuitBreaker/config",
                new RiskRuleConfigUpdateRequest(MaxDrawdownPercent: 6m, Reason: "test update"));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var updated = await updateResponse.Content.ReadFromJsonAsync<RiskRuleConfigDto>(JsonOptions);
            Assert.NotNull(updated);
            Assert.Equal(6m, updated!.MaxDrawdownPercent);
        }
        finally
        {
            if (before!.MaxDrawdownPercent is { } originalMaxDrawdownPercent)
            {
                var restoreResponse = await _riskClient.PutAsJsonAsync(
                    "/api/risk/rules/DrawdownCircuitBreaker/config",
                    new RiskRuleConfigUpdateRequest(
                        MaxDrawdownPercent: originalMaxDrawdownPercent,
                        Reason: "restore endpoint test fixture state"));
                Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
            }
        }
    }

    [Fact]
    public async Task UnknownRiskRule_ReturnsNotFound()
    {
        var response = await _riskClient.GetAsync("/api/risk/rules/unknown/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
