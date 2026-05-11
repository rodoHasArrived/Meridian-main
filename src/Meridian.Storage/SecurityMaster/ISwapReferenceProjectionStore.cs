namespace Meridian.Storage.SecurityMaster;

public interface ISwapReferenceProjectionStore
{
    Task<SwapProjectionRow?> GetSwapAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<SwapProjectionRow>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default);
    Task<IReadOnlyList<SwapProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}

public sealed record SwapProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? SwapType,
    DateOnly EffectiveDate,
    DateOnly MaturityDate,
    string LifecycleState,
    string PrimaryIdentifierValue,
    long Version);
