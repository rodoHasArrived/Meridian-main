using System.Collections.ObjectModel;
using System.Net.Http.Json;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Adapters.Templates;

/// <summary>
/// Broker adapter template showing strict boundary separation:
/// - Broker-specific DTOs and enums stay inside this file/namespace.
/// - Canonical Meridian contracts are exposed via execution SDK models.
/// </summary>
public static class BrokerAdapterTemplate
{
    public static void Register(IServiceCollection services)
    {
        services.AddHttpClient<TemplateBrokerHttpClient>();
        services.AddScoped<TemplateBrokerSessionService>();
        services.AddScoped<TemplateBrokerMapper>();
        services.AddScoped<TemplateBrokerHealthReporter>();
        services.AddScoped<TemplateBrokerOrderExtensions>();
        services.AddScoped<TemplateBrokerExecutionExtensions>();
        services.AddScoped<TemplateBrokerMarketDataExtensions>();
        // Optional streaming client for providers that support push updates.
        services.AddScoped<ITemplateBrokerStreamingClient, TemplateBrokerNoopStreamingClient>();
    }
}

public sealed class TemplateBrokerSessionService
{
    private readonly TemplateBrokerHttpClient _http;

    public TemplateBrokerSessionService(TemplateBrokerHttpClient http) => _http = http;

    public Task<TemplateBrokerSession> OpenSessionAsync(CancellationToken ct = default) =>
        _http.PostAsync<TemplateBrokerSessionRequest, TemplateBrokerSession>("/session/open", new(), ct);
}

public sealed class TemplateBrokerHttpClient
{
    private readonly HttpClient _httpClient;

    public TemplateBrokerHttpClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest payload, CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, payload, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Broker response payload at '{path}' was empty.");
    }
}

public interface ITemplateBrokerStreamingClient : IAsyncDisposable
{
    IAsyncEnumerable<TemplateBrokerStreamMessage> StreamAsync(CancellationToken ct = default);
}

public sealed class TemplateBrokerNoopStreamingClient : ITemplateBrokerStreamingClient
{
    public async IAsyncEnumerable<TemplateBrokerStreamMessage> StreamAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class TemplateBrokerMapper
{
    public OrderRequest ToCanonicalOrderRequest(TemplateBrokerOrderPayload payload) => new()
    {
        Symbol = payload.Symbol,
        Quantity = payload.Quantity,
        Side = payload.Side == TemplateBrokerOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
        Type = OrderType.Market,
        TimeInForce = TimeInForce.Day,
        ClientOrderId = payload.ClientReference,
    };

    public ExecutionReport ToCanonicalExecutionReport(TemplateBrokerOrderStatusPayload payload) => new()
    {
        OrderId = payload.BrokerOrderId,
        GatewayOrderId = payload.BrokerOrderId,
        ClientOrderId = payload.ClientReference,
        Symbol = payload.Symbol,
        Side = payload.Side == TemplateBrokerOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
        OrderStatus = payload.Status switch
        {
            TemplateBrokerOrderStatus.New => OrderStatus.Accepted,
            TemplateBrokerOrderStatus.Partial => OrderStatus.PartiallyFilled,
            TemplateBrokerOrderStatus.Filled => OrderStatus.Filled,
            TemplateBrokerOrderStatus.Cancelled => OrderStatus.Cancelled,
            _ => OrderStatus.Rejected,
        },
        ReportType = payload.Status == TemplateBrokerOrderStatus.Cancelled
            ? ExecutionReportType.Cancelled
            : ExecutionReportType.New,
        LastQuantity = payload.LastFillQuantity,
        LastPrice = payload.LastFillPrice,
        Timestamp = payload.BrokerTimestamp,
    };
}

public sealed class TemplateBrokerHealthReporter
{
    public BrokerHealthStatus MapHealth(TemplateBrokerHealthPayload payload)
    {
        if (!payload.IsApiReachable)
            return BrokerHealthStatus.Unhealthy("Broker API unreachable.");

        var details = $"LatencyMs={payload.LatencyMs}; Degradation={payload.DegradationLevel}";
        return payload.DegradationLevel == TemplateBrokerDegradation.Critical
            ? BrokerHealthStatus.Degraded(details)
            : BrokerHealthStatus.Healthy(details);
    }
}

public sealed class TemplateBrokerOrderExtensions
{
    public Task<ExecutionReport> PlaceOrderAsync(OrderRequest order, CancellationToken ct = default) =>
        Task.FromResult(new ExecutionReport { OrderId = order.ClientOrderId ?? "template" });

    public Task<IReadOnlyList<ExecutionReport>> PollOrderStatusAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExecutionReport>>(Array.Empty<ExecutionReport>());
}

public sealed class TemplateBrokerExecutionExtensions
{
    public Task<IReadOnlyList<ExecutionReport>> ReconcileExecutionsAsync(
        IReadOnlyList<ExecutionReport> localExecutionLog,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(localExecutionLog);
        return Task.FromResult<IReadOnlyList<ExecutionReport>>(new ReadOnlyCollection<ExecutionReport>(localExecutionLog.ToList()));
    }
}

public sealed class TemplateBrokerMarketDataExtensions
{
    public Task IngestMarketDataAsync(IAsyncEnumerable<TemplateBrokerMarketDataPayload> feed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return Task.CompletedTask;
    }
}

// Broker-specific request/response DTOs and enums. Keep them in the adapter boundary.
public sealed record TemplateBrokerSessionRequest;
public sealed record TemplateBrokerSession(string SessionToken, DateTimeOffset ExpiresAtUtc);
public sealed record TemplateBrokerStreamMessage(string Channel, string PayloadJson);
public sealed record TemplateBrokerOrderPayload(string Symbol, decimal Quantity, TemplateBrokerOrderSide Side, string? ClientReference);
public sealed record TemplateBrokerOrderStatusPayload(
    string BrokerOrderId,
    string Symbol,
    TemplateBrokerOrderSide Side,
    string? ClientReference,
    TemplateBrokerOrderStatus Status,
    decimal LastFillQuantity,
    decimal LastFillPrice,
    DateTimeOffset BrokerTimestamp);
public sealed record TemplateBrokerHealthPayload(bool IsApiReachable, int LatencyMs, TemplateBrokerDegradation DegradationLevel);
public sealed record TemplateBrokerMarketDataPayload(string Symbol, decimal Price, long Size, DateTimeOffset Timestamp);

public enum TemplateBrokerOrderSide { Buy, Sell }
public enum TemplateBrokerOrderStatus { New, Partial, Filled, Cancelled, Rejected }
public enum TemplateBrokerDegradation { None, Elevated, Critical }
