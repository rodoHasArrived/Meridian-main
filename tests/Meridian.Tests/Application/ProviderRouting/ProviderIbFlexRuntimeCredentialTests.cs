using System.Text;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderIbFlexRuntimeCredentialTests : IDisposable
{
    private const string SampleXml = """
        <FlexQueryResponse>
          <FlexStatements count="1">
            <FlexStatement accountId="U1234567" fromDate="20260601" toDate="20260630">
              <Trades>
                <Trade accountId="U1234567" symbol="AAPL" tradeDate="20260615" quantity="1" tradePrice="100" netCash="-100" currency="USD" buySell="BUY" tradeID="1" assetCategory="STK" />
              </Trades>
            </FlexStatement>
          </FlexStatements>
        </FlexQueryResponse>
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ibflex-credential-recovery", Guid.NewGuid().ToString("N"));
    private CoreConfigStore Config => new(Path.Combine(_root, "appsettings.json"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HostedIbFlex_OwnedProviderCannotFetchWithLegacyCredentialsAfterTransfer(bool workstation, bool rollback)
    {
        var store = await SetupAsync();
        await CreateOwnershipAsync();
        var commands = new ProviderRuntimeCredentialService(Config, store);
        var preview = await commands.GetMigrationPreviewAsync("ib-flex", "owned", "tenant-a");
        await commands.TransferAsync("ib-flex", "owned", "tenant-a", "transfer",
            preview.AuditMetadata["credentialGeneration"], "operator", false);
        await commands.BindAsync("ib-flex", "owned", "tenant-a");
        if (rollback)
            await commands.TransferAsync("ib-flex", "owned", "tenant-a", "transfer", null, "operator", true);
        var client = new Mock<IIbFlexWebServiceClient>(MockBehavior.Strict);
        using var services = CreateServices(new FileProviderCredentialStore(_root, _ => null), client.Object, workstation);
        var connector = ResolveConnector(services);

        Func<Task> fetch = () => connector.FetchAsync(new("ib-flex", "U1234567"));

        await fetch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        client.VerifyNoOtherCalls();
        connector.Descriptor.SupportsFileImport.Should().BeTrue();
        (await connector.ParseAsync(SampleDocument())).HasErrors.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedIbFlex_EntirelyLegacyFetchWorksAndExistingConnectorRechecksNewOwnership(bool workstation)
    {
        var store = await SetupAsync();
        var client = new Mock<IIbFlexWebServiceClient>(MockBehavior.Strict);
        client.Setup(value => value.FetchStatementAsync("synthetic-token", "1234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(SampleXml)));
        using var services = CreateServices(store, client.Object, workstation);
        var connector = ResolveConnector(services);

        var document = await connector.FetchAsync(new("ib-flex", "U1234567"));

        connector.Descriptor.SupportsRemoteFetch.Should().BeTrue();
        document.ExternalAccountId.Should().Be("U1234567");
        (await connector.ParseAsync(document)).Records.Should().ContainSingle();
        client.VerifyAll();
        client.Invocations.Clear();
        await CreateOwnershipAsync();

        Func<Task> fetch = () => connector.FetchAsync(new("ib-flex", "U1234567"));

        await fetch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReconciliationWithoutRuntimeGuard_DisablesRemoteFetchButKeepsFileParsing()
    {
        var store = await SetupAsync();
        var client = new Mock<IIbFlexWebServiceClient>(MockBehavior.Strict);
        using var services = CreateServices(store, client.Object, workstation: null);
        var connector = ResolveConnector(services);

        connector.Descriptor.SupportsRemoteFetch.Should().BeFalse();
        connector.Descriptor.SupportsFileImport.Should().BeTrue();
        var parsed = await connector.ParseAsync(SampleDocument());
        parsed.HasErrors.Should().BeFalse();
        parsed.Records.Should().ContainSingle();
        Func<Task> fetch = () => connector.FetchAsync(new("ib-flex", "U1234567"));
        await fetch.Should().ThrowAsync<NotSupportedException>();
        client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HostedIbFlex_MissingVaultOrConfigurationKeepsFileParsing(bool workstation, bool missingVault)
    {
        var store = await SetupAsync();
        var client = new Mock<IIbFlexWebServiceClient>(MockBehavior.Strict);
        using var services = CreateServices(missingVault ? null : store, client.Object, workstation,
            registerConfiguration: missingVault);
        var connector = ResolveConnector(services);

        connector.Descriptor.SupportsRemoteFetch.Should().BeFalse();
        connector.Descriptor.SupportsFileImport.Should().BeTrue();
        (await connector.ParseAsync(SampleDocument())).Records.Should().ContainSingle();
        Func<Task> fetch = () => connector.FetchAsync(new("ib-flex", "U1234567"));
        await fetch.Should().ThrowAsync<NotSupportedException>();
        client.VerifyNoOtherCalls();
    }

    private ServiceProvider CreateServices(IProviderCredentialStore? store, IIbFlexWebServiceClient client, bool? workstation,
        bool registerConfiguration = true)
    {
        var services = new ServiceCollection();
        if (registerConfiguration)
            services.AddSingleton(Config);
        if (store is not null)
            services.AddSingleton(store);
        services.AddSingleton(client);
        if (workstation == true)
            services.AddWorkstationSharedServices(new ConfigurationBuilder().Build());
        else if (workstation == false)
            new ProviderRoutingFeatureRegistration().Register(services, new CompositionOptions { ConfigPath = Config.ConfigPath });
        services.AddStatementReconciliationServices(_root);
        return services.BuildServiceProvider();
    }

    private static IbFlexStatementConnector ResolveConnector(ServiceProvider services) =>
        services.GetServices<IStatementConnector>().OfType<IbFlexStatementConnector>().Single();

    private static StatementSourceDocument SampleDocument() =>
        new("synthetic-flex.xml", Encoding.UTF8.GetBytes(SampleXml), ExternalAccountId: "U1234567");

    private async Task CreateOwnershipAsync()
    {
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
            "owned", "ib-flex", "Owned IB Flex connection", ExternalAccountId: "U1234567"), "tenant-a", "production");
    }

    private async Task<FileProviderCredentialStore> SetupAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        var store = new FileProviderCredentialStore(_root, _ => null);
        await store.SaveAsync(new("ib-flex", new Dictionary<string, string?>
        {
            ["Token"] = "synthetic-token",
            ["QueryId"] = "1234"
        }, "production", "operator"));
        return store;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
