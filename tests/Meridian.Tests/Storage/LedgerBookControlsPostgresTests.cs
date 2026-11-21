using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class LedgerBookControlsPostgresTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Migration_ExistingBooksWarn_NewBooksEnforce_ReapplyPreservesMode()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var schema = database.CreateSchemaName("control_migration");
        var oldBook = Guid.NewGuid();
        var newBook = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        var path = FindMigration();
        var script = (await File.ReadAllTextAsync(path)).Replace("__SCHEMA__", $"\"{schema}\"", StringComparison.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"create schema \"{schema}\"; create table \"{schema}\".ledger_books(ledger_book_id uuid primary key); insert into \"{schema}\".ledger_books values (@old);";
        command.Parameters.AddWithValue("old", oldBook);
        await command.ExecuteNonQueryAsync();
        command.Parameters.Clear();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"insert into \"{schema}\".ledger_books values (@new);";
        command.Parameters.AddWithValue("new", newBook);
        await command.ExecuteNonQueryAsync();
        command.Parameters.Clear();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"select mode from \"{schema}\".ledger_book_controls where ledger_book_id=@book";
        command.Parameters.AddWithValue("book", oldBook);
        Assert.Equal("LegacyWarning", await command.ExecuteScalarAsync());
        command.Parameters["book"].Value = newBook;
        Assert.Equal("Enforced", await command.ExecuteScalarAsync());
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task NewBook_IsEnforcedDespiteOldCreatedAt_AndMissingPreflightCannotActivateLegacy()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var bookId = Guid.NewGuid();
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "synthetic-fund", Guid.NewGuid(),
            FundStructureNodeKindDto.Entity, "Synthetic entity", "USD", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        var controls = await database.JournalStore.GetBookControlsAsync(bookId);
        Assert.Equal(LedgerBookControlModeDto.Enforced, controls!.Mode);
        Assert.True(controls.CreatedAtUtc > DateTimeOffset.UnixEpoch);
        var preflight = await database.JournalStore.PrepareBookControlsAsync(bookId);
        Assert.False(preflight.CanActivate);
        Assert.Contains(preflight.Decisions, item => item.Code == "preflight-unavailable");
    }

    private static string FindMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "Meridian.Storage", "Ledger", "Migrations", "V_ledger_061__entity_controls.sql");
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The entity control migration could not be located.");
    }
}
