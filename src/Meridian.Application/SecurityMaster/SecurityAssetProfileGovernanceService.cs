using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityAssetProfileGovernanceService : ISecurityAssetProfileCatalog
{
    IReadOnlyList<SecurityAssetProfileDefinitionDto> GetAllProfiles();
    SecurityAssetProfileLineageDto GetLineage(string profileId);
    IReadOnlyList<SecurityAssetProfilePromotionCandidateDto> GetPromotionCandidates();

    Task<SecurityAssetProfileGovernanceResultDto> DraftProfileAsync(
        SecurityAssetProfileDraftRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<SecurityAssetProfileGovernanceResultDto> ApproveProfileAsync(
        SecurityAssetProfileApprovalRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<SecurityAssetProfileGovernanceResultDto> RollbackProfileAsync(
        SecurityAssetProfileRollbackRequestDto request,
        string actor,
        CancellationToken ct = default);
}

public sealed class SecurityAssetProfileGovernanceService : ISecurityAssetProfileGovernanceService
{
    private static readonly HashSet<string> ReservedFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "assetClass",
        "commonTerms",
        "customProfileId",
        "effectiveFrom",
        "effectiveTo",
        "identifiers",
        "profileApproval",
        "profileFields",
        "profileVersion",
        "provenance",
        "schemaVersion",
        "securityId",
        "status",
        "version"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IReadOnlyList<SecurityAssetProfileDefinitionDto> _seedProfiles =
        StaticSecurityAssetProfileCatalog.CreateDefault().GetProfiles();
    private readonly string? _persistencePath;
    private SecurityAssetProfileGovernanceSnapshot _inMemorySnapshot = new([], []);

    public SecurityAssetProfileGovernanceService()
    {
    }

    public SecurityAssetProfileGovernanceService(StorageOptions storageOptions)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "governance", "security-asset-profiles.json");
    }

    // The selectable catalog must stay aligned with write-time governance, which accepts a
    // version only when its status is Approved or Superseded AND its effective window covers the
    // write date. Both halves of that predicate apply here: a Superseded predecessor stays
    // selectable while a future-dated replacement waits to activate (it is the only version
    // write-time validation accepts during that gap), and a freshly Approved version whose
    // EffectiveFrom is still in the future is NOT selectable yet — exposing it would advertise a
    // create/amend that validation rejects with SM_CUSTOM_PROFILE_VERSION_NOT_EFFECTIVE.
    public IReadOnlyList<SecurityAssetProfileDefinitionDto> GetProfiles()
        => GetProfiles(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime.Date));

    // Selectability is evaluated against the WRITE's effective date: write-time governance pins
    // the version whose window covers that date, so a backdated amendment must be able to
    // discover the historical (now superseded) version governing its effective date — filtering
    // only against today would hide exactly the version such a write needs.
    public IReadOnlyList<SecurityAssetProfileDefinitionDto> GetProfiles(DateOnly effectiveAt)
        => GetAllProfiles()
            .Where(profile => profile.Status is SecurityAssetProfileStatusDto.Approved or SecurityAssetProfileStatusDto.Superseded
                && profile.EffectiveFrom <= effectiveAt
                && (profile.EffectiveTo is not DateOnly effectiveTo || effectiveAt <= effectiveTo))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static profile => profile.Version)
            .ToArray();

    public IReadOnlyList<SecurityAssetProfileDefinitionDto> GetAllProfiles()
        => MergeProfiles(ReadSnapshot().Profiles);

    public bool TryGetProfile(string profileId, int version, out SecurityAssetProfileDefinitionDto profile)
    {
        profile = GetAllProfiles().FirstOrDefault(candidate =>
            candidate.Version == version
            && string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))!;
        return profile is not null;
    }

    public bool TryGetLatestApprovedProfile(string profileId, out SecurityAssetProfileDefinitionDto profile)
    {
        profile = GetAllProfiles()
            .Where(candidate => candidate.Status == SecurityAssetProfileStatusDto.Approved
                && string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static candidate => candidate.Version)
            .FirstOrDefault()!;
        return profile is not null;
    }

    public SecurityAssetProfileLineageDto GetLineage(string profileId)
    {
        var normalizedProfileId = Required(profileId, nameof(profileId));
        var snapshot = ReadSnapshot();
        var versions = GetAllProfiles()
            .Where(profile => string.Equals(profile.ProfileId, normalizedProfileId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static profile => profile.Version)
            .ToArray();
        var auditEvents = snapshot.AuditEvents
            .Where(audit => string.Equals(audit.ProfileId, normalizedProfileId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static audit => audit.OccurredAtUtc)
            .ToArray();

        return new SecurityAssetProfileLineageDto(normalizedProfileId, versions, auditEvents);
    }

    public IReadOnlyList<SecurityAssetProfilePromotionCandidateDto> GetPromotionCandidates()
        => GetAllProfiles()
            .Where(static profile => profile.Status == SecurityAssetProfileStatusDto.Approved)
            .Select(BuildPromotionCandidate)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task<SecurityAssetProfileGovernanceResultDto> DraftProfileAsync(
        SecurityAssetProfileDraftRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolvedActor = ResolveActor(actor, request.RequestedBy);
        var now = DateTimeOffset.UtcNow;
        var correlationId = ResolveCorrelationId(request.CorrelationId, "asset-profile-draft");
        var rationale = Required(request.Rationale, nameof(request.Rationale));
        var profileId = Required(request.ProfileId, nameof(request.ProfileId));
        var profileName = Required(request.Name, nameof(request.Name));
        var category = Required(request.Category, nameof(request.Category));

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = ReadSnapshot();
            var profiles = MergeProfiles(snapshot.Profiles).ToList();
            var existingDraft = profiles
                .Where(profile => string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                                  && profile.Status == SecurityAssetProfileStatusDto.Draft)
                .OrderByDescending(static profile => profile.Version)
                .FirstOrDefault();
            var version = existingDraft?.Version
                          ?? profiles
                              .Where(profile => string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                              .Select(static profile => profile.Version)
                              .DefaultIfEmpty(0)
                              .Max() + 1;

            var draft = new SecurityAssetProfileDefinitionDto(
                ProfileId: profileId,
                Version: version,
                Name: profileName,
                Category: category,
                SubType: string.IsNullOrWhiteSpace(request.SubType) ? null : request.SubType.Trim(),
                Status: SecurityAssetProfileStatusDto.Draft,
                Fields: NormalizeFields(request.Fields),
                IdentifierPreferences: NormalizeIdentifierPreferences(request.IdentifierPreferences),
                LifecycleStates: NormalizeLifecycleStates(request.LifecycleStates),
                AccountingImpactHints: request.AccountingImpactHints?.Distinct().ToArray() ?? [],
                DateOrderRules: request.DateOrderRules?.ToArray() ?? [],
                EffectiveFrom: DateOnly.FromDateTime(now.UtcDateTime),
                EffectiveTo: null,
                ApprovedBy: resolvedActor,
                ApprovedAtUtc: now,
                ChangeReason: rationale);
            ValidateProfileDefinition(draft);

            UpsertProfile(profiles, draft);
            var auditEvent = BuildAuditEvent(
                "security-asset-profile-drafted",
                resolvedActor,
                rationale,
                correlationId,
                draft,
                previousVersion: existingDraft?.Version,
                approvalReference: null,
                now);
            var saved = new SecurityAssetProfileGovernanceSnapshot(profiles, [.. snapshot.AuditEvents, auditEvent]);
            await SaveSnapshotAsync(saved, ct).ConfigureAwait(false);

            return new SecurityAssetProfileGovernanceResultDto(draft, GetLineage(profileId), auditEvent);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<SecurityAssetProfileGovernanceResultDto> ApproveProfileAsync(
        SecurityAssetProfileApprovalRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolvedActor = ResolveActor(actor, request.RequestedBy);
        var profileId = Required(request.ProfileId, nameof(request.ProfileId));
        var rationale = Required(request.Rationale, nameof(request.Rationale));
        var approvalReference = Required(request.ApprovalReference, nameof(request.ApprovalReference));
        var correlationId = ResolveCorrelationId(request.CorrelationId, "asset-profile-approve");
        var now = DateTimeOffset.UtcNow;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = ReadSnapshot();
            var profiles = MergeProfiles(snapshot.Profiles).ToList();
            var draft = profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                && profile.Version == request.Version
                && profile.Status == SecurityAssetProfileStatusDto.Draft);
            if (draft is null)
            {
                throw new ArgumentException("A matching draft profile version is required before approval.", nameof(request));
            }

            var approved = draft with
            {
                Status = SecurityAssetProfileStatusDto.Approved,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = null,
                ApprovedBy = resolvedActor,
                ApprovedAtUtc = now,
                ChangeReason = rationale,
                ApprovalReference = approvalReference
            };
            ValidateProfileDefinition(approved);
            EnsureTransitionDateSucceedsIncumbents(profiles, approved.ProfileId, approved.Version, request.EffectiveFrom);
            SupersedeCurrentApproved(profiles, approved.ProfileId, approved.Version, request.EffectiveFrom);
            UpsertProfile(profiles, approved);

            var auditEvent = BuildAuditEvent(
                "security-asset-profile-approved",
                resolvedActor,
                rationale,
                correlationId,
                approved,
                previousVersion: null,
                approvalReference,
                now);
            var saved = new SecurityAssetProfileGovernanceSnapshot(profiles, [.. snapshot.AuditEvents, auditEvent]);
            await SaveSnapshotAsync(saved, ct).ConfigureAwait(false);

            return new SecurityAssetProfileGovernanceResultDto(approved, GetLineage(profileId), auditEvent);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<SecurityAssetProfileGovernanceResultDto> RollbackProfileAsync(
        SecurityAssetProfileRollbackRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolvedActor = ResolveActor(actor, request.RequestedBy);
        var profileId = Required(request.ProfileId, nameof(request.ProfileId));
        var rationale = Required(request.Rationale, nameof(request.Rationale));
        var approvalReference = Required(request.ApprovalReference, nameof(request.ApprovalReference));
        var correlationId = ResolveCorrelationId(request.CorrelationId, "asset-profile-rollback");
        var now = DateTimeOffset.UtcNow;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = ReadSnapshot();
            var profiles = MergeProfiles(snapshot.Profiles).ToList();
            var target = profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                && profile.Version == request.TargetVersion
                && profile.Status is SecurityAssetProfileStatusDto.Approved or SecurityAssetProfileStatusDto.Superseded);
            if (target is null)
            {
                throw new ArgumentException("Rollback target must be an approved or superseded profile version.", nameof(request));
            }

            var nextVersion = profiles
                .Where(profile => string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                .Select(static profile => profile.Version)
                .DefaultIfEmpty(0)
                .Max() + 1;
            var rollback = target with
            {
                Version = nextVersion,
                Status = SecurityAssetProfileStatusDto.Approved,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = null,
                ApprovedBy = resolvedActor,
                ApprovedAtUtc = now,
                ChangeReason = $"Rollback to version {request.TargetVersion}: {rationale}",
                ApprovalReference = approvalReference
            };
            ValidateProfileDefinition(rollback);
            EnsureTransitionDateSucceedsIncumbents(profiles, rollback.ProfileId, rollback.Version, request.EffectiveFrom);
            SupersedeCurrentApproved(profiles, rollback.ProfileId, rollback.Version, request.EffectiveFrom);
            UpsertProfile(profiles, rollback);

            var auditEvent = BuildAuditEvent(
                "security-asset-profile-rollback-approved",
                resolvedActor,
                rationale,
                correlationId,
                rollback,
                previousVersion: request.TargetVersion,
                approvalReference,
                now);
            var saved = new SecurityAssetProfileGovernanceSnapshot(profiles, [.. snapshot.AuditEvents, auditEvent]);
            await SaveSnapshotAsync(saved, ct).ConfigureAwait(false);

            return new SecurityAssetProfileGovernanceResultDto(rollback, GetLineage(profileId), auditEvent);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private SecurityAssetProfileGovernanceSnapshot ReadSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            lock (_readGate)
            {
                return _inMemorySnapshot;
            }
        }

        lock (_readGate)
        {
            try
            {
                return JsonSerializer.Deserialize<SecurityAssetProfileGovernanceSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions) ?? new SecurityAssetProfileGovernanceSnapshot([], []);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Security asset profile governance persistence at '{_persistencePath}' is corrupt and cannot be loaded.",
                    ex);
            }
        }
    }

    private async Task SaveSnapshotAsync(SecurityAssetProfileGovernanceSnapshot snapshot, CancellationToken ct)
    {
        var normalized = new SecurityAssetProfileGovernanceSnapshot(
            Profiles: snapshot.Profiles
                .OrderBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static profile => profile.Version)
                .ToArray(),
            AuditEvents: snapshot.AuditEvents
                .OrderBy(static audit => audit.OccurredAtUtc)
                .ToArray());

        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            lock (_readGate)
            {
                _inMemorySnapshot = normalized;
            }
            return;
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        await AtomicFileWriter.WriteAsync(_persistencePath, json, ct).ConfigureAwait(false);
    }

    private IReadOnlyList<SecurityAssetProfileDefinitionDto> MergeProfiles(
        IReadOnlyList<SecurityAssetProfileDefinitionDto> persistedProfiles)
    {
        var merged = _seedProfiles.ToDictionary(
            static profile => $"{profile.ProfileId}:{profile.Version}",
            StringComparer.OrdinalIgnoreCase);
        foreach (var profile in persistedProfiles)
        {
            merged[$"{profile.ProfileId}:{profile.Version}"] = profile;
        }

        return merged.Values
            .OrderBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static profile => profile.Version)
            .ToArray();
    }

    private static void UpsertProfile(
        List<SecurityAssetProfileDefinitionDto> profiles,
        SecurityAssetProfileDefinitionDto profile)
    {
        var index = profiles.FindIndex(candidate =>
            candidate.Version == profile.Version
            && string.Equals(candidate.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }
    }

    /// <summary>
    /// A replacement (or rollback) whose effective date is on or before an incumbent approved
    /// version's <c>EffectiveFrom</c> would assign that incumbent an <c>EffectiveTo</c> preceding
    /// its own start — a corrupt window that strands every write date the incumbent formerly
    /// governed. Backdating a transition requires a governed rebuild of the affected windows, not
    /// a silent supersede, so the transition date must strictly succeed each incumbent's start.
    /// </summary>
    private static void EnsureTransitionDateSucceedsIncumbents(
        List<SecurityAssetProfileDefinitionDto> profiles,
        string profileId,
        int approvedVersion,
        DateOnly effectiveFrom)
    {
        var incumbent = profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
            && profile.Version != approvedVersion
            && profile.Status == SecurityAssetProfileStatusDto.Approved
            && profile.EffectiveFrom >= effectiveFrom);
        if (incumbent is not null)
        {
            throw new ArgumentException(
                $"Effective date {effectiveFrom:yyyy-MM-dd} must be after the incumbent approved version " +
                $"{incumbent.Version}'s effective start {incumbent.EffectiveFrom:yyyy-MM-dd}: superseding it with an " +
                "earlier or equal date would leave its governed window ending before it began.");
        }
    }

    private static void SupersedeCurrentApproved(
        List<SecurityAssetProfileDefinitionDto> profiles,
        string profileId,
        int approvedVersion,
        DateOnly effectiveFrom)
    {
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (!string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                || profile.Version == approvedVersion
                || profile.Status != SecurityAssetProfileStatusDto.Approved)
            {
                continue;
            }

            profiles[index] = profile with
            {
                Status = SecurityAssetProfileStatusDto.Superseded,
                EffectiveTo = effectiveFrom.AddDays(-1)
            };
        }
    }

    private static SecurityAssetProfileGovernanceAuditEventDto BuildAuditEvent(
        string eventType,
        string actor,
        string rationale,
        string correlationId,
        SecurityAssetProfileDefinitionDto profile,
        int? previousVersion,
        string? approvalReference,
        DateTimeOffset now)
        => new(
            AuditId: $"asset-profile-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..58],
            EventType: eventType,
            OccurredAtUtc: now,
            Actor: actor,
            Rationale: rationale,
            CorrelationId: correlationId,
            ProfileId: profile.ProfileId,
            Version: profile.Version,
            Status: profile.Status,
            PreviousVersion: previousVersion,
            ApprovalReference: approvalReference);

    private static SecurityAssetProfilePromotionCandidateDto BuildPromotionCandidate(
        SecurityAssetProfileDefinitionDto profile)
    {
        var signals = new List<SecurityAssetProfilePromotionSignalDto>();
        var dedicatedBehaviorNeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = 0;

        AddAccountingSignals(profile, signals, dedicatedBehaviorNeeds, ref score);

        var projectedFields = profile.Fields.Count(static field => field.IsProjected);
        if (projectedFields >= 3)
        {
            score += Math.Min(projectedFields, 8) * 4;
            dedicatedBehaviorNeeds.Add("typed projection");
            signals.Add(new SecurityAssetProfilePromotionSignalDto(
                "profile-fields.projected",
                "Projected field surface",
                $"{projectedFields} profile fields are projected and should become typed read-model columns if usage grows.",
                projectedFields >= 6
                    ? SecurityAssetProfilePromotionSignalSeverityDto.Strong
                    : SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
        }

        var searchableFields = profile.Fields.Count(static field => field.IsSearchable);
        if (searchableFields >= 2)
        {
            score += Math.Min(searchableFields, 6) * 3;
            dedicatedBehaviorNeeds.Add("indexed search");
            signals.Add(new SecurityAssetProfilePromotionSignalDto(
                "profile-fields.searchable",
                "Searchable field coverage",
                $"{searchableFields} fields are searchable and may need indexed package-specific lookup support.",
                SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
        }

        if (profile.Fields.Any(static field => field.FieldType == SecurityAssetProfileFieldTypeDto.SecurityLink))
        {
            score += 14;
            dedicatedBehaviorNeeds.Add("security-link reconciliation");
            signals.Add(new SecurityAssetProfilePromotionSignalDto(
                "profile-fields.security-link",
                "Security-link fields",
                "Security-link fields indicate cross-instrument reconciliation or exposure behavior that should not remain generic JSON.",
                SecurityAssetProfilePromotionSignalSeverityDto.Strong));
        }

        if (profile.DateOrderRules.Count > 0)
        {
            score += 8;
            dedicatedBehaviorNeeds.Add("lifecycle validation");
            signals.Add(new SecurityAssetProfilePromotionSignalDto(
                "profile-rules.date-order",
                "Date-order rules",
                $"{profile.DateOrderRules.Count} date-order rule(s) should be carried into first-class package validation.",
                SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
        }

        if (profile.LifecycleStates.Count >= 4)
        {
            score += 8;
            dedicatedBehaviorNeeds.Add("lifecycle state model");
            signals.Add(new SecurityAssetProfilePromotionSignalDto(
                "profile-lifecycle.states",
                "Lifecycle state coverage",
                $"{profile.LifecycleStates.Count} lifecycle states indicate workflow-specific state transitions.",
                SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
        }

        var requiredCloseIdentifiers = profile.IdentifierPreferences.Count(static preference => preference.IsRequiredForClose);
        if (requiredCloseIdentifiers > 0)
        {
            score += requiredCloseIdentifiers * 6;
            dedicatedBehaviorNeeds.Add("close/readiness identity coverage");
            signals.Add(new SecurityAssetProfilePromotionSignalDto(
                "profile-identifiers.close-required",
                "Close identifier requirements",
                $"{requiredCloseIdentifiers} identifier preference(s) are required for close readiness.",
                SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
        }

        if (signals.Count == 0)
        {
            signals.Add(new SecurityAssetProfilePromotionSignalDto(
                "profile-watchlist.low-complexity",
                "Low complexity profile",
                "No dedicated accounting, reconciliation, lifecycle, or projection behavior is visible yet.",
                SecurityAssetProfilePromotionSignalSeverityDto.Info));
        }

        var readiness = score >= 72
            ? SecurityAssetProfilePromotionReadinessDto.ReadyForFirstClassPackage
            : score >= 40
                ? SecurityAssetProfilePromotionReadinessDto.Candidate
                : SecurityAssetProfilePromotionReadinessDto.Watchlist;
        var package = ResolveRecommendedPackage(profile);

        return new SecurityAssetProfilePromotionCandidateDto(
            ProfileId: profile.ProfileId,
            Version: profile.Version,
            Name: profile.Name,
            Category: profile.Category,
            SubType: profile.SubType,
            Readiness: readiness,
            Score: Math.Min(score, 100),
            IsCandidate: readiness != SecurityAssetProfilePromotionReadinessDto.Watchlist,
            RecommendedPackageId: package.PackageId,
            RecommendedPackageName: package.PackageName,
            DedicatedBehaviorNeeds: dedicatedBehaviorNeeds.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Signals: signals
                .OrderByDescending(static signal => signal.Severity)
                .ThenBy(static signal => signal.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PromotionRationale: BuildPromotionRationale(readiness, profile, package.PackageName));
    }

    private static void AddAccountingSignals(
        SecurityAssetProfileDefinitionDto profile,
        List<SecurityAssetProfilePromotionSignalDto> signals,
        HashSet<string> dedicatedBehaviorNeeds,
        ref int score)
    {
        foreach (var hint in profile.AccountingImpactHints.Distinct())
        {
            switch (hint)
            {
                case SecurityAssetProfileAccountingImpactHintDto.CommitmentAccounting:
                    score += 24;
                    dedicatedBehaviorNeeds.Add("commitment accounting");
                    signals.Add(new SecurityAssetProfilePromotionSignalDto(
                        "accounting.commitment",
                        "Commitment accounting",
                        "Commitment, funded, and unfunded balances usually need package-specific accounting and close controls.",
                        SecurityAssetProfilePromotionSignalSeverityDto.Strong));
                    break;
                case SecurityAssetProfileAccountingImpactHintDto.NavBasedValuation:
                    score += 18;
                    dedicatedBehaviorNeeds.Add("NAV valuation");
                    signals.Add(new SecurityAssetProfilePromotionSignalDto(
                        "valuation.nav",
                        "NAV-based valuation",
                        "NAV dates and stale valuation checks should become first-class package validation when volume grows.",
                        SecurityAssetProfilePromotionSignalSeverityDto.Strong));
                    break;
                case SecurityAssetProfileAccountingImpactHintDto.FactorSchedule:
                    score += 26;
                    dedicatedBehaviorNeeds.Add("factor schedule projection");
                    signals.Add(new SecurityAssetProfilePromotionSignalDto(
                        "projection.factor-schedule",
                        "Factor schedule projection",
                        "Factor schedules, original face, and current factor need dedicated projections for fixed-income operations.",
                        SecurityAssetProfilePromotionSignalSeverityDto.Strong));
                    break;
                case SecurityAssetProfileAccountingImpactHintDto.IncomeAccrual:
                    score += 18;
                    dedicatedBehaviorNeeds.Add("income accrual");
                    signals.Add(new SecurityAssetProfilePromotionSignalDto(
                        "accounting.income-accrual",
                        "Income accrual",
                        "Income accrual requires package-specific ledger and report-pack behavior before live fund operations depend on it.",
                        SecurityAssetProfilePromotionSignalSeverityDto.Strong));
                    break;
                case SecurityAssetProfileAccountingImpactHintDto.OwnershipPercentage:
                    score += 12;
                    dedicatedBehaviorNeeds.Add("ownership roll-forward");
                    signals.Add(new SecurityAssetProfilePromotionSignalDto(
                        "valuation.ownership",
                        "Ownership roll-forward",
                        "Ownership percentages affect exposure, valuation, and reporting projections.",
                        SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
                    break;
                case SecurityAssetProfileAccountingImpactHintDto.LedgerClassification:
                    score += 10;
                    dedicatedBehaviorNeeds.Add("ledger classification");
                    signals.Add(new SecurityAssetProfilePromotionSignalDto(
                        "accounting.ledger-classification",
                        "Ledger classification",
                        "Ledger classification hints should be promoted when the profile needs dedicated posting behavior.",
                        SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
                    break;
                case SecurityAssetProfileAccountingImpactHintDto.Valuation:
                    score += 10;
                    dedicatedBehaviorNeeds.Add("valuation projection");
                    signals.Add(new SecurityAssetProfilePromotionSignalDto(
                        "valuation.generic",
                        "Valuation projection",
                        "Valuation fields should be reviewed for package-specific projection once profile usage is high.",
                        SecurityAssetProfilePromotionSignalSeverityDto.Advisory));
                    break;
            }
        }
    }

    private static (string PackageId, string PackageName) ResolveRecommendedPackage(SecurityAssetProfileDefinitionDto profile)
    {
        var key = $"{profile.ProfileId} {profile.Category} {profile.SubType} {profile.Name}".ToLowerInvariant();
        if (key.Contains("structured", StringComparison.Ordinal) || key.Contains("io/po", StringComparison.Ordinal))
            return ("fixed-income.structured-credit", "Structured Credit Fixed-Income Package");
        if (key.Contains("real-estate", StringComparison.Ordinal) || key.Contains("realassets", StringComparison.Ordinal))
            return ("alternatives.real-estate", "Real Estate Alternatives Package");
        if (key.Contains("private-fund", StringComparison.Ordinal) || key.Contains("privatefund", StringComparison.Ordinal))
            return ("alternatives.private-funds", "Private Funds Package");
        if (key.Contains("private-company", StringComparison.Ordinal) || key.Contains("privateequity", StringComparison.Ordinal))
            return ("alternatives.private-company-equity", "Private Company Equity Package");
        if (key.Contains("co-invest", StringComparison.Ordinal) || key.Contains("spv", StringComparison.Ordinal))
            return ("alternatives.co-invest-spv", "Co-invest and SPV Package");

        return ($"custom.{profile.ProfileId}", $"{profile.Name} Package");
    }

    private static string BuildPromotionRationale(
        SecurityAssetProfilePromotionReadinessDto readiness,
        SecurityAssetProfileDefinitionDto profile,
        string packageName)
        => readiness switch
        {
            SecurityAssetProfilePromotionReadinessDto.ReadyForFirstClassPackage =>
                $"{profile.Name} has enough accounting, projection, identifier, or lifecycle signals to open a {packageName} design without breaking existing Security Master IDs.",
            SecurityAssetProfilePromotionReadinessDto.Candidate =>
                $"{profile.Name} should stay configurable while operators collect usage evidence and package-specific accounting or projection requirements.",
            _ =>
                $"{profile.Name} should remain a governed custom profile until usage or dedicated behavior justifies a first-class package."
        };

    private static IReadOnlyList<SecurityAssetProfileFieldDefinitionDto> NormalizeFields(
        IReadOnlyList<SecurityAssetProfileFieldDefinitionDto>? fields)
        => (fields ?? [])
            .Select(static field => field with
            {
                Key = Required(field.Key, nameof(field.Key)),
                Label = Required(field.Label, nameof(field.Label)),
                AllowedValues = field.AllowedValues?.Select(value => Required(value, nameof(field.AllowedValues))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? []
            })
            .OrderBy(static field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<SecurityAssetProfileIdentifierPreferenceDto> NormalizeIdentifierPreferences(
        IReadOnlyList<SecurityAssetProfileIdentifierPreferenceDto>? preferences)
        => (preferences ?? [])
            .Select(static preference => preference with { Reason = Required(preference.Reason, nameof(preference.Reason)) })
            .GroupBy(static preference => preference.Kind)
            // Duplicate kinds merge with the STRICTEST close requirement retained: taking the
            // first entry verbatim would let [Cusip optional, Cusip required] normalize to
            // optional and silently drop an identifier the submitted profile declared mandatory.
            .Select(static group => group.First() with
            {
                IsRequiredForClose = group.Any(static preference => preference.IsRequiredForClose)
            })
            .OrderBy(static preference => preference.Kind.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> NormalizeLifecycleStates(IReadOnlyList<string>? lifecycleStates)
        => (lifecycleStates ?? [])
            .Select(static state => Required(state, nameof(lifecycleStates)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void ValidateProfileDefinition(SecurityAssetProfileDefinitionDto profile)
    {
        if (!profile.ProfileId.All(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
        {
            throw new ArgumentException("ProfileId may contain only letters, digits, dash, or underscore.");
        }

        if (profile.Version <= 0)
        {
            throw new ArgumentException("Profile version must be greater than zero.");
        }

        if (profile.Fields.Count == 0)
        {
            throw new ArgumentException("At least one typed field is required.");
        }

        var duplicateField = profile.Fields
            .GroupBy(static field => field.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateField is not null)
        {
            throw new ArgumentException($"Profile field key '{duplicateField.Key}' is duplicated.");
        }

        var reservedField = profile.Fields.FirstOrDefault(static field => ReservedFieldKeys.Contains(field.Key));
        if (reservedField is not null)
        {
            throw new ArgumentException($"Profile field key '{reservedField.Key}' is reserved.");
        }

        foreach (var field in profile.Fields)
        {
            if (field.FieldType == SecurityAssetProfileFieldTypeDto.Enum && field.AllowedValues.Count == 0)
            {
                throw new ArgumentException($"Enum field '{field.Key}' requires at least one allowed value.");
            }

            if (field.MinValue.HasValue && field.MaxValue.HasValue && field.MinValue.Value > field.MaxValue.Value)
            {
                throw new ArgumentException($"Field '{field.Key}' has a minimum value greater than its maximum value.");
            }
        }

        var fieldsByKey = profile.Fields.ToDictionary(static field => field.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in profile.DateOrderRules)
        {
            if (!fieldsByKey.TryGetValue(rule.StartFieldKey, out var startField)
                || !fieldsByKey.TryGetValue(rule.EndFieldKey, out var endField))
            {
                throw new ArgumentException("Date order rules must reference profile field keys.");
            }

            // Runtime enforcement (ValidateDateOrderRule) silently passes whenever either value
            // cannot be read as a date, so a rule over non-Date fields would advertise an ordering
            // control that never fires. Require Date-typed fields at definition time instead.
            if (startField.FieldType != SecurityAssetProfileFieldTypeDto.Date
                || endField.FieldType != SecurityAssetProfileFieldTypeDto.Date)
            {
                throw new ArgumentException(
                    $"Date order rule '{rule.Code}' must reference Date-typed profile fields; " +
                    $"'{rule.StartFieldKey}' is {startField.FieldType} and '{rule.EndFieldKey}' is {endField.FieldType}.");
            }

            _ = Required(rule.Code, nameof(rule.Code));
            _ = Required(rule.Message, nameof(rule.Message));
        }
    }

    private static string ResolveActor(string actor, string? requestedBy)
        => string.IsNullOrWhiteSpace(actor)
            ? Required(requestedBy, nameof(requestedBy))
            : actor.Trim();

    private static string ResolveCorrelationId(string? correlationId, string prefix)
        => string.IsNullOrWhiteSpace(correlationId)
            ? $"{prefix}-{Guid.NewGuid():N}"
            : correlationId.Trim();

    private static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }

    private sealed record SecurityAssetProfileGovernanceSnapshot(
        IReadOnlyList<SecurityAssetProfileDefinitionDto> Profiles,
        IReadOnlyList<SecurityAssetProfileGovernanceAuditEventDto> AuditEvents);
}
