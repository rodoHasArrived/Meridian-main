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
    public async Task IdentifierAliasFromThePreReconciliationSeed_IsClaimedAndReconciled()
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

        registry.ResolveToCanonical("US0000000001").Should().BeNull("the legacy identifier alias was the seed's copy");
        registry.ResolveToCanonical("US0000000002").Should().Be("SMTEST");
        registry.ResolveToCanonical("LEGACYOP").Should().Be("SMTEST", "a non-identifier alias is not claimed");
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
