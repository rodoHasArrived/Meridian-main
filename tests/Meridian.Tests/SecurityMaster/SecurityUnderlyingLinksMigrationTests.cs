using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Migration 049 creates the underlying-graph table additively, with the invariants the store relies on.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityUnderlyingLinksMigrationTests
{
    private const string MigrationFile = "049_security_underlying_links.sql";

    [Fact]
    public void Migration_CreatesTheGraphTable_WithItsInvariants()
    {
        var sql = ReadMigration();

        sql.Should().Contain("create table if not exists __SCHEMA__.security_underlying_links");
        sql.Should().Contain("link_id                uuid        not null primary key");
        sql.Should().Contain("check (underlying_security_id is not null or reference_text is not null)", "every edge names a security or a reference");
        sql.Should().Contain("check (source in ('Declared', 'Derived'))");
        sql.Should().Contain("check (valid_to is null or valid_to > valid_from)");
        sql.Should().Contain("on __SCHEMA__.security_underlying_links (underlying_security_id)", "the reverse walk is indexed");
        sql.Should().NotContain("underlying_security_id uuid        null references", "a dangling target is a gap, not a refusal");
        sql.Should().NotContain("drop ");
    }

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", MigrationFile));
    }
}
