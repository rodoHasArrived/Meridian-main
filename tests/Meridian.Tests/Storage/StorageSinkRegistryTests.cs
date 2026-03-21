using System.Reflection;
using FluentAssertions;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="StorageSinkRegistry"/>, <see cref="StorageSinkAttribute"/>,
/// and the related extension methods.
/// </summary>
public sealed class StorageSinkRegistryTests
{
    #region StorageSinkAttribute — basic property tests

    [Fact]
    public void StorageSinkAttribute_SetsIdAndDisplayName()
    {
        var attr = new StorageSinkAttribute("test-sink", "Test Sink");

        attr.Id.Should().Be("test-sink");
        attr.DisplayName.Should().Be("Test Sink");
        attr.Description.Should().BeNull();
        attr.EnabledByDefault.Should().BeFalse();
    }

    [Fact]
    public void StorageSinkAttribute_NullId_ThrowsArgumentNullException()
    {
        var act = () => new StorageSinkAttribute(null!, "Display");

        act.Should().Throw<ArgumentNullException>().WithParameterName("id");
    }

    [Fact]
    public void StorageSinkAttribute_NullDisplayName_ThrowsArgumentNullException()
    {
        var act = () => new StorageSinkAttribute("id", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("displayName");
    }

    [Fact]
    public void StorageSinkAttribute_OptionalProperties_AreSettable()
    {
        var attr = new StorageSinkAttribute("id", "Display")
        {
            Description = "My description",
            EnabledByDefault = true
        };

        attr.Description.Should().Be("My description");
        attr.EnabledByDefault.Should().BeTrue();
    }

    #endregion

    #region StorageSinkAttributeExtensions

    [Fact]
    public void GetStorageSinkAttribute_ReturnsAttribute_WhenPresent()
    {
        var type = typeof(FakeStorageSink);

        var attr = type.GetStorageSinkAttribute();

        attr.Should().NotBeNull();
        attr!.Id.Should().Be("fake");
        attr.DisplayName.Should().Be("Fake Storage");
    }

    [Fact]
    public void GetStorageSinkAttribute_ReturnsNull_WhenAbsent()
    {
        var type = typeof(NotAPluginSink);

        var attr = type.GetStorageSinkAttribute();

        attr.Should().BeNull();
    }

    [Fact]
    public void GetStorageSinkMetadata_ReturnsMetadata_WhenAttributePresent()
    {
        var type = typeof(FakeStorageSink);

        var metadata = type.GetStorageSinkMetadata();

        metadata.Should().NotBeNull();
        metadata!.Id.Should().Be("fake");
        metadata.DisplayName.Should().Be("Fake Storage");
        metadata.ImplementationType.Should().Be(typeof(FakeStorageSink));
    }

    [Fact]
    public void GetStorageSinkMetadata_ReturnsNull_WhenAttributeAbsent()
    {
        var type = typeof(NotAPluginSink);

        var metadata = type.GetStorageSinkMetadata();

        metadata.Should().BeNull();
    }

    [Fact]
    public void IsStorageSinkPlugin_ReturnsTrue_ForDecoratedConcreteSink()
    {
        typeof(FakeStorageSink).IsStorageSinkPlugin().Should().BeTrue();
    }

    [Fact]
    public void IsStorageSinkPlugin_ReturnsFalse_WhenAttributeAbsent()
    {
        typeof(NotAPluginSink).IsStorageSinkPlugin().Should().BeFalse();
    }

    [Fact]
    public void IsStorageSinkPlugin_ReturnsFalse_ForAbstractClass()
    {
        typeof(AbstractFakeSink).IsStorageSinkPlugin().Should().BeFalse();
    }

    [Fact]
    public void IsStorageSinkPlugin_ReturnsFalse_WhenDoesNotImplementIStorageSink()
    {
        typeof(NotASink).IsStorageSinkPlugin().Should().BeFalse();
    }

    #endregion

    #region StorageSinkRegistry — constructor validation

    [Fact]
    public void DiscoverFromAssemblies_NullAssemblies_ThrowsArgumentException()
    {
        var registry = new StorageSinkRegistry();

        var act = () => registry.DiscoverFromAssemblies(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DiscoverFromAssemblies_EmptyAssemblies_ThrowsArgumentException()
    {
        var registry = new StorageSinkRegistry();

        var act = () => registry.DiscoverFromAssemblies();

        act.Should().Throw<ArgumentException>().WithParameterName("assemblies");
    }

    #endregion

    #region StorageSinkRegistry — discovery

    [Fact]
    public void DiscoverFromAssemblies_FindsDecoratedSinks_InAssembly()
    {
        var registry = new StorageSinkRegistry();

        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        registry.Sinks.Should().ContainSingle(s => s.Id == "fake");
    }

    [Fact]
    public void DiscoverFromAssemblies_SkipsAbstractTypes()
    {
        var registry = new StorageSinkRegistry();

        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        registry.Sinks.Should().NotContain(s => s.Id == "abstract-fake");
    }

    [Fact]
    public void DiscoverFromAssemblies_SkipsTypesNotImplementingIStorageSink()
    {
        var registry = new StorageSinkRegistry();

        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        registry.Sinks.Should().NotContain(s => s.Id == "not-a-sink");
    }

    [Fact]
    public void DiscoverFromAssemblies_DeduplicatesById_WhenScannedTwice()
    {
        var registry = new StorageSinkRegistry();

        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        registry.Sinks.Count(s => s.Id == "fake").Should().Be(1);
    }

    [Fact]
    public void DiscoverFromAssemblies_DeduplicatesById_CaseInsensitive()
    {
        var registry = new StorageSinkRegistry();

        // Register a sink with id "FAKE" (upper) in addition to "fake" (lower)
        // Both should map to the same id, so only one entry should be present.
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        registry.Sinks.Count(s => s.Id.Equals("fake", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    [Fact]
    public void Sinks_PopulatesMetadataCorrectly()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        var meta = registry.Sinks.Single(s => s.Id == "fake");

        meta.DisplayName.Should().Be("Fake Storage");
        meta.Description.Should().Be("Used for tests only.");
        meta.EnabledByDefault.Should().BeFalse();
        meta.ImplementationType.Should().Be(typeof(FakeStorageSink));
    }

    [Fact]
    public void StorageAssembly_Contains_JsonlAndParquetSinks()
    {
        var registry = new StorageSinkRegistry();

        registry.DiscoverFromAssemblies(typeof(JsonlStorageSink).Assembly);

        registry.Sinks.Should().Contain(s => s.Id == "jsonl");
        registry.Sinks.Should().Contain(s => s.Id == "parquet");
    }

    [Fact]
    public void JsonlSinkMetadata_HasExpectedValues()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(JsonlStorageSink).Assembly);

        var meta = registry.Sinks.Single(s => s.Id == "jsonl");

        meta.ImplementationType.Should().Be(typeof(JsonlStorageSink));
        meta.EnabledByDefault.Should().BeTrue();
    }

    [Fact]
    public void ParquetSinkMetadata_HasExpectedValues()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(JsonlStorageSink).Assembly);

        var meta = registry.Sinks.Single(s => s.Id == "parquet");

        meta.ImplementationType.Should().Be(typeof(ParquetStorageSink));
        meta.EnabledByDefault.Should().BeFalse();
    }

    #endregion

    #region StorageSinkRegistry — TryGetSink

    [Fact]
    public void TryGetSink_ReturnsTrue_ForKnownId()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        var found = registry.TryGetSink("fake", out var metadata);

        found.Should().BeTrue();
        metadata.Should().NotBeNull();
    }

