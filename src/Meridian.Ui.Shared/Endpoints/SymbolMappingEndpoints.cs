using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering symbol mapping API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// </summary>
public static class SymbolMappingEndpoints
{
    /// <summary>
    /// Maps all symbol mapping API endpoints.
    /// </summary>
    public static void MapSymbolMappingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Symbol Mapping");

        // Get all symbol mappings
        group.MapGet(UiApiRoutes.SymbolMappings, (ConfigStore store) =>
        {
            var cfg = store.Load();
            var mappings = cfg.DataSources?.SymbolMappings?.Mappings ?? Array.Empty<SymbolMappingConfig>();

            var response = mappings.Select(m => new SymbolMappingResponse(
                CanonicalSymbol: m.CanonicalSymbol,
                IbSymbol: m.IbSymbol,
                AlpacaSymbol: m.AlpacaSymbol,
                PolygonSymbol: m.PolygonSymbol,
                YahooSymbol: m.YahooSymbol,
                Name: m.Name,
                Figi: m.Figi
            )).ToArray();

            return Results.Json(response, jsonOptions);
        }).WithName("GetSymbolMappings").Produces(200);

        // Create or update symbol mapping
        group.MapPost(UiApiRoutes.SymbolMappings, async (ConfigStore store, SymbolMappingRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.CanonicalSymbol))
                return Results.BadRequest("CanonicalSymbol is required.");

            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var symbolMappings = dataSources.SymbolMappings ?? new SymbolMappingsConfig();
            var mappings = (symbolMappings.Mappings ?? Array.Empty<SymbolMappingConfig>()).ToList();

            var mapping = new SymbolMappingConfig(
                CanonicalSymbol: req.CanonicalSymbol.ToUpperInvariant(),
                IbSymbol: req.IbSymbol,
                AlpacaSymbol: req.AlpacaSymbol,
                PolygonSymbol: req.PolygonSymbol,
                YahooSymbol: req.YahooSymbol,
                Name: req.Name,
                Figi: req.Figi
            );

            var idx = mappings.FindIndex(m =>
                string.Equals(m.CanonicalSymbol, req.CanonicalSymbol, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                mappings[idx] = mapping;
            else
                mappings.Add(mapping);

            var next = cfg with
            {
                DataSources = dataSources with
                {
                    SymbolMappings = symbolMappings with { Mappings = mappings.ToArray() }
                }
            };
            await store.SaveAsync(next);

            return Results.Ok();
        }).WithName("UpsertSymbolMapping").Produces(200).Produces(400);

        // Delete symbol mapping
        group.MapDelete(UiApiRoutes.SymbolMappings + "/{symbol}", async (ConfigStore store, string symbol) =>
        {
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var symbolMappings = dataSources.SymbolMappings ?? new SymbolMappingsConfig();
            var mappings = (symbolMappings.Mappings ?? Array.Empty<SymbolMappingConfig>()).ToList();

            var removed = mappings.RemoveAll(m =>
                string.Equals(m.CanonicalSymbol, symbol, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
                return Results.NotFound();

            var next = cfg with
            {
                DataSources = dataSources with
                {
                    SymbolMappings = symbolMappings with { Mappings = mappings.ToArray() }
                }
            };
            await store.SaveAsync(next);

            return Results.Ok();
        }).WithName("DeleteSymbolMapping").Produces(200).Produces(404);

        // Get single symbol mapping
        group.MapGet(UiApiRoutes.SymbolMappings + "/{symbol}", (ConfigStore store, string symbol) =>
        {
            var cfg = store.Load();
            var mapping = cfg.DataSources?.SymbolMappings?.Mappings?
                .FirstOrDefault(m => string.Equals(m.CanonicalSymbol, symbol, StringComparison.OrdinalIgnoreCase));

            if (mapping == null)
                return Results.NotFound();

            var response = new SymbolMappingResponse(
                CanonicalSymbol: mapping.CanonicalSymbol,
                IbSymbol: mapping.IbSymbol,
                AlpacaSymbol: mapping.AlpacaSymbol,
                PolygonSymbol: mapping.PolygonSymbol,
                YahooSymbol: mapping.YahooSymbol,
                Name: mapping.Name,
                Figi: mapping.Figi
            );

            return Results.Json(response, jsonOptions);
        }).WithName("GetSymbolMapping").Produces(200).Produces(404);

        // Import symbol mappings from CSV
        group.MapPost(UiApiRoutes.SymbolMappings + "/import", async (ConfigStore store, HttpRequest request) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Content must be multipart/form-data");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
                return Results.BadRequest("No file uploaded");

            var mappings = new List<SymbolMappingConfig>();

            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                var headerParsed = false;
                var providerColumns = new Dictionary<int, string>();

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(',');
                    if (!headerParsed)
                    {
                        // Parse header
                        for (var i = 1; i < parts.Length; i++)
                        {
                            providerColumns[i] = parts[i].Trim().ToLowerInvariant();
                        }
                        headerParsed = true;
                        continue;
                    }

                    if (parts.Length < 2)
                        continue;

                    var canonicalSymbol = parts[0].Trim();
                    if (string.IsNullOrWhiteSpace(canonicalSymbol))
                        continue;

                    string? ibSymbol = null, alpacaSymbol = null, polygonSymbol = null, yahooSymbol = null, name = null, figi = null;

                    foreach (var (colIndex, colName) in providerColumns)
                    {
                        if (colIndex < parts.Length)
                        {
                            var value = parts[colIndex].Trim();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                switch (colName)
                                {
                                    case "ib":
                                        ibSymbol = value;
                                        break;
                                    case "alpaca":
                                        alpacaSymbol = value;
                                        break;
                                    case "polygon":
                                        polygonSymbol = value;
                                        break;
                                    case "yahoo":
                                        yahooSymbol = value;
                                        break;
                                    case "name":
                                        name = value;
                                        break;
                                    case "figi":
                                        figi = value;
                                        break;
                                }
                            }
                        }
                    }

                    mappings.Add(new SymbolMappingConfig(
                        CanonicalSymbol: canonicalSymbol.ToUpperInvariant(),
                        IbSymbol: ibSymbol,
                        AlpacaSymbol: alpacaSymbol,
                        PolygonSymbol: polygonSymbol,
                        YahooSymbol: yahooSymbol,
                        Name: name,
                        Figi: figi
                    ));
                }
            }

            // Merge with existing mappings
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var symbolMappings = dataSources.SymbolMappings ?? new SymbolMappingsConfig();
            var existingMappings = (symbolMappings.Mappings ?? Array.Empty<SymbolMappingConfig>()).ToList();

            foreach (var mapping in mappings)
            {
                var idx = existingMappings.FindIndex(m =>
                    string.Equals(m.CanonicalSymbol, mapping.CanonicalSymbol, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    existingMappings[idx] = mapping;
                else
                    existingMappings.Add(mapping);
            }

            var next = cfg with
            {
                DataSources = dataSources with
                {
                    SymbolMappings = symbolMappings with { Mappings = existingMappings.ToArray() }
                }
            };
            await store.SaveAsync(next);

            return Results.Ok(new { imported = mappings.Count });
        }).WithName("ImportSymbolMappings").Produces(200).Produces(400);
    }
}
