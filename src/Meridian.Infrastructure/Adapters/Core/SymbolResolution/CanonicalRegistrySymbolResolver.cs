using System.Threading;
using Meridian.Contracts.Catalog;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core.SymbolResolution;

/// <summary>
/// Registry-first symbol resolver: answers from <see cref="ICanonicalSymbolRegistry"/>
/// when the identifier is already known, delegates to an optional inner resolver
/// (typically OpenFIGI) on misses, and registers successful inner resolutions back
/// into the registry so every subsequent lookup converges on the canonical spine.
/// </summary>
public sealed class CanonicalRegistrySymbolResolver : ISymbolResolver
{
    private readonly ICanonicalSymbolRegistry _registry;
    private readonly ISymbolResolver? _inner;
    private readonly ILogger _log;

    public CanonicalRegistrySymbolResolver(
        ICanonicalSymbolRegistry registry,
        ISymbolResolver? inner = null,
        ILogger? log = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _inner = inner;
        _log = log ?? LoggingSetup.ForContext<CanonicalRegistrySymbolResolver>();
    }

    public string Name => _inner is null ? "canonical-registry" : $"canonical-registry+{_inner.Name}";

    public async Task<SymbolResolution?> ResolveAsync(string symbol, string? exchange = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        if (_registry.GetDefinition(symbol.Trim()) is { } definition)
            return ToResolution(definition);

        if (_inner is null)
            return null;

        var resolved = await _inner.ResolveAsync(symbol, exchange, ct).ConfigureAwait(false);
        if (resolved is not null)
            await LearnAsync(symbol, resolved, ct).ConfigureAwait(false);

        return resolved;
    }

    public async Task<string?> MapSymbolAsync(string symbol, string fromProvider, string toProvider, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var trimmed = symbol.Trim();
        var canonical = _registry.TryResolveWithProvider(trimmed, fromProvider)
            ?? _registry.ResolveToCanonical(trimmed);
        if (canonical is not null)
            return canonical;

        if (_inner is null)
            return null;

        var mapped = await _inner.MapSymbolAsync(trimmed, fromProvider, toProvider, ct).ConfigureAwait(false);
        if (mapped is not null && !string.Equals(mapped, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            await LearnAliasAsync(trimmed, mapped, ct).ConfigureAwait(false);
        }

        return mapped;
    }

    public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
        => _inner?.SearchAsync(query, maxResults, ct)
            ?? Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);

    private async Task LearnAsync(string input, SymbolResolution resolution, CancellationToken ct)
    {
        try
        {
            var aliases = new List<string>(resolution.ProviderSymbols.Values) { input.Trim().ToUpperInvariant() };
            await _registry.RegisterAsync(new CanonicalSymbolDefinition
            {
                Canonical = resolution.Ticker,
                DisplayName = resolution.Name,
                Aliases = aliases
                    .Where(alias => !string.Equals(alias, resolution.Ticker, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Exchange = resolution.Exchange,
                Currency = resolution.Currency,
                Isin = resolution.Isin,
                Figi = resolution.Figi,
                CompositeFigi = resolution.CompositeFigi,
                Cusip = resolution.Cusip,
                Sedol = resolution.Sedol
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to register resolved symbol {Symbol} into the canonical registry", resolution.Ticker);
        }
    }

    private async Task LearnAliasAsync(string input, string mapped, CancellationToken ct)
    {
        try
        {
            await _registry.RegisterAsync(new CanonicalSymbolDefinition
            {
                Canonical = input.ToUpperInvariant(),
                Aliases = [mapped]
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to register alias {Alias} for {Symbol} into the canonical registry", mapped, input);
        }
    }

    private static SymbolResolution ToResolution(CanonicalSymbolDefinition definition)
        => new(
            definition.Canonical,
            Figi: definition.Figi,
            CompositeFigi: definition.CompositeFigi,
            Isin: definition.Isin,
            Cusip: definition.Cusip,
            Sedol: definition.Sedol,
            Name: definition.DisplayName,
            Exchange: definition.Exchange,
            Currency: definition.Currency);
}
