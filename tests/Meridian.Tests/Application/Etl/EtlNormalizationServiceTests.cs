using FluentAssertions;
using Meridian.Application.Canonicalization;
using Meridian.Application.Etl;
using Meridian.Contracts.Etl;
using Meridian.Domain.Events;
using NSubstitute;

namespace Meridian.Tests.Application.Etl;

public sealed class EtlNormalizationServiceTests
{
    [Fact]
    public async Task NormalizeAsync_MapsTradeRecord_ToAcceptedMarketEvent()
    {
        var canonicalizer = Substitute.For<IEventCanonicalizer>();
        canonicalizer.Canonicalize(Arg.Any<MarketEvent>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<MarketEvent>());
        var sut = new EtlNormalizationService(canonicalizer);

        var outcome = await sut.NormalizeAsync(
            new EtlJobDefinition
            {
                JobId = "job-1",
                FlowDirection = EtlFlowDirection.Import,
                PartnerSchemaId = "partner.trades.csv.v1",
                LogicalSourceName = "partner-a",
                Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = "/tmp" },
                Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog }
            },
            new PartnerRecordEnvelope
            {
                PartnerSchemaId = "partner.trades.csv.v1",
                SourceFileName = "input.csv",
                SourceFileChecksum = "abc",
                RecordIndex = 1,
                Fields = new Dictionary<string, string?>
                {
                    ["timestamp"] = "2026-01-01T00:00:00Z",
                    ["symbol"] = "AAPL",
                    ["price"] = "123.45",
                    ["size"] = "100",
                    ["venue"] = "XNAS",
                    ["sequence"] = "9",
                    ["aggressor"] = "BUY"
                }
            });

        outcome.Disposition.Should().Be(EtlRecordDisposition.Accepted);
        outcome.Event.Should().NotBeNull();
        outcome.Event!.Symbol.Should().Be("AAPL");
        outcome.Event.Source.Should().Be("partner-a");
    }
}
