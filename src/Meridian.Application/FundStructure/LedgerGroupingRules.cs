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
            .GroupBy(static x => x.NodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static assignment => LedgerGroupId.Create(assignment.AssignmentReference)).First());

    public static LedgerGroupId ResolveLedgerGroupId(
        AccountSummaryDto account,
        IReadOnlyDictionary<Guid, LedgerGroupId> ledgerAssignments)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(ledgerAssignments);

        foreach (var candidateNodeId in GetAssignmentCandidateNodeIds(account))
        {
            if (ledgerAssignments.TryGetValue(candidateNodeId, out var assignedLedger))
            {
                return assignedLedger;
            }
        }

        if (TryParse(account.LedgerReference) is { } fromAccount)
        {
            return fromAccount;
        }

        return LedgerGroupId.Unassigned;
    }

    private static LedgerGroupId? TryParse(string? value) =>
        LedgerGroupId.TryCreate(value, out var parsed) ? parsed : null;

    private static IEnumerable<Guid> GetAssignmentCandidateNodeIds(AccountSummaryDto account)
    {
        var seen = new HashSet<Guid>();

        if (seen.Add(account.AccountId))
        {
            yield return account.AccountId;
        }

        if (TryParseGuid(account.PortfolioId, out var portfolioId) && seen.Add(portfolioId))
        {
            yield return portfolioId;
        }

        if (account.SleeveId is Guid sleeveId && seen.Add(sleeveId))
        {
            yield return sleeveId;
        }

        if (account.VehicleId is Guid vehicleId && seen.Add(vehicleId))
        {
            yield return vehicleId;
        }

        if (account.FundId is Guid fundId && seen.Add(fundId))
        {
            yield return fundId;
        }

        if (account.EntityId is Guid entityId && seen.Add(entityId))
        {
            yield return entityId;
        }
    }

    private static bool TryParseGuid(string? value, out Guid guid) =>
        Guid.TryParse(value, out guid);
}
