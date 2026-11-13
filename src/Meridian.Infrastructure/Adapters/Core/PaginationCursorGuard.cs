namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Rejects repeated continuation cursors without logging credential-bearing values.</summary>
internal sealed class PaginationCursorGuard(string provider)
{
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

    public void Visit(string? cursor)
    {
        if (!string.IsNullOrEmpty(cursor) && !_visited.Add(cursor))
            throw new InvalidOperationException($"{provider} pagination repeated a continuation cursor.");
    }
}
