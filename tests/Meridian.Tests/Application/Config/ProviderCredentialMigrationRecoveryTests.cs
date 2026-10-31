using FluentAssertions;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Tests.Application.Config;

/// <summary>
/// Protects operator recovery after ownership transfer publication is interrupted or refused,
/// including a later credential rotation that must never be undone by retrying an old receipt.
/// </summary>
public sealed class ProviderCredentialMigrationRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-migration-recovery", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "connection-a", "account-a", "paper");

    private FileProviderCredentialStore Store() => new(_root, _ => null);
    private static ProviderCredentialSaveRequest Request(string key = "synthetic-key") => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = "synthetic-secret" }, "paper", "operator");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransferRetry_InterruptedRecoveryPublicationRepairsBackupWithoutReapplyingOperation(bool rollback)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var store = Store();
        await store.SaveAsync(Request(), ct);
        var original = (await store.ReadForProviderAsync("alpaca", ct))!;
        var expected = await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator", ct);
        if (rollback)
            expected = await store.RollbackMigrationAsync("alpaca", Owner, "migration-a", "controller", ct);
        var auditPath = Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl");
        var auditBeforeRetry = await File.ReadAllTextAsync(auditPath, ct);

        // A transfer publishes its receipt and primary before mirroring the recovery generation.
        // This is the durable filesystem state left by a process exit between those publications.
        File.Delete(store.VaultPath + ".bak");
        var restarted = Store();
        var retry = rollback
            ? await restarted.RollbackMigrationAsync("alpaca", Owner, "migration-a", "controller", ct)
            : await restarted.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator", ct);

        retry.Should().Be(expected);
        File.Exists(store.VaultPath + ".bak").Should().BeTrue("a successful retry must restore the committed recovery generation");
        (await File.ReadAllTextAsync(auditPath, ct)).Should().Be(auditBeforeRetry, "retry must not audit a second transfer");
        await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary", ct);
        var recovered = Store();
        if (rollback)
        {
            (await recovered.ReadScopedAsync("alpaca", Owner, ct)).Should().BeNull();
            (await recovered.ReadForProviderAsync("alpaca", ct))!.Credentials.Should().BeEquivalentTo(original.Credentials);
        }
        else
        {
            (await recovered.ReadForProviderAsync("alpaca", ct)).Should().BeNull();
            (await recovered.ReadScopedAsync("alpaca", Owner, ct))!.Credentials.Should().BeEquivalentTo(original.Credentials);
        }
    }

    [Fact]
    public async Task MigrationRetry_AfterRotationRepairsRecoveryWithCurrentSecretAndRetainsOriginalReceipt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var store = Store();
        await store.SaveAsync(Request(), ct);
        var original = (await store.ReadForProviderAsync("alpaca", ct))!;
        var receipt = await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator", ct);
        var owned = (await store.ReadScopedAsync("alpaca", Owner, ct))!;
        var replacementGeneration = await store.SaveScopedRotatedCredentialsAsync(Request("synthetic-replacement"), Owner, owned, ct);
        File.Delete(store.VaultPath + ".bak");

        var restarted = Store();
        (await restarted.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator", ct))
            .Should().Be(receipt);

        File.Exists(store.VaultPath + ".bak").Should().BeTrue();
        await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary", ct);
        var recovered = (await Store().ReadScopedAsync("alpaca", Owner, ct))!;
        recovered.Get("KeyId").Should().Be("synthetic-replacement");
        recovered.CredentialGeneration.Should().Be(replacementGeneration);
        (await Store().ReadForProviderAsync("alpaca", ct)).Should().BeNull();
        Func<Task> rollback = () => Store().RollbackMigrationAsync("alpaca", Owner, "migration-a", "controller", ct);
        await rollback.Should().ThrowAsync<ProviderCredentialConflictException>();
    }

    [Fact]
    public async Task Rollback_AfterOperatorReplacementAndPrimaryCorruptionStillRejectsOriginalGeneration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var store = Store();
        await store.SaveAsync(Request(), ct);
        var original = (await store.ReadForProviderAsync("alpaca", ct))!;
        await store.MigrateToScopeAsync("alpaca", Owner, "migration-a", original.CredentialGeneration, "operator", ct);

        // Settings credential replacement uses SaveScopedAsync, not the remote token-rotation API.
        // The former generation is just as unsafe to restore after an operator replaces/revokes it.
        await store.SaveScopedAsync(Request("operator-replacement"), Owner, ct);
        var replacement = (await store.ReadScopedAsync("alpaca", Owner, ct))!;
        Func<Task> rollback = () => Store().RollbackMigrationAsync("alpaca", Owner, "migration-a", "controller", ct);
        await rollback.Should().ThrowAsync<ProviderCredentialConflictException>();
        await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary", ct);

        await rollback.Should().ThrowAsync<ProviderCredentialConflictException>();

        var recovered = (await Store().ReadScopedAsync("alpaca", Owner, ct))!;
        recovered.CredentialGeneration.Should().Be(replacement.CredentialGeneration);
        recovered.Get("KeyId").Should().Be("operator-replacement");
        (await Store().ReadForProviderAsync("alpaca", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Transfer_FromBackupRecoveryPreservesOnlyReadableGenerationWhenPublicationIsRefused()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var store = Store();
        await store.SaveAsync(Request("retained-key"), ct);
        await store.SaveAsync(Request("newer-key"), ct);
        var backup = store.VaultPath + ".bak";
        var retainedBytes = await File.ReadAllBytesAsync(backup, ct);

        // The primary is unavailable and its destination cannot accept the atomic replacement.
        // Reading from backup is supported; a refused write must not erase that sole good copy.
        File.Delete(store.VaultPath);
        Directory.CreateDirectory(store.VaultPath);
        var retained = (await store.ReadForProviderAsync("alpaca", ct))!;
        retained.Get("KeyId").Should().Be("retained-key");
        Func<Task> migrate = () => store.MigrateToScopeAsync("alpaca", Owner, "migration-a", retained.CredentialGeneration, "operator", ct);

        var failure = await migrate.Should().ThrowAsync<Exception>();
        failure.Which.Should().Match<Exception>(exception => exception is IOException || exception is UnauthorizedAccessException);

        File.Exists(backup).Should().BeTrue("a refused primary publication must preserve the only readable vault generation");
        (await File.ReadAllBytesAsync(backup, ct)).Should().Equal(retainedBytes);
        (await Store().ReadForProviderAsync("alpaca", ct))!.Credentials.Should().BeEquivalentTo(retained.Credentials);
        (await Store().ReadScopedAsync("alpaca", Owner, ct)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfer_FromMissingOrCorruptPrimaryPublishesOwnedRecoveryWithoutLegacyResurrection(bool corruptPrimary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var store = Store();
        await store.SaveAsync(Request("retained-key"), ct);
        await store.SaveRotatedCredentialsAsync(Request("retained-key"), ct);
        var retained = (await store.ReadForProviderAsync("alpaca", ct))!;
        if (corruptPrimary)
            await File.WriteAllTextAsync(store.VaultPath, "corrupt-primary", ct);
        else
            File.Delete(store.VaultPath);

        await Store().MigrateToScopeAsync("alpaca", Owner, "recovered-transfer", retained.CredentialGeneration, "operator", ct);

        var restarted = Store();
        (await restarted.ReadForProviderAsync("alpaca", ct)).Should().BeNull();
        (await restarted.ReadScopedAsync("alpaca", Owner, ct))!.Credentials.Should().BeEquivalentTo(retained.Credentials);
        (await File.ReadAllBytesAsync(store.VaultPath + ".bak", ct)).Should().Equal(await File.ReadAllBytesAsync(store.VaultPath, ct));

        // A second recovery must retain the new ownership, never the provider-wide source snapshot.
        File.Delete(store.VaultPath);
        var recovered = Store();
        (await recovered.ReadForProviderAsync("alpaca", ct)).Should().BeNull();
        var owned = (await recovered.ReadScopedAsync("alpaca", Owner, ct))!;
        owned.CredentialGeneration.Should().Be(retained.CredentialGeneration);
        owned.Credentials.Should().BeEquivalentTo(retained.Credentials);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
