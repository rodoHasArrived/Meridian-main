using FluentAssertions;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

public sealed class OptionalProviderCapabilityContractsTests
{
    [Theory]
    [InlineData(ProviderCapabilityKind.TradingCalendar, 17)]
    [InlineData(ProviderCapabilityKind.News, 18)]
    [InlineData(ProviderCapabilityKind.Scanner, 19)]
    [InlineData(ProviderCapabilityKind.PnLStream, 20)]
    [InlineData(ProviderCapabilityKind.MarketRules, 21)]
    [InlineData(ProviderCapabilityKind.InstrumentDiscovery, 22)]
    public void ProviderCapabilityKinds_UsePortableStableValues(ProviderCapabilityKind capability, byte expectedValue)
    {
        ((byte)capability).Should().Be(expectedValue);
    }

    [Theory]
    [MemberData(nameof(OptionalServiceContracts))]
    public void OptionalProviderServices_ExposeProviderNeutralRequestAndOutputContracts(
        Type serviceType,
        string methodName,
        Type requestType,
        Type returnType)
    {
        var method = serviceType.GetMethod(methodName);

        method.Should().NotBeNull();
        method!.GetParameters().Should().ContainSingle(parameter => parameter.ParameterType == requestType);
        method.ReturnType.Should().Be(returnType);
        requestType.Namespace.Should().Be("Meridian.ProviderSdk");
    }

    public static IEnumerable<object[]> OptionalServiceContracts =>
    [
        [typeof(IProviderNewsService), nameof(IProviderNewsService.GetNewsAsync), typeof(ProviderNewsRequest), typeof(Task<IReadOnlyList<ProviderNewsArticle>>)],
        [typeof(IProviderScannerService), nameof(IProviderScannerService.ScanAsync), typeof(ProviderScannerRequest), typeof(Task<IReadOnlyList<ProviderScannerResult>>)],
        [typeof(IProviderPnlStream), nameof(IProviderPnlStream.StreamAsync), typeof(ProviderPnlStreamRequest), typeof(IAsyncEnumerable<ProviderPnlUpdate>)],
        [typeof(ITradingCalendarProvider), nameof(ITradingCalendarProvider.GetTradingCalendarAsync), typeof(ProviderTradingCalendarRequest), typeof(Task<ProviderTradingCalendarResponse>)],
        [typeof(IMarketRuleProvider), nameof(IMarketRuleProvider.GetMarketRuleAsync), typeof(MarketRuleRequest), typeof(Task<ProviderMarketRule>)],
        [typeof(IProviderInstrumentDiscoveryService), nameof(IProviderInstrumentDiscoveryService.DiscoverAsync), typeof(ProviderInstrumentDiscoveryRequest), typeof(Task<IReadOnlyList<ProviderInstrument>>)]
    ];
}
