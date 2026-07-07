using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Moq;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Tests the canonical resolution spine: the registry answers first, inner resolutions
/// are learned back into the registry, and misses stay honest (null, not guesses).
/// </summary>
public sealed class CanonicalRegistrySymbolResolverTests
{
    [Fact]
    public async Task MapSymbolAsync_RegistryHit_AnswersWithoutInnerResolver()
    {
        var registry = new Mock<ICanonicalSymbolRegistry>();
        registry.Setup(r => r.TryResolveWithProvider("aapl.us", "stooq")).Returns("AAPL");
        var inner = new Mock<ISymbolResolver>(MockBehavior.Strict);
        var resolver = new CanonicalRegistrySymbolResolver(registry.Object, inner.Object);

        var mapped = await resolver.MapSymbolAsync("aapl.us", "stooq", "polygon");

        mapped.Should().Be("AAPL");
        inner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_RegistryMiss_DelegatesAndLearnsIntoRegistry()
    {
        var registry = new Mock<ICanonicalSymbolRegistry>();
        registry.Setup(r => r.GetDefinition(It.IsAny<string>())).Returns((CanonicalSymbolDefinition?)null);
        CanonicalSymbolDefinition? learned = null;
        registry.Setup(r => r.RegisterAsync(It.IsAny<CanonicalSymbolDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<CanonicalSymbolDefinition, CancellationToken>((definition, _) => learned = definition)
            .Returns(Task.CompletedTask);

        var inner = new Mock<ISymbolResolver>();
        inner.Setup(i => i.ResolveAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SymbolResolution("AAPL", Figi: "BBG000B9XRY4", Isin: "US0378331005", Exchange: "NASDAQ", Currency: "USD"));
        var resolver = new CanonicalRegistrySymbolResolver(registry.Object, inner.Object);

        var resolution = await resolver.ResolveAsync("AAPL");

        resolution.Should().NotBeNull();
        learned.Should().NotBeNull("inner resolutions must converge back onto the canonical registry");
        learned!.Canonical.Should().Be("AAPL");
        learned.Figi.Should().Be("BBG000B9XRY4");
        learned.Isin.Should().Be("US0378331005");
    }

    [Fact]
    public async Task ResolveAsync_RegistryHit_ReturnsDefinitionWithoutInner()
    {
        var registry = new Mock<ICanonicalSymbolRegistry>();
        registry.Setup(r => r.GetDefinition("AAPL")).Returns(new CanonicalSymbolDefinition
        {
            Canonical = "AAPL",
            DisplayName = "Apple Inc.",
            Figi = "BBG000B9XRY4",
            Exchange = "NASDAQ",
            Currency = "USD"
        });
        var inner = new Mock<ISymbolResolver>(MockBehavior.Strict);
        var resolver = new CanonicalRegistrySymbolResolver(registry.Object, inner.Object);

        var resolution = await resolver.ResolveAsync("AAPL");

        resolution.Should().NotBeNull();
        resolution!.Ticker.Should().Be("AAPL");
        resolution.Figi.Should().Be("BBG000B9XRY4");
        inner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MapSymbolAsync_MissWithoutInner_ReturnsNullInsteadOfGuessing()
    {
        var registry = new Mock<ICanonicalSymbolRegistry>();
        var resolver = new CanonicalRegistrySymbolResolver(registry.Object);

        var mapped = await resolver.MapSymbolAsync("UNKNOWN", "stooq", "polygon");

        mapped.Should().BeNull();
    }
}
