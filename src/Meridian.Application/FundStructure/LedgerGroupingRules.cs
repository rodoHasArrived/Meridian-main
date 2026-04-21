using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

internal static class LedgerGroupingRules
{
    private static readonly StringComparer AssignmentComparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyDictionary<Guid, LedgerGroupId> BuildLedgerAssignments(
        IReadOnlyList<FundStructureAssignmentDto> assignments) =>
        assignments
            .Where(assignment => AssignmentComparer.Equals(assignment.AssignmentType, "LedgerGroup"))
            .Select(assignment => (assignment.NodeId, GroupId: TryParse(assignment.AssignmentReference)))
            .Where(static x => x.GroupId.HasValue)
            .GroupBy(static x => x.NodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static x => x.GroupId!.Value).First());

    public static LedgerGroupId ResolveLedgerGroupId(
        AccountSummaryDto account,
        IReadOnlyDictionary<Guid, LedgerGroupId> ledgerAssignments)
    {
        if (ledgerAssignments.TryGetValue(account.AccountId, out var assignedLedger))
        {
            return assignedLedger;
        }

        if (TryParse(account.LedgerReference) is { } fromAccount)
        {
            return fromAccount;
        }

        return LedgerGroupId.Unassigned;
    }

    private static LedgerGroupId? TryParse(string? value) =>
        LedgerGroupId.TryCreate(value, out var parsed) ? parsed : null;
}
