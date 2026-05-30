using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Host;

public interface ICppTraderSessionClient : IAsyncDisposable
{
    string SessionId { get; }

    CppTraderSessionKind SessionKind { get; }

    Task<RegisterSymbolResponse> RegisterSymbolAsync(RegisterSymbolRequest request, CancellationToken ct = default);

    Task<SubmitOrderResponse> SubmitOrderAsync(SubmitOrderRequest request, CancellationToken ct = default);

    Task<CancelOrderResponse> CancelOrderAsync(CancelOrderRequest request, CancellationToken ct = default);

    Task<GetSnapshotResponse> GetSnapshotAsync(GetSnapshotRequest request, CancellationToken ct = default);

    Task<HeartbeatResponse> HeartbeatAsync(CancellationToken ct = default);

    IAsyncEnumerable<CppTraderEnvelope> ReadEventsAsync(CancellationToken ct = default);
}
