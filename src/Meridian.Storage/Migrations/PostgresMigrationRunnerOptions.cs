namespace Meridian.Storage.Migrations;

/// <summary>
/// Configuration for <see cref="PostgresMigrationRunner"/>. Feature-specific migration runners
/// translate their own options into this shape so every schema shares one migration engine while
/// keeping its historical ledger table layout and script conventions.
/// </summary>
public sealed class PostgresMigrationRunnerOptions
{
    /// <summary>PostgreSQL connection string for the target database.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Schema that owns the feature's tables and migration ledger.</summary>
    public required string Schema { get; init; }

    /// <summary>
    /// Directory containing the feature's <c>*.sql</c> scripts, relative to
    /// <see cref="AppContext.BaseDirectory"/> (for example <c>Banking/Migrations</c>).
    /// </summary>
    public required string ScriptsSubdirectory { get; init; }

    /// <summary>Human-readable feature name used in error messages.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Stable slug used to build the advisory-lock scope
    /// (<c>meridian.{LockScopeName}.migrations.{Schema}</c>) that serializes concurrent starters.
    /// </summary>
    public required string LockScopeName { get; init; }

    /// <summary>
    /// Setting name reported when <see cref="ConnectionString"/> is missing
    /// (for example <c>BankingStoreOptions.ConnectionString</c>).
    /// </summary>
    public required string ConnectionStringSettingName { get; init; }

    /// <summary>Migration ledger table name inside <see cref="Schema"/>.</summary>
    public string LedgerTableName { get; init; } = "schema_migrations";

    /// <summary>Primary-key column of the migration ledger.</summary>
    public string LedgerKeyColumn { get; init; } = "filename";

    /// <summary>
    /// When true, script filenames must begin with a unique numeric ordinal prefix that is stored
    /// alongside the filename (Security Master convention).
    /// </summary>
    public bool TrackOrdinals { get; init; }

    /// <summary>
    /// When true, <c>__SCHEMA__</c> renders as a quoted identifier; otherwise the raw validated
    /// schema name is used. Existing schemas must keep their historical quoting so identifier
    /// case-folding does not change.
    /// </summary>
    public bool QuoteSchemaInScripts { get; init; }

    /// <summary>
    /// When true, a freshly created ledger declares its checksum column <c>not null</c>
    /// (Asset Operations convention).
    /// </summary>
    public bool ChecksumRequired { get; init; }

    /// <summary>Whether a missing scripts directory is an error or an empty migration set.</summary>
    public bool ThrowWhenScriptsDirectoryMissing { get; init; } = true;

    /// <summary>How a checksum mismatch on an already-applied script is handled.</summary>
    public MigrationDriftPolicy DriftPolicy { get; init; } = MigrationDriftPolicy.Throw;
}

/// <summary>Behavior when an applied migration script's content no longer matches its checksum.</summary>
public enum MigrationDriftPolicy
{
    /// <summary>Fail startup: applied scripts are immutable (append-only convention).</summary>
    Throw,

    /// <summary>
    /// Re-apply the script on every startup and update the ledger checksum. Used by schemas whose
    /// scripts were historically re-run on every startup and are written to be idempotent.
    /// </summary>
    Reapply,
}
