using System.Text.Json;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Storage.AssetOperations;

/// <summary>
/// Durable, rebuildable role and book-position projection state. This is not a ledger balance or
/// an accounting source of truth.
/// </summary>
public sealed record InstrumentPositionProjectionSnapshot(
    Guid SecurityId,
    IReadOnlyList<InstrumentRoleDto> InstrumentRoles,
    IReadOnlyList<BookPositionDto> BookPositions,
    IReadOnlyList<PositionEconomicStateDto> PositionEconomicStates,
    IReadOnlyList<ProjectionLineageDto> ProjectionLineages)
{
    public Guid? LedgerBookId { get; init; }

    public DateOnly? AsOfDate { get; init; }
}

public interface IInstrumentPositionProjectionStore
{
    Task<InstrumentPositionProjectionSnapshot> GetSecurityAsync(
        Guid securityId,
        CancellationToken ct = default);

    Task<InstrumentPositionProjectionSnapshot> GetAsOfAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly asOfDate,
        CancellationToken ct = default);

    Task<BookPositionDto?> GetBookPositionAsync(
        Guid positionId,
        CancellationToken ct = default);

    Task<BookPositionDto> UpsertAsync(
        InstrumentRoleDto role,
        BookPositionDto position,
        PositionEconomicStateDto? economicState,
        long expectedVersion,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default);
}

