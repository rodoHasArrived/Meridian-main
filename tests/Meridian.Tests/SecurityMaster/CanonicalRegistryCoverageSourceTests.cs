using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies that every canonical symbol known to the canonicalization spine feeds the
/// security-master coverage sweep, deduplicated and resilient to a missing registry.
/// </summary>
public sealed class CanonicalRegistryCoverageSourceTests
{
    [Fact]
    public async Task GetActiveSymbols_ReturnsDistinctCanonicalSymbols()
    {
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.GetAll().Returns(
        [
            new CanonicalSymbolDefinition { Canonical = "AAPL" },
            new CanonicalSymbolDefinition { Canonical = "aapl" },
            new CanonicalSymbolDefinition { Canonical = "GME" },
            new CanonicalSymbolDefinition { Canonical = "  " }
        ]);
        var source = new CanonicalRegistryCoverageSource(registry);

        var symbols = await source.GetActiveSymbolsAsync();

        symbols.Should().BeEquivalentTo("AAPL", "GME");
        source.SourceName.Should().Be("canonical-symbol-registry");
    }

    [Fact]
    public async Task GetActiveSymbols_WithoutRegistry_ReturnsEmpty()
    {
        var symbols = await new CanonicalRegistryCoverageSource().GetActiveSymbolsAsync();

        symbols.Should().BeEmpty();
    }
}
