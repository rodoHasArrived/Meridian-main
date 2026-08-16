using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for configuration API endpoints.
/// Tests GET/POST/DELETE operations on /api/config/*.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ConfigEndpointTests : IClassFixture<EndpointTestFixture>
{
    private const string ConfigPasswordHash = "pbkdf2-sha256$210000$MZbfWqYODb9fl/pT/2g2Wg==$hsDcSOJ5uPYBFGsUp2lD6DhaPQAeWDEc5+j0D/gk3RA=";

    private readonly EndpointTestFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ConfigEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region GET /api/config

    [Fact]
    public async Task GetConfig_ReturnsJsonWithExpectedFields()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var response = await client.GetAsync("/api/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("dataRoot");
        json.Should().ContainKey("dataSource");
        json.Should().ContainKey("symbols");
        json.Should().ContainKey("storage");
    }

    [Fact]
    public async Task GetConfig_ContainsConfiguredSymbols()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var response = await client.GetAsync("/api/config");
        var json = await DeserializeAsync(response);

        json.Should().ContainKey("symbols");
        var symbols = json["symbols"];
        symbols.ValueKind.Should().Be(JsonValueKind.Array);
        symbols.GetArrayLength().Should().BeGreaterThanOrEqualTo(2,
            "Test config includes SPY and AAPL");
    }

    #endregion

    #region POST /api/config/symbols

    [Fact]
    public async Task AddSymbol_WithValidData_WithoutPermission_ReturnsUnauthorized()
    {
        using var client = _fixture.CreateNoRedirectClient();
        var payload = new { Symbol = "MSFT", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 10, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/config/symbols", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddSymbol_WithEmptySymbol_WithModifyPermission_ReturnsBadRequest()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ModifyConfig);
        var payload = new { Symbol = "", SubscribeTrades = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/config/symbols", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddSymbol_WithoutPermission_DoesNotPersistInConfig()
    {
        using var unauthenticatedClient = _fixture.CreateNoRedirectClient();
        var payload = new { Symbol = "TSLA", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var addResponse = await unauthenticatedClient.PostAsync("/api/config/symbols", content);
        addResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify the unauthorized mutation did not change the configuration.
        using var viewClient = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var configResponse = await viewClient.GetAsync("/api/config");
        var json = await DeserializeAsync(configResponse);
        var symbols = json["symbols"].EnumerateArray()
            .Select(s => s.GetProperty("symbol").GetString())
            .ToList();

        symbols.Should().NotContain("TSLA");
    }

    #endregion

    #region DELETE /api/config/symbols/{symbol}

    [Fact]
    public async Task DeleteSymbol_WithoutPermission_ReturnsUnauthorized()
    {
        using var modifyClient = _fixture.CreatePermittedClient(UserPermission.ModifyConfig);
        // First add a symbol to delete
        var addPayload = new { Symbol = "GOOG", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var addResponse = await modifyClient.PostAsync("/api/config/symbols",
            new StringContent(JsonSerializer.Serialize(addPayload), Encoding.UTF8, "application/json"));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var unauthenticatedClient = _fixture.CreateNoRedirectClient();
        var response = await unauthenticatedClient.DeleteAsync("/api/config/symbols/GOOG");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteSymbol_WithoutPermission_LeavesConfigurationUnchanged()
    {
        using var modifyClient = _fixture.CreatePermittedClient(UserPermission.ModifyConfig);
        // Add a symbol
        var addPayload = new { Symbol = "NFLX", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var addResponse = await modifyClient.PostAsync("/api/config/symbols",
            new StringContent(JsonSerializer.Serialize(addPayload), Encoding.UTF8, "application/json"));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // An unauthenticated caller cannot delete it.
        using var unauthenticatedClient = _fixture.CreateNoRedirectClient();
        var deleteResponse = await unauthenticatedClient.DeleteAsync("/api/config/symbols/NFLX");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify the symbol remains configured after the rejected mutation.
        using var viewClient = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var configResponse = await viewClient.GetAsync("/api/config");
        var json = await DeserializeAsync(configResponse);
        var symbols = json["symbols"].EnumerateArray()
            .Select(s => s.GetProperty("symbol").GetString())
            .ToList();

        symbols.Should().Contain("NFLX");
    }

    #endregion

    #region GET /api/config/derivatives

    [Fact]
    public async Task GetDerivatives_ReturnsJson()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var response = await client.GetAsync("/api/config/derivatives");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region POST /api/config/datasource

    [Fact]
    public async Task UpdateDataSource_WithValidValue_WithoutPermission_ReturnsUnauthorized()
    {
        using var client = _fixture.CreateNoRedirectClient();
        var payload = new { DataSource = "Alpaca" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/config/datasource", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateDataSource_WithInvalidValue_WithModifyPermission_ReturnsBadRequest()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ModifyConfig);
        var payload = new { DataSource = "InvalidProvider" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/config/datasource", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateDataSource_WithCookieAuth_RequiresCsrfHeader()
    {
        using var client = _fixture.CreateNoRedirectClient();
        var originalUsername = Environment.GetEnvironmentVariable("MDC_USERNAME");
        var originalPasswordHash = Environment.GetEnvironmentVariable("MDC_PASSWORD_HASH");
        Environment.SetEnvironmentVariable("MDC_USERNAME", "config-admin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", ConfigPasswordHash);

        try
        {
            var loginPayload = new { Username = "config-admin", Password = "config-password" };
            var loginContent = new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json");
            var loginResponse = await client.PostAsync("/api/auth/login", loginContent);
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var setCookies = loginResponse.Headers
                .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .ToArray();
            var sessionCookie = ExtractCookieValue(setCookies, "mdc-session");
            var csrfCookie = ExtractCookieValue(setCookies, "mdc-csrf");
            sessionCookie.Should().NotBeNullOrWhiteSpace();
            csrfCookie.Should().NotBeNullOrWhiteSpace();

            var cookieHeader = $"mdc-session={sessionCookie}; mdc-csrf={csrfCookie}";
            var payload = new { DataSource = "Alpaca" };
            var requestBody = JsonSerializer.Serialize(payload);

            using var noCsrfRequest = new HttpRequestMessage(HttpMethod.Post, "/api/config/datasource")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            noCsrfRequest.Headers.Add("Cookie", cookieHeader);
            var noCsrfResponse = await client.SendAsync(noCsrfRequest);
            noCsrfResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var withCsrfRequest = new HttpRequestMessage(HttpMethod.Post, "/api/config/datasource")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            withCsrfRequest.Headers.Add("Cookie", cookieHeader);
            withCsrfRequest.Headers.Add("X-CSRF-Token", csrfCookie);
            var withCsrfResponse = await client.SendAsync(withCsrfRequest);
            withCsrfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERNAME", originalUsername);
            Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", originalPasswordHash);
        }
    }

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content, JsonOptions)!;
    }

    private static string? ExtractCookieValue(IEnumerable<string> setCookies, string cookieName)
    {
        var prefix = cookieName + "=";
        var match = setCookies.FirstOrDefault(cookie => cookie.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return null;

        var separatorIndex = match.IndexOf(';');
        return separatorIndex >= 0
            ? match[prefix.Length..separatorIndex]
            : match[prefix.Length..];
    }
}
