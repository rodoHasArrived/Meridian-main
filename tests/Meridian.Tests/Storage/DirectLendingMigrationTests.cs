using FluentAssertions;

namespace Meridian.Tests.Storage;

public sealed class DirectLendingMigrationTests
{
    [Theory]
    [InlineData("005_direct_lending_workflows.sql")]
    [InlineData("005_direct_lending_operations.sql")]
    public void DirectLendingOutboxMigrations_DefineIdempotentPendingOutboxIndexes(string fileName)
    {
        var sql = ReadMigration(fileName);

        sql.Should().Contain("create table if not exists __SCHEMA__.outbox_message");
        sql.Should().Contain("message_key");
        sql.Should().Contain("processed_at");
        sql.Should().Contain("visible_after");
        sql.Should().Contain("where processed_at is null");
        sql.Should().Contain("create unique index if not exists ux_outbox_message_topic_message_key");
        sql.Should().Contain("on __SCHEMA__.outbox_message");
        sql.Should().Contain("topic");
        sql.Should().Contain("message_key");
    }

    private static string ReadMigration(string fileName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "DirectLending", "Migrations", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
