using System.Net.Http;
using System.Text;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>Reads and edits manual provider-symbol overrides through the host.</summary>
public interface ISymbolMappingClient
{
    IReadOnlyList<MappingProviderInfo> Providers { get; }
    Task LoadAsync(CancellationToken ct = default);
    IReadOnlyList<SymbolMapping> GetMappings();
    Dictionary<string, string> TestMapping(string canonicalSymbol);
    Task SetProviderSymbolAsync(string canonicalSymbol, string providerId, string providerSymbol, CancellationToken ct = default);
    Task RemoveMappingAsync(string canonicalSymbol, CancellationToken ct = default);
    Task<int> ImportFromCsvAsync(string csvContent, CancellationToken ct = default);
    Task<string> ExportToCsvAsync(CancellationToken ct = default);
}

/// <summary>Projects a host snapshot for display and posts mutations to the canonical registry.</summary>
public sealed class SymbolMappingApiClient(ApiClientService api) : ISymbolMappingClient
{
    private IReadOnlyList<SymbolMapping> _mappings = [];

    public IReadOnlyList<MappingProviderInfo> Providers => SymbolMappingService.KnownProviders;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var data = ProviderDiagnostics.HostResponse.RequireData(
            await api.GetWithResponseAsync<SymbolMappingResponse[]>(UiApiRoutes.SymbolMappings, ct).ConfigureAwait(false));
        _mappings = data.Where(row => row.ProviderSymbols is { Count: > 0 }).Select(row => new SymbolMapping
        {
            CanonicalSymbol = row.CanonicalSymbol,
            DisplayName = row.Name,
            Figi = row.Figi,
            ProviderSymbols = new Dictionary<string, string>(row.ProviderSymbols!, StringComparer.OrdinalIgnoreCase),
            IsCustomMapping = true,
            UpdatedAt = row.UpdatedAt?.UtcDateTime ?? default
        }).ToArray();
    }

    public IReadOnlyList<SymbolMapping> GetMappings() => _mappings;

    public Dictionary<string, string> TestMapping(string canonicalSymbol)
    {
        var row = _mappings.FirstOrDefault(mapping => string.Equals(mapping.CanonicalSymbol, canonicalSymbol, StringComparison.OrdinalIgnoreCase));
        return Providers.ToDictionary(provider => provider.Id, provider => row?.ProviderSymbols?.GetValueOrDefault(provider.Id)
            ?? SymbolMappingService.ApplyDefaultTransform(canonicalSymbol, provider.Id));
    }

    public async Task SetProviderSymbolAsync(string canonicalSymbol, string providerId, string providerSymbol, CancellationToken ct = default)
    {
        var path = MappingPath(canonicalSymbol) + "/providers/" + Uri.EscapeDataString(providerId.ToLowerInvariant());
        ProviderDiagnostics.HostResponse.EnsureSuccess(
            await api.PutWithResponseAsync<object>(path, new { providerSymbol }, ct).ConfigureAwait(false));
        await LoadAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveMappingAsync(string canonicalSymbol, CancellationToken ct = default)
    {
        ProviderDiagnostics.HostResponse.EnsureSuccess(
            await api.DeleteWithResponseAsync<object>(MappingPath(canonicalSymbol), ct).ConfigureAwait(false));
        await LoadAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ImportFromCsvAsync(string csvContent, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(csvContent, Encoding.UTF8, "text/csv"), "file", "symbol-mappings.csv");
        var result = ProviderDiagnostics.HostResponse.RequireData(await api.PostMultipartWithResponseAsync<ImportResult>(
            UiApiRoutes.SymbolMappings + "/import", form, ct).ConfigureAwait(false));
        await LoadAsync(ct).ConfigureAwait(false);
        return result.Imported;
    }

    public async Task<string> ExportToCsvAsync(CancellationToken ct = default)
        => ProviderDiagnostics.HostResponse.RequireData(await api.GetTextWithResponseAsync(
            UiApiRoutes.SymbolMappings + "/csv/export", ct).ConfigureAwait(false));

    private static string MappingPath(string canonicalSymbol) => $"{UiApiRoutes.SymbolMappings}/{Uri.EscapeDataString(canonicalSymbol)}";

    private sealed record ImportResult(int Imported);
}
