using FluentAssertions;
using Meridian.Application.Etl;
using Meridian.Contracts.Etl;

namespace Meridian.Tests.Application.Etl;

public sealed class EtlJobDefinitionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-etl-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_AndGetAsync_RoundTripsDefinition()
    {
        var store = new EtlJobDefinitionStore(_root);
        var definition = new EtlJobDefinition
        {
            JobId = "job-1",
            FlowDirection = EtlFlowDirection.Import,
            PartnerSchemaId = "partner.trades.csv.v1",
            LogicalSourceName = "partner-a",
            Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = _root },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog }
        };

        await store.SaveAsync(definition);
        var loaded = await store.GetAsync("job-1");

        loaded.Should().NotBeNull();
        loaded!.LogicalSourceName.Should().Be("partner-a");
        loaded.PartnerSchemaId.Should().Be("partner.trades.csv.v1");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
