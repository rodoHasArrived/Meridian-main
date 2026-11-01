using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Moq;

namespace Meridian.Tests.Application.ProviderRouting;

/// <summary>Partial configuration recovery must not reactivate provider-wide credential setup.</summary>
public sealed class ProviderSetupRuntimeBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-setup-binding", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("alpaca")]
    [InlineData("alpaca-api")]
    public async Task ConfigureLegacy_DanglingRuntimeBinding_RefusesBeforeVaultAccess(string providerId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var config = new ConfigStore(Path.Combine(_root, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: _root, ProviderConnections: new ProviderConnectionsConfig
        {
            RuntimeCredentials = [new(providerId, "tenant-a", "removed-owner")]
        }), timeout.Token);
        var before = await File.ReadAllBytesAsync(config.ConfigPath, timeout.Token);
        var vault = new Mock<IProviderCredentialStore>(MockBehavior.Strict);
        var setup = new ProviderSetupService(config, vault.Object, new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create()));

        var result = await setup.ConfigureAsync(new("alpaca", "Recovered provider", "new-key", "new-secret", null,
            ["streaming"], "paper"), timeout.Token, "operator-a");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("authorized connection");
        vault.VerifyNoOtherCalls();
        (await File.ReadAllBytesAsync(config.ConfigPath, timeout.Token)).Should().Equal(before);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
