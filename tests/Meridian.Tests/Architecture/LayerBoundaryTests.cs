using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
// Import ArchRuleDefinition statically for concise fluent rules.
using static ArchUnitNET.Fluent.ArchRuleDefinition;
// Alias the ArchUnitNET domain type to avoid collision with this test's namespace.
using ArchModel = ArchUnitNET.Domain.Architecture;

namespace Meridian.Tests.Architecture;

/// <summary>
/// ArchUnitNET tests that enforce the layer-boundary rules defined in
/// <c>docs/architecture/layer-boundaries.md</c> and the ADR quick-reference table.
///
/// Rules enforced:
/// <list type="bullet">
///   <item>Contracts is a leaf — it must not depend on any other project (including Domain and Infrastructure).</item>
///   <item>ProviderSdk must only depend on Contracts.</item>
///   <item>Domain must not depend on Infrastructure.</item>
///   <item>Adapter namespaces must not cross-reference peer adapters.</item>
/// </list>
/// </summary>
public sealed class LayerBoundaryTests
{
    // Build the architecture model once per test class.
    private static readonly ArchModel Architecture = new ArchLoader()
        .LoadAssemblies(
            // Leaf / shared contracts
            typeof(Meridian.Contracts.Domain.ProviderId).Assembly,
            // Provider SDK
            typeof(Meridian.Infrastructure.Adapters.Core.IProviderMetadata).Assembly,
            // Domain
            typeof(Meridian.Domain.Events.MarketEvent).Assembly,
            // Infrastructure (adapters, providers, resilience)
            typeof(Meridian.Infrastructure.Adapters.Core.ProviderTemplate).Assembly)
        .Build();

    // ------------------------------------------------------------------ //
    //  Contracts — leaf project (no upstream dependencies)                //
    // ResideInNamespaceMatching / ResideInAssemblyMatching are used here  //
    // because ResideInNamespace and ResideInAssembly do exact matching;   //
    // Contracts types live in sub-namespaces (Meridian.Contracts.*)
    // and assembly-level rules require distinguishing Infrastructure.dll  //
    // from ProviderSdk.dll (both share the Infrastructure namespace).     //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Contracts_ShouldNot_DependOn_Domain()
    {
        var rule = Types()
            .That().ResideInNamespaceMatching(@"^Meridian\.Contracts\.")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"^Meridian\.Domain\."))
            .Because("Contracts is a leaf project that must have zero upstream project dependencies (ADR-001).");

        rule.Check(Architecture);
    }

    [Fact]
    public void Contracts_ShouldNot_DependOn_Infrastructure()
    {
        var rule = Types()
            .That().ResideInNamespaceMatching(@"^Meridian\.Contracts\.")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^Meridian\.Infrastructure$"))
            .Because("Contracts is a leaf project that must have zero upstream project dependencies (ADR-001).");

        rule.Check(Architecture);
    }

    // ------------------------------------------------------------------ //
    //  Domain — must not depend on Infrastructure                         //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Domain_ShouldNot_DependOn_Infrastructure()
    {
        var rule = Types()
            .That().ResideInNamespaceMatching(@"^Meridian\.Domain\.")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^Meridian\.Infrastructure$"))
            .Because("Domain types must remain independent of Infrastructure to preserve the dependency inversion principle.");

        rule.Check(Architecture);
    }

    // ------------------------------------------------------------------ //
    //  ProviderSdk — must only depend on Contracts                        //
    // ------------------------------------------------------------------ //

    [Fact]
    public void ProviderSdk_ShouldNot_DependOn_Domain()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.ProviderSdk")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"^Meridian\.Domain\."))
            .Because("ProviderSdk must only reference Contracts to stay thin and reusable (ADR-001).");

        rule.Check(Architecture);
    }

    [Fact]
    public void ProviderSdk_ShouldNot_DependOn_Infrastructure()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.ProviderSdk")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^Meridian\.Infrastructure$"))
            .Because("ProviderSdk must only reference Contracts to stay thin and reusable (ADR-001).");

        rule.Check(Architecture);
    }

    // ------------------------------------------------------------------ //
    //  Adapter cross-references — adapters must not depend on peer adapters //
    // ------------------------------------------------------------------ //

    [Fact]
    public void AlpacaAdapter_ShouldNot_DependOn_PolygonAdapter()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Alpaca")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Infrastructure.Adapters.Polygon"))
            .Because("Provider adapters must not cross-reference peer adapters to keep them independently deployable.");

        rule.Check(Architecture);
    }

    [Fact]
    public void PolygonAdapter_ShouldNot_DependOn_AlpacaAdapter()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Polygon")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Infrastructure.Adapters.Alpaca"))
            .Because("Provider adapters must not cross-reference peer adapters to keep them independently deployable.");

        rule.Check(Architecture);
    }

    [Fact]
    public void FinnhubAdapter_ShouldNot_DependOn_AlpacaAdapter()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Finnhub")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Infrastructure.Adapters.Alpaca"))
            .Because("Provider adapters must not cross-reference peer adapters to keep them independently deployable.");

        rule.Check(Architecture);
    }

    // ------------------------------------------------------------------ //
    //  Provider-local constants stay internal                             //
    // ------------------------------------------------------------------ //

    [Fact]
    public void AlpacaConstants_ShouldBe_Internal()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Alpaca")
            .And().HaveNameEndingWith("Constants")
            .Or().HaveNameEndingWith("Endpoints")
            .Or().HaveNameEndingWith("RateLimits")
            .Or().HaveNameEndingWith("MessageTypes")
            .Or().HaveNameEndingWith("Actions")
            .Or().HaveNameEndingWith("DedupLimits")
            .Should().NotBePublic()
            .Because("Provider-local constants and endpoint strings are implementation details that must not leak into the public API.");

        rule.Check(Architecture);
    }

    [Fact]
    public void PolygonConstants_ShouldBe_Internal()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Polygon")
            .And().HaveNameEndingWith("Constants")
            .Or().HaveNameEndingWith("Endpoints")
            .Or().HaveNameEndingWith("RateLimits")
            .Or().HaveNameEndingWith("MessageTypes")
            .Or().HaveNameEndingWith("EventTypes")
            .Or().HaveNameEndingWith("Actions")
            .Or().HaveNameEndingWith("Feeds")
            .Or().HaveNameEndingWith("ApiKeyLimits")
            .Should().NotBePublic()
            .Because("Provider-local constants and endpoint strings are implementation details that must not leak into the public API.");

        rule.Check(Architecture);
    }

    [Fact]
    public void FinnhubConstants_ShouldBe_Internal()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Finnhub")
            .And().HaveNameEndingWith("Constants")
            .Or().HaveNameEndingWith("Endpoints")
            .Or().HaveNameEndingWith("RateLimits")
            .Or().HaveNameEndingWith("Headers")
            .Or().HaveNameEndingWith("Resolutions")
            .Or().HaveNameEndingWith("CandleStatus")
            .Should().NotBePublic()
            .Because("Provider-local constants and endpoint strings are implementation details that must not leak into the public API.");

        rule.Check(Architecture);
    }
}
