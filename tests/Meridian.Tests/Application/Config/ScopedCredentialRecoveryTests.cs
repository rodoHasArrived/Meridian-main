using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Contracts.Configuration;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ScopedCredentialRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-scoped-recovery", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "connection-a", "account-a", "paper");
    private static readonly ProviderCredentialScope Other = new("tenant-b", "connection-a", "account-a", "paper");

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData(null, false)]
    public async Task ScopedVerification_WithoutTestedGenerationCannotVerifyOrInvalidateReplacement(string? generation, bool success)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveScopedAsync(Request("tested"), Owner);
        await vault.SaveScopedAsync(Request("replacement"), Owner);
        var current = (await vault.ReadScopedAsync("alpaca", Owner))!;
        await vault.RecordScopedVerificationAsync(new("alpaca", true, ExternalAccountId: Owner.ExternalAccountId)
        { ExpectedCredentialGeneration = current.CredentialGeneration }, Owner);
        var vaultBefore = await File.ReadAllBytesAsync(vault.VaultPath);
        var auditPath = Path.Combine(Path.GetDirectoryName(vault.VaultPath)!, "provider-credentials.audit.jsonl");
        var auditBefore = await File.ReadAllBytesAsync(auditPath);

        var verify = () => vault.RecordScopedVerificationAsync(new("alpaca", success,
            ExternalAccountId: Owner.ExternalAccountId)
        { ExpectedCredentialGeneration = generation }, Owner);

        await verify.Should().ThrowAsync<ProviderCredentialConflictException>();
        (await File.ReadAllBytesAsync(vault.VaultPath)).Should().Equal(vaultBefore);
        (await File.ReadAllBytesAsync(auditPath)).Should().Equal(auditBefore);
        (await vault.GetScopedStatusAsync("alpaca", Owner)).VerificationState.Should().Be(ProviderVerificationStateDto.Verified);
    }

    [Fact]
    public async Task LegacyImportAndOwnedRotation_KeepVerificationSeparateAndNeverRecoverRevokedSecret()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.ImportLegacyAsync([Request("legacy")]);
        var legacy = (await vault.ReadForProviderAsync("alpaca"))!;
        await vault.RecordVerificationAsync(new("alpaca", true, ExternalAccountId: Owner.ExternalAccountId)
        { ExpectedCredentialGeneration = legacy.CredentialGeneration });
        // Historical verification never authorizes or creates a tenant-owned connection.
        (await vault.ReadScopedAsync("alpaca", Owner)).Should().BeNull();
        await vault.SaveScopedAsync(Request("owned"), Owner);
        await vault.SaveScopedAsync(Request("foreign"), Other);
        var owned = (await vault.ReadScopedAsync("alpaca", Owner))!;
        (await vault.GetScopedStatusAsync("alpaca", Owner)).VerificationState.Should().NotBe(ProviderVerificationStateDto.Verified);
        await vault.RecordScopedVerificationAsync(new("alpaca", true, ExternalAccountId: Owner.ExternalAccountId)
        { ExpectedCredentialGeneration = owned.CredentialGeneration }, Owner);
        var verified = (await vault.ReadScopedAsync("alpaca", Owner))!;

        var replacementGeneration = await vault.SaveScopedRotatedCredentialsAsync(Request("replacement"), Owner, verified);
        var staleRotation = () => vault.SaveScopedRotatedCredentialsAsync(Request("late-rotation"), Owner, verified);
        await staleRotation.Should().ThrowAsync<ProviderCredentialConflictException>();
        var staleVerification = () => vault.RecordScopedVerificationAsync(new("alpaca", true,
            ExternalAccountId: Owner.ExternalAccountId)
        { ExpectedCredentialGeneration = verified.CredentialGeneration }, Owner);
        await staleVerification.Should().ThrowAsync<ProviderCredentialConflictException>();

        // A completed remote rotation invalidates the old secret; recovery must retain its replacement.
        await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");
        var recovered = new FileProviderCredentialStore(_root);
        var replacement = (await recovered.ReadScopedAsync("alpaca", Owner))!;
        replacement.CredentialGeneration.Should().Be(replacementGeneration);
        replacement.Get("KeyId").Should().Be("replacement");
        (await recovered.GetScopedStatusAsync("alpaca", Owner)).VerificationState.Should().NotBe(ProviderVerificationStateDto.Verified);
        (await recovered.ReadScopedAsync("alpaca", Other))!.Get("KeyId").Should().Be("foreign");
        (await recovered.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("legacy");

        await recovered.DeleteScopedAsync("alpaca", Owner, "operator");
        await File.WriteAllTextAsync(recovered.VaultPath, "corrupt-primary-again");
        var afterDelete = new FileProviderCredentialStore(_root);
        (await afterDelete.ReadScopedAsync("alpaca", Owner)).Should().BeNull();
        (await afterDelete.ReadScopedAsync("alpaca", Other))!.Get("KeyId").Should().Be("foreign");
        (await afterDelete.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("legacy");
        var audit = await File.ReadAllTextAsync(Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl"));
        audit.Should().NotContain("secret-owned").And.NotContain("secret-replacement").And.NotContain("secret-foreign");
    }

    [Theory]
    [InlineData("current")]
    [InlineData("replaced")]
    [InlineData("deleted")]
    [InlineData("other-scope")]
    public async Task ScopedVerification_RequiresTheExpectedOwnedGeneration(string transition)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Request("unassigned"));
        await vault.SaveScopedAsync(Request("owned"), Owner);
        await vault.SaveScopedAsync(Request("foreign"), Other);
        var original = (await vault.ReadScopedAsync("alpaca", Owner))!;
        var foreign = (await vault.ReadScopedAsync("alpaca", Other))!;
        var unassigned = (await vault.ReadForProviderAsync("alpaca"))!;
        if (transition == "replaced")
            await vault.SaveScopedAsync(Request("replacement"), Owner);
        else if (transition == "deleted")
            await vault.DeleteScopedAsync("alpaca", Owner, "operator");
        var vaultBefore = await File.ReadAllBytesAsync(vault.VaultPath);
        var auditPath = Path.Combine(Path.GetDirectoryName(vault.VaultPath)!, "provider-credentials.audit.jsonl");
        var auditBefore = await File.ReadAllBytesAsync(auditPath);
        var verifiedAt = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var update = new ProviderCredentialVerificationUpdate("alpaca", true,
            ExternalAccountId: Owner.ExternalAccountId, VerifiedAt: verifiedAt, Actor: "controller")
        {
            ExpectedCredentialGeneration = transition == "other-scope"
                ? foreign.CredentialGeneration : original.CredentialGeneration
        };

        var verify = () => vault.RecordScopedVerificationAsync(update, Owner);

        if (transition == "current")
        {
            await verify();
            var retained = (await new FileProviderCredentialStore(_root).ReadScopedAsync("alpaca", Owner))!;
            retained.CredentialGeneration.Should().Be(original.CredentialGeneration);
            retained.LastVerifiedAt.Should().Be(verifiedAt);
            retained.AuditMetadata["lastVerifiedBy"].Should().Be("controller");
        }
        else
        {
            await verify.Should().ThrowAsync<ProviderCredentialConflictException>();
            (await File.ReadAllBytesAsync(vault.VaultPath)).Should().Equal(vaultBefore);
            (await File.ReadAllBytesAsync(auditPath)).Should().Equal(auditBefore);
        }
        (await vault.ReadScopedAsync("alpaca", Other)).Should().BeEquivalentTo(foreign);
        (await vault.ReadForProviderAsync("alpaca")).Should().BeEquivalentTo(unassigned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadScopedOAuth_WriterLockHeldReadsOnlyOwnedPublishedGeneration(bool recoverBackup)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("provider", Token("unassigned"));
        await vault.SaveScopedOAuthTokenAsync("provider", Token("owned"), Owner);
        await vault.SaveScopedOAuthTokenAsync("provider", Token("foreign"), Other);
        await vault.SaveScopedOAuthTokenAsync("provider", Token("rotated"), Owner);
        if (recoverBackup)
            await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");
        using var writer = new FileStream(vault.VaultPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reopened = new FileProviderCredentialStore(_root);

        var owned = await reopened.ReadScopedOAuthTokensAsync(Owner, timeout.Token);
        var foreign = await reopened.ReadScopedOAuthTokensAsync(Other, timeout.Token);

        owned.Should().ContainSingle().Which.Value.AccessToken.Should().Be("rotated");
        foreign.Should().ContainSingle().Which.Value.AccessToken.Should().Be("foreign");
        (await reopened.ReadOAuthTokensAsync(timeout.Token)).Should().ContainSingle().Which.Value.AccessToken.Should().Be("unassigned");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadScopedProvider_WriterLockHeldReadsOnlyOwnedPublishedGeneration(bool recoverBackup)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Request("unassigned"));
        await vault.SaveScopedAsync(Request("owned"), Owner);
        await vault.SaveScopedAsync(Request("foreign"), Other);
        await vault.SaveScopedAsync(Request("rotated"), Owner);
        if (recoverBackup)
            await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");
        using var writer = new FileStream(vault.VaultPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reopened = new FileProviderCredentialStore(_root);

        var owned = await reopened.ReadScopedAsync("alpaca", Owner, timeout.Token);
        var foreign = await reopened.ReadScopedAsync("alpaca", Other, timeout.Token);

        owned!.Get("KeyId").Should().Be(recoverBackup ? "owned" : "rotated");
        foreign!.Get("KeyId").Should().Be("foreign");
        (await reopened.ReadForProviderAsync("alpaca", timeout.Token))!.Get("KeyId").Should().Be("unassigned");
    }

    [Fact]
    public async Task ReadScopedOAuth_AbsentVaultDoesNotCreateStorage()
    {
        var vault = new FileProviderCredentialStore(_root);

        (await vault.ReadScopedOAuthTokensAsync(Owner)).Should().BeEmpty();

        Directory.Exists(_root).Should().BeFalse("a status read cannot require a writable secret volume");
    }

    [Fact]
    public async Task ScopedInitialization_RetriesOwnedReadWithoutTouchingUnassignedMigrationFiles()
    {
        var legacyPath = Path.Combine(_root, ".mdc", "oauth_tokens.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(legacyPath, "unassigned-source");
        await File.WriteAllTextAsync(legacyPath + ".migrated", "unassigned-cleanup");
        var vault = new RecoverableScopedReadVault();
        await using var service = new OAuthTokenRefreshService(_root, vault: vault, ownershipScope: Owner);
        var first = () => service.InitializeAsync();
        await first.Should().ThrowAsync<InvalidOperationException>();

        await service.InitializeAsync();

        service.GetToken("provider")!.AccessToken.Should().Be("owned");
        vault.Reads.Should().Be(2);
        (await File.ReadAllTextAsync(legacyPath)).Should().Be("unassigned-source");
        (await File.ReadAllTextAsync(legacyPath + ".migrated")).Should().Be("unassigned-cleanup");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedAuditAppend_RecoversTailAndRetainsOwnership(bool providerCredentialWrite)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("provider", Token("unassigned"));
        var auditPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl");
        var committed = await File.ReadAllTextAsync(auditPath);
        const string tail = "{\"action\":\"interrupted";
        await File.AppendAllTextAsync(auditPath, tail);

        if (providerCredentialWrite)
            await vault.SaveScopedAsync(Request("owned"), Owner);
        else
            await vault.SaveScopedOAuthTokenAsync("provider", Token("owned"), Owner);

        var recovered = await File.ReadAllTextAsync(auditPath);
        recovered.Should().StartWith(committed).And.EndWith("\n");
        var lines = recovered.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        using var last = JsonDocument.Parse(lines[1]);
        var scope = last.RootElement.GetProperty("scope");
        scope.GetProperty("tenantId").GetString().Should().Be(Owner.TenantId);
        scope.GetProperty("connectionId").GetString().Should().Be(Owner.ConnectionId);
        scope.GetProperty("externalAccountId").GetString().Should().Be(Owner.ExternalAccountId);
        scope.GetProperty("environment").GetString().Should().Be(Owner.Environment);
        var tails = Directory.GetFiles(Path.GetDirectoryName(auditPath)!, "provider-credentials.audit.jsonl.partial-*");
        tails.Should().ContainSingle();
        (await File.ReadAllBytesAsync(tails[0])).Should().Equal(Encoding.UTF8.GetBytes(tail));
        (await vault.ReadOAuthTokensAsync())["provider"].AccessToken.Should().Be("unassigned");
    }

    private static OAuthToken Token(string value) => new(value, "Bearer", DateTimeOffset.UtcNow.AddHours(1), "refresh-" + value);

    private static ProviderCredentialSaveRequest Request(string value) => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = value, ["SecretKey"] = "secret-" + value }, "paper", "operator");

    private sealed class RecoverableScopedReadVault : IScopedOAuthTokenVault
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyDictionary<string, OAuthToken>> ReadScopedOAuthTokensAsync(ProviderCredentialScope scope, CancellationToken ct = default)
        {
            scope.Should().Be(Owner);
            if (++Reads == 1)
                return Task.FromException<IReadOnlyDictionary<string, OAuthToken>>(new IOException("Temporarily unavailable storage."));
            return Task.FromResult<IReadOnlyDictionary<string, OAuthToken>>(new Dictionary<string, OAuthToken> { ["provider"] = Token("owned") });
        }

        public Task SaveScopedOAuthTokenAsync(string providerName, OAuthToken? token, ProviderCredentialScope scope, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, OAuthToken>> ReadOAuthTokensAsync(CancellationToken ct = default)
            => throw new NotSupportedException("Scoped initialization cannot read unassigned tokens.");
        public Task SaveOAuthTokenAsync(string providerName, OAuthToken? token, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ImportOAuthTokensAsync(IReadOnlyDictionary<string, OAuthToken> tokens, CancellationToken ct = default)
            => throw new NotSupportedException("Scoped initialization cannot claim unassigned tokens.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
