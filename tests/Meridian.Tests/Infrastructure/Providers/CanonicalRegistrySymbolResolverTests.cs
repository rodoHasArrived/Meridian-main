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

    [Fact]
    public async Task MapSymbolAsync_RegistryHit_ReturnsTargetProviderSymbol()
    {
        var registry = new Mock<ICanonicalSymbolRegistry>();
        registry.Setup(r => r.TryResolveWithProvider("BRK-B", "yahoo")).Returns("BRK.B");
        registry.Setup(r => r.GetProviderSymbol("BRK.B", "stooq")).Returns("brk-b.us");
        var inner = new Mock<ISymbolResolver>(MockBehavior.Strict);
        using var resolver = new CanonicalRegistrySymbolResolver(registry.Object, inner.Object);

        var mapped = await resolver.MapSymbolAsync("BRK-B", "yahoo", "stooq");

        mapped.Should().Be("brk-b.us");
        inner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_InnerProviderSymbols_AreLearnedWithProviderScope()
    {
        var registry = new Mock<ICanonicalSymbolRegistry>();
        registry.Setup(r => r.GetDefinition("BRK.B")).Returns((CanonicalSymbolDefinition?)null);
        CanonicalSymbolDefinition? learned = null;
        registry.Setup(r => r.RegisterAsync(It.IsAny<CanonicalSymbolDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<CanonicalSymbolDefinition, CancellationToken>((definition, _) => learned = definition)
            .Returns(Task.CompletedTask);
        var resolution = new SymbolResolution("BRK.B");
        resolution.ProviderSymbols["yahoo"] = "BRK-B";
        resolution.ProviderSymbols["stooq"] = "brk-b.us";
        var inner = new Mock<ISymbolResolver>();
        inner.Setup(r => r.ResolveAsync("BRK.B", null, It.IsAny<CancellationToken>())).ReturnsAsync(resolution);
        using var resolver = new CanonicalRegistrySymbolResolver(registry.Object, inner.Object);

        await resolver.ResolveAsync("BRK.B");

        learned.Should().NotBeNull();
        learned!.ProviderSymbols["yahoo"].Symbol.Should().Be("BRK-B");
        learned.ProviderSymbols["yahoo"].Source.Should().Be(SymbolMappingSources.OpenFigi);
        learned.ProviderSymbols["stooq"].Symbol.Should().Be("brk-b.us");
    }

    [Fact]
    public async Task MapSymbolAsync_CompareMode_ReportsMismatchAndReturnsLegacyResult()
    {
        var securityId = Guid.NewGuid();
        var registry = new Mock<ICanonicalSymbolRegistry>();
        registry.Setup(r => r.TryResolveWithProvider("BRK-B", "yahoo")).Returns("BRK.B");
        registry.Setup(r => r.GetProviderSymbol("BRK.B", "stooq")).Returns("brk-b.us");
        registry.Setup(r => r.GetDefinition("BRK-B")).Returns(new CanonicalSymbolDefinition
        {
            Canonical = "BRK.B",
            SecurityId = securityId
        });
        var inner = new Mock<ISymbolResolver>();
        inner.Setup(r => r.MapSymbolAsync("BRK-B", "yahoo", "stooq", It.IsAny<CancellationToken>()))
            .ReturnsAsync("legacy-brk.us");
        SymbolResolutionMismatch? mismatch = null;
        using var resolver = new CanonicalRegistrySymbolResolver(
            registry.Object,
            inner.Object,
            mode: SymbolResolutionMode.Compare,
            mismatchObserver: observed => mismatch = observed);

        var mapped = await resolver.MapSymbolAsync("BRK-B", "yahoo", "stooq");

        mapped.Should().Be("legacy-brk.us");
        mismatch.Should().NotBeNull();
        mismatch!.CanonicalResult.Should().Be("brk-b.us");
        mismatch.LegacyResult.Should().Be("legacy-brk.us");
        mismatch.SecurityId.Should().Be(securityId);
    }
}