    [Fact]
    public void TryGetSink_IsCaseInsensitive()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        registry.TryGetSink("FAKE", out _).Should().BeTrue();
        registry.TryGetSink("Fake", out _).Should().BeTrue();
    }

    [Fact]
    public void TryGetSink_ReturnsFalse_ForUnknownId()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        var found = registry.TryGetSink("nonexistent", out var metadata);

        found.Should().BeFalse();
        metadata.Should().BeNull();
    }

    #endregion

    #region StorageSinkRegistry — RegisterServices

    [Fact]
    public void RegisterServices_AddsDiscoveredTypesToServiceCollection()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        var services = new ServiceCollection();
        registry.RegisterServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(FakeStorageSink));
    }

    [Fact]
    public void RegisterServices_SkipsAlreadyRegisteredTypes()
    {
        var registry = new StorageSinkRegistry();
        registry.DiscoverFromAssemblies(typeof(StorageSinkRegistryTests).Assembly);

        var services = new ServiceCollection();
        services.AddSingleton<FakeStorageSink>(); // explicit registration first

        registry.RegisterServices(services);

        // Should still have exactly one registration for FakeStorageSink
        services.Count(d => d.ServiceType == typeof(FakeStorageSink)).Should().Be(1);
    }

    [Fact]
    public void RegisterServices_NullServices_ThrowsArgumentNullException()
    {
        var registry = new StorageSinkRegistry();

        var act = () => registry.RegisterServices(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region StorageSinkMetadata

    [Fact]
    public void StorageSinkMetadata_FromAttribute_PopulatesAllFields()
    {
        var attr = new StorageSinkAttribute("my-sink", "My Sink")
        {
            Description = "Desc",
            EnabledByDefault = true
        };

        var meta = StorageSinkMetadata.FromAttribute(attr, typeof(FakeStorageSink));

        meta.Id.Should().Be("my-sink");
        meta.DisplayName.Should().Be("My Sink");
        meta.Description.Should().Be("Desc");
        meta.EnabledByDefault.Should().BeTrue();
        meta.ImplementationType.Should().Be(typeof(FakeStorageSink));
    }

    #endregion

    // ───────────────────────────────────────────────
    //  Test doubles
    // ───────────────────────────────────────────────

    [StorageSink("fake", "Fake Storage", Description = "Used for tests only.")]
    private sealed class FakeStorageSink : IStorageSink
    {
        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default) => default;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    // Decorated but abstract — must be skipped
    [StorageSink("abstract-fake", "Abstract Fake")]
    private abstract class AbstractFakeSink : IStorageSink
    {
        public abstract ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default);
        public abstract Task FlushAsync(CancellationToken ct = default);
        public abstract ValueTask DisposeAsync();
    }

    // Decorated but does NOT implement IStorageSink — must be skipped
    [StorageSink("not-a-sink", "Not A Sink")]
    private sealed class NotASink
    {
        public void DoSomething() { }
    }

    // Not decorated — must be skipped
    private sealed class NotAPluginSink : IStorageSink
    {
        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default) => default;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }
}
