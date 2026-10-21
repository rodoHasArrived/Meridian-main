using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaTradeUpdateHashCompatibilityTests
{
    // Fixed pre-metadata canonical bytes and their uppercase SHA-256 hash. This fixture cannot
    // silently learn a new hash from ComputeContentHash when ExecutionReport gains a property.
    private const string LegacyHashMaterial = """
        {"Report":{"OrderId":"reconciled-order-1","ReportType":2,"Symbol":"MSFT","Side":0,"OrderStatus":2,"OrderQuantity":10,"FilledQuantity":4,"FillPrice":401.25,"Commission":null,"Fees":null,"SlippageCost":null,"Timestamp":"2026-08-05T15:00:00.1234567+00:00","RejectReason":null,"GatewayOrderId":"reconciled-order-1","ClientOrderId":"client-reconciled-order-1","OptionContract":null,"Legs":null,"Diagnostics":{"BrokerStatus":"partially_filled","BrokerRejectReason":null,"RejectCode":null,"Category":"alpaca-rest-reconciliation","Severity":null,"RecommendedAction":null}},"Timestamp":"2026-08-05T15:00:00.1234567+00:00"}
        """;
    private const string LegacyContentHash = "C8C5F71BFA06D119904E6DEB37306942CBC3CF40D3AE0071285AD78B4DE3FEB1";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reconciliation_LegacyPersistedHash_DeduplicatesAfterAddingDerivedMetadata(
        bool hasProviderEventId, bool wasPending)
    {
        var legacy = JsonSerializer.Deserialize(
            LegacyHashMaterial, AlpacaTradeUpdateStateJsonContext.Default.AlpacaTradeUpdateHashMaterial)!;
        var upgradedReport = legacy.Report with
        {
            FillPriceIsCumulativeAverage = true,
            LastFillQuantity = 4m,
            AssetClass = "us_equity",
            ReplacesOrderId = "predecessor-order-1",
            OrderLimitPrice = 402m,
            OrderStopPrice = 400m,
            HasOrderTerms = true,
            CandidateGatewayOrderId = "replacement-order-1",
            CandidateClientOrderId = "replacement-client-1"
        };
        AlpacaTradeUpdateStateCodec.ComputeContentHash(upgradedReport, legacy.Timestamp)
            .Should().Be(LegacyContentHash);
        var scope = FileAlpacaTradeUpdateCursorStore.CreateIdentityScope("paper-account-42", "paper");
        const string source = "rest-reconciliation";
        var providerEventId = hasProviderEventId ? "broker-event-1" : null;
        var expectedEventId = hasProviderEventId
            ? AlpacaTradeUpdateStateCodec.CreateProviderEventId(scope, source, providerEventId!)
            : AlpacaTradeUpdateStateCodec.CreateStableEventId(scope, source, LegacyContentHash);
        var oldState = wasPending
            ? new AlpacaTradeUpdateCursorState(AlpacaTradeUpdateCursorState.CurrentVersion, null,
                [], new Dictionary<string, string>(),
                [new(expectedEventId, LegacyContentHash, legacy.Report, legacy.Timestamp)])
            : new AlpacaTradeUpdateCursorState(AlpacaTradeUpdateCursorState.CurrentVersion, legacy.Timestamp,
                [expectedEventId], new Dictionary<string, string> { [expectedEventId] = LegacyContentHash }, []);
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-alpaca-legacy-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "alpaca.cursor");
        try
        {
            // Write an actual pre-metadata cursor shape, including the retained old content hash.
            var serializedLegacy = AlpacaTradeUpdateStateCodec.Serialize(oldState);
            serializedLegacy.Should().NotContain(nameof(ExecutionReport.FillPriceIsCumulativeAverage));
            File.WriteAllText(path, serializedLegacy);
            var store = new FileAlpacaTradeUpdateCursorStore(path, new AtomicFileWriterAdapter());
            var delivered = new List<ExecutionReport>();

            await using (var client = CreateClient(store, source, providerEventId, upgradedReport))
            {
                await client.ReconcileAfterConnectAsync();
                await client.ReconcileAfterConnectAsync();
                store.LoadState().PendingEnvelopes.Should().HaveCount(wasPending ? 1 : 0);
                await client.DisposeAsync();
                await foreach (var report in client.Reports)
                    delivered.Add(report);
            }

            delivered.Should().HaveCount(wasPending ? 1 : 0,
                "pending legacy fills execute once, while committed fills never execute again");
            var committed = store.LoadState();
            committed.EventIds.Should().Equal(expectedEventId);
            committed.EventHashes.Should().ContainKey(expectedEventId);
            committed.EventHashes[expectedEventId].Should().Be(LegacyContentHash);
            committed.PendingEnvelopes.Should().BeEmpty();

            await using var restarted = CreateClient(store, source, providerEventId, upgradedReport);
            await restarted.ReconcileAfterConnectAsync();
            await restarted.DisposeAsync();
            await foreach (var report in restarted.Reports)
                delivered.Add(report);
            delivered.Should().HaveCount(wasPending ? 1 : 0,
                "restart and redelivery must retain the same deduplication identity");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DurableReportSerialization_RetainsDerivedExecutionMetadataWithoutChangingStableHash()
    {
        var legacy = JsonSerializer.Deserialize(
            LegacyHashMaterial, AlpacaTradeUpdateStateJsonContext.Default.AlpacaTradeUpdateHashMaterial)!;
        var report = legacy.Report with
        {
            FillPriceIsCumulativeAverage = true,
            LastFillQuantity = 4m,
            AssetClass = "us_equity",
            ReplacesOrderId = "predecessor-order-1",
            OrderLimitPrice = 402m,
            OrderStopPrice = 400m,
            HasOrderTerms = true,
            CandidateGatewayOrderId = "replacement-order-1",
            CandidateClientOrderId = "replacement-client-1"
        };
        var state = new AlpacaTradeUpdateCursorState(AlpacaTradeUpdateCursorState.CurrentVersion, null,
            [], new Dictionary<string, string>(),
            [new("provider-event-1", LegacyContentHash, report, legacy.Timestamp)]);

        var serialized = AlpacaTradeUpdateStateCodec.Serialize(state);

        using var json = JsonDocument.Parse(serialized);
        json.RootElement.GetProperty("PendingEnvelopes")[0].GetProperty("Report")
            .GetProperty(nameof(ExecutionReport.FillPriceIsCumulativeAverage)).GetBoolean().Should().BeTrue();
        var replayed = AlpacaTradeUpdateStateCodec.Deserialize(serialized).PendingEnvelopes.Should().ContainSingle().Which;
        replayed.Report.FillPriceIsCumulativeAverage.Should().BeTrue();
        replayed.Report.CandidateClientOrderId.Should().Be("replacement-client-1");
        replayed.Report.Should().BeEquivalentTo(report);
        replayed.ContentHash.Should().Be(LegacyContentHash);
    }

    private static AlpacaTradeUpdatesClient CreateClient(IAlpacaTradeUpdateCursorStore store,
        string source, string? providerEventId, ExecutionReport report)
    {
        var client = new AlpacaTradeUpdatesClient(
            new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: true),
            NullLogger<AlpacaTradeUpdatesClient>.Instance, new AtomicFileWriterAdapter(), cursorStore: store);
        client.ConfigureDurableStateScope("paper-account-42", "paper");
        client.ConfigureReconciliation((_, _) => Task.FromResult<IReadOnlyList<AlpacaReconciliationReport>>(
            [new(source, providerEventId, report)]));
        return client;
    }
}
