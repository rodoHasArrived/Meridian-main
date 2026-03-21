using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Concrete test implementation of ConfigServiceBase for testing.
/// </summary>
internal sealed class TestConfigService : ConfigServiceBase
{
    public AppConfigDto? StoredConfig { get; set; }
    public string? LastErrorMessage { get; private set; }
    public int LoadCount { get; private set; }
    public int SaveCount { get; private set; }

    public override string ConfigPath => "/test/config/appsettings.json";

    protected override Task<AppConfigDto?> LoadConfigCoreAsync(CancellationToken ct = default)
    {
        LoadCount++;
        return Task.FromResult(StoredConfig);
    }

    protected override Task SaveConfigCoreAsync(AppConfigDto config, CancellationToken ct = default)
    {
        SaveCount++;
        StoredConfig = config;
        return Task.CompletedTask;
    }

    protected override void LogError(string message, Exception? exception = null)
    {
        LastErrorMessage = message;
    }
}

public sealed class ConfigServiceBaseTests
{
    private readonly TestConfigService _sut = new();

    [Fact]
    public async Task ValidateConfigDetailAsync_ReturnsValid_WhenConfigIsEmpty()
    {
        _sut.StoredConfig = new AppConfigDto();

        var result = await _sut.ValidateConfigDetailAsync();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateConfigDetailAsync_ReturnsError_WhenBackfillEnabledButAllProvidersDisabled()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Backfill = new BackfillConfigDto
            {
                Enabled = true,
                Providers = new BackfillProvidersConfigDto
                {
                    Alpaca = new BackfillProviderOptionsDto { Enabled = false },
                    Polygon = new BackfillProviderOptionsDto { Enabled = false }
                }
            }
        };

        var result = await _sut.ValidateConfigDetailAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("all historical providers are disabled"));
    }

    [Fact]
    public async Task ValidateConfigDetailAsync_ReturnsWarning_WhenDuplicatePriorities()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Backfill = new BackfillConfigDto
            {
                Enabled = true,
                Providers = new BackfillProvidersConfigDto
                {
                    Alpaca = new BackfillProviderOptionsDto { Enabled = true, Priority = 1 },
                    Polygon = new BackfillProviderOptionsDto { Enabled = true, Priority = 1 }
                }
            }
        };

        var result = await _sut.ValidateConfigDetailAsync();

        result.Warnings.Should().Contain(w => w.Contains("share priority 1"));
    }

    [Fact]
    public async Task ValidateConfigDetailAsync_ReturnsError_WhenNegativePriority()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Backfill = new BackfillConfigDto
            {
                Providers = new BackfillProvidersConfigDto
                {
                    Alpaca = new BackfillProviderOptionsDto { Priority = -1 }
                }
            }
        };

        var result = await _sut.ValidateConfigDetailAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("invalid priority"));
    }

    [Fact]
    public async Task AddSymbolAsync_AddsNewSymbol()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Symbols = new[] { new SymbolConfigDto { Symbol = "AAPL" } }
        };

        await _sut.AddSymbolAsync(new SymbolConfigDto { Symbol = "MSFT" });

        _sut.StoredConfig!.Symbols.Should().HaveCount(2);
        _sut.StoredConfig.Symbols.Should().Contain(s => s.Symbol == "MSFT");
    }

    [Fact]
    public async Task AddSymbolAsync_DoesNotAddDuplicate()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Symbols = new[] { new SymbolConfigDto { Symbol = "AAPL" } }
        };

        await _sut.AddSymbolAsync(new SymbolConfigDto { Symbol = "AAPL" });

        _sut.StoredConfig!.Symbols.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveSymbolAsync_RemovesExistingSymbol()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Symbols = new[]
            {
                new SymbolConfigDto { Symbol = "AAPL" },
                new SymbolConfigDto { Symbol = "MSFT" }
            }
        };

        await _sut.RemoveSymbolAsync("AAPL");

        _sut.StoredConfig!.Symbols.Should().HaveCount(1);
        _sut.StoredConfig.Symbols.Should().NotContain(s => s.Symbol == "AAPL");
    }

    [Fact]
    public async Task GetBackfillProviderOptionsAsync_ReturnsOptions_ForKnownProvider()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Backfill = new BackfillConfigDto
            {
                Providers = new BackfillProvidersConfigDto
                {
                    Alpaca = new BackfillProviderOptionsDto { Enabled = true, Priority = 1 }
                }
            }
        };

        var options = await _sut.GetBackfillProviderOptionsAsync("alpaca");

        options.Should().NotBeNull();
        options!.Enabled.Should().BeTrue();
        options.Priority.Should().Be(1);
    }

    [Fact]
    public async Task GetBackfillProviderOptionsAsync_ThrowsForUnknownProvider()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Backfill = new BackfillConfigDto { Providers = new BackfillProvidersConfigDto() }
        };

        var act = async () => await _sut.GetBackfillProviderOptionsAsync("unknown");

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SetBackfillProviderOptionsAsync_StoresOptions()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Backfill = new BackfillConfigDto { Providers = new BackfillProvidersConfigDto() }
        };

        var options = new BackfillProviderOptionsDto { Enabled = true, Priority = 5, RateLimitPerMinute = 100, RateLimitPerHour = 5000 };
        await _sut.SetBackfillProviderOptionsAsync("polygon", options);

        _sut.StoredConfig!.Backfill!.Providers!.Polygon.Should().NotBeNull();
        _sut.StoredConfig.Backfill.Providers.Polygon!.Priority.Should().Be(5);
        _sut.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task SetBackfillProviderOptionsAsync_ThrowsForInvalidOptions()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            Backfill = new BackfillConfigDto { Providers = new BackfillProvidersConfigDto() }
        };

        var options = new BackfillProviderOptionsDto { Priority = -1 };
        var act = async () => await _sut.SetBackfillProviderOptionsAsync("alpaca", options);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NormalizeProviderId_NormalizesYahoo()
    {
        ConfigServiceBase.NormalizeProviderId("YahooFinance").Should().Be("yahoo");
        ConfigServiceBase.NormalizeProviderId("YAHOO").Should().Be("yahoo");
    }

    [Fact]
    public void NormalizeProviderId_NormalizesNasdaq()
    {
        ConfigServiceBase.NormalizeProviderId("nasdaq").Should().Be("nasdaqdatalink");
    }

    [Fact]
    public async Task DeleteDataSourceAsync_RemovesSourceById()
    {
        _sut.StoredConfig = new AppConfigDto
        {
            DataSources = new DataSourcesConfigDto
            {
                Sources = new[]
                {
                    new DataSourceConfigDto { Id = "src1" },
                    new DataSourceConfigDto { Id = "src2" }
                }
            }
        };

        await _sut.DeleteDataSourceAsync("src1");

        _sut.StoredConfig!.DataSources!.Sources.Should().HaveCount(1);
    }
}
