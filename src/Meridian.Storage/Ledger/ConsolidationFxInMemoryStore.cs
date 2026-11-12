using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>Single-process local/demo store with the same immutable revision contract as PostgreSQL.</summary>
public class ConsolidationFxInMemoryStore : IConsolidationFxStore
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _authority = new(1, 1);
    private ConsolidationFxState _state = new([], []);

    public async Task RetainRateSetAsync(ConsolidationFxRateSetDto rateSet, CancellationToken ct = default)
    {
        ConsolidationFxCurrencyRules.Validate(rateSet);
        var retained = Clone(rateSet with { ContentHash = ConsolidationFxValidation.Hash(rateSet) });
        await _authority.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ConsolidationFxState next;
            lock (_sync)
            {
                var history = _state.RateSets.Where(x => x.TenantId == retained.TenantId && x.CompanyId == retained.CompanyId && x.RateSetId == retained.RateSetId).ToArray();
                var existing = history.SingleOrDefault(x => x.Revision == retained.Revision);
                if (existing is not null)
                {
                    if (!Sha256Digest.FixedEquals(existing.ContentHash, retained.ContentHash))
                        throw new InvalidOperationException("An approved rate-set revision cannot be replaced.");
                    return;
                }
                var previous = history.MaxBy(x => x.Revision);
                RequirePredecessor(retained.Revision, retained.PreviousHash, previous?.Revision, previous?.ContentHash);
                next = _state with { RateSets = [.. _state.RateSets, retained] };
            }
            await PersistAsync(next, ct).ConfigureAwait(false);
            lock (_sync) _state = next;
        }
        finally { _authority.Release(); }
    }

    public Task<ConsolidationFxRateSetDto?> GetRateSetAsync(string tenantId, string companyId, Guid rateSetId, int revision, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var value = _state.RateSets.SingleOrDefault(x => x.TenantId == tenantId && x.CompanyId == companyId && x.RateSetId == rateSetId && x.Revision == revision);
            return Task.FromResult(value is null ? null : Clone(value));
        }
    }

    public Task<IReadOnlyList<ConsolidationFxRateSetDto>> ListRateSetsAsync(string tenantId, string companyId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult<IReadOnlyList<ConsolidationFxRateSetDto>>(_state.RateSets
            .Where(x => x.TenantId == tenantId && x.CompanyId == companyId)
            .OrderBy(x => x.RateSetId).ThenBy(x => x.Revision).Select(Clone).ToArray());
    }

    public async Task RetainConfigurationAsync(ConsolidationFxBookConfigurationDto configuration, CancellationToken ct = default)
    {
        ConsolidationFxCurrencyRules.Validate(configuration);
        var retained = Clone(configuration with { ContentHash = ConsolidationFxValidation.Hash(configuration) });
        await _authority.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ConsolidationFxState next;
            lock (_sync)
            {
                if (!_state.RateSets.Any(x => x.TenantId == retained.TenantId && x.CompanyId == retained.CompanyId &&
                    x.RateSetId == retained.RateSetId && x.Revision == retained.RateSetRevision))
                    throw new InvalidOperationException("The exact approved scoped rate-set revision is required.");
                var history = _state.Configurations.Where(x => x.LedgerBookId == retained.LedgerBookId).ToArray();
                if (history.Any(x => x.TenantId != retained.TenantId || x.CompanyId != retained.CompanyId))
                    throw new InvalidOperationException("A book's FX configuration cannot change owner scope.");
                var existing = history.SingleOrDefault(x => x.Revision == retained.Revision);
                if (existing is not null)
                {
                    if (!Sha256Digest.FixedEquals(existing.ContentHash, retained.ContentHash))
                        throw new InvalidOperationException("An approved FX configuration revision cannot be replaced.");
                    return;
                }
                var previous = history.MaxBy(x => x.Revision);
                RequirePredecessor(retained.Revision, retained.PreviousHash, previous?.Revision, previous?.ContentHash);
                next = _state with { Configurations = [.. _state.Configurations, retained] };
            }
            await PersistAsync(next, ct).ConfigureAwait(false);
            lock (_sync) _state = next;
        }
        finally { _authority.Release(); }
    }

    public Task<ConsolidationFxBookConfigurationDto?> GetConfigurationAsync(Guid ledgerBookId, DateOnly asOf, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var value = _state.Configurations.Where(x => x.LedgerBookId == ledgerBookId && x.EffectiveFrom <= asOf)
                .MaxBy(x => x.Revision);
            return Task.FromResult(value is null || (value.EffectiveTo is { } end && asOf >= end) ? null : Clone(value));
        }
    }

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        await _authority.WaitAsync(ct).ConfigureAwait(false);
        return new LocalLease(_authority);
    }

    public Task<ConsolidationFxBookConfigurationDto?> GetConfigurationRevisionAsync(Guid ledgerBookId, int revision, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var value = _state.Configurations.SingleOrDefault(x => x.LedgerBookId == ledgerBookId && x.Revision == revision);
            return Task.FromResult(value is null ? null : Clone(value));
        }
    }

    protected virtual Task PersistAsync(ConsolidationFxState state, CancellationToken ct) => Task.CompletedTask;

    protected void Restore(ConsolidationFxState state)
    {
        // Validate complete predecessor chains instead of accepting a plausible latest snapshot.
        foreach (var history in state.RateSets.GroupBy(x => (x.TenantId, x.CompanyId, x.RateSetId)))
        {
            ConsolidationFxRateSetDto? previous = null;
            foreach (var value in history.OrderBy(x => x.Revision))
            {
                ConsolidationFxCurrencyRules.Validate(value);
                RequirePredecessor(value.Revision, value.PreviousHash, previous?.Revision, previous?.ContentHash);
                if (value.ContentHash is null) throw new InvalidDataException("Retained FX evidence is missing its content hash.");
                previous = value;
            }
        }
        foreach (var history in state.Configurations.GroupBy(x => x.LedgerBookId))
        {
            ConsolidationFxBookConfigurationDto? previous = null;
            foreach (var value in history.OrderBy(x => x.Revision))
            {
                ConsolidationFxCurrencyRules.Validate(value);
                RequirePredecessor(value.Revision, value.PreviousHash, previous?.Revision, previous?.ContentHash);
                if (value.ContentHash is null || (previous is not null && (value.TenantId != previous.TenantId || value.CompanyId != previous.CompanyId)) ||
                    !state.RateSets.Any(x => x.TenantId == value.TenantId && x.CompanyId == value.CompanyId && x.RateSetId == value.RateSetId && x.Revision == value.RateSetRevision))
                    throw new InvalidDataException("Retained FX configuration has missing or inconsistent scope/rate authority.");
                previous = value;
            }
        }
        lock (_sync) _state = state;
    }

    internal static void RequirePredecessor(int revision, string? hash, int? priorRevision, string? priorHash)
    {
        if (revision != (priorRevision ?? 0) + 1 || (priorRevision is null ? hash is not null : !Sha256Digest.FixedEquals(hash, priorHash)))
            throw new InvalidOperationException("FX authority requires a contiguous revision and the exact prior approved hash.");
    }

    private static ConsolidationFxRateSetDto Clone(ConsolidationFxRateSetDto value)
        => JsonSerializer.Deserialize(JsonSerializer.Serialize(value, ConsolidationFxJsonContext.Default.ConsolidationFxRateSetDto), ConsolidationFxJsonContext.Default.ConsolidationFxRateSetDto)!;
    private static ConsolidationFxBookConfigurationDto Clone(ConsolidationFxBookConfigurationDto value)
        => JsonSerializer.Deserialize(JsonSerializer.Serialize(value, ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto), ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto)!;

    private sealed class LocalLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed record ConsolidationFxState(IReadOnlyList<ConsolidationFxRateSetDto> RateSets,
    IReadOnlyList<ConsolidationFxBookConfigurationDto> Configurations);

/// <summary>Atomic single-process development persistence. Production uses PostgreSQL.</summary>
public sealed class ConsolidationFxFileStore : ConsolidationFxInMemoryStore
{
    private readonly string _path;
    public ConsolidationFxFileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        if (File.Exists(_path)) Restore(JsonSerializer.Deserialize(File.ReadAllText(_path), ConsolidationFxStateJsonContext.Default.ConsolidationFxState)
            ?? throw new InvalidDataException("The retained FX state is empty."));
    }
    protected override Task PersistAsync(ConsolidationFxState state, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(_path, JsonSerializer.Serialize(state, ConsolidationFxStateJsonContext.Default.ConsolidationFxState), ct);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConsolidationFxState))]
internal sealed partial class ConsolidationFxStateJsonContext : JsonSerializerContext;
