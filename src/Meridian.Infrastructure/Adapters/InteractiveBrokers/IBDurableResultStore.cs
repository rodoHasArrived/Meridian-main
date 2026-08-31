using System.Text.Json;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Immutable ownership captured when an IB request is issued. Callback threads must reuse this
/// value instead of resolving ambient request state.
/// </summary>
public sealed record IBDataRequestOwnership(string TenantId, string CompanyId)
{
    public static IBDataRequestOwnership Require(IBDataRequestOwnership? ownership)
    {
        if (ownership is null ||
            string.IsNullOrWhiteSpace(ownership.TenantId) ||
            string.IsNullOrWhiteSpace(ownership.CompanyId))
        {
            throw new InvalidOperationException(
                "Tenant and company ownership must be captured before issuing a durable IB request.");
        }

        return new IBDataRequestOwnership(ownership.TenantId.Trim(), ownership.CompanyId.Trim());
    }
}

/// <summary>
/// A persisted, request-correlated IB result. Tenant, company, connection, and immutable
/// correlation identity are all part of its durable ownership boundary.
/// </summary>
public sealed record IBDurableResult(
    string TenantId,
    ProviderDataRequestReadModel Request,
    IBDataLineage? Lineage)
{
    public string CompanyId { get; init; } = string.Empty;
    public string ProviderConnectionId { get; init; } = string.Empty;
    public string RequestCorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// Durable query seam; callers must always supply both tenant and company they are authorized to
/// read.
/// </summary>
public interface IBDurableResultStore
{
    void Upsert(
        IBDataRequestOwnership ownership,
        string providerConnectionId,
        string requestCorrelationId,
        ProviderDataRequestReadModel request,
        IBDataLineage? lineage);

    IReadOnlyList<IBDurableResult> Get(
        string tenantId,
        string companyId,
        string? capability = null,
        string? accountId = null,
        string? modelAccountId = null);
}

/// <summary>
/// Small atomic JSON result store used until an installation supplies a database projection. It is
/// deliberately keyed by tenant + company + provider connection + immutable correlation + request
/// id, so process restarts and shared gateway request-id reuse cannot cross ownership boundaries.
/// </summary>
public sealed class JsonIBDurableResultStore : IBDurableResultStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, IBDurableResult>? _results;

    public JsonIBDurableResultStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    public void Upsert(
        IBDataRequestOwnership ownership,
        string providerConnectionId,
        string requestCorrelationId,
        ProviderDataRequestReadModel request,
        IBDataLineage? lineage)
    {
        ownership = IBDataRequestOwnership.Require(ownership);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestCorrelationId);
        ArgumentNullException.ThrowIfNull(request);
        providerConnectionId = providerConnectionId.Trim();
        requestCorrelationId = requestCorrelationId.Trim();
        if (!string.Equals(
                providerConnectionId,
                request.Provenance.ProviderConnectionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                requestCorrelationId,
                request.Provenance.CorrelationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Durable IB connection and correlation identity must match the captured request provenance.");
        }

        var durableResult = new IBDurableResult(ownership.TenantId, request, lineage)
        {
            CompanyId = ownership.CompanyId,
            ProviderConnectionId = providerConnectionId,
            RequestCorrelationId = requestCorrelationId
        };
        lock (_gate)
        {
            var results = Load();
            results[CreateKey(durableResult)] = durableResult;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(results.Values));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    public IReadOnlyList<IBDurableResult> Get(
        string tenantId,
        string companyId,
        string? capability = null,
        string? accountId = null,
        string? modelAccountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        tenantId = tenantId.Trim();
        companyId = companyId.Trim();
        lock (_gate)
            return Load().Values
                .Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal))
                .Where(x => string.Equals(x.CompanyId, companyId, StringComparison.Ordinal))
                .Where(x => capability is null || string.Equals(x.Request.Capability, capability, StringComparison.OrdinalIgnoreCase))
                .Where(x => accountId is null || string.Equals(x.Request.AccountId, accountId, StringComparison.Ordinal))
                .Where(x => modelAccountId is null || string.Equals(x.Request.ModelAccountId, modelAccountId, StringComparison.Ordinal))
                .OrderByDescending(x => x.Request.UpdatedAt).ToArray();
    }

    private Dictionary<string, IBDurableResult> Load()
    {
        if (_results is not null)
            return _results;
        if (!File.Exists(_path))
            return _results = new();
        var values = JsonSerializer.Deserialize<List<IBDurableResult>>(File.ReadAllText(_path)) ?? [];
        return _results = values
            .Where(IsAuthoritativelyScoped)
            .ToDictionary(CreateKey, StringComparer.Ordinal);
    }

    private static bool IsAuthoritativelyScoped(IBDurableResult result)
        => !string.IsNullOrWhiteSpace(result.TenantId) &&
           !string.IsNullOrWhiteSpace(result.CompanyId) &&
           !string.IsNullOrWhiteSpace(result.ProviderConnectionId) &&
           !string.IsNullOrWhiteSpace(result.RequestCorrelationId) &&
           result.Request?.Provenance is { } provenance &&
           string.Equals(
               result.ProviderConnectionId,
               provenance.ProviderConnectionId,
               StringComparison.Ordinal) &&
           string.Equals(
               result.RequestCorrelationId,
               provenance.CorrelationId,
               StringComparison.Ordinal);

    private static string CreateKey(IBDurableResult result)
        => string.Concat(
            KeyPart(result.TenantId),
            KeyPart(result.CompanyId),
            KeyPart(result.ProviderConnectionId),
            KeyPart(result.RequestCorrelationId),
            result.Request.RequestId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string KeyPart(string value)
        => string.Concat(
            value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            value);
}

/// <summary>Materializes callback updates without treating a submitted request as live entitlement.</summary>
public sealed class IBDurableResultProjector
{
    private readonly IBDurableResultStore _store;

    public IBDurableResultProjector(IBDurableResultStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public void Materialize(
        IBDataRequestOwnership ownership,
        ProviderDataRequestReadModel request,
        IBDataLineage? lineage)
    {
        ownership = IBDataRequestOwnership.Require(ownership);
        ArgumentNullException.ThrowIfNull(request);
        _store.Upsert(
            ownership,
            request.Provenance.ProviderConnectionId,
            request.Provenance.CorrelationId,
            request,
            lineage);
    }
}
