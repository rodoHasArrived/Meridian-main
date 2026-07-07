namespace Meridian.TestSupport;

/// <summary>
/// Generates run-scoped PostgreSQL schema names so concurrent test runs stay isolated.
/// </summary>
public static class PostgresTestSchema
{
    /// <summary>
    /// Returns a unique schema name of the form <c>{prefix}_test_{guid:N}</c>, matching the
    /// per-run naming each module previously produced inline (for example
    /// <c>ledger_test_…</c>, <c>dl_test_…</c>, <c>sm_test_…</c>).
    /// </summary>
    public static string NewSchemaName(string prefix) => $"{prefix}_test_{Guid.NewGuid():N}";
}
