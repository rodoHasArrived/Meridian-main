using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.Onboarding;

/// <summary>
/// Owns bounded onboarding casework and reproducible comparison history. The only source dependency
/// is a read-only capture seam; neither readiness nor a packet can change accounting authority.
/// </summary>
public sealed class OnboardingWorkspaceService(
    IOnboardingWorkspaceStore store,
    IOnboardingSourceProvider sources,
    TimeProvider? clock = null)
{
    public const string ReadOnlyAuthorityPosture = "ExternalBooksReadOnly;AccountingAuthorityTransitionSeparatelyControlled";
    public const string ComparisonAlgorithmVersion = "onboarding-comparison/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "Balance", "Position", "Nav" };
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<OnboardingWorkspaceDto> CreateAsync(
        CreateOnboardingWorkspaceRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Required(actorId, "Actor");
        Required(request.Name, "Workspace name");
        ValidateScope(request.Scope);
        ValidateCriteria(request.Scope, request.Criteria, actorId);
        var now = _clock.GetUtcNow();
        var scope = Copy(request.Scope) with { AccountIds = request.Scope.AccountIds.Select(id => Guid.Parse(id).ToString("D")).ToArray() };
        var workspace = new OnboardingWorkspaceDto(
            Guid.NewGuid().ToString("N"), request.Name.Trim(), actorId.Trim(), scope, 1, 1, now, now,
            Copy(request.Criteria), [new(1, Copy(request.Criteria), actorId, now)], [], [], [], [], [],
            new("AwaitingSources", false, 0, [], [], [], 0, request.Criteria.MinimumReviewers, []),
            ReadOnlyAuthorityPosture);
        workspace = workspace with { Readiness = CalculateReadiness(workspace) };
        await store.SaveAsync(Copy(workspace), null, ct);
        return Copy(workspace);
    }

    public async Task<IReadOnlyList<OnboardingWorkspaceDto>> ListAsync(
        string tenantId, string companyId, CancellationToken ct = default)
    {
        ValidateEnterpriseScope(tenantId, companyId);
        var values = await store.ListAsync(tenantId, companyId, ct);
        if (values.Any(value => value.Scope.TenantId != tenantId || value.Scope.CompanyId != companyId))
            throw new InvalidOperationException("Onboarding storage returned a workspace outside the requested enterprise scope.");
        return values.Select(Copy).ToArray();
    }

    public async Task<OnboardingWorkspaceDto?> GetAsync(
        string tenantId, string companyId, string workspaceId, CancellationToken ct = default)
    {
        ValidateEnterpriseScope(tenantId, companyId);
        Required(workspaceId, "Workspace identity");
        var workspace = await store.GetAsync(tenantId, companyId, workspaceId, ct);
        if (workspace is null)
            return null;
        if (workspace.Scope.TenantId != tenantId || workspace.Scope.CompanyId != companyId || workspace.WorkspaceId != workspaceId)
            throw new InvalidOperationException("Onboarding storage returned a workspace outside the requested scope.");
        return Copy(workspace);
    }

    public async Task<OnboardingWorkspaceDto> UpdateCriteriaAsync(
        string tenantId, string companyId, string workspaceId,
        UpdateOnboardingCriteriaRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        ValidateCriteria(workspace.Scope, request.Criteria, workspace.OwnerId);
        var next = Advance(workspace, true) with
        {
            Criteria = Copy(request.Criteria),
            CriteriaHistory = [.. workspace.CriteriaHistory,
                new(workspace.DataRevision + 1, Copy(request.Criteria), actorId, _clock.GetUtcNow())]
        };
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingWorkspaceDto> CompareAsync(
        string tenantId, string companyId, string workspaceId,
        CaptureOnboardingComparisonRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        ValidateCaptureRequest(workspace, request);
        // Retain an isolated copy immediately. Later provider updates cannot reinterpret this capture.
        var capture = Copy(await sources.CaptureAsync(Copy(workspace), request, ct));
        capture = ValidateCapture(workspace, request, capture);
        var next = Advance(workspace, true);
        var comparison = BuildComparison(workspace, request, capture, actorId, Guid.NewGuid().ToString("N"),
            _clock.GetUtcNow(), next.DataRevision);
        next = next with { Comparisons = [.. workspace.Comparisons, comparison] };
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingWorkspaceDto> AssignDifferenceAsync(
        string tenantId, string companyId, string workspaceId, string differenceKey,
        AssignOnboardingDifferenceRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        Required(request.OwnerId, "Difference owner");
        Required(request.Notes, "Assignment rationale");
        ValidateEvidence(request.EvidenceIds, true);
        if (!workspace.Comparisons.SelectMany(run => run.Differences).Any(row => row.DifferenceKey == differenceKey))
            throw new OnboardingValidationException("The difference does not belong to this workspace.");
        var next = Advance(workspace, true) with
        {
            Assignments = [.. workspace.Assignments, new(differenceKey, request.OwnerId, request.Notes,
                request.EvidenceIds.ToArray(), actorId, _clock.GetUtcNow(), workspace.DataRevision + 1)]
        };
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingWorkspaceDto> ReviewAsync(
        string tenantId, string companyId, string workspaceId,
        ReviewOnboardingWorkspaceRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        Required(actorId, "Reviewer");
        if (SameActor(actorId, workspace.OwnerId) || !workspace.Criteria.RequiredReviewerIds.Any(reviewer => SameActor(reviewer, actorId)))
            throw new UnauthorizedAccessException("Review requires a designated reviewer independent of the onboarding owner.");
        if (request.DataRevision != workspace.DataRevision)
            throw new OnboardingConcurrencyException("The reviewed revision changed. Reload the workspace before reviewing.");
        if (request.Decision is not ("Approved" or "ChangesRequested"))
            throw new OnboardingValidationException("Review decision must be Approved or ChangesRequested.");
        Required(request.Notes, "Reviewer rationale");
        ValidateEvidence(request.EvidenceIds, true);
        var next = Advance(workspace, false) with
        {
            Reviews = [.. workspace.Reviews, new(Guid.NewGuid().ToString("N"), workspace.DataRevision,
                actorId, request.Decision, request.Notes, request.EvidenceIds.ToArray(), _clock.GetUtcNow())]
        };
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingReadinessPacketDto> FreezePacketAsync(
        string tenantId, string companyId, string workspaceId,
        FreezeOnboardingPacketRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        var content = Copy(new OnboardingPacketContentDto(
            workspace.WorkspaceId, workspace.Name, workspace.OwnerId, workspace.Scope, workspace.Version,
            workspace.DataRevision, workspace.Criteria, workspace.CriteriaHistory, workspace.Comparisons,
            CurrentDifferences(workspace), workspace.Assignments, workspace.Reviews, CalculateReadiness(workspace),
            ReadOnlyAuthorityPosture));
        var packet = new OnboardingReadinessPacketDto(Guid.NewGuid().ToString("N"), _clock.GetUtcNow(), actorId,
            content, HashPacketContent(content), "SHA-256");
        await SaveAsync(Advance(workspace, false) with { Packets = [.. workspace.Packets, packet] }, workspace.Version, ct);
        return Copy(packet);
    }

    public async Task<OnboardingReadinessPacketDto?> GetPacketAsync(
        string tenantId, string companyId, string workspaceId, string packetId, CancellationToken ct = default)
    {
        var workspace = await GetAsync(tenantId, companyId, workspaceId, ct);
        var packet = workspace?.Packets.SingleOrDefault(item => item.PacketId == packetId);
        if (packet is not null && packet.ContentHash != HashPacketContent(packet.Content))
            throw new InvalidOperationException("The retained onboarding packet failed its content-integrity check.");
        return packet is null ? null : Copy(packet);
    }

    /// <summary>Recomputes solely from retained inputs and preceding retained history, with no live source calls.</summary>
    public async Task<OnboardingComparisonDto?> ReplayComparisonAsync(
        string tenantId, string companyId, string workspaceId, string comparisonId, CancellationToken ct = default)
    {
        var workspace = await GetAsync(tenantId, companyId, workspaceId, ct);
        var run = workspace?.Comparisons.SingleOrDefault(item => item.ComparisonId == comparisonId);
        if (workspace is null || run is null)
            return null;
        if (run.AlgorithmVersion != ComparisonAlgorithmVersion)
            throw new InvalidOperationException("The retained comparison uses an unsupported algorithm version.");
        string? previousHash = null;
        var sequence = 1;
        var replayIndex = workspace.Comparisons.ToList().FindIndex(item => item.ComparisonId == comparisonId);
        foreach (var retained in workspace.Comparisons.Take(replayIndex + 1))
        {
            if (!VerifyComparisonHash(retained) || retained.Sequence != sequence || retained.PreviousContentHash != previousHash)
                throw new InvalidOperationException("The retained comparison history failed its content-integrity check.");
            previousHash = retained.ContentHash;
            sequence++;
        }
        var historical = workspace with
        {
            Criteria = run.Criteria,
            Comparisons = workspace.Comparisons.Where(item => item.Sequence < run.Sequence).ToArray(),
            Assignments = workspace.Assignments.Where(item => item.DataRevision < run.DataRevision).ToArray()
        };
        var request = new CaptureOnboardingComparisonRequestDto(0, run.AsOfDate, run.ProviderId, run.ImportId,
            run.MappingProfileId, run.MappingVersion, run.Notes);
        var inputs = ValidateCapture(historical, request, Copy(run.Inputs));
        var replay = BuildComparison(historical, request, inputs, run.ActorId, run.ComparisonId, run.CapturedAtUtc, run.DataRevision);
        if (replay.ContentHash != run.ContentHash)
            throw new InvalidOperationException("The retained comparison failed reproducibility validation.");
        return Copy(replay);
    }

    public static string HashSourcePayload(string payload) =>
        Sha256Digest.ComputeUtf8(payload);

    public static string HashPacketContent(OnboardingPacketContentDto content) => Hash(content);

    public static bool VerifyComparisonHash(OnboardingComparisonDto comparison) =>
        comparison.ContentHash == Hash(comparison with { ContentHash = "" });

    public static string DifferenceKey(OnboardingObservationDto observation) => Hash(new
    {
        observation.Kind,
        observation.AccountId,
        observation.Currency,
        observation.InstrumentId
    });

    private OnboardingWorkspaceDto Advance(OnboardingWorkspaceDto workspace, bool dataChanged) => workspace with
    {
        Version = workspace.Version + 1,
        DataRevision = workspace.DataRevision + (dataChanged ? 1 : 0),
        UpdatedAtUtc = _clock.GetUtcNow()
    };

    private async Task<OnboardingWorkspaceDto> SaveAsync(OnboardingWorkspaceDto workspace, int expectedVersion, CancellationToken ct)
    {
        var next = workspace with { CurrentDifferences = CurrentDifferences(workspace), Readiness = CalculateReadiness(workspace) };
        await store.SaveAsync(Copy(next), expectedVersion, ct);
        return Copy(next);
    }

    private async Task<OnboardingWorkspaceDto> RequireAsync(
        string tenantId, string companyId, string workspaceId, int expectedVersion, CancellationToken ct)
    {
        var workspace = await GetAsync(tenantId, companyId, workspaceId, ct)
            ?? throw new KeyNotFoundException("Onboarding workspace was not found in the selected enterprise scope.");
        if (workspace.Version != expectedVersion)
            throw new OnboardingConcurrencyException("Onboarding workspace changed. Reload it before retrying.");
        return workspace;
    }

    private static OnboardingComparisonDto BuildComparison(
        OnboardingWorkspaceDto workspace, CaptureOnboardingComparisonRequestDto request, OnboardingSourceCaptureDto capture,
        string actorId, string comparisonId, DateTimeOffset capturedAtUtc, int dataRevision)
    {
        // A correction compares with its prior same-date result. A new period compares with the latest
        // earlier period. Append sequence and its separate hash chain never rewrite chronological history.
        var predecessor = workspace.Comparisons.LastOrDefault(item => item.AsOfDate == request.AsOfDate)
            ?? workspace.Comparisons.Where(item => item.AsOfDate < request.AsOfDate)
                .OrderBy(item => item.AsOfDate).ThenBy(item => item.Sequence).LastOrDefault();
        var previous = predecessor?.Differences.ToDictionary(row => row.DifferenceKey, StringComparer.Ordinal)
            ?? new Dictionary<string, OnboardingDifferenceDto>(StringComparer.Ordinal);
        var differences = new List<OnboardingDifferenceDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in capture.Observations.OrderBy(DifferenceKey, StringComparer.Ordinal))
        {
            var key = DifferenceKey(row);
            seen.Add(key);
            previous.TryGetValue(key, out var prior);
            var tolerance = Tolerance(workspace.Criteria, row.Kind);
            var difference = row.MeridianAmount.HasValue && row.ExternalAmount.HasValue
                ? row.MeridianAmount.Value - row.ExternalAmount.Value : (decimal?)null;
            var missing = difference is null || !string.IsNullOrWhiteSpace(row.MissingReason);
            var unequal = missing || Math.Abs(difference!.Value) > tolerance;
            if (!unequal && prior is null)
                continue;
            var status = !unequal ? "Resolved" : prior is null || prior.Status == "Resolved" ? "New" : "Persistent";
            var assignment = workspace.Assignments.LastOrDefault(item => item.DifferenceKey == key);
            differences.Add(new(key, row.Kind, row.AccountId, row.Currency, row.InstrumentId,
                row.MeridianAmount, row.ExternalAmount, difference, tolerance, status,
                assignment?.OwnerId ?? prior?.OwnerId ?? workspace.OwnerId,
                prior?.FirstSeenComparisonId ?? comparisonId, comparisonId,
                row.EvidenceIds.Concat(prior?.EvidenceIds ?? []).Concat(assignment?.EvidenceIds ?? [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                missing ? row.MissingReason ?? "A comparison amount is missing." : null));
        }
        // Disappearing positions/balances cannot be marked resolved without explicit observed amounts.
        foreach (var prior in previous.Values.Where(row => !seen.Contains(row.DifferenceKey)))
        {
            var assignment = workspace.Assignments.LastOrDefault(item => item.DifferenceKey == prior.DifferenceKey);
            differences.Add(prior with
            {
                Status = "Persistent",
                MeridianAmount = null,
                ExternalAmount = null,
                Difference = null,
                OwnerId = assignment?.OwnerId ?? prior.OwnerId,
                LastSeenComparisonId = comparisonId,
                MissingReason = "The previously compared row is missing from this source capture.",
                EvidenceIds = prior.EvidenceIds.Concat(assignment?.EvidenceIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            });
        }
        var comparison = new OnboardingComparisonDto(comparisonId, workspace.Comparisons.Count + 1,
            request.AsOfDate, dataRevision, actorId, capturedAtUtc, request.Notes,
            request.ProviderId, request.ImportId, request.MappingProfileId, request.MappingVersion,
            Copy(workspace.Criteria), capture, differences.OrderBy(row => row.DifferenceKey, StringComparer.Ordinal).ToArray(),
            Coverage(workspace, capture), "", workspace.Comparisons.LastOrDefault()?.ContentHash, ComparisonAlgorithmVersion,
            predecessor?.ComparisonId);
        return comparison with { ContentHash = Hash(comparison) };
    }

    private static IReadOnlyList<OnboardingDifferenceDto> CurrentDifferences(OnboardingWorkspaceDto workspace)
    {
        var latest = workspace.Comparisons.OrderBy(item => item.AsOfDate).ThenBy(item => item.Sequence).LastOrDefault();
        return latest?.Differences.Select(row => WithAssignment(row, workspace.Assignments)).ToArray() ?? [];
    }

    private static OnboardingDifferenceDto WithAssignment(
        OnboardingDifferenceDto row, IReadOnlyList<OnboardingDifferenceAssignmentDto> assignments)
    {
        var latest = assignments.LastOrDefault(item => item.DifferenceKey == row.DifferenceKey);
        return latest is null ? row : row with
        {
            OwnerId = latest.OwnerId,
            EvidenceIds = row.EvidenceIds.Concat(latest.EvidenceIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
    }

    private static OnboardingReadinessDto CalculateReadiness(OnboardingWorkspaceDto workspace)
    {
        var latestByDate = workspace.Comparisons.GroupBy(run => run.AsOfDate)
            .ToDictionary(group => group.Key, group => group.MaxBy(run => run.Sequence)!);
        var missingDates = workspace.Criteria.RequiredDates.Where(date => !latestByDate.ContainsKey(date)).Order().ToArray();
        var requiredRuns = workspace.Criteria.RequiredDates.Where(latestByDate.ContainsKey).Select(date => latestByDate[date]).ToArray();
        var missing = requiredRuns.SelectMany(run => run.Inputs.MissingSources.Select(source => source with
        {
            Message = $"{run.AsOfDate:yyyy-MM-dd}: {source.Message}"
        })).ToArray();
        var unresolved = requiredRuns.SelectMany(run => run.Differences.Where(row => row.Status != "Resolved")
            .Select(row => new OnboardingPeriodDifferenceDto(run.ComparisonId, run.AsOfDate, WithAssignment(row, workspace.Assignments)))).ToArray();
        var blockers = new List<string>();
        if (missingDates.Length > 0)
            blockers.Add($"Capture {missingDates.Length} required date(s).");
        if (missing.Length > 0)
            blockers.Add($"Resolve {missing.Length} missing source requirement(s).");
        if (unresolved.Length > 0)
            blockers.Add($"Resolve {unresolved.Length} difference(s) across the required dates.");
        foreach (var run in requiredRuns)
        {
            if (Hash(run.Criteria) != Hash(workspace.Criteria))
                blockers.Add($"Recapture {run.AsOfDate:yyyy-MM-dd} against the current owner criteria.");
            if (run.CoveragePercent < workspace.Criteria.MinimumCoveragePercent)
                blockers.Add($"Coverage for {run.AsOfDate:yyyy-MM-dd} is below the required {workspace.Criteria.MinimumCoveragePercent}%.");
            if (workspace.Criteria.RequireCloseReadiness && run.Inputs.CloseReadiness is not { IsComplete: true, IsReadyToClose: true })
                blockers.Add($"Close-readiness evidence for {run.AsOfDate:yyyy-MM-dd} is incomplete or blocked.");
        }
        var reviews = workspace.Reviews.Where(review => review.DataRevision == workspace.DataRevision
                && !SameActor(review.ReviewerId, workspace.OwnerId)
                && workspace.Criteria.RequiredReviewerIds.Any(reviewer => SameActor(reviewer, review.ReviewerId)))
            .GroupBy(review => review.ReviewerId.Trim(), StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToArray();
        var approved = reviews.Count(review => review.Decision == "Approved");
        var dataBlocked = blockers.Count > 0;
        if (reviews.Any(review => review.Decision == "ChangesRequested"))
            blockers.Add("A reviewer requested changes on the current revision.");
        if (approved < workspace.Criteria.MinimumReviewers)
            blockers.Add($"Obtain {workspace.Criteria.MinimumReviewers - approved} additional independent review(s) of revision {workspace.DataRevision}.");
        var ready = blockers.Count == 0;
        var status = ready ? "Ready" : workspace.Comparisons.Count == 0 ? "AwaitingSources" : dataBlocked ? "Exceptions" : "AwaitingReview";
        return new(status, ready, unresolved.Length, missingDates, missing, blockers.ToArray(), approved,
            workspace.Criteria.MinimumReviewers, unresolved);
    }

    private static decimal Coverage(OnboardingWorkspaceDto workspace, OnboardingSourceCaptureDto capture)
    {
        var complete = 0;
        foreach (var account in workspace.Scope.AccountIds)
            foreach (var kind in workspace.Criteria.RequiredKinds)
            {
                var rows = capture.Observations.Where(row => row.AccountId == account && row.Kind == kind).ToArray();
                if (rows.Length > 0 && rows.All(row => row.MeridianAmount.HasValue && row.ExternalAmount.HasValue
                    && string.IsNullOrWhiteSpace(row.MissingReason)))
                    complete++;
            }
        return 100m * complete / (workspace.Scope.AccountIds.Count * workspace.Criteria.RequiredKinds.Count);
    }

    private static OnboardingSourceCaptureDto ValidateCapture(
        OnboardingWorkspaceDto workspace, CaptureOnboardingComparisonRequestDto request, OnboardingSourceCaptureDto capture)
    {
        if (capture.Snapshots is null || capture.Observations is null || capture.MissingSources is null)
            throw new OnboardingValidationException("Source capture collections must be present.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in capture.Snapshots)
        {
            Required(snapshot.SnapshotId, "Source snapshot identity");
            Required(snapshot.SourceKind, "Source kind");
            Required(snapshot.SourceId, "Source identity");
            Required(snapshot.Version, "Source version");
            Required(snapshot.PayloadJson, "Source payload");
            if (!ids.Add(snapshot.SnapshotId))
                throw new OnboardingValidationException("Source snapshot identities must be unique.");
            if (snapshot.AsOfDate < workspace.Scope.StartDate || snapshot.AsOfDate > request.AsOfDate
                || snapshot.MappingVersion != request.MappingVersion)
                throw new OnboardingValidationException("Source snapshot date must fall within the bounded capture period and its mapping version must match the request.");
            if (!string.Equals(snapshot.ContentHash, HashSourcePayload(snapshot.PayloadJson), StringComparison.OrdinalIgnoreCase))
                throw new OnboardingValidationException("Source snapshot content hash does not match its retained payload.");
            try
            { using var document = JsonDocument.Parse(snapshot.PayloadJson); }
            catch (JsonException) { throw new OnboardingValidationException("Source snapshot payload must be valid JSON."); }
            ValidateEvidence(snapshot.EvidenceIds, false);
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var missing = capture.MissingSources.ToList();
        foreach (var row in capture.Observations)
        {
            if (!Kinds.Contains(row.Kind) || !workspace.Scope.AccountIds.Contains(row.AccountId, StringComparer.Ordinal))
                throw new OnboardingValidationException("An observation is outside the selected kind or account population.");
            Required(row.Currency, "Observation currency");
            if (row.Kind == "Position")
                Required(row.InstrumentId, "Position instrument identity");
            if (!keys.Add(DifferenceKey(row)))
                throw new OnboardingValidationException("Duplicate comparison observations cannot be collapsed or netted.");
            if (row.SnapshotIds is null || row.SnapshotIds.Count == 0 || row.SnapshotIds.Any(id => !ids.Contains(id)))
                throw new OnboardingValidationException("Every comparison observation must reference retained source snapshots.");
            ValidateEvidence(row.EvidenceIds, false);
            if (!row.MeridianAmount.HasValue || !row.ExternalAmount.HasValue || !string.IsNullOrWhiteSpace(row.MissingReason))
                AddMissing(missing, new("MissingAmount", row.Kind, row.AccountId,
                    row.MissingReason ?? $"An amount is missing for {row.AccountId}/{row.Kind}/{row.Currency}/{row.InstrumentId}."));
        }
        foreach (var account in workspace.Scope.AccountIds)
            foreach (var kind in workspace.Criteria.RequiredKinds)
            {
                if (!capture.Observations.Any(row => row.AccountId == account && row.Kind == kind))
                    AddMissing(missing, new("MissingCoverage", kind, account, $"No {kind} source covers selected account {account}."));
            }
        if (capture.CloseReadiness is not null
            && (capture.CloseReadiness.Scope.LedgerBookId != workspace.Scope.LedgerBookId
                || capture.CloseReadiness.Scope.EntityId != workspace.Scope.EntityId))
            throw new OnboardingValidationException("Close-readiness evidence does not match the selected entity and book.");
        return capture with { MissingSources = missing.ToArray() };
    }

    private static void AddMissing(List<OnboardingMissingSourceDto> missing, OnboardingMissingSourceDto item)
    {
        if (!missing.Contains(item))
            missing.Add(item);
    }

    private static void ValidateCaptureRequest(OnboardingWorkspaceDto workspace, CaptureOnboardingComparisonRequestDto request)
    {
        if (request.AsOfDate < workspace.Scope.StartDate || request.AsOfDate > workspace.Scope.EndDate)
            throw new OnboardingValidationException("The comparison date is outside the bounded onboarding range.");
        Required(request.ProviderId, "Provider identity");
        Required(request.ImportId, "Exact import identity");
        Required(request.MappingProfileId, "Mapping profile identity");
        Required(request.MappingVersion, "Exact mapping version");
    }

    private static void ValidateScope(OnboardingScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateEnterpriseScope(scope.TenantId, scope.CompanyId);
        Required(scope.EntityId, "Entity identity");
        Required(scope.FundProfileId, "Fund profile identity");
        if (scope.LedgerBookId == Guid.Empty)
            throw new OnboardingValidationException("Select a ledger book.");
        if (scope.StartDate > scope.EndDate)
            throw new OnboardingValidationException("Start date must not be after end date.");
        if (scope.AccountIds is null || scope.AccountIds.Count == 0 || scope.AccountIds.Count > 1000
            || scope.AccountIds.Any(id => !Guid.TryParse(id, out var parsed) || parsed == Guid.Empty)
            || scope.AccountIds.Select(id => Guid.Parse(id)).Distinct().Count() != scope.AccountIds.Count)
            throw new OnboardingValidationException("Select a unique, explicit population of financial account identities (maximum 1,000).");
    }

    private static void ValidateCriteria(OnboardingScopeDto scope, OnboardingCriteriaDto criteria, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.RequiredDates is null || criteria.RequiredDates.Count == 0 || criteria.RequiredDates.Count > 366
            || criteria.RequiredDates.Distinct().Count() != criteria.RequiredDates.Count
            || criteria.RequiredDates.Any(date => date < scope.StartDate || date > scope.EndDate))
            throw new OnboardingValidationException("Select unique required dates inside the onboarding range (maximum 366).");
        if (criteria.RequiredKinds is null || criteria.RequiredKinds.Count == 0
            || criteria.RequiredKinds.Any(kind => !Kinds.Contains(kind))
            || criteria.RequiredKinds.Distinct(StringComparer.Ordinal).Count() != criteria.RequiredKinds.Count)
            throw new OnboardingValidationException("Select unique comparison kinds: Balance, Position, or Nav.");
        if (criteria.BalanceTolerance < 0 || criteria.PositionTolerance < 0 || criteria.NavTolerance < 0)
            throw new OnboardingValidationException("Comparison tolerances cannot be negative.");
        if (criteria.MinimumCoveragePercent <= 0 || criteria.MinimumCoveragePercent > 100)
            throw new OnboardingValidationException("Minimum coverage must be greater than zero and at most 100 percent.");
        if (criteria.RequiredReviewerIds is null || criteria.RequiredReviewerIds.Count == 0
            || criteria.RequiredReviewerIds.Any(id => string.IsNullOrWhiteSpace(id) || SameActor(id, ownerId))
            || criteria.RequiredReviewerIds.Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != criteria.RequiredReviewerIds.Count
            || criteria.MinimumReviewers < 1 || criteria.MinimumReviewers > criteria.RequiredReviewerIds.Count)
            throw new OnboardingValidationException("Select enough distinct independent reviewers for the minimum review count.");
        Required(criteria.ReviewInstructions, "Review criteria");
    }

    private static decimal Tolerance(OnboardingCriteriaDto criteria, string kind) => kind switch
    {
        "Balance" => criteria.BalanceTolerance,
        "Position" => criteria.PositionTolerance,
        "Nav" => criteria.NavTolerance,
        _ => throw new OnboardingValidationException("Unsupported comparison kind.")
    };

    private static void ValidateEnterpriseScope(string tenantId, string companyId)
    {
        Required(tenantId, "Tenant identity");
        Required(companyId, "Company identity");
    }

    private static void RequireOwner(OnboardingWorkspaceDto workspace, string actorId)
    {
        Required(actorId, "Actor");
        if (!SameActor(workspace.OwnerId, actorId))
            throw new UnauthorizedAccessException("Only the onboarding owner can change this workspace.");
    }

    private static void ValidateEvidence(IReadOnlyList<string> evidenceIds, bool required)
    {
        if (evidenceIds is null || (required && evidenceIds.Count == 0)
            || evidenceIds.Any(string.IsNullOrWhiteSpace)
            || evidenceIds.Distinct(StringComparer.Ordinal).Count() != evidenceIds.Count)
            throw new OnboardingValidationException("Provide distinct retained supporting evidence identities.");
    }

    private static void Required(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new OnboardingValidationException($"{label} is required.");
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private static string Hash<T>(T value) => HashSourcePayload(JsonSerializer.Serialize(value, Json));
    private static bool SameActor(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
