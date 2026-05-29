using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public sealed class FundStructurePolicyService : IFundStructurePolicyService
{
    public const int MaxCashFlowWindowDays = 3650;
    public const int MaxCashFlowBucketDays = 365;
    public void EnsureSingleOperatingParent(CreateInvestmentPortfolioRequest request)
    {
        var assignedParents = 0;
        if (request.ClientId.HasValue) assignedParents++;
        if (request.FundId.HasValue) assignedParents++;
        if (request.SleeveId.HasValue) assignedParents++;
        if (request.VehicleId.HasValue) assignedParents++;
        if (request.EntityId.HasValue) assignedParents++;

        if (assignedParents > 1)
        {
            throw new InvalidOperationException("Investment portfolios can only be assigned to one operating parent.");
        }
    }

    public void ValidateOwnershipLink(
        OwnershipLinkDto candidate,
        FundStructureNodeKindDto parentKind,
        FundStructureNodeKindDto childKind,
        IReadOnlyCollection<OwnershipLinkDto> existingLinks,
        IReadOnlyDictionary<Guid, FundStructureNodeKindDto> nodeKinds)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingLinks);
        ArgumentNullException.ThrowIfNull(nodeKinds);

        if (candidate.ParentNodeId == candidate.ChildNodeId)
        {
            throw new InvalidOperationException("Ownership parent and child must be different nodes.");
        }

        ValidateOwnershipWindow(candidate.EffectiveFrom, candidate.EffectiveTo);
        EnsureRelationshipIsCompatible(candidate.RelationshipType, parentKind, childKind);
        EnsurePrimaryUniqueness(candidate, existingLinks);
        EnsureOwnershipPercent(candidate, existingLinks);
        EnsureNoActiveCycle(candidate, existingLinks);
        EnsureKnownKinds(existingLinks, nodeKinds);
    }

    public void ValidateOwnershipWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new InvalidOperationException("Ownership EffectiveTo cannot precede EffectiveFrom.");
        }
    }

    public void ValidateOwnershipReplacement(OwnershipLinkDto existingLink, OwnershipLinkDto replacementLink)
    {
        ArgumentNullException.ThrowIfNull(existingLink);
        ArgumentNullException.ThrowIfNull(replacementLink);

        ValidateOwnershipWindow(existingLink.EffectiveFrom, existingLink.EffectiveTo);
        ValidateOwnershipWindow(replacementLink.EffectiveFrom, replacementLink.EffectiveTo);

        if (existingLink.OwnershipLinkId == replacementLink.OwnershipLinkId)
        {
            throw new InvalidOperationException("Replacement ownership links must use a new identifier.");
        }

        if (existingLink.ParentNodeId != replacementLink.ParentNodeId
            || existingLink.ChildNodeId != replacementLink.ChildNodeId
            || existingLink.RelationshipType != replacementLink.RelationshipType)
        {
            throw new InvalidOperationException("Replacement ownership links must amend the same parent, child, and relationship type.");
        }

        if (!existingLink.EffectiveTo.HasValue)
        {
            throw new InvalidOperationException("The existing ownership link must be expired before replacement.");
        }

        if (existingLink.EffectiveTo.Value > replacementLink.EffectiveFrom)
        {
            throw new InvalidOperationException("Replacement ownership windows cannot overlap the expired link.");
        }
    }

    public void ValidateCashFlowQuery(GovernanceCashFlowQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.HistoricalDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "HistoricalDays must be greater than or equal to zero.");
        }

        if (query.ForecastDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "ForecastDays must be greater than or equal to zero.");
        }

        if (query.HistoricalDays > MaxCashFlowWindowDays)
        {
            throw new ArgumentOutOfRangeException(nameof(query), $"HistoricalDays must be less than or equal to {MaxCashFlowWindowDays}.");
        }

        if (query.ForecastDays > MaxCashFlowWindowDays)
        {
            throw new ArgumentOutOfRangeException(nameof(query), $"ForecastDays must be less than or equal to {MaxCashFlowWindowDays}.");
        }

        if (query.BucketDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "BucketDays must be greater than zero.");
        }

        if (query.BucketDays > MaxCashFlowBucketDays)
        {
            throw new ArgumentOutOfRangeException(nameof(query), $"BucketDays must be less than or equal to {MaxCashFlowBucketDays}.");
        }
    }

    private static void EnsureRelationshipIsCompatible(
        OwnershipRelationshipTypeDto relationshipType,
        FundStructureNodeKindDto parentKind,
        FundStructureNodeKindDto childKind)
    {
        var compatible = relationshipType switch
        {
            OwnershipRelationshipTypeDto.Owns =>
                (parentKind, childKind) is (FundStructureNodeKindDto.Organization, FundStructureNodeKindDto.Business)
                    or (FundStructureNodeKindDto.Client, FundStructureNodeKindDto.InvestmentPortfolio)
                    or (FundStructureNodeKindDto.Fund, FundStructureNodeKindDto.Vehicle)
                    or (FundStructureNodeKindDto.Fund, FundStructureNodeKindDto.Entity)
                    or (FundStructureNodeKindDto.Vehicle, FundStructureNodeKindDto.InvestmentPortfolio)
                    or (FundStructureNodeKindDto.Entity, FundStructureNodeKindDto.Vehicle)
                    or (FundStructureNodeKindDto.Entity, FundStructureNodeKindDto.InvestmentPortfolio)
                    or (FundStructureNodeKindDto.Fund, FundStructureNodeKindDto.Account)
                    or (FundStructureNodeKindDto.Vehicle, FundStructureNodeKindDto.Account)
                    or (FundStructureNodeKindDto.Entity, FundStructureNodeKindDto.Account),
            OwnershipRelationshipTypeDto.Advises =>
                (parentKind, childKind) is (FundStructureNodeKindDto.Business, FundStructureNodeKindDto.Client),
            OwnershipRelationshipTypeDto.Operates =>
                (parentKind, childKind) is (FundStructureNodeKindDto.Business, FundStructureNodeKindDto.Fund)
                    or (FundStructureNodeKindDto.Business, FundStructureNodeKindDto.InvestmentPortfolio)
                    or (FundStructureNodeKindDto.Fund, FundStructureNodeKindDto.Account)
                    or (FundStructureNodeKindDto.Sleeve, FundStructureNodeKindDto.Account)
                    or (FundStructureNodeKindDto.Vehicle, FundStructureNodeKindDto.Account)
                    or (FundStructureNodeKindDto.InvestmentPortfolio, FundStructureNodeKindDto.Account),
            OwnershipRelationshipTypeDto.ClearsFor or OwnershipRelationshipTypeDto.CustodiesFor =>
                childKind == FundStructureNodeKindDto.Account && IsAccountRelationshipParent(parentKind),
            OwnershipRelationshipTypeDto.AllocatesTo =>
                (parentKind, childKind) is (FundStructureNodeKindDto.Fund, FundStructureNodeKindDto.Sleeve)
                    or (FundStructureNodeKindDto.Fund, FundStructureNodeKindDto.InvestmentPortfolio)
                    or (FundStructureNodeKindDto.Sleeve, FundStructureNodeKindDto.InvestmentPortfolio),
            _ => false
        };

        if (!compatible)
        {
            throw new InvalidOperationException(
                $"Relationship {relationshipType} is not compatible with parent kind {parentKind} and child kind {childKind}.");
        }
    }

    private static bool IsAccountRelationshipParent(FundStructureNodeKindDto parentKind) =>
        parentKind is FundStructureNodeKindDto.Fund
            or FundStructureNodeKindDto.Sleeve
            or FundStructureNodeKindDto.Vehicle
            or FundStructureNodeKindDto.Entity
            or FundStructureNodeKindDto.InvestmentPortfolio;

    private static void EnsurePrimaryUniqueness(
        OwnershipLinkDto candidate,
        IReadOnlyCollection<OwnershipLinkDto> existingLinks)
    {
        if (!candidate.IsPrimary)
        {
            return;
        }

        var overlaps = existingLinks.Any(link =>
            link.OwnershipLinkId != candidate.OwnershipLinkId
            && link.IsPrimary
            && link.ChildNodeId == candidate.ChildNodeId
            && link.RelationshipType == candidate.RelationshipType
            && WindowsOverlap(candidate, link));

        if (overlaps)
        {
            throw new InvalidOperationException(
                "Only one active primary ownership link is allowed for the same child and relationship type.");
        }
    }

    private static void EnsureOwnershipPercent(
        OwnershipLinkDto candidate,
        IReadOnlyCollection<OwnershipLinkDto> existingLinks)
    {
        if (candidate.OwnershipPercent is < 0m or > 100m)
        {
            throw new InvalidOperationException("Ownership percentages must be between 0 and 100.");
        }

        if (!candidate.OwnershipPercent.HasValue || !RelationshipUsesPercent(candidate.RelationshipType))
        {
            return;
        }

        var siblingTotal = existingLinks
            .Where(link => link.OwnershipLinkId != candidate.OwnershipLinkId)
            .Where(link => link.ParentNodeId == candidate.ParentNodeId)
            .Where(link => link.RelationshipType == candidate.RelationshipType)
            .Where(link => link.OwnershipPercent.HasValue)
            .Where(link => WindowsOverlap(candidate, link))
            .Sum(link => link.OwnershipPercent!.Value);

        if (siblingTotal + candidate.OwnershipPercent.Value > 100m)
        {
            throw new InvalidOperationException(
                "Active sibling ownership percentages cannot exceed 100 for the same parent and relationship type.");
        }
    }

    private static void EnsureNoActiveCycle(
        OwnershipLinkDto candidate,
        IReadOnlyCollection<OwnershipLinkDto> existingLinks)
    {
        var activeLinks = existingLinks
            .Where(link => link.OwnershipLinkId != candidate.OwnershipLinkId)
            .Where(link => IsActiveAt(link, candidate.EffectiveFrom))
            .ToList();

        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(candidate.ChildNodeId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var next in activeLinks.Where(link => link.ParentNodeId == current).Select(link => link.ChildNodeId))
            {
                if (next == candidate.ParentNodeId)
                {
                    throw new InvalidOperationException("Ownership links cannot create cycles through active links.");
                }

                stack.Push(next);
            }
        }
    }

    private static void EnsureKnownKinds(
        IReadOnlyCollection<OwnershipLinkDto> existingLinks,
        IReadOnlyDictionary<Guid, FundStructureNodeKindDto> nodeKinds)
    {
        foreach (var link in existingLinks)
        {
            if (!nodeKinds.ContainsKey(link.ParentNodeId) || !nodeKinds.ContainsKey(link.ChildNodeId))
            {
                throw new InvalidOperationException("Ownership validation requires node kind metadata for every active link.");
            }
        }
    }

    private static bool RelationshipUsesPercent(OwnershipRelationshipTypeDto relationshipType) =>
        relationshipType is OwnershipRelationshipTypeDto.Owns or OwnershipRelationshipTypeDto.AllocatesTo;

    private static bool IsActiveAt(OwnershipLinkDto link, DateTimeOffset instant) =>
        link.EffectiveFrom <= instant && (!link.EffectiveTo.HasValue || link.EffectiveTo.Value > instant);

    private static bool WindowsOverlap(OwnershipLinkDto left, OwnershipLinkDto right)
    {
        var leftTo = left.EffectiveTo ?? DateTimeOffset.MaxValue;
        var rightTo = right.EffectiveTo ?? DateTimeOffset.MaxValue;
        return left.EffectiveFrom < rightTo && right.EffectiveFrom < leftTo;
    }
}
