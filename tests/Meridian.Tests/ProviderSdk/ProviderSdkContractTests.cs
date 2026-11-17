using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

/// <summary>
/// Direct coverage of ProviderSdk helpers that carry behaviour rather than data: granularity
/// parsing shared by the CLI, endpoints and checkpoint keys; the per-capability safety defaults
/// that decide whether a provider may fail over; and the shared HTTP header helpers.
/// </summary>
public sealed class ProviderSdkContractTests
{
    [Theory]
    [InlineData("1m", DataGranularity.Minute1)]
    [InlineData("1-min", DataGranularity.Minute1)]
    [InlineData(" 5min ", DataGranularity.Minute5)]
    [InlineData("minute_15", DataGranularity.Minute15)]
    [InlineData("30M", DataGranularity.Minute30)]
    [InlineData("Hourly", DataGranularity.Hour1)]
    [InlineData("4h", DataGranularity.Hour4)]
    [InlineData("1d", DataGranularity.Daily)]
    [InlineData("WEEKLY", DataGranularity.Weekly)]
    [InlineData("1month", DataGranularity.Monthly)]
    [InlineData("Minute5", DataGranularity.Minute5)]
    public void TryParseValue_AcceptsAliasesAndMemberNames(string text, DataGranularity expected)
    {
        DataGranularityExtensions.TryParseValue(text, out var granularity).Should().BeTrue();
        granularity.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fortnightly")]
    [InlineData("1")]
    [InlineData("42")]
    [InlineData("-1")]
    public void TryParseValue_RefusesUnknownAndNumericText(string? text)
    {
        // An integer string used to parse as whatever enum value it named, including undefined
        // ones: "1" silently meant Minute5 and "42" produced a granularity with no definition.
        DataGranularityExtensions.TryParseValue(text, out _).Should().BeFalse();
        DataGranularityExtensions.ParseValueOrDefault(text, DataGranularity.Hour4).Should().Be(DataGranularity.Hour4);
    }

    [Fact]
    public void EveryGranularity_RoundTripsThroughItsConfigAndUiSpellings()
    {
        foreach (var granularity in Enum.GetValues<DataGranularity>())
        {
            DataGranularityExtensions.TryParseValue(granularity.ToConfigValue(), out var fromConfig).Should().BeTrue();
            fromConfig.Should().Be(granularity, $"{granularity} must survive its config spelling");

            // Checkpoint keys are written with ToUiValue and read back with TryParseValue.
            DataGranularityExtensions.TryParseValue(granularity.ToUiValue(), out var fromUi).Should().BeTrue();
            fromUi.Should().Be(granularity, $"{granularity} must survive its UI spelling");
        }
    }

    [Fact]
    public void Intraday_AndDurations_AreConsistent()
    {
        foreach (var granularity in Enum.GetValues<DataGranularity>())
        {
            granularity.IsIntraday().Should().Be(granularity.ToTimeSpan() < TimeSpan.FromDays(1),
                $"{granularity} is intraday exactly when its bar is shorter than a day");
        }

        DataGranularity.Minute15.ToTimeSpan().Should().Be(TimeSpan.FromMinutes(15));
        DataGranularity.Hour4.ToAlpacaTimeframe().Should().Be("4Hour");
        DataGranularity.Minute1.ToStorageFilePrefix().Should().NotBe(DataGranularity.Minute5.ToStorageFilePrefix());
    }

    [Theory]
    [InlineData(ProviderCapabilityKind.OrderExecution, true)]
    [InlineData(ProviderCapabilityKind.BankStatements, true)]
    [InlineData(ProviderCapabilityKind.ExecutionHistory, false)]
    [InlineData(ProviderCapabilityKind.AccountBalances, false)]
    [InlineData(ProviderCapabilityKind.AccountPositions, false)]
    [InlineData(ProviderCapabilityKind.CashTransactions, false)]
    [InlineData(ProviderCapabilityKind.PnLStream, false)]
    public void AccountAndExecutionCapabilities_NeverFailOverAutomatically(ProviderCapabilityKind capability, bool productionReady)
    {
        // Silently routing orders or account state to a different connection would act on, or
        // report, the wrong account.
        var policy = ProviderSafetyPolicy.DefaultFor(capability);

        policy.Mode.Should().Be(ProviderSafetyMode.NoAutomaticFailover);
        policy.RequireExplicitBinding.Should().BeTrue();
        policy.RequireProductionReady.Should().Be(productionReady);
    }

    [Fact]
    public void ReconciliationFeeds_StayWithinOneInstitution_AndMarketDataFailsOverOnHealth()
    {
        var reconciliation = ProviderSafetyPolicy.DefaultFor(ProviderCapabilityKind.ReconciliationFeed);
        var marketData = ProviderSafetyPolicy.DefaultFor(ProviderCapabilityKind.RealtimeMarketData);

        reconciliation.Mode.Should().Be(ProviderSafetyMode.SameInstitutionOnly);
        reconciliation.RequireExplicitBinding.Should().BeTrue();
        marketData.Mode.Should().Be(ProviderSafetyMode.HealthAwareFailover);
        marketData.RequireExplicitBinding.Should().BeFalse();
    }

    [Fact]
    public void HttpHelpers_SetJsonGzipAndUserAgent_AndReplaceOrSkipTheApiKey()
    {
        using var client = new HttpClient();
        ProviderHttpUtilities.ConfigureDefaultHeaders(client, "Meridian-Test/1.0");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/");

        ProviderHttpUtilities.AddApiKeyHeader(request, "X-Api-Key", "first");
        ProviderHttpUtilities.AddApiKeyHeader(request, "X-Api-Key", "second");
        ProviderHttpUtilities.AddApiKeyHeader(request, "X-Other-Key", "   ");

        client.DefaultRequestHeaders.Accept.Should().ContainSingle(header => header.MediaType == "application/json");
        client.DefaultRequestHeaders.AcceptEncoding.Should().Contain(header => header.Value == "gzip");
        client.DefaultRequestHeaders.UserAgent.ToString().Should().Contain("Meridian-Test/1.0");
        request.Headers.GetValues("X-Api-Key").Should().Equal("second");
        request.Headers.Contains("X-Other-Key").Should().BeFalse("a blank key is not sent");
    }
}
