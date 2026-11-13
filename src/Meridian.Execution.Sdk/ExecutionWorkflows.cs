namespace Meridian.Execution.Sdk;

public enum OrderSessionIntent { RegularOnly, QueueNextSession, ExtendedHours }

public sealed record OrderCheck(string Code, string Status, string Message);
public sealed record OrderMarketReference(decimal Price, DateTimeOffset ObservedAt, string Source);
public sealed record OrderPreviewResult(
    OrderRequest Request, string Fingerprint, DateTimeOffset EvaluatedAt, DateTimeOffset ExpiresAt,
    bool CanSubmit, bool RequiresApproval, decimal? EstimatedNotional, decimal? EstimatedFees,
    OrderMarketReference? Reference, IReadOnlyList<OrderCheck> Checks);

public interface IOrderPreviewService
{
    Task<OrderPreviewResult> PreviewAsync(OrderRequest request, CancellationToken ct = default);
}

public interface IOrderSessionPolicy
{
    Task<OrderCheck> EvaluateAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>Fresh executable-side prices; absence is never represented as zero.</summary>
public interface IOrderMarketReferenceSource
{
    OrderMarketReference? GetReference(string symbol, OrderSide side);
}

public interface IOrderInstrumentCoverageSource
{
    Task<bool> IsUsdEquityAsync(OrderRequest request, CancellationToken ct = default);
}
