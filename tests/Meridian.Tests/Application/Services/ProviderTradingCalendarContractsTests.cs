using FluentAssertions;
using Meridian.Contracts.Operations;
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
                    "NYSE",
                    "US",
                    "Regular",
                    new DateTimeOffset(2026, 7, 2, 13, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 2, 20, 0, 0, TimeSpan.Zero))
            ],
            Closures: [],
            Provenance: new ProviderCalendarProvenance(
                ProviderId: "calendar-vendor",
                SourceReference: "calendar-vendor/us/2026-07-02",
                RetrievedAtUtc: new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
                SourceAsOfUtc: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                DataProvenance: DataProvenance.Real));

        response.EnsureProvenanceComplete();
        response.Provenance.DataProvenance.Should().Be(DataProvenance.Real);
        ProviderCapabilityKind.TradingCalendar.Should().Be((ProviderCapabilityKind)17);
    }

    [Fact]
    public void ProviderCalendarResponse_RejectsIncompleteProvenance()
    {
        var response = new ProviderTradingCalendarResponse(
            Sessions: [],
            Closures: [],
            Provenance: new ProviderCalendarProvenance(
                ProviderId: "calendar-vendor",
                SourceReference: "",
                RetrievedAtUtc: default,
                SourceAsOfUtc: null,
                DataProvenance: DataProvenance.Real));

        var action = response.EnsureProvenanceComplete;

        action.Should().Throw<ArgumentException>();
    }
}
