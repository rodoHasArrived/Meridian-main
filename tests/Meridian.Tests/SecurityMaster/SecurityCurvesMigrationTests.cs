using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migration 052 creates the curve tables additively with the invariants the store relies on;
/// migration 053 admits the curves' governance stream without narrowing any existing one.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityCurvesMigrationTests
{
    private const string CurvesMigrationFile = "052_security_curves.sql";
    private const string GovernanceStreamMigrationFile = "053_security_governance_events_curves_stream.sql";

    [Fact]
    public void CurvesMigration_CreatesBothTables_WithTheirInvariants()
    {
        var sql = ReadMigration(CurvesMigrationFile);

        sql.Should().Contain("create table if not exists __SCHEMA__.security_curves");
        sql.Should().Contain("create table if not exists __SCHEMA__.security_curve_versions");
        sql.Should().Contain("check (kind in ('Discount', 'Forward', 'Inflation'))");
        sql.Should().Contain("check ((kind = 'Discount') = (index_name is null))", "a discount curve has no index and every other kind has one");
        sql.Should().Contain("check (currency = upper(currency) and length(currency) = 3)");
        sql.Should().Contain("ux_security_curves_key").And.Contain("(upper(curve_key))", "the key is unique ignoring case");
        sql.Should().Contain("unique (curve_id, version)");
        sql.Should().Contain("unique (curve_id, as_of, content_sha256)", "the same content for the same as-of is one row");
        sql.Should().Contain("on __SCHEMA__.security_curve_versions (curve_id, as_of desc, version desc)", "the as-of read is indexed");
        sql.Should().Contain("before update or delete on __SCHEMA__.security_curve_versions");
        sql.Should().Contain("before truncate on __SCHEMA__.security_curve_versions");
        sql.Should().Contain("before update or delete on __SCHEMA__.security_curves\n", "a curve's identity is write-once in the schema, not only in the store");
        sql.Should().Contain("before truncate on __SCHEMA__.security_curves\n");
        sql.Should().Contain("errcode = '23514'");
        System.Text.RegularExpressions.Regex.Matches(sql, @"execute function __SCHEMA__\.reject_security_curve_mutation\(\);")
            .Should().HaveCount(4, "one reject function guards update, delete and truncate on both tables");
        sql.Should().NotContain("drop ");
    }

    [Fact]
    public void GovernanceStreamMigration_ReplacesOnlyTheStreamCheck_AndKeepsEveryExistingStream()
    {
        var sql = ReadMigration(GovernanceStreamMigrationFile);

        sql.Should().Contain("drop constraint security_governance_events_stream_check;");
        sql.Should().Contain("check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild', 'UnderlyingGraph', 'Curves'))");
        System.Text.RegularExpressions.Regex.Matches(sql, @"\bdrop\b").Should().ContainSingle("only the stream check is replaced");
        sql.Should().NotContain("delete ").And.NotContain("update ").And.NotContain("truncate");
    }

    private static string ReadMigration(string migrationFile)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", migrationFile)).ReplaceLineEndings("\n");
    }
}
