using System.Text.Json;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Catalog;
using Meridian.Identity.Auth;
using Meridian.Storage.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Registry-backed symbol mapping endpoints shared by browser and desktop hosts.
/// Legacy configuration remains a migration/rollback input but is not a second write model.
/// </summary>
public static class SymbolMappingEndpoints
{
    private static readonly string[] LegacyProviders = ["ib", "alpaca", "polygon", "yahoo"];

    public static void MapSymbolMappingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Symbol Mapping");
        var requireModifyConfig = EndpointAuthorization.Require(UserPermission.ModifyConfig);

        group.RequireWorkstationTenantScope();
        group.RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig);

        group.MapGet(UiApiRoutes.CanonicalSymbolRegistry, (
            ICanonicalSymbolRegistry registry,
            IServiceProvider services) =>
        {
            var storedRegistry = services.GetService<ISymbolRegistryService>()?.GetRegistry();
            var mismatchReader = services.GetService<ISymbolResolutionMismatchReader>();
            var mismatches = mismatchReader?.GetRecent() ?? [];
            var mode = GetResolutionMode(services);
            var mismatchSecurityIds = mismatches
                .Where(static mismatch => mismatch.SecurityId.HasValue)
                .Select(static mismatch => mismatch.SecurityId!.Value)
                .ToHashSet();
            var mismatchCanonicals = mismatches
                .Select(mismatch => registry.GetDefinition(mismatch.Input)?.Canonical)
                .Where(static canonical => canonical is not null)
                .Select(static canonical => canonical!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var response = new CanonicalSymbolRegistryResponse(
                RegistryVersion: storedRegistry?.RegistryVersion ?? "1.0.0",
                ResolutionMode: mode,
                CompareModeReturnsLegacy: mode == SymbolResolutionMode.Compare,
                TotalMismatchCount: mismatchReader?.TotalCount ?? 0,
                LastMismatchAt: mismatches.FirstOrDefault()?.ObservedAt,
                RecentMismatches: mismatches.Select(ToMismatchResponse).ToArray(),
                Migrations: (storedRegistry?.MigrationMarkers ?? new Dictionary<string, string>())
                    .OrderBy(static marker => marker.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static marker => new CanonicalSymbolMigrationResponse(marker.Key, marker.Value))
                    .ToArray(),
                Symbols: registry.GetAll()
                    .OrderBy(static definition => definition.Canonical, StringComparer.OrdinalIgnoreCase)
                    .Select(definition => ToRegistryEntryResponse(
                        definition,
                        mismatchSecurityIds.Contains(definition.SecurityId ?? Guid.Empty)
                            || mismatchCanonicals.Contains(definition.Canonical)))
                    .ToArray());

            return Results.Json(response, jsonOptions);
        }).WithName("GetCanonicalSymbolRegistry")
        .Produces<CanonicalSymbolRegistryResponse>(200);

        group.MapGet(UiApiRoutes.SymbolMappings, (ICanonicalSymbolRegistry registry) =>
            Results.Json(
                registry.GetAll().Select(ToResponse).OrderBy(static mapping => mapping.CanonicalSymbol).ToArray(),
                jsonOptions))
            .WithName("GetSymbolMappings")
            .Produces(200);

        group.MapPost(UiApiRoutes.SymbolMappings, async (
            ICanonicalSymbolRegistry registry,
            SymbolMappingRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.CanonicalSymbol))
                return Results.BadRequest("CanonicalSymbol is required.");

            var canonical = req.CanonicalSymbol.Trim().ToUpperInvariant();
            await registry.RegisterAsync(new CanonicalSymbolDefinition
            {
                Canonical = canonical,
                DisplayName = req.Name,
                Figi = req.Figi
            }, ct).ConfigureAwait(false);

            await SetIfPresentAsync(registry, canonical, "ib", req.IbSymbol, ct).ConfigureAwait(false);
            await SetIfPresentAsync(registry, canonical, "alpaca", req.AlpacaSymbol, ct).ConfigureAwait(false);
            await SetIfPresentAsync(registry, canonical, "polygon", req.PolygonSymbol, ct).ConfigureAwait(false);
            await SetIfPresentAsync(registry, canonical, "yahoo", req.YahooSymbol, ct).ConfigureAwait(false);
            return Results.Ok();
        }).WithName("UpsertSymbolMapping").Produces(200).Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        group.MapDelete(UiApiRoutes.SymbolMappings + "/{symbol}", async (
            ICanonicalSymbolRegistry registry,
            string symbol,
            CancellationToken ct) =>
        {
            var definition = registry.GetDefinition(symbol);
            if (definition is null)
                return Results.NotFound();

            var removed = false;
            foreach (var provider in LegacyProviders)
            {
                if (!definition.ProviderSymbols.TryGetValue(provider, out var mapping) || !mapping.IsOverride)
                    continue;

                removed |= await registry.RemoveProviderSymbolAsync(definition.Canonical, provider, ct)
                    .ConfigureAwait(false);
            }

            return removed ? Results.Ok() : Results.NotFound();
        }).WithName("DeleteSymbolMapping").Produces(200).Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        group.MapGet(UiApiRoutes.SymbolMappings + "/{symbol}", (
            ICanonicalSymbolRegistry registry,
            string symbol) =>
        {
            var definition = registry.GetDefinition(symbol);
            return definition is null
                ? Results.NotFound()
                : Results.Json(ToResponse(definition), jsonOptions);
        }).WithName("GetSymbolMapping").Produces(200).Produces(404);

        group.MapPost(UiApiRoutes.SymbolMappings + "/import", async (
            ICanonicalSymbolRegistry registry,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Content must be multipart/form-data");

            var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file uploaded");

            var imported = 0;
            using var reader = new StreamReader(file.OpenReadStream());
            var header = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (header is null)
                return Results.Ok(new { imported });

            var columns = header.Split(',')
                .Select(static value => value.Trim().Trim('"').ToLowerInvariant())
                .ToArray();

            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = ParseCsvLine(line);
                var canonical = GetColumn(columns, values, "canonical")
                    ?? GetColumn(columns, values, "symbol");
                if (string.IsNullOrWhiteSpace(canonical))
                    continue;

                canonical = canonical.Trim().ToUpperInvariant();
                await registry.RegisterAsync(new CanonicalSymbolDefinition
                {
                    Canonical = canonical,
                    DisplayName = GetColumn(columns, values, "name"),
                    Figi = GetColumn(columns, values, "figi")
                }, ct).ConfigureAwait(false);

                foreach (var provider in LegacyProviders)
                {
                    await SetIfPresentAsync(
                        registry,
                        canonical,
                        provider,
                        GetColumn(columns, values, provider),
                        ct).ConfigureAwait(false);
                }

                imported++;
            }

            return Results.Ok(new { imported });
        }).WithName("ImportSymbolMappings").Produces(200).Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);
    }

    private static SymbolMappingResponse ToResponse(CanonicalSymbolDefinition definition)
        => new(
            CanonicalSymbol: definition.Canonical,
            IbSymbol: GetProviderSymbol(definition, "ib"),
            AlpacaSymbol: GetProviderSymbol(definition, "alpaca"),
            PolygonSymbol: GetProviderSymbol(definition, "polygon"),
            YahooSymbol: GetProviderSymbol(definition, "yahoo"),
            Name: definition.DisplayName,
            Figi: definition.Figi);

    private static CanonicalSymbolRegistryEntryResponse ToRegistryEntryResponse(
        CanonicalSymbolDefinition definition,
        bool hasRecentMismatch)
    {
        var richAliases = definition.AliasDefinitions
            .GroupBy(
                static alias => $"{alias.Provider}\u001f{alias.Alias}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
        var richAliasValues = richAliases
            .Select(static alias => alias.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliases = richAliases
            .Select(static alias => new CanonicalSymbolAliasResponse(
                alias.Alias,
                alias.Source,
                alias.Provider,
                alias.ValidFrom,
                alias.ValidTo,
                alias.IsActive))
            .Concat(definition.Aliases
                .Where(alias => !richAliasValues.Contains(alias))
                .Select(static alias => new CanonicalSymbolAliasResponse(
                    alias,
                    Source: null,
                    Provider: null,
                    ValidFrom: null,
                    ValidTo: null,
                    IsActive: true)))
            .OrderBy(static alias => alias.Alias, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static alias => alias.Provider, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var providerAliases = definition.ProviderSymbols
            .OrderBy(static mapping => mapping.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static mapping => new CanonicalProviderAliasResponse(
                Provider: mapping.Key,
                Symbol: mapping.Value.Symbol,
                Source: mapping.Value.Source,
                IsOverride: mapping.Value.IsOverride,
                UpdatedAt: mapping.Value.UpdatedAt))
            .ToArray();
        var provenanceSources = aliases
            .Select(static alias => alias.Source)
            .Concat(providerAliases.Select(static mapping => mapping.Source))
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Select(static source => source!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty(SymbolMappingSources.Registry)
            .ToArray();

        return new CanonicalSymbolRegistryEntryResponse(
            SecurityId: definition.SecurityId,
            CanonicalTicker: definition.Canonical,
            DisplayName: definition.DisplayName,
            AssetClass: definition.AssetClass,
            Exchange: definition.Exchange,
            Currency: definition.Currency,
            Identifiers: new CanonicalSymbolIdentifiersResponse(
                definition.Isin,
                definition.Figi,
                definition.CompositeFigi,
                definition.Cusip,
                definition.Sedol),
            Aliases: aliases,
            ProviderAliases: providerAliases,
            ProvenanceSources: provenanceSources,
            HasRecentMismatch: hasRecentMismatch);
    }

    private static CanonicalSymbolResolutionMismatchResponse ToMismatchResponse(SymbolResolutionMismatch mismatch)
        => new(
            mismatch.Input,
            mismatch.FromProvider,
            mismatch.ToProvider,
            mismatch.LegacyResult,
            mismatch.CanonicalResult,
            mismatch.SecurityId,
            mismatch.ObservedAt);

    private static SymbolResolutionMode GetResolutionMode(IServiceProvider services)
    {
        var config = services.GetService<ConfigStore>()?.Load();
        return config?.DataSources?.SymbolMappings?.ResolutionMode
            ?? config?.Backfill?.SymbolResolutionMode
            ?? SymbolResolutionMode.Compare;
    }

    private static string? GetProviderSymbol(CanonicalSymbolDefinition definition, string provider)
        => definition.ProviderSymbols.TryGetValue(provider, out var mapping) ? mapping.Symbol : null;

    private static Task SetIfPresentAsync(
        ICanonicalSymbolRegistry registry,
        string canonical,
        string provider,
        string? providerSymbol,
        CancellationToken ct)
        => string.IsNullOrWhiteSpace(providerSymbol)
            ? Task.CompletedTask
            : registry.SetProviderSymbolAsync(
                canonical,
                provider,
                providerSymbol.Trim(),
                SymbolMappingSources.Operator,
                isOverride: true,
                ct);

    private static string? GetColumn(string[] columns, string[] values, string name)
    {
        var index = Array.FindIndex(columns, column => column.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < values.Length && !string.IsNullOrWhiteSpace(values[index])
            ? values[index].Trim().Trim('"')
            : null;
    }

    private static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var character in line)
        {
            if (character == '"')
                inQuotes = !inQuotes;
            else if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(character);
        }
        values.Add(current.ToString());
        return values.ToArray();
    }
}
