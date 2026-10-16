using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed class SymbolAliasRepresentativeTests : IDisposable
{
    private static readonly DateTimeOffset RenewedFrom = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RenewedTo = RenewedFrom.AddYears(1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "symbol-alias-representative-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("expired", false)]
    [InlineData("expired", true)]
    [InlineData("disabled", false)]
    [InlineData("disabled", true)]
    [InlineData("future", false)]
    [InlineData("future", true)]
    public async Task Reconcile_RenewedActiveAliasWinsOverInactiveHistory_InEitherInputOrder(string history, bool reverse)
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        await registry.ReconcileSecurityMasterAsync([id], [new CanonicalSymbolDefinition
        {
            Canonical = "RENEWED", SecurityId = id,
            Aliases = [" ALIAS "],
            AliasDefinitions = AliasHistory(history, reverse)
        }]);

        AssertActiveRepresentatives(store, registry);
        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        AssertActiveRepresentatives(reloaded, new CanonicalSymbolRegistry(reloaded));

        await registry.ReconcileSecurityMasterAsync([id], []);
        registry.ResolveToCanonical("ALIAS").Should().BeNull("a normalized rich alias must not acquire an inferred plain-alias source");
        registry.TryResolveWithProvider("VENDOR.VALUE", "feed").Should().BeNull();
        registry.GetAll().Should().BeEmpty();
    }

    [Theory]
    [InlineData("expired", false)]
    [InlineData("expired", true)]
    [InlineData("disabled", false)]
    [InlineData("disabled", true)]
    [InlineData("future", false)]
    [InlineData("future", true)]
    public async Task ImportSymbols_RenewedActiveAliasWinsAfterValueAndProviderNormalization(string history, bool reverse)
    {
        var store = new SymbolRegistryService(_directory);
        var aliases = AliasHistory(history, reverse).Select(alias => new SymbolAlias
        {
            Alias = alias.Alias,
            Source = alias.Source,
            Provider = alias.Provider,
            IsActive = alias.IsActive,
            ValidFrom = alias.ValidFrom?.UtcDateTime,
            ValidTo = alias.ValidTo?.UtcDateTime
        }).ToList();

        await store.ImportSymbolsAsync([new SymbolRegistryEntry { Canonical = "RENEWED", SecurityId = Guid.NewGuid(), Aliases = aliases }]);

        AssertActiveRepresentatives(store, new CanonicalSymbolRegistry(store));
        var reloaded = new SymbolRegistryService(_directory);
        await reloaded.InitializeAsync();
        AssertActiveRepresentatives(reloaded, new CanonicalSymbolRegistry(reloaded));
    }

    [Fact]
    public async Task InactiveRichAlias_SuppressesWhitespaceEquivalentPlainAlias_WithoutInventingActiveAuthority()
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        await registry.RegisterBatchAsync([new CanonicalSymbolDefinition
        {
            Canonical = "RENEWED", Aliases = [" ALIAS "],
            AliasDefinitions = [new("alias", SymbolMappingSources.Operator, IsActive: false)]
        }]);

        registry.ResolveToCanonical("ALIAS").Should().BeNull();
        var alias = store.GetRegistry().Symbols["RENEWED"].Aliases.Should().ContainSingle().Which;
        alias.Source.Should().Be(SymbolMappingSources.Operator);
        alias.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedValue_KeepsIndependentSourceGroupsAndIdentifierOwnership(bool reverse)
    {
        var store = new SymbolRegistryService(_directory);
        var registry = new CanonicalSymbolRegistry(store);
        var id = Guid.NewGuid();
        CanonicalSymbolAliasDefinition[] aliases =
        [
            new("SHARED", SymbolMappingSources.SecurityMaster, IsActive: false),
            new("SHARED", SymbolMappingSources.SecurityMasterIdentifier, IsActive: true),
            new("SHARED", SymbolMappingSources.Operator, IsActive: true)
        ];
        if (reverse)
            Array.Reverse(aliases);
        await registry.ReconcileSecurityMasterAsync([id], [new CanonicalSymbolDefinition
        {
            Canonical = "RENEWED", SecurityId = id, Isin = "SHARED", AliasDefinitions = aliases
        }]);

        store.GetRegistry().Symbols["RENEWED"].Aliases.Should().HaveCount(3);

        await registry.ReconcileSecurityMasterAsync([id], []);

        registry.ResolveToCanonical("SHARED").Should().Be("RENEWED");
        var retained = store.GetRegistry().Symbols["RENEWED"];
        retained.Aliases.Should().ContainSingle().Which.Source.Should().Be(SymbolMappingSources.Operator);
        retained.Identifiers.Isin.Should().BeNull("the Security Master identifier marker survives selection independently of the ordinary alias source");
    }

    [Fact]
    public async Task ActiveTie_ChoosesLatestValidityThenStableExactSpelling_IndependentlyOfOrder()
    {
        static SymbolAlias[] Candidates(bool reverse)
        {
            SymbolAlias[] aliases =
            [
                new() { Alias = "ALIAS", Source = "manual", IsActive = true, ValidFrom = RenewedFrom.AddDays(-1).UtcDateTime, ValidTo = RenewedTo.AddDays(10).UtcDateTime },
                new() { Alias = "ALIAS", Source = "manual", IsActive = true, ValidFrom = RenewedFrom.UtcDateTime, ValidTo = RenewedTo.AddDays(-1).UtcDateTime },
                new() { Alias = "ALIAS", Source = "manual", IsActive = true, ValidFrom = RenewedFrom.UtcDateTime, ValidTo = RenewedTo.UtcDateTime },
                new() { Alias = "alias", Source = "manual", IsActive = true, ValidFrom = RenewedFrom.UtcDateTime, ValidTo = RenewedTo.UtcDateTime }
            ];
            if (reverse)
                Array.Reverse(aliases);
            return aliases;
        }

        var selections = new List<SymbolAlias>();
        foreach (var reverse in new[] { false, true })
        {
            var store = new SymbolRegistryService(Path.Combine(_directory, reverse.ToString()));
            await store.ImportSymbolsAsync([new SymbolRegistryEntry { Canonical = "RENEWED", Aliases = Candidates(reverse).ToList() }]);
            selections.Add(store.GetRegistry().Symbols["RENEWED"].Aliases.Should().ContainSingle().Which);
        }

        foreach (var alias in selections)
        {
            alias.ValidFrom.Should().Be(RenewedFrom.UtcDateTime);
            alias.ValidTo.Should().Be(RenewedTo.UtcDateTime);
        }
        selections[0].Alias.Should().Be(selections[1].Alias);
        selections[0].Source.Should().Be(selections[1].Source);
    }

    private static IReadOnlyList<CanonicalSymbolAliasDefinition> AliasHistory(string history, bool reverse)
    {
        var oldFrom = history switch
        {
            "expired" => RenewedFrom.AddYears(-100),
            "future" => RenewedFrom.AddYears(2),
            _ => RenewedFrom.AddYears(-1)
        };
        DateTimeOffset? oldTo = history == "disabled" ? null : oldFrom.AddMonths(1);
        CanonicalSymbolAliasDefinition[] aliases =
        [
            new(" ALIAS ", SymbolMappingSources.SecurityMaster, ValidFrom: oldFrom, ValidTo: oldTo, IsActive: false),
            new("ALIAS", SymbolMappingSources.SecurityMaster, ValidFrom: RenewedFrom, ValidTo: RenewedTo, IsActive: true),
            new(" VENDOR.VALUE ", SymbolMappingSources.SecurityMaster, " FEED ", oldFrom, oldTo, IsActive: false),
            new("vendor.value", SymbolMappingSources.SecurityMaster, "feed", RenewedFrom, RenewedTo, IsActive: true)
        ];
        if (reverse)
            Array.Reverse(aliases);
        return aliases;
    }

    private static void AssertActiveRepresentatives(SymbolRegistryService store, CanonicalSymbolRegistry registry)
    {
        var aliases = store.GetRegistry().Symbols["RENEWED"].Aliases;
        aliases.Should().HaveCount(2);
        aliases.Should().OnlyContain(alias => alias.IsActive && alias.Source == SymbolMappingSources.SecurityMaster
            && alias.ValidFrom == RenewedFrom.UtcDateTime && alias.ValidTo == RenewedTo.UtcDateTime);
        aliases.Should().ContainSingle(alias => alias.Provider == null);
        aliases.Should().ContainSingle(alias => alias.Provider == "feed");
        registry.ResolveToCanonical("ALIAS").Should().Be("RENEWED", "the supplied active projection remains authoritative without consulting wall-clock time");
        store.LookupSymbol("ALIAS").CanonicalSymbol.Should().Be("RENEWED");
        registry.TryResolveWithProvider("VENDOR.VALUE", "feed").Should().Be("RENEWED");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
