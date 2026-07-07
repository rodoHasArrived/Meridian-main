using Meridian.Application.UI;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Coverage source exposing the streaming symbols configured in AppConfig, so the
/// security-master coverage sweep can flag configured-but-unmastered instruments.
/// Returns an empty set when no config store is available (e.g. minimal test hosts).
/// </summary>
public sealed class ConfiguredSymbolCoverageSource : ISecurityCoverageSymbolSource
{
    private readonly ConfigStore? _configStore;

    public ConfiguredSymbolCoverageSource(ConfigStore? configStore = null)
    {
        _configStore = configStore;
    }

    public string SourceName => "configured-streaming";

    public Task<IReadOnlyCollection<string>> GetActiveSymbolsAsync(CancellationToken ct = default)
    {
        if (_configStore is null)
        {
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }

        var symbols = (_configStore.Load().Symbols ?? [])
            .Select(static config => config.Symbol)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<string>>(symbols);
    }
}
