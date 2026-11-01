using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Plaid;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.FinancialOperations.Banking;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderPlaidRuntimeCredentialTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-plaid-credential-recovery", Guid.NewGuid().ToString("N"));
    private CoreConfigStore Config => new(Path.Combine(_root, "appsettings.json"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkstationPlaid_OwnedProviderCannotUseLegacyOrOptionsCredentialsAfterTransfer(bool rollback)
    {
        var store = await SetupAsync();
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
            "owned", "plaid", "Owned Plaid connection", ExternalAccountId: "account-a"), "tenant-a", "sandbox");
        var commands = new ProviderRuntimeCredentialService(Config, store);
        var preview = await commands.GetMigrationPreviewAsync("plaid", "owned", "tenant-a");
        await commands.TransferAsync("plaid", "owned", "tenant-a", "transfer",
            preview.AuditMetadata["credentialGeneration"], "operator", false);
        await commands.BindAsync("plaid", "owned", "tenant-a");
        if (rollback)
            await commands.TransferAsync("plaid", "owned", "tenant-a", "transfer", null, "operator", true);
        var client = new Mock<IPlaidClient>(MockBehavior.Strict);
        using var services = CreateServices(new FileProviderCredentialStore(_root, _ => null), client.Object);
        var service = services.GetRequiredService<PlaidWorkstationService>();

        Func<Task> search = () => service.SearchInstitutionsAsync("bank");

        await search.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WorkstationPlaid_EntirelyLegacyCredentialsRemainUsableAndOwnershipChangesBlockExistingService()
    {
        var store = await SetupAsync();
        var client = new Mock<IPlaidClient>(MockBehavior.Strict);
        client.Setup(value => value.SearchInstitutionsAsync(
                It.Is<PlaidClientCredentials>(credentials => credentials.ClientId == "synthetic-client" && credentials.Secret == "synthetic-secret"),
                "bank", It.IsAny<IReadOnlyList<PlaidProductDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaidInstitutionSearchResult("bank", [], "synthetic-request"));
        using var services = CreateServices(store, client.Object);
        var service = services.GetRequiredService<PlaidWorkstationService>();
        (await service.SearchInstitutionsAsync("bank")).RequestId.Should().Be("synthetic-request");
        client.VerifyAll();
        client.Invocations.Clear();
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
            "owned", "plaid", "Owned Plaid connection", ExternalAccountId: "account-a"), "tenant-a", "sandbox");

        Func<Task> search = () => service.SearchInstitutionsAsync("bank");

        await search.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        client.VerifyNoOtherCalls();
    }

    private ServiceProvider CreateServices(IProviderCredentialStore store, IPlaidClient client)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Config);
        services.AddSingleton(store);
        services.AddSingleton(client);
        services.AddSingleton(new Mock<IPlaidConnectionRepository>(MockBehavior.Strict).Object);
        services.AddSingleton(new Mock<IFundAccountService>(MockBehavior.Strict).Object);
        services.AddSingleton(new Mock<IBankingService>(MockBehavior.Strict).Object);
        services.AddSingleton(PlaidOptions.Default with { ClientId = "unselected-options-client", Secret = "unselected-options-secret" });
        services.AddWorkstationSharedServices(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private async Task<FileProviderCredentialStore> SetupAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        var store = new FileProviderCredentialStore(_root, _ => null);
        await store.SaveAsync(new("plaid", new Dictionary<string, string?>
        {
            ["ClientId"] = "synthetic-client",
            ["Secret"] = "synthetic-secret"
        }, "sandbox", "operator"));
        return store;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
