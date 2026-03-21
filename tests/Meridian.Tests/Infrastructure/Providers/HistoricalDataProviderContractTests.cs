using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Abstract base class for verifying that every <see cref="IHistoricalDataProvider"/> implementation
/// satisfies the common behavioral contract defined in ADR-001.
/// </summary>
/// <typeparam name="TProvider">The concrete provider under test.</typeparam>
/// <remarks>
/// Inherit from this class in a per-provider test class and implement
/// <see cref="CreateProvider"/> to supply a testable instance:
/// <code>
/// public sealed class StooqContractTests : HistoricalDataProviderContractTests&lt;StooqHistoricalDataProvider&gt;
/// {
///     protected override StooqHistoricalDataProvider CreateProvider() =&gt; new();
/// }
/// </code>
/// The suite runs automatically for every derived class, ensuring uniform coverage.
/// </remarks>
public abstract class HistoricalDataProviderContractTests<TProvider>
    where TProvider : IHistoricalDataProvider
{
    /// <summary>
    /// Creates and returns a new, testable instance of the provider under test.
    /// The instance must be in a usable state (credentials are not required for
    /// contract tests; use stub / no-op credentials or pass null where allowed).
    /// </summary>
    protected abstract TProvider CreateProvider();

    // ------------------------------------------------------------------ //
    //  Identity & metadata contract                                        //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Name_IsNotNullOrWhiteSpace()
    {
        using var provider = CreateProvider();
        provider.Name.Should().NotBeNullOrWhiteSpace(
            "every provider must declare a unique machine-readable identifier");
    }

    [Fact]
    public void DisplayName_IsNotNullOrWhiteSpace()
    {
        using var provider = CreateProvider();
        provider.DisplayName.Should().NotBeNullOrWhiteSpace(
            "every provider must expose a human-readable display name");
    }

    [Fact]
    public void Description_IsNotNullOrWhiteSpace()
    {
        using var provider = CreateProvider();
        provider.Description.Should().NotBeNullOrWhiteSpace(
            "every provider must include a description of its data coverage");
    }

    [Fact]
    public void Priority_IsPositive()
    {
        using var provider = CreateProvider();
        provider.Priority.Should().BePositive(
            "provider priority must be a positive integer used for routing order");
    }

    [Fact]
    public void IProviderMetadata_ProviderId_MatchesName()
    {
        using var provider = CreateProvider();
        var meta = (Meridian.Infrastructure.Adapters.Core.IProviderMetadata)provider;
        meta.ProviderId.Should().Be(provider.Name,
            "IProviderMetadata.ProviderId must delegate to Name for historical providers");
    }

    [Fact]
    public void IProviderMetadata_DisplayName_MatchesDisplayName()
    {
        using var provider = CreateProvider();
        var meta = (Meridian.Infrastructure.Adapters.Core.IProviderMetadata)provider;
        meta.ProviderDisplayName.Should().Be(provider.DisplayName,
            "IProviderMetadata.ProviderDisplayName must delegate to DisplayName");
    }

    // ------------------------------------------------------------------ //
    //  Capability contract                                                 //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Capabilities_IsNotNull()
    {
        using var provider = CreateProvider();
        // Capabilities is a struct so can never be null, but the property must not throw.
        var act = () => _ = provider.Capabilities;
        act.Should().NotThrow("accessing Capabilities must never throw");
    }

    [Fact]
    public void SupportedMarkets_IsNotNull()
    {
        using var provider = CreateProvider();
        provider.SupportedMarkets.Should().NotBeNull(
            "SupportedMarkets must return an empty list rather than null when no markets are specified");
    }

    // ------------------------------------------------------------------ //
    //  Health check contract                                               //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task IsAvailableAsync_DoesNotThrow()
    {
        using var provider = CreateProvider();
        // IsAvailableAsync should never throw regardless of network state.
        // The default implementation returns true without I/O.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var act = async () => await provider.IsAvailableAsync(cts.Token);
        await act.Should().NotThrowAsync(
            "IsAvailableAsync must not propagate exceptions; it must return false on failure");
    }

    // ------------------------------------------------------------------ //
    //  Disposal contract                                                   //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var provider = CreateProvider();

        var act = () =>
        {
            provider.Dispose();
            provider.Dispose();
        };

        act.Should().NotThrow("IDisposable.Dispose must be idempotent per the BCL contract");
    }
}
