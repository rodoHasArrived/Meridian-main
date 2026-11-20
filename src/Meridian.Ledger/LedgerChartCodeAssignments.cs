using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Protects retained account codes when replacing a scoped chart.</summary>
public static class LedgerChartCodeAssignments
{
    /// <summary>Requires an already assigned code to remain on the same node.</summary>
    public static void RequireUnchanged(
        IReadOnlyList<ChartOfAccountsNodeDto> retained,
        IReadOnlyList<ChartOfAccountsNodeDto> proposed)
    {
        foreach (var node in retained)
        {
            if (string.IsNullOrWhiteSpace(node.AccountCode))
                continue;

            var replacement = proposed.FirstOrDefault(candidate =>
                string.Equals(candidate.NodeId, node.NodeId, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(node.AccountCode.Trim(), replacement?.AccountCode?.Trim(), StringComparison.Ordinal))
            {
                throw new LedgerChartCodeConflictException(
                    $"Account code '{node.AccountCode}' is already assigned to node '{node.NodeId}'. Reload the chart before saving.");
            }
        }
    }
}

/// <summary>Reports that a chart replacement would lose a retained code assignment.</summary>
public sealed class LedgerChartCodeConflictException(string message) : InvalidOperationException(message);
