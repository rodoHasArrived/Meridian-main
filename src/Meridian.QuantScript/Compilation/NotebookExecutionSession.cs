namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Tracks cell checkpoints for a single notebook editor session.
/// </summary>
public sealed class NotebookExecutionSession
{
    private readonly Dictionary<string, CheckpointEntry> _checkpoints = new(StringComparer.Ordinal);

    public void InvalidateFrom(IReadOnlyList<NotebookCellExecutionIdentity> cells, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (startIndex < 0 || startIndex >= cells.Count)
            return;

        for (var index = startIndex; index < cells.Count; index++)
            _checkpoints.Remove(cells[index].CellId);
    }

    public int GetReplayStartIndex(IReadOnlyList<NotebookCellExecutionIdentity> cells, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count == 0)
            return 0;

        targetIndex = Math.Clamp(targetIndex, 0, cells.Count - 1);

        for (var index = 0; index <= targetIndex; index++)
        {
            if (!HasValidCheckpoint(cells[index]))
                return index;
        }

        return targetIndex;
    }

    public ScriptExecutionCheckpoint? GetPreviousCheckpoint(
        IReadOnlyList<NotebookCellExecutionIdentity> cells,
        int cellIndex)
    {
        ArgumentNullException.ThrowIfNull(cells);

        for (var index = cellIndex - 1; index >= 0; index--)
        {
            var identity = cells[index];
            if (_checkpoints.TryGetValue(identity.CellId, out var entry) &&
                entry.Revision == identity.Revision)
            {
                return entry.Checkpoint;
            }
        }

        return null;
    }

    public void RecordSuccessfulRun(NotebookCellExecutionIdentity cell, ScriptExecutionCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        _checkpoints[cell.CellId] = new CheckpointEntry(cell.Revision, checkpoint);
    }

    public void RecordFailedRun(IReadOnlyList<NotebookCellExecutionIdentity> cells, int failedIndex)
        => InvalidateFrom(cells, failedIndex);

    public int GetValidFrontierIndex(IReadOnlyList<NotebookCellExecutionIdentity> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var frontier = -1;
        for (var index = 0; index < cells.Count; index++)
        {
            if (!HasValidCheckpoint(cells[index]))
                break;

            frontier = index;
        }

        return frontier;
    }

    public void Reset() => _checkpoints.Clear();

    private bool HasValidCheckpoint(NotebookCellExecutionIdentity cell)
        => _checkpoints.TryGetValue(cell.CellId, out var entry) &&
           entry.Revision == cell.Revision;

    private sealed record CheckpointEntry(int Revision, ScriptExecutionCheckpoint Checkpoint);
}

/// <summary>
/// Lightweight identity for a notebook cell revision tracked by <see cref="NotebookExecutionSession"/>.
/// </summary>
public sealed record NotebookCellExecutionIdentity(string CellId, int Revision);
