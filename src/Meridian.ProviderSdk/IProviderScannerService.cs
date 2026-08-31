using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.ProviderSdk;

/// <summary>Provider-neutral request for a ranked market scanner query.</summary>
public sealed record ProviderScannerRequest(
    string ScanCode,
    string? Instrument = null,
    string? Location = null,
    int? Limit = null,
    IReadOnlyDictionary<string, string>? Filters = null);

/// <summary>Optional provider capability for retrieving scanner results.</summary>
public interface IProviderScannerService : IProviderMetadata
{
    Task<IReadOnlyList<ProviderScannerResult>> ScanAsync(ProviderScannerRequest request, CancellationToken ct = default);
}
