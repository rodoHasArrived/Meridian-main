using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Archival;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed class SecurityMasterCanonicalMovePlanTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sm-canonical-move-plan-" + Guid.NewGuid().ToString("N"));
    private string RegistryPath => Path.Combine(_directory, "_catalog", "symbols.json");

    public static IEnumerable<object[]> MoveOrders()
    {
        foreach (var scenario in new[] { "chain", "swap", "cycle" })
            foreach (var reverseRegistry in new[] { false, true })
                foreach (var reverseIncoming in new[] { false, true })
                    yield return [scenario, reverseRegistry, reverseIncoming];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Withdrawal_FreesSecurityMasterOnlyDestination_BeforeAnyActiveMove(bool reverseRegistry)
    {
        var (store, registry, ids) = await SeedAsync(["START", "TARGET"], reverseRegistry);

        await registry.ReconcileSecurityMasterAsync(ids, [Definition(ids[0], "TARGET", 0, "NEW")]);

        registry.GetAll().Should().ContainSingle().Which.SecurityId.Should().Be(ids[0]);
        registry.GetDefinition("TARGET")!.SecurityId.Should().Be(ids[0]);
        registry.ResolveToCanonical("START").Should().BeNull();
        registry.ResolveToCanonical("OLD.ALIAS.1").Should().BeNull();
        registry.ResolveToCanonical("NEW.ALIAS.0").Should().Be("TARGET");
        store.LookupSymbol("TARGET").Entry!.SecurityId.Should().Be(ids[0]);
        var reloaded = await ReloadAsync();
        reloaded.GetDefinition("TARGET")!.SecurityId.Should().Be(ids[0]);
        reloaded.GetAll().Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(MoveOrders))]
    public async Task ActiveMoves_ApplyFinalChainSwapOrCycle_IndependentlyOfRegistryAndIncomingOrder(
        string scenario,
        bool reverseRegistry,
        bool reverseIncoming)
    {
        string[] originals = scenario == "swap" ? ["FIRST", "SECOND"] : ["FIRST", "SECOND", "THIRD"];
        string[] destinations = scenario switch
        {
            "chain" => new[] { "SECOND", "THIRD", "FOURTH" },
            "swap" => ["SECOND", "FIRST"],
            _ => ["SECOND", "THIRD", "FIRST"]
        };
        var (store, registry, ids) = await SeedAsync(originals, reverseRegistry);
        await store.AddAliasAsync(originals[0], new SymbolAlias { Alias = "INDEPENDENT.ALIAS", Source = "manual" });
        await registry.SetProviderSymbolAsync(originals[0], "independent", "INDEPENDENT.PROVIDER", SymbolMappingSources.Operator, true);
        var incoming = ids.Select((id, index) => Definition(id, destinations[index], index, "NEW")).ToArray();
        if (reverseIncoming)
            Array.Reverse(incoming);

        await registry.ReconcileSecurityMasterAsync(ids, incoming);

        AssertMovedIdentities(store, registry, ids, destinations);
        registry.ResolveToCanonical("INDEPENDENT.ALIAS").Should().Be(destinations[0]);
        registry.TryResolveWithProvider("INDEPENDENT.PROVIDER", "independent").Should().Be(destinations[0]);
        registry.GetProviderSymbol("INDEPENDENT.ALIAS", "independent").Should().Be("INDEPENDENT.PROVIDER");
        if (scenario == "chain")
            registry.ResolveToCanonical("FIRST").Should().BeNull("the moved Security Master-only name does not become an independent alias");
        var reloadedStore = new SymbolRegistryService(_directory);
        await reloadedStore.InitializeAsync();
        var reloaded = new CanonicalSymbolRegistry(reloadedStore);
        AssertMovedIdentities(reloadedStore, reloaded, ids, destinations);
        reloaded.ResolveToCanonical("INDEPENDENT.ALIAS").Should().Be(destinations[0]);
        reloaded.TryResolveWithProvider("INDEPENDENT.PROVIDER", "independent").Should().Be(destinations[0]);
    }

    [Theory]
    [InlineData("alias", false)]
    [InlineData("alias", true)]
    [InlineData("provider", false)]
    [InlineData("provider", true)]
    public async Task Withdrawal_RetainsDestinationWithIndependentAuthority_AndRefusesItsTransferAtomically(
        string authority,
        bool reverseRegistry)
    {
        var (store, registry, ids) = await SeedAsync(["START", "TARGET"], reverseRegistry);
        if (authority == "alias")
            await store.AddAliasAsync("TARGET", new SymbolAlias { Alias = "INDEPENDENT.TARGET", Source = "manual" });
        else
            await registry.SetProviderSymbolAsync("TARGET", "independent", "INDEPENDENT.TARGET", SymbolMappingSources.Operator, true);
        var before = await File.ReadAllTextAsync(RegistryPath);
        var original = store.GetRegistry();

        var move = () => registry.ReconcileSecurityMasterAsync(ids, [Definition(ids[0], "TARGET", 0, "NEW")]);

        await move.Should().ThrowAsync<InvalidOperationException>();
        AssertOriginalIdentities(store, registry, ids, ["START", "TARGET"]);
        store.GetRegistry().Should().BeSameAs(original);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        registry.ResolveToCanonical("INDEPENDENT.TARGET").Should().Be("TARGET");
        registry.ResolveToCanonical("NEW.ALIAS.0").Should().BeNull();
    }

    [Theory]
    [InlineData("independent-canonical", false)]
    [InlineData("independent-canonical", true)]
    [InlineData("same-name-alias", false)]
    [InlineData("same-name-alias", true)]
    [InlineData("same-name-typed-identifier", false)]
    [InlineData("same-name-typed-identifier", true)]
    public async Task MovedAwayCanonical_WithIndependentAuthorityCannotBeTransferredToAnotherSecurity(
        string authority,
        bool reverseRegistry)
    {
        var (store, registry, ids) = await SeedAsync(["START", "TARGET"], reverseRegistry);
        if (authority == "independent-canonical")
            await registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "TARGET", SecurityId = ids[1] });
        else if (authority == "same-name-alias")
            await store.AddAliasAsync("TARGET", new SymbolAlias { Alias = "TARGET", Source = "manual", IsActive = true });
        else
        {
            store.GetRegistry().Symbols["TARGET"].Identifiers.Figi = "TARGET";
            await store.SaveRegistryAsync();
        }
        var original = store.GetRegistry();
        var before = await File.ReadAllTextAsync(RegistryPath);

        var move = () => registry.ReconcileSecurityMasterAsync(ids,
            [Definition(ids[0], "TARGET", 0, "NEW"), Definition(ids[1], "AWAY", 1, "NEW")]);

        await move.Should().ThrowAsync<InvalidOperationException>();
        AssertOriginalIdentities(store, registry, ids, ["START", "TARGET"]);
        store.GetRegistry().Should().BeSameAs(original);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        registry.ResolveToCanonical("AWAY").Should().BeNull();
        if (authority == "same-name-typed-identifier")
            store.GetRegistry().Symbols["TARGET"].Identifiers.Figi.Should().Be("TARGET");
        if (authority == "same-name-alias")
            store.GetRegistry().Symbols["TARGET"].Aliases.Should().Contain(alias => alias.Alias == "TARGET" && alias.Source == "manual");
    }

    [Fact]
    public async Task OccupiedDestinationOutsideScope_RefusesWithoutChangingDiskOrResolvers()
    {
        var (store, registry, ids) = await SeedAsync(["START", "TARGET"], false);
        var before = await File.ReadAllTextAsync(RegistryPath);
        var original = store.GetRegistry();

        var move = () => registry.ReconcileSecurityMasterAsync([ids[0]], [Definition(ids[0], "TARGET", 0, "NEW")]);

        await move.Should().ThrowAsync<InvalidOperationException>();
        AssertOriginalIdentities(store, registry, ids, ["START", "TARGET"]);
        store.GetRegistry().Should().BeSameAs(original);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
    }

    [Fact]
    public async Task DuplicateNormalizedDestinations_RefuseBeforePublishingAnyMove()
    {
        var (store, registry, ids) = await SeedAsync(["FIRST", "SECOND"], false);
        var before = await File.ReadAllTextAsync(RegistryPath);
        var original = store.GetRegistry();

        var move = () => registry.ReconcileSecurityMasterAsync(ids,
            [Definition(ids[0], " shared ", 0, "NEW"), Definition(ids[1], "SHARED", 1, "NEW")]);

        await move.Should().ThrowAsync<InvalidOperationException>();
        AssertOriginalIdentities(store, registry, ids, ["FIRST", "SECOND"]);
        store.GetRegistry().Should().BeSameAs(original);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        registry.ResolveToCanonical("SHARED").Should().BeNull();
    }

    [Fact]
    public async Task FreshSecurityId_CanBindExistingIdentityFreeManualCanonical_WithoutClaimingIndependentAuthority()
    {
        var store = new SymbolRegistryService(_directory);
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "MANUAL",
            Aliases = [new() { Alias = "INDEPENDENT.MANUAL", Source = SymbolMappingSources.Operator }]
        });
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();

        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "MANUAL", 0, "NEW")]);

        registry.GetDefinition("MANUAL")!.SecurityId.Should().Be(id);
        registry.GetDefinition("MANUAL")!.SecurityMasterOwnsCanonical.Should().BeFalse();
        registry.ResolveToCanonical("INDEPENDENT.MANUAL").Should().Be("MANUAL");
        registry.ResolveToCanonical("NEW.ALIAS.0").Should().Be("MANUAL");
        await registry.ReconcileSecurityMasterAsync([id], []);
        registry.ResolveToCanonical("MANUAL").Should().Be("MANUAL");
        registry.ResolveToCanonical("INDEPENDENT.MANUAL").Should().Be("MANUAL");
        registry.ResolveToCanonical("NEW.ALIAS.0").Should().BeNull();
        var reloaded = await ReloadAsync();
        reloaded.GetDefinition("MANUAL")!.SecurityId.Should().Be(id);
        reloaded.GetDefinition("MANUAL")!.SecurityMasterOwnsCanonical.Should().BeFalse();
    }

    [Fact]
    public async Task RetainedSecurityId_CannotMoveIntoAnotherIdentityFreeManualCanonical()
    {
        var (store, registry, ids) = await SeedAsync(["START"], false);
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "MANUAL",
            Aliases = [new() { Alias = "INDEPENDENT.MANUAL", Source = SymbolMappingSources.Operator }]
        });
        var before = await File.ReadAllTextAsync(RegistryPath);
        var original = store.GetRegistry();

        var move = () => registry.ReconcileSecurityMasterAsync(ids, [Definition(ids[0], "MANUAL", 0, "NEW")]);

        await move.Should().ThrowAsync<InvalidOperationException>();
        store.GetRegistry().Should().BeSameAs(original);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        registry.GetDefinition("START")!.SecurityId.Should().Be(ids[0]);
        registry.ResolveToCanonical("OLD.ALIAS.0").Should().Be("START");
        registry.GetDefinition("MANUAL")!.SecurityId.Should().BeNull();
        registry.ResolveToCanonical("INDEPENDENT.MANUAL").Should().Be("MANUAL");
        registry.ResolveToCanonical("NEW.ALIAS.0").Should().BeNull();
    }

    [Fact]
    public async Task FailedSwapWrite_LeavesDiskAndLiveResolversUnchanged_AndSameMoveSetCanRetry()
    {
        var failWrite = false;
        var store = new SymbolRegistryService(_directory, (path, contents, ct) => failWrite
            ? Task.FromException(new IOException("injected canonical move write failure"))
            : AtomicFileWriter.WriteAsync(path, contents, ct));
        var registry = new CanonicalSymbolRegistry(store);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await registry.ReconcileSecurityMasterAsync(ids,
            [Definition(ids[0], "FIRST", 0, "OLD"), Definition(ids[1], "SECOND", 1, "OLD")]);
        var original = store.GetRegistry();
        var before = await File.ReadAllTextAsync(RegistryPath);
        var incoming = new[] { Definition(ids[0], "SECOND", 0, "NEW"), Definition(ids[1], "FIRST", 1, "NEW") };
        failWrite = true;

        var move = () => registry.ReconcileSecurityMasterAsync(ids, incoming);
        await move.Should().ThrowAsync<IOException>();

        AssertOriginalIdentities(store, registry, ids, ["FIRST", "SECOND"]);
        store.GetRegistry().Should().BeSameAs(original);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        registry.ResolveToCanonical("NEW.ALIAS.0").Should().BeNull();
        registry.ResolveToCanonical("NEW.ALIAS.1").Should().BeNull();
        failWrite = false;
        await move();
        AssertMovedIdentities(store, registry, ids, ["SECOND", "FIRST"]);
    }

    private async Task<(SymbolRegistryService Store, CanonicalSymbolRegistry Registry, Guid[] Ids)> SeedAsync(
        string[] canonicals,
        bool reverseRegistry)
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var ids = canonicals.Select(_ => Guid.NewGuid()).ToArray();
        await registry.ReconcileSecurityMasterAsync(ids,
            ids.Select((id, index) => Definition(id, canonicals[index], index, "OLD")));
        if (reverseRegistry)
        {
            // Exercise the persisted dictionary's insertion order, even if incoming plans
            // are sorted deterministically before their initial registration.
            store.GetRegistry().Symbols = store.GetRegistry().Symbols.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            await store.SaveRegistryAsync();
        }
        return (store, registry, ids);
    }

    private async Task<CanonicalSymbolRegistry> ReloadAsync()
    {
        var store = new SymbolRegistryService(_directory);
        await store.InitializeAsync();
        return new CanonicalSymbolRegistry(store);
    }

    private static CanonicalSymbolDefinition Definition(Guid id, string canonical, int index, string revision)
    {
        var alias = $"{revision}.ALIAS.{index}";
        var isin = Identifier(index, revision);
        return new()
        {
            Canonical = canonical,
            SecurityId = id,
            Isin = isin,
            AliasDefinitions = [new(alias, SymbolMappingSources.SecurityMaster), new(isin, SymbolMappingSources.SecurityMasterIdentifier)],
            ProviderSymbols = new Dictionary<string, ProviderSymbolDefinition> { ["feed"] = new(alias, SymbolMappingSources.SecurityMaster) }
        };
    }

    private static string Identifier(int index, string revision) => $"US{(revision == "OLD" ? index + 1 : index + 101):0000000000}";

    private static void AssertOriginalIdentities(SymbolRegistryService store, CanonicalSymbolRegistry registry, Guid[] ids, string[] canonicals)
    {
        registry.GetAll().Should().HaveCount(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            registry.GetDefinition(canonicals[index])!.SecurityId.Should().Be(ids[index]);
            registry.ResolveToCanonical($"OLD.ALIAS.{index}").Should().Be(canonicals[index]);
            registry.TryResolveWithProvider($"OLD.ALIAS.{index}", "feed").Should().Be(canonicals[index]);
            store.LookupSymbol(Identifier(index, "OLD")).Entry!.SecurityId.Should().Be(ids[index]);
        }
    }

    private static void AssertMovedIdentities(SymbolRegistryService store, CanonicalSymbolRegistry registry, Guid[] ids, string[] destinations)
    {
        registry.GetAll().Should().HaveCount(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            registry.GetDefinition(destinations[index])!.SecurityId.Should().Be(ids[index]);
            registry.GetDefinition(destinations[index])!.SecurityMasterCanonicalWithdrawn.Should().BeFalse();
            registry.ResolveToCanonical($"NEW.ALIAS.{index}").Should().Be(destinations[index]);
            registry.TryResolveWithProvider($"NEW.ALIAS.{index}", "feed").Should().Be(destinations[index]);
            store.LookupSymbol(Identifier(index, "NEW")).Entry!.SecurityId.Should().Be(ids[index]);
            registry.ResolveToCanonical($"OLD.ALIAS.{index}").Should().BeNull();
            registry.ResolveToCanonical(Identifier(index, "OLD")).Should().BeNull();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
