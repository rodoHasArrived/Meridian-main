using Meridian.Core.Pipeline;
// ✅ ADR-001: IBrokerageGateway contract for brokerage order execution
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery via [DataSource]
// ✅ ADR-010: HTTP client via IHttpClientFactory
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Robinhood brokerage gateway for live order execution via the unofficial Robinhood API.
/// Uses REST for order management and a bounded channel for execution report streaming.
///
/// <para>
/// <b>Important:</b> Robinhood does not publish an official public API.
/// This gateway targets the same endpoints used by the Robinhood mobile application.
/// Set <c>ROBINHOOD_ACCESS_TOKEN</c> to your personal access token.
/// </para>
///
/// <para>
/// Order types supported: market, limit, stop, stop-limit.
/// Order modification is implemented as cancel + resubmit (Robinhood does not support
/// in-place order modification via the unofficial API).
/// </para>
/// </summary>
[DataSource("robinhood-brokerage", "Robinhood Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 35, Description = "Robinhood order execution via unofficial API (requires personal access token)")]
[ImplementsAdr("ADR-001", "Robinhood brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory for HTTP connections")]
public sealed class RobinhoodBrokerageGateway : IBrokerageGateway, IBrokerageOrderAmendmentRecoveryGateway,
    IPreassignedReplacementReferenceGateway
{
    private const string BaseUrl = "https://api.robinhood.com";
    private const string OptionsPositionsUrl = BaseUrl + "/options/positions/?nonzero=true";
    private const string EnvAccessToken = "ROBINHOOD_ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RobinhoodBrokerageGateway> _logger;
    private readonly string? _accessToken;
    private readonly Channel<ExecutionReport> _reportChannel;
    private readonly ConcurrentDictionary<string, OrderChain> _orderChains = new(StringComparer.Ordinal);
    private volatile bool _connected;
    private bool _disposed;

    public RobinhoodBrokerageGateway(
        IHttpClientFactory httpClientFactory,
        ILogger<RobinhoodBrokerageGateway> logger,
        string? accessToken = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _accessToken = accessToken ?? Environment.GetEnvironmentVariable(EnvAccessToken);

        if (string.IsNullOrWhiteSpace(_accessToken))
            _logger.LogWarning(
                "ROBINHOOD_ACCESS_TOKEN is not set; Robinhood brokerage gateway will be unavailable.");

        _reportChannel = EventPipelinePolicy.Default.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    // ── IBrokerageGateway / IExecutionGateway ─────────────────────────────

    /// <inheritdoc />
    public string GatewayId => "robinhood";

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public string BrokerDisplayName => "Robinhood (unofficial)";

    /// <inheritdoc />
    public BrokerageCapabilities BrokerageCapabilities { get; } =
        BrokerageCapabilities.UsEquity(
            modification: false,  // Cancel + resubmit only
            partialFills: true,
            shortSelling: false,
            fractional: true,
            extendedHours: false);

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected)
            return;

        if (string.IsNullOrWhiteSpace(_accessToken))
            throw new InvalidOperationException(
                "ROBINHOOD_ACCESS_TOKEN is required. Set the environment variable before calling ConnectAsync.");

        // Validate token by fetching accounts.
        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        _connected = true;
        _logger.LogInformation(
            "Robinhood brokerage connected: account {AccountId}, equity {Equity:C}",
            account.AccountId, account.Equity);
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _logger.LogInformation("Robinhood brokerage disconnected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        => SubmitOrderCoreAsync(request, ct, publishReports: true);

    private async Task<ExecutionReport> SubmitOrderCoreAsync(
        OrderRequest request, CancellationToken ct, bool publishReports)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        RejectSessionScopedOrderType(request.Type);

        _logger.LogInformation(
            "Robinhood submitting order: {Side} {Quantity} {Symbol} @ {Type}",
            request.Side, request.Quantity, request.Symbol, request.Type);

        var accountUrl = await GetAccountUrlAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accountUrl))
        {
            var rejectReport = BuildRejectedReport(request, "Could not resolve Robinhood account URL.");
            if (publishReports)
                await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        if (IsOptionOrderRequest(request))
        {
            if (!TryGetOptionOrderMetadata(request, out var optionInstrumentUrl, out var positionEffect))
            {
                var rejectReport = BuildRejectedReport(
                    request,
                    "Robinhood option orders require option_instrument_url and position_effect metadata.");
                if (publishReports)
                    await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
                return rejectReport;
            }

            using var optionClient = CreateHttpClient();
            var optionInstrument = await ResolveOptionInstrumentAsync(optionInstrumentUrl, optionClient, ct).ConfigureAwait(false);
            if (optionInstrument is null)
            {
                var rejectReport = BuildRejectedReport(
                    request,
                    "Robinhood option orders require a trusted option_instrument_url that can be resolved.");
                if (publishReports)
                    await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
                return rejectReport;
            }

            if (!string.Equals(optionInstrument.ChainSymbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                var rejectReport = BuildRejectedReport(
                    request,
                    $"Robinhood option instrument chain symbol '{optionInstrument.ChainSymbol}' does not match order symbol '{request.Symbol}'.");
                if (publishReports)
                    await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
                return rejectReport;
            }

            return await SubmitOptionOrderAsync(
                request,
                accountUrl,
                optionInstrumentUrl,
                positionEffect,
                ct,
                publishReports).ConfigureAwait(false);
        }

        // Look up the instrument URL required by Robinhood's order API.
        var instrumentUrl = await GetInstrumentUrlAsync(request.Symbol, ct).ConfigureAwait(false);
        if (instrumentUrl is null)
        {
            var rejectReport = BuildRejectedReport(request, $"Could not resolve instrument URL for {request.Symbol}");
            if (publishReports)
                await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        var payload = new RobinhoodOrderPayload
        {
            Account = accountUrl,
            Instrument = instrumentUrl,
            Symbol = request.Symbol.ToUpperInvariant(),
            Side = request.Side == OrderSide.Buy ? "buy" : "sell",
            Type = MapOrderType(request.Type),
            TimeInForce = MapTimeInForce(request.TimeInForce),
            Trigger = request.Type is OrderType.StopMarket or OrderType.StopLimit ? "stop" : "immediate",
            Quantity = request.Quantity.ToString("G", CultureInfo.InvariantCulture),
            Price = request.LimitPrice?.ToString("G", CultureInfo.InvariantCulture),
            StopPrice = request.StopPrice?.ToString("G", CultureInfo.InvariantCulture),
            RefId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
        };

        using var client = CreateHttpClient();
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/orders/", payload,
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderPayload, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!publishReports && ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout))
                throw new HttpRequestException("Robinhood replacement outcome was not confirmed.", null, response.StatusCode);
            _logger.LogError("Robinhood order rejected: {StatusCode} {Body}", response.StatusCode, errorBody);
            var rejectReport = BuildRejectedReport(request, $"API error {response.StatusCode}: {errorBody}");
            if (publishReports)
                await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        var order = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderResponse, ct).ConfigureAwait(false);

        var report = new ExecutionReport
        {
            OrderId = order?.Id ?? request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId ?? payload.RefId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = MapRobinhoodStatus(order?.State),
            OrderQuantity = request.Quantity,
            HasOrderTerms = true,
            OrderLimitPrice = request.LimitPrice,
            OrderStopPrice = request.StopPrice,
            FilledQuantity = ParseDecimal(order?.CumulativeQuantity),
            FillPrice = TryParseDecimal(order?.AveragePrice),
            FillPriceIsCumulativeAverage = true,
            GatewayOrderId = order?.Id,
            Timestamp = order?.CreatedAt ?? DateTimeOffset.UtcNow,
        };
        if (publishReports)
        {
            RememberSubmission(report, request.Quantity);
            await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        }
        return report;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        using var client = CreateHttpClient();
        var chain = await ResolveOrderChainAsync(client, orderId, ct).ConfigureAwait(false);
        if (chain is null)
            return await PublishAsync(UnknownOrderReport(orderId, ExecutionReportType.CancelRejected), ct).ConfigureAwait(false);

        await chain.OperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (chain.HasUnconfirmedReplacement
                && !await ReconcilePendingReplacementAsync(client, chain, ct).ConfigureAwait(false))
                return await PublishAsync(UnconfirmedReplacementReport(chain) with
                {
                    ReportType = ExecutionReportType.CancelRejected
                }, ct).ConfigureAwait(false);
            var (snapshot, error) = await RequestConfirmedCancellationAsync(client, chain, ct).ConfigureAwait(false);
            var report = BuildOrderReport(chain, snapshot, error is null ? null : ExecutionReportType.CancelRejected, error);
            return await PublishAsync(report, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            chain.OperationGate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>Cancellation must be confirmed before a replacement can be submitted.</remarks>
    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        using var client = CreateHttpClient();
        var chain = await ResolveOrderChainAsync(client, orderId, ct).ConfigureAwait(false);
        if (chain is null)
            return await PublishAsync(UnknownOrderReport(orderId, ExecutionReportType.ModificationRejected), ct).ConfigureAwait(false);

        // Serialize cancel/replace operations across every client and broker alias in the chain.
        await chain.OperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (chain.HasUnconfirmedReplacement
                && !await ReconcilePendingReplacementAsync(client, chain, ct).ConfigureAwait(false))
                return await PublishAsync(UnconfirmedReplacementReport(chain), ct).ConfigureAwait(false);
            var requestedTotal = modification.NewQuantity ?? chain.TotalQuantity;
            if (requestedTotal <= 0m)
                return await PublishAsync(BuildOrderReport(chain, chain.Snapshot,
                    ExecutionReportType.ModificationRejected, "Replacement quantity must be positive."), ct).ConfigureAwait(false);

            var (snapshot, error) = await RequestConfirmedCancellationAsync(client, chain, ct).ConfigureAwait(false);
            if (error is not null || snapshot.Status != OrderStatus.Cancelled)
                return await PublishAsync(BuildOrderReport(chain, snapshot,
                    ExecutionReportType.ModificationRejected,
                    error ?? "Order became terminal before cancellation; no replacement was submitted."), ct).ConfigureAwait(false);

            // A missing quantity is not evidence of zero fills. Fail closed instead of risking
            // duplicate exposure when Robinhood omits the cumulative execution fields.
            if (snapshot.FilledQuantity is not { } executed)
                return await PublishAsync(BuildOrderReport(chain, snapshot,
                    ExecutionReportType.ModificationRejected,
                    "Cancelled order did not include a confirmed cumulative fill quantity; no replacement was submitted."), ct).ConfigureAwait(false);

            if (executed > 0m && snapshot.AverageFillPrice is not > 0m)
                return await PublishAsync(BuildOrderReport(chain, snapshot,
                    ExecutionReportType.ModificationRejected,
                    "Cancelled order did not include a confirmed execution price; no replacement was submitted."), ct).ConfigureAwait(false);

            if (executed < 0m || snapshot.Quantity <= 0m || executed > snapshot.Quantity)
                return await PublishAsync(BuildOrderReport(chain, snapshot,
                    ExecutionReportType.ModificationRejected,
                    "Cancelled order included an invalid cumulative fill quantity; no replacement was submitted."), ct).ConfigureAwait(false);

            var cumulativeFills = chain.PriorFilledQuantity + executed;
            var cumulativeNotional = chain.PriorFilledNotional + executed * (snapshot.AverageFillPrice ?? 0m);
            var remaining = requestedTotal - cumulativeFills;
            if (remaining <= 0m)
                return await PublishAsync(BuildOrderReport(chain, snapshot,
                    ExecutionReportType.ModificationRejected,
                    "Confirmed fills already meet or exceed the requested total; no replacement was submitted."), ct).ConfigureAwait(false);

            var request = snapshot.Equity is { } equity
                ? new OrderRequest
                {
                    Symbol = snapshot.Symbol,
                    Side = snapshot.Side,
                    Type = ParseOrderType(equity.Type),
                    TimeInForce = ParseTimeInForce(equity.TimeInForce),
                    Quantity = remaining,
                    LimitPrice = modification.NewLimitPrice ?? TryParseDecimal(equity.Price),
                    StopPrice = modification.NewStopPrice ?? TryParseDecimal(equity.StopPrice)
                }
                : BuildOptionModifyRequest(snapshot.Id, snapshot.Option!, modification) with { Quantity = remaining };

            // Robinhood ref_id is unique per submission; retain the canonical client identity
            // separately while all historical broker IDs continue resolving to this chain. The
            // OMS assigns and persists the ref before dispatch so restart recovery can find it.
            request = request with
            {
                ClientOrderId = string.IsNullOrWhiteSpace(modification.ReplacementClientOrderId)
                ? Guid.NewGuid().ToString("N") : modification.ReplacementClientOrderId
            };
            // Retain the attempt before dispatch. A timed out POST may already have created a
            // live order: another amendment must not generate a second replacement blindly.
            var attempt = new ReplacementAttempt(request.ClientOrderId!, request, snapshot,
                requestedTotal, cumulativeFills, cumulativeNotional);
            chain.PendingReplacement = attempt;
            _orderChains[request.ClientOrderId!] = chain;
            ExecutionReport replacement;
            try
            {
                replacement = await SubmitOrderCoreAsync(request, ct, publishReports: false).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Once dispatch was attempted, cancellation cannot prove the broker rejected
                // the order. Deliver the pending result so the OMS retains the risk reserve.
                return await PublishAsync(UnconfirmedReplacementReport(chain), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Robinhood replacement {RefId} requires reconciliation", request.ClientOrderId);
                return await PublishAsync(UnconfirmedReplacementReport(chain), CancellationToken.None).ConfigureAwait(false);
            }
            if (replacement.OrderStatus != OrderStatus.Rejected && string.IsNullOrWhiteSpace(replacement.GatewayOrderId))
                return await PublishAsync(UnconfirmedReplacementReport(chain), CancellationToken.None).ConfigureAwait(false);
            chain.PendingReplacement = null;
            if (replacement.OrderStatus == OrderStatus.Rejected)
                return await PublishAsync(BuildOrderReport(chain, snapshot,
                    ExecutionReportType.ModificationRejected,
                    $"Original order cancelled; replacement submission failed: {replacement.RejectReason}"), ct).ConfigureAwait(false);

            AdoptReplacement(chain, attempt, snapshot with
            {
                Id = replacement.GatewayOrderId!,
                RefId = request.ClientOrderId,
                Quantity = remaining,
                FilledQuantity = replacement.FilledQuantity,
                AverageFillPrice = replacement.FillPrice,
                Status = replacement.OrderStatus,
                LimitPrice = request.LimitPrice,
                StopPrice = request.StopPrice,
                HasOrderTerms = true
            });
            var report = replacement with
            {
                ClientOrderId = chain.ClientOrderId,
                ReportType = ExecutionReportType.Modified,
                OrderQuantity = requestedTotal,
                FilledQuantity = cumulativeFills + replacement.FilledQuantity,
                FillPriceIsCumulativeAverage = true,
                ReplacesOrderId = snapshot.Id,
                HasOrderTerms = true,
                OrderLimitPrice = request.LimitPrice,
                OrderStopPrice = request.StopPrice,
                FillPrice = GetChainAverageFillPrice(chain, chain.Snapshot)
            };
            return await PublishAsync(report, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            chain.OperationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ExecutionReport?> GetAmendmentForRecoveryAsync(
        string clientOrderId, OrderState retainedOrder, PendingOrderAmendment amendment, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientOrderId);
        ArgumentNullException.ThrowIfNull(retainedOrder);
        ArgumentNullException.ThrowIfNull(amendment);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(amendment.OriginalBrokerOrderId))
            return null;

        using var client = CreateHttpClient();
        var chain = _orderChains.GetOrAdd(clientOrderId, _ => new OrderChain(new OrderSnapshot(
            amendment.OriginalBrokerOrderId, clientOrderId, retainedOrder.Symbol, retainedOrder.Side,
            amendment.Original.Quantity, retainedOrder.FilledQuantity, retainedOrder.AverageFillPrice,
            OrderStatus.PendingNew), clientOrderId, amendment.Original.Quantity)
        {
            RetainedReplacementRefId = amendment.CandidateClientOrderId,
            RetainedAmendmentUnresolved = true
        });
        _orderChains[amendment.OriginalBrokerOrderId] = chain;
        if (!string.IsNullOrWhiteSpace(amendment.CandidateClientOrderId))
            _orderChains[amendment.CandidateClientOrderId] = chain;

        await chain.OperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            chain.RetainedReplacementRefId = amendment.CandidateClientOrderId;
            chain.RetainedAmendmentUnresolved = true;
            // The host can crash after dispatch but before the generated provider ref is
            // persisted. Keep known aliases blocked; absence of the ref proves no outcome.
            if (string.IsNullOrWhiteSpace(amendment.CandidateClientOrderId))
                return null;
            if (chain.PendingReplacement is null && chain.ActiveBrokerId == amendment.OriginalBrokerOrderId)
            {
                var original = await ReadOrderSnapshotAsync(client, amendment.OriginalBrokerOrderId, ct).ConfigureAwait(false);
                if (original is not { Status: OrderStatus.Cancelled, FilledQuantity: { } originalFilled }
                    || !string.Equals(original.Symbol, retainedOrder.Symbol, StringComparison.OrdinalIgnoreCase)
                    || original.Side != retainedOrder.Side
                    || originalFilled < 0m
                    || retainedOrder.FilledQuantity > 0m && retainedOrder.AverageFillPrice is not > 0m
                    || originalFilled > 0m && original.AverageFillPrice is not > 0m)
                    return null;
                // With the chain's earlier members retained, attribute each member's final fills
                // from the broker: a predecessor fill the store never booked is then recovered
                // exactly instead of blocking. Without them, only the retained aggregate is known.
                var prior = amendment.PriorChainBrokerOrderIds is { } priorIds
                    ? await ReadPriorChainFillsAsync(client, priorIds, retainedOrder, ct).ConfigureAwait(false)
                    : retainedOrder.FilledQuantity >= originalFilled
                        ? (Quantity: retainedOrder.FilledQuantity - originalFilled,
                           Notional: retainedOrder.FilledQuantity * (retainedOrder.AverageFillPrice ?? 0m)
                               - originalFilled * (original.AverageFillPrice ?? 0m))
                        : ((decimal Quantity, decimal Notional)?)null;
                if (prior is not { } chainPrior)
                    return null;
                var basisFilled = chainPrior.Quantity + originalFilled;
                var basisNotional = chainPrior.Notional + originalFilled * (original.AverageFillPrice ?? 0m);
                if (basisFilled < retainedOrder.FilledQuantity || basisFilled >= amendment.Proposed.Quantity)
                    return null;
                if (original.Equity is null && (original.Option?.Legs is not { Length: 1 } legs
                    || string.IsNullOrWhiteSpace(legs[0].Option) || string.IsNullOrWhiteSpace(legs[0].PositionEffect)))
                    return null;
                var remaining = amendment.Proposed.Quantity - basisFilled;
                var request = original.Equity is { } equity
                    ? new OrderRequest
                    {
                        Symbol = amendment.Proposed.Symbol,
                        Side = amendment.Proposed.Side,
                        Type = amendment.Proposed.Type,
                        TimeInForce = ParseTimeInForce(equity.TimeInForce),
                        Quantity = remaining,
                        LimitPrice = amendment.Proposed.LimitPrice,
                        StopPrice = amendment.Proposed.StopPrice
                    }
                    : BuildOptionModifyRequest(original.Id, original.Option!, new OrderModification
                    {
                        NewQuantity = remaining,
                        NewLimitPrice = amendment.Proposed.LimitPrice,
                        NewStopPrice = amendment.Proposed.StopPrice
                    });
                request = request with { ClientOrderId = amendment.CandidateClientOrderId };
                chain.Snapshot = original;
                chain.PriorFilledQuantity = chainPrior.Quantity;
                chain.PriorFilledNotional = chainPrior.Notional;
                chain.PendingReplacement = new ReplacementAttempt(amendment.CandidateClientOrderId, request, original,
                    amendment.Proposed.Quantity, basisFilled, basisNotional);
                _orderChains[original.Id] = chain;
                _orderChains[amendment.CandidateClientOrderId] = chain;
            }

            if (chain.PendingReplacement is not null)
            {
                if (chain.PendingReplacement.RefId != amendment.CandidateClientOrderId
                    || !await ReconcilePendingReplacementAsync(client, chain, ct).ConfigureAwait(false))
                    return null;
            }
            else
            {
                var observed = await ReadOrderSnapshotAsync(client, chain.ActiveBrokerId, ct).ConfigureAwait(false);
                if (observed is null || observed.RefId != amendment.CandidateClientOrderId)
                    return null;
                chain.Snapshot = MergeObservation(chain.Snapshot, observed);
                chain.RetainedReplacementRefId = null;
                chain.RetainedAmendmentUnresolved = false;
            }
            // A canceled predecessor alone never settles a retained replacement intent.
            if (chain.ActiveBrokerId == amendment.OriginalBrokerOrderId)
                return null;
            return BuildOrderReport(chain, chain.Snapshot, chain.Snapshot.Status == OrderStatus.Rejected
                ? ExecutionReportType.ModificationRejected : ExecutionReportType.Modified) with
            {
                ReplacesOrderId = amendment.OriginalBrokerOrderId,
                CandidateClientOrderId = amendment.CandidateClientOrderId
            };
        }
        finally
        {
            chain.OperationGate.Release();
        }
    }

    // Earlier chain members were confirmed cancelled before each replacement, so their broker
    // snapshots carry final fills. Any missing, working or mismatched member proves nothing.
    private async Task<(decimal Quantity, decimal Notional)?> ReadPriorChainFillsAsync(
        HttpClient client, IReadOnlyCollection<string> brokerOrderIds, OrderState retainedOrder, CancellationToken ct)
    {
        decimal quantity = 0m, notional = 0m;
        foreach (var brokerOrderId in brokerOrderIds)
        {
            var member = await ReadOrderSnapshotAsync(client, brokerOrderId, ct).ConfigureAwait(false);
            if (member is not { FilledQuantity: { } filled }
                || member.Status is not (OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired)
                || !string.Equals(member.Symbol, retainedOrder.Symbol, StringComparison.OrdinalIgnoreCase)
                || member.Side != retainedOrder.Side
                || filled < 0m || filled > 0m && member.AverageFillPrice is not > 0m)
                return null;
            quantity += filled;
            notional += filled * (member.AverageFillPrice ?? 0m);
        }
        return (quantity, notional);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _reportChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return report;
        }
    }

    /// <inheritdoc />
    public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var response = await client.GetAsync($"{BaseUrl}/accounts/", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodAccountListResponse, ct).ConfigureAwait(false);

        var acct = result?.Results?.FirstOrDefault();
        return new AccountInfo
        {
            AccountId = acct?.AccountNumber ?? "unknown",
            Equity = ParseDecimal(acct?.Equity),
            Cash = ParseDecimal(acct?.Cash),
            BuyingPower = ParseDecimal(acct?.BuyingPower),
            Currency = "USD",
            Status = acct?.Deactivated == true ? "deactivated" : "active",
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var optionsFetch = client.GetAsync(OptionsPositionsUrl, ct);
        var response = await client.GetAsync($"{BaseUrl}/positions/?nonzero=true", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodPositionListResponse, ct).ConfigureAwait(false);

        if (result?.Results is null)
            return Array.Empty<BrokerPosition>();

        var positions = new List<BrokerPosition>();
        foreach (var p in result.Results)
        {
            // Resolve the instrument URL to get the symbol.
            var symbol = await ResolveSymbolFromInstrumentAsync(p.Instrument, client, ct).ConfigureAwait(false);
            positions.Add(new BrokerPosition
            {
                PositionId = symbol ?? p.Instrument,
                Symbol = symbol ?? p.Instrument ?? string.Empty,
                UnderlyingSymbol = symbol ?? p.Instrument ?? string.Empty,
                Description = symbol ?? p.Instrument ?? string.Empty,
                Quantity = ParseDecimal(p.Quantity),
                AverageEntryPrice = ParseDecimal(p.AveragePrice),
                MarketPrice = 0m, // Robinhood positions endpoint does not include current price
                MarketValue = 0m,
                UnrealizedPnl = 0m,
                AssetClass = "equity",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["asset_class"] = "equity"
                }
            });
        }

        // Harvest the options response — already in-flight from the concurrent fetch above.
        try
        {
            var optResponse = await optionsFetch.ConfigureAwait(false);
            if (optResponse.IsSuccessStatusCode)
            {
                var optResult = await optResponse.Content.ReadFromJsonAsync(
                    RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionPositionListResponse, ct)
                    .ConfigureAwait(false);

                if (optResult?.Results is not null)
                {
                    var optionDetails = await Task.WhenAll(optResult.Results.Select(async p => new
                    {
                        Position = p,
                        Detail = await ResolveOptionInstrumentAsync(p.Option, client, ct).ConfigureAwait(false)
                    })).ConfigureAwait(false);

                    foreach (var item in optionDetails)
                    {
                        var p = item.Position;
                        var detail = item.Detail;
                        var qty = ParseDecimal(p.Quantity);
                        if (qty == 0m)
                            continue;

                        var underlyingSymbol = detail?.ChainSymbol ?? p.ChainSymbol ?? string.Empty;
                        var expiration = TryParseDateOnly(detail?.ExpirationDate);
                        var strike = TryParseDecimal(detail?.StrikePrice);
                        var right = NormalizeOptionRight(detail?.Type ?? p.Type);
                        positions.Add(new BrokerPosition
                        {
                            PositionId = p.Option ?? BuildFallbackOptionPositionId(underlyingSymbol, right, expiration, strike),
                            Symbol = underlyingSymbol,
                            UnderlyingSymbol = underlyingSymbol,
                            Description = BuildOptionDescription(underlyingSymbol, strike, right, expiration),
                            Quantity = qty,
                            AverageEntryPrice = ParseDecimal(p.AveragePrice),
                            MarketPrice = 0m,
                            MarketValue = 0m,
                            UnrealizedPnl = 0m,
                            AssetClass = "option",
                            Expiration = expiration,
                            Strike = strike,
                            Right = right,
                            Metadata = BuildOptionPositionMetadata(p.Option, underlyingSymbol, right, expiration, strike)
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "Robinhood options positions request returned {StatusCode}", optResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Use the URL constant to keep exception-path logging allocation-free.
            _logger.LogWarning(ex, "Failed to fetch Robinhood options positions from {Url}", OptionsPositionsUrl);
        }

        return positions.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var response = await client.GetAsync(
            $"{BaseUrl}/orders/?state=queued,unconfirmed,confirmed,partially_filled", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderListResponse, ct).ConfigureAwait(false);

        if (result?.Results is null)
            return Array.Empty<BrokerOrder>();

        return result.Results.Select(o => new BrokerOrder
        {
            OrderId = o.Id ?? string.Empty,
            ClientOrderId = o.RefId,
            Symbol = o.Symbol ?? string.Empty,
            Side = o.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            Type = ParseOrderType(o.Type),
            Quantity = ParseDecimal(o.Quantity),
            FilledQuantity = 0m, // Robinhood executed_notional is a currency value, not filled shares/contracts.
            LimitPrice = string.IsNullOrEmpty(o.Price) ? null : ParseDecimal(o.Price),
            StopPrice = string.IsNullOrEmpty(o.StopPrice) ? null : ParseDecimal(o.StopPrice),
            Status = MapRobinhoodStatus(o.State),
            CreatedAt = o.CreatedAt ?? DateTimeOffset.UtcNow,
        }).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_connected)
                return BrokerHealthStatus.Unhealthy("Not connected");
            var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
            return account.Status == "active"
                ? BrokerHealthStatus.Healthy($"Account {account.AccountId} active")
                : BrokerHealthStatus.Unhealthy($"Account status: {account.Status}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Robinhood health check failed");
            return BrokerHealthStatus.Unhealthy(ex.Message);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        _connected = false;
        _reportChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private sealed record OrderSnapshot(
        string Id,
        string? RefId,
        string Symbol,
        OrderSide Side,
        decimal Quantity,
        decimal? FilledQuantity,
        decimal? AverageFillPrice,
        OrderStatus Status,
        RobinhoodOrderResponse? Equity = null,
        RobinhoodOptionOrderResponse? Option = null,
        decimal? LimitPrice = null,
        decimal? StopPrice = null,
        bool HasOrderTerms = false);

    private sealed record ReplacementAttempt(
        string RefId, OrderRequest Request, OrderSnapshot CancelledOrder,
        decimal TotalQuantity, decimal PriorFilledQuantity, decimal PriorFilledNotional);

    private sealed class OrderChain(OrderSnapshot snapshot, string clientOrderId, decimal totalQuantity)
    {
        public string ClientOrderId { get; } = clientOrderId;
        public string ActiveBrokerId { get; set; } = snapshot.Id;
        public decimal TotalQuantity { get; set; } = totalQuantity;
        public decimal PriorFilledQuantity { get; set; }
        public decimal PriorFilledNotional { get; set; }
        public ReplacementAttempt? PendingReplacement { get; set; }
        public string? RetainedReplacementRefId { get; set; }
        public bool RetainedAmendmentUnresolved { get; set; }
        public bool HasUnconfirmedReplacement => PendingReplacement is not null || RetainedAmendmentUnresolved;
        public string? UnconfirmedReplacementRefId => PendingReplacement?.RefId ?? RetainedReplacementRefId;
        public OrderSnapshot Snapshot { get; set; } = snapshot;
        public SemaphoreSlim OperationGate { get; } = new(1, 1);
    }

    private void RememberSubmission(ExecutionReport report, decimal quantity)
    {
        if (report.OrderStatus == OrderStatus.Rejected || report.GatewayOrderId is not { Length: > 0 } id)
            return;
        var clientId = report.ClientOrderId ?? id;
        var snapshot = new OrderSnapshot(id, clientId, report.Symbol, report.Side,
            quantity, report.FilledQuantity, report.FillPrice, report.OrderStatus,
            LimitPrice: report.OrderLimitPrice, StopPrice: report.OrderStopPrice, HasOrderTerms: report.HasOrderTerms);
        var chain = new OrderChain(snapshot, clientId, quantity);
        _orderChains[id] = chain;
        _orderChains[clientId] = chain;
    }

    private async Task<OrderChain?> ResolveOrderChainAsync(HttpClient client, string orderId, CancellationToken ct)
    {
        if (_orderChains.TryGetValue(orderId, out var existing))
            return existing;
        var snapshot = await ReadOrderSnapshotAsync(client, orderId, ct).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        // A newly discovered broker ID may belong to a timed out replacement whose ref is
        // already retained. Reuse its operation gate and fill basis instead of forking a chain.
        if (snapshot.RefId is not null && _orderChains.TryGetValue(snapshot.RefId, out existing))
            return existing;
        var chain = _orderChains.GetOrAdd(snapshot.Id,
            _ => new OrderChain(snapshot, snapshot.RefId ?? snapshot.Id, snapshot.Quantity));
        _orderChains[orderId] = chain;
        _orderChains[chain.ClientOrderId] = chain;
        return chain;
    }

    private async Task<OrderSnapshot?> ReadOrderSnapshotAsync(HttpClient client, string orderId, CancellationToken ct)
    {
        var equity = await TryGetOrderAsync(client, orderId, ct).ConfigureAwait(false);
        if (equity is not null && string.Equals(equity.Id, orderId, StringComparison.Ordinal))
            return ToOrderSnapshot(equity);

        var option = await TryGetOptionOrderAsync(client, orderId, ct).ConfigureAwait(false);
        return option is not null && string.Equals(option.Id, orderId, StringComparison.Ordinal)
            ? ToOrderSnapshot(option)
            : null;
    }

    private static OrderSnapshot ToOrderSnapshot(RobinhoodOrderResponse equity)
    {
        var filled = TryParseDecimal(equity.CumulativeQuantity);
        var average = TryParseDecimal(equity.AveragePrice);
        if (average is null && filled is > 0m && TryParseDecimal(equity.ExecutedNotional) is { } notional)
            average = notional / filled.Value;
        return new OrderSnapshot(equity.Id ?? string.Empty, equity.RefId, equity.Symbol ?? string.Empty,
            ParseOrderSide(equity.Side), ParseDecimal(equity.Quantity), filled,
            average, MapRobinhoodStatus(equity.State), Equity: equity,
            LimitPrice: TryParseDecimal(equity.Price), StopPrice: TryParseDecimal(equity.StopPrice), HasOrderTerms: true);
    }

    private static OrderSnapshot ToOrderSnapshot(RobinhoodOptionOrderResponse option)
        => new(option.Id ?? string.Empty, option.RefId, option.ChainSymbol ?? string.Empty,
            ParseOptionOrderSide(option), ParseDecimal(option.Quantity), TryParseDecimal(option.ProcessedQuantity),
            GetOptionAverageFillPrice(option), MapRobinhoodStatus(option.State), Option: option,
            LimitPrice: TryParseDecimal(option.Price), HasOrderTerms: true);

    private void AdoptReplacement(OrderChain chain, ReplacementAttempt attempt, OrderSnapshot replacement)
    {
        chain.PriorFilledQuantity = attempt.PriorFilledQuantity;
        chain.PriorFilledNotional = attempt.PriorFilledNotional;
        chain.TotalQuantity = attempt.TotalQuantity;
        chain.ActiveBrokerId = replacement.Id;
        chain.Snapshot = replacement;
        chain.PendingReplacement = null;
        chain.RetainedReplacementRefId = null;
        chain.RetainedAmendmentUnresolved = false;
        _orderChains[replacement.Id] = chain;
        _orderChains[attempt.RefId] = chain;
    }

    private async Task<bool> ReconcilePendingReplacementAsync(HttpClient client, OrderChain chain, CancellationToken ct)
    {
        if (chain.PendingReplacement is not { } attempt)
            return false;
        var path = IsOptionOrderRequest(attempt.Request) ? "/options/orders/" : "/orders/";
        string? next = $"{BaseUrl}{path}?ref_id={Uri.EscapeDataString(attempt.RefId)}";
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var matches = new Dictionary<string, OrderSnapshot>(StringComparer.Ordinal);
        try
        {
            // Include every state and consume every page. An empty page or a missing order is
            // not evidence that a timed out POST failed, and a first match is not uniqueness.
            for (var page = 0; next is not null && page < 100; page++)
            {
                if (!TryBuildTrustedRobinhoodUri(next, out var uri) || uri.AbsolutePath != path
                    || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) || !visited.Add(uri.AbsoluteUri))
                    return false;
                using var response = await client.GetAsync(uri, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return false;
                IEnumerable<OrderSnapshot> orders;
                if (path == "/orders/")
                {
                    var result = await response.Content.ReadFromJsonAsync(
                        RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderListResponse, ct).ConfigureAwait(false);
                    if (result?.Results is null)
                        return false;
                    orders = result.Results.Select(ToOrderSnapshot);
                    next = result.Next;
                }
                else
                {
                    var result = await response.Content.ReadFromJsonAsync(
                        RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionOrderListResponse, ct).ConfigureAwait(false);
                    if (result?.Results is null)
                        return false;
                    orders = result.Results.Select(ToOrderSnapshot);
                    next = result.Next;
                }

                foreach (var candidate in orders.Where(order => order.RefId == attempt.RefId))
                {
                    if (!MatchesReplacementAttempt(attempt, candidate))
                        return false;
                    matches[candidate.Id] = matches.TryGetValue(candidate.Id, out var prior)
                        ? MergeObservation(prior, candidate)
                        : candidate;
                }
                if (matches.Count > 1)
                    return false;
                if (string.IsNullOrWhiteSpace(next))
                    next = null;
            }
            if (next is not null || matches.Count != 1)
                return false;

            var found = matches.Values.Single();
            var current = await ReadOrderSnapshotAsync(client, found.Id, ct).ConfigureAwait(false);
            if (current is null || current.RefId != attempt.RefId || !MatchesReplacementAttempt(attempt, current))
                return false;
            found = MergeObservation(found, current);
            AdoptReplacement(chain, attempt, found);
            var report = BuildOrderReport(chain, found, found.Status == OrderStatus.Rejected
                ? ExecutionReportType.ModificationRejected
                : ExecutionReportType.Modified) with
            { ReplacesOrderId = attempt.CancelledOrder.Id };
            await PublishAsync(report, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Robinhood replacement {RefId} reconciliation remains unconfirmed", attempt.RefId);
            return false;
        }
    }

    private static bool MatchesReplacementAttempt(ReplacementAttempt attempt, OrderSnapshot candidate)
    {
        var request = attempt.Request;
        if (string.IsNullOrWhiteSpace(candidate.Id) || candidate.Id == attempt.CancelledOrder.Id
            || !string.Equals(candidate.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase)
            || candidate.Side != request.Side || candidate.Quantity != request.Quantity
            || candidate.FilledQuantity is not { } filled || filled < 0m || filled > candidate.Quantity
            || filled > 0m && candidate.AverageFillPrice is not > 0m)
            return false;
        var state = candidate.Equity?.State ?? candidate.Option?.State;
        if (state is not ("queued" or "unconfirmed" or "confirmed" or "partially_filled" or "filled"
            or "cancelled" or "canceled" or "pending_cancel" or "expired" or "failed" or "rejected"))
            return false;
        if (!IsTerminal(candidate.Status)
            && (candidate.LimitPrice != request.LimitPrice || candidate.StopPrice != request.StopPrice))
            return false;
        if (!IsOptionOrderRequest(request))
            return candidate.Equity is { } equity
                && string.Equals(equity.TimeInForce, MapTimeInForce(request.TimeInForce), StringComparison.OrdinalIgnoreCase)
                && string.Equals(equity.Side, request.Side == OrderSide.Buy ? "buy" : "sell", StringComparison.OrdinalIgnoreCase)
                && string.Equals(equity.Type, MapOrderType(request.Type), StringComparison.OrdinalIgnoreCase);
        var leg = candidate.Option?.Legs is { Length: 1 } legs ? legs[0] : null;
        return leg is { RatioQuantity: 1 }
            && string.Equals(leg.Side, request.Side == OrderSide.Buy ? "buy" : "sell", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Option!.Type, MapOrderType(request.Type), StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Option.TimeInForce, MapTimeInForce(request.TimeInForce), StringComparison.OrdinalIgnoreCase)
            && request.Metadata is not null
            && request.Metadata.TryGetValue("option_instrument_url", out var option)
            && string.Equals(leg.Option, option, StringComparison.Ordinal)
            && request.Metadata.TryGetValue("position_effect", out var effect)
            && string.Equals(leg.PositionEffect, effect, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(OrderSnapshot Snapshot, string? Error)> RequestConfirmedCancellationAsync(
        HttpClient client, OrderChain chain, CancellationToken ct)
    {
        if (chain.HasUnconfirmedReplacement)
            return (chain.Snapshot with { Status = OrderStatus.PendingNew },
                $"Replacement {chain.UnconfirmedReplacementRefId} must be reconciled before further operations.");
        var snapshot = await ReadOrderSnapshotAsync(client, chain.ActiveBrokerId, ct).ConfigureAwait(false);
        if (snapshot is null)
            return (chain.Snapshot, "Cannot confirm the current order state; no cancellation or replacement was submitted.");
        snapshot = MergeObservation(chain.Snapshot, snapshot);
        chain.Snapshot = snapshot;
        if (IsTerminal(snapshot.Status))
            return (snapshot, null);

        var segment = snapshot.Option is null ? "orders" : "options/orders";
        using var response = await client.PostAsync(
            $"{BaseUrl}/{segment}/{snapshot.Id}/cancel/", content: null, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var observed = await ReadOrderSnapshotAsync(client, snapshot.Id, ct).ConfigureAwait(false);
            chain.Snapshot = observed is null ? snapshot : MergeObservation(snapshot, observed);
            return (chain.Snapshot, $"Cancel request failed: {response.StatusCode} {body}");
        }

        // HTTP acceptance is not a terminal acknowledgement. Confirm the exact broker order,
        // including any executions that won the race, before allowing another submission.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            var observed = await ReadOrderSnapshotAsync(client, snapshot.Id, ct).ConfigureAwait(false);
            if (observed is null)
                continue;
            snapshot = MergeObservation(snapshot, observed);
            chain.Snapshot = snapshot;
            if (IsTerminal(snapshot.Status))
                return (snapshot, null);
        }

        chain.Snapshot = snapshot with { Status = OrderStatus.PendingCancel };
        return (chain.Snapshot, "Cancellation has not been confirmed; no replacement was submitted.");
    }

    private static decimal? GetOptionAverageFillPrice(RobinhoodOptionOrderResponse order)
    {
        if (TryParseDecimal(order.AveragePrice) is { } average)
            return average;
        // The unofficial API commonly reports option execution prices on the single leg.
        // Do not treat processed_premium (cash) as a per-contract execution price.
        var executions = order.Legs is { Length: 1 } ? order.Legs[0].Executions : null;
        if (executions is null || executions.Length == 0)
            return null;
        decimal quantity = 0m, notional = 0m;
        foreach (var execution in executions)
        {
            if (TryParseDecimal(execution.Quantity) is not > 0m || TryParseDecimal(execution.Price) is not > 0m)
                return null;
            var executed = ParseDecimal(execution.Quantity);
            quantity += executed;
            notional += executed * ParseDecimal(execution.Price);
        }
        return quantity > 0m && quantity == TryParseDecimal(order.ProcessedQuantity)
            ? notional / quantity
            : null;
    }

    private static OrderSnapshot MergeObservation(OrderSnapshot previous, OrderSnapshot observed)
    {
        if (previous.Id != observed.Id)
            return observed;
        if (previous.FilledQuantity is { } filled && observed.FilledQuantity is { } current && current < filled)
            observed = observed with { FilledQuantity = filled, AverageFillPrice = previous.AverageFillPrice };
        if (IsTerminal(previous.Status) && !IsTerminal(observed.Status))
            observed = observed with { Status = previous.Status };
        return observed;
    }

    private static ExecutionReport UnconfirmedReplacementReport(OrderChain chain)
        => BuildOrderReport(chain, chain.Snapshot with { Status = OrderStatus.PendingNew },
            ExecutionReportType.ModificationPending,
            chain.UnconfirmedReplacementRefId is { } reference
                ? $"Replacement {reference} has an unconfirmed outcome; reconcile it before further operations."
                : "Replacement outcome is unconfirmed and its provider reference is unavailable; broker reconciliation is required.")
            with
        {
            OrderQuantity = chain.PendingReplacement?.TotalQuantity ?? chain.TotalQuantity,
            CandidateClientOrderId = chain.UnconfirmedReplacementRefId,
            HasOrderTerms = false,
            OrderLimitPrice = null,
            OrderStopPrice = null
        };

    private static bool IsTerminal(OrderStatus status)
        => status is OrderStatus.Cancelled or OrderStatus.Filled or OrderStatus.Rejected or OrderStatus.Expired;

    private static ExecutionReport BuildOrderReport(
        OrderChain chain, OrderSnapshot snapshot, ExecutionReportType? type = null, string? error = null) => new()
        {
            OrderId = snapshot.Id,
            ClientOrderId = chain.ClientOrderId,
            GatewayOrderId = snapshot.Id,
            Symbol = snapshot.Symbol,
            Side = snapshot.Side,
            ReportType = type ?? snapshot.Status switch
            {
                OrderStatus.Cancelled => ExecutionReportType.Cancelled,
                OrderStatus.Filled => ExecutionReportType.Fill,
                OrderStatus.PartiallyFilled => ExecutionReportType.PartialFill,
                OrderStatus.Rejected => ExecutionReportType.Rejected,
                OrderStatus.Expired => ExecutionReportType.Expired,
                _ => ExecutionReportType.New
            },
            OrderStatus = snapshot.Status,
            OrderQuantity = chain.TotalQuantity,
            HasOrderTerms = snapshot.HasOrderTerms,
            OrderLimitPrice = snapshot.LimitPrice,
            OrderStopPrice = snapshot.StopPrice,
            FilledQuantity = chain.PriorFilledQuantity + (snapshot.FilledQuantity ?? 0m),
            FillPriceIsCumulativeAverage = true,
            FillPrice = GetChainAverageFillPrice(chain, snapshot),
            RejectReason = error,
            Timestamp = DateTimeOffset.UtcNow
        };

    private static decimal? GetChainAverageFillPrice(OrderChain chain, OrderSnapshot snapshot)
    {
        if (snapshot.FilledQuantity is not { } filled || chain.PriorFilledQuantity + filled <= 0m)
            return null;
        if (filled > 0m && snapshot.AverageFillPrice is null)
            return null;
        return (chain.PriorFilledNotional + filled * (snapshot.AverageFillPrice ?? 0m))
            / (chain.PriorFilledQuantity + filled);
    }

    private static ExecutionReport UnknownOrderReport(string orderId, ExecutionReportType type) => new()
    {
        OrderId = orderId,
        ReportType = type,
        OrderStatus = OrderStatus.PendingNew,
        Symbol = string.Empty,
        Side = OrderSide.Buy,
        RejectReason = $"Cannot modify or cancel order {orderId}: original order details not found."
    };

    private async Task<ExecutionReport> PublishAsync(ExecutionReport report, CancellationToken ct)
    {
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException(
                "Robinhood brokerage gateway is not connected. Call ConnectAsync first.");
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.RobinhoodBrokerage);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    private async Task<string?> GetInstrumentUrlAsync(string symbol, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient();
            var response = await client.GetAsync(
                $"{BaseUrl}/instruments/?symbol={Uri.EscapeDataString(symbol)}", ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodInstrumentListResponse, ct).ConfigureAwait(false);

            return result?.Results?.FirstOrDefault()?.Url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve instrument URL for {Symbol}", symbol);
            return null;
        }
    }

    private async Task<string?> GetAccountUrlAsync(CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient();
            var response = await client.GetAsync($"{BaseUrl}/accounts/", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodAccountListResponse, ct).ConfigureAwait(false);

            return result?.Results?.FirstOrDefault()?.Url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Robinhood account URL");
            return null;
        }
    }

    private async Task<RobinhoodOrderResponse?> TryGetOrderAsync(
        HttpClient client,
        string orderId,
        CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync($"{BaseUrl}/orders/{orderId}/", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderResponse,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood failed to fetch equity order {OrderId}", orderId);
            return null;
        }
    }

    private async Task<RobinhoodOptionOrderResponse?> TryGetOptionOrderAsync(
        HttpClient client,
        string orderId,
        CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync($"{BaseUrl}/options/orders/{orderId}/", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionOrderResponse,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood failed to fetch option order {OrderId}", orderId);
            return null;
        }
    }

    private async Task<ExecutionReport> SubmitOptionOrderAsync(
        OrderRequest request,
        string accountUrl,
        string optionInstrumentUrl,
        string positionEffect,
        CancellationToken ct,
        bool publishReports)
    {
        var payload = new RobinhoodOptionOrderPayload
        {
            Account = accountUrl,
            Direction = request.Side == OrderSide.Buy ? "debit" : "credit",
            Quantity = request.Quantity.ToString("G", CultureInfo.InvariantCulture),
            Type = MapOrderType(request.Type),
            TimeInForce = MapTimeInForce(request.TimeInForce),
            Price = request.LimitPrice?.ToString("G", CultureInfo.InvariantCulture),
            RefId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            Legs =
            [
                new RobinhoodOptionLeg
                {
                    Option = optionInstrumentUrl,
                    Side = request.Side == OrderSide.Buy ? "buy" : "sell",
                    PositionEffect = positionEffect,
                    RatioQuantity = 1,
                }
            ]
        };

        using var client = CreateHttpClient();
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/options/orders/",
            payload,
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionOrderPayload,
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!publishReports && ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout))
                throw new HttpRequestException("Robinhood replacement outcome was not confirmed.", null, response.StatusCode);
            _logger.LogError("Robinhood option order rejected: {StatusCode} {Body}", response.StatusCode, errorBody);
            var rejectReport = BuildRejectedReport(request, $"API error {response.StatusCode}: {errorBody}");
            if (publishReports)
                await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        var order = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionOrderResponse,
            ct).ConfigureAwait(false);

        var report = new ExecutionReport
        {
            OrderId = order?.Id ?? request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId ?? payload.RefId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = MapRobinhoodStatus(order?.State),
            OrderQuantity = request.Quantity,
            HasOrderTerms = true,
            OrderLimitPrice = request.LimitPrice,
            OrderStopPrice = request.StopPrice,
            FilledQuantity = ParseDecimal(order?.ProcessedQuantity),
            FillPrice = order is null ? null : GetOptionAverageFillPrice(order),
            FillPriceIsCumulativeAverage = true,
            GatewayOrderId = order?.Id,
            Timestamp = order?.CreatedAt ?? DateTimeOffset.UtcNow,
        };
        if (publishReports)
        {
            RememberSubmission(report, request.Quantity);
            await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        }
        return report;
    }

    private static async Task<string?> ResolveSymbolFromInstrumentAsync(
        string? instrumentUrl, HttpClient client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentUrl))
            return null;

        if (!TryBuildTrustedRobinhoodUri(instrumentUrl, out var requestUri))
            return null;

        try
        {
            var resp = await client.GetAsync(requestUri, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var instrument = await resp.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodInstrumentResponse, ct).ConfigureAwait(false);
            return instrument?.Symbol;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<RobinhoodOptionInstrumentResponse?> ResolveOptionInstrumentAsync(
        string? optionInstrumentUrl,
        HttpClient client,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(optionInstrumentUrl))
            return null;

        if (!TryBuildTrustedRobinhoodUri(optionInstrumentUrl, out var requestUri))
            return null;

        try
        {
            var response = await client.GetAsync(requestUri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionInstrumentResponse,
                ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryBuildTrustedRobinhoodUri(string url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(parsed.Host, "api.robinhood.com", StringComparison.OrdinalIgnoreCase))
            return false;

        uri = parsed;
        return true;
    }

    private static Dictionary<string, string> BuildOptionPositionMetadata(
        string? optionInstrumentUrl,
        string underlyingSymbol,
        string? right,
        DateOnly? expiration,
        decimal? strike)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["asset_class"] = "option"
        };

        if (!string.IsNullOrWhiteSpace(optionInstrumentUrl))
            metadata["option_instrument_url"] = optionInstrumentUrl;
        if (!string.IsNullOrWhiteSpace(underlyingSymbol))
            metadata["underlying_symbol"] = underlyingSymbol;
        if (!string.IsNullOrWhiteSpace(right))
            metadata["right"] = right;
        if (expiration.HasValue)
            metadata["expiration"] = expiration.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (strike.HasValue)
            metadata["strike"] = strike.Value.ToString("G", CultureInfo.InvariantCulture);

        return metadata;
    }

    private static string BuildOptionDescription(
        string underlyingSymbol,
        decimal? strike,
        string? right,
        DateOnly? expiration)
    {
        var parts = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(underlyingSymbol))
            parts.Add(underlyingSymbol);
        if (strike.HasValue)
            parts.Add(strike.Value.ToString("G", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(right))
            parts.Add(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(right.ToLowerInvariant()));
        if (expiration.HasValue)
            parts.Add(expiration.Value.ToString("ddMMMyy", CultureInfo.InvariantCulture));

        return parts.Count == 0 ? "Option Position" : string.Join(" ", parts);
    }

    private static string BuildFallbackOptionPositionId(
        string underlyingSymbol,
        string? right,
        DateOnly? expiration,
        decimal? strike)
    {
        var expirationText = expiration?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown-expiry";
        var strikeText = strike?.ToString("G", CultureInfo.InvariantCulture) ?? "unknown-strike";
        var rightText = string.IsNullOrWhiteSpace(right) ? "unknown-right" : right;
        return $"{underlyingSymbol}:{rightText}:{expirationText}:{strikeText}";
    }

    private static string? NormalizeOptionRight(string? value) => value?.ToLowerInvariant() switch
    {
        "call" or "c" => "call",
        "put" or "p" => "put",
        _ => value
    };

    private static DateOnly? TryParseDateOnly(string? value)
        => ProviderDateParsing.ParseProviderDateOrNull(value);

    private static decimal? TryParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool IsOptionOrderRequest(OrderRequest request)
    {
        if (request.Metadata is null)
            return false;

        return request.Metadata.TryGetValue("option_instrument_url", out var optionInstrumentUrl)
                   && !string.IsNullOrWhiteSpace(optionInstrumentUrl)
               || request.Metadata.TryGetValue("asset_class", out var assetClass)
                   && string.Equals(assetClass, "option", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetOptionOrderMetadata(
        OrderRequest request,
        out string optionInstrumentUrl,
        out string positionEffect)
    {
        optionInstrumentUrl = string.Empty;
        positionEffect = string.Empty;

        if (request.Metadata is null)
            return false;

        request.Metadata.TryGetValue("option_instrument_url", out var optionInstrumentUrlValue);
        request.Metadata.TryGetValue("position_effect", out var positionEffectValue);
        optionInstrumentUrl = optionInstrumentUrlValue ?? string.Empty;
        positionEffect = positionEffectValue ?? string.Empty;

        return !string.IsNullOrWhiteSpace(optionInstrumentUrl)
               && !string.IsNullOrWhiteSpace(positionEffect);
    }

    private static OrderRequest BuildOptionModifyRequest(
        string orderId,
        RobinhoodOptionOrderResponse originalOption,
        OrderModification modification)
    {
        var leg = originalOption.Legs?.FirstOrDefault();
        if (leg is null || string.IsNullOrWhiteSpace(leg.Option) || string.IsNullOrWhiteSpace(leg.PositionEffect))
        {
            throw new InvalidOperationException(
                $"Cannot modify Robinhood option order {orderId}: option leg metadata is missing.");
        }

        return new OrderRequest
        {
            Symbol = originalOption.ChainSymbol ?? string.Empty,
            Side = ParseOptionOrderSide(originalOption),
            Type = ParseOrderType(originalOption.Type),
            TimeInForce = ParseTimeInForce(originalOption.TimeInForce),
            Quantity = modification.NewQuantity ?? ParseDecimal(originalOption.Quantity),
            LimitPrice = modification.NewLimitPrice
                ?? (string.IsNullOrEmpty(originalOption.Price) ? null : ParseDecimal(originalOption.Price)),
            StopPrice = modification.NewStopPrice,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = leg.Option,
                ["position_effect"] = leg.PositionEffect
            }
        };
    }

    private static ExecutionReport BuildRejectedReport(OrderRequest request, string reason) =>
        new()
        {
            OrderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId,
            ReportType = ExecutionReportType.Rejected,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = OrderStatus.Rejected,
            RejectReason = reason,
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static OrderSide ParseOrderSide(string? side) =>
        string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Sell
            : OrderSide.Buy;

    private static OrderSide ParseOptionOrderSide(RobinhoodOptionOrderResponse? optionOrder)
    {
        var legSide = optionOrder?.Legs?.FirstOrDefault()?.Side;
        return !string.IsNullOrWhiteSpace(legSide)
            ? ParseOrderSide(legSide)
            : string.Equals(optionOrder?.Direction, "credit", StringComparison.OrdinalIgnoreCase)
                ? OrderSide.Sell
                : OrderSide.Buy;
    }

    private static string MapOrderType(OrderType type) => type switch
    {
        OrderType.Market => "market",
        OrderType.Limit => "limit",
        OrderType.StopMarket => "stop",
        OrderType.StopLimit => "stop_limit",
        OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose
            => throw new NotSupportedException(
                $"Robinhood order mapping does not preserve the {type} session timing qualifier."),
        _ => throw new NotSupportedException($"Robinhood order mapping does not support {type}.")
    };

    private static void RejectSessionScopedOrderType(OrderType type)
    {
        if (type is OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose)
        {
            throw new NotSupportedException(
                $"Robinhood gateway does not currently preserve the {type} session timing qualifier.");
        }
    }

    private static string MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.Day => "gfd",         // Good-for-day
        TimeInForce.GoodTilCancelled => "gtc",
        TimeInForce.ImmediateOrCancel => "ioc",
        TimeInForce.FillOrKill => "fok",
        _ => "gfd"
    };

    private static OrderType ParseOrderType(string? type) => type switch
    {
        "market" => OrderType.Market,
        "limit" => OrderType.Limit,
        "stop" => OrderType.StopMarket,
        "stop_limit" => OrderType.StopLimit,
        _ => OrderType.Market
    };

    private static TimeInForce ParseTimeInForce(string? tif) => tif switch
    {
        "gfd" or "day" => TimeInForce.Day,
        "gtc" => TimeInForce.GoodTilCancelled,
        "ioc" => TimeInForce.ImmediateOrCancel,
        "fok" => TimeInForce.FillOrKill,
        _ => TimeInForce.Day
    };

    private static OrderStatus MapRobinhoodStatus(string? state) => state switch
    {
        "queued" => OrderStatus.PendingNew,
        "unconfirmed" => OrderStatus.PendingNew,
        "confirmed" => OrderStatus.Accepted,
        "partially_filled" => OrderStatus.PartiallyFilled,
        "filled" => OrderStatus.Filled,
        "cancelled" or "canceled" => OrderStatus.Cancelled,
        "pending_cancel" => OrderStatus.PendingCancel,
        "expired" => OrderStatus.Expired,
        "failed" => OrderStatus.Rejected,
        "rejected" => OrderStatus.Rejected,
        _ => OrderStatus.PendingNew
    };

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var result) ? result : 0m;

    // ── JSON DTOs (ADR-014: source generators) ────────────────────────────

    internal sealed class RobinhoodOrderPayload
    {
        [JsonPropertyName("account")] public string? Account { get; set; }
        [JsonPropertyName("instrument")] public string? Instrument { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("trigger")] public string? Trigger { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
    }

    internal sealed class RobinhoodOrderResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("executed_notional")] public string? ExecutedNotional { get; set; }
        [JsonPropertyName("cumulative_quantity")] public string? CumulativeQuantity { get; set; }
        [JsonPropertyName("average_price")] public string? AveragePrice { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    internal sealed class RobinhoodOrderListResponse
    {
        [JsonPropertyName("next")] public string? Next { get; set; }
        [JsonPropertyName("results")] public RobinhoodOrderResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodAccountResponse
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("account_number")] public string? AccountNumber { get; set; }
        [JsonPropertyName("equity")] public string? Equity { get; set; }
        [JsonPropertyName("cash")] public string? Cash { get; set; }
        [JsonPropertyName("buying_power")] public string? BuyingPower { get; set; }
        [JsonPropertyName("deactivated")] public bool? Deactivated { get; set; }
    }

    internal sealed class RobinhoodAccountListResponse
    {
        [JsonPropertyName("results")] public RobinhoodAccountResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodPositionResponse
    {
        [JsonPropertyName("instrument")] public string? Instrument { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("average_buy_price")] public string? AveragePrice { get; set; }
    }

    internal sealed class RobinhoodPositionListResponse
    {
        [JsonPropertyName("results")] public RobinhoodPositionResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodInstrumentResponse
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    }

    internal sealed class RobinhoodInstrumentListResponse
    {
        [JsonPropertyName("results")] public RobinhoodInstrumentResponse[]? Results { get; set; }
    }
    // ── Options DTOs ──────────────────────────────────────────────────────

    /// <summary>A single leg within a Robinhood options order.</summary>
    internal sealed class RobinhoodOptionLeg
    {
        [JsonPropertyName("option")] public string? Option { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("ratio_quantity")] public int RatioQuantity { get; set; } = 1;
        [JsonPropertyName("executions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RobinhoodOptionExecution[]? Executions { get; set; }
        [JsonPropertyName("position_effect")] public string? PositionEffect { get; set; }
    }

    /// <summary>Payload for <c>POST /options/orders/</c>.</summary>
    internal sealed class RobinhoodOptionExecution
    {
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
    }

    internal sealed class RobinhoodOptionOrderPayload
    {
        [JsonPropertyName("account")] public string? Account { get; set; }
        [JsonPropertyName("direction")] public string? Direction { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
        [JsonPropertyName("legs")] public RobinhoodOptionLeg[]? Legs { get; set; }
    }

    /// <summary>Response from <c>POST /options/orders/</c> or a single entry in <c>GET /options/orders/</c>.</summary>
    internal sealed class RobinhoodOptionOrderResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
        [JsonPropertyName("chain_symbol")] public string? ChainSymbol { get; set; }
        [JsonPropertyName("direction")] public string? Direction { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("processed_quantity")] public string? ProcessedQuantity { get; set; }
        [JsonPropertyName("average_price")] public string? AveragePrice { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("legs")] public RobinhoodOptionLeg[]? Legs { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    /// <summary>Paginated list response from <c>GET /options/orders/</c>.</summary>
    internal sealed class RobinhoodOptionOrderListResponse
    {
        [JsonPropertyName("next")] public string? Next { get; set; }
        [JsonPropertyName("results")] public RobinhoodOptionOrderResponse[]? Results { get; set; }
    }

    /// <summary>A single entry from <c>GET /options/positions/</c>.</summary>
    internal sealed class RobinhoodOptionPositionResponse
    {
        /// <summary>URL of the options instrument.</summary>
        [JsonPropertyName("option")] public string? Option { get; set; }
        /// <summary>Underlying ticker symbol (e.g. "AAPL").</summary>
        [JsonPropertyName("chain_symbol")] public string? ChainSymbol { get; set; }
        /// <summary>Number of contracts held (string decimal).</summary>
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        /// <summary>Average cost per contract (string decimal).</summary>
        [JsonPropertyName("average_price")] public string? AveragePrice { get; set; }
        /// <summary>"call" or "put".</summary>
        [JsonPropertyName("type")] public string? Type { get; set; }
    }

    /// <summary>Paginated list response from <c>GET /options/positions/</c>.</summary>
    internal sealed class RobinhoodOptionPositionListResponse
    {
        [JsonPropertyName("results")] public RobinhoodOptionPositionResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodOptionInstrumentResponse
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("chain_symbol")] public string? ChainSymbol { get; set; }
        [JsonPropertyName("expiration_date")] public string? ExpirationDate { get; set; }
        [JsonPropertyName("strike_price")] public string? StrikePrice { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}

/// <summary>
/// Source-generated JSON serializer context for Robinhood brokerage DTOs (ADR-014).
/// </summary>
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOrderPayload))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOrderResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOrderListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodAccountResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodAccountListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodPositionResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodPositionListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodInstrumentResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodInstrumentListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionLeg))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionOrderPayload))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionOrderResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionOrderListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionPositionResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionPositionListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionInstrumentResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class RobinhoodBrokerageSerializerContext : JsonSerializerContext;
