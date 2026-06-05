using System.Text.Json;
using Meridian.Contracts.Derivatives;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.Derivatives;

public sealed class SwapProjectionService : ISwapReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly ISwapReferenceProjectionStore _projectionStore;

    public SwapProjectionService(
        ISecurityMasterStore securityMasterStore,
        ISwapReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<SwapReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "Swap", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetSwapAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row, security.AssetSpecificTerms);
    }

    public async Task<IReadOnlyList<SwapReferenceDto>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(swapType))
        {
            return Array.Empty<SwapReferenceDto>();
        }

        var rows = await _projectionStore.GetBySwapTypeAsync(swapType.Trim(), ct).ConfigureAwait(false);
        return await MapRowsWithSecurityTermsAsync(rows, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SwapReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
        return await MapRowsWithSecurityTermsAsync(rows, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SwapReferenceDto>> MapRowsWithSecurityTermsAsync(
        IReadOnlyList<SwapProjectionRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<SwapReferenceDto>();
        }

        var results = new List<SwapReferenceDto>(rows.Count);
        foreach (var row in rows)
        {
            var security = await _securityMasterStore.GetProjectionAsync(row.SecurityId, ct).ConfigureAwait(false);
            var terms = security is not null &&
                string.Equals(security.AssetClass, "Swap", StringComparison.OrdinalIgnoreCase)
                    ? security.AssetSpecificTerms
                    : default;
            results.Add(MapRow(row, terms));
        }

        return results;
    }

    private static SwapReferenceDto MapRow(SwapProjectionRow row, JsonElement assetSpecificTerms)
    {
        var legs = ParseLegs(assetSpecificTerms);
        return new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.SwapType,
            row.EffectiveDate,
            row.MaturityDate,
            row.LifecycleStat,
            legs,
            row.PrimaryIdentifierValue,
            row.Version);
    }

    private static IReadOnlyList<SwapLegDto> ParseLegs(JsonElement assetSpecificTerms)
    {
        if (assetSpecificTerms.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<SwapLegDto>();
        }

        if (!assetSpecificTerms.TryGetProperty("legs", out var legsElement) ||
            legsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SwapLegDto>();
        }

        var result = new List<SwapLegDto>(legsElement.GetArrayLength());
        foreach (var leg in legsElement.EnumerateArray())
        {
            var legType = leg.TryGetProperty("legType", out var lt) ? lt.GetString() ?? string.Empty : string.Empty;
            var currency = leg.TryGetProperty("currency", out var cur) ? cur.GetString() ?? string.Empty : string.Empty;
            var index = leg.TryGetProperty("index", out var idx) && idx.ValueKind != JsonValueKind.Null ? idx.GetString() : null;
            decimal? fixedRate = null;
            if (leg.TryGetProperty("fixedRate", out var fr) && fr.ValueKind == JsonValueKind.Number)
            {
                fixedRate = fr.GetDecimal();
            }

            result.Add(new SwapLegDto(legType, currency, index, fixedRate));
        }

        return result;
    }
}

public sealed class NullSwapReferenceService : ISwapReferenceService
{
    public Task<SwapReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<SwapReferenceDto?>(null);

    public Task<IReadOnlyList<SwapReferenceDto>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SwapReferenceDto>>(Array.Empty<SwapReferenceDto>());

    public Task<IReadOnlyList<SwapReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SwapReferenceDto>>(Array.Empty<SwapReferenceDto>());
}
