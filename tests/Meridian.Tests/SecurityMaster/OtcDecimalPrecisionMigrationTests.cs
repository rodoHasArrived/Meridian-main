using System.Text.RegularExpressions;
using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migration 070 removes the numeric(28,10) typmod from every OTC amount, quantity and exposure
/// column (review Priority 1, step 3), as migration 058 did for the schedule projections. The test
/// derives the bounded columns from the migrations that created them, so a column added later with
/// the old typmod is not silently left behind.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OtcDecimalPrecisionMigrationTests
{
    private const string MigrationFile = "070_otc_decimal_precision.sql";
    private static readonly string[] CreatingMigrations = ["050_otc_contracts.sql", "054_otc_exposure_and_collateral.sql"];

    private static readonly Regex TableHeader = new(@"create table if not exists __SCHEMA__\.(?<table>\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex BoundedColumn = new(@"^\s*(?<column>\w+)\s+numeric\(28\s*,\s*10\)", RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void Migration_WidensEveryBoundedOtcColumn_ToUnconstrainedNumeric()
    {
        var widening = ReadMigration(MigrationFile);
        var bounded = CreatingMigrations.SelectMany(file => BoundedColumns(ReadMigration(file))).ToArray();

        bounded.Should().NotBeEmpty();
        foreach (var (table, column) in bounded)
        {
            var tableBlock = Regex.Match(widening, $@"alter table __SCHEMA__\.{table}\b(?<body>[^;]*);");
            tableBlock.Success.Should().BeTrue($"070 must alter {table}");
            tableBlock.Groups["body"].Value.Should().Contain($"alter column {column} type numeric", $"{table}.{column} is numeric(28,10)");
        }
    }

    [Fact]
    public void Migration_OnlyWidens()
    {
        // The header comment names the old typmod and the triggers; judge the statements only.
        var sql = Regex.Replace(ReadMigration(MigrationFile), "--[^\n]*", string.Empty);

        sql.Should().Contain("alter column", "the statements must survive comment stripping");
        sql.Should().NotMatchRegex(@"numeric\(\d", "the migration removes typmods; it never sets a new bound");
        sql.Should().NotContainAny("drop ", "update ", "delete ", "truncate ");
    }

    private static IEnumerable<(string Table, string Column)> BoundedColumns(string sql)
    {
        var headers = TableHeader.Matches(sql).ToArray();
        for (var i = 0; i < headers.Length; i++)
        {
            var start = headers[i].Index;
            var end = i + 1 < headers.Length ? headers[i + 1].Index : sql.Length;
            var body = sql[start..end];
            foreach (Match column in BoundedColumn.Matches(body))
            {
                yield return (headers[i].Groups["table"].Value, column.Groups["column"].Value);
            }
        }
    }

    private static string ReadMigration(string migrationFile)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", migrationFile));
    }
}