internal static class InstrumentPositionProjectionRules
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static (BookPositionDto Position, PositionEconomicStateDto? EconomicState) NormalizeWrite(
        BookPositionDto position,
        PositionEconomicStateDto? economicState)
    {
        var embeddedState = position.CurrentEconomicState;
        if (embeddedState is not null && economicState is not null &&
            !PayloadEquals(embeddedState, economicState))
        {
            throw new InvalidOperationException(
                "The embedded and explicit economic states must describe the same approved projection.");
        }

        var state = economicState ?? embeddedState;
        if (position.ProjectionLineage is not null && state?.ProjectionLineage is not null &&
            !PayloadEquals(position.ProjectionLineage, state.ProjectionLineage))
        {
            throw new InvalidOperationException(
                "The book-position and economic-state projection lineage must match.");
        }

        return (position with
        {
            CurrentEconomicState = state,
            ProjectionLineage = state?.ProjectionLineage ?? position.ProjectionLineage
        }, state);
    }

    public static void ValidateWrite(
        InstrumentRoleDto role,
        BookPositionDto position,
        PositionEconomicStateDto? economicState,
        long expectedVersion,
        AssetOperationsWriteApprovalDto approval)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(approval);

        if (expectedVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), "Expected version cannot be negative.");
        }

        ValidateRole(role, approval);

        if (position.EffectiveTo < position.EffectiveFrom)
        {
            throw new InvalidOperationException("Projection end dates cannot precede start dates.");
        }

        if (position.PositionId == Guid.Empty || position.SecurityId == Guid.Empty ||
            position.RoleId == Guid.Empty || position.BookContext.LedgerBookId == Guid.Empty ||
            position.BookContext.FundStructureNodeId == Guid.Empty || position.Version <= 0 ||
            string.IsNullOrWhiteSpace(position.BookContext.FundProfileId) ||
            string.IsNullOrWhiteSpace(position.PositionSide) ||
            string.IsNullOrWhiteSpace(position.Status))
        {
            throw new InvalidOperationException("Book positions require position, security, role, book, side, and version values.");
        }

        var expectedOwnerKind = position.BookContext.FundStructureNodeKind.ToString();
        if (role.RoleId != position.RoleId || role.SecurityId != position.SecurityId ||
            !ExactTextEquals(role.OwnerScopeId, position.BookContext.FundProfileId) ||
            !ExactTextEquals(role.OwnerScopeKind, expectedOwnerKind))
        {
            throw new InvalidOperationException("The instrument role and book position must have matching security and owner scope.");
        }

        ValidateRoleWindow(role, position);

        ValidatePositionProvenance(position);
        ValidateDimensions(position);
        ValidateEconomicState(position, economicState);
    }

    public static void ValidateDedicatedProvenance(
        InstrumentRoleDto role,
        BookPositionDto position,
        PositionEconomicStateDto? economicState)
    {
        if (role.OriginEvent is null || !HasEvidence(role.EvidenceLinks))
        {
            throw new InvalidOperationException(
                "Dedicated instrument-role writes require a retained source event and evidence.");
        }

        if (position.OriginEvent is null && position.ProjectionLineage is null ||
            !HasEvidence(position.EvidenceLinks))
        {
            throw new InvalidOperationException(
                "Dedicated book-position writes require retained event lineage and evidence.");
        }

        if (economicState is not null &&
            (economicState.SourceEvent is null && economicState.ProjectionLineage is null ||
             !HasEvidence(economicState.EvidenceLinks)))
        {
            throw new InvalidOperationException(
                "Dedicated economic-state writes require retained event lineage and evidence.");
        }
    }

    public static void ValidateRole(
        InstrumentRoleDto role,
        AssetOperationsWriteApprovalDto approval)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(approval);
        if (role.RoleId == Guid.Empty || role.SecurityId == Guid.Empty || role.Version <= 0 ||
            string.IsNullOrWhiteSpace(role.OwnerScopeId) || string.IsNullOrWhiteSpace(role.OwnerScopeKind) ||
            string.IsNullOrWhiteSpace(role.RoleKind) || string.IsNullOrWhiteSpace(role.AccountingSide) ||
            string.IsNullOrWhiteSpace(role.EconomicSide) || role.EffectiveTo < role.EffectiveFrom)
        {
            throw new InvalidOperationException(
                "Instrument role projections require identity, owner scope, role kind, valid dates, and a positive version.");
        }

        ValidateRoleProvenance(role);
        if (string.IsNullOrWhiteSpace(approval.Actor) || string.IsNullOrWhiteSpace(approval.ApprovalReference) ||
            string.IsNullOrWhiteSpace(approval.Rationale) || approval.ApprovedAt == default)
        {
            throw new InvalidOperationException("Instrument position writes require retained approval actor, reference, rationale, and timestamp.");
        }
    }

    public static void ValidateDimensions(BookPositionDto position)
    {
        var dimensions = position.BookContext.Dimensions;
        if (dimensions is null)
        {
            return;
        }

        if (dimensions.InstrumentId is Guid instrumentId && instrumentId != position.SecurityId ||
            dimensions.PositionId is Guid positionId && positionId != position.PositionId ||
            !string.IsNullOrWhiteSpace(dimensions.BookId) &&
            !TextEquals(dimensions.BookId, position.BookContext.LedgerBookId.ToString("D")) ||
            !string.IsNullOrWhiteSpace(dimensions.FundId) &&
            !TextEquals(dimensions.FundId, position.BookContext.FundProfileId))
        {
            throw new InvalidOperationException("Book-position dimensions conflict with the position security, position, or ledger-book identity.");
        }
    }

    public static void ValidateEconomicState(
        BookPositionDto position,
        PositionEconomicStateDto? state)
    {
        ValidatePositionProvenance(position);
        if (state is null)
        {
            return;
        }

        if (state.EconomicStateId == Guid.Empty || state.PositionId != position.PositionId ||
            state.Version <= 0 ||
            string.IsNullOrWhiteSpace(state.Currency))
        {
            throw new InvalidOperationException(
                "Economic states require identity, matching position, currency, and a positive version.");
        }

        if (state.AsOfDate < position.EffectiveFrom ||
            position.EffectiveTo is DateOnly positionEnd && state.AsOfDate > positionEnd)
        {
            throw new InvalidOperationException("Economic-state dates must fall inside the book position effective window.");
        }

        var source = state.SourceEvent;
        var lineage = state.ProjectionLineage;
        var positionSource = position.OriginEvent;
        var positionLineage = position.ProjectionLineage;
        if (source?.EventId == Guid.Empty ||
            positionSource?.SecurityId is Guid positionSourceSecurityId && positionSourceSecurityId != position.SecurityId ||
            positionSource?.BookPositionId is Guid positionSourcePositionId && positionSourcePositionId != position.PositionId ||
            positionLineage?.BookPositionId is Guid positionLineageId && positionLineageId != position.PositionId ||
            positionLineage?.TriggerEvent.SecurityId is Guid positionTriggerSecurityId && positionTriggerSecurityId != position.SecurityId ||
            positionLineage?.TriggerEvent.BookPositionId is Guid positionTriggerPositionId && positionTriggerPositionId != position.PositionId ||
            source?.SecurityId is Guid sourceSecurityId && sourceSecurityId != position.SecurityId ||
            source?.BookPositionId is Guid sourcePositionId && sourcePositionId != position.PositionId ||
            lineage?.BookPositionId is Guid lineagePositionId && lineagePositionId != position.PositionId ||
            lineage?.TriggerEvent.SecurityId is Guid triggerSecurityId && triggerSecurityId != position.SecurityId ||
            lineage?.TriggerEvent.BookPositionId is Guid triggerPositionId && triggerPositionId != position.PositionId)
        {
            throw new InvalidOperationException("Economic-state event or projection lineage crosses the position security or book-position boundary.");
        }

        if (source is not null && lineage is not null && source.EventId != lineage.TriggerEvent.EventId)
        {
            throw new InvalidOperationException(
                "Economic-state source event and projection trigger event must identify the same event.");
        }
    }

    public static void ValidateStandaloneLineage(BookPositionDto position, ProjectionLineageDto lineage)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        ValidateLineageBoundary(position, lineage);
    }

    public static bool IsActive(DateOnly from, DateOnly? to, DateOnly asOf)
        => from <= asOf && (to is null || to >= asOf);

    public static void ValidateRoleWindow(InstrumentRoleDto role, BookPositionDto position)
    {
        if (role.EffectiveFrom > position.EffectiveFrom ||
            role.EffectiveTo is DateOnly roleEnd &&
            (position.EffectiveTo is null || position.EffectiveTo > roleEnd))
        {
            throw new InvalidOperationException(
                "The instrument role effective window must cover the book position effective window.");
        }
    }

    public static bool RangesOverlap(DateOnly leftFrom, DateOnly? leftTo, DateOnly rightFrom, DateOnly? rightTo)
        => leftFrom <= (rightTo ?? DateOnly.MaxValue) && rightFrom <= (leftTo ?? DateOnly.MaxValue);

    public static bool SameOverlapScope(BookPositionDto left, BookPositionDto right)
        => left.SecurityId == right.SecurityId &&
           left.RoleId == right.RoleId &&
           left.BookContext.LedgerBookId == right.BookContext.LedgerBookId &&
           TextEquals(left.BookContext.FundProfileId, right.BookContext.FundProfileId) &&
           left.BookContext.FundStructureNodeKind == right.BookContext.FundStructureNodeKind &&
           TextEquals(left.PositionSide, right.PositionSide);

    public static bool ParticipatesInActiveOverlap(string? status)
        => !TextEquals(status, "Closed") &&
           !TextEquals(status, "Inactive") &&
           !TextEquals(status, "Terminated") &&
           !TextEquals(status, "Matured");

    public static bool TextEquals(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool ExactTextEquals(string? left, string? right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private static void ValidateRoleProvenance(InstrumentRoleDto role)
    {
        if (role.OriginEvent is not null &&
            (role.OriginEvent.EventId == Guid.Empty ||
             role.OriginEvent.SecurityId is Guid securityId && securityId != role.SecurityId))
        {
            throw new InvalidOperationException(
                "Instrument-role provenance crosses the Security Master boundary.");
        }
    }

    private static void ValidatePositionProvenance(BookPositionDto position)
    {
        if (position.OriginEvent is not null &&
            (position.OriginEvent.EventId == Guid.Empty ||
             position.OriginEvent.SecurityId is Guid securityId && securityId != position.SecurityId ||
             position.OriginEvent.BookPositionId is Guid positionId && positionId != position.PositionId))
        {
            throw new InvalidOperationException(
                "Book-position origin-event provenance crosses its security or position boundary.");
        }

        if (position.ProjectionLineage is not null)
        {
            ValidateLineageBoundary(position, position.ProjectionLineage);
        }
    }

    private static void ValidateLineageBoundary(BookPositionDto position, ProjectionLineageDto lineage)
    {
        if (lineage.ProjectionRunId == Guid.Empty || lineage.TriggerEvent.EventId == Guid.Empty ||
            (lineage.BookPositionId ?? lineage.TriggerEvent.BookPositionId) is not Guid ||
            lineage.BookPositionId is Guid lineagePositionId && lineagePositionId != position.PositionId ||
            lineage.TriggerEvent.SecurityId is Guid securityId && securityId != position.SecurityId ||
            lineage.TriggerEvent.BookPositionId is Guid triggerPositionId && triggerPositionId != position.PositionId)
        {
            throw new InvalidOperationException(
                "Projection lineage crosses the book-position security or position boundary.");
        }
    }

    private static bool PayloadEquals<T>(T left, T right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, JsonOptions),
            JsonSerializer.SerializeToElement(right, JsonOptions));

    private static bool HasEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Count > 0 && evidenceLinks.All(static link => !string.IsNullOrWhiteSpace(link));
}
