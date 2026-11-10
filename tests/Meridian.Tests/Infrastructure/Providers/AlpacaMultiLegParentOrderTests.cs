using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Alpaca returns a multi-leg package parent with an empty symbol and side: only its legs name
/// instruments. Observed against the paper Trading API, one such order in the recent order
/// history made the post-connect reconciliation emit a report the durable inbox refuses, which
/// failed the reconciliation for every other order and kept the execution stream unhealthy.
/// </summary>
public sealed class AlpacaMultiLegParentOrderTests : IDisposable
{
    // The parent exactly as the paper Trading API returns it (identifiers replaced).
    private const string MultiLegParent =
        """
        {
          "id":"mleg-parent","client_order_id":"client-mleg","created_at":"2026-11-10T07:25:05.1Z",
          "updated_at":"2026-11-10T07:25:05.2Z","submitted_at":"2026-11-10T07:25:05.1Z","filled_at":null,
          "expired_at":null,"canceled_at":null,"failed_at":null,"replaced_at":null,"replaced_by":null,"replaces":null,
          "asset_id":"","symbol":"","asset_class":"","notional":null,"qty":"1","filled_qty":"0","filled_avg_price":null,
          "order_class":"mleg","order_type":"limit","type":"limit","side":"","time_in_force":"day","limit_price":"0.01",
          "stop_price":null,"status":"accepted","extended_hours":false,"legs":null
        }
        """;

    private const string SingleInstrumentOrder =
        """
        {
          "id":"plain-order","client_order_id":"client-plain","symbol":"SPY","asset_class":"us_equity","side":"buy",
          "type":"limit","order_class":"","qty":"1","filled_qty":"0","filled_avg_price":null,"limit_price":"1","stop_price":null,
          "status":"canceled","created_at":"2026-11-10T07:22:11Z","updated_at":"2026-11-10T07:22:12Z","canceled_at":"2026-11-10T07:22:12Z"
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meridian-alpaca-mleg-{Guid.NewGuid():N}");

    public AlpacaMultiLegParentOrderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task ReconcileExecutionSnapshotsAsync_MultiLegParent_IsLeftOutAndTheOtherOrdersReconcile()
    {
        await using var gateway = CreateGateway(BrokerHistory);

        var reports = await gateway.ReconcileExecutionSnapshotsAsync(watermark: null, CancellationToken.None);

        reports.Should().ContainSingle().Which.Report.Should().Match<ExecutionReport>(report =>
            report.OrderId == "plain-order" && report.Symbol == "SPY" && report.ReportType == ExecutionReportType.Cancelled);
    }

    [Fact]
    public async Task ReconcileAfterConnectAsync_BrokerHistoryWithMultiLegParent_StillAdmitsTheOtherOrders()
    {
        await using var stream = CreateStream();
        await using var gateway = CreateGateway(BrokerHistory, stream);
        stream.ConfigureDurableStateScope("paper-account-42", AlpacaCredentialEnvironment.PaperEnvironment);

        await stream.ReconcileAfterConnectAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reports = stream.Reports.GetAsyncEnumerator(timeout.Token);
        (await reports.MoveNextAsync()).Should().BeTrue("the single-instrument order must still be recovered");
        reports.Current.OrderId.Should().Be("plain-order");
    }

    [Fact]
    public async Task ProcessMessageAsync_MultiLegParentUpdate_IsNotAdmittedAndTheInboxStaysUsable()
    {
        await using var stream = CreateStream();
        stream.ConfigureDurableStateScope("paper-account-42", AlpacaCredentialEnvironment.PaperEnvironment);

        await stream.ProcessMessageAsync(TradeUpdate("new", MultiLegParent));
        await stream.ProcessMessageAsync(TradeUpdate("canceled", SingleInstrumentOrder));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reports = stream.Reports.GetAsyncEnumerator(timeout.Token);
        (await reports.MoveNextAsync()).Should().BeTrue("an update after the package event must still be admitted");
        reports.Current.OrderId.Should().Be("plain-order");
        stream.UnhealthyReason.Should().NotContain("inbox", "a package parent event must not fail the durable inbox");
    }

    [Fact]
    public async Task ProcessMessageAsync_SingleInstrumentUpdateWithoutSymbol_IsStillRefused()
    {
        await using var stream = CreateStream();
        stream.ConfigureDurableStateScope("paper-account-42", AlpacaCredentialEnvironment.PaperEnvironment);
        var withoutSymbol = SingleInstrumentOrder.Replace("\"symbol\":\"SPY\"", "\"symbol\":\"\"", StringComparison.Ordinal);

        var admission = () => stream.ProcessMessageAsync(TradeUpdate("canceled", withoutSymbol));

        await admission.Should().ThrowAsync<InvalidDataException>(
            "only the documented package shape is exempt; any other order without an instrument is corrupt evidence");
    }

    [Fact]
    public async Task GetOrderForRecoveryAsync_MultiLegParent_IsRefusedWithTheReason()
    {
        await using var gateway = CreateGateway(_ => MultiLegParent);
        typeof(AlpacaBrokerageGateway)
            .GetField("_connected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(gateway, true);

        var recovery = () => gateway.GetOrderForRecoveryAsync("client-mleg");

        (await recovery.Should().ThrowAsync<InvalidDataException>()).Which.Message.Should().Contain("multi-leg parent");
    }

    private static string BrokerHistory(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/account/activities/FILL", StringComparison.Ordinal)
            ? "[]"
            : $"[{MultiLegParent},{SingleInstrumentOrder}]";

    private static string TradeUpdate(string @event, string order) =>
        "{\"stream\":\"trade_updates\",\"data\":{\"event\":\"" + @event
            + "\",\"timestamp\":\"2026-11-10T07:25:06Z\",\"order\":" + order + "}}";

    private AlpacaTradeUpdatesClient CreateStream()
    {
        var writer = new AtomicFileWriterAdapter();
        return new AlpacaTradeUpdatesClient(Options, NullLogger<AlpacaTradeUpdatesClient>.Instance, writer,
            cursorStore: new FileAlpacaTradeUpdateCursorStore(Path.Combine(_directory, "trade-updates.json"), writer));
    }

    private static AlpacaBrokerageGateway CreateGateway(
        Func<HttpRequestMessage, string> respond, AlpacaTradeUpdatesClient? stream = null) =>
        new(new StubHttpClientFactory(new StubHandler(respond)), Options, NullLogger<AlpacaBrokerageGateway>.Instance, stream);

    private static AlpacaOptions Options => new(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: true);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
    }
}
