using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.UI;
using Meridian.Contracts.Catalog;
using Meridian.Core.Config;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Idempotently imports both legacy symbol-mapping stores into the canonical registry.
/// Legacy inputs are intentionally retained as comparison and rollback sources.
/// </summary>
public sealed class CanonicalSymbolRegistryMigrationService : IHostedService
{
    private const string MigrationId = "canonical-symbol-spine-v1";
    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConfigStore _configStore;
    private readonly ICanonicalSymbolRegistry _registry;
    private readonly ISymbolRegistryService _registryStore;
    private readonly ILogger<CanonicalSymbolRegistryMigrationService> _logger;

    public CanonicalSymbolRegistryMigrationService(
        ConfigStore configStore,
        ICanonicalSymbolRegistry registry,
        ISymbolRegistryService registryStore,
        ILogger<CanonicalSymbolRegistryMigrationService> logger)
    {
        _configStore = configStore;
        _registry = registry;
        _registryStore = registryStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configStore.Load();
        var inlineMappings = config.DataSources?.SymbolMappings?.Mappings ?? [];
        var externalInputs = await LoadExternalInputsAsync(config, cancellationToken).ConfigureAwait(false);
        var fingerprint = ComputeFingerprint(inlineMappings, externalInputs);

        var completedFingerprint = await _registryStore
            .GetMigrationMarkerAsync(MigrationId, cancellationToken)
            .ConfigureAwait(false);
        if (completedFingerprint is not null &&
            string.Equals(completedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        var imported = 0;
        foreach (var mapping in inlineMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportAsync(ToDefinition(mapping), cancellationToken).ConfigureAwait(false);
            imported++;
        }

        foreach (var external in externalInputs.SelectMany(static input => input.Mappings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(external.CanonicalSymbol))
                continue;

            await ImportAsync(new CanonicalSymbolDefinition
            {
                Canonical = external.CanonicalSymbol.Trim().ToUpperInvariant(),
                DisplayName = external.DisplayName,
                AssetClass = ToAssetClass(external.SecurityType),
                Exchange = external.PrimaryExchange,
                Currency = external.Currency,
                Figi = external.Figi,
                Isin = external.Isin,
                Cusip = external.Cusip,
                ProviderSymbols = ToProviderDefinitions(external.ProviderSymbols)
            }, cancellationToken).ConfigureAwait(false);
            imported++;
        }

        await _registryStore
            .SetMigrationMarkerAsync(MigrationId, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Imported {Count} legacy symbol mappings into the canonical registry; legacy sources were retained for comparison and rollback.",
            imported);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task ImportAsync(CanonicalSymbolDefinition definition, CancellationToken ct)
        => string.IsNullOrWhiteSpace(definition.Canonical)
            ? Task.CompletedTask
            : _registry.RegisterAsync(definition, ct);

    private async Task<IReadOnlyList<LegacyExternalInput>> LoadExternalInputsAsync(
        AppConfig config,
        CancellationToken ct)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuredPath = config.DataSources?.SymbolMappings?.PersistencePath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configDirectory = Path.GetDirectoryName(_configStore.ConfigPath) ?? Environment.CurrentDirectory;
            paths.Add(Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(configDirectory, configuredPath)));
        }

        paths.Add(Path.Combine(_configStore.GetDataRoot(config), "_config", "symbol-mappings.json"));
        paths.Add(Path.Combine(AppContext.BaseDirectory, "data", "_config", "symbol-mappings.json"));

        var inputs = new List<LegacyExternalInput>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                var document = JsonSerializer.Deserialize<LegacyExternalMappings>(json, LegacyJsonOptions);
                inputs.Add(new LegacyExternalInput(path, json, document?.Mappings ?? []));
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipped malformed legacy symbol mapping file {Path}", path);
            }
        }

        return inputs;
    }

    private static CanonicalSymbolDefinition ToDefinition(SymbolMappingConfig mapping)
        => new()
        {
            Canonical = mapping.CanonicalSymbol.Trim().ToUpperInvariant(),
            DisplayName = mapping.Name,
            Figi = mapping.Figi,
            ProviderSymbols = ToProviderDefinitions(new Dictionary<string, string?>
            {
                ["ib"] = mapping.IbSymbol,
                ["alpaca"] = mapping.AlpacaSymbol,
                ["polygon"] = mapping.PolygonSymbol,
                ["yahoo"] = mapping.YahooSymbol
            })
        };

    private static IReadOnlyDictionary<string, ProviderSymbolDefinition> ToProviderDefinitions(
        IReadOnlyDictionary<string, string?>? providerSymbols)
        => providerSymbols?
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                static pair => pair.Key.Trim().ToLowerInvariant(),
                static pair => new ProviderSymbolDefinition(
                    pair.Value!.Trim(),
                    SymbolMappingSources.LegacyConfig,
                    IsOverride: true,
                    UpdatedAt: DateTimeOffset.UtcNow),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ProviderSymbolDefinition>(StringComparer.OrdinalIgnoreCase);

    private static string ComputeFingerprint(
        IReadOnlyList<SymbolMappingConfig> inlineMappings,
        IReadOnlyList<LegacyExternalInput> externalInputs)
    {
        var builder = new StringBuilder(JsonSerializer.Serialize(inlineMappings));
        foreach (var input in externalInputs.OrderBy(static input => input.Path, StringComparer.OrdinalIgnoreCase))
            builder.Append('\n').Append(input.Path).Append('\n').Append(input.Json);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string ToAssetClass(string? securityType)
        => securityType?.Trim().ToUpperInvariant() switch
        {
            "OPT" => "option",
            "FUT" => "future",
            "CASH" or "FX" => "forex",
            "CRYPTO" => "crypto",
            _ => "equity"
        };

    private sealed record LegacyExternalInput(
        string Path,
        string Json,
        IReadOnlyList<LegacyExternalMapping> Mappings);

    private sealed class LegacyExternalMappings
    {
        public LegacyExternalMapping[]? Mappings { get; set; }
    }

    private sealed class LegacyExternalMapping
    {
        public string CanonicalSymbol { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? SecurityType { get; set; }
        public string? PrimaryExchange { get; set; }
        public string? Currency { get; set; }
        public string? Figi { get; set; }
        public string? Isin { get; set; }
        public string? Cusip { get; set; }
        public Dictionary<string, string?>? ProviderSymbols { get; set; }
    }
}
