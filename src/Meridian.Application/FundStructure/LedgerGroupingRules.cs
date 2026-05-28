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

    public static LedgerMappingResolutionDto ResolveLedgerGroupMapping(
        AccountSummaryDto account,
        IReadOnlyList<FundStructureAssignmentDto> assignments)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assignments);

        if (TryResolveAssignment(
                account.AccountId,
                FundStructureNodeKindDto.Account,
                LedgerMappingSourceDto.AccountAssignment,
                assignments,
                out var resolution))
        {
            return resolution;
        }

        if (TryParseGuid(account.PortfolioId, out var portfolioId)
            && TryResolveAssignment(
                portfolioId,
                FundStructureNodeKindDto.InvestmentPortfolio,
                LedgerMappingSourceDto.InvestmentPortfolioAssignment,
                assignments,
                out resolution))
        {
            return resolution;
        }

        if (account.SleeveId is Guid sleeveId
            && TryResolveAssignment(
                sleeveId,
                FundStructureNodeKindDto.Sleeve,
                LedgerMappingSourceDto.SleeveAssignment,
                assignments,
                out resolution))
        {
            return resolution;
        }

        if (account.VehicleId is Guid vehicleId
            && TryResolveAssignment(
                vehicleId,
                FundStructureNodeKindDto.Vehicle,
                LedgerMappingSourceDto.VehicleAssignment,
                assignments,
                out resolution))
        {
            return resolution;
        }

        if (account.FundId is Guid fundId
            && TryResolveAssignment(
                fundId,
                FundStructureNodeKindDto.Fund,
                LedgerMappingSourceDto.FundAssignment,
                assignments,
                out resolution))
        {
            return resolution;
        }

        if (account.EntityId is Guid entityId
            && TryResolveAssignment(
                entityId,
                FundStructureNodeKindDto.Entity,
                LedgerMappingSourceDto.EntityAssignment,
                assignments,
                out resolution))
        {
            return resolution;
        }

        if (LedgerGroupId.TryCreate(account.LedgerReference, out var fromAccount))
        {
            return new LedgerMappingResolutionDto(
                fromAccount,
                LedgerMappingSourceDto.AccountLedgerReference,
                account.AccountId,
                FundStructureNodeKindDto.Account,
                account.LedgerReference,
                RequiresUserMapping: false,
                IssueCodes: []);
        }

        var issueCode = string.IsNullOrWhiteSpace(account.LedgerReference)
            ? "ledger-mapping.missing"
            : "ledger-mapping.invalid-ledger-reference";

        return new LedgerMappingResolutionDto(
            LedgerGroupId.Unassigned,
            LedgerMappingSourceDto.Unassigned,
            account.AccountId,
            FundStructureNodeKindDto.Account,
            account.LedgerReference,
            RequiresUserMapping: true,
            IssueCodes: [issueCode]);
    }

    private static bool TryResolveAssignment(
        Guid nodeId,
        FundStructureNodeKindDto nodeKind,
        LedgerMappingSourceDto source,
        IReadOnlyList<FundStructureAssignmentDto> assignments,
        out LedgerMappingResolutionDto resolution)
    {
        var assignment = assignments.FirstOrDefault(assignment =>
            assignment.NodeId == nodeId &&
            IsLedgerGroupAssignmentType(assignment.AssignmentType));

        if (assignment is null)
        {
            resolution = default!;
            return false;
        }

        resolution = new LedgerMappingResolutionDto(
            LedgerGroupId.Create(assignment.AssignmentReference),
            source,
            nodeId,
            nodeKind,
            assignment.AssignmentReference,
            RequiresUserMapping: false,
            IssueCodes: []);
        return true;
    }

    private static bool TryParseGuid(string? value, out Guid guid) =>
        Guid.TryParse(value, out guid);
}
