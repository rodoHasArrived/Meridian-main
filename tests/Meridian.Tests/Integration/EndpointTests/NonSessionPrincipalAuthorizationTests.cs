using System.Net;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// The governed surface resolves permissions from the session items, so any authentication posture
/// that authenticates a caller without populating them refuses every declared route. Two such
/// postures exist beside the browser session -- the out-of-band API key and optional mode, where a
/// deployment has no accounts at all -- and both silently lost access to roughly two hundred routes
/// as this wave declared them, because the only coverage either had pointed at routes that were
/// still undeclared.
/// <para>
/// These tests pair each posture with a route that genuinely carries a permission, so the class of
/// regression is caught at the first declaration rather than the two-hundredth.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class NonSessionPrincipalAuthorizationTests : EndpointIntegrationTestBase
{
    // Declared ViewMarketData when the live-data family was gated; the quote stream's polling
    // fallback, and so exactly the kind of route an out-of-band script calls.
    private const string DeclaredRoute = "/api/data/quotes-snapshot";

    public NonSessionPrincipalAuthorizationTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ValidApiKey_ReachesADeclaredRoute()
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "declared-route-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.TradeDesk));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeclaredRoute);
            request.Headers.Add("X-Api-Key", "declared-route-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().NotBe(
                HttpStatusCode.Unauthorized,
                "a validated key carries its configured role, so the permission filter must resolve");
            response.StatusCode.Should().NotBe(
                HttpStatusCode.Forbidden,
                "TradeDesk holds ViewMarketData, which is what this route declares");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task ApiKeyWithANarrowerRole_IsRefusedByThePermissionRatherThanTheKey()
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "narrow-role-key");
        // Accounting holds no ViewMarketData, so the key authenticates but the route still refuses.
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", nameof(UserRole.Accounting));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeclaredRoute);
            request.Headers.Add("X-Api-Key", "narrow-role-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                "the key is valid, so this is an authorization refusal and not an authentication one -- "
                + "which is what makes MDC_API_KEY_ROLE a real control rather than a formality");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }

    [Fact]
    public async Task ApiKeyWithAnUnknownRole_FailsClosed()
    {
        var original = Environment.GetEnvironmentVariable("MDC_API_KEY");
        var originalRole = Environment.GetEnvironmentVariable("MDC_API_KEY_ROLE");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "bad-role-key");
        Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", "NotARole");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeclaredRoute);
            request.Headers.Add("X-Api-Key", "bad-role-key");

            using var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                "a misconfigured role must surface rather than quietly applying a different permission set");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", original);
            Environment.SetEnvironmentVariable("MDC_API_KEY_ROLE", originalRole);
        }
    }
}
