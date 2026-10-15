using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Archival;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed class SecurityMasterCanonicalWithdrawalDefinitionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sm-withdrawn-definition-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WithdrawnDefinition_HidesOnlyCanonical_AndPublishesCurrentMappingsWithIndependentAuthority_AfterReload()
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "EXPIRED.PRIMARY", "OLD", false)]);
        await store.AddAliasAsync("EXPIRED.PRIMARY", new SymbolAlias { Alias = "INDEPENDENT.ALIAS", Source = "manual" });
        await registry.SetProviderSymbolAsync("EXPIRED.PRIMARY", "independent", "INDEPENDENT.PROVIDER", SymbolMappingSources.Operator, true);

        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, Carrier(id), "CURRENT", true)]);

        AssertCurrentMappings(store, registry, id);
        registry.ResolveToCanonical("INDEPENDENT.ALIAS").Should().Be(Carrier(id));
        registry.TryResolveWithProvider("INDEPENDENT.PROVIDER", "independent").Should().Be(Carrier(id));
        registry.ResolveToCanonical("EXPIRED.PRIMARY").Should().BeNull();
        registry.ResolveToCanonical("OLD.ALIAS").Should().BeNull();
        registry.ResolveToCanonical("OLD.ISIN").Should().BeNull();
        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        var reloadedRegistry = new CanonicalSymbolRegistry(reloaded);
        AssertCurrentMappings(reloaded, reloadedRegistry, id);
        reloadedRegistry.ResolveToCanonical("INDEPENDENT.ALIAS").Should().Be(Carrier(id));
        reloadedRegistry.TryResolveWithProvider("INDEPENDENT.PROVIDER", "independent").Should().Be(Carrier(id));
    }

    [Fact]
    public async Task WithdrawnDefinition_CurrentAliasMatchingExpiredPrimary_RemainsResolvableByItsOwnAuthority()
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "EXPIRED.PRIMARY", "OLD", false)]);
        var current = Definition(id, Carrier(id), "CURRENT", true);
        var withdrawn = new CanonicalSymbolDefinition
        {
            Canonical = current.Canonical,
            SecurityId = id,
            SecurityMasterCanonicalWithdrawn = true,
            Isin = current.Isin,
            ProviderSymbols = current.ProviderSymbols,
            AliasDefinitions = current.AliasDefinitions.Concat([new CanonicalSymbolAliasDefinition(
                "EXPIRED.PRIMARY", SymbolMappingSources.SecurityMaster, ValidFrom: DateTimeOffset.UtcNow.AddDays(-1), IsActive: true)]).ToArray()
        };

        await registry.ReconcileSecurityMasterAsync([id], [withdrawn]);

        registry.ResolveToCanonical(Carrier(id)).Should().BeNull();
        registry.ResolveToCanonical("EXPIRED.PRIMARY").Should().Be(Carrier(id));
        registry.GetDefinition("EXPIRED.PRIMARY")!.SecurityId.Should().Be(id);
        store.LookupSymbol("EXPIRED.PRIMARY").Entry!.SecurityId.Should().Be(id);
    }

    [Fact]
    public async Task WithdrawnDefinition_DoesNotWithdrawAnIndependentlyRegisteredSameName()
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "MANUAL", SecurityId = id });
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "MANUAL", "OLD", false)]);

        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "MANUAL", "CURRENT", true)]);

        registry.ResolveToCanonical("MANUAL").Should().Be("MANUAL");
        registry.ResolveToCanonical("CURRENT.ALIAS").Should().Be("MANUAL");
        registry.GetDefinition("MANUAL")!.SecurityMasterOwnsCanonical.Should().BeFalse();
        registry.GetDefinition("MANUAL")!.SecurityMasterCanonicalWithdrawn.Should().BeFalse();
        store.LookupSymbol("MANUAL").Found.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryRegistration_IgnoresCallerSuppliedSecurityMasterOwnershipAndWithdrawalFlags(bool batch)
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        var supplied = new CanonicalSymbolDefinition
        {
            Canonical = "MANUAL",
            SecurityId = id,
            SecurityMasterOwnsCanonical = true,
            SecurityMasterCanonicalWithdrawn = true
        };

        if (batch)
            await registry.RegisterBatchAsync([supplied]);
        else
            await registry.RegisterAsync(supplied);

        registry.ResolveToCanonical("MANUAL").Should().Be("MANUAL");
        registry.GetDefinition("MANUAL")!.SecurityMasterOwnsCanonical.Should().BeFalse();
        registry.GetDefinition("MANUAL")!.SecurityMasterCanonicalWithdrawn.Should().BeFalse();
    }

    [Fact]
    public async Task FailedWithdrawnDefinitionWrite_LeavesActiveStateUnchanged_AndCanRetry()
    {
        var failWrite = false;
        var store = new SymbolRegistryService(_directory, (path, json, ct) => failWrite
            ? Task.FromException(new IOException("injected withdrawn definition write failure"))
            : AtomicFileWriter.WriteAsync(path, json, ct));
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "EXPIRED.PRIMARY", "OLD", false)]);
        var original = store.GetRegistry();
        var path = Path.Combine(_directory, "_catalog", "symbols.json");
        var before = await File.ReadAllTextAsync(path);
        failWrite = true;
        var reconcile = () => registry.ReconcileSecurityMasterAsync([id], [Definition(id, Carrier(id), "CURRENT", true)]);

        await reconcile.Should().ThrowAsync<IOException>();

        store.GetRegistry().Should().BeSameAs(original);
        (await File.ReadAllTextAsync(path)).Should().Be(before);
        registry.ResolveToCanonical("EXPIRED.PRIMARY").Should().Be("EXPIRED.PRIMARY");
        registry.ResolveToCanonical("OLD.ALIAS").Should().Be("EXPIRED.PRIMARY");
        registry.ResolveToCanonical("CURRENT.ALIAS").Should().BeNull();
        failWrite = false;
        await reconcile();
        AssertCurrentMappings(store, registry, id);
    }

    private static void AssertCurrentMappings(SymbolRegistryService store, CanonicalSymbolRegistry registry, Guid id)
    {
        var carrier = Carrier(id);
        registry.ResolveToCanonical(carrier).Should().BeNull();
        registry.GetDefinition(carrier).Should().BeNull();
        registry.IsKnown(carrier).Should().BeFalse();
        store.LookupSymbol(carrier).Found.Should().BeFalse();
        registry.ResolveToCanonical("CURRENT.ALIAS").Should().Be(carrier);
        registry.ResolveToCanonical("CURRENT.ISIN").Should().Be(carrier);
        registry.TryResolveWithProvider("CURRENT.PROVIDER", "feed").Should().Be(carrier);
        registry.GetProviderSymbol("CURRENT.ALIAS", "feed").Should().Be("CURRENT.PROVIDER");
        registry.GetDefinition("CURRENT.ALIAS")!.SecurityId.Should().Be(id);
        registry.GetDefinition("CURRENT.ISIN")!.SecurityMasterCanonicalWithdrawn.Should().BeTrue();
        registry.GetDefinition("CURRENT.ALIAS")!.Isin.Should().Be("CURRENT.ISIN");
        store.LookupSymbol("CURRENT.ALIAS").Entry!.SecurityId.Should().Be(id);
    }

    private static CanonicalSymbolDefinition Definition(Guid id, string canonical, string prefix, bool withdrawn)
        => new()
        {
            Canonical = canonical,
            SecurityId = id,
            SecurityMasterCanonicalWithdrawn = withdrawn,
            AliasDefinitions = [new(prefix + ".ALIAS", SymbolMappingSources.SecurityMaster),
                new(prefix + ".ISIN", SymbolMappingSources.SecurityMasterIdentifier)],
            Isin = prefix + ".ISIN",
            ProviderSymbols = new Dictionary<string, ProviderSymbolDefinition> { ["feed"] = new(prefix + ".PROVIDER", SymbolMappingSources.SecurityMaster) }
        };

    private static string Carrier(Guid id) => $"SECURITY-ID:{id:N}".ToUpperInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
