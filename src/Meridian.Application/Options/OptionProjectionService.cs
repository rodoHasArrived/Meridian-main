using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Options;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.Options;

public sealed class OptionProjectionService : IOptionReferenceService, IOptionChainImportService
{
    private readonly IOptionReferenceProjectionStore _projectionStore;

    public OptionProjectionService(IOptionReferenceProjectionStore projectionStore)
    {
        _projectionStore = projectionStore;
    }

    public async Task<OptionContractReferenceDto?> GetContractAsync(string contractSymbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contractSymbol))
        {
            return null;
        }

        var row = await _projectionStore.GetContractAsync(NormalizeIdentifier(contractSymbol), ct).ConfigureAwait(false);
        return row is null ? null : MapContract(row);
    }

    public async Task<OptionSeriesDto?> GetSeriesAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(optionChainId))
        {
            return null;
        }

        var normalizedOptionChainId = NormalizeIdentifier(optionChainId);
        var rows = await _projectionStore
            .GetSeriesContractsAsync(normalizedOptionChainId, expiryDate, ct)
            .ConfigureAwait(false);
        return rows.Count == 0 ? null : MapSeries(rows, normalizedOptionChainId, expiryDate);
    }

    public async Task<OptionChainSnapshotDto?> GetChainSnapshotAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol))
        {
            return null;
        }

        var normalizedUnderlyingSymbol = NormalizeIdentifier(underlyingSymbol);
        var rows = await _projectionStore
            .GetUnderlyingContractsAsync(normalizedUnderlyingSymbol, expiryDate, ct)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return null;
        }

        var grouped = rows
            .GroupBy(r => r.OptionChainId, StringComparer.OrdinalIgnoreCase)
            .Select(group => MapSeries(group.ToList(), group.Key, expiryDate))
            .ToArray();

        return new OptionChainSnapshotDto(
            grouped[0].OptionChainId,
            normalizedUnderlyingSymbol,
            expiryDate,
            rows.Max(r => r.LastUpdatedUtc),
            rows.Count,
            grouped);
    }

    public async Task<OptionContractReferenceDto?> GetUnderlyingLinkageAsync(string contractSymbol, CancellationToken ct = default)
    {
        var contract = await GetContractAsync(contractSymbol, ct).ConfigureAwait(false);
        if (contract is null || string.IsNullOrWhiteSpace(contract.UnderlyingSymbol))
        {
            return null;
        }

        return contract;
    }

    public Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(underlyingSymbol)
            ? Task.FromResult<IReadOnlyList<DateOnly>>(Array.Empty<DateOnly>())
            : _projectionStore.GetExpiryLadderAsync(NormalizeIdentifier(underlyingSymbol), ct);

    public async Task<OptionChainSnapshotDto> ImportSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _projectionStore.UpsertChainSnapshotAsync(snapshot, ct).ConfigureAwait(false);

        var normalizedUnderlyingSymbol = NormalizeIdentifier(snapshot.UnderlyingSymbol);
        var chainId = BuildOptionChainId(normalizedUnderlyingSymbol, snapshot.Expiration);
        var series = await GetSeriesAsync(chainId, snapshot.Expiration, ct).ConfigureAwait(false);

        return new OptionChainSnapshotDto(
            chainId,
            normalizedUnderlyingSymbol,
            snapshot.Expiration,
            snapshot.Timestamp,
            snapshot.TotalContracts,
            series is null ? Array.Empty<OptionSeriesDto>() : [series]);
    }

    private static OptionContractReferenceDto MapContract(OptionContractProjectionRow row)
        => new(
            row.ContractSymbol,
            row.SecurityId,
            row.OptionChainId,
            row.UnderlyingSymbol,
            row.UnderlyingSecurityId,
            row.PutCall,
            row.Strike,
            row.ExpiryDate,
            row.Multiplier,
            row.IsAdjusted,
            row.LastTradingDate,
            row.Version);

    private static OptionSeriesDto MapSeries(
        IReadOnlyList<OptionContractProjectionRow> rows,
        string optionChainId,
        DateOnly expiryDate)
    {
        var ordered = rows
            .OrderBy(r => r.Strike)
            .ThenBy(r => r.PutCall, StringComparer.OrdinalIgnoreCase)
            .Select(MapContract)
            .ToArray();

        return new OptionSeriesDto(optionChainId, rows[0].UnderlyingSymbol, expiryDate, ordered);
    }

    private static string BuildOptionChainId(string underlyingSymbol, DateOnly expiryDate)
        => $"{NormalizeIdentifier(underlyingSymbol)}-{expiryDate:yyyyMMdd}";

    private static string NormalizeIdentifier(string value)
        => value.Trim().ToUpperInvariant();
}

public sealed class NullOptionReferenceService : IOptionReferenceService
{
    public Task<OptionContractReferenceDto?> GetContractAsync(string contractSymbol, CancellationToken ct = default)
        => Task.FromResult<OptionContractReferenceDto?>(null);

    public Task<OptionSeriesDto?> GetSeriesAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default)
        => Task.FromResult<OptionSeriesDto?>(null);

    public Task<OptionChainSnapshotDto?> GetChainSnapshotAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default)
        => Task.FromResult<OptionChainSnapshotDto?>(null);

    public Task<OptionContractReferenceDto?> GetUnderlyingLinkageAsync(string contractSymbol, CancellationToken ct = default)
        => Task.FromResult<OptionContractReferenceDto?>(null);

    public Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DateOnly>>(Array.Empty<DateOnly>());
}

public sealed class NullOptionChainImportService : IOptionChainImportService
{
    public Task<OptionChainSnapshotDto> ImportSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return Task.FromResult(new OptionChainSnapshotDto(
            $"{snapshot.UnderlyingSymbol.Trim().ToUpperInvariant()}-{snapshot.Expiration:yyyyMMdd}",
            snapshot.UnderlyingSymbol.Trim().ToUpperInvariant(),
            snapshot.Expiration,
            snapshot.Timestamp,
            snapshot.TotalContracts,
            Array.Empty<OptionSeriesDto>()));
    }
}
