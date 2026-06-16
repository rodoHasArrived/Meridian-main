using FluentAssertions;
using Meridian.Modularity;

namespace Meridian.Tests.Modularity;

/// <summary>
/// Covers the keyed-catalog semantics of <see cref="ExtensionRegistry{TContract}"/> and the
/// reflection helpers in <see cref="ExtensionDiscovery"/> — the Wave 2 primitives that let the
/// hard-coded catalogs become "built-ins plus discoverable additions".
/// </summary>
public sealed class ExtensionRegistryTests
{
    private sealed record Template(string Id, string Name);

    private static ExtensionRegistry<Template> NewRegistry()
        => new(static t => t.Id);

    [Fact]
    public void Add_RegistersAndPreservesInsertionOrder()
    {
        var registry = NewRegistry()
            .Add(new Template("b", "Beta"))
            .Add(new Template("a", "Alpha"));

        registry.Count.Should().Be(2);
        registry.Entries.Select(t => t.Id).Should().Equal("b", "a");
    }

    [Fact]
    public void Add_Throws_OnDuplicateKey_CaseInsensitiveByDefault()
    {
        var registry = NewRegistry().Add(new Template("dup", "first"));

        var act = () => registry.Add(new Template("DUP", "second"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*key 'DUP' is already registered*");
    }

    [Fact]
    public void TryAdd_ReturnsFalse_OnDuplicate_AndKeepsFirst()
    {
        var registry = NewRegistry().Add(new Template("k", "first"));

        registry.TryAdd(new Template("k", "second")).Should().BeFalse();
        registry.Get("k").Name.Should().Be("first");
    }

    [Fact]
    public void Get_Throws_WhenMissing_TryGet_ReturnsFalse()
    {
        var registry = NewRegistry();

        registry.Invoking(r => r.Get("nope")).Should().Throw<KeyNotFoundException>();
        registry.TryGet("nope", out _).Should().BeFalse();
    }

    [Fact]
    public void Add_Throws_OnEmptyKey()
    {
        var registry = NewRegistry();

        var act = () => registry.Add(new Template("  ", "blank"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*null or empty key*");
    }

    // ---- ExtensionDiscovery ----

    private interface ISampleExtension { }
    private sealed class ConcreteExtension : ISampleExtension { }
    private abstract class AbstractExtension : ISampleExtension { }

    [Fact]
    public void FindImplementations_ReturnsConcreteTypesOnly()
    {
        var found = ExtensionDiscovery
            .FindImplementations<ISampleExtension>(typeof(ExtensionRegistryTests).Assembly)
            .ToList();

        found.Should().Contain(typeof(ConcreteExtension));
        found.Should().NotContain(typeof(AbstractExtension));
        found.Should().NotContain(typeof(ISampleExtension));
    }
}
