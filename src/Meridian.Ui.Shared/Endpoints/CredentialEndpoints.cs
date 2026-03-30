using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// REST endpoints for managing provider API credentials.
/// Credentials are stored as user-scoped environment variables;
/// secrets are never returned in full — only a masked preview is sent.
/// </summary>
public static class CredentialEndpoints
{
    /// <summary>
    /// Well-known provider → required env-var mapping used by all credential endpoints.
    /// Keeping this local avoids a dependency on Meridian.Ui.Services from this project.
    /// </summary>
    private static readonly IReadOnlyList<ProviderCredentialDescriptor> KnownProviders =
    [
        new("alpaca",       "Alpaca",           ["APCA_API_KEY_ID", "APCA_API_SECRET_KEY"]),
        new("polygon",      "Polygon",          ["POLYGON_API_KEY"]),
        new("finnhub",      "Finnhub",          ["FINNHUB_API_KEY"]),
        new("tiingo",       "Tiingo",           ["TIINGO_API_TOKEN"]),
        new("alphaVantage", "Alpha Vantage",    ["ALPHA_VANTAGE_API_KEY"]),
        new("nasdaq",       "Nasdaq Data Link", ["NASDAQ_API_KEY"]),
        new("twelvedata",   "Twelve Data",      ["TWELVE_DATA_API_KEY"]),
        new("openfigi",     "OpenFIGI",         ["OPENFIGI_API_KEY"]),
        new("ib",           "Interactive Brokers", []),
        new("synthetic",    "Synthetic",        []),
        new("stooq",        "Stooq",            []),
        new("stocksharp",   "StockSharp",       []),
    ];

    /// <summary>Registers all credential management routes.</summary>
    public static void MapCredentialEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Credentials");

        // GET /api/credentials — list all providers with their credential status
        group.MapGet(Contracts.Api.UiApiRoutes.Credentials, () =>
        {
            var result = KnownProviders.Select(p => BuildStatus(p));
            return Results.Json(result, jsonOptions);
        });

        // GET /api/credentials/{provider}
        group.MapGet(Contracts.Api.UiApiRoutes.CredentialByProvider, (string provider) =>
        {
            var descriptor = FindProvider(provider);
            if (descriptor is null)
                return Results.NotFound(new { error = $"Provider '{provider}' not found." });

            var envVarStatus = descriptor.RequiredEnvVars.Select(v =>
            {
                var value = Environment.GetEnvironmentVariable(v);
                return new
                {
                    name = v,
                    isSet = !string.IsNullOrWhiteSpace(value),
                    maskedValue = MaskValue(value)
                };
            }).ToArray();

            return Results.Json(new
            {
                providerId = descriptor.Id,
                displayName = descriptor.DisplayName,
                state = ResolveState(descriptor),
                envVars = envVarStatus
            }, jsonOptions);
        });

        // POST /api/credentials/{provider} — set env vars for a provider
        group.MapPost(Contracts.Api.UiApiRoutes.CredentialByProvider, async (string provider, HttpContext ctx) =>
        {
            var descriptor = FindProvider(provider);
            if (descriptor is null)
                return Results.NotFound(new { error = $"Provider '{provider}' not found." });

            JsonDocument doc;
            try
            { doc = await JsonDocument.ParseAsync(ctx.Request.Body); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body." }); }

            var warnings = new List<string>();
            foreach (var envVar in descriptor.RequiredEnvVars)
            {
                if (!doc.RootElement.TryGetProperty(envVar, out var el) &&
                    !doc.RootElement.TryGetProperty(envVar.ToLowerInvariant(), out el))
                    continue;   // partial update allowed

                var value = el.GetString();
                if (value is not null)
                    Environment.SetEnvironmentVariable(envVar, value, EnvironmentVariableTarget.User);
                else
                    warnings.Add($"Value for '{envVar}' was null; skipped.");
            }

            return Results.Json(new
            {
                providerId = provider,
                state = ResolveState(descriptor),
                warnings
            }, jsonOptions);
        });

        // DELETE /api/credentials/{provider} — clear env vars
        group.MapDelete(Contracts.Api.UiApiRoutes.CredentialByProvider, (string provider) =>
        {
            var descriptor = FindProvider(provider);
            if (descriptor is null)
                return Results.NotFound(new { error = $"Provider '{provider}' not found." });

            foreach (var envVar in descriptor.RequiredEnvVars)
                Environment.SetEnvironmentVariable(envVar, null, EnvironmentVariableTarget.User);

            return Results.Json(new
            {
                providerId = provider,
                cleared = descriptor.RequiredEnvVars
            }, jsonOptions);
        });

        // POST /api/credentials/{provider}/test — verify credentials present
        group.MapPost(Contracts.Api.UiApiRoutes.CredentialTest, (string provider) =>
        {
            var descriptor = FindProvider(provider);
            if (descriptor is null)
                return Results.NotFound(new { error = $"Provider '{provider}' not found." });

            var state = ResolveState(descriptor);
            var ok = state is "Configured" or "NotRequired";

            return Results.Json(new
            {
                providerId = provider,
                success = ok,
                state,
                message = ok
                    ? $"Credentials for '{provider}' are present and ready."
                    : $"Credentials for '{provider}' are missing or incomplete."
            }, jsonOptions);
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ProviderCredentialDescriptor? FindProvider(string id) =>
        KnownProviders.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string ResolveState(ProviderCredentialDescriptor p)
    {
        if (p.RequiredEnvVars.Count == 0)
            return "NotRequired";
        var missing = p.RequiredEnvVars
            .Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)))
            .ToList();
        return missing.Count == 0 ? "Configured"
            : missing.Count < p.RequiredEnvVars.Count ? "Partial"
            : "Missing";
    }

    private static object BuildStatus(ProviderCredentialDescriptor p) => new
    {
        providerId = p.Id,
        displayName = p.DisplayName,
        state = ResolveState(p)
    };

    private static string MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Length <= 4)
            return new string('*', value.Length);
        return string.Concat(value.AsSpan(0, 4), new string('*', Math.Min(value.Length - 4, 12)));
    }

    private sealed record ProviderCredentialDescriptor(
        string Id,
        string DisplayName,
        IReadOnlyList<string> RequiredEnvVars);
}

