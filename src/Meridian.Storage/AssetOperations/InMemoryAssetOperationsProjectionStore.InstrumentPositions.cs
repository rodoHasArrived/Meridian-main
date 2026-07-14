using System.Text.Json;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Storage.AssetOperations;

public sealed partial class InMemoryAssetOperationsProjectionStore
{
    private static readonly JsonSerializerOptions InstrumentPositionJsonOptions = new(JsonSerializerDefaults.Web);

    private Dictionary<Guid, InstrumentRoleDto> _instrumentRoles = [];
    private Dictionary<Guid, BookPositionDto> _bookPositions = [];
    private Dictionary<Guid, PositionEconomicStateDto> _positionEconomicStates = [];
    private Dictionary<Guid, ProjectionLineageDto> _projectionLineages = [];
    private Dictionary<Guid, AssetOperationsWriteApprovalDto> _projectionApprovals = [];

    public Task<InstrumentPositionProjectionSnapshot> GetSecurityAsync(
        Guid securityId,
        CancellationToken ct = default)
    {
        if (securityId == Guid.Empty)
        {
            throw new ArgumentException("A Security Master identity is required.", nameof(securityId));
        }

        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ClonePayload(BuildSecuritySnapshot(securityId)));
        }
    }

    public Task<InstrumentPositionProjectionSnapshot> GetAsOfAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        if (securityId == Guid.Empty)
        {
            throw new ArgumentException("A Security Master identity is required.", nameof(securityId));
        }

        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("A ledger-book identity is required.", nameof(ledgerBookId));
        }

        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ClonePayload(BuildSnapshot(securityId, ledgerBookId, asOfDate)));
        }
    }

    public Task<BookPositionDto?> GetBookPositionAsync(
        Guid positionId,
        CancellationToken ct = default)
    {
        if (positionId == Guid.Empty)
        {
            throw new ArgumentException("A book-position identity is required.", nameof(positionId));
        }

        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_bookPositions.TryGetValue(positionId, out var position))
            {
                return Task.FromResult<BookPositionDto?>(null);
            }

            var states = OrderedStates(PositionIdSet(positionId));
            var lineages = OrderedLineages(PositionIdSet(positionId));
            return Task.FromResult<BookPositionDto?>(ClonePayload(HydratePosition(position, states, lineages)));
        }
    }

    public Task<BookPositionDto> UpsertAsync(
        InstrumentRoleDto role,
        BookPositionDto position,
        PositionEconomicStateDto? economicState,
        long expectedVersion,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        var normalized = InstrumentPositionProjectionRules.NormalizeWrite(position, economicState);
        var state = normalized.EconomicState;
        var normalizedPosition = normalized.Position;
        if (state is not null && state.Version != normalizedPosition.Version)
        {
            throw new InvalidOperationException(
                "A dedicated economic-state write must use the same version as its book position.");
        }

        InstrumentPositionProjectionRules.ValidateWrite(
            role,
            normalizedPosition,
            state,
            expectedVersion,
            approval);
        InstrumentPositionProjectionRules.ValidateDedicatedProvenance(role, normalizedPosition, state);
        EnsureRoleCoversPosition(role, normalizedPosition);
        role = ClonePayload(role);
        normalizedPosition = ClonePayload(normalizedPosition);
        state = state is null ? null : ClonePayload(state);
        approval = ClonePayload(approval);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var roles = new Dictionary<Guid, InstrumentRoleDto>(_instrumentRoles);
            var positions = new Dictionary<Guid, BookPositionDto>(_bookPositions);
            var states = new Dictionary<Guid, PositionEconomicStateDto>(_positionEconomicStates);
            var lineages = new Dictionary<Guid, ProjectionLineageDto>(_projectionLineages);
            var approvals = new Dictionary<Guid, AssetOperationsWriteApprovalDto>(_projectionApprovals);

            if (IsExactReplay(
                    roles,
                    positions,
                    states,
                    lineages,
                    role,
                    normalizedPosition,
                    state,
                    expectedVersion))
            {
                return Task.FromResult(ClonePayload(HydratePosition(
                    positions[position.PositionId],
                    OrderedStates(PositionIdSet(position.PositionId), states),
                    OrderedLineages(PositionIdSet(position.PositionId), lineages))));
            }

            positions.TryGetValue(position.PositionId, out var persistedPosition);
            roles.TryGetValue(role.RoleId, out var persistedRole);
            ValidateDedicatedPositionVersion(persistedPosition, normalizedPosition, expectedVersion);
            var roleChanged = persistedRole is null || !PayloadEquals(persistedRole, role);
            var positionChanged = persistedPosition is null || !PayloadEquals(persistedPosition, normalizedPosition);
            var stateChanged = state is not null && !states.ContainsKey(state.EconomicStateId);
            ApplyRole(roles, role, requireConsecutiveVersion: true);
            EnsurePositionOwnership(persistedPosition, normalizedPosition);
            EnsureRoleCoversPositions(positions.Values, role, normalizedPosition);
            EnsureStatesRemainWithinPosition(states.Values, normalizedPosition);
            EnsureNoOverlap(positions.Values, normalizedPosition);
            positions[normalizedPosition.PositionId] = normalizedPosition;
            AppendEconomicState(states, lineages, positions, normalizedPosition, state);
            AppendProjectionLineage(lineages, positions, normalizedPosition.ProjectionLineage);
            RetainApproval(approvals, role.RoleId, approval, roleChanged);
            RetainApproval(approvals, normalizedPosition.PositionId, approval, positionChanged);
            if (state is not null)
            {
                RetainApproval(approvals, state.EconomicStateId, approval, stateChanged);
            }

            _instrumentRoles = roles;
            _bookPositions = positions;
            _positionEconomicStates = states;
            _projectionLineages = lineages;
            _projectionApprovals = approvals;
            RefreshLegacyDetail(normalizedPosition.SecurityId);

            return Task.FromResult(ClonePayload(HydratePosition(
                normalizedPosition,
                OrderedStates(PositionIdSet(normalizedPosition.PositionId)),
                OrderedLineages(PositionIdSet(normalizedPosition.PositionId)))));
        }
    }

    private void ApplyLegacyInstrumentPositionProjection(
        AssetOperationsDetailDto detail,
        AssetOperationsWriteApprovalDto approval)
    {
        var roles = new Dictionary<Guid, InstrumentRoleDto>(_instrumentRoles);
        var positions = new Dictionary<Guid, BookPositionDto>(_bookPositions);
        var states = new Dictionary<Guid, PositionEconomicStateDto>(_positionEconomicStates);
        var lineages = new Dictionary<Guid, ProjectionLineageDto>(_projectionLineages);
        var approvals = new Dictionary<Guid, AssetOperationsWriteApprovalDto>(_projectionApprovals);

        foreach (var role in detail.InstrumentRoles)
        {
            if (role.SecurityId != detail.Subject.SecurityId)
            {
                throw new InvalidOperationException(
                    "Instrument roles must match the Asset Operations subject Security Master identity.");
            }

            ValidateLegacyRole(role, approval);
            roles.TryGetValue(role.RoleId, out var persistedRole);
            var changed = persistedRole is null || !PayloadEquals(persistedRole, role);
            ApplyRole(roles, role, requireConsecutiveVersion: false);
            RetainApproval(approvals, role.RoleId, approval, changed);
        }

        foreach (var position in detail.BookPositions)
        {
            if (position.SecurityId != detail.Subject.SecurityId)
            {
                throw new InvalidOperationException(
                    "Book positions must match the Asset Operations subject Security Master identity.");
            }

            if (!roles.TryGetValue(position.RoleId, out var role))
            {
                throw new InvalidOperationException(
                    $"Book position '{position.PositionId:D}' requires a persisted instrument role.");
            }

            var explicitState = position.CurrentEconomicState is not null
                ? detail.PositionEconomicStates.FirstOrDefault(candidate =>
                    candidate.EconomicStateId == position.CurrentEconomicState.EconomicStateId)
                : detail.PositionEconomicStates
                    .Where(candidate => candidate.PositionId == position.PositionId)
                    .OrderByDescending(static candidate => candidate.AsOfDate)
                    .ThenByDescending(static candidate => candidate.Version)
                    .ThenByDescending(static candidate => candidate.EconomicStateId)
                    .FirstOrDefault();
            var normalized = InstrumentPositionProjectionRules.NormalizeWrite(position, explicitState);
            var state = normalized.EconomicState;
            var normalizedPosition = normalized.Position;
            InstrumentPositionProjectionRules.ValidateWrite(
                role,
                normalizedPosition,
                state,
                positions.GetValueOrDefault(position.PositionId)?.Version ?? 0,
                approval);
            EnsureRoleCoversPosition(role, normalizedPosition);
            positions.TryGetValue(position.PositionId, out var persistedPosition);
            EnsurePositionOwnership(persistedPosition, normalizedPosition);
            if (persistedPosition is not null &&
                !PayloadEquals(persistedPosition, normalizedPosition) &&
                normalizedPosition.Version <= persistedPosition.Version)
            {
                throw new InvalidOperationException(
                    $"Book position '{position.PositionId:D}' is stale or conflicts with the persisted version.");
            }

            var positionChanged = persistedPosition is null || !PayloadEquals(persistedPosition, normalizedPosition);
            EnsureRoleCoversPositions(positions.Values, role, normalizedPosition);
            EnsureStatesRemainWithinPosition(states.Values, normalizedPosition);
            EnsureNoOverlap(positions.Values, normalizedPosition);
            positions[normalizedPosition.PositionId] = normalizedPosition;
            AppendEconomicState(states, lineages, positions, normalizedPosition, state);
            AppendProjectionLineage(lineages, positions, normalizedPosition.ProjectionLineage);
            RetainApproval(approvals, normalizedPosition.PositionId, approval, positionChanged);
            if (state is not null)
            {
                RetainApproval(
                    approvals,
                    state.EconomicStateId,
                    approval,
                    !_positionEconomicStates.ContainsKey(state.EconomicStateId));
            }
        }

        foreach (var role in detail.InstrumentRoles)
        {
            EnsureRoleCoversPositions(positions.Values, role, null);
        }

        foreach (var state in detail.PositionEconomicStates)
        {
            if (!positions.TryGetValue(state.PositionId, out var position))
            {
                throw new InvalidOperationException(
                    $"Economic state '{state.EconomicStateId:D}' requires a persisted book position.");
            }

            if (position.SecurityId != detail.Subject.SecurityId)
            {
                throw new InvalidOperationException(
                    "Economic states must match the Asset Operations subject Security Master identity.");
            }

            InstrumentPositionProjectionRules.ValidateEconomicState(position, state);
            AppendEconomicState(states, lineages, positions, position, state);
            RetainApproval(
                approvals,
                state.EconomicStateId,
                approval,
                !_positionEconomicStates.ContainsKey(state.EconomicStateId));
        }

        foreach (var lineage in detail.ProjectionLineages)
        {
            var positionId = lineage.BookPositionId ?? lineage.TriggerEvent.BookPositionId;
            if (positionId is not Guid durablePositionId ||
                !positions.TryGetValue(durablePositionId, out var position) ||
                position.SecurityId != detail.Subject.SecurityId)
            {
                throw new InvalidOperationException(
                    "Projection lineage must match a book position in the Asset Operations subject.");
            }

            InstrumentPositionProjectionRules.ValidateStandaloneLineage(position, lineage);
            AppendProjectionLineage(lineages, positions, lineage);
        }

        _instrumentRoles = roles;
        _bookPositions = positions;
        _positionEconomicStates = states;
        _projectionLineages = lineages;
        _projectionApprovals = approvals;
    }

    private InstrumentPositionProjectionSnapshot BuildSecuritySnapshot(Guid securityId)
        => BuildSnapshot(securityId, null, null);

    private InstrumentPositionProjectionSnapshot BuildSnapshot(
        Guid securityId,
        Guid? ledgerBookId,
        DateOnly? asOfDate)
    {
        var candidatePositions = _bookPositions.Values
            .Where(position => position.SecurityId == securityId)
            .Where(position => !ledgerBookId.HasValue || position.BookContext.LedgerBookId == ledgerBookId.Value)
            .Where(position => !asOfDate.HasValue || InstrumentPositionProjectionRules.IsActive(
                position.EffectiveFrom,
                position.EffectiveTo,
                asOfDate.Value))
            .ToArray();
        var candidateRoleIds = candidatePositions
            .Select(static position => position.RoleId)
            .ToHashSet();
        var roles = _instrumentRoles.Values
            .Where(role => role.SecurityId == securityId)
            .Where(role => !ledgerBookId.HasValue || candidateRoleIds.Contains(role.RoleId))
            .Where(role => !asOfDate.HasValue || InstrumentPositionProjectionRules.IsActive(
                role.EffectiveFrom,
                role.EffectiveTo,
                asOfDate.Value))
            .OrderBy(static role => role.EffectiveFrom)
            .ThenBy(static role => role.Version)
            .ThenBy(static role => role.RoleId)
            .ToArray();
        var activeRoleIds = roles.Select(static role => role.RoleId).ToHashSet();
        var positions = candidatePositions
            .Where(position => !asOfDate.HasValue || activeRoleIds.Contains(position.RoleId))
            .OrderBy(static position => position.BookContext.LedgerBookId)
            .ThenBy(static position => position.EffectiveFrom)
            .ThenBy(static position => position.Version)
            .ThenBy(static position => position.PositionId)
            .ToArray();
        var positionIds = positions.Select(static position => position.PositionId).ToHashSet();
        var states = OrderedStates(positionIds)
            .Where(state => !asOfDate.HasValue || state.AsOfDate <= asOfDate.Value)
            .ToArray();
        var lineages = OrderedLineages(positionIds)
            .Where(lineage => !asOfDate.HasValue || lineage.ProjectionAsOfDate <= asOfDate.Value)
            .ToArray();
        var hydratedPositions = positions
            .Select(position => HydratePosition(position, states, lineages))
            .ToArray();

        return new InstrumentPositionProjectionSnapshot(
            securityId,
            roles,
            hydratedPositions,
            states,
            lineages)
        {
            LedgerBookId = ledgerBookId,
            AsOfDate = asOfDate
        };
    }

    private void RefreshLegacyDetail(Guid securityId)
    {
        if (!_details.TryGetValue(securityId, out var detail))
        {
            return;
        }

        var snapshot = BuildSecuritySnapshot(securityId);
        _details[securityId] = detail with
        {
            InstrumentRoles = snapshot.InstrumentRoles,
            BookPositions = snapshot.BookPositions,
            PositionEconomicStates = snapshot.PositionEconomicStates,
            ProjectionLineages = snapshot.ProjectionLineages
        };
    }

    private IReadOnlyList<PositionEconomicStateDto> OrderedStates(
        IReadOnlySet<Guid> positionIds,
        IReadOnlyDictionary<Guid, PositionEconomicStateDto>? source = null)
        => (source ?? _positionEconomicStates).Values
            .Where(state => positionIds.Contains(state.PositionId))
            .OrderBy(static state => state.AsOfDate)
            .ThenBy(static state => state.Version)
            .ThenBy(static state => state.EconomicStateId)
            .ToArray();

    private static HashSet<Guid> PositionIdSet(Guid positionId) => [positionId];

    private IReadOnlyList<ProjectionLineageDto> OrderedLineages(
        IReadOnlySet<Guid> positionIds,
        IReadOnlyDictionary<Guid, ProjectionLineageDto>? source = null)
        => (source ?? _projectionLineages).Values
            .Where(lineage => lineage.BookPositionId is Guid positionId && positionIds.Contains(positionId) ||
                lineage.TriggerEvent.BookPositionId is Guid triggerPositionId && positionIds.Contains(triggerPositionId))
            .OrderBy(static lineage => lineage.ProjectionAsOfDate)
            .ThenBy(static lineage => lineage.GeneratedAtUtc)
            .ThenBy(static lineage => lineage.ProjectionRunId)
            .ToArray();

    private static BookPositionDto HydratePosition(
        BookPositionDto position,
        IReadOnlyList<PositionEconomicStateDto> states,
        IReadOnlyList<ProjectionLineageDto> lineages)
    {
        var currentState = states
            .Where(state => state.PositionId == position.PositionId)
            .OrderByDescending(static state => state.AsOfDate)
            .ThenByDescending(static state => state.Version)
            .ThenByDescending(static state => state.EconomicStateId)
            .FirstOrDefault();
        var currentLineage = currentState?.ProjectionLineage ?? lineages
            .Where(lineage => lineage.BookPositionId == position.PositionId ||
                lineage.TriggerEvent.BookPositionId == position.PositionId)
            .OrderByDescending(static lineage => lineage.ProjectionAsOfDate)
            .ThenByDescending(static lineage => lineage.GeneratedAtUtc)
            .ThenByDescending(static lineage => lineage.ProjectionRunId)
            .FirstOrDefault();

        return position with
        {
            CurrentEconomicState = currentState,
            ProjectionLineage = currentLineage
        };
    }

    private static void ValidateDedicatedPositionVersion(
        BookPositionDto? persisted,
        BookPositionDto incoming,
        long expectedVersion)
    {
        if (persisted is null)
        {
            if (expectedVersion != 0 || incoming.Version != 1)
            {
                throw new InvalidOperationException(
                    "New book positions require ExpectedVersion 0 and position Version 1.");
            }

            return;
        }

        if (expectedVersion != persisted.Version || incoming.Version != checked(persisted.Version + 1))
        {
            throw new InvalidOperationException(
                $"Book position '{incoming.PositionId:D}' is stale; expected persisted version {persisted.Version} and next version {persisted.Version + 1}.");
        }
    }

    private static bool IsExactReplay(
        IReadOnlyDictionary<Guid, InstrumentRoleDto> roles,
        IReadOnlyDictionary<Guid, BookPositionDto> positions,
        IReadOnlyDictionary<Guid, PositionEconomicStateDto> states,
        IReadOnlyDictionary<Guid, ProjectionLineageDto> lineages,
        InstrumentRoleDto role,
        BookPositionDto position,
        PositionEconomicStateDto? state,
        long expectedVersion)
    {
        if (expectedVersion != position.Version - 1 ||
            !roles.TryGetValue(role.RoleId, out var persistedRole) ||
            !positions.TryGetValue(position.PositionId, out var persistedPosition) ||
            !PayloadEquals(persistedRole, role) ||
            !PayloadEquals(persistedPosition, position))
        {
            return false;
        }

        if (state is not null &&
            (!states.TryGetValue(state.EconomicStateId, out var persistedState) ||
             !PayloadEquals(persistedState, state)))
        {
            return false;
        }

        var lineage = state?.ProjectionLineage ?? position.ProjectionLineage;
        return lineage is null ||
            lineages.TryGetValue(lineage.ProjectionRunId, out var persistedLineage) &&
            PayloadEquals(persistedLineage, lineage);
    }

    private static void ApplyRole(
        IDictionary<Guid, InstrumentRoleDto> roles,
        InstrumentRoleDto role,
        bool requireConsecutiveVersion)
    {
        if (!roles.TryGetValue(role.RoleId, out var persisted))
        {
            roles[role.RoleId] = role;
            return;
        }

        if (!SameRoleIdentity(persisted, role))
        {
            throw new InvalidOperationException(
                $"Instrument role '{role.RoleId:D}' cannot change security, owner scope, or role kind.");
        }

        if (!PayloadEquals(persisted, role) &&
            (role.Version <= persisted.Version ||
             requireConsecutiveVersion && role.Version != persisted.Version + 1))
        {
            throw new InvalidOperationException(
                $"Instrument role '{role.RoleId:D}' is stale or conflicts with the persisted version.");
        }

        roles[role.RoleId] = PayloadEquals(persisted, role) ? persisted : role;
    }

    private static void ValidateLegacyRole(
        InstrumentRoleDto role,
        AssetOperationsWriteApprovalDto approval)
        => InstrumentPositionProjectionRules.ValidateRole(role, approval);

    private static void EnsurePositionOwnership(
        BookPositionDto? persisted,
        BookPositionDto incoming)
    {
        if (persisted is null)
        {
            return;
        }

        if (persisted.SecurityId != incoming.SecurityId ||
            persisted.RoleId != incoming.RoleId ||
            persisted.BookContext.LedgerBookId != incoming.BookContext.LedgerBookId ||
            persisted.BookContext.FundStructureNodeId != incoming.BookContext.FundStructureNodeId ||
            persisted.BookContext.FundStructureNodeKind != incoming.BookContext.FundStructureNodeKind ||
            !InstrumentPositionProjectionRules.ExactTextEquals(
                persisted.BookContext.FundProfileId,
                incoming.BookContext.FundProfileId) ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.PositionSide, incoming.PositionSide))
        {
            throw new InvalidOperationException(
                $"Book position '{incoming.PositionId:D}' cannot cross its security, role, owner, side, or ledger-book boundary.");
        }
    }

    private static void EnsureNoOverlap(
        IEnumerable<BookPositionDto> positions,
        BookPositionDto incoming)
    {
        if (!InstrumentPositionProjectionRules.ParticipatesInActiveOverlap(incoming.Status))
        {
            return;
        }

        var overlap = positions.FirstOrDefault(position =>
            position.PositionId != incoming.PositionId &&
            InstrumentPositionProjectionRules.ParticipatesInActiveOverlap(position.Status) &&
            InstrumentPositionProjectionRules.SameOverlapScope(position, incoming) &&
            InstrumentPositionProjectionRules.RangesOverlap(
                position.EffectiveFrom,
                position.EffectiveTo,
                incoming.EffectiveFrom,
                incoming.EffectiveTo));
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"Book position '{incoming.PositionId:D}' overlaps active position '{overlap.PositionId:D}' in the same security, role, owner, side, and book scope.");
        }
    }

    private static void EnsureRoleCoversPosition(
        InstrumentRoleDto role,
        BookPositionDto position)
        => InstrumentPositionProjectionRules.ValidateRoleWindow(role, position);

    private static void EnsureRoleCoversPositions(
        IEnumerable<BookPositionDto> persistedPositions,
        InstrumentRoleDto role,
        BookPositionDto? replacement)
    {
        var positions = persistedPositions
            .Where(position => replacement is null || position.PositionId != replacement.PositionId)
            .Concat(replacement is null ? [] : [replacement]);
        foreach (var position in positions.Where(position => position.RoleId == role.RoleId))
        {
            EnsureRoleCoversPosition(role, position);
        }
    }

    private static void EnsureStatesRemainWithinPosition(
        IEnumerable<PositionEconomicStateDto> states,
        BookPositionDto position)
    {
        foreach (var state in states.Where(state => state.PositionId == position.PositionId))
        {
            InstrumentPositionProjectionRules.ValidateEconomicState(position, state);
        }
    }

    private static void AppendEconomicState(
        IDictionary<Guid, PositionEconomicStateDto> states,
        IDictionary<Guid, ProjectionLineageDto> lineages,
        IReadOnlyDictionary<Guid, BookPositionDto> positions,
        BookPositionDto position,
        PositionEconomicStateDto? state)
    {
        if (state is null)
        {
            return;
        }

        if (!positions.ContainsKey(state.PositionId))
        {
            throw new InvalidOperationException(
                $"Economic state '{state.EconomicStateId:D}' requires a persisted book position.");
        }

        if (states.TryGetValue(state.EconomicStateId, out var persisted))
        {
            InstrumentPositionProjectionRules.ValidateEconomicState(position, state);
            if (!PayloadEquals(persisted, state))
            {
                throw new InvalidOperationException(
                    $"Economic state '{state.EconomicStateId:D}' is append-only and cannot be replaced.");
            }

            AppendProjectionLineage(lineages, positions, state.ProjectionLineage);
            return;
        }

        InstrumentPositionProjectionRules.ValidateEconomicState(position, state);
        ValidateCompatibilityStateVersion(position, state);

        var conflictingVersion = states.Values.FirstOrDefault(candidate =>
            candidate.PositionId == state.PositionId &&
            candidate.Version == state.Version);
        if (conflictingVersion is not null)
        {
            throw new InvalidOperationException(
                $"Economic state version {state.Version} already exists for book position '{state.PositionId:D}'.");
        }

        states[state.EconomicStateId] = state;
        AppendProjectionLineage(lineages, positions, state.ProjectionLineage);
    }

    private static void AppendProjectionLineage(
        IDictionary<Guid, ProjectionLineageDto> lineages,
        IReadOnlyDictionary<Guid, BookPositionDto> positions,
        ProjectionLineageDto? lineage)
    {
        if (lineage is null)
        {
            return;
        }

        var positionId = lineage.BookPositionId ?? lineage.TriggerEvent.BookPositionId;
        if (positionId is not Guid durablePositionId || !positions.TryGetValue(durablePositionId, out var position))
        {
            throw new InvalidOperationException(
                $"Projection lineage '{lineage.ProjectionRunId:D}' requires a persisted book position.");
        }

        if (lineage.TriggerEvent.SecurityId is Guid securityId && securityId != position.SecurityId)
        {
            throw new InvalidOperationException(
                $"Projection lineage '{lineage.ProjectionRunId:D}' crosses the position security boundary.");
        }

        InstrumentPositionProjectionRules.ValidateStandaloneLineage(position, lineage);

        if (lineages.TryGetValue(lineage.ProjectionRunId, out var persisted) &&
            !PayloadEquals(persisted, lineage))
        {
            throw new InvalidOperationException(
                $"Projection lineage '{lineage.ProjectionRunId:D}' is append-only and cannot be replaced.");
        }

        lineages[lineage.ProjectionRunId] = persisted ?? lineage;
    }

    private static bool SameRoleIdentity(InstrumentRoleDto left, InstrumentRoleDto right)
        => left.SecurityId == right.SecurityId &&
           InstrumentPositionProjectionRules.ExactTextEquals(left.OwnerScopeId, right.OwnerScopeId) &&
           InstrumentPositionProjectionRules.ExactTextEquals(left.OwnerScopeKind, right.OwnerScopeKind) &&
           InstrumentPositionProjectionRules.ExactTextEquals(left.RoleKind, right.RoleKind);

    private static void RetainApproval(
        IDictionary<Guid, AssetOperationsWriteApprovalDto> approvals,
        Guid projectionId,
        AssetOperationsWriteApprovalDto approval,
        bool changed)
    {
        if (changed || !approvals.ContainsKey(projectionId))
        {
            approvals[projectionId] = approval;
        }
    }

    private static void ValidateCompatibilityStateVersion(
        BookPositionDto position,
        PositionEconomicStateDto state)
    {
        if (state.Version != position.Version && state.Version != checked(position.Version + 1))
        {
            throw new InvalidOperationException(
                "Compatibility economic-state writes must match the position version or its immediate successor.");
        }
    }

    internal AssetOperationsWriteApprovalDto? GetRetainedProjectionApproval(Guid projectionId)
    {
        lock (_gate)
        {
            var approval = _projectionApprovals.GetValueOrDefault(projectionId);
            return approval is null ? null : ClonePayload(approval);
        }
    }

    private static bool PayloadEquals<T>(T left, T right)
        => string.Equals(
            JsonSerializer.Serialize(left, InstrumentPositionJsonOptions),
            JsonSerializer.Serialize(right, InstrumentPositionJsonOptions),
            StringComparison.Ordinal);

    private static T ClonePayload<T>(T value)
        => JsonSerializer.Deserialize<T>(
               JsonSerializer.Serialize(value, InstrumentPositionJsonOptions),
               InstrumentPositionJsonOptions)
           ?? throw new InvalidOperationException($"Could not clone {typeof(T).Name}.");
}
