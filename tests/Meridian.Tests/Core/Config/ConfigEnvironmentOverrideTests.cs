using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

// MDC_* variables are process-global, so these must not run beside ConfigTemplateGeneratorTests,
// which sets the same MDC_DATASOURCE and MDC_SYMBOLS values.
[Collection("Sequential")]
public sealed class ConfigEnvironmentOverrideTests
{
    [Fact]
    public void ApplyOverrides_MapsIbSocketAndClientPortalSeparately()
    {
        using var hostVar = new EnvironmentVariableScope("MDC_IB_HOST", "10.0.0.5");
        using var portVar = new EnvironmentVariableScope("MDC_IB_PORT", "7497");
        using var clientIdVar = new EnvironmentVariableScope("MDC_IB_CLIENT_ID", "17");
        using var paperVar = new EnvironmentVariableScope("MDC_IB_PAPER", "true");
        using var portalEnabledVar = new EnvironmentVariableScope("MDC_IB_CLIENT_PORTAL_ENABLED", "true");
        using var portalUrlVar = new EnvironmentVariableScope("MDC_IB_CLIENT_PORTAL_BASE_URL", "https://localhost:5000");
        using var portalCertVar = new EnvironmentVariableScope("MDC_IB_CLIENT_PORTAL_ALLOW_SELF_SIGNED", "true");

        var sut = new ConfigEnvironmentOverride();
        var result = sut.ApplyOverrides(new AppConfig());

        result.IB.Should().NotBeNull();
        result.IB!.Host.Should().Be("10.0.0.5");
        result.IB.Port.Should().Be(7497);
        result.IB.ClientId.Should().Be(17);
        result.IB.UsePaperTrading.Should().BeTrue();

        result.IBClientPortal.Should().NotBeNull();
        result.IBClientPortal!.Enabled.Should().BeTrue();
        result.IBClientPortal.BaseUrl.Should().Be("https://localhost:5000");
        result.IBClientPortal.AllowSelfSignedCertificates.Should().BeTrue();
    }

    [Fact]
    public void ApplyOverrides_ValidDataSource_IsApplied()
    {
        using var dataSourceVar = new EnvironmentVariableScope("MDC_DATASOURCE", "alpaca");

        var sut = new ConfigEnvironmentOverride();
        var result = sut.ApplyOverrides(new AppConfig());

        result.DataSource.Should().Be(DataSourceKind.Alpaca);
    }

    [Fact]
    public void ApplyOverrides_UnknownDataSource_FailsClosed()
    {
        // A typo must not silently route the operator to another provider's data feed.
        using var dataSourceVar = new EnvironmentVariableScope("MDC_DATASOURCE", "alpacca");

        var sut = new ConfigEnvironmentOverride();
        var act = () => sut.ApplyOverrides(new AppConfig());

        act.Should().Throw<Meridian.Core.Exceptions.ConfigurationException>()
            .WithMessage("*alpacca*")
            .WithMessage("*MDC_DATASOURCE*");
    }

    [Fact]
    public void ApplyOverrides_Symbols_AreSplitAndUppercased()
    {
        // MDC_SYMBOLS was advertised by the Docker template but had no mapping, so setting it
        // did nothing. Uppercasing matters because SymbolConfigValidator matches ^[A-Z0-9\-\.\/]+$.
        using var symbolsVar = new EnvironmentVariableScope("MDC_SYMBOLS", "spy, qqq ,BRK.B");

        var sut = new ConfigEnvironmentOverride();
        var result = sut.ApplyOverrides(new AppConfig());

        result.Symbols.Should().NotBeNull();
        result.Symbols!.Select(s => s.Symbol).Should().Equal("SPY", "QQQ", "BRK.B");

        // Depth stays off when nothing is configured to inherit from: the environment cannot know
        // whether the selected provider advertises Level2Book, and SubscriptionOrchestrator leaks
        // a registration and an ownership lease when the client answers -1.
        result.Symbols.Should().OnlyContain(s => !s.SubscribeDepth && s.SubscribeTrades);
    }

    [Fact]
    public void ApplyOverrides_Symbols_DeduplicatesCaseInsensitively()
    {
        // Uppercasing merges spellings the operator wrote as distinct, and per-symbol validation
        // accepts both copies. SubscriptionOrchestrator.ApplyAsync then keys its desired set with
        // ToDictionary(..., StringComparer.OrdinalIgnoreCase), which throws on the duplicate and
        // aborts collector startup — so the repeat has to be dropped here, before orchestration.
        using var symbolsVar = new EnvironmentVariableScope("MDC_SYMBOLS", "SPY,spy, QQQ ,Spy,qqq");

        var sut = new ConfigEnvironmentOverride();
        var result = sut.ApplyOverrides(new AppConfig());

        result.Symbols.Should().NotBeNull();
        result.Symbols!.Select(s => s.Symbol).Should().Equal("SPY", "QQQ");

        // The same key the orchestrator builds must not throw.
        var act = () => result.Symbols!.ToDictionary(s => s.Symbol.Trim(), s => s, StringComparer.OrdinalIgnoreCase);
        act.Should().NotThrow();
    }

    [Fact]
    public void ApplyOverrides_Symbols_InheritCollectionTogglesWithoutContractIdentity()
    {
        // MDC_SYMBOLS changes which symbols are collected, not how. A configuration that
        // deliberately enabled depth keeps it; one that disabled it — like the generated Docker
        // template — is not silently re-enabled by the record defaults.
        using var symbolsVar = new EnvironmentVariableScope("MDC_SYMBOLS", "IBM,GE");

        var configured = new AppConfig(Symbols:
        [
            new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: 25, LocalSymbol: "PCG PRA", ConId: 12345)
        ]);

        var result = new ConfigEnvironmentOverride().ApplyOverrides(configured);

        result.Symbols!.Select(s => s.Symbol).Should().Equal("IBM", "GE");
        result.Symbols.Should().OnlyContain(s => s.SubscribeDepth && s.DepthLevels == 25);

        // Contract identity is per-symbol and must not be carried across: a LocalSymbol or ConId
        // belonging to SPY would describe the wrong instrument entirely on IBM and GE.
        result.Symbols.Should().OnlyContain(s => s.LocalSymbol == null && s.ConId == null);
    }

    [Fact]
    public void ApplyOverrides_SymbolListWithNoSymbols_FailsClosed()
    {
        // Same contract as MDC_DATASOURCE: a variable the operator deliberately set must not
        // resolve to "subscribe to nothing" in silence.
        using var symbolsVar = new EnvironmentVariableScope("MDC_SYMBOLS", " , , ");

        var sut = new ConfigEnvironmentOverride();
        var act = () => sut.ApplyOverrides(new AppConfig());

        act.Should().Throw<Meridian.Core.Exceptions.ConfigurationException>()
            .WithMessage("*MDC_SYMBOLS*");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
