using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for the authentication endpoints:
/// GET /login, POST /api/auth/login, POST /api/auth/logout.
///
/// The test fixture runs under the Test environment, where authentication defaults
/// to optional unless MDC_AUTH_MODE overrides it. This lets us verify endpoint
/// reachability and input validation without requiring real credentials.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class AuthEndpointTests : EndpointIntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };
    private const string TestPasswordHash = "pbkdf2-sha256$210000$oOQU8zfLm/Pzwrl8VZlatQ==$ePPcBmch9qAIfhbablmoBT/tKPGb/TKmFBHlFWKV1uU=";

    public AuthEndpointTests(EndpointTestFixture fixture) : base(fixture) { }

    // ================================================================
    // GET /login
    // ================================================================

    [Fact]
    public async Task LoginPage_ReturnsHtml()
    {
        var response = await GetAsync("/login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        contentType.Should().Be("text/html");
    }

    [Fact]
    public async Task LoginPage_ContainsSignInForm()
    {
        var response = await GetAsync("/login");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("action=\"/api/auth/login\"");
        html.Should().Contain("name=\"username\"");
        html.Should().Contain("name=\"password\"");
    }

    [Fact]
    public async Task LoginPage_ContainsWorkstationContext()
    {
        var response = await GetAsync("/login");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Operator workstation");
        html.Should().Contain("Web workstation");
        html.Should().Contain("Session required");
        html.Should().Contain("MDC_USERNAME");
        html.Should().Contain("MDC_PASSWORD_HASH");
    }

    [Fact]
    public async Task LoginPage_WithErrorQueryParam_ContainsErrorMessage()
    {
        var response = await GetAsync("/login?error=1");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("role=\"alert\"");
        html.Should().Contain("Sign-in failed");
        html.Should().Contain("Invalid username or password");
    }

    [Fact]
    public async Task LoginPage_WithoutErrorQueryParam_DoesNotContainErrorMessage()
    {
        var response = await GetAsync("/login");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().NotContain("class=\"login-error\"");
    }

    // ================================================================
    // POST /api/auth/login  (JSON content type)
    // ================================================================

    [Fact]
    public async Task LoginJson_WithEmptyBody_ReturnsBadRequest()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LoginJson_WithNullUsername_ReturnsBadRequest()
    {
        var payload = new { Username = (string?)null, Password = "secret" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LoginJson_WithNullPassword_ReturnsBadRequest()
    {
        var payload = new { Username = "admin", Password = (string?)null };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LoginJson_WithWrongCredentials_ReturnsUnauthorized()
    {
        // MDC_USERNAME / MDC_PASSWORD_HASH are not set, so CreateSession always returns null.
        var payload = new { Username = "admin", Password = "wrongpassword" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginJson_WithWrongCredentials_ReturnsJsonError()
    {
        var payload = new { Username = "admin", Password = "wrongpassword" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/auth/login", content);

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("error");
    }

    [Fact]
    public async Task LoginJson_WithValidCredentials_UsesSecureCookiesWhenLocalTransportIsUnproven()
    {
        var originalUsername = Environment.GetEnvironmentVariable("MDC_USERNAME");
        var originalPasswordHash = Environment.GetEnvironmentVariable("MDC_PASSWORD_HASH");
        Environment.SetEnvironmentVariable("MDC_USERNAME", "test-admin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", TestPasswordHash);

        try
        {
            var payload = new { Username = "test-admin", Password = "test-password" };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await Client.PostAsync("/api/auth/login", content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var setCookies = response.Headers
                .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .ToList();

            var sessionCookie = setCookies.Single(
                cookie => cookie.Contains("mdc-session=", StringComparison.OrdinalIgnoreCase));
            var csrfCookie = setCookies.Single(
                cookie => cookie.Contains("mdc-csrf=", StringComparison.OrdinalIgnoreCase));

            sessionCookie.Split(';').Should().Contain(
                attribute => attribute.Trim().Equals("Secure", StringComparison.OrdinalIgnoreCase));
            csrfCookie.Split(';').Should().Contain(
                attribute => attribute.Trim().Equals("Secure", StringComparison.OrdinalIgnoreCase));
            setCookies.Should().Contain(cookie => cookie.Contains("SameSite=Strict", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERNAME", originalUsername);
            Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", originalPasswordHash);
        }
    }

    // ================================================================
    // POST /api/auth/login  (form content type)
    // ================================================================

    [Fact]
    public async Task LoginForm_WithEmptyCredentials_RedirectsToLoginWithError()
    {
        // Use a client that does NOT follow redirects so we can inspect the Location header
        using var noRedirectClient = Fixture.CreateNoRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "",
            ["password"] = ""
        });
        var response = await noRedirectClient.PostAsync("/api/auth/login", form);

        // Empty credentials → redirect back to /login?error=1
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().StartWith("/login");
        response.Headers.Location?.ToString().Should().Contain("error");
    }

    [Fact]
    public async Task LoginForm_WithCredentials_NoEnvVarsConfigured_RedirectsToLoginWithError()
    {
        using var noRedirectClient = Fixture.CreateNoRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret"
        });
        var response = await noRedirectClient.PostAsync("/api/auth/login", form);

        // No MDC_USERNAME/MDC_PASSWORD_HASH set, so credentials are rejected and redirect to login with error.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Contain("error");
    }

    // ================================================================
    // POST /api/auth/logout
    // ================================================================

    [Fact]
    public async Task Logout_WithoutSession_RedirectsToLoginPage()
    {
        using var noRedirectClient = Fixture.CreateNoRedirectClient();

        var response = await noRedirectClient.PostAsync("/api/auth/logout", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/login");
    }

    [Fact]
    public async Task Logout_ClearsCookie()
    {
        using var noRedirectClient = Fixture.CreateNoRedirectClient();

        var response = await noRedirectClient.PostAsync("/api/auth/logout", content: null);

        // The Set-Cookie header should contain the session cookie name with an expired/empty value
        var setCookie = response.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        // Either no cookie is set (nothing to clear) or it contains the session cookie name
        if (setCookie.Count > 0)
        {
            setCookie.Should().ContainMatch("*mdc-session*");
        }
    }

    // ================================================================
    // Middleware passthrough when no credentials configured
    // ================================================================

    [Fact]
    public async Task ProtectedEndpoint_WhenNoCredentialsConfigured_PassesThrough()
    {
        var response = await GetAsync("/api/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Root_WhenNoCredentialsConfigured_ReturnsNotFound()
    {
        var response = await GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProtectedEndpoint_WhenAuthModeRequiredAndCredentialsMissing_ReturnsServiceUnavailable()
    {
        var originalAuthMode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "required");
        try
        {
            var response = await GetAsync("/api/status");

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("Authentication is required but is not configured");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_AUTH_MODE", originalAuthMode);
        }
    }

    [Fact]
    public async Task LoginJson_WhenAuthModeRequiredAndCredentialsMissing_ReturnsServiceUnavailable()
    {
        var originalAuthMode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "required");
        try
        {
            var payload = new { Username = "admin", Password = "secret" };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await Client.PostAsync("/api/auth/login", content);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("Authentication is required but is not configured");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_AUTH_MODE", originalAuthMode);
        }
    }

    [Fact]
    public async Task ApiKeyMiddleware_DoesNotAcceptQueryStringApiKey()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "integration-test-key");
        try
        {
            var response = await GetAsync("/api/status?api_key=integration-test-key");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("X-Api-Key");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalApiKey);
        }
    }

    [Fact]
    public async Task ApiKeyMiddleware_AcceptsHeaderApiKey()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "integration-test-key");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/status");
            request.Headers.Add("X-Api-Key", "integration-test-key");

            var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalApiKey);
        }
    }

    [Fact]
    public async Task ApiKeyMiddleware_MissingHeader_ReturnsUnauthorized()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "integration-test-key");
        try
        {
            var response = await GetAsync("/api/status");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("X-Api-Key");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalApiKey);
        }
    }

    [Fact]
    public async Task ApiKeyMiddleware_WrongKey_ReturnsUnauthorized()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "integration-test-key");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/status");
            request.Headers.Add("X-Api-Key", "not-the-key");

            var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalApiKey);
        }
    }

    [Theory]
    [InlineData("/api/status", true)]
    [InlineData("/apiary/status", false)]
    [InlineData("/workstation/evidence/vault/example", false)]
    public void ApiKeyMiddleware_IsApiKeyCandidate_DefersOnlyApiRequests(
        string path,
        bool expectedCandidate)
    {
        var originalApiKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "integration-test-key");
        try
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Request.Headers["X-Api-Key"] = "attacker-controlled-value";

            ApiKeyMiddleware.IsApiKeyCandidate(context).Should().Be(expectedCandidate);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalApiKey);
        }
    }

    // ================================================================
    // Monitoring exemptions: probes and the metrics scrape stay reachable
    // in authenticated postures (PRD-019)
    // ================================================================

    [Theory]
    [InlineData("/health", true)]
    [InlineData("/health/", true)]
    [InlineData("/HEALTH", true)]
    [InlineData("/healthz", true)]
    [InlineData("/ready", true)]
    [InlineData("/readyz", true)]
    [InlineData("/live", true)]
    [InlineData("/livez", true)]
    [InlineData("/startup", true)]
    [InlineData("/startupz", true)]
    [InlineData("/metrics", true)]
    [InlineData("/api/health", false)]
    [InlineData("/api/health/detailed", false)]
    [InlineData("/health/detailed", false)]
    [InlineData("/metricsz", false)]
    public void MonitoringEndpointExemptions_CoverProbesAndScrapeOnly(string path, bool expected)
        => MonitoringEndpointExemptions.IsExempt(path).Should().Be(expected);

    [Theory]
    [InlineData("/health")]
    [InlineData("/healthz")]
    [InlineData("/ready")]
    [InlineData("/readyz")]
    [InlineData("/live")]
    [InlineData("/livez")]
    [InlineData("/startup")]
    [InlineData("/startupz")]
    [InlineData("/metrics")]
    public async Task MonitoringEndpoint_WhenSessionAuthConfigured_ServesWithoutASession(string path)
    {
        var originalUsername = Environment.GetEnvironmentVariable("MDC_USERNAME");
        var originalPasswordHash = Environment.GetEnvironmentVariable("MDC_PASSWORD_HASH");
        Environment.SetEnvironmentVariable("MDC_USERNAME", "test-admin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", TestPasswordHash);
        try
        {
            using var client = Fixture.CreateNoRedirectClient();

            // Control: session authentication is genuinely enforced while this case runs,
            // so a passing probe below proves the exemption rather than a disabled gate.
            using var control = await client.GetAsync("/api/status");
            control.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var response = await client.GetAsync(path);

            ((int)response.StatusCode).Should().NotBeInRange(300, 399,
                "external monitors reach {0} without a session, so it must serve rather than redirect to /login", path);
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERNAME", originalUsername);
            Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", originalPasswordHash);
        }
    }

    [Fact]
    public async Task HealthAndMetrics_WhenSessionAuthConfigured_ReturnMonitoringPayloads()
    {
        var originalUsername = Environment.GetEnvironmentVariable("MDC_USERNAME");
        var originalPasswordHash = Environment.GetEnvironmentVariable("MDC_PASSWORD_HASH");
        Environment.SetEnvironmentVariable("MDC_USERNAME", "test-admin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", TestPasswordHash);
        try
        {
            using var client = Fixture.CreateNoRedirectClient();

            // The compose healthcheck path: an unauthenticated GET /health must see health
            // truth (200 healthy or 503 unhealthy), never a login redirect masking a 503.
            using var health = await client.GetAsync("/health");
            health.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
            health.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

            // The Prometheus scrape path: an unauthenticated GET /metrics must return the
            // exposition body, not the login page HTML a redirect-following scraper would see.
            using var metrics = await client.GetAsync("/metrics");
            metrics.StatusCode.Should().Be(HttpStatusCode.OK);
            metrics.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
            var exposition = await metrics.Content.ReadAsStringAsync();
            exposition.Should().Contain("mdc_published");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERNAME", originalUsername);
            Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", originalPasswordHash);
        }
    }

    [Fact]
    public async Task HealthAndMetrics_WhenAuthModeRequiredAndCredentialsMissing_StillServe()
    {
        var originalAuthMode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "required");
        try
        {
            using var client = Fixture.CreateNoRedirectClient();

            // Control: the fail-closed configuration error still guards non-exempt routes.
            using var control = await client.GetAsync("/api/status");
            control.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            using var health = await client.GetAsync("/health");
            health.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            var healthBody = await health.Content.ReadAsStringAsync();
            healthBody.Should().NotContain("Authentication is required");

            using var metrics = await client.GetAsync("/metrics");
            metrics.StatusCode.Should().Be(HttpStatusCode.OK);
            metrics.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_AUTH_MODE", originalAuthMode);
        }
    }

    [Fact]
    public async Task ApiKeyMiddleware_SessionAuthenticatedRequest_PassesWithoutApiKey()
    {
        // The browser workstation authenticates with a login session, not an API key.
        // A session-authenticated request (emulated via the fixture's X-Test-Auth marker,
        // which sets the same context items LoginSessionMiddleware sets) must pass the
        // API-key gate even when MDC_API_KEY is configured.
        var originalApiKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        Environment.SetEnvironmentVariable("MDC_API_KEY", "integration-test-key");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/status");
            request.Headers.Add("X-Test-Auth", "directlending-admin");

            var response = await Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_API_KEY", originalApiKey);
        }
    }
}
