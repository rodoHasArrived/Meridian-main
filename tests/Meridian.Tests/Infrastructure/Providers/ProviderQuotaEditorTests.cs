using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderQuotaEditorTests
{
    [Fact]
    public void PaidPreset_UpdatesQuotaAndPacingWithoutGrantingEntitlements()
    {
        var current = new AppConfig();
        var result = ProviderQuotaCatalog.Apply(current, "twelvedata", new("grow", 55, 3500, 30));
        var options = result.ProviderQuotas!.TwelveData!;
        var pacing = BackfillProviderRuntimeOptions.FromConfig(result.Backfill!.Providers, "twelvedata");

        Assert.Equal(new ProviderQuotaSettingsDto("grow", 55, 3500, 30), ProviderQuotaCatalog.Read(result, "twelvedata"));
        Assert.False(options.FundamentalsEntitled);
        Assert.False(options.NoDailyLimit);
        var budget = ProviderQuotaBudgets.TwelveData(options, pacing);
        Assert.Equal(55, budget.Maximum);
        Assert.Equal(3500, budget.DailyMaximum);
        Assert.Equal(TimeSpan.FromSeconds(2), budget.MinimumDelay);
        Assert.Equal(DailyQuotaResetMode.UtcMidnight, budget.DailyReset);
        Assert.Same(result, ProviderQuotaCatalog.Apply(result, "twelvedata", new("grow", 55, 3500, 30)));
    }

    [Fact]
    public void AlphaVantagePreset_DoesNotEnableAnUnconfiguredProvider()
    {
        var result = ProviderQuotaCatalog.Apply(new AppConfig(), "alphavantage", new("premium-75", 75, 20_000, 20));

        Assert.False(result.Backfill!.Providers!.AlphaVantage!.Enabled);
        Assert.Equal("premium-75", result.Backfill.Providers.AlphaVantage.TierId);
        Assert.Equal(20, BackfillProviderRuntimeOptions.FromConfig(result.Backfill.Providers, "alphavantage").Requests);
        Assert.Equal(DailyQuotaResetMode.Rolling24Hours, ProviderQuotaBudgets.AlphaVantage(result.ProviderQuotas!.AlphaVantage).DailyReset);
    }

    [Theory]
    [InlineData("grow", 55, null, 30)]
    [InlineData("grow", 56, 1000, 30)]
    [InlineData("grow", 55, 79_201, 30)]
    [InlineData("grow", 55, 2000, 56)]
    [InlineData("free", 8, 801, 8)]
    [InlineData("free", 8, 800, 0)]
    public void InvalidEdits_AreRejected(string tier, int minute, int? daily, int requestsPerMinute)
        => Assert.Throws<ArgumentException>(() =>
            ProviderQuotaCatalog.Apply(new AppConfig(), "twelvedata", new(tier, minute, daily, requestsPerMinute)));

    [Fact]
    public void BuiltAdapterShowsBootLimitsWhileTheSavedPresetWaitsForRestart()
    {
        var before = new AppConfig { ProviderQuotas = new(TwelveData: new(20, NoDailyLimit: true)) };
        var after = ProviderQuotaCatalog.Apply(before, "twelvedata", new("grow", 55, 4000, 30));
        var pending = new ConfigurationFieldChange("providerQuotas.twelveData.dailyCredits", ConfigurationChangeImpact.RestartHost);
        var changes = new ConfigurationChangeStatus(ConfigurationChangeImpact.RestartHost, [pending], [pending], DateTimeOffset.UtcNow);
        using var registry = new RateLimiterRegistry();
        using var adapter = new TwelveDataHistoricalDataProvider("synthetic-key", rateLimiterRegistry: registry,
            quotaConfig: before.ProviderQuotas!.TwelveData);

        Assert.Null(ProviderQuotaCatalog.Read(before, "twelvedata").TierId);
        var response = ProviderQuotaEndpoints.BuildResponse("twelvedata", after, changes, [adapter], canEdit: true)!;
        Assert.Equal(55, response.Saved!.UnitsPerMinute);
        Assert.Equal(4000, response.Saved.DailyLimit);
        Assert.Equal(20, response.Running!.MaximumPerWindow);
        Assert.Null(response.Running.DailyLimit);
        Assert.True(response.PendingRestart);
        Assert.True(response.CanEdit);
    }

    [Fact]
    public void FredUsesTheAdapterLimitEvenWhenLegacyConfigDeclaresADifferentValue()
    {
        var config = new AppConfig(Backfill: new(Providers: new(Fred: new(RateLimitPerMinute: 7))));
        var changes = new ConfigurationChangeStatus(ConfigurationChangeImpact.Live, [], [], null);
        using var adapter = new FredHistoricalDataProvider("synthetic-key");
        var response = ProviderQuotaEndpoints.BuildResponse("fred", config, changes, [adapter], canEdit: true)!;

        Assert.False(response.CanEdit);
        Assert.Null(response.Saved);
        Assert.Equal("adapter-fixed", response.Policy);
        Assert.Equal(adapter.MaxRequestsPerWindow, response.Running!.MaximumPerWindow);
        Assert.NotEqual(7, response.Running.MaximumPerWindow);
    }

    [Fact]
    public void QuotaMutationContractRefusesCredentialFields()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProviderQuotaEditDto>(
            """{"tierId":"grow","unitsPerMinute":55,"dailyLimit":2000,"requestsPerMinute":30,"apiKey":"synthetic-key"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
