using System.Text;
using System.Text.Json;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Catalog;
using Meridian.Identity.Auth;
using Meridian.Storage.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Registry-backed symbol mapping endpoints shared by browser and desktop hosts.
/// Legacy configuration remains a migration/rollback input but is not a second write model.
/// </summary>
public static partial class SymbolMappingEndpoints
{
    private static readonly string[] LegacyProviders = ["ib", "alpaca", "polygon", "yahoo"];
    private static readonly (string Id, string Label)[] MappingProviders =
    [
        ("ib", "Interactive Brokers"),
        ("alpaca", "Alpaca Markets"),
        ("polygon", "Polygon.io"),
        ("yahoo", "Yahoo Finance"),
        ("stooq", "Stooq"),
        ("tiingo", "Tiingo"),
        ("finnhub", "Finnhub"),
        ("alphavantage", "Alpha Vantage"),
        ("nyse", "NYSE")
    ];

    public static void MapSymbolMappingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        MapRolloutEndpoints(app, jsonOptions);
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

            var mappings = new[]
            {
                ("ib", req.IbSymbol), ("alpaca", req.AlpacaSymbol),
                ("polygon", req.PolygonSymbol), ("yahoo", req.YahooSymbol)
            };
            await SaveMappingsAsync(registry,
                [CreateOperatorDefinition(req.CanonicalSymbol, req.Name, req.Figi, mappings)], ct)
                .ConfigureAwait(false);
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
            var removed = await DeleteMappingsAsync(registry, symbol, provider: null, ct).ConfigureAwait(false);
            return removed ? Results.Ok() : Results.NotFound();
        }).WithName("DeleteSymbolMapping").Produces(200).Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        const string providerMappingRoute = UiApiRoutes.SymbolMappings + "/{symbol}/providers/{provider}";
        group.MapPut(providerMappingRoute, async (
            HttpContext context, ICanonicalSymbolRegistry registry,
            string symbol, string provider, SymbolProviderOverrideRequest request, CancellationToken ct) =>
        {
            if (!IsValidProviderId(provider) || string.IsNullOrWhiteSpace(request.ProviderSymbol))
                return ApiProblemDetails.Validation(context, "mapping", "A provider ID and provider symbol are required.");

            await SaveMappingsAsync(registry,
                [CreateOperatorDefinition(symbol, null, null, [(provider, request.ProviderSymbol)])], ct)
                .ConfigureAwait(false);
            return Results.Ok();
        }).WithName("SetSymbolProviderOverride").Produces(200).ProducesValidationProblem()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        group.MapDelete(providerMappingRoute, async (
            ICanonicalSymbolRegistry registry, string symbol, string provider, CancellationToken ct) =>
        {
            var removed = await DeleteMappingsAsync(registry, symbol, provider, ct).ConfigureAwait(false);
            return removed ? Results.Ok() : Results.NotFound();
        }).WithName("DeleteSymbolProviderOverride").Produces(200).Produces(404)
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

            using var stream = file.OpenReadStream();
            using var parser = new TextFieldParser(stream) { HasFieldsEnclosedInQuotes = true };
            parser.SetDelimiters(",");
            List<CanonicalSymbolDefinition> definitions = [];
            try
            {
                var columns = parser.ReadFields() ?? [];
                var canonicalIndex = FindColumn(columns, "canonical", "canonicalSymbol", "symbol");
                if (canonicalIndex < 0)
                    return Results.BadRequest("A Canonical, CanonicalSymbol, or Symbol column is required.");

                var nameIndex = FindColumn(columns, "name", "displayName");
                var figiIndex = FindColumn(columns, "figi");
                var providerColumns = MappingProviders.Select(p => (p.Id, Index: FindColumn(columns, p.Id, p.Label)))
                    .Where(column => column.Index >= 0)
                    .ToArray();
                while (parser.ReadFields() is { } fields)
                {
                    ct.ThrowIfCancellationRequested();
                    if (GetColumn(fields, canonicalIndex) is { } canonical)
                    {
                        definitions.Add(CreateOperatorDefinition(canonical, GetColumn(fields, nameIndex),
                            GetColumn(fields, figiIndex), providerColumns.Select(column =>
                                (column.Id, GetColumn(fields, column.Index)))));
                    }
                }
            }
            catch (MalformedLineException)
            {
                return Results.BadRequest("The CSV contains a malformed row.");
            }

            var imported = await SaveMappingsAsync(registry, definitions, ct).ConfigureAwait(false);

            return Results.Ok(new { imported });
        }).WithName("ImportSymbolMappings").Produces(200).Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        group.MapGet(UiApiRoutes.SymbolMappings + "/csv/export", (ICanonicalSymbolRegistry registry) =>
        {
            var csv = new StringBuilder("Canonical,Name,Figi,")
                .AppendJoin(',', MappingProviders.Select(provider => provider.Id))
                .AppendLine();
            foreach (var mapping in registry.GetAll().Select(ToResponse).Where(row => row.ProviderSymbols is { Count: > 0 }))
            {
                var values = new[] { mapping.CanonicalSymbol, mapping.Name, mapping.Figi }
                    .Concat(MappingProviders.Select(provider => mapping.ProviderSymbols!.GetValueOrDefault(provider.Id)));
                csv.AppendJoin(',', values.Select(EscapeCsv)).AppendLine();
            }

            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "symbol-mappings.csv");
        }).WithName("ExportSymbolMappings").Produces(200, contentType: "text/csv");
    }

    private static SymbolMappingResponse ToResponse(CanonicalSymbolDefinition definition)
        => new(
            CanonicalSymbol: definition.Canonical,
            IbSymbol: GetProviderSymbol(definition, "ib"),
            AlpacaSymbol: GetProviderSymbol(definition, "alpaca"),
            PolygonSymbol: GetProviderSymbol(definition, "polygon"),
            YahooSymbol: GetProviderSymbol(definition, "yahoo"),
            Name: definition.DisplayName,
            Figi: definition.Figi)
        {
            ProviderSymbols = definition.ProviderSymbols.Where(entry => entry.Value.IsOverride)
                .ToDictionary(entry => entry.Key, entry => entry.Value.Symbol, StringComparer.OrdinalIgnoreCase),
            UpdatedAt = definition.ProviderSymbols.Values.Where(mapping => mapping.IsOverride)
                .Select(mapping => mapping.UpdatedAt).Max()
        };

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

    private static CanonicalSymbolDefinition CreateOperatorDefinition(
        string canonical, string? name, string? figi, IEnumerable<(string Provider, string? Symbol)> mappings)
        => new()
        {
            Canonical = canonical.Trim().ToUpperInvariant(),
            DisplayName = name,
            Figi = figi,
            ProviderSymbols = mappings.Where(mapping => !string.IsNullOrWhiteSpace(mapping.Symbol))
                .ToDictionary(mapping => mapping.Provider.Trim().ToLowerInvariant(),
                    mapping => new ProviderSymbolDefinition(mapping.Symbol!.Trim(), SymbolMappingSources.Operator, IsOverride: true),
                    StringComparer.OrdinalIgnoreCase)
        };

    private static Task<int> SaveMappingsAsync(
        ICanonicalSymbolRegistry registry, IReadOnlyList<CanonicalSymbolDefinition> definitions, CancellationToken ct)
        => definitions.Count == 0
            ? Task.FromResult(0)
            : registry is ICanonicalSymbolOperatorMappingWriter writer
                ? writer.SaveOperatorMappingsAsync(definitions, ct)
                : registry.RegisterBatchAsync(definitions, ct);

    private static async Task<bool> DeleteMappingsAsync(
        ICanonicalSymbolRegistry registry, string canonical, string? provider, CancellationToken ct)
    {
        if (registry is ICanonicalSymbolOperatorMappingWriter writer)
            return await writer.RemoveOperatorMappingsAsync(canonical, provider, ct).ConfigureAwait(false) > 0;

        var definition = registry.GetDefinition(canonical);
        if (definition is null)
            return false;

        var removed = false;
        foreach (var (providerId, mapping) in definition.ProviderSymbols)
        {
            if (mapping.IsOverride && (provider is null || providerId.Equals(provider, StringComparison.OrdinalIgnoreCase)))
                removed |= await registry.RemoveProviderSymbolAsync(definition.Canonical, providerId, ct).ConfigureAwait(false);
        }

        return removed;
    }

    private static int FindColumn(string[] columns, params string[] names)
        => Array.FindIndex(columns, column => names.Contains(column.Trim(), StringComparer.OrdinalIgnoreCase));

    private static string? GetColumn(string[] fields, int index)
        => index >= 0 && index < fields.Length && !string.IsNullOrWhiteSpace(fields[index])
            ? fields[index].Trim()
            : null;

    private static string EscapeCsv(string? value)
        => value is null ? string.Empty : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static bool IsValidProviderId(string id)
        => id is { Length: > 0 and <= 100 }
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
