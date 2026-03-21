using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Tests for ConfigurationPresets.ApplyPreset, verifying that each role-based
/// preset sets the expected data source, symbols, and storage configuration.
/// </summary>
public sealed class ConfigurationPresetsTests
{
    private static AppConfig EmptyConfig() => new AppConfig();

    // ─── AvailablePresets / PresetDescriptions ───────────────────────────────

    [Fact]
    public void AvailablePresets_ContainsExpectedNames()
    {
        ConfigurationPresets.AvailablePresets.Should()
            .Contain(new[] { "researcher", "daytrader", "options", "crypto" });
    }

    [Fact]
    public void PresetDescriptions_HasEntryForEachPreset()
    {
        foreach (var preset in ConfigurationPresets.AvailablePresets)
        {
            ConfigurationPresets.PresetDescriptions.Should().ContainKey(preset);
            ConfigurationPresets.PresetDescriptions[preset].Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void ApplyPreset_UnknownPreset_ThrowsArgumentException()
    {
        var act = () => ConfigurationPresets.ApplyPreset("unknown-preset", EmptyConfig());
        act.Should().Throw<ArgumentException>().WithMessage("*unknown-preset*");
    }

    // ─── researcher preset ───────────────────────────────────────────────────

    [Fact]
    public void ApplyPreset_Researcher_SetsAlpacaDataSource()
    {
        var result = ConfigurationPresets.ApplyPreset("researcher", EmptyConfig());
        result.DataSource.Should().Be(DataSourceKind.Alpaca);
    }

    [Fact]
    public void ApplyPreset_Researcher_HasSymbols()
    {
        var result = ConfigurationPresets.ApplyPreset("researcher", EmptyConfig());
        result.Symbols.Should().NotBeNullOrEmpty();
        result.Symbols!.Select(s => s.Symbol).Should().Contain("SPY");
    }

    [Fact]
    public void ApplyPreset_Researcher_EnablesBackfill()
    {
        var result = ConfigurationPresets.ApplyPreset("researcher", EmptyConfig());
        result.Backfill.Should().NotBeNull();
        result.Backfill!.Enabled.Should().BeTrue();
    }

    [Fact]
    public void ApplyPreset_Researcher_EnablesCompression()
    {
        var result = ConfigurationPresets.ApplyPreset("researcher", EmptyConfig());
        result.Compress.Should().BeTrue();
    }

    // ─── daytrader preset ────────────────────────────────────────────────────

    [Fact]
    public void ApplyPreset_DayTrader_SetsAlpacaDataSource()
    {
        var result = ConfigurationPresets.ApplyPreset("daytrader", EmptyConfig());
        result.DataSource.Should().Be(DataSourceKind.Alpaca);
    }

    [Fact]
    public void ApplyPreset_DayTrader_EnablesL2Depth()
    {
        var result = ConfigurationPresets.ApplyPreset("daytrader", EmptyConfig());
        result.Symbols.Should().NotBeNullOrEmpty();
        result.Symbols!.All(s => s.SubscribeDepth == true).Should().BeTrue();
    }

    [Fact]
    public void ApplyPreset_DayTrader_EnablesTrades()
    {
        var result = ConfigurationPresets.ApplyPreset("daytrader", EmptyConfig());
        result.Symbols!.All(s => s.SubscribeTrades == true).Should().BeTrue();
    }

    // ─── options preset ──────────────────────────────────────────────────────

    [Fact]
    public void ApplyPreset_Options_SetsIBDataSource()
    {
        var result = ConfigurationPresets.ApplyPreset("options", EmptyConfig());
        result.DataSource.Should().Be(DataSourceKind.IB);
    }

    [Fact]
    public void ApplyPreset_Options_HasSymbols()
    {
        var result = ConfigurationPresets.ApplyPreset("options", EmptyConfig());
        result.Symbols.Should().NotBeNullOrEmpty();
    }

    // ─── crypto preset ───────────────────────────────────────────────────────

    [Fact]
    public void ApplyPreset_Crypto_SetsAlpacaDataSource()
    {
        var result = ConfigurationPresets.ApplyPreset("crypto", EmptyConfig());
        result.DataSource.Should().Be(DataSourceKind.Alpaca);
    }

    [Fact]
    public void ApplyPreset_Crypto_HasCryptoSymbols()
    {
        var result = ConfigurationPresets.ApplyPreset("crypto", EmptyConfig());
        result.Symbols.Should().NotBeNullOrEmpty();
        var symbols = result.Symbols!.Select(s => s.Symbol).ToList();
        // Crypto preset should include crypto symbols like BTC or ETH
        symbols.Should().Contain(s => s.Contains("BTC") || s.Contains("ETH") || s.Contains("USDT") || s.Contains("USD"));
    }

    // ─── case-insensitivity ──────────────────────────────────────────────────

    [Theory]
    [InlineData("RESEARCHER")]
    [InlineData("Researcher")]
    [InlineData("DAYTRADER")]
    [InlineData("DayTrader")]
    [InlineData("OPTIONS")]
    [InlineData("CRYPTO")]
    public void ApplyPreset_CaseInsensitive_DoesNotThrow(string presetName)
    {
        var act = () => ConfigurationPresets.ApplyPreset(presetName, EmptyConfig());
        act.Should().NotThrow();
    }

    // ─── config preservation ─────────────────────────────────────────────────

    [Fact]
    public void ApplyPreset_PreservesOtherConfigProperties()
    {
        var original = EmptyConfig() with
        {
            DataRoot = "/custom/data"
        };
        var result = ConfigurationPresets.ApplyPreset("researcher", original);
        result.DataRoot.Should().Be("/custom/data");
    }
}
