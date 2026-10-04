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
    private string LegacyPath => Path.Combine(_root, ".mdc", "oauth_tokens.json");
    private static OAuthToken Token(string value = "imported-access") => new(value, "Bearer",
        DateTimeOffset.UtcNow.AddHours(1), "imported-refresh");

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    [InlineData(false, "corrupt")]
    [InlineData(false, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    [InlineData(true, "corrupt")]
    [InlineData(true, null)]
    public async Task LegacyImport_RetainsImportedRecoveryGenerationBeforeErasingSource(bool existingVault, string? corruption)
    {
        var vault = new FileProviderCredentialStore(_root);
        if (existingVault)
            await vault.SaveOAuthTokenAsync("existing", Token("retained-access"));
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        await File.WriteAllTextAsync(LegacyPath, JsonSerializer.Serialize(new Dictionary<string, OAuthToken>
        {
            ["imported"] = Token()
        }));

        await using (var migrated = new OAuthTokenRefreshService(_root))
            await migrated.InitializeAsync();
        File.Exists(LegacyPath).Should().BeFalse();
        File.Exists(vault.VaultPath + ".bak").Should().BeTrue();
        if (corruption is null)
            File.Delete(vault.VaultPath);
        else
            await File.WriteAllTextAsync(vault.VaultPath, corruption);

        await using var recovered = new OAuthTokenRefreshService(_root);
        await recovered.InitializeAsync();
        recovered.GetToken("imported")!.AccessToken.Should().Be("imported-access");
        recovered.GetToken("imported")!.RefreshToken.Should().Be("imported-refresh");
        if (existingVault)
            recovered.GetToken("existing")!.AccessToken.Should().Be("retained-access");
    }

    [Fact]
    public async Task ConcurrentLegacyInitializers_CompleteCleanupAndReadRetainedTokens()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        await File.WriteAllTextAsync(LegacyPath, JsonSerializer.Serialize(new Dictionary<string, OAuthToken>
        {
            ["provider"] = Token()
        }));
        var services = Enumerable.Range(0, 8).Select(_ => new OAuthTokenRefreshService(_root)).ToArray();
        try
        {
            await Task.WhenAll(services.Select(service => service.InitializeAsync())).WaitAsync(TimeSpan.FromSeconds(15));
            foreach (var service in services)
                service.GetToken("provider")!.AccessToken.Should().Be("imported-access");
            File.Exists(LegacyPath).Should().BeFalse();
            File.Exists(LegacyPath + ".migrated").Should().BeFalse();
        }
        finally
        {
            foreach (var service in services)
                await service.DisposeAsync();
        }
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderLegacyImport_RetainsRecoveryBeforeErasingSource(bool existingVault)
    {
        var vault = new FileProviderCredentialStore(_root);
        if (existingVault)
            await vault.SaveOAuthTokenAsync("existing", Token("retained-access"));
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "provider-credentials.json");
        await File.WriteAllTextAsync(path, """{"polygon":{"ApiKey":"provider-secret"}}""");
        var adapter = new Meridian.Ui.Shared.Services.ProviderCredentialStore(_root);
        (await adapter.GetCredentialsAsync("polygon"))["ApiKey"].Should().Be("provider-secret");
        File.Exists(path).Should().BeFalse();
        await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");

        (await new FileProviderCredentialStore(_root).ReadForProviderAsync("polygon"))!.Get("ApiKey").Should().Be("provider-secret");
    }

    [Fact]
    public async Task ProviderLegacyImport_ConcurrentAdaptersFinishCleanup()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "provider-credentials.json");
        await File.WriteAllTextAsync(path, """{"polygon":{"ApiKey":"provider-secret"}}""");
        var adapters = Enumerable.Range(0, 8).Select(_ => new Meridian.Ui.Shared.Services.ProviderCredentialStore(_root));

        var results = await Task.WhenAll(adapters.Select(adapter => adapter.GetCredentialsAsync("polygon")))
            .WaitAsync(TimeSpan.FromSeconds(15));

        foreach (var result in results)
            result["ApiKey"].Should().Be("provider-secret");
        File.Exists(path).Should().BeFalse();
        File.Exists(path + ".migrated").Should().BeFalse();
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
