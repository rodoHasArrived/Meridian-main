using System.Text.Json;
using Meridian.ProviderSdk;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Store;

public sealed record IBDataResultStoreOptions
{
    public required string DataRoot { get; init; }
}

/// <summary>
/// Crash-safe IB result store. Each upsert replaces one complete atomic snapshot, so an interrupted
/// write leaves either the previous valid result set or the new valid result set available at restart.
/// </summary>
public sealed class JsonFileIBDataResultStore : IIBDataResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileIBDataResultStore(IBDataResultStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataRoot);
        _path = Path.Combine(options.DataRoot, "provider-results", "interactive-brokers", "results.json");
    }

    public async ValueTask UpsertAsync(IBDataResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.ResultIdentity);
        result = result with
        {
            TenantId = result.TenantId.Trim(),
            CompanyId = result.CompanyId.Trim(),
            ResultIdentity = result.ResultIdentity.Trim()
        };
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var values = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            values[CreateKey(result.TenantId, result.CompanyId, result.ResultIdentity)] = result;
            var ordered = values.Values
                .OrderBy(x => x.TenantId, StringComparer.Ordinal)
                .ThenBy(x => x.CompanyId, StringComparer.Ordinal)
                .ThenBy(x => x.CapturedAt)
                .ThenBy(x => x.ResultIdentity, StringComparer.Ordinal)
                .ToArray();
            await AtomicFileWriter.WriteAsync(_path, JsonSerializer.Serialize(ordered, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<IBDataResult>> QueryAsync(IBDataResultQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.CompanyId);
        if (query.Limit < 1)
            throw new ArgumentOutOfRangeException(nameof(query), "Limit must be positive.");
        if (query.CapturedFrom > query.CapturedTo)
            throw new ArgumentException("CapturedFrom must not be after CapturedTo.", nameof(query));
        var tenantId = query.TenantId.Trim();
        var companyId = query.CompanyId.Trim();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false)).Values
                .Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal))
                .Where(x => string.Equals(x.CompanyId, companyId, StringComparison.Ordinal))
                .Where(x => query.Capability is null || string.Equals(x.Capability, query.Capability, StringComparison.OrdinalIgnoreCase))
                .Where(x => query.RequestIdentity is null || string.Equals(x.RequestIdentity, query.RequestIdentity, StringComparison.Ordinal))
                .Where(x => query.Symbol is null || string.Equals(x.Symbol, query.Symbol, StringComparison.OrdinalIgnoreCase))
                .Where(x => query.AccountId is null || string.Equals(x.AccountId, query.AccountId, StringComparison.OrdinalIgnoreCase))
                .Where(x => !query.CapturedFrom.HasValue || x.CapturedAt >= query.CapturedFrom)
                .Where(x => !query.CapturedTo.HasValue || x.CapturedAt <= query.CapturedTo)
                .OrderBy(x => x.CapturedAt).ThenBy(x => x.ResultIdentity, StringComparer.Ordinal).Take(query.Limit).ToArray();
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, IBDataResult>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new(StringComparer.Ordinal);
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var values = await JsonSerializer.DeserializeAsync<IBDataResult[]>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        return values
            .Where(static x =>
                !string.IsNullOrWhiteSpace(x.TenantId)
                && !string.IsNullOrWhiteSpace(x.CompanyId)
                && !string.IsNullOrWhiteSpace(x.ResultIdentity))
            .Select(static x => x with
            {
                TenantId = x.TenantId.Trim(),
                CompanyId = x.CompanyId.Trim(),
                ResultIdentity = x.ResultIdentity.Trim()
            })
            .ToDictionary(
                x => CreateKey(x.TenantId, x.CompanyId, x.ResultIdentity),
                StringComparer.Ordinal);
    }

    private static string CreateKey(string tenantId, string companyId, string resultIdentity)
        => string.Concat(
            tenantId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            tenantId,
            companyId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            companyId,
            resultIdentity.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            resultIdentity);
}
