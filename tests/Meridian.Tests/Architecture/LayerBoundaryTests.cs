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
    private static readonly Lazy<ArchModel> Architecture = new(() => new ArchLoader()
        .LoadAssemblies(
            // Leaf / shared contracts
            typeof(Meridian.Contracts.Domain.ProviderId).Assembly,
            // Provider SDK
            typeof(Meridian.Infrastructure.Adapters.Core.IProviderMetadata).Assembly,
            // Domain
            typeof(Meridian.Domain.Events.MarketEvent).Assembly,
            // Application
            typeof(Meridian.FinancialOperations.Reconciliation.StatementReconciliationService).Assembly,
            // UI services
            typeof(Meridian.Ui.Services.Services.Reconciliation.ReconciliationApiService).Assembly,
            // Infrastructure (adapters, providers, resilience)
            typeof(Meridian.Infrastructure.Adapters.Core.ProviderTemplate).Assembly)
        .Build());

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

        rule.Check(Architecture.Value);
    }

    [Fact]
    public void Contracts_ShouldNot_DependOn_Infrastructure()
    {
        var rule = Types()
            .That().ResideInNamespaceMatching(@"^Meridian\.Contracts\.")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^Meridian\.Infrastructure$"))
            .Because("Contracts is a leaf project that must have zero upstream project dependencies (ADR-001).");

        rule.Check(Architecture.Value);
    }

    // ------------------------------------------------------------------ //
    //  Domain — must not depend on Infrastructure                         //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Domain_ShouldNot_DependOn_Infrastructure()
    {
        var domainAssembly = typeof(Meridian.Domain.Events.MarketEvent).Assembly;

        var forbiddenReferences = domainAssembly
            .GetReferencedAssemblies()
            .Where(reference => reference.Name is "Meridian.Infrastructure")
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Empty(forbiddenReferences);
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

        rule.Check(Architecture.Value);
    }

    [Fact]
    public void ProviderSdk_ShouldNot_DependOn_Infrastructure()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.ProviderSdk")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^Meridian\.Infrastructure$"))
            .Because("ProviderSdk must only reference Contracts to stay thin and reusable (ADR-001).");

        rule.Check(Architecture.Value);
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

        rule.Check(Architecture.Value);
    }

    [Fact]
    public void PolygonAdapter_ShouldNot_DependOn_AlpacaAdapter()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Polygon")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Infrastructure.Adapters.Alpaca"))
            .Because("Provider adapters must not cross-reference peer adapters to keep them independently deployable.");

        rule.Check(Architecture.Value);
    }

    [Fact]
    public void FinnhubAdapter_ShouldNot_DependOn_AlpacaAdapter()
    {
        var rule = Types()
            .That().ResideInNamespace("Meridian.Infrastructure.Adapters.Finnhub")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Infrastructure.Adapters.Alpaca"))
            .Because("Provider adapters must not cross-reference peer adapters to keep them independently deployable.");

        rule.Check(Architecture.Value);
    }

    // ------------------------------------------------------------------ //
    //  Provider-local constants stay internal                             //
    // ------------------------------------------------------------------ //

    [Fact]
    public void AlpacaConstants_ShouldBe_Internal()
    {
        AssertProviderLocalNamesAreInternal(
            "Meridian.Infrastructure.Adapters.Alpaca",
            "Constants",
            "Endpoints",
            "RateLimits",
            "MessageTypes",
            "Actions",
            "DedupLimits");
    }

    [Fact]
    public void PolygonConstants_ShouldBe_Internal()
    {
        AssertProviderLocalNamesAreInternal(
            "Meridian.Infrastructure.Adapters.Polygon",
            "Constants",
            "Endpoints",
            "RateLimits",
            "MessageTypes",
            "EventTypes",
            "Actions",
            "Feeds",
            "ApiKeyLimits");
    }

    [Fact]
    public void FinnhubConstants_ShouldBe_Internal()
    {
        AssertProviderLocalNamesAreInternal(
            "Meridian.Infrastructure.Adapters.Finnhub",
            "Constants",
            "Endpoints",
            "RateLimits",
            "Headers",
            "Resolutions",
            "CandleStatus");
    }

    [Fact]
    public void ReconciliationContext_ShouldNot_DependOn_UiShared_Or_UiServices()
    {
        var reconciliationAssembly = typeof(Meridian.FinancialOperations.Reconciliation.StatementReconciliationContextAdapter).Assembly;

        var forbiddenReferences = reconciliationAssembly
            .GetReferencedAssemblies()
            .Where(reference => reference.Name is "Meridian.Ui.Shared" or "Meridian.Ui.Services")
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    private static void AssertProviderLocalNamesAreInternal(string providerNamespace, params string[] suffixes)
    {
        var providerLocalTypes = typeof(Meridian.Infrastructure.Adapters.Core.ProviderTemplate).Assembly
            .GetTypes()
            .Where(type =>
                string.Equals(type.Namespace, providerNamespace, StringComparison.Ordinal) &&
                suffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(providerLocalTypes);
        foreach (var type in providerLocalTypes)
        {
            Assert.False(
                type.IsVisible,
                $"{type.FullName} must stay internal because provider-local constants and endpoint strings are implementation details that must not leak into the public API.");
        }
    }

    [Fact]
    public void UiReconciliationApi_ShouldNot_DependOn_InfrastructureReconciliation()
    {
        var rule = Types()
            .That().ResideInNamespaceMatching(@"^Meridian\.Ui\.Services\.Services\.Reconciliation\.")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"^Meridian\.Infrastructure\.Reconciliation\."))
            .Because("UI reconciliation services should consume application-owned workflows instead of orchestrating infrastructure reconciliation storage directly.")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture.Value);
    }
}
