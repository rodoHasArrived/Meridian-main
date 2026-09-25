using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ScopedCredentialRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-scoped-recovery", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "connection-a", "account-a", "paper");
    private static readonly ProviderCredentialScope Other = new("tenant-b", "connection-a", "account-a", "paper");

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
