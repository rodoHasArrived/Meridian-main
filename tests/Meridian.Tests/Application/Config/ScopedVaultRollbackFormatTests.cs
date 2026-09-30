using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

/// <summary>
/// Binaries that predate scoped ownership ignore the envelope version, accept only the two legacy
/// protection tags, and would rewrite a readable vault without Scope or scoped OAuth records.
/// These tests pin the rollback guard: scoped state makes every surviving generation unreadable
/// to them, while vaults without scoped state keep the legacy format.
/// </summary>
public sealed class ScopedVaultRollbackFormatTests : IDisposable
{
    private static readonly string[] LegacyProtectionTags = ["dpapi-current-user", "local-aes-gcm"];
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "connection-a", "account-a", "paper");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-scoped-rollback", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProviderWideVault_KeepsTheLegacyFormatOlderBinariesCanOpen()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Request("first"));
        await vault.SaveAsync(Request("second"));

        ReadEnvelope(vault.VaultPath).Should().Match<Envelope>(e => e.Version == 1 && LegacyProtectionTags.Contains(e.Protection));
        ReadEnvelope(BackupPath(vault)).Should().Match<Envelope>(e => e.Version == 1 && LegacyProtectionTags.Contains(e.Protection));
    }

    [Fact]
    public async Task ScopedProviderSave_LeavesNoGenerationAnOlderBinaryCanRewrite()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Request("unassigned"));
        await vault.SaveScopedAsync(Request("owned"), Owner);

        AssertUnreadableByOlderBinaries(vault.VaultPath);
        AssertUnreadableByOlderBinaries(BackupPath(vault));

        var reopened = new FileProviderCredentialStore(_root);
        (await reopened.ReadScopedAsync("alpaca", Owner))!.Get("KeyId").Should().Be("owned");
        (await reopened.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("unassigned");

        File.Delete(vault.VaultPath);
        (await new FileProviderCredentialStore(_root).ReadForProviderAsync("alpaca"))!.Get("KeyId")
            .Should().Be("unassigned", "the re-protected backup still holds the unchanged previous generation");
    }

    [Fact]
    public async Task ScopedOAuthSave_LeavesNoGenerationAnOlderBinaryCanRewrite()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("provider", Token("unassigned"));
        await vault.SaveScopedOAuthTokenAsync("provider", Token("owned"), Owner);

        AssertUnreadableByOlderBinaries(vault.VaultPath);
        AssertUnreadableByOlderBinaries(BackupPath(vault));
        (await new FileProviderCredentialStore(_root).ReadScopedOAuthTokensAsync(Owner))
            .Should().ContainSingle().Which.Value.AccessToken.Should().Be("owned");
    }

    [Fact]
    public async Task ScopedSaveAfterPrimaryCorruption_UpgradesTheLegacyBackupItFellBackTo()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Request("first"));
        await vault.SaveAsync(Request("second"));
        ReadEnvelope(BackupPath(vault)).Protection.Should().BeOneOf(LegacyProtectionTags);
        await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");

        await vault.SaveScopedAsync(Request("owned"), Owner);

        AssertUnreadableByOlderBinaries(vault.VaultPath);
        AssertUnreadableByOlderBinaries(BackupPath(vault));
        File.Delete(vault.VaultPath);
        (await new FileProviderCredentialStore(_root).ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("first");
    }

    [Fact]
    public async Task RemovingAllScopedState_RestoresTheLegacyFormat()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Request("unassigned"));
        await vault.SaveScopedAsync(Request("owned"), Owner);

        await vault.DeleteScopedAsync("alpaca", Owner, "operator");

        ReadEnvelope(vault.VaultPath).Should().Match<Envelope>(e => e.Version == 1 && LegacyProtectionTags.Contains(e.Protection));
        (await new FileProviderCredentialStore(_root).ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("unassigned");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VaultFromANewerFormatVersion_IsRefusedInsteadOfRewritten(bool keepReadableBackup)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveScopedAsync(Request("first"), Owner);
        await vault.SaveScopedAsync(Request("owned"), Owner);
        if (!keepReadableBackup)
            File.Delete(BackupPath(vault));
        var json = await File.ReadAllTextAsync(vault.VaultPath);
        await File.WriteAllTextAsync(vault.VaultPath, json.Replace("\"version\":2", "\"version\":3", StringComparison.Ordinal));

        var save = () => new FileProviderCredentialStore(_root).SaveAsync(Request("rewrite"));

        await save.Should().ThrowAsync<NotSupportedException>().WithMessage("*newer Meridian release*");
        (await File.ReadAllTextAsync(vault.VaultPath)).Should().Contain("\"version\":3",
            "a newer primary must not be replaced from an older backup this release can read");
    }

    private static void AssertUnreadableByOlderBinaries(string path)
    {
        var envelope = ReadEnvelope(path);
        envelope.Version.Should().Be(2);
        LegacyProtectionTags.Should().NotContain(envelope.Protection);
    }

    private static string BackupPath(FileProviderCredentialStore vault) => vault.VaultPath + ".bak";

    private static Envelope ReadEnvelope(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new Envelope(document.RootElement.GetProperty("version").GetInt32(), document.RootElement.GetProperty("protection").GetString()!);
    }

    private static OAuthToken Token(string value) => new(value, "Bearer", DateTimeOffset.UtcNow.AddHours(1), "refresh-" + value);

    private static ProviderCredentialSaveRequest Request(string value) => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = value, ["SecretKey"] = "secret-" + value }, "paper", "operator");

    private sealed record Envelope(int Version, string Protection);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
