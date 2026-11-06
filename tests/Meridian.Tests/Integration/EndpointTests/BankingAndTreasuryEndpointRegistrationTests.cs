using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Regression coverage for the banking, ingestion-job and money-market-fund endpoint groups.
/// They were defined but never mapped, so the browser payment-approvals screen received 404s
/// (its front-end tests mock fetch and could not notice). These tests run against the composed
/// application so an unmapped group fails here.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankingAndTreasuryEndpointRegistrationTests : IClassFixture<EndpointTestFixture>
{
    private static readonly string EntityId = Guid.NewGuid().ToString("D");
    private readonly EndpointTestFixture _fixture;

    public BankingAndTreasuryEndpointRegistrationTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("GET /api/banking/payments/pending")]
    [InlineData("POST /api/banking/payments")]
    [InlineData("POST /api/banking/payments/{pendingPaymentId:guid}/approve")]
    [InlineData("POST /api/banking/payments/{pendingPaymentId:guid}/reject")]
    [InlineData("GET /api/banking/transactions")]
    [InlineData("POST /api/dev/seed/bank-transactions")]
    [InlineData("GET /api/ingestion/jobs")]
    [InlineData("POST /api/ingestion/jobs")]
    [InlineData("GET /api/ingestion/summary")]
    [InlineData("GET /api/security-master/money-market-funds/{securityId:guid}")]
    [InlineData("POST /api/security-master/money-market-funds/{securityId:guid}/rebuild")]
    public void Route_IsMapped_AndDeclaresAuthorization(string routeKey)
    {
        var endpoint = FindEndpoint(routeKey);

        endpoint.Should().NotBeNull($"{routeKey} must be mapped by MapUiEndpoints");
        endpoint!.Metadata.GetMetadata<EndpointAuthorizationMetadata>()
            .Should().NotBeNull($"{routeKey} must declare its permission requirement");
    }

    [Fact]
    public async Task PendingPayments_UsedByPaymentApprovalsScreen_IsNotNotFound()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewDirectLending);

        var response = await client.GetAsync($"/api/banking/payments/pending?entityId={EntityId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BankTransactions_UsedByPaymentApprovalsScreen_IsNotNotFound()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewDirectLending);

        var response = await client.GetAsync($"/api/banking/transactions?entityId={EntityId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PendingPayments_WithoutPermission_IsRejected()
    {
        using var client = _fixture.CreatePermittedClient();

        var response = await client.GetAsync($"/api/banking/payments/pending?entityId={EntityId}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InitiatePayment_WithTypeMismatchedBody_ReturnsBadRequestNot500()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ManageDirectLending);
        using var content = new StringContent(
            """{"amount":"not-a-number","effectiveDate":"2026-11-06"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync($"/api/banking/payments?entityId={EntityId}", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("POST /api/ingestion/jobs/{jobId}/transition")]
    [InlineData("DELETE /api/ingestion/jobs/{jobId}")]
    public void RawIngestionStateChangeRoutes_AreNotMapped(string routeKey)
    {
        // Operator state changes go through the governed workstation action route, which
        // validates the transition, requires a rationale and idempotency key, and retains evidence.
        FindEndpoint(routeKey).Should().BeNull();
    }

    [Fact]
    public void MoneyMarketFundSearch_IsDeclaredNonMutating()
    {
        // Read-only roles (Analysis, Executive) hold ViewSecurityMaster; the POST search must not be
        // refused by the read-only-role mutation cap.
        FindEndpoint("POST /api/security-master/money-market-funds/search")!
            .Metadata.GetMetadata<EndpointNonMutatingMetadata>().Should().NotBeNull();
    }

    [Fact]
    public async Task IngestionJobs_WithoutPermission_IsRejected()
    {
        using var client = _fixture.CreatePermittedClient();

        var response = await client.GetAsync("/api/ingestion/jobs");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MoneyMarketFundRebuild_WithViewOnlyPermission_IsForbidden()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewSecurityMaster);

        var response = await client.PostAsync(
            $"/api/security-master/money-market-funds/{Guid.NewGuid():D}/rebuild",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void DevSeedRoute_IsOnlyMappedInDevelopmentAndTestHosts(string environmentName, bool expected)
    {
        var environment = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
        {
            EnvironmentName = environmentName
        };

        BankingEndpoints.IsSeedEnvironment(environment).Should().Be(expected);
    }

    private RouteEndpoint? FindEndpoint(string routeKey)
    {
        var separator = routeKey.IndexOf(' ');
        var method = routeKey[..separator];
        var pattern = routeKey[(separator + 1)..].TrimStart('/');

        return _fixture.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .FirstOrDefault(endpoint =>
                string.Equals((endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'), pattern, StringComparison.OrdinalIgnoreCase)
                && (endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
                    .Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));
    }
}
