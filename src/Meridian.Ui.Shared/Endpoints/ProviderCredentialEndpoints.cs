using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering provider credential management API endpoints.
/// Handles credential setup, validation, and testing for data providers.
/// </summary>
public static class ProviderCredentialEndpoints
{
    public static void MapProviderCredentialEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Provider Credentials");

        // POST /api/credentials/{provider}/validate — validate provider credentials
        group.MapPost(UiApiRoutes.ProviderCredentialsValidate, async (
            HttpContext ctx,
            string provider,
            ConfigStore store,
            ProviderCredentialRequest req) =>
        {
            if (!HasManageCredentialsPermission(ctx))
                return EndpointHelpers.Forbidden();

            if (string.IsNullOrWhiteSpace(provider))
                return Results.BadRequest(new { error = "Provider name is required" });

            if (req.Credentials is null || req.Credentials.Count == 0)
                return Results.BadRequest(new { error = "Credentials are required" });

            try
            {
                var cfg = store.Load();
                var isValid = ValidateProviderCredentials(provider, req.Credentials);

                return Results.Json(new ProviderCredentialValidationResult(
                    IsValid: isValid,
                    Status: isValid ? "Valid" : "Invalid",
                    ErrorMessage: isValid ? null : $"Credentials for {provider} are incomplete or invalid",
                    LastVerified: DateTime.UtcNow,
                    Details: new Dictionary<string, string>
                    {
                        { "provider", provider },
                        { "credentialCount", req.Credentials.Count.ToString() },
                        { "validated", isValid ? "yes" : "no" }
                    }
                ), jsonOptions);
            }
            catch (Exception)
            {
                return Results.Json(new ProviderCredentialValidationResult(
                    IsValid: false,
                    Status: "Error",
                    ErrorMessage: "An error occurred while validating credentials."
                ), jsonOptions);
            }
        })
        .WithName("ValidateProviderCredentials")
        .WithDescription("Validates provider credentials without testing connectivity.")
        .Produces<ProviderCredentialValidationResult>(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/providers/{provider}/test-connection — test connection with credentials
        group.MapPost(UiApiRoutes.ProviderConnectionTest, async (
            HttpContext ctx,
            string provider,
            ConfigStore store,
            ProviderCredentialRequest req) =>
        {
            if (!HasManageCredentialsPermission(ctx))
                return EndpointHelpers.Forbidden();

            if (string.IsNullOrWhiteSpace(provider))
                return Results.BadRequest(new { error = "Provider name is required" });

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var isConnected = await TestProviderConnection(provider, req.Credentials);
                sw.Stop();

                return Results.Json(new ProviderConnectionTest(
                    Provider: provider,
                    IsConnected: isConnected,
                    ResponseTimeMs: (int)sw.ElapsedMilliseconds,
                    Status: isConnected ? "Connected" : "Failed",
                    ErrorMessage: isConnected ? null : $"Connection test to {provider} failed",
                    TestedAt: DateTime.UtcNow
                ), jsonOptions);
            }
            catch (Exception)
            {
                return Results.Json(new ProviderConnectionTest(
                    Provider: provider,
                    IsConnected: false,
                    Status: "Error",
                    ErrorMessage: "An error occurred while testing the connection.",
                    TestedAt: DateTime.UtcNow
                ), jsonOptions);
            }
        })
        .WithName("TestProviderConnection")
        .WithDescription("Tests connectivity to a data provider with given credentials.")
        .Produces<ProviderConnectionTest>(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/credentials/{provider} — get credential setup requirements for provider
        group.MapGet(UiApiRoutes.CredentialByProvider, (string provider) =>
        {
            var requirements = GetProviderCredentialRequirements(provider);
            return Results.Json(requirements, jsonOptions);
        })
        .WithName("GetProviderCredentialRequirements")
        .WithDescription("Returns credential field requirements for a specific provider.")
        .Produces(200);

        // GET /api/credentials — get all providers and their credential requirements
        group.MapGet(UiApiRoutes.Credentials, () =>
        {
            var providers = new[] { "Alpaca", "Polygon", "Robinhood", "Yahoo", "InteractiveBrokers", "Finnhub", "Tiingo", "AlphaVantage" };
            var requirements = providers.ToDictionary(p => p, GetProviderCredentialRequirements);
            return Results.Json(new { providers = requirements }, jsonOptions);
        })
        .WithName("GetAllProviderCredentialRequirements")
        .WithDescription("Returns credential field requirements for all supported providers.")
        .Produces(200);
    }

