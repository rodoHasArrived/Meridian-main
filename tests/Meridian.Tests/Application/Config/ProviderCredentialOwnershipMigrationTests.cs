using System.Net;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
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
        {
            // A provider-wide token transfers with the record; the conflict is a token the owned scope already holds.
            await store.SaveOAuthTokenAsync("alpaca", new OAuthToken("access", "Bearer", DateTimeOffset.UtcNow.AddHours(1), RefreshToken: "refresh"));
            await store.SaveScopedOAuthTokenAsync("alpaca", new OAuthToken("owned", "Bearer", DateTimeOffset.UtcNow.AddHours(1)), Owner);
        }
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
                    ["ClientId"] = "legacy-client",
                    ["ClientSecret"] = "legacy-secret",
                    ["RefreshToken"] = "legacy-refresh",
                    ["RealmId"] = "account-a"
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

    [Fact]
    public async Task Transfer_MovesTheProviderWideOAuthTokenUnderTheScope_AndBackOnRollback()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        var token = new OAuthToken("access-1", "Bearer", DateTimeOffset.UtcNow.AddHours(1), "refresh-1");
        await store.SaveOAuthTokenAsync("alpaca", token);
        var original = (await store.ReadForProviderAsync("alpaca"))!;

        var receipt = await store.MigrateToScopeAsync("alpaca", Owner, "migration-oauth", original.CredentialGeneration, "operator");
        receipt.OAuthTokenTransferred.Should().BeTrue();
        (await store.ReadOAuthTokensAsync()).Should().BeEmpty("the provider-wide token no longer exists beside the owned record");
        (await store.ReadScopedOAuthTokensAsync(Owner)).Should().ContainSingle().Which.Value.Should().Be(token);
        (await store.ReadScopedOAuthTokensAsync(new("tenant-b", "connection-a", "account-a", "paper"))).Should().BeEmpty();

        var rolledBack = await store.RollbackMigrationAsync("alpaca", Owner, "migration-oauth", "controller");
        rolledBack.RolledBack.Should().BeTrue();
        rolledBack.OAuthTokenTransferred.Should().BeTrue();
        (await store.ReadScopedOAuthTokensAsync(Owner)).Should().BeEmpty();
        (await store.ReadOAuthTokensAsync()).Should().ContainSingle().Which.Value.Should().Be(token);
        var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl"));
        audit.Should().NotContain("access-1").And.NotContain("refresh-1");
    }

    [Fact]
    public async Task Transfer_RefusesWhenTheOwnedScopeAlreadyHoldsAnOAuthToken()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        await store.SaveOAuthTokenAsync("alpaca", new OAuthToken("legacy", "Bearer", DateTimeOffset.UtcNow.AddHours(1)));
        // The owned token names the provider under another spelling; the target is still occupied.
        await store.SaveScopedOAuthTokenAsync("Alpaca", new OAuthToken("owned", "Bearer", DateTimeOffset.UtcNow.AddHours(1)), Owner);
        var original = (await store.ReadForProviderAsync("alpaca"))!;

        var act = () => store.MigrateToScopeAsync("alpaca", Owner, "migration-conflict", original.CredentialGeneration, "operator");

        await act.Should().ThrowAsync<ProviderCredentialConflictException>();
        (await store.ReadForProviderAsync("alpaca"))!.Credentials.Should().BeEquivalentTo(original.Credentials);
        (await store.ReadOAuthTokensAsync()).Should().ContainKey("alpaca");
    }

    [Fact]
    public async Task TransferredOAuthToken_CannotBeRepublishedProviderWide_AndACachedRuntimeEvictsItWithoutARemoteRefresh()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        using var handler = new RotatingProvider();
        using var client = new HttpClient(handler);
        await using var cached = new OAuthTokenRefreshService(_root, httpClient: client, vault: store);
        cached.RegisterProvider(new OAuthProviderConfig("alpaca", "client", TokenEndpoint: "https://provider.example/token"));
        var token = new OAuthToken("access-1", "Bearer", DateTimeOffset.UtcNow.AddHours(1), "refresh-1");
        await cached.StoreTokenAsync("alpaca", token);
        var original = (await store.ReadForProviderAsync("alpaca"))!;

        (await store.MigrateToScopeAsync("alpaca", Owner, "migration-cache", original.CredentialGeneration, "operator")).OAuthTokenTransferred.Should().BeTrue();

        var refresh = await cached.RefreshTokenAsync("alpaca");
        refresh.Success.Should().BeFalse();
        refresh.Error.Should().Contain("ownership changed");
        handler.Calls.Should().Be(0, "a runtime that no longer owns the token must not rotate it at the provider");
        cached.GetToken("alpaca").Should().BeNull("the transferred token is evicted from the provider-wide runtime");
        (await store.ReadScopedOAuthTokensAsync(Owner)).Should().ContainSingle().Which.Value.Should().Be(token, "the owner's refresh token is untouched");

        var republish = () => store.SaveOAuthTokenAsync("Alpaca", new OAuthToken("access-2", "Bearer", DateTimeOffset.UtcNow.AddHours(1), "refresh-2"));
        await republish.Should().ThrowAsync<ProviderCredentialConflictException>().WithMessage("*transferred to an owned connection*");
        await store.ImportOAuthTokensAsync(new Dictionary<string, OAuthToken> { ["alpaca"] = token });
        (await store.ReadOAuthTokensAsync()).Should().BeEmpty("a legacy snapshot never republishes a transferred token provider-wide");

        await store.RollbackMigrationAsync("alpaca", Owner, "migration-cache", "controller");
        (await store.ReadOAuthTokensAsync()).Should().ContainSingle().Which.Value.Should().Be(token);
        await store.SaveOAuthTokenAsync("alpaca", new OAuthToken("access-3", "Bearer", DateTimeOffset.UtcNow.AddHours(1), "refresh-3"));
        (await store.ReadOAuthTokensAsync())["alpaca"].AccessToken.Should().Be("access-3", "rollback restores provider-wide ownership and its saves");
    }

    [Fact]
    public async Task Finalize_RequiresVerificationUnderTheNewOwnership_ThenRetiresRollbackAndReleasesTheProviderWideRecord()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request());
        var original = (await store.ReadForProviderAsync("alpaca"))!;
        var receipt = await store.MigrateToScopeAsync("alpaca", Owner, "migration-f", original.CredentialGeneration, "operator");

        var unverified = () => store.FinalizeMigrationAsync("alpaca", Owner, "migration-f", "controller");
        await unverified.Should().ThrowAsync<InvalidOperationException>().WithMessage("*successful verification*");
        var legacySave = () => store.SaveAsync(Request("other-key"));
        await legacySave.Should().ThrowAsync<ProviderCredentialConflictException>("the active migration still blocks the provider-wide record");

        var ownedGeneration = (await store.ReadScopedAsync("alpaca", Owner))!.CredentialGeneration;
        await store.RecordScopedVerificationAsync(new("alpaca", false, "boom") { ExpectedCredentialGeneration = ownedGeneration }, Owner);
        await unverified.Should().ThrowAsync<InvalidOperationException>("a failed verification does not finalize");
        await store.RecordScopedVerificationAsync(new("alpaca", true, ExternalAccountId: Owner.ExternalAccountId, Actor: "verifier")
        { ExpectedCredentialGeneration = ownedGeneration }, Owner);

        var finalized = await store.FinalizeMigrationAsync("alpaca", Owner, "migration-f", "controller");
        finalized.FinalizedBy.Should().Be("controller");
        finalized.FinalizedAt.Should().NotBeNull();
        finalized.IsActive.Should().BeFalse();
        var backup = store.VaultPath + ".bak";
        (await File.ReadAllBytesAsync(backup)).Should().Equal(await File.ReadAllBytesAsync(store.VaultPath),
            "both recovery generations hold the finalized receipt");
        File.Delete(backup);
        store = new FileProviderCredentialStore(_root);
        (await store.FinalizeMigrationAsync("alpaca", Owner, "migration-f", "someone-else")).Should().Be(finalized, "finalization is idempotent");
        File.Exists(backup).Should().BeTrue("a finalized retry repairs the recovery generation");
        (await File.ReadAllBytesAsync(backup)).Should().Equal(await File.ReadAllBytesAsync(store.VaultPath));
        File.Delete(store.VaultPath);
        store = new FileProviderCredentialStore(_root);
        var recoveredRollback = () => store.RollbackMigrationAsync("alpaca", Owner, "migration-f", "controller");
        await recoveredRollback.Should().ThrowAsync<InvalidOperationException>().WithMessage("*finalized migration cannot be rolled back*",
            "recovery from the backup generation cannot reopen rollback of a finalized migration");
        var rollback = () => store.RollbackMigrationAsync("alpaca", Owner, "migration-f", "controller");
        await rollback.Should().ThrowAsync<InvalidOperationException>().WithMessage("*finalized migration cannot be rolled back*");
        (await store.ReadScopedAsync("alpaca", Owner))!.Credentials.Should().BeEquivalentTo(original.Credentials);
        await store.SaveAsync(Request("other-key"));
        (await store.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("other-key", "a finalized migration no longer blocks a new provider-wide record");
        var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl"));
        audit.Should().Contain("ownership-finalize").And.Contain("migration-f");
        audit.Split('\n').Count(line => line.Contains("ownership-finalize")).Should().Be(1, "retries add no finalization or audit event");
        _ = receipt;
    }

    private sealed class RotatingProvider : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"replacement-access","refresh_token":"replacement-refresh","token_type":"Bearer","expires_in":3600}""")
            });
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
