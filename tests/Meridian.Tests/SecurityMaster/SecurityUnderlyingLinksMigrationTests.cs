using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migration 049 creates the underlying-graph table additively, with the invariants the store relies
/// on; migration 051 admits the graph's governance stream without narrowing any existing one.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityUnderlyingLinksMigrationTests
{
    private const string MigrationFile = "049_security_underlying_links.sql";
    private const string GovernanceStreamMigrationFile = "051_security_governance_events_underlying_graph_stream.sql";

    [Fact]
    public void Migration_CreatesTheGraphTable_WithItsInvariants()
    {
        var sql = ReadMigration(MigrationFile);

        sql.Should().Contain("create table if not exists __SCHEMA__.security_underlying_links");
        sql.Should().Contain("link_id                uuid        not null primary key");
        sql.Should().Contain("check (underlying_security_id is not null or reference_text is not null)", "every edge names a security or a reference");
        sql.Should().Contain("check (source in ('Declared', 'Derived'))");
        sql.Should().Contain("check (valid_to is null or valid_to > valid_from)");
        sql.Should().Contain("on __SCHEMA__.security_underlying_links (underlying_security_id)", "the reverse walk is indexed");
        sql.Should().NotContain("underlying_security_id uuid        null references", "a dangling target is a gap, not a refusal");
        sql.Should().NotContain("drop ");
    }

    [Fact]
    public void GovernanceStreamMigration_ReplacesOnlyTheStreamCheck_AndKeepsEveryExistingStream()
    {
        var sql = ReadMigration(GovernanceStreamMigrationFile);

        sql.Should().Contain("drop constraint security_governance_events_stream_check;");
        sql.Should().Contain("check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild', 'UnderlyingGraph'))");
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
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", migrationFile));
    }
}
