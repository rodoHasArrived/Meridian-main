using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ExecutionPaperGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Execution;

public sealed class ExecutionLogBoundaryTests : IDisposable
{
    private const string PrivateValue = "private-value\r\nFORGED\u2028END";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-execution-log-boundary-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BrokerageSubmissionLogsOpaqueSymbolCorrelationWithoutChangingTheRequestOrReport()
    {
        var logger = new RecordingLogger<ProbeGateway>();
        await using var gateway = new ProbeGateway(logger);
        await gateway.ConnectAsync();
        logger.Entries.Clear();
        var request = new OrderRequest
        {
            Symbol = PrivateValue,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 100m
        };

        var report = await gateway.SubmitOrderAsync(request);

        gateway.LastRequest.Should().BeSameAs(request);
        report.Symbol.Should().Be(PrivateValue);
        logger.Entries.Should().ContainSingle().Which.Message.Should().Contain(Sha256Digest.ComputeUtf8(PrivateValue));
        AssertSafe(logger.Entries, PrivateValue);
    }

    [Fact]
    public async Task OrderRejectionKeepsTheExactReasonInRetainedJournalAndAuditInsteadOfOperationalLogs()
    {
        var logger = new RecordingLogger<OrderManagementSystem>();
        var risk = Substitute.For<IRiskValidator>();
        var rejection = RiskValidationResult.Rejected(PrivateValue);
        risk.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>()).Returns(rejection);
        risk.ValidateResolvedOrderAsync(Arg.Any<GatewayResolvedOrder>(), Arg.Any<CancellationToken>()).Returns(rejection);
        await using var journal = new ExecutionJournal(Path.Combine(_root, "journal"));
        await using var audit = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_root, "audit")), NullLogger<ExecutionAuditTrailService>.Instance);
        var gateway = new ExecutionPaperGateway(NullLogger<ExecutionPaperGateway>.Instance);
        using var oms = new OrderManagementSystem(gateway, logger, riskValidator: risk, auditTrail: audit, journal: journal);
        await oms.StartAsync();
        logger.Entries.Clear();

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "log-boundary-order",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 100m,
            FundAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(PrivateValue);
        (await journal.ReadAsync()).Should().ContainSingle(item => item.Kind == "Rejected" && item.Detail == PrivateValue);
        (await audit.GetRecentAsync()).Should().ContainSingle(item => item.Action == "OrderRejected" && item.Message == PrivateValue);
        logger.Entries.Should().Contain(item => item.Message.Contains(Sha256Digest.ComputeUtf8(PrivateValue), StringComparison.Ordinal));
        AssertSafe(logger.Entries, PrivateValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokerageCancelAndModifyLogOpaqueCorrelationWithoutChangingCoreInputsOrReports(bool modify)
    {
        var logger = new RecordingLogger<ProbeGateway>();
        await using var gateway = new ProbeGateway(logger);
        await gateway.ConnectAsync();
        logger.Entries.Clear();
        var modification = new OrderModification { NewQuantity = 2m, NewLimitPrice = 101m };

        var report = modify
            ? await gateway.ModifyOrderAsync(PrivateValue, modification)
            : await gateway.CancelOrderAsync(PrivateValue);

        gateway.LastOperationOrderId.Should().Be(PrivateValue);
        if (modify)
            gateway.LastModification.Should().BeSameAs(modification);
        else
            gateway.LastModification.Should().BeNull();
        report.OrderId.Should().Be(PrivateValue);
        report.ReportType.Should().Be(modify ? ExecutionReportType.Modified : ExecutionReportType.Cancelled);
        logger.Entries.Should().ContainSingle().Which.Message.Should().Contain(Sha256Digest.ComputeUtf8(PrivateValue));
        AssertSafe(logger.Entries, PrivateValue);
    }

    [Fact]
    public async Task CorruptPaperJsonlSkipsOnlyTheBadRecordWithoutLoggingItsPathOrRewritingSourceBytes()
    {
        var logger = new RecordingLogger<JsonlFilePaperSessionStore>();
        var path = Path.Combine(_root, "private-path\u2028END");
        var store = new JsonlFilePaperSessionStore(path, logger);
        const string sessionId = "paper-logs";
        await store.AppendOrderUpdateAsync(sessionId, new OrderState
        {
            OrderId = "valid-order",
            Symbol = PrivateValue,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            Status = OrderStatus.PendingNew,
            CreatedAt = DateTimeOffset.Parse("2026-01-02T12:00:00Z"),
            LastUpdatedAt = DateTimeOffset.Parse("2026-01-02T12:00:00Z")
        });
        var source = Path.Combine(path, sessionId, "orders.jsonl");
        await File.AppendAllTextAsync(source, "{private-corrupt-record\n");
        var bytes = await File.ReadAllBytesAsync(source);
        logger.Entries.Clear();

        var orders = await store.LoadOrderHistoryAsync(sessionId);

        orders.Should().ContainSingle().Which.Symbol.Should().Be(PrivateValue);
        (await File.ReadAllBytesAsync(source)).Should().Equal(bytes);
        logger.Entries.Should().ContainSingle().Which.Message.Should().Be("Skipping corrupt paper-session JSONL record (JsonException).");
        AssertSafe(logger.Entries, "private-path", "private-corrupt-record", PrivateValue);
    }

    [Fact]
    public async Task ClosingARestoredSessionHashesItsCanonicalIdWhilePersistingThatIdUnchanged()
    {
        var logger = new RecordingLogger<PaperSessionPersistenceService>();
        var store = Substitute.For<IPaperSessionStore>();
        var record = new PersistedSessionRecord(PrivateValue, "strategy", "Synthetic", 1000m,
            DateTimeOffset.Parse("2026-01-02T12:00:00Z"), null, true, []);
        store.LoadAllSessionsAsync(Arg.Any<CancellationToken>()).Returns(new[] { record });
        store.LoadFillRecordsAsync(PrivateValue, Arg.Any<CancellationToken>()).Returns(Array.Empty<PaperSessionFillRecord>());
        store.LoadSettlementsAsync(PrivateValue, Arg.Any<CancellationToken>()).Returns(Array.Empty<PaperSessionSettlementRecord>());
        store.LoadLedgerJournalAsync(PrivateValue, Arg.Any<CancellationToken>()).Returns(Array.Empty<PersistedJournalEntryDto>());
        store.LoadOrderHistoryAsync(PrivateValue, Arg.Any<CancellationToken>()).Returns(Array.Empty<OrderState>());
        await using var service = new PaperSessionPersistenceService(logger, store);
        await service.InitialiseAsync();
        logger.Entries.Clear();

        (await service.CloseSessionAsync(PrivateValue)).Should().BeTrue();

        await store.Received(1).SaveSessionMetadataAsync(
            Arg.Is<PersistedSessionRecord>(item => item.SessionId == PrivateValue && !item.IsActive), Arg.Any<CancellationToken>());
        await store.Received(1).SaveLedgerJournalAsync(PrivateValue,
            Arg.Is<IReadOnlyList<PersistedJournalEntryDto>>(items => items.Count > 0), Arg.Any<CancellationToken>());
        logger.Entries.Should().HaveCount(2);
        logger.Entries.Should().OnlyContain(item => item.Message.Contains(Sha256Digest.ComputeUtf8(PrivateValue), StringComparison.Ordinal));
        AssertSafe(logger.Entries, PrivateValue);
    }

    [Fact]
    public async Task RobinhoodRecoveryFailureLogsOnlyOrderHashAndFailureTypeAndRetainsUnknownOrderIdentity()
    {
        const string orderId = "private-order-id";
        var logger = new RecordingLogger<RobinhoodBrokerageGateway>();
        await using var gateway = new RobinhoodBrokerageGateway(new StubClientFactory(), logger, accessToken: "synthetic-token");
        await gateway.ConnectAsync();
        logger.Entries.Clear();

        var report = await gateway.CancelOrderAsync(orderId);

        report.OrderId.Should().Be(orderId);
        report.ReportType.Should().Be(ExecutionReportType.CancelRejected);
        logger.Entries.Should().HaveCount(2);
        logger.Entries.Should().OnlyContain(item => item.Message.Contains(Sha256Digest.ComputeUtf8(orderId), StringComparison.Ordinal)
            && item.Message.Contains(nameof(HttpRequestException), StringComparison.Ordinal));
        AssertSafe(logger.Entries, orderId, "private-provider-response");
    }

    [Fact]
    public async Task RobinhoodCallerCancellationStillPropagatesWithoutARecoveryWarning()
    {
        var logger = new RecordingLogger<RobinhoodBrokerageGateway>();
        await using var gateway = new RobinhoodBrokerageGateway(new StubClientFactory(), logger, accessToken: "synthetic-token");
        await gateway.ConnectAsync();
        logger.Entries.Clear();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await gateway.Invoking(item => item.CancelOrderAsync("cancelled-order", cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        logger.Entries.Should().BeEmpty();
    }

    private static void AssertSafe(IReadOnlyList<RecordedLogEntry> entries, params string[] privateValues)
    {
        entries.Should().NotBeEmpty();
        foreach (var entry in entries)
        {
            entry.Exception.Should().BeNull();
            entry.Message.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']).Should().Be(-1);
            foreach (var value in privateValues)
                entry.Message.Should().NotContain(value);
        }
    }

    private sealed class ProbeGateway(ILogger logger) : BaseBrokerageGateway(logger)
    {
        public OrderRequest? LastRequest { get; private set; }
        public string? LastOperationOrderId { get; private set; }
        public OrderModification? LastModification { get; private set; }
        public override string GatewayId => "synthetic";
        public override string BrokerDisplayName => "Synthetic";
        public override BrokerageCapabilities BrokerageCapabilities => BrokerageCapabilities.UsEquity();
        protected override Task ConnectCoreAsync(CancellationToken ct) => Task.CompletedTask;
        protected override Task DisconnectCoreAsync(CancellationToken ct) => Task.CompletedTask;
        protected override Task<ExecutionReport> SubmitOrderCoreAsync(OrderRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new ExecutionReport
            {
                OrderId = "synthetic-order",
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = ExecutionReportType.Fill,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = request.Quantity,
                FilledQuantity = request.Quantity,
                FillPrice = 100m,
                Timestamp = DateTimeOffset.Parse("2026-01-02T12:00:00Z")
            });
        }
        protected override Task<ExecutionReport> CancelOrderCoreAsync(string orderId, CancellationToken ct)
        {
            LastOperationOrderId = orderId;
            return Task.FromResult(OperationReport(orderId, ExecutionReportType.Cancelled, OrderStatus.Cancelled));
        }
        protected override Task<ExecutionReport> ModifyOrderCoreAsync(string orderId, OrderModification modification, CancellationToken ct)
        {
            LastOperationOrderId = orderId;
            LastModification = modification;
            return Task.FromResult(OperationReport(orderId, ExecutionReportType.Modified, OrderStatus.PendingNew));
        }
        private static ExecutionReport OperationReport(string orderId, ExecutionReportType type, OrderStatus status)
            => new()
            {
                OrderId = orderId,
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                ReportType = type,
                OrderStatus = status,
                Timestamp = DateTimeOffset.Parse("2026-01-02T12:00:00Z")
            };
        public override Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath == "/accounts/")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"results":[{"account_number":"synthetic-account","equity":"1000","cash":"1000","buying_power":"1000","deactivated":false}]}""", Encoding.UTF8, "application/json")
                });
            throw new HttpRequestException("private-provider-response\r\nFORGED\u2028END");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
