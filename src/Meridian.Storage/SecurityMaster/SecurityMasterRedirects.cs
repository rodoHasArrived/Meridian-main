using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>A retired security and the survivor it was merged into (review C1).</summary>
public sealed record SecurityMasterRedirect(
    Guid RetiredSecurityId,
    Guid SurvivorSecurityId,
    DateTimeOffset MergedAt,
    string MergedBy,
    string Reason);

/// <summary>Durable survivorship redirects (migration 073).</summary>
public interface ISecurityMasterRedirectStore
{
    /// <summary>Records a redirect; idempotent for the same pair, refused for a different survivor.</summary>
    Task AddAsync(SecurityMasterRedirect redirect, CancellationToken ct = default);

    Task<SecurityMasterRedirect?> GetAsync(Guid retiredSecurityId, CancellationToken ct = default);

    /// <summary>Follows redirects from <paramref name="securityId"/>; returns it unchanged when none exists.</summary>
    async Task<Guid> ResolveSurvivorAsync(Guid securityId, CancellationToken ct = default)
    {
        var current = securityId;
        for (var depth = 0; depth < MaxRedirectDepth; depth++)
        {
            var redirect = await GetAsync(current, ct).ConfigureAwait(false);
            if (redirect is null)
                return current;
            current = redirect.SurvivorSecurityId;
        }

        throw new InvalidOperationException(
            $"Security '{securityId:D}' redirects through more than {MaxRedirectDepth} merges; the redirect chain must be repaired.");
    }

    /// <summary>Bound on followed merges so a malformed cycle cannot loop.</summary>
    const int MaxRedirectDepth = 8;
}

/// <summary>Process-local redirects for hosts without PostgreSQL.</summary>
public sealed class InMemorySecurityMasterRedirectStore : ISecurityMasterRedirectStore
{
    private readonly ConcurrentDictionary<Guid, SecurityMasterRedirect> _redirects = new();

    public Task AddAsync(SecurityMasterRedirect redirect, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        var stored = _redirects.GetOrAdd(redirect.RetiredSecurityId, redirect);
        if (stored.SurvivorSecurityId != redirect.SurvivorSecurityId)
        {
            throw new InvalidOperationException(
                $"Security '{redirect.RetiredSecurityId:D}' was already merged into '{stored.SurvivorSecurityId:D}'.");
        }

        return Task.CompletedTask;
    }

    public Task<SecurityMasterRedirect?> GetAsync(Guid retiredSecurityId, CancellationToken ct = default)
        => Task.FromResult(_redirects.TryGetValue(retiredSecurityId, out var redirect) ? redirect : null);
}

/// <summary>PostgreSQL redirects (migration 073).</summary>
public sealed class PostgresSecurityMasterRedirectStore : ISecurityMasterRedirectStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterRedirectStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private string Table => $"{_options.Schema}.security_master_redirects";

    public async Task AddAsync(SecurityMasterRedirect redirect, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {Table} (retired_security_id, survivor_security_id, merged_at, merged_by, reason)
            values (@retired, @survivor, @merged_at, @merged_by, @reason)
            on conflict (retired_security_id) do nothing
            returning survivor_security_id;
            """;
        command.Parameters.AddWithValue("retired", redirect.RetiredSecurityId);
        command.Parameters.AddWithValue("survivor", redirect.SurvivorSecurityId);
        command.Parameters.AddWithValue("merged_at", redirect.MergedAt.UtcDateTime);
        command.Parameters.AddWithValue("merged_by", redirect.MergedBy);
        command.Parameters.AddWithValue("reason", redirect.Reason);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is Guid)
            return;

        var existing = await GetAsync(redirect.RetiredSecurityId, ct).ConfigureAwait(false);
        if (existing is not null && existing.SurvivorSecurityId != redirect.SurvivorSecurityId)
        {
            throw new InvalidOperationException(
                $"Security '{redirect.RetiredSecurityId:D}' was already merged into '{existing.SurvivorSecurityId:D}'.");
        }
    }

    public async Task<SecurityMasterRedirect?> GetAsync(Guid retiredSecurityId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select retired_security_id, survivor_security_id, merged_at, merged_by, reason
            from {Table}
            where retired_security_id = @retired;
            """;
        command.Parameters.AddWithValue("retired", retiredSecurityId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new SecurityMasterRedirect(
                reader.GetGuid(0),
                reader.GetGuid(1),
                new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
                reader.GetString(3),
                reader.GetString(4))
            : null;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
