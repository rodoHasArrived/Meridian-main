using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Archival;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class SecurityMasterLegacyIdentifierCleanupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LegacyIdentifierCleanup_" + Guid.NewGuid().ToString("N"));
    private string RegistryPath => Path.Combine(_directory, "_catalog", "symbols.json");

    [Fact]
    public async Task Apply_RemovesOnlyExactEvidenceBoundAlias_AndPreservesReplacementAndOtherAuthorities()
    {
        var store = new SymbolRegistryService(_directory);
        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "CLEANUP",
            SecurityId = id,
            Identifiers = new() { Isin = "REPLACEMENT-ISIN", Figi = "RETAINED-FIGI" },
            Aliases =
            [
                new() { Alias = "OLD-ISIN", Source = "manual" },
                new() { Alias = "OLD-ISIN", Source = SymbolMappingSources.Operator },
                new() { Alias = "OLD-ISIN", Source = "manual", Provider = "feed" },
                new() { Alias = "OTHER-LEGACY", Source = "manual" }
            ]
        });
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "OTHER",
            SecurityId = otherId,
            Aliases = [new() { Alias = "OLD-ISIN", Source = "manual" }]
        });
        var registry = new CanonicalSymbolRegistry(store);
        var manifest = await ManifestAsync(store, Item(id));
        var before = await File.ReadAllTextAsync(RegistryPath);
        var validation = await registry.ValidateLegacyIdentifierCleanupAsync(manifest);

        validation.RemovedAliases.Should().Be(1);
        validation.ClearedIdentifiers.Should().Be(0);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        store.GetRegistry().LegacyIdentifierCleanupReceipts.Should().BeEmpty();

        var receipt = await registry.ApplyLegacyIdentifierCleanupAsync(validation);

        receipt.Manifest.Should().BeEquivalentTo(manifest);
        var retained = store.GetRegistry().Symbols["CLEANUP"];
        retained.Aliases.Should().HaveCount(3);
        retained.Aliases.Should().Contain(alias => alias.Alias == "OLD-ISIN" && alias.Source == SymbolMappingSources.Operator);
        retained.Aliases.Should().Contain(alias => alias.Alias == "OLD-ISIN" && alias.Provider == "feed");
        retained.Aliases.Should().Contain(alias => alias.Alias == "OTHER-LEGACY");
        retained.Identifiers.Isin.Should().Be("REPLACEMENT-ISIN");
        retained.Identifiers.Figi.Should().Be("RETAINED-FIGI");
        store.GetRegistry().Symbols["OTHER"].Aliases.Should().ContainSingle();
        registry.TryResolve("OLD-ISIN", "feed").Should().Be("CLEANUP");
    }

    [Theory]
    [InlineData(SecurityIdentifierKind.Isin, "isin")]
    [InlineData(SecurityIdentifierKind.Cusip, "manual")]
    [InlineData(SecurityIdentifierKind.Figi, "figi")]
    [InlineData(SecurityIdentifierKind.Sedol, null)]
    public async Task Apply_ClearsSelectedTypedIdentifierAndAllResolverViews_AfterDryRun(
        SecurityIdentifierKind kind,
        string? source)
    {
        var store = new SymbolRegistryService(_directory);
        var id = Guid.NewGuid();
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "CLEANUP",
            SecurityId = id,
            Identifiers = Identifiers(kind, "OLD-ISIN"),
            Aliases = [new() { Alias = "OLD-ISIN", Source = source }]
        });
        var registry = new CanonicalSymbolRegistry(store);
        registry.ResolveToCanonical("OLD-ISIN").Should().Be("CLEANUP");
        var manifest = await ManifestAsync(store, Item(id) with
        {
            IdentifierKind = kind,
            ExpectedAliasSource = source,
            TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1"
        });
        var validation = await registry.ValidateLegacyIdentifierCleanupAsync(manifest);

        var receipt = await registry.ApplyLegacyIdentifierCleanupAsync(validation);

        receipt.RemovedAliases.Should().Be(1);
        receipt.ClearedIdentifiers.Should().Be(1);
        registry.ResolveToCanonical("OLD-ISIN").Should().BeNull();
        store.LookupSymbol("OLD-ISIN").Found.Should().BeFalse();
        store.GetRegistry().Symbols["CLEANUP"].Identifiers.Should().BeEquivalentTo(new SymbolIdentifiers());
        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        new CanonicalSymbolRegistry(reloaded).ResolveToCanonical("OLD-ISIN").Should().BeNull();
        reloaded.GetRegistry().LegacyIdentifierCleanupReceipts[manifest.CleanupId].Should().BeEquivalentTo(receipt);
    }

    [Fact]
    public async Task Apply_ExactReplayAfterRestart_ReturnsReceiptWithoutWriting_AndRejectsCleanupIdReuse()
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        var manifest = await ManifestAsync(store, Item(id) with
        {
            TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1"
        });
        var validation = await store.ValidateLegacyIdentifierCleanupAsync(manifest);
        var receipt = await store.ApplyLegacyIdentifierCleanupAsync(validation);
        var persisted = await File.ReadAllTextAsync(RegistryPath);
        var reloaded = new SymbolRegistryService(_directory, (_, _, _) => throw new IOException("must not write exact replay"));
        await reloaded.InitializeAsync();

        var replayValidation = await reloaded.ValidateLegacyIdentifierCleanupAsync(manifest);
        var replay = await reloaded.ApplyLegacyIdentifierCleanupAsync(replayValidation);

        replay.Should().BeEquivalentTo(receipt);
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(persisted);
        var changed = manifest with { Items = [Item(id) with { EvidenceReference = "incident:other-review" }] };
        var conflicting = () => reloaded.ValidateLegacyIdentifierCleanupAsync(changed);
        await conflicting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different manifest*");
    }

    [Fact]
    public async Task Apply_WriteFailure_PreservesDiskRegistryAndResolver_ThenRetriesSameValidatedPlan()
    {
        var failWrite = false;
        var store = new SymbolRegistryService(_directory, (path, contents, ct) => failWrite
            ? Task.FromException(new IOException("injected cleanup write failure"))
            : AtomicFileWriter.WriteAsync(path, contents, ct));
        var id = await SeedAsync(store);
        var registry = new CanonicalSymbolRegistry(store);
        var manifest = await ManifestAsync(store, Item(id) with
        {
            TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1"
        });
        var validation = await registry.ValidateLegacyIdentifierCleanupAsync(manifest);
        var before = await File.ReadAllTextAsync(RegistryPath);
        var originalRegistry = store.GetRegistry();
        failWrite = true;

        var apply = () => registry.ApplyLegacyIdentifierCleanupAsync(validation);
        await apply.Should().ThrowAsync<IOException>();

        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        store.GetRegistry().Should().BeSameAs(originalRegistry);
        store.GetRegistry().LegacyIdentifierCleanupReceipts.Should().BeEmpty();
        registry.ResolveToCanonical("OLD-ISIN").Should().Be("CLEANUP");
        store.LookupSymbol("OLD-ISIN").Found.Should().BeTrue();
        failWrite = false;
        await apply();
        registry.ResolveToCanonical("OLD-ISIN").Should().BeNull();
    }

    [Fact]
    public async Task Apply_AnyRegistryChangeInvalidatesDryRun_WithoutPartialCleanup()
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        var manifest = await ManifestAsync(store, Item(id));
        var validation = await store.ValidateLegacyIdentifierCleanupAsync(manifest);
        await store.RegisterSymbolAsync(new SymbolRegistryEntry { Canonical = "UNRELATED" });
        var before = await File.ReadAllTextAsync(RegistryPath);

        var apply = () => store.ApplyLegacyIdentifierCleanupAsync(validation);
        await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*registry changed*");
        var staleDryRun = () => store.ValidateLegacyIdentifierCleanupAsync(manifest);
        await staleDryRun.Should().ThrowAsync<InvalidOperationException>();

        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        store.GetRegistry().LegacyIdentifierCleanupReceipts.Should().BeEmpty();
        store.LookupSymbol("OLD-ISIN").Found.Should().BeTrue();
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("security-master")]
    [InlineData("openfigi")]
    [InlineData("provider-name")]
    public async Task Validate_RefusesExplicitSourceEvenWithEvidence(string source)
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        var manifest = await ManifestAsync(store, Item(id) with { ExpectedAliasSource = source });
        var validate = () => store.ValidateLegacyIdentifierCleanupAsync(manifest);

        await validate.Should().ThrowAsync<ArgumentException>().WithMessage("*explicit operator, provider, or other authority*");
        store.LookupSymbol("OLD-ISIN").Found.Should().BeTrue();
        store.GetRegistry().LegacyIdentifierCleanupReceipts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("security")]
    [InlineData("kind")]
    [InlineData("duplicate")]
    [InlineData("typed-evidence")]
    public async Task Validate_RejectsIncompleteOrDuplicateManifest(string invalidPart)
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        var item = Item(id);
        item = invalidPart switch
        {
            "evidence" => item with { EvidenceReference = " " },
            "security" => item with { SecurityId = Guid.Empty },
            "kind" => item with { IdentifierKind = SecurityIdentifierKind.Ticker },
            "typed-evidence" => item with { TypedIdentifierEvidenceReference = " " },
            _ => item
        };
        var manifest = await ManifestAsync(store, item);
        if (invalidPart == "duplicate")
            manifest = manifest with { Items = [item, item with { IdentifierKind = SecurityIdentifierKind.Figi }] };
        var validate = () => store.ValidateLegacyIdentifierCleanupAsync(manifest);

        await validate.Should().ThrowAsync<ArgumentException>();
        store.LookupSymbol("OLD-ISIN").Found.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("source-case")]
    [InlineData("wrong-security")]
    [InlineData("provider-only")]
    [InlineData("ambiguous")]
    public async Task Validate_RequiresExactProviderlessSelectionForEveryItem_WithoutPartialChanges(string invalidPart)
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        var invalid = Item(id) with { IdentifierValue = "SECOND-IDENTIFIER" };
        if (invalidPart == "source-case")
            invalid = Item(id) with { ExpectedAliasSource = "MANUAL" };
        else if (invalidPart == "wrong-security")
            invalid = Item(Guid.NewGuid());
        else if (invalidPart == "provider-only")
            await store.RegisterSymbolAsync(new SymbolRegistryEntry
            {
                Canonical = "CLEANUP",
                SecurityId = id,
                Aliases = [new() { Alias = invalid.IdentifierValue, Source = "manual", Provider = "feed" }]
            });
        else if (invalidPart == "ambiguous")
        {
            // Old on-disk registries can contain duplicates; validation must refuse ambiguity
            // even before normalization has had an opportunity to collapse them.
            store.GetRegistry().Symbols["CLEANUP"].Aliases.Add(new() { Alias = invalid.IdentifierValue, Source = "manual" });
            store.GetRegistry().Symbols["CLEANUP"].Aliases.Add(new() { Alias = invalid.IdentifierValue, Source = "manual" });
        }
        var manifest = await ManifestAsync(store, Item(id), invalid);
        var before = await File.ReadAllTextAsync(RegistryPath);
        var validate = () => store.ValidateLegacyIdentifierCleanupAsync(manifest);

        await validate.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        store.LookupSymbol("OLD-ISIN").Found.Should().BeTrue();
        store.GetRegistry().LegacyIdentifierCleanupReceipts.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_RefusesManifestOrCountChangesAfterDryRun()
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        var validation = await store.ValidateLegacyIdentifierCleanupAsync(await ManifestAsync(store, Item(id) with
        {
            TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1"
        }));
        var changedManifest = validation with
        {
            Manifest = validation.Manifest with { Items = [Item(id) with { EvidenceReference = "incident:different" }] }
        };
        var manifestApply = () => store.ApplyLegacyIdentifierCleanupAsync(changedManifest);
        await manifestApply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manifest no longer matches*");
        var countApply = () => store.ApplyLegacyIdentifierCleanupAsync(validation with { ClearedIdentifiers = 0 });
        await countApply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*selection no longer matches*");
        store.LookupSymbol("OLD-ISIN").Found.Should().BeTrue();
        store.GetRegistry().LegacyIdentifierCleanupReceipts.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_MultipleSelectedCopies_ClearOneTypedFieldOnce()
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "CLEANUP",
            SecurityId = id,
            Aliases = [new() { Alias = "OLD-ISIN", Source = "isin" }]
        });
        var manifest = await ManifestAsync(store,
            Item(id) with { TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1" },
            Item(id) with { ExpectedAliasSource = "isin" });
        var validation = await store.ValidateLegacyIdentifierCleanupAsync(manifest);

        validation.RemovedAliases.Should().Be(2);
        validation.ClearedIdentifiers.Should().Be(1);
        var receipt = await store.ApplyLegacyIdentifierCleanupAsync(validation);
        receipt.ClearedIdentifiers.Should().Be(1);
        store.LookupSymbol("OLD-ISIN").Found.Should().BeFalse();
    }

    [Theory]
    [InlineData(SecurityIdentifierKind.Isin)]
    [InlineData(SecurityIdentifierKind.Cusip)]
    [InlineData(SecurityIdentifierKind.Figi)]
    [InlineData(SecurityIdentifierKind.Sedol)]
    public async Task Apply_WithoutSeparateTypedEvidence_PreservesMatchingTypedIdentifier(SecurityIdentifierKind kind)
    {
        var store = new SymbolRegistryService(_directory);
        var id = Guid.NewGuid();
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "CLEANUP",
            SecurityId = id,
            Identifiers = Identifiers(kind, "OLD-ISIN"),
            Aliases = [new() { Alias = "OLD-ISIN", Source = "manual" }]
        });
        var registry = new CanonicalSymbolRegistry(store);
        var manifest = await ManifestAsync(store, Item(id) with { IdentifierKind = kind });

        var dryRun = await registry.ValidateLegacyIdentifierCleanupAsync(manifest);
        dryRun.RemovedAliases.Should().Be(1);
        dryRun.ClearedIdentifiers.Should().Be(0, "alias origin does not establish typed-field origin");
        var receipt = await registry.ApplyLegacyIdentifierCleanupAsync(dryRun);

        receipt.ClearedIdentifiers.Should().Be(0);
        store.GetRegistry().Symbols["CLEANUP"].Aliases.Should().BeEmpty();
        store.GetRegistry().Symbols["CLEANUP"].Identifiers.Should().BeEquivalentTo(Identifiers(kind, "OLD-ISIN"));
        registry.ResolveToCanonical("OLD-ISIN").Should().Be("CLEANUP", "independent typed authority remains");
    }

    [Theory]
    [InlineData("operator", null)]
    [InlineData("openfigi", null)]
    [InlineData("registry", null)]
    [InlineData("security-master-identifier", null)]
    [InlineData("manual", "feed")]
    public async Task Validate_TypedClearWithContradictoryAliasAuthority_RefusesAndAliasOnlyCleanupPreservesIt(
        string source,
        string? provider)
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "CLEANUP",
            SecurityId = id,
            Aliases = [new() { Alias = "OLD-ISIN", Source = source, Provider = provider }]
        });
        var item = Item(id) with { TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1" };
        var manifest = await ManifestAsync(store, item);
        var before = await File.ReadAllTextAsync(RegistryPath);

        var validate = () => store.ValidateLegacyIdentifierCleanupAsync(manifest);
        await validate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*authority*");
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        store.GetRegistry().Symbols["CLEANUP"].Identifiers.Isin.Should().Be("OLD-ISIN");

        var aliasOnly = manifest with { Items = [item with { TypedIdentifierEvidenceReference = null }] };
        var dryRun = await store.ValidateLegacyIdentifierCleanupAsync(aliasOnly);
        dryRun.ClearedIdentifiers.Should().Be(0);
        await store.ApplyLegacyIdentifierCleanupAsync(dryRun);
        store.GetRegistry().Symbols["CLEANUP"].Identifiers.Isin.Should().Be("OLD-ISIN");
        store.GetRegistry().Symbols["CLEANUP"].Aliases.Should().ContainSingle().Which.Source.Should().Be(source);
    }

    [Fact]
    public async Task Validate_TypedClearWithMatchingProviderMapping_RefusesWithoutChanges()
    {
        var store = new SymbolRegistryService(_directory);
        var id = await SeedAsync(store);
        await store.AddProviderMappingAsync("CLEANUP", "feed", "OLD-ISIN", SymbolMappingSources.Operator, true);
        var manifest = await ManifestAsync(store, Item(id) with
        {
            TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1"
        });
        var before = await File.ReadAllTextAsync(RegistryPath);

        var validate = () => store.ValidateLegacyIdentifierCleanupAsync(manifest);
        await validate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*authority*");
        (await File.ReadAllTextAsync(RegistryPath)).Should().Be(before);
        store.GetRegistry().Symbols["CLEANUP"].Identifiers.Isin.Should().Be("OLD-ISIN");
    }

    [Fact]
    public async Task Apply_TypedEvidenceDoesNotClearACaseDifferentRetainedValue()
    {
        var store = new SymbolRegistryService(_directory);
        var id = Guid.NewGuid();
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "CLEANUP",
            SecurityId = id,
            Identifiers = new() { Isin = "old-isin" },
            Aliases = [new() { Alias = "OLD-ISIN", Source = "manual" }]
        });
        var manifest = await ManifestAsync(store, Item(id) with
        {
            TypedIdentifierEvidenceReference = "incident:typed-identifier-origin/field-1"
        });

        var dryRun = await store.ValidateLegacyIdentifierCleanupAsync(manifest);
        dryRun.ClearedIdentifiers.Should().Be(0);
        await store.ApplyLegacyIdentifierCleanupAsync(dryRun);
        store.GetRegistry().Symbols["CLEANUP"].Identifiers.Isin.Should().Be("old-isin");
    }

    private static async Task<Guid> SeedAsync(SymbolRegistryService store)
    {
        var id = Guid.NewGuid();
        await store.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "CLEANUP",
            SecurityId = id,
            Identifiers = new() { Isin = "OLD-ISIN" },
            Aliases = [new() { Alias = "OLD-ISIN", Source = "manual" }]
        });
        return id;
    }

    private static SecurityMasterLegacyIdentifierCleanupItem Item(Guid id)
        => new(id, SecurityIdentifierKind.Isin, "OLD-ISIN", "manual", "incident:legacy-seed-review/alias-1");

    private static async Task<SecurityMasterLegacyIdentifierCleanupManifest> ManifestAsync(
        SymbolRegistryService store,
        params SecurityMasterLegacyIdentifierCleanupItem[] items)
        => new("legacy-cleanup-1", await store.GetLegacyIdentifierCleanupFingerprintAsync(), items);

    private static SymbolIdentifiers Identifiers(SecurityIdentifierKind kind, string value) => new()
    {
        Isin = kind == SecurityIdentifierKind.Isin ? value : null,
        Cusip = kind == SecurityIdentifierKind.Cusip ? value : null,
        Figi = kind == SecurityIdentifierKind.Figi ? value : null,
        Sedol = kind == SecurityIdentifierKind.Sedol ? value : null
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
