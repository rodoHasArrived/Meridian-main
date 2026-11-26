using FluentAssertions;
using Meridian.DataIntegration.Credentials;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

/// <summary>
/// Directory syncs that guard an irreversible credential step must fail closed, and the step
/// must succeed on retry once the directory can be synced again.
/// </summary>
[Collection("Sequential")]
public sealed class CredentialDirectorySyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-credential-dirsync", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Save_AuditJournalSyncFails_LeavesVaultUnwrittenAndRetrySucceeds()
    {
        const string id = "xero";
        var descriptor = ProviderCredentialCatalog.Find(id)!;
        var values = new ExternalGlTestStore(id).Values;
        var fields = descriptor.RequiredFields.Where(f => f.Required).ToDictionary(f => f.Name, f => (string?)values[f.Name]);
        var store = new FileProviderCredentialStore(_root);

        using (DirectorySyncFaults.FailIn(Path.Combine(_root, ".mdc")))
        {
            var save = () => store.SaveAsync(new(id, fields));
            await save.Should().ThrowAsync<DirectorySyncException>();
        }

        File.Exists(store.VaultPath).Should().BeFalse("the vault is mutated only after its owed audit is durable");

        await store.SaveAsync(new(id, fields));
        var reopened = await new FileProviderCredentialStore(_root).ReadForProviderAsync(id);
        foreach (var field in fields)
            reopened!.Get(field.Key).Should().Be(field.Value);
    }

    [Fact]
    public async Task LegacyMigration_RenameSyncFails_KeepsSnapshotUnerasedAndRetryFinishesWithoutReimport()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "legacy-credentials.json");
        const string snapshot = "{\"provider\":\"legacy-secret\"}";
        await File.WriteAllTextAsync(source, snapshot);
        var imports = 0;
        Task Import(string json, CancellationToken _)
        {
            json.Should().Be(snapshot);
            imports++;
            return Task.CompletedTask;
        }

        using (DirectorySyncFaults.FailIn(_root))
        {
            var migrate = () => LegacyCredentialFileMigration.MigrateAsync(source, Import);
            await migrate.Should().ThrowAsync<DirectorySyncException>();
            imports.Should().Be(1);
            File.Exists(source).Should().BeFalse();
            (await File.ReadAllTextAsync(source + ".migrated")).Should().Be(snapshot,
                "nothing may be erased before the rename out of the discovery path is durable");

            // Resuming the cleanup must not erase it either while the rename is still unconfirmed.
            await migrate.Should().ThrowAsync<DirectorySyncException>();
            (await File.ReadAllTextAsync(source + ".migrated")).Should().Be(snapshot);
        }

        await LegacyCredentialFileMigration.MigrateAsync(source, Import);

        imports.Should().Be(1, "a resumed cleanup never re-imports the snapshot");
        File.Exists(source).Should().BeFalse();
        File.Exists(source + ".migrated").Should().BeFalse();
    }
}
