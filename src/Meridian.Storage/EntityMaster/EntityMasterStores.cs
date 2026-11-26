using System.Collections.Concurrent;
using Meridian.Contracts.EntityMaster;
using Meridian.Storage.Migrations;
using Npgsql;

namespace Meridian.Storage.EntityMaster;

/// <summary>Connection settings for the entity master (ADR-033).</summary>
public sealed class EntityMasterOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Schema { get; set; } = "entity_master";
}

/// <summary>Applies the entity-master migration set.</summary>
public sealed class EntityMasterMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public EntityMasterMigrationRunner(EntityMasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("EntityMaster", "Migrations"),
            DisplayName = "Entity master",
            LockScopeName = "entity_master",
            ConnectionStringSettingName = $"{nameof(EntityMasterOptions)}.{nameof(options.ConnectionString)}",
            LedgerTableName = "entity_master_schema_migrations",
            DriftPolicy = MigrationDriftPolicy.Reapply,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}

/// <summary>Process-local entity master for hosts without PostgreSQL.</summary>
public sealed class InMemoryEntityMasterStore : IEntityMasterStore
{
    private readonly ConcurrentDictionary<Guid, LegalEntityDto> _entities = new();
    private readonly List<EntityParentLinkDto> _parents = [];
    private readonly List<SecurityIssuerLinkDto> _issuers = [];
    private readonly object _gate = new();

    public Task<LegalEntityDto> CreateEntityAsync(CreateLegalEntityRequest request, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LegalName);
        var lei = EntityMasterRules.NormalizeLei(request.Lei);
        var cik = EntityMasterRules.NormalizeCik(request.Cik);
        lock (_gate)
        {
            if (lei is not null && _entities.Values.Any(entity => entity.Lei == lei))
                throw new InvalidOperationException($"An entity with LEI '{lei}' already exists.");
            if (cik is not null && _entities.Values.Any(entity => entity.Cik == cik))
                throw new InvalidOperationException($"An entity with CIK '{cik}' already exists.");
            var entity = new LegalEntityDto(request.EntityId ?? Guid.NewGuid(), request.LegalName.Trim(), lei, cik,
                request.Jurisdiction?.Trim(), DateTimeOffset.UtcNow, actor);
            _entities[entity.EntityId] = entity;
            return Task.FromResult(entity);
        }
    }

    public Task<LegalEntityDto?> GetEntityAsync(Guid entityId, CancellationToken ct = default)
        => Task.FromResult(_entities.TryGetValue(entityId, out var entity) ? entity : null);

    public Task<LegalEntityDto?> FindByLeiAsync(string lei, CancellationToken ct = default)
    {
        var normalized = EntityMasterRules.NormalizeLei(lei);
        return Task.FromResult(_entities.Values.FirstOrDefault(entity => entity.Lei == normalized));
    }

    public Task<LegalEntityDto?> FindByCikAsync(string cik, CancellationToken ct = default)
    {
        var normalized = EntityMasterRules.NormalizeCik(cik);
        return Task.FromResult(_entities.Values.FirstOrDefault(entity => entity.Cik == normalized));
    }

    public Task<EntityParentLinkDto> SetParentAsync(Guid entityId, Guid parentEntityId, DateOnly validFrom, string actor, CancellationToken ct = default)
    {
        if (entityId == parentEntityId)
            throw new ArgumentException("An entity cannot be its own parent.");
        lock (_gate)
        {
            RequireEntity(entityId);
            RequireEntity(parentEntityId);
            var link = new EntityParentLinkDto(entityId, parentEntityId, validFrom, null, DateTimeOffset.UtcNow, actor);
            CloseOpen(_parents, existing => existing.EntityId == entityId, validFrom,
                existing => existing with { ValidTo = validFrom }, static existing => existing.ValidFrom, static existing => existing.ValidTo);
            _parents.Add(link);
            return Task.FromResult(link);
        }
    }

    public Task<EntityParentLinkDto?> GetParentAsync(Guid entityId, DateOnly asOf, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_parents.FirstOrDefault(link => link.EntityId == entityId && link.ValidFrom <= asOf && (link.ValidTo is null || link.ValidTo > asOf)));
        }
    }

    public Task<SecurityIssuerLinkDto> LinkIssuerAsync(Guid securityId, Guid issuerEntityId, DateOnly validFrom, string actor, CancellationToken ct = default)
    {
        lock (_gate)
        {
            RequireEntity(issuerEntityId);
            var link = new SecurityIssuerLinkDto(securityId, issuerEntityId, validFrom, null, DateTimeOffset.UtcNow, actor);
            CloseOpen(_issuers, existing => existing.SecurityId == securityId, validFrom,
                existing => existing with { ValidTo = validFrom }, static existing => existing.ValidFrom, static existing => existing.ValidTo);
            _issuers.Add(link);
            return Task.FromResult(link);
        }
    }

    public Task<SecurityIssuerLinkDto?> GetIssuerAsync(Guid securityId, DateOnly asOf, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_issuers.FirstOrDefault(link => link.SecurityId == securityId && link.ValidFrom <= asOf && (link.ValidTo is null || link.ValidTo > asOf)));
        }
    }

    private void RequireEntity(Guid entityId)
    {
        if (!_entities.ContainsKey(entityId))
            throw new InvalidOperationException($"Entity '{entityId:D}' was not found.");
    }

    private static void CloseOpen<T>(List<T> links, Func<T, bool> sameSubject, DateOnly validFrom, Func<T, T> close, Func<T, DateOnly> from, Func<T, DateOnly?> to)
    {
        var index = links.FindIndex(existing => sameSubject(existing) && to(existing) is null);
        if (index < 0)
            return;
        if (from(links[index]) >= validFrom)
            throw new InvalidOperationException("A new link must start after the open link it supersedes.");
        links[index] = close(links[index]);
    }
}