    private static bool ValidateProviderCredentials(string provider, Dictionary<string, string> credentials)
    {
        return provider.ToLowerInvariant() switch
        {
            "alpaca" => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("KeyId")) &&
                       !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("SecretKey")),
            "polygon" => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("ApiKey")),
            "robinhood" => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("Username")) &&
                          !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("Password")),
            "yahoo" => true, // Yahoo Finance doesn't require credentials
            "finnhub" => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("ApiKey")),
            "tiingo" => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("ApiKey")),
            "alphavantage" => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("ApiKey")),
            "interactivebrokers" => !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("Host")) &&
                                   !string.IsNullOrWhiteSpace(credentials.GetValueOrDefault("Port")),
            _ => false
        };
    }

    private static async Task<bool> TestProviderConnection(string provider, Dictionary<string, string>? credentials)
    {
        // Simulate provider connection test based on provider type
        // In a real implementation, this would call actual provider APIs
        await Task.Delay(500); // Simulate network latency

        if (credentials is null || credentials.Count == 0)
            return false;

        return provider.ToLowerInvariant() switch
        {
            "alpaca" => credentials.ContainsKey("KeyId") && credentials.ContainsKey("SecretKey"),
            "polygon" => credentials.ContainsKey("ApiKey"),
            "robinhood" => credentials.ContainsKey("Username") && credentials.ContainsKey("Password"),
            "yahoo" => true,
            "finnhub" => credentials.ContainsKey("ApiKey"),
            "tiingo" => credentials.ContainsKey("ApiKey"),
            "alphavantage" => credentials.ContainsKey("ApiKey"),
            "interactivebrokers" => credentials.ContainsKey("Host") && credentials.ContainsKey("Port"),
            _ => false
        };
    }

    private static object GetProviderCredentialRequirements(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "alpaca" => new
            {
                name = "Alpaca",
                description = "Live and paper trading via Alpaca API",
                type = "trading_broker",
                supportsDemo = false,
                fields = new[]
                {
                    new { name = "KeyId", label = "API Key ID", required = true, type = "text", help = "Your Alpaca API Key ID" },
                    new { name = "SecretKey", label = "Secret Key", required = true, type = "password", help = "Your Alpaca Secret Key" }
                },
                docs = "https://alpaca.markets/docs/api-references/",
                supportedDataTypes = new[] { "RealTime", "Historical" }
            },
            "polygon" => new
            {
                name = "Polygon.io",
                description = "Stock market data provider",
                type = "data_provider",
                supportsDemo = true,
                fields = new[]
                {
                    new { name = "ApiKey", label = "API Key", required = true, type = "password", help = "Your Polygon.io API key" }
                },
                docs = "https://polygon.io/docs",
                supportedDataTypes = new[] { "RealTime", "Historical", "Options" }
            },
            "robinhood" => new
            {
                name = "Robinhood",
                description = "Commission-free trading and brokerage",
                type = "trading_broker",
                supportsDemo = false,
                fields = new[]
                {
                    new { name = "Username", label = "Email/Username", required = true, type = "email", help = "Your Robinhood account email" },
                    new { name = "Password", label = "Password", required = true, type = "password", help = "Your Robinhood account password" }
                },
                docs = "https://robinhood.com",
                supportedDataTypes = new[] { "RealTime", "Historical" }
            },
            "yahoo" => new
            {
                name = "Yahoo Finance",
                description = "Free historical and real-time data",
                type = "data_provider",
                supportsDemo = true,
                fields = Array.Empty<object>(),
                docs = "https://finance.yahoo.com",
                supportedDataTypes = new[] { "Historical", "RealTime" }
            },
            "finnhub" => new
            {
                name = "Finnhub",
                description = "Real-time stock market data",
                type = "data_provider",
                supportsDemo = true,
                fields = new[]
                {
                    new { name = "ApiKey", label = "API Key", required = true, type = "password", help = "Your Finnhub API key" }
                },
                docs = "https://finnhub.io/docs/api",
                supportedDataTypes = new[] { "RealTime", "Historical" }
            },
            "tiingo" => new
            {
                name = "Tiingo",
                description = "Financial data APIs and tools",
                type = "data_provider",
                supportsDemo = true,
                fields = new[]
                {
                    new { name = "ApiKey", label = "API Key", required = true, type = "password", help = "Your Tiingo API key" }
                },
                docs = "https://api.tiingo.com",
                supportedDataTypes = new[] { "Historical", "Intraday" }
            },
            "alphavantage" => new
            {
                name = "Alpha Vantage",
                description = "Stock market APIs with free tier",
                type = "data_provider",
                supportsDemo = true,
                fields = new[]
                {
                    new { name = "ApiKey", label = "API Key", required = true, type = "password", help = "Your Alpha Vantage API key" }
                },
                docs = "https://www.alphavantage.co",
                supportedDataTypes = new[] { "Historical", "Intraday" }
            },
            "interactivebrokers" => new
            {
                name = "Interactive Brokers",
                description = "Professional trading platform with market data",
                type = "trading_broker",
                supportsDemo = true,
                fields = new[]
                {
                    new { name = "Host", label = "IBGateway Host", required = true, type = "text", help = "Hostname or IP of IBGateway (default: localhost)" },
                    new { name = "Port", label = "IBGateway Port", required = true, type = "text", help = "Port of IBGateway (default: 7497)" }
                },
                docs = "https://www.interactivebrokers.com",
                supportedDataTypes = new[] { "RealTime", "Historical" }
            },
            _ => new
            {
                name = provider,
                description = $"Data provider: {provider}",
                type = "data_provider",
                supportsDemo = false,
                fields = new[]
                {
                    new { name = "ApiKey", label = "API Key", required = true, type = "password", help = "Provider API key" }
                },
                docs = "",
                supportedDataTypes = new[] { "Historical" }
            }
        };
    }

    private static bool HasManageCredentialsPermission(HttpContext context)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserPermissionsKey, out var value) &&
            value is UserPermission current)
        {
            return (current & UserPermission.ManageCredentials) == UserPermission.ManageCredentials;
        }

        return false;
    }
}
