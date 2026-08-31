using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests proving the initial-account bootstrap surface stays reachable while
/// authentication is required but no account exists yet.
///
/// Regression coverage for the fail-closed ordering bug where
/// <see cref="Meridian.Ui.Shared.Endpoints.LoginSessionMiddleware"/> returned 503 from the
/// unconfigured branch before the /setup/account and /api/auth/bootstrap exemptions ran,
/// so a fresh install could never create its first account.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class InitialAccountBootstrapEndpointTests : EndpointIntegrationTestBase
{
    private const string BootstrapToken = "bootstrap-integration-test-token";

    public InitialAccountBootstrapEndpointTests(EndpointTestFixture fixture) : base(fixture) { }

    [Fact]
    public async Task BootstrapSurface_WhenAuthRequiredAndNoAccounts_CompletesFirstAccountCreation()
    {
        var originalAuthMode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        var originalToken = Environment.GetEnvironmentVariable("MDC_BOOTSTRAP_TOKEN");
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "required");
        Environment.SetEnvironmentVariable("MDC_BOOTSTRAP_TOKEN", BootstrapToken);
        try
        {
            // Any other route stays fail-closed while unconfigured.
            var status = await GetAsync("/api/status");
            status.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            // The setup page is reachable, not 503, while no account exists.
            var page = await GetAsync("/setup/account");
            page.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await page.Content.ReadAsStringAsync();
            html.Should().Contain("Create your Meridian login");

            // The bootstrap API is reachable and completes first-account creation.
            using var noRedirectClient = Fixture.CreateNoRedirectClient();
            var payload = new { token = BootstrapToken, username = "first-admin", password = "correct-horse-battery" };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await noRedirectClient.PostAsync("/api/auth/bootstrap", content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("/workstation/setup");
            response.Headers
                .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .Should().ContainMatch("*mdc-session*");

            // Once the first account exists the setup surface withdraws itself.
            var pageAfter = await noRedirectClient.GetAsync("/setup/account");
            pageAfter.StatusCode.Should().Be(HttpStatusCode.Redirect);
            pageAfter.Headers.Location?.ToString().Should().Be("/login");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_AUTH_MODE", originalAuthMode);
            Environment.SetEnvironmentVariable("MDC_BOOTSTRAP_TOKEN", originalToken);
        }
    }

    [Fact]
    public async Task BootstrapApi_WithInvalidToken_FailsClosedAtTheEndpointNotTheMiddleware()
    {
        var originalAuthMode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        var originalToken = Environment.GetEnvironmentVariable("MDC_BOOTSTRAP_TOKEN");
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "required");
        Environment.SetEnvironmentVariable("MDC_BOOTSTRAP_TOKEN", BootstrapToken);
        try
        {
            var payload = new { token = "wrong-token", username = "intruder", password = "long-enough-password" };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await Client.PostAsync("/api/auth/bootstrap", content);

            // Reachable (no 503 from the unconfigured branch) but refused by the
            // endpoint's own token gate.
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_AUTH_MODE", originalAuthMode);
            Environment.SetEnvironmentVariable("MDC_BOOTSTRAP_TOKEN", originalToken);
        }
    }
}
