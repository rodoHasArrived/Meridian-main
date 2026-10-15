using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Archival;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed class SecurityMasterSymbolReconciliationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sm-registry-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reconciliation_PreservesSharedAliasOwnershipAndOperatorOverride()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await registry.RegisterAsync(Definition(id, "SMTEST", "SHARED", SymbolMappingSources.Operator));
        await registry.SetProviderSymbolAsync("SMTEST", "ib", "OPERATOR", SymbolMappingSources.Operator, true);
        await registry.RegisterAsync(Definition(otherId, "OTHERTEST", "UNRELATED", SymbolMappingSources.SecurityMaster));
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "SMTEST", "SHARED", SymbolMappingSources.SecurityMaster)]);
        registry.GetDefinition("SMTEST")!.AliasDefinitions.Should().Contain(alias => alias.Source == SymbolMappingSources.Operator);
        registry.GetDefinition("SMTEST")!.AliasDefinitions.Should().Contain(alias => alias.Source == SymbolMappingSources.SecurityMaster);

        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "SMTEST", "REPLACED", SymbolMappingSources.SecurityMaster)]);
        registry.ResolveToCanonical("SHARED").Should().Be("SMTEST", "independent operator ownership survives");
        registry.ResolveToCanonical("REPLACED").Should().Be("SMTEST");
        registry.GetProviderSymbol("SMTEST", "ib").Should().Be("OPERATOR");
        registry.ResolveToCanonical("UNRELATED").Should().Be("OTHERTEST", "scope is bounded to the supplied IDs");

        // An inactive cached security withdraws its source mappings, but not other authority.
        await registry.ReconcileSecurityMasterAsync([id], []);
        registry.ResolveToCanonical("REPLACED").Should().BeNull();
        registry.ResolveToCanonical("SHARED").Should().Be("SMTEST");
        registry.GetProviderSymbol("SMTEST", "ib").Should().Be("OPERATOR");
    }

    [Fact]
    public async Task FailedReconciliation_LeavesDiskAndLiveResolversUnchanged_ThenCanRetry()
    {
        var failWrite = false;
        var store = new SymbolRegistryService(_directory, (path, contents, ct) =>
            failWrite ? Task.FromException(new IOException("injected write failure"))
                : AtomicFileWriter.WriteAsync(path, contents, ct));
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "SMTEST", "OLD", SymbolMappingSources.SecurityMaster)]);
        var path = Path.Combine(_directory, "_catalog", "symbols.json");
        var original = await File.ReadAllTextAsync(path);
        failWrite = true;
        var update = () => registry.ReconcileSecurityMasterAsync([id], [Definition(id, "SMTEST", "NEW", SymbolMappingSources.SecurityMaster)]);
        await update.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(path)).Should().Be(original);
        registry.ResolveToCanonical("OLD").Should().Be("SMTEST");
        registry.ResolveToCanonical("NEW").Should().BeNull();
        store.LookupSymbol("OLD").CanonicalSymbol.Should().Be("SMTEST");

        failWrite = false;
        await update();
        registry.ResolveToCanonical("OLD").Should().BeNull();
        registry.ResolveToCanonical("NEW").Should().Be("SMTEST");
        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        new CanonicalSymbolRegistry(reloaded).ResolveToCanonical("OLD").Should().BeNull();
    }

    [Fact]
    public async Task PlainAndRichAlias_KeepExplicitProvenanceAndDisabledState()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        await registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "SMTEST",
            SecurityId = id,
            Aliases = ["SMTEST.O"],
            AliasDefinitions = [new("SMTEST.O", SymbolMappingSources.SecurityMaster, IsActive: false)]
        });
        registry.ResolveToCanonical("SMTEST.O").Should().BeNull();
        registry.GetDefinition("SMTEST")!.AliasDefinitions.Should().ContainSingle()
            .Which.Source.Should().Be(SymbolMappingSources.SecurityMaster);
    }

    [Fact]
    public async Task CorrectedSecurityMasterIdentifier_StopsResolvingWhileOtherAuthorityIsKept()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        await registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "SMTEST",
            SecurityId = id,
            Figi = "BBG000OPERATR"
        });

        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, isin: "US0000000001")]);
        registry.ResolveToCanonical("US0000000001").Should().Be("SMTEST");

        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, isin: "US0000000002")]);
        registry.ResolveToCanonical("US0000000001").Should().BeNull("the superseded ISIN was Security Master-owned");
        registry.ResolveToCanonical("US0000000002").Should().Be("SMTEST");
        registry.GetDefinition("SMTEST")!.Isin.Should().Be("US0000000002");
        registry.GetDefinition("SMTEST")!.Figi.Should().Be("BBG000OPERATR", "an identifier another source supplied is kept");

        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, isin: null)]);
        registry.ResolveToCanonical("US0000000002").Should().BeNull("a removed Security Master identifier stops resolving");
        registry.GetDefinition("SMTEST")!.Isin.Should().BeNull();
    }

    [Fact]
    public async Task ProvenanceFreeIdentifierAlias_IsPreservedUntilExplicitEvidenceBasedCleanup()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        // The earlier seed registered identifiers as plain aliases, whose source is inferred ("isin").
        await registry.RegisterBatchAsync([new CanonicalSymbolDefinition
        {
            Canonical = "SMTEST",
            SecurityId = id,
            Aliases = ["US0000000001", "LEGACYOP"],
            Isin = "US0000000001"
        }]);
        registry.ResolveToCanonical("US0000000001").Should().Be("SMTEST");

        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, isin: "US0000000002")]);

        registry.ResolveToCanonical("US0000000001").Should().Be("SMTEST", "a legacy seed alias is indistinguishable from a manually registered alias");
        registry.ResolveToCanonical("US0000000002").Should().Be("SMTEST");
        registry.ResolveToCanonical("LEGACYOP").Should().Be("SMTEST", "a non-identifier alias is not claimed");
    }

    [Fact]
    public async Task PrimaryTickerRename_AlignsCanonicalIdentityAndResolvers_AfterReload()
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "OLD", "OLD.PROVIDER", SymbolMappingSources.SecurityMaster)]);

        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "NEW", "NEW.PROVIDER", SymbolMappingSources.SecurityMaster)]);

        registry.ResolveToCanonical("NEW").Should().Be("NEW");
        registry.ResolveToCanonical("NEW.PROVIDER").Should().Be("NEW");
        registry.ResolveToCanonical("OLD").Should().BeNull("the current snapshot supplies no retained historical alias");
        registry.ResolveToCanonical("OLD.PROVIDER").Should().BeNull();
        registry.GetAll().Should().ContainSingle().Which.SecurityId.Should().Be(id);
        registry.GetDefinition("NEW")!.SecurityMasterOwnsCanonical.Should().BeTrue();
        store.LookupSymbol("NEW").CanonicalSymbol.Should().Be("NEW");

        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        var reloadedRegistry = new CanonicalSymbolRegistry(reloaded);
        reloadedRegistry.ResolveToCanonical("NEW").Should().Be("NEW");
        reloadedRegistry.ResolveToCanonical("OLD").Should().BeNull();
        reloadedRegistry.GetAll().Where(entry => entry.SecurityId == id)
            .Should().ContainSingle().Which.Canonical.Should().Be("NEW");
    }

    [Fact]
    public async Task Rename_PreservesAnIndependentlyRegisteredCanonicalAsAnAlias()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        await registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "OPERATOR.OLD", SecurityId = id });

        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "CURRENT", "CURRENT.PROVIDER", SymbolMappingSources.SecurityMaster)]);

        registry.ResolveToCanonical("OPERATOR.OLD").Should().Be("CURRENT");
        registry.ResolveToCanonical("CURRENT").Should().Be("CURRENT");
        registry.GetDefinition("CURRENT")!.AliasDefinitions.Should().Contain(alias =>
            alias.Alias == "OPERATOR.OLD" && alias.Source == SymbolMappingSources.Registry);

        await registry.ReconcileSecurityMasterAsync([id], []);
        registry.ResolveToCanonical("CURRENT.PROVIDER").Should().BeNull();
        registry.ResolveToCanonical("OPERATOR.OLD").Should().Be("CURRENT", "independent registry authority survives withdrawal");
    }

    [Fact]
    public async Task WithdrawnSecurityMasterOnlySecurity_RemovesTheCanonicalAndAllOwnedMappings()
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, "US0000000001")]);

        await registry.ReconcileSecurityMasterAsync([id], []);

        registry.ResolveToCanonical("SMTEST").Should().BeNull();
        registry.ResolveToCanonical("US0000000001").Should().BeNull();
        registry.GetAll().Should().BeEmpty();
        store.LookupSymbol("SMTEST").Found.Should().BeFalse();
        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        reloaded.LookupSymbol("SMTEST").Found.Should().BeFalse();
    }

    [Fact]
    public async Task IndependentRegistrationOfSeededCanonical_RetainsItAfterWithdrawal()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, "US0000000001")]);
        await registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "SMTEST", SecurityId = id });

        await registry.ReconcileSecurityMasterAsync([id], []);

        registry.ResolveToCanonical("SMTEST").Should().Be("SMTEST");
        registry.ResolveToCanonical("US0000000001").Should().BeNull();
        registry.GetDefinition("SMTEST")!.SecurityMasterOwnsCanonical.Should().BeFalse();
    }

    [Fact]
    public async Task IndependentlyRegisteredAlternativeTicker_RetainsItAfterWithdrawal()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, "US0000000001")]);
        await registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "OPERATOR.ALT", SecurityId = id });

        await registry.ReconcileSecurityMasterAsync([id], []);

        registry.ResolveToCanonical("OPERATOR.ALT").Should().Be("SMTEST");
        registry.GetDefinition("SMTEST")!.AliasDefinitions.Should().Contain(alias =>
            alias.Alias == "OPERATOR.ALT" && alias.Source == SymbolMappingSources.Registry);
    }

    [Fact]
    public async Task RenameIntoAnotherIdentity_RefusesWithoutChangingDiskOrResolvers()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id, otherId],
            [Definition(id, "FIRST", "FIRST.ALIAS", SymbolMappingSources.SecurityMaster),
                Definition(otherId, "SECOND", "SECOND.ALIAS", SymbolMappingSources.SecurityMaster)]);
        var path = Path.Combine(_directory, "_catalog", "symbols.json");
        var original = await File.ReadAllTextAsync(path);

        var rename = () => registry.ReconcileSecurityMasterAsync([id],
            [Definition(id, "SECOND", "CONFLICT", SymbolMappingSources.SecurityMaster)]);

        await rename.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(path)).Should().Be(original);
        registry.ResolveToCanonical("FIRST.ALIAS").Should().Be("FIRST");
        registry.ResolveToCanonical("SECOND.ALIAS").Should().Be("SECOND");
        registry.ResolveToCanonical("CONFLICT").Should().BeNull();
    }

    [Fact]
    public async Task UnknownCanonicalProvenance_DefaultsToIndependentAuthority()
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "LEGACY", SecurityId = id });
        var path = Path.Combine(_directory, "_catalog", "symbols.json");
        (await File.ReadAllTextAsync(path)).Should().NotContain("securityMasterOwnsCanonical");
        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        registry = new CanonicalSymbolRegistry(reloaded);

        await registry.ReconcileSecurityMasterAsync([id], []);

        registry.ResolveToCanonical("LEGACY").Should().Be("LEGACY");
    }

    [Fact]
    public async Task OrdinarySecurityMasterAlias_SharingAnotherSourcesIdentifier_DoesNotClearIt()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        await registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "SMTEST",
            SecurityId = id,
            Figi = "BBG000OPENFIG"
        });
        var withAlias = Definition(id, "SMTEST", "BBG000OPENFIG", SymbolMappingSources.SecurityMaster);

        await registry.ReconcileSecurityMasterAsync([id], [withAlias]);
        await registry.ReconcileSecurityMasterAsync([id], [withAlias]);
        await registry.ReconcileSecurityMasterAsync([id], [Definition(id, "SMTEST", "OTHER", SymbolMappingSources.SecurityMaster)]);

        registry.GetDefinition("SMTEST")!.Figi.Should().Be(
            "BBG000OPENFIG", "Security Master never supplied the FIGI, only an alias with the same spelling");
        registry.ResolveToCanonical("BBG000OPENFIG").Should().Be("SMTEST");
    }

    [Fact]
    public async Task InferredSourceAliasForAnIdentifierSecurityMasterDoesNotSupply_IsNotClaimed()
    {
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var id = Guid.NewGuid();
        // Another caller registered a CUSIP and the same value as a plain alias ("manual" source).
        await registry.RegisterBatchAsync([new CanonicalSymbolDefinition
        {
            Canonical = "SMTEST",
            SecurityId = id,
            Aliases = ["037833100"],
            Cusip = "037833100"
        }]);

        await registry.ReconcileSecurityMasterAsync([id], [IdentifierDefinition(id, isin: "US0000000002")]);

        registry.GetDefinition("SMTEST")!.Cusip.Should().Be("037833100", "Security Master supplies no CUSIP");
        registry.ResolveToCanonical("037833100").Should().Be("SMTEST");
    }

    // Mirrors the definitions SecurityMasterCanonicalSymbolSeedService builds for identifiers.
    private static CanonicalSymbolDefinition IdentifierDefinition(Guid id, string? isin)
        => new()
        {
            Canonical = "SMTEST",
            SecurityId = id,
            Aliases = isin is null ? [] : [isin],
            AliasDefinitions = isin is null ? [] : [new(isin, SymbolMappingSources.SecurityMasterIdentifier)],
            Isin = isin
        };

    private static CanonicalSymbolDefinition Definition(Guid id, string ticker, string alias, string source)
        => new()
        {
            Canonical = ticker,
            SecurityId = id,
            AliasDefinitions = [new(alias, source)],
            ProviderSymbols = new Dictionary<string, ProviderSymbolDefinition>
            {
                ["ib"] = new(alias, source)
            }
        };

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
