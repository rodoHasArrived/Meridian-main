using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Text;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterStore
{
    /// <summary>
    /// Cached SQL per (descriptor, schema). The statements are a pure function of the descriptor's
    /// column list, so they are built once instead of re-concatenated for every persisted record.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Table, string Schema), SecurityTermsProjectionSql> TermsProjectionSqlCache = new();

    /// <summary>
    /// The column every terms projection is keyed and conflict-resolved on. Named rather than
    /// assumed positionally, so the upsert's update clause stays correct if the identity spine is
    /// ever reordered.
    /// </summary>
    private const string TermsProjectionConflictKey = "security_id";

    /// <summary>
    /// One projected value bound to its column, carried in insert order.
    /// </summary>
    /// <param name="ColumnName">The SQL column, which is also the parameter name.</param>
    /// <param name="Value">The bound value, already <see cref="DBNull"/> where the term is absent.</param>
    internal sealed record SecurityTermsProjectionValue(string ColumnName, object Value);

    /// <summary>The rows to write into one child projection table, in terms-document order.</summary>
    internal sealed record SecurityTermsProjectionChildRows(
        string TableName,
        IReadOnlyList<IReadOnlyList<SecurityTermsProjectionValue>> Rows);

    /// <summary>
    /// A fully decoded relational projection, ready to bind. Building it is pure — no connection, no
    /// schema, no SQL — so the decode half of a schema-driven projection is unit-testable in the same
    /// way <c>TryBuildBondProjection</c> is, rather than only reachable through an integration test
    /// (the CI gate excludes <c>Category=Integration</c>, so a decode regression would otherwise
    /// surface no earlier than the weekly certification run).
    /// </summary>
    internal sealed record SecurityTermsProjectionPlan(
        string TableName,
        IReadOnlyList<SecurityTermsProjectionValue> Columns,
        IReadOnlyList<SecurityTermsProjectionChildRows> Children)
    {
        /// <summary>The bound value for <paramref name="columnName"/>, or null when the column is not projected.</summary>
        internal object? Value(string columnName)
            => Columns.FirstOrDefault(column =>
                string.Equals(column.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))?.Value;

        /// <summary>The rows planned for child table <paramref name="tableName"/>, or an empty list.</summary>
        internal IReadOnlyList<IReadOnlyList<SecurityTermsProjectionValue>> ChildRows(string tableName)
            => Children.FirstOrDefault(child =>
                string.Equals(child.TableName, tableName, StringComparison.OrdinalIgnoreCase))?.Rows ?? [];
    }

    /// <summary>The statements one descriptor needs against one schema.</summary>
    private sealed record SecurityTermsProjectionSql(
        string Upsert,
        string DeleteParent,
        IReadOnlyDictionary<string, string> ChildInserts,
        IReadOnlyDictionary<string, string> ChildDeletes);

    /// <summary>
    /// Decodes <paramref name="record"/> into the relational projection <paramref name="descriptor"/>
    /// declares, or reports that the record has no projection.
    /// <para>
    /// There is no projection when the record carries a different asset class, when a gating term is
    /// missing, or when a child array holds a malformed element. The last case is deliberate: a
    /// principal or factor schedule that lost a row still reads as a complete schedule and would
    /// misstate amortization, so a partial decode suppresses the projection instead of publishing
    /// it. Callers delete any stale rows on a false result, which is what keeps a record that
    /// changes asset class from leaving an orphaned projection behind.
    /// </para>
    /// </summary>
    internal static bool TryBuildTermsProjection(
        SecurityTermsProjectionDescriptor descriptor,
        SecurityProjectionRecord record,
        out SecurityTermsProjectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(record);

        plan = null!;

        if (!string.Equals(record.AssetClass, descriptor.AssetClass, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var terms = record.AssetSpecificTerms;
        var columns = new List<SecurityTermsProjectionValue>(
            SecurityTermsProjectionRegistry.LeadingIdentityColumns.Count
            + descriptor.Columns.Count
            + SecurityTermsProjectionRegistry.TrailingIdentityColumns.Count)
        {
            new("security_id", record.SecurityId),
            new("display_name", record.DisplayName),
            new("currency", record.Currency ?? string.Empty),
        };

        foreach (var column in descriptor.Columns)
        {
            var value = ReadTermValue(terms, column.TermKey, column.Type);
            if (column.Gates && value is null)
            {
                return false;
            }

            columns.Add(new(column.ColumnName, value ?? DBNull.Value));
        }

        columns.Add(new("primary_identifier_value", record.PrimaryIdentifierValue));
        columns.Add(new("version", record.Version));

        var children = new List<SecurityTermsProjectionChildRows>(descriptor.ChildTables.Count);
        foreach (var child in descriptor.ChildTables)
        {
            if (!TryBuildChildRows(terms, record.SecurityId, child, out var rows))
            {
                return false;
            }

            children.Add(new(child.TableName, rows));
        }

        plan = new(descriptor.TableName, columns, children);
        return true;
    }

    private static bool TryBuildChildRows(
        JsonElement terms,
        Guid securityId,
        SecurityTermsProjectionChildTable child,
        out IReadOnlyList<IReadOnlyList<SecurityTermsProjectionValue>> rows)
    {
        rows = [];

        if (!terms.TryGetProperty(child.TermKey, out var array))
        {
            return true;
        }

        // A term that is present as JSON null is the serializer's rendering of an absent optional
        // list, not a malformed schedule; anything else non-array is a shape the contract forbids.
        if (array.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var built = new List<IReadOnlyList<SecurityTermsProjectionValue>>();
        var ordinal = 0;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var row = new List<SecurityTermsProjectionValue>(SecurityTermsProjectionRegistry.ChildKeyColumns.Count + child.Columns.Count)
            {
                new("security_id", securityId),
                new("ordinal", ordinal),
            };

            foreach (var column in child.Columns)
            {
                var value = ReadTermValue(element, column.ElementKey, column.Type);
                if (column.Required && value is null)
                {
                    return false;
                }

                row.Add(new(column.ColumnName, value ?? DBNull.Value));
            }

            built.Add(row);
            ordinal++;
        }

        rows = built;
        return true;
    }

    /// <summary>
    /// Reads one declared term as its projected CLR value, or null when it is absent or carries a
    /// shape the contract does not declare. Delegates to the same <c>GetOptional*</c> readers the
    /// hand-written projections use, so a schema-driven column and a hand-written one decode a
    /// payload identically instead of diverging on their own tolerance rules.
    /// </summary>
    private static object? ReadTermValue(JsonElement source, string key, SecurityAssetTermFieldType type)
        => type switch
        {
            // NormalizeOptional: blank reads as absent and surrounding whitespace is trimmed, so a
            // padded vendor value and a clean one land on the same indexed column value.
            SecurityAssetTermFieldType.String => TextPrimitives.NormalizeOptional(GetOptionalString(source, key)),
            SecurityAssetTermFieldType.Decimal => GetOptionalDecimal(source, key),
            SecurityAssetTermFieldType.Integer => GetOptionalInt(source, key),
            SecurityAssetTermFieldType.Boolean => GetOptionalBool(source, key),
            SecurityAssetTermFieldType.Date => GetOptionalDateOnly(source, key)?.ToDateTime(TimeOnly.MinValue),
            _ => null
        };

    /// <summary>
    /// Builds the parent upsert for <paramref name="descriptor"/>. The <c>do update set</c> clause
    /// restates every non-key column from <c>excluded</c>, which is exactly what the hand-written
    /// projections spell out by hand.
    /// </summary>
    internal static string BuildTermsProjectionUpsertSql(SecurityTermsProjectionDescriptor descriptor, string schema)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var columns = ProjectedColumnNames(descriptor);
        var builder = new StringBuilder()
            .Append("insert into ").Append(schema).Append('.').Append(descriptor.TableName).Append(" (")
            .AppendJoin(", ", columns)
            .Append(')')
            .Append(" values (")
            .AppendJoin(", ", columns.Select(static column => "@" + column))
            .Append(')')
            .Append(" on conflict (").Append(TermsProjectionConflictKey).Append(") do update set ")
            .AppendJoin(
                ", ",
                columns
                    .Where(static column => !string.Equals(column, TermsProjectionConflictKey, StringComparison.Ordinal))
                    .Select(static column => $"{column} = excluded.{column}"))
            .Append(';');

        return builder.ToString();
    }

    /// <summary>
    /// Builds the row insert for one child projection table. Child rows are cleared and re-inserted
    /// on every write, so the statement never needs a conflict clause.
    /// </summary>
    internal static string BuildTermsProjectionChildInsertSql(SecurityTermsProjectionChildTable child, string schema)
    {
        ArgumentNullException.ThrowIfNull(child);

        var columns = SecurityTermsProjectionRegistry.ChildKeyColumns
            .Concat(child.Columns.Select(static column => column.ColumnName))
            .ToArray();

        return new StringBuilder()
            .Append("insert into ").Append(schema).Append('.').Append(child.TableName).Append(" (")
            .AppendJoin(", ", columns)
            .Append(") values (")
            .AppendJoin(", ", columns.Select(static column => "@" + column))
            .Append(");")
            .ToString();
    }

    /// <summary>Builds the by-security delete used to clear a projection table.</summary>
    internal static string BuildTermsProjectionDeleteSql(string tableName, string schema)
        => $"delete from {schema}.{tableName} where security_id = @security_id;";

    private static IReadOnlyList<string> ProjectedColumnNames(SecurityTermsProjectionDescriptor descriptor)
        => SecurityTermsProjectionRegistry.LeadingIdentityColumns
            .Concat(descriptor.Columns.Select(static column => column.ColumnName))
            .Concat(SecurityTermsProjectionRegistry.TrailingIdentityColumns)
            .ToArray();

    private SecurityTermsProjectionSql GetTermsProjectionSql(SecurityTermsProjectionDescriptor descriptor)
        => TermsProjectionSqlCache.GetOrAdd(
            (descriptor.TableName, _options.Schema),
            static (_, state) => new SecurityTermsProjectionSql(
                BuildTermsProjectionUpsertSql(state.Descriptor, state.Schema),
                BuildTermsProjectionDeleteSql(state.Descriptor.TableName, state.Schema),
                state.Descriptor.ChildTables.ToDictionary(
                    static child => child.TableName,
                    child => BuildTermsProjectionChildInsertSql(child, state.Schema),
                    StringComparer.Ordinal),
                state.Descriptor.ChildTables.ToDictionary(
                    static child => child.TableName,
                    child => BuildTermsProjectionDeleteSql(child.TableName, state.Schema),
                    StringComparer.Ordinal)),
            (Descriptor: descriptor, Schema: _options.Schema));

    /// <summary>
    /// Writes the schema-driven projection <paramref name="descriptor"/> declares for
    /// <paramref name="record"/>, or clears it when the record has none. Child rows are replaced
    /// wholesale before the parent upsert so a shortened schedule cannot leave trailing rows behind,
    /// and so the child inserts always land after the parent row they reference exists.
    /// </summary>
    private async Task WriteTermsProjectionAsync(
        SecurityTermsProjectionDescriptor descriptor,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        var sql = GetTermsProjectionSql(descriptor);
        var projected = TryBuildTermsProjection(descriptor, record, out var plan);

        // Child rows are cleared explicitly rather than left to the foreign key's cascade: the
        // projected path upserts the parent instead of deleting it, so no cascade fires there, and
        // relying on one for the delete path would put a correctness requirement in the DDL that the
        // descriptor cannot state or check.
        foreach (var child in descriptor.ChildTables)
        {
            await ExecuteBySecurityIdAsync(connection, transaction, sql.ChildDeletes[child.TableName], record.SecurityId, ct)
                .ConfigureAwait(false);
        }

        if (!projected)
        {
            await ExecuteBySecurityIdAsync(connection, transaction, sql.DeleteParent, record.SecurityId, ct)
                .ConfigureAwait(false);
            return;
        }

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = sql.Upsert;
            foreach (var column in plan.Columns)
            {
                upsert.Parameters.AddWithValue(column.ColumnName, column.Value);
            }

            await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var child in plan.Children)
        {
            foreach (var row in child.Rows)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = sql.ChildInserts[child.TableName];
                foreach (var column in row)
                {
                    insert.Parameters.AddWithValue(column.ColumnName, column.Value);
                }

                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ExecuteBySecurityIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.Parameters.AddWithValue("security_id", securityId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
