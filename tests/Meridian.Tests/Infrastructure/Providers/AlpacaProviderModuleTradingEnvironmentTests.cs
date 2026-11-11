using System.Net;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Meridian.Core.IO;
using Meridian.Tests.Ui;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// The Alpaca module's setup probe and its registered brokerage gateway must reach the same
/// Trading API environment with the same key pair. The probe used to follow the
/// <c>useSandbox</c> setting while the gateway followed <c>ALPACA_TRADING_ENVIRONMENT</c>, so a
/// probe could report one environment reachable and the gateway then trade in the other.
/// </summary>
[Trait("Category", "Unit")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaProviderModuleTradingEnvironmentTests : IDisposable
{
    private const string Paper = "https://paper-api.alpaca.markets";
    private const string Live = "https://api.alpaca.markets";

    private static readonly string[] Variables =
    [
        AlpacaCredentialEnvironment.KeyIdName, AlpacaCredentialEnvironment.SecretKeyName,
        AlpacaCredentialEnvironment.TradingEnvironmentName, .. AlpacaCredentialEnvironment.KeyIdAliases,
        .. AlpacaCredentialEnvironment.SecretKeyAliases,
    ];

    private readonly Dictionary<string, string?> _original =
        Variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    public AlpacaProviderModuleTradingEnvironmentTests()
    {
        foreach (var name in Variables)
            Environment.SetEnvironmentVariable(name, null);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _original)
            Environment.SetEnvironmentVariable(name, value);
    }

    [Theory]
    // The variable decides; the market-data sandbox setting never does.
    [InlineData("paper", "false", Paper)]
    [InlineData("paper", null, Paper)]
    [InlineData("live", "true", Live)]
    [InlineData("live", null, Live)]
    // Nothing names an environment: paper, as order routing has always defaulted.
    [InlineData(null, "false", Paper)]
    [InlineData(null, "true", Paper)]
    [InlineData(null, null, Paper)]
    public async Task ProbeAndRegisteredGateway_UseTheSameTradingEnvironment(string? variable, string? useSandbox, string expectedHost)
    {
        Environment.SetEnvironmentVariable(AlpacaCredentialEnvironment.TradingEnvironmentName, variable);
        using var probeHandler = new RecordingHandler(HttpStatusCode.OK);
        using var probeClient = new HttpClient(probeHandler);
        var module = ConfiguredModule(useSandbox, probeClient);

        var probe = await module.ProbeConnectionAsync();
        var gatewayHost = await FirstGatewayRequestHostAsync(module);

        var probed = probeHandler.Requests.Should().ContainSingle().Subject;
        probed.Host.Should().Be(new Uri(expectedHost).Host);
        probed.KeyId.Should().Be("module-key");
        probe.ProbeDetail.Should().Contain(expectedHost == Paper ? "paper" : "live");
        gatewayHost.Should().Be(probed.Host, "the gateway must trade where the probe looked");
    }

    [Fact]
    public async Task ProbeAndRegisteredGateway_UseTheModuleKeyPair_EvenWhenTheEnvironmentHoldsAnother()
    {
        Environment.SetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName, "environment-key");
        Environment.SetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName, "environment-secret");
        using var probeHandler = new RecordingHandler(HttpStatusCode.OK);
        using var probeClient = new HttpClient(probeHandler);
        var module = ConfiguredModule(useSandbox: null, probeClient);

        await module.ProbeConnectionAsync();
        var (_, keyId) = await FirstGatewayRequestAsync(module);

        probeHandler.Requests.Should().ContainSingle().Which.KeyId.Should().Be("module-key");
        keyId.Should().Be("module-key", "the gateway is pinned to the key pair the probe validated");
    }

    [Fact]
    public void WithoutModuleCredentials_TheEnvironmentKeyPairIsUsed()
    {
        Environment.SetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName, "environment-key");
        Environment.SetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName, "environment-secret");
        Environment.SetEnvironmentVariable(AlpacaCredentialEnvironment.TradingEnvironmentName, "live");

        var credentials = new AlpacaProviderModule().ResolveTradingCredentials();

        credentials.Should().Be(new AlpacaCredentialSnapshot("environment-key", "environment-secret", "live", UseSandbox: false));
    }

    private static AlpacaProviderModule ConfiguredModule(string? useSandbox, HttpClient probeClient)
    {
        var settings = new Dictionary<string, string?>();
        if (useSandbox is not null)
            settings["useSandbox"] = useSandbox;
        var module = new AlpacaProviderModule(probeClient);
        module.Configure(new ProviderModuleContext
        {
            Credentials = new Dictionary<string, string?> { ["keyId"] = "module-key", ["secretKey"] = "module-secret" },
            Settings = settings,
        });
        return module;
    }

    private static async Task<string> FirstGatewayRequestHostAsync(AlpacaProviderModule module) =>
        (await FirstGatewayRequestAsync(module)).Host;

    /// <summary>Connects the gateway the module registers and reports where its first request went.</summary>
    private static async Task<(string Host, string? KeyId)> FirstGatewayRequestAsync(AlpacaProviderModule module)
    {
        using var handler = new RecordingHandler(HttpStatusCode.Unauthorized);
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clients);
        services.AddSingleton(Substitute.For<IAtomicFileWriter>());
        module.Register(services, new DataSourceRegistry());
        await using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<AlpacaBrokerageGateway>();

        // The stub refuses the account read, so the connection attempt ends there.
        try
        {
            await gateway.ConnectAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        var request = handler.Requests.Should().NotBeEmpty().And.Subject.First();
        return request;
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(string Host, string? KeyId)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.Host,
                request.Headers.TryGetValues("APCA-API-KEY-ID", out var values) ? values.Single() : null));
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
