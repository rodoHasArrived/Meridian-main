using System.Linq;
using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Application.ProviderRouting;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class DesktopConfigurationRecoveryServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-config-recovery-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureReadableConfiguration_WhenMissing_CreatesConfigAndLastKnownGood()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        var service = new DesktopConfigurationRecoveryService(configPath);

        var result = service.EnsureReadableConfiguration();

        result.Outcome.Should().Be(ConfigurationProvisioningResult.CreatedDefault);
        File.Exists(configPath).Should().BeTrue();
        File.Exists(service.LastKnownGoodPath).Should().BeTrue();
        ReadConfiguration(configPath).Should().NotBeNull();
    }

    [Fact]
    public void EnsureReadableConfiguration_WhenCurrentFileIsCorrupt_RestoresLastKnownGoodAndRetainsEvidence()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        var timestamp = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var service = new DesktopConfigurationRecoveryService(configPath, () => timestamp);
        var expected = AppConfigDefaults.CreateDefaultAppConfig();
        expected.DataSource = "RecoveredProvider";
        var validJson = JsonSerializer.Serialize(expected, DesktopJsonOptions.PrettyPrint);
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(service.LastKnownGoodPath, validJson);
        File.WriteAllText(configPath, "{ not-json");

        var result = service.EnsureReadableConfiguration();

        result.Outcome.Should().Be(ConfigurationProvisioningResult.RestoredLastKnownGood);
        ReadConfiguration(configPath).DataSource.Should().Be("RecoveredProvider");
        result.InvalidConfigurationPath.Should().NotBeNull();
        File.ReadAllText(result.InvalidConfigurationPath!).Should().Be("{ not-json");
        result.RecoveryReceiptPath.Should().NotBeNull();
        File.Exists(result.RecoveryReceiptPath!).Should().BeTrue();
        result.RetainedArtifacts.Should().HaveCount(2);
    }

    [Fact]
    public void EnsureReadableConfiguration_WhenNoValidRecoveryPoint_RestoresDefaultsAndRetainsInvalidFile()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(configPath, string.Empty);
        var service = new DesktopConfigurationRecoveryService(configPath);

        var result = service.EnsureReadableConfiguration();

        result.Outcome.Should().Be(ConfigurationProvisioningResult.RepairedInvalid);
        ReadConfiguration(configPath).DataSource.Should().Be("NoOp");
        File.Exists(result.InvalidConfigurationPath!).Should().BeTrue();
        File.Exists(result.RecoveryReceiptPath!).Should().BeTrue();
    }

    [Fact]
    public async Task PersistValidConfigurationAsync_IsRejectedBeforeReplacingCurrentFile()
    {
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        var service = new DesktopConfigurationRecoveryService(configPath);
        service.EnsureReadableConfiguration();
        var original = File.ReadAllText(configPath);

        var act = () => service.PersistValidConfigurationAsync("[]");

        await act.Should().ThrowAsync<JsonException>();
        File.ReadAllText(configPath).Should().Be(original);
    }

    [Theory]
    [InlineData("{\"Alpaca\":{\"KeyId\":\"synthetic-key\",\"SecretKey\":\"synthetic-secret\"}}")]
    [InlineData("{\"Backfill\":{\"Providers\":{\"Polygon\":{\"ApiKey\":\"synthetic-secret\"}}}}")]
    [InlineData("{\"ProviderModules\":{\"Modules\":{\"alpaca\":{\"Settings\":{\"SecretKey\":\"synthetic-secret\"}}}}}")]
    public async Task ProtectedPersistence_RefusesRawProviderSecretsBeforeEitherGenerationChanges(string unsafeJson)
    {
        var service = ProtectedService();
        service.EnsureReadableConfiguration();
        var primary = File.ReadAllBytes(service.ConfigPath);
        var recovery = File.ReadAllBytes(service.LastKnownGoodPath);
        var act = () => service.PersistValidConfigurationAsync(unsafeJson);

        var refusal = await act.Should().ThrowAsync<ProviderConfigurationCredentialException>();

        refusal.Which.Message.Should().NotContain("synthetic-secret");
        File.ReadAllBytes(service.ConfigPath).Should().Equal(primary);
        File.ReadAllBytes(service.LastKnownGoodPath).Should().Equal(recovery);
        Directory.GetFiles(_tempRoot).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedStartup_RefusesLegacySecretsBeforeBackupOrRecovery_AndRequiresBothGenerationsPurged(bool unsafeRecoveryCopy)
    {
        var service = ProtectedService();
        service.EnsureReadableConfiguration();
        var cleanJson = File.ReadAllText(service.ConfigPath);
        var unsafePath = unsafeRecoveryCopy ? service.LastKnownGoodPath : service.ConfigPath;
        const string legacy = "{\"Alpaca\":{\"SecretKey\":\"synthetic-legacy-secret\"}}";
        File.WriteAllText(service.ConfigPath, legacy);
        File.WriteAllText(service.LastKnownGoodPath, legacy);
        var before = Directory.GetFiles(_tempRoot).ToDictionary(path => path, File.ReadAllBytes);
        Action startup = () => service.EnsureReadableConfiguration();

        startup.Should().Throw<ProviderConfigurationCredentialException>();
        var overwriteWithClean = () => service.PersistValidConfigurationAsync(cleanJson);
        await overwriteWithClean.Should().ThrowAsync<ProviderConfigurationCredentialException>();
        foreach (var file in before)
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        Directory.GetFiles(_tempRoot).Should().BeEquivalentTo(before.Keys);

        // The fixture models the operator's reviewed purge; runtime rejection never deletes or
        // silently replaces legacy secrets. Once both retained generations are clean, retry works.
        File.WriteAllText(unsafePath, cleanJson);
        startup.Should().Throw<ProviderConfigurationCredentialException>("purging one generation cannot authorize the other");
        var otherPath = unsafeRecoveryCopy ? service.ConfigPath : service.LastKnownGoodPath;
        File.WriteAllText(otherPath, cleanJson);
        service.EnsureReadableConfiguration().Outcome.Should().Be(ConfigurationProvisioningResult.AlreadyValid);
        File.ReadAllText(service.ConfigPath).Should().Be(cleanJson);
        File.ReadAllText(service.LastKnownGoodPath).Should().Be(cleanJson);
        Directory.GetFiles(_tempRoot).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedStartup_UnparseableSourceIsNotCopiedToInvalidOrLastKnownGoodArtifacts(bool malformedRecoveryCopy)
    {
        var service = ProtectedService();
        service.EnsureReadableConfiguration();
        var cleanJson = File.ReadAllText(service.ConfigPath);
        var unsafePath = malformedRecoveryCopy ? service.LastKnownGoodPath : service.ConfigPath;
        File.WriteAllText(unsafePath, "{\"Alpaca\":{\"SecretKey\":\"synthetic-secret\"");
        var primary = File.ReadAllBytes(service.ConfigPath);
        var recovery = File.ReadAllBytes(service.LastKnownGoodPath);
        Action startup = () => service.EnsureReadableConfiguration();

        startup.Should().Throw<ProviderConfigurationCredentialException>();
        File.ReadAllBytes(service.ConfigPath).Should().Equal(primary);
        File.ReadAllBytes(service.LastKnownGoodPath).Should().Equal(recovery);
        Directory.GetFiles(_tempRoot).Should().HaveCount(2);
        File.Delete(unsafePath); // Reviewed purge of the unsafe generation, after preserving/migrating any secrets.
        var recovered = service.EnsureReadableConfiguration();
        recovered.Outcome.Should().Be(malformedRecoveryCopy
            ? ConfigurationProvisioningResult.AlreadyValid : ConfigurationProvisioningResult.RestoredLastKnownGood);
        File.ReadAllText(service.ConfigPath).Should().Be(cleanJson);
        File.ReadAllText(service.LastKnownGoodPath).Should().Be(cleanJson);
    }

    [Fact]
    public void ProtectedStartup_MissingPrimaryRestoresExactOwnedConfigurationInsteadOfOverwritingItWithDefaults()
    {
        var service = ProtectedService();
        Directory.CreateDirectory(_tempRoot);
        const string retained = "{\"DataRoot\":\"retained-owned-data\",\"ProviderConnections\":{\"Connections\":[{\"ConnectionId\":\"paper-a\",\"ProviderFamilyId\":\"alpaca\",\"TenantId\":\"tenant-a\",\"ExternalAccountId\":\"account-a\",\"CredentialEnvironment\":\"paper\"}]}}";
        File.WriteAllText(service.LastKnownGoodPath, retained);

        var result = service.EnsureReadableConfiguration();

        result.Outcome.Should().Be(ConfigurationProvisioningResult.RestoredLastKnownGood);
        result.InvalidConfigurationPath.Should().BeNull();
        result.OperatorMessage.Should().NotContain("invalid file");
        File.ReadAllText(service.ConfigPath).Should().Be(retained);
        File.ReadAllText(service.LastKnownGoodPath).Should().Be(retained);
        Directory.GetFiles(_tempRoot).Should().HaveCount(2);
    }

    private DesktopConfigurationRecoveryService ProtectedService()
        => new(Path.Combine(_tempRoot, "appsettings.json"), ownershipPolicy: new ProviderCredentialOwnershipPolicy(true));

    private static AppConfigDto ReadConfiguration(string path)
        => JsonSerializer.Deserialize<AppConfigDto>(File.ReadAllText(path), DesktopJsonOptions.PrettyPrint)!;

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
