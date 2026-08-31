using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Identity;

public interface IRolePermissionProfileStore
{
    Task<RolePermissionCatalogDto> GetCatalogAsync(CancellationToken ct = default);

    bool TryGetProfile(string roleProfileName, out RolePermissionProfileDto profile);

    Task<RolePermissionProfileUpsertResultDto> UpsertAsync(
        RolePermissionProfileUpsertRequestDto request,
        string actor,
        CancellationToken ct = default);
}

public sealed class FileRolePermissionProfileStore : IRolePermissionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _path;
    private readonly ILogger<FileRolePermissionProfileStore>? _logger;

    public FileRolePermissionProfileStore(StorageOptions storageOptions, ILogger<FileRolePermissionProfileStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _path = Path.Combine(storageOptions.RootPath, "governance", "role-permission-profiles.json");
        _logger = logger;
    }

    public Task<RolePermissionCatalogDto> GetCatalogAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(BuildCatalog(ReadSnapshot().Profiles));
    }

    public bool TryGetProfile(string roleProfileName, out RolePermissionProfileDto profile)
    {
        var normalized = NormalizeProfileName(roleProfileName);
        if (normalized.Length == 0)
        {
            profile = null!;
            return false;
        }

        profile = ReadSnapshot().Profiles
            .Select(ToDto)
            .FirstOrDefault(candidate => ProfileKey(candidate.Role) == normalized || ProfileKey(candidate.DisplayName) == normalized)!;
        return profile is not null;
    }

    public async Task<RolePermissionProfileUpsertResultDto> UpsertAsync(
        RolePermissionProfileUpsertRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = Validate(request, actor);
        var now = DateTimeOffset.UtcNow;
        var auditId = IdentityGovernanceNormalization.NewAuditId("role-profile", now, maxLength: 46);
        var correlationId = IdentityGovernanceNormalization.NormalizeCorrelationId(request.CorrelationId, "settings-role-profile");

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = ReadSnapshot();
            var profiles = snapshot.Profiles.ToList();
            var index = profiles.FindIndex(profile => ProfileKey(profile.ProfileName) == validated.ProfileKey);
            var existing = index >= 0 ? profiles[index] : null;

            var persisted = new PersistedRolePermissionProfile(
                ProfileName: validated.ProfileName,
                DisplayName: validated.DisplayName,
                Description: validated.Description,
                BaseRole: validated.BaseRole,
                Permissions: validated.PermissionNames,
                PermissionMask: (long)validated.Permissions,
                CreatedBy: existing?.CreatedBy ?? validated.Actor,
                CreatedAtUtc: existing?.CreatedAtUtc ?? now,
                UpdatedBy: validated.Actor,
                UpdatedAtUtc: now,
                LastRationale: validated.Rationale,
                LastAuditId: auditId,
                LastCorrelationId: correlationId);

            if (index >= 0)
            {
                profiles[index] = persisted;
            }
            else
            {
                profiles.Add(persisted);
            }

            await SaveAsync(new RolePermissionProfileSnapshot(profiles), ct).ConfigureAwait(false);

            var profileDto = ToDto(persisted);
            var catalog = BuildCatalog(profiles);
            var auditEvent = new RolePermissionProfileAuditEventDto(
                AuditId: auditId,
                EventType: "role-permission-profile-upserted",
                OccurredAtUtc: now,
                Actor: validated.Actor,
                Rationale: validated.Rationale,
                CorrelationId: correlationId,
                ProfileName: validated.ProfileName,
                BaseRole: validated.BaseRole,
                PermissionNames: validated.PermissionNames,
                PermissionMask: (long)validated.Permissions);

            return new RolePermissionProfileUpsertResultDto(profileDto, catalog, auditEvent);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private RolePermissionProfileSnapshot ReadSnapshot()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return new RolePermissionProfileSnapshot([]);
            }

            try
            {
                var snapshot = JsonSerializer.Deserialize<RolePermissionProfileSnapshot>(
                    File.ReadAllText(_path),
                    JsonOptions);
                return snapshot ?? new RolePermissionProfileSnapshot([]);
            }
            catch (JsonException ex)
            {
                // Fail safe (no custom profiles) so built-in roles still resolve, but surface the
                // data-integrity problem instead of silently masking a corrupt governance file.
                _logger?.LogError(
                    ex,
                    "Corrupt role-permission-profile governance file at {Path}; treating as no custom profiles. Manual data-integrity review required.",
                    _path);
                return new RolePermissionProfileSnapshot([]);
            }
        }
    }

    private async Task SaveAsync(RolePermissionProfileSnapshot snapshot, CancellationToken ct)
    {
        var ordered = snapshot with
        {
            Profiles = snapshot.Profiles
                .OrderBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        await AtomicFileWriter.WriteAsync(_path, json, ct).ConfigureAwait(false);
    }

    private static RolePermissionCatalogDto BuildCatalog(IReadOnlyList<PersistedRolePermissionProfile> customProfiles)
    {
        var builtIn = RolePermissions.GetCatalog();
        var profiles = builtIn.Roles
            .Concat(customProfiles.Select(ToDto))
            .OrderBy(profile => profile.IsBuiltIn ? 0 : 1)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return builtIn with { Roles = profiles };
    }

    private static ValidatedRolePermissionProfile Validate(
        RolePermissionProfileUpsertRequestDto request,
        string actor)
    {
        var profileName = IdentityGovernanceNormalization.NormalizeRequired(request.ProfileName, nameof(request.ProfileName));
        var profileKey = ProfileKey(profileName);
        if (profileKey.Length == 0)
        {
            throw new ArgumentException("ProfileName must include at least one letter or digit.", nameof(request));
        }

        if (Enum.GetNames<UserRole>().Any(role => ProfileKey(role) == profileKey))
        {
            throw new ArgumentException("Custom role profile names cannot replace a built-in role.", nameof(request));
        }

        var displayName = IdentityGovernanceNormalization.NormalizeRequired(request.DisplayName, nameof(request.DisplayName));
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? $"Custom authority profile based on {request.BaseRole}."
            : request.Description.Trim();
        if (!Enum.TryParse<UserRole>(request.BaseRole?.Trim(), ignoreCase: true, out var baseRole))
        {
            throw new ArgumentException($"Unknown base role '{request.BaseRole}'.", nameof(request));
        }

        if (!RolePermissions.TryParsePermissionNames(request.PermissionNames, out var permissions, out var invalid) ||
            invalid.Count > 0)
        {
            throw new ArgumentException($"Unknown permissions: {string.Join(", ", invalid)}.", nameof(request));
        }

        if (permissions == UserPermission.None)
        {
            throw new ArgumentException("At least one permission is required.", nameof(request));
        }

        var rationale = IdentityGovernanceNormalization.NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var resolvedActor = IdentityGovernanceNormalization.ResolveActor(actor, request.RequestedBy);

        return new ValidatedRolePermissionProfile(
            ProfileName: profileName,
            ProfileKey: profileKey,
            DisplayName: displayName,
            Description: description,
            BaseRole: baseRole.ToString(),
            PermissionNames: RolePermissions.GetPermissionNames(permissions),
            Permissions: permissions,
            Actor: resolvedActor,
            Rationale: rationale);
    }

    private static string NormalizeProfileName(string? value) => ProfileKey(value ?? string.Empty);

    private static string ProfileKey(string value)
        => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static RolePermissionProfileDto ToDto(PersistedRolePermissionProfile profile)
        => new(
            Role: profile.ProfileName,
            DisplayName: profile.DisplayName,
            Description: profile.Description,
            IsBuiltIn: false,
            Permissions: profile.Permissions,
            PermissionMask: profile.PermissionMask,
            BaseRole: profile.BaseRole,
            CreatedBy: profile.CreatedBy,
            CreatedAtUtc: profile.CreatedAtUtc,
            UpdatedBy: profile.UpdatedBy,
            UpdatedAtUtc: profile.UpdatedAtUtc,
            LastRationale: profile.LastRationale,
            LastAuditId: profile.LastAuditId);

    private sealed record RolePermissionProfileSnapshot(
        IReadOnlyList<PersistedRolePermissionProfile> Profiles);

    private sealed record PersistedRolePermissionProfile(
        string ProfileName,
        string DisplayName,
        string Description,
        string BaseRole,
        IReadOnlyList<string> Permissions,
        long PermissionMask,
        string CreatedBy,
        DateTimeOffset CreatedAtUtc,
        string UpdatedBy,
        DateTimeOffset UpdatedAtUtc,
        string LastRationale,
        string LastAuditId,
        string LastCorrelationId);

    private sealed record ValidatedRolePermissionProfile(
        string ProfileName,
        string ProfileKey,
        string DisplayName,
        string Description,
        string BaseRole,
        IReadOnlyList<string> PermissionNames,
        UserPermission Permissions,
        string Actor,
        string Rationale);
}
