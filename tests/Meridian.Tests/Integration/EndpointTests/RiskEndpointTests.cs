using System.Net;
using System.Net.Http.Json;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class RiskEndpointTests : EndpointIntegrationTestBase
{
    public RiskEndpointTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GetRiskRules_ReturnsKnownRuleSet()
    {
        var response = await GetAsync("/api/risk/rules");
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
        var before = await GetJsonAsync<RiskRuleConfigDto>("/api/risk/rules/DrawdownCircuitBreaker/config");
        Assert.NotNull(before);

        var updateResponse = await Client.PutAsJsonAsync(
            "/api/risk/rules/DrawdownCircuitBreaker/config",
            new RiskRuleConfigUpdateRequest(MaxDrawdownPercent: 6m, Reason: "test update"));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<RiskRuleConfigDto>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(6m, updated!.MaxDrawdownPercent);
    }

    [Fact]
    public async Task UnknownRiskRule_ReturnsNotFound()
    {
        var response = await GetAsync("/api/risk/rules/unknown/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
