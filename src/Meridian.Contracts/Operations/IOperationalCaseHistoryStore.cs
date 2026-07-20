namespace Meridian.Contracts.Operations;

/// <summary>
/// Append-only persistence boundary for hash-chained operational case history.
/// </summary>
public interface IOperationalCaseHistoryStore
{
    ValueTask<OperationalCaseHistoryRecord> AppendAsync(
        OperationalCaseHistoryAppendRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
        OperationalCaseHistoryQuery query,
        CancellationToken cancellationToken = default);
}
