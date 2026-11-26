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
}
