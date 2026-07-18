using Meridian.Contracts.Catalog;
using Meridian.Core.Logging;
using Meridian.Infrastructure.Utilities;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core.SymbolResolution;

/// <summary>
/// Registry-first symbol resolver. Provider mappings remain provider-scoped and successful
/// enrichment is merged back into the durable registry without replacing curated identity data.
/// </summary>
public sealed class CanonicalRegistrySymbolResolver : ISymbolResolver, IDisposable
{
    private readonly ICanonicalSymbolRegistry _registry;
    private readonly ISymbolResolver? _inner;
    private readonly SymbolResolutionMode _mode;
    private readonly Action<SymbolResolutionMismatch>? _mismatchObserver;
    private readonly ILogger _log;
    private bool _disposed;

    public CanonicalRegistrySymbolResolver(
        ICanonicalSymbolRegistry registry,
        ISymbolResolver? inner = null,
        ILogger? log = null,
        SymbolResolutionMode mode = SymbolResolutionMode.Canonical,
        Action<SymbolResolutionMismatch>? mismatchObserver = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _inner = inner;
        _mode = mode;
        _mismatchObserver = mismatchObserver;
        _log = log ?? LoggingSetup.ForContext<CanonicalRegistrySymbolResolver>();
    }

    public string Name => _inner is null ? "canonical-registry" : $"canonical-registry+{_inner.Name}";

    public async Task<SymbolResolution?> ResolveAsync(
        string symbol,
        string? exchange = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    public async Task<string?> MapSymbolAsync(
        string symbol,
        string fromProvider,
        string toProvider,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var trimmed = symbol.Trim();
        if (_mode == SymbolResolutionMode.Legacy)
            return await ResolveLegacyAsync(trimmed, fromProvider, toProvider, ct).ConfigureAwait(false);

        var canonicalResult = await ResolveCanonicalMappingAsync(
            trimmed,
            fromProvider,
            toProvider,
            ct).ConfigureAwait(false);

        if (_mode == SymbolResolutionMode.Canonical)
        {
            return canonicalResult
                ?? await ResolveLegacyAsync(trimmed, fromProvider, toProvider, ct).ConfigureAwait(false);
        }

        var legacyResult = await ResolveLegacyAsync(trimmed, fromProvider, toProvider, ct).ConfigureAwait(false);
        if (!string.Equals(legacyResult, canonicalResult, StringComparison.OrdinalIgnoreCase))
            ReportMismatch(trimmed, fromProvider, toProvider, legacyResult, canonicalResult);

        // Migration safety: comparison mode observes the new result but does not change behavior.
        return legacyResult;
    }

    public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
        => _inner?.SearchAsync(query, maxResults, ct)
            ?? Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);

    private async Task<string?> ResolveCanonicalMappingAsync(
        string symbol,
        string fromProvider,
        string toProvider,
        CancellationToken ct)
    {
        var canonical = _registry.TryResolveWithProvider(symbol, fromProvider)
            ?? _registry.ResolveToCanonical(symbol);

        if (canonical is not null)
        {
            return _registry.GetProviderSymbol(canonical, toProvider)
                ?? SymbolNormalization.NormalizeForProvider(canonical, toProvider);
        }

        if (_inner is null)
            return null;

        var resolution = await _inner.ResolveAsync(symbol, ct: ct).ConfigureAwait(false);
        if (resolution is null)
            return null;

        await LearnAsync(symbol, resolution, ct).ConfigureAwait(false);
        if (resolution.ProviderSymbols.TryGetValue(NormalizeProvider(toProvider), out var providerSymbol))
            return providerSymbol;

        return SymbolNormalization.NormalizeForProvider(resolution.Ticker, toProvider);
    }

    private Task<string?> ResolveLegacyAsync(
        string symbol,
        string fromProvider,
        string toProvider,
        CancellationToken ct)
        => _inner?.MapSymbolAsync(symbol, fromProvider, toProvider, ct)
            ?? Task.FromResult<string?>(null);

    private async Task LearnAsync(string input, SymbolResolution resolution, CancellationToken ct)
    {
        try
        {
            var providerSymbols = resolution.ProviderSymbols
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(
                    static pair => NormalizeProvider(pair.Key),
                    static pair => new ProviderSymbolDefinition(
                        pair.Value,
                        SymbolMappingSources.OpenFigi,
                        IsOverride: false,
                        UpdatedAt: DateTimeOffset.UtcNow),
                    StringComparer.OrdinalIgnoreCase);

            await _registry.RegisterAsync(new CanonicalSymbolDefinition
            {
                Canonical = resolution.Ticker.Trim().ToUpperInvariant(),
                DisplayName = resolution.Name,
                Aliases = [input.Trim()],
                ProviderSymbols = providerSymbols,
                Exchange = resolution.Exchange,
                Currency = resolution.Currency,
                Isin = resolution.Isin,
                Figi = resolution.Figi,
                CompositeFigi = resolution.CompositeFigi,
                Cusip = resolution.Cusip,
                Sedol = resolution.Sedol
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning(ex, "Failed to register resolved symbol {Symbol} into the canonical registry", resolution.Ticker);
        }
    }

    private void ReportMismatch(
        string input,
        string fromProvider,
        string toProvider,
        string? legacyResult,
        string? canonicalResult)
    {
        var securityId = _registry.GetDefinition(input)?.SecurityId;
        var mismatch = new SymbolResolutionMismatch(
            input,
            NormalizeProvider(fromProvider),
            NormalizeProvider(toProvider),
            legacyResult,
            canonicalResult,
            securityId,
            DateTimeOffset.UtcNow);

        _log.Warning(
            "Symbol resolution mismatch for {Input} from {FromProvider} to {ToProvider}: legacy={LegacyResult}, canonical={CanonicalResult}, securityId={SecurityId}",
            input,
            fromProvider,
            toProvider,
            legacyResult,
            canonicalResult,
            securityId);
        _mismatchObserver?.Invoke(mismatch);
    }

    private static SymbolResolution ToResolution(CanonicalSymbolDefinition definition)
    {
        var resolution = new SymbolResolution(
            definition.Canonical,
            Figi: definition.Figi,
            CompositeFigi: definition.CompositeFigi,
            Isin: definition.Isin,
            Cusip: definition.Cusip,
            Sedol: definition.Sedol,
            Name: definition.DisplayName,
            Exchange: definition.Exchange,
            Currency: definition.Currency);

        foreach (var (provider, providerSymbol) in definition.ProviderSymbols)
            resolution.ProviderSymbols[NormalizeProvider(provider)] = providerSymbol.Symbol;

        return resolution;
    }

    private static string NormalizeProvider(string provider)
        => string.IsNullOrWhiteSpace(provider) ? string.Empty : provider.Trim().ToLowerInvariant();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        (_inner as IDisposable)?.Dispose();
    }
}
