using FluentAssertions;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Tests.Application.Config;

public sealed class ProviderCredentialOwnershipMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ownership-migration", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "connection-a", "account-a", "paper");
    private static ProviderCredentialSaveRequest Request(string key = "test-key") => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = "test-secret" }, "paper", "operator");

    [Fact]
    public async Task Transfer_RestartRetryAndRollbackPreserveSecretsAndAuditedOwnership()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        var original = (await store.ReadForProviderAsync("alpaca"))!;
        var receipt = await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator");
        store = new FileProviderCredentialStore(_root);
        (await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator")).Should().Be(receipt);
        (await store.ReadForProviderAsync("alpaca")).Should().BeNull();
        var owned = (await store.ReadScopedAsync("alpaca", Owner))!;
        owned.Credentials.Should().BeEquivalentTo(original.Credentials);
        owned.CredentialGeneration.Should().Be(original.CredentialGeneration);
        owned.LastVerifiedAt.Should().BeNull();
        (await store.ReadScopedAsync("alpaca", new("tenant-b", "connection-a", "account-a", "paper"))).Should().BeNull();

        var rolledBack = await store.RollbackMigrationAsync("alpaca", Owner, "migration-a", "controller");
        rolledBack.RolledBack.Should().BeTrue();
        rolledBack.RolledBackBy.Should().Be("controller");
        store = new FileProviderCredentialStore(_root);
        (await store.RollbackMigrationAsync("alpaca", Owner, "migration-a", "controller")).Should().Be(rolledBack);
        (await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator")).Should().Be(rolledBack);
        (await store.ReadScopedAsync("alpaca", Owner)).Should().BeNull();
        (await store.ReadForProviderAsync("alpaca"))!.Credentials.Should().BeEquivalentTo(original.Credentials);
        var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl"));
        audit.Should().Contain("ownership-migrate").And.Contain("ownership-rollback").And.Contain("migration-a");
        audit.Should().NotContain("test-secret").And.NotContain("test-key");
        (await File.ReadAllTextAsync(store.VaultPath)).Should().NotContain("test-secret");
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("environment")]
    [InlineData("destination")]
    [InlineData("oauth")]
    public async Task Transfer_ConflictingAuthorityNeverChangesEitherRecord(string conflict)
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        var original = (await store.ReadForProviderAsync("alpaca"))!;
        if (conflict == "destination")
            await store.SaveScopedAsync(Request("other"), Owner);
        if (conflict == "oauth")
            await store.SaveOAuthTokenAsync("alpaca", new OAuthToken("access", "Bearer", DateTimeOffset.UtcNow.AddHours(1), RefreshToken: "refresh"));
        var before = await File.ReadAllBytesAsync(store.VaultPath);
        var scope = conflict == "environment" ? new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "live") : Owner;
        Func<Task> migrate = () => store.MigrateToScopeAsync("alpaca", scope, "migration-a",
            conflict == "generation" ? "stale" : original.CredentialGeneration, "operator");

        await migrate.Should().ThrowAsync<Exception>();

        (await File.ReadAllBytesAsync(store.VaultPath)).Should().Equal(before);
        (await store.ReadForProviderAsync("alpaca"))!.Credentials.Should().BeEquivalentTo(original.Credentials);
    }

    [Fact]
    public async Task Rotation_PreventsRollbackOrLegacyOverwriteAndRecoversOnlyOwnedReplacement()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        var original = (await store.ReadForProviderAsync("alpaca"))!;
        await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator");
        var owned = (await store.ReadScopedAsync("alpaca", Owner))!;
        await store.SaveScopedRotatedCredentialsAsync(Request("rotated"), Owner, owned);
        Func<Task> rollback = () => store.RollbackMigrationAsync("alpaca", Owner, "migration-a", "controller");
        await rollback.Should().ThrowAsync<ProviderCredentialConflictException>();
        Func<Task> overwrite = () => store.SaveAsync(Request("legacy-replacement"));
        await overwrite.Should().ThrowAsync<ProviderCredentialConflictException>();
        await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary");

        var recovered = new FileProviderCredentialStore(_root);
        (await recovered.ReadScopedAsync("alpaca", Owner))!.Get("KeyId").Should().Be("rotated");
        (await recovered.ReadForProviderAsync("alpaca")).Should().BeNull();
    }

    [Fact]
    public async Task Migration_BackupRecoveryCannotResurrectUnassignedSecrets()
    {
        var store = new FileProviderCredentialStore(_root, name => name == "ALPACA_KEY_ID" ? "ambient-key" : null);
        await store.SaveAsync(Request());
        var original = (await store.ReadForProviderAsync("alpaca"))!;
        await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator");
        await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary");

        (await store.ReadForProviderAsync("alpaca")).Should().BeNull();
        (await store.ReadScopedAsync("alpaca", Owner))!.Get("KeyId").Should().Be("test-key");
    }

    [Fact]
    public async Task CancellationBeforeTransferLeavesLegacyRecordUntouched()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        var original = (await store.ReadForProviderAsync("alpaca"))!;
        var before = await File.ReadAllBytesAsync(store.VaultPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> migrate = () => store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration,
            "operator", cancellation.Token);
        await migrate.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(store.VaultPath)).Should().Equal(before);
    }

    [Fact]
    public async Task InterruptedAudit_ReplaysCommittedTransferOnceAfterRestart()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        var original = (await store.ReadForProviderAsync("alpaca"))!;
        var auditPath = Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl");
        File.Delete(auditPath);
        Directory.CreateDirectory(auditPath);
        var receipt = await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator");
        (await store.GetScopedStatusAsync("alpaca", Owner)).AuditMetadata.Should().ContainKey("auditPending");
        Directory.Delete(auditPath);
        store = new FileProviderCredentialStore(_root);
        (await store.ReplayPendingAuditAsync()).Should().Be(1);
        (await store.ReplayPendingAuditAsync()).Should().Be(0);
        (await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator")).Should().Be(receipt);
        (await File.ReadAllLinesAsync(auditPath)).Should().ContainSingle();
        (await store.ReadForProviderAsync("alpaca")).Should().BeNull();
    }

    [Fact]
    public async Task LegacyEnvironmentAlias_TransfersToCanonicalScopeAndRemainsReadableAfterRollback()
    {
        var store = new FileProviderCredentialStore(_root);
        await ProviderCredentialStoreTests.WriteEncryptedFixtureAsync(store, new Dictionary<string, object>
        {
            ["quickbooks"] = new
            {
                providerId = "quickbooks",
                environment = "live",
                fields = new Dictionary<string, string>
                {
                    ["ClientId"] = "legacy-client", ["ClientSecret"] = "legacy-secret",
                    ["RefreshToken"] = "legacy-refresh", ["RealmId"] = "account-a"
                },
                metadata = new Dictionary<string, string> { ["credentialGeneration"] = "legacy-generation" }
            }
        }, scoped: false);
        var scope = new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "production");
        (await store.ReadForProviderAsync("quickbooks"))!.Environment.Should().Be("live");

        await store.MigrateToScopeAsync("quickbooks", scope, "legacy-alias", "legacy-generation", "operator");

        var migrated = (await new FileProviderCredentialStore(_root).ReadScopedAsync("quickbooks", scope))!;
        migrated.Environment.Should().Be("production");
        migrated.Get("RefreshToken").Should().Be("legacy-refresh");
        await store.RollbackMigrationAsync("quickbooks", scope, "legacy-alias", "operator");
        (await new FileProviderCredentialStore(_root).ReadForProviderAsync("quickbooks"))!.Environment.Should().Be("production");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
