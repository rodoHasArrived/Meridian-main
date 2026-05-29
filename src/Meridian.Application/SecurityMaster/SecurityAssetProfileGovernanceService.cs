using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityAssetProfileGovernanceService : ISecurityAssetProfileCatalog
{
    IReadOnlyList<SecurityAssetProfileDefinitionDto> GetAllProfiles();
    SecurityAssetProfileLineageDto GetLineage(string profileId);

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
    private readonly IReadOnlyList<SecurityAssetProfileDefinitionDto> _seedProfiles = SecurityAssetProfileSeeds.Create();
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

    public IReadOnlyList<SecurityAssetProfileDefinitionDto> GetProfiles()
        => GetAllProfiles()
            .Where(static profile => profile.Status == SecurityAssetProfileStatusDto.Approved)
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
        profile = GetProfiles()
            .Where(candidate => string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
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
                ChangeReason = rationale
            };
            ValidateProfileDefinition(approved);
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
                ChangeReason = $"Rollback to version {request.TargetVersion}: {rationale}"
            };
            ValidateProfileDefinition(rollback);
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
            catch (JsonException)
            {
                return new SecurityAssetProfileGovernanceSnapshot([], []);
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
            .Select(static group => group.First())
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

        var fieldKeys = profile.Fields.Select(static field => field.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in profile.DateOrderRules)
        {
            if (!fieldKeys.Contains(rule.StartFieldKey) || !fieldKeys.Contains(rule.EndFieldKey))
            {
                throw new ArgumentException("Date order rules must reference profile field keys.");
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
