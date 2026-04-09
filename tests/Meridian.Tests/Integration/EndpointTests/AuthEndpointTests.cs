using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
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
    public async Task LoginPage_WithErrorQueryParam_ContainsErrorMessage()
    {
        var response = await GetAsync("/login?error=1");
        var html = await response.Content.ReadAsStringAsync();

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
        // MDC_USERNAME / MDC_PASSWORD are not set → CreateSession always returns null
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

        // No MDC_USERNAME/MDC_PASSWORD set → credentials rejected → redirect to login with error
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
            body.Should().Contain("Authentication is required but not configured");
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
            body.Should().Contain("Authentication is required but not configured");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_AUTH_MODE", originalAuthMode);
        }
    }

    [Fact]
    public async Task ApiKeyMiddleware_DoesNotAcceptQueryStringApiKey()
    {
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
            Environment.SetEnvironmentVariable("MDC_API_KEY", null);
        }
    }

    [Fact]
    public async Task ApiKeyMiddleware_AcceptsHeaderApiKey()
    {
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
            Environment.SetEnvironmentVariable("MDC_API_KEY", null);
        }
    }
}