/// <summary>PostgreSQL entity master (ADR-033), migrated on first use.</summary>
public sealed class PostgresEntityMasterStore : IEntityMasterStore
{
    private readonly EntityMasterOptions _options;
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private bool _migrated;

    public PostgresEntityMasterStore(EntityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private string Table(string name) => $"{_options.Schema}.{name}";

    public async Task<LegalEntityDto> CreateEntityAsync(CreateLegalEntityRequest request, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LegalName);
        var lei = EntityMasterRules.NormalizeLei(request.Lei);
        var cik = EntityMasterRules.NormalizeCik(request.Cik);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {Table("legal_entities")} (entity_id, legal_name, lei, cik, jurisdiction, recorded_by)
            values (@id, @name, @lei, @cik, @jurisdiction, @by)
            returning entity_id, legal_name, lei, cik, jurisdiction, recorded_at, recorded_by;
            """;
        command.Parameters.AddWithValue("id", request.EntityId ?? Guid.NewGuid());
        command.Parameters.AddWithValue("name", request.LegalName.Trim());
        command.Parameters.Add(new NpgsqlParameter("lei", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)lei ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("cik", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)cik ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("jurisdiction", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)request.Jurisdiction?.Trim() ?? DBNull.Value });
        command.Parameters.AddWithValue("by", actor);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            return MapEntity(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"An entity with the same LEI or CIK already exists ({ex.ConstraintName}).", ex);
        }
    }

    public Task<LegalEntityDto?> GetEntityAsync(Guid entityId, CancellationToken ct = default)
        => QueryEntityAsync("entity_id = @value", entityId, ct);

    public Task<LegalEntityDto?> FindByLeiAsync(string lei, CancellationToken ct = default)
        => QueryEntityAsync("lei = @value", EntityMasterRules.NormalizeLei(lei)!, ct);

    public Task<LegalEntityDto?> FindByCikAsync(string cik, CancellationToken ct = default)
        => QueryEntityAsync("cik = @value", EntityMasterRules.NormalizeCik(cik)!, ct);

    public async Task<EntityParentLinkDto> SetParentAsync(Guid entityId, Guid parentEntityId, DateOnly validFrom, string actor, CancellationToken ct = default)
    {
        if (entityId == parentEntityId)
            throw new ArgumentException("An entity cannot be its own parent.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await CloseOpenLinkAsync(connection, transaction, "entity_parent_links", "entity_id", entityId, validFrom, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Table("entity_parent_links")} (entity_id, parent_entity_id, valid_from, recorded_by)
            values (@entity, @parent, @from, @by)
            returning entity_id, parent_entity_id, valid_from, valid_to, recorded_at, recorded_by;
            """;
        command.Parameters.AddWithValue("entity", entityId);
        command.Parameters.AddWithValue("parent", parentEntityId);
        command.Parameters.AddWithValue("from", validFrom);
        command.Parameters.AddWithValue("by", actor);
        EntityParentLinkDto link;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            await reader.ReadAsync(ct).ConfigureAwait(false);
            link = new EntityParentLinkDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3), new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero), reader.GetString(5));
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return link;
    }

    public async Task<EntityParentLinkDto?> GetParentAsync(Guid entityId, DateOnly asOf, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select entity_id, parent_entity_id, valid_from, valid_to, recorded_at, recorded_by
            from {Table("entity_parent_links")}
            where entity_id = @id and valid_from <= @as_of and (valid_to is null or valid_to > @as_of);
            """;
        command.Parameters.AddWithValue("id", entityId);
        command.Parameters.AddWithValue("as_of", asOf);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new EntityParentLinkDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3), new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero), reader.GetString(5))
            : null;
    }

    public async Task<SecurityIssuerLinkDto> LinkIssuerAsync(Guid securityId, Guid issuerEntityId, DateOnly validFrom, string actor, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await CloseOpenLinkAsync(connection, transaction, "security_issuer_links", "security_id", securityId, validFrom, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Table("security_issuer_links")} (security_id, issuer_entity_id, valid_from, recorded_by)
            values (@security, @issuer, @from, @by)
            returning security_id, issuer_entity_id, valid_from, valid_to, recorded_at, recorded_by;
            """;
        command.Parameters.AddWithValue("security", securityId);
        command.Parameters.AddWithValue("issuer", issuerEntityId);
        command.Parameters.AddWithValue("from", validFrom);
        command.Parameters.AddWithValue("by", actor);
        SecurityIssuerLinkDto link;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            await reader.ReadAsync(ct).ConfigureAwait(false);
            link = MapIssuer(reader);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return link;
    }

    public async Task<SecurityIssuerLinkDto?> GetIssuerAsync(Guid securityId, DateOnly asOf, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select security_id, issuer_entity_id, valid_from, valid_to, recorded_at, recorded_by
            from {Table("security_issuer_links")}
            where security_id = @id and valid_from <= @as_of and (valid_to is null or valid_to > @as_of);
            """;
        command.Parameters.AddWithValue("id", securityId);
        command.Parameters.AddWithValue("as_of", asOf);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapIssuer(reader) : null;
    }

    private async Task CloseOpenLinkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string keyColumn, Guid key, DateOnly validFrom, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            update {Table(table)}
            set valid_to = @from
            where {keyColumn} = @key and valid_to is null
            returning valid_from;
            """;
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("from", validFrom);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            throw new InvalidOperationException("A new link must start after the open link it supersedes.", ex);
        }
    }

    private async Task<LegalEntityDto?> QueryEntityAsync(string predicate, object value, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select entity_id, legal_name, lei, cik, jurisdiction, recorded_at, recorded_by
            from {Table("legal_entities")}
            where {predicate};
            """;
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapEntity(reader) : null;
    }

    private static LegalEntityDto MapEntity(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
        reader.GetString(6));

    private static SecurityIssuerLinkDto MapIssuer(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetFieldValue<DateOnly>(2),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
        new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
        reader.GetString(5));

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("EntityMasterOptions.ConnectionString is not configured.");

        if (!_migrated)
        {
            await _migrationGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_migrated)
                {
                    await new EntityMasterMigrationRunner(_options).EnsureMigratedAsync(ct).ConfigureAwait(false);
                    _migrated = true;
                }
            }
            finally
            {
                _migrationGate.Release();
            }
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
