using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public static class LedgerGroupingRules
{
    public const string LedgerGroupAssignmentType = "LedgerGroup";

    private static readonly StringComparer AssignmentComparer = StringComparer.OrdinalIgnoreCase;

    public static bool IsLedgerGroupAssignmentType(string? assignmentType) =>
        AssignmentComparer.Equals(assignmentType, LedgerGroupAssignmentType);

    public static string NormalizeAssignmentReference(string assignmentType, string assignmentReference) =>
        IsLedgerGroupAssignmentType(assignmentType)
            ? LedgerGroupId.Normalize(assignmentReference)
            : assignmentReference;

    public static IReadOnlyDictionary<Guid, LedgerGroupId> BuildLedgerAssignments(
        IReadOnlyList<FundStructureAssignmentDto> assignments) =>
        assignments
            .Where(assignment => IsLedgerGroupAssignmentType(assignment.AssignmentType))
            .GroupBy(static assignment => assignment.NodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static assignment => LedgerGroupId.Create(assignment.AssignmentReference)).First());

    public static LedgerGroupId ResolveLedgerGroupId(
        AccountSummaryDto account,
        IReadOnlyDictionary<Guid, LedgerGroupId> ledgerAssignments)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(ledgerAssignments);

        if (ledgerAssignments.TryGetValue(account.AccountId, out var assignedLedger))
        {
            return assignedLedger;
        }

        if (TryParseGuid(account.PortfolioId, out var portfolioId)
            && ledgerAssignments.TryGetValue(portfolioId, out assignedLedger))
        {
            return assignedLedger;
        }

        if (account.SleeveId is Guid sleeveId
            && ledgerAssignments.TryGetValue(sleeveId, out assignedLedger))
        {
            return assignedLedger;
        }

        if (account.VehicleId is Guid vehicleId
            && ledgerAssignments.TryGetValue(vehicleId, out assignedLedger))
        {
            return assignedLedger;
        }

        if (account.FundId is Guid fundId
            && ledgerAssignments.TryGetValue(fundId, out assignedLedger))
        {
            return assignedLedger;
        }

        if (account.EntityId is Guid entityId
            && ledgerAssignments.TryGetValue(entityId, out assignedLedger))
        {
            return assignedLedger;
        }

        if (LedgerGroupId.TryCreate(account.LedgerReference, out var fromAccount))
        {
            return fromAccount;
        }

        return LedgerGroupId.Unassigned;
    }

    private static bool TryParseGuid(string? value, out Guid guid) =>
        Guid.TryParse(value, out guid);
}
