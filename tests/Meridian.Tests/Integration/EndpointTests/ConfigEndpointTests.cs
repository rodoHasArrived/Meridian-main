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
public sealed class ConfigEndpointTests : IDisposable
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ConfigEndpointTests(EndpointTestFixture fixture)
    {
        // SEC-001: configuration routes now require ViewConfig/ModifyConfig. Authorize this suite's
        // client so the existing functional assertions continue to exercise the handlers.
        _client = fixture.CreatePermittedClient(UserPermission.ViewConfig, UserPermission.ModifyConfig);
    }

    public void Dispose() => _client.Dispose();

    #region GET /api/config

    [Fact]
    public async Task GetConfig_ReturnsJsonWithExpectedFields()
    {
        var response = await _client.GetAsync("/api/config");

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
        var response = await _client.GetAsync("/api/config");
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
        var payload = new { Symbol = "MSFT", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 10, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/symbols", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddSymbol_WithEmptySymbol_WithoutPermission_ReturnsUnauthorized()
    {
        var payload = new { Symbol = "", SubscribeTrades = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/symbols", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddSymbol_WithoutPermission_DoesNotPersistInConfig()
    {
        var payload = new { Symbol = "TSLA", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var addResponse = await _client.PostAsync("/api/config/symbols", content);
        addResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify the symbol appears in config
        var configResponse = await _client.GetAsync("/api/config");
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
        // First add a symbol to delete
        var addPayload = new { Symbol = "GOOG", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        await _client.PostAsync("/api/config/symbols",
            new StringContent(JsonSerializer.Serialize(addPayload), Encoding.UTF8, "application/json"));

        var response = await _client.DeleteAsync("/api/config/symbols/GOOG");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteSymbol_WithoutPermission_LeavesConfigurationUnchanged()
    {
        // Add a symbol
        var addPayload = new { Symbol = "NFLX", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        await _client.PostAsync("/api/config/symbols",
            new StringContent(JsonSerializer.Serialize(addPayload), Encoding.UTF8, "application/json"));

        // Delete it
        var deleteResponse = await _client.DeleteAsync("/api/config/symbols/NFLX");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify it's gone
        var configResponse = await _client.GetAsync("/api/config");
        var json = await DeserializeAsync(configResponse);
        var symbols = json["symbols"].EnumerateArray()
            .Select(s => s.GetProperty("symbol").GetString())
            .ToList();

        symbols.Should().NotContain("NFLX");
    }

    #endregion

    #region GET /api/config/derivatives

    [Fact]
    public async Task GetDerivatives_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/config/derivatives");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region POST /api/config/datasource

    [Fact]
    public async Task UpdateDataSource_WithValidValue_WithoutPermission_ReturnsUnauthorized()
    {
        var payload = new { DataSource = "Alpaca" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/datasource", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateDataSource_WithInvalidValue_WithoutPermission_ReturnsUnauthorized()
    {
        var payload = new { DataSource = "InvalidProvider" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/datasource", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateDataSource_WithCookieAuth_RequiresCsrfHeader()
    {
        var originalUsername = Environment.GetEnvironmentVariable("MDC_USERNAME");
        var originalPassword = Environment.GetEnvironmentVariable("MDC_PASSWORD");
        Environment.SetEnvironmentVariable("MDC_USERNAME", "config-admin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD", "config-password");

        try
        {
            var loginPayload = new { Username = "config-admin", Password = "config-password" };
            var loginContent = new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json");
            var loginResponse = await _client.PostAsync("/api/auth/login", loginContent);
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
            var noCsrfResponse = await _client.SendAsync(noCsrfRequest);
            noCsrfResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var withCsrfRequest = new HttpRequestMessage(HttpMethod.Post, "/api/config/datasource")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            withCsrfRequest.Headers.Add("Cookie", cookieHeader);
            withCsrfRequest.Headers.Add("X-CSRF-Token", csrfCookie);
            var withCsrfResponse = await _client.SendAsync(withCsrfRequest);
            withCsrfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERNAME", originalUsername);
            Environment.SetEnvironmentVariable("MDC_PASSWORD", originalPassword);
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
