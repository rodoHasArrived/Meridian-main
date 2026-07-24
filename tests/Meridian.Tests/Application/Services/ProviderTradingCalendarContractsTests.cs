using FluentAssertions;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class ProviderTradingCalendarContractsTests
{
    [Fact]
    public void ProviderCalendarResponse_AcceptsCompleteSharedProvenance()
    {
        var response = new ProviderTradingCalendarResponse(
            Sessions:
            [
                new ProviderTradingSession(
                    new DateOnly(2026, 7, 2),
                    "NYSE",
                    "US",
                    "Regular",
                    new DateTimeOffset(2026, 7, 2, 13, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 2, 20, 0, 0, TimeSpan.Zero))
            ],
            Closures: [],
            Provenance: new ProviderDataProvenance(
                ProviderId: "calendar-vendor",
                ProviderConnectionId: "primary-feed",
                SourceTimestamp: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                ReceiptTimestamp: new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
                Entitlement: "us-equities-calendar",
                Feed: "calendar-v2",
                MarketDataAvailability: "live",
                RequestOrSubscriptionDescriptor: "US:2026-07-02",
                ProviderNativeId: "NYSE-2026-07-02",
                CorrelationId: "calendar-request-42",
                StableDeduplicationKey: "calendar-vendor:US:2026-07-02"));

        response.EnsureProvenanceComplete();
        response.Provenance.ProviderConnectionId.Should().Be("primary-feed");
        ProviderCapabilityKind.TradingCalendar.Should().Be((ProviderCapabilityKind)17);
    }

    [Fact]
    public void ProviderCalendarResponse_RejectsIncompleteProvenance()
    {
        var response = new ProviderTradingCalendarResponse(
            Sessions: [],
            Closures: [],
            Provenance: new ProviderDataProvenance(
                ProviderId: "calendar-vendor",
                ProviderConnectionId: "",
                SourceTimestamp: default,
                ReceiptTimestamp: default,
                Entitlement: "",
                Feed: "",
                MarketDataAvailability: "",
                RequestOrSubscriptionDescriptor: "",
                ProviderNativeId: "",
                CorrelationId: "",
                StableDeduplicationKey: ""));

        var action = response.EnsureProvenanceComplete;

        action.Should().Throw<ArgumentException>();
    }
}
