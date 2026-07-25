using FluentAssertions;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ProviderDataProjectionEndpointsTests
{
    [Fact]
    public void CreateProjection_DeduplicatesLiveRowsAndRetainsAvailabilityAndLineage()
    {
        var service = new ProviderDataReadModelService(
            [new Requests()],
            [new News()],
            [new Calendar()],
            [new Instruments()],
            [new Availability()]);

        var projection = ProviderDataProjectionEndpoints.CreateProjection(service);

        projection.ScannerResults.Should().ContainSingle();
        projection.ScannerResults[0].Provenance.Key.Should().Be("scanner-msft");
        projection.ScannerResults[0].Provenance.Source!.ProviderConnectionId.Should().Be("ib-gateway-1");
        projection.ScannerResults[0].Availability.Entitlement.Should().Be("US equities");
        projection.ScannerResults[0].Availability.ConnectionState.Should().Be("Connected");
        projection.PnlStreams.Should().ContainSingle();
        projection.MarketRules.Should().ContainSingle();
        projection.News.Should().ContainSingle().Which.Provenance.Key.Should().Be("news-results");
        projection.Calendars.Should().ContainSingle().Which.Availability.IsAvailable.Should().BeTrue();
        projection.Instruments.Should().ContainSingle().Which.Provenance.Capability.Should().Be("instrument-discovery");
    }

    private sealed class Requests : IProviderDataReadService
    {
        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() =>
        [
            new(42, "ib", "scanner", ProviderDataRequestStatus.Streaming, new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero), Evidence("request-42"), ScannerResults: [new(1, "MSFT", "NASDAQ", null, null, null, null, null, Evidence("scanner-msft"))], Pnl: new("DU1", null, 1m, 2m, 3m, null, null, Evidence("pnl-du1")), MarketRuleIncrements: [new(0m, .01m, Evidence("rule-0"))]),
            new(42, "ib", "scanner", ProviderDataRequestStatus.Streaming, new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero), Evidence("request-42"), ScannerResults: [new(1, "MSFT", "NASDAQ", null, null, null, null, null, Evidence("scanner-msft"))])
        ];
        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }
    private sealed class Availability : IProviderDataAvailabilityReadService { public IReadOnlyList<ProviderDataAvailability> GetAvailability() => [new("ib", true, "Connected", DateTimeOffset.UtcNow, "US equities", "gateway healthy")]; }
    private sealed class News : IProviderNewsReadService { public string ProviderFamily => "ib"; public IReadOnlyList<ProviderNewsItem> GetNews() => [new("n1", "Results", DateTimeOffset.UtcNow, "MSFT", null, null, Evidence("news-results"))]; public async IAsyncEnumerable<ProviderNewsItem> WatchNewsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; } }
    private sealed class Calendar : IProviderCalendarReadService { public string ProviderFamily => "ib"; public IReadOnlyList<ProviderCalendarEvent> GetCalendarEvents() => [new("c1", "NYSE", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "halt", null, Evidence("calendar-nyse"))]; public async IAsyncEnumerable<ProviderCalendarEvent> WatchCalendarEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; } }
    private sealed class Instruments : IProviderInstrumentDiscoveryReadService { public string ProviderFamily => "ib"; public IReadOnlyList<ProviderInstrumentDiscoveryResult> GetInstruments() => [new("i1", "MSFT", "Microsoft", null, null, Evidence("instrument-msft"))]; public async IAsyncEnumerable<ProviderInstrumentDiscoveryResult> WatchInstrumentsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; } }

    private static ProviderDataProvenance Evidence(string stableKey) => new("ib", "ib-gateway-1", DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, "US equities", "market-data", "real-time", "scanner", stableKey, "correlation-42", stableKey);
}
