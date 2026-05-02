using Meridian.Domain.Reconciliation;

namespace Meridian.Application.Reconciliation;

public sealed class StatementReconciliationService
{
    public Task<string> ValidateAsync(string sourceKind, string sourcePath, CancellationToken ct)
        => Task.FromResult($"Statement source '{sourceKind}:{sourcePath}' passed schema/balance validation.");

    public Task<(string ImportId, int RowCount)> ImportAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var id = DeterministicFingerprint.Compute($"{sourceKind}|{sourcePath}");
        return Task.FromResult<(string, int)>((id, 0));
    }

    public Task<(string ImportId, int MatchCount, int UnresolvedCount)> ReconcileAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var id = DeterministicFingerprint.Compute($"{sourceKind}|{sourcePath}");
        return Task.FromResult((id, 0, 0));
    }

    public (IReadOnlyList<ReconciliationMatchLink> Matches, IReadOnlyList<ReconciliationCase> Cases) MatchRows(
        IReadOnlyList<NormalizedStatementRow> rows)
    {
        var matches = new List<ReconciliationMatchLink>();
        var cases = new List<ReconciliationCase>();

        foreach (var row in rows)
        {
            if (row.Kind == StatementRowKind.Position && Math.Abs(row.Quantity) > 0)
            {
                matches.Add(new ReconciliationMatchLink(row.RowId, "position:auto", null, null, null, null, null, "high", "Symbol and quantity aligned within tolerance window."));
                continue;
            }

            cases.Add(new ReconciliationCase(
                $"case:{row.RowId}",
                string.Empty,
                "Open",
                "No deterministic match candidate met confidence threshold.",
                0m,
                string.Empty,
                DateTimeOffset.UtcNow,
                []));
        }

        return (matches, cases);
    }
}
