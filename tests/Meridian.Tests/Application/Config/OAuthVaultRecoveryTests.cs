using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class OAuthVaultRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-oauth-recovery", Guid.NewGuid().ToString("N"));
    private static OAuthToken Token(string value = "imported-access") => new(value, "Bearer",
        DateTimeOffset.UtcNow.AddHours(1), "imported-refresh");

    [Fact]
    public async Task InitializeAsync_StorageRecoveryRetriesTheSameService()
    {
        var vault = new RecoverableReadVault();
        await using var service = new OAuthTokenRefreshService(_root, vault: vault);
        var first = () => service.InitializeAsync();
        await first.Should().ThrowAsync<InvalidOperationException>();

        await service.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5));

        service.GetToken("provider")!.AccessToken.Should().Be("imported-access");
        vault.Reads.Should().Be(2);
    }

    [Fact]
    public async Task RefreshLoop_InitializationFailureCanStopAndRestart()
    {
        var vault = new RecoverableReadVault();
        await using var service = new OAuthTokenRefreshService(_root, vault: vault);
        service.Start();
        await vault.FailedRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync();

        service.Start();
        await vault.SuccessfulRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.InitializeAsync();

        service.GetToken("provider")!.AccessToken.Should().Be("imported-access");
        await service.StopAsync();
        vault.Reads.Should().Be(2);
    }

    [Theory]
    [InlineData(false, "{\"action\":\"interrupted")]
    [InlineData(true, "{\"action\":\"interrupted")]
    [InlineData(false, "{\"action\":\"unterminated\"}")]
    [InlineData(true, "{\"action\":\"unterminated\"}")]
    public async Task AuditAppend_RecoversIncompleteTailWithoutCorruptingCommittedRecords(bool providerCredentialWrite, string tail)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("first", Token());
        var auditPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl");
        var committed = await File.ReadAllTextAsync(auditPath);
        await File.AppendAllTextAsync(auditPath, tail);

        if (providerCredentialWrite)
            await vault.SaveAsync(new ProviderCredentialSaveRequest("polygon",
                new Dictionary<string, string?> { ["ApiKey"] = "provider-secret" }));
        else
            await vault.SaveOAuthTokenAsync("next", Token("next-access"));

        var recovered = await File.ReadAllTextAsync(auditPath);
        recovered.Should().StartWith(committed).And.EndWith("\n");
        var lines = recovered.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        foreach (var line in lines)
        {
            using var parsed = JsonDocument.Parse(line);
            parsed.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        }
        var retainedTail = Directory.GetFiles(Path.GetDirectoryName(auditPath)!, "provider-credentials.audit.jsonl.partial-*");
        retainedTail.Should().ContainSingle();
        (await File.ReadAllBytesAsync(retainedTail[0])).Should().Equal(Encoding.UTF8.GetBytes(tail));
    }

    [Fact]
    public async Task ReadOAuthTokens_WriterLockHeldReadsPublishedGeneration()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("provider", Token());
        using var writer = new FileStream(vault.VaultPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var tokens = await new FileProviderCredentialStore(_root).ReadOAuthTokensAsync(timeout.Token);

        tokens["provider"].AccessToken.Should().Be("imported-access");
    }

    private sealed class RecoverableReadVault : IOAuthTokenVault
    {
        public int Reads { get; private set; }
        public TaskCompletionSource FailedRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SuccessfulRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyDictionary<string, OAuthToken>> ReadOAuthTokensAsync(CancellationToken ct = default)
        {
            if (++Reads == 1)
            {
                FailedRead.TrySetResult();
                return Task.FromException<IReadOnlyDictionary<string, OAuthToken>>(new IOException("Temporarily unavailable storage."));
            }
            SuccessfulRead.TrySetResult();
            return Task.FromResult<IReadOnlyDictionary<string, OAuthToken>>(new Dictionary<string, OAuthToken> { ["provider"] = Token() });
        }

        public Task ImportOAuthTokensAsync(IReadOnlyDictionary<string, OAuthToken> tokens, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task SaveOAuthTokenAsync(string providerName, OAuthToken? token, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
