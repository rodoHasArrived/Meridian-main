using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.Identity;

public interface IUserAccountStore
{
    bool HasAccounts { get; }

    IReadOnlyList<UserAccountConfig> LoadAccounts();

    Task<IReadOnlyList<UserAccountDto>> GetAccountsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<UserAccountAuditEventDto>> GetAuditEventsAsync(int? limit = null, CancellationToken ct = default);

    Task<UserAccountMutationResultDto> UpsertAsync(
        UserAccountUpsertRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<UserAccountMutationResultDto> ResetPasswordAsync(
        UserPasswordResetRequestDto request,
        string actor,
        int revokedSessionCount = 0,
        CancellationToken ct = default);

    Task<UserAccountMutationResultDto> SetDisabledAsync(
        UserAccountDisableRequestDto request,
        string actor,
        int revokedSessionCount = 0,
        CancellationToken ct = default);

    Task<UserSessionRevokeResultDto> RecordSessionRevocationAsync(
        UserSessionRevokeRequestDto request,
        string actor,
        int revokedSessionCount,
        CancellationToken ct = default);
}

public sealed class FileUserAccountStore : IUserAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _path;
    private readonly string _auditPath;

    public FileUserAccountStore(StorageOptions storageOptions)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _path = Path.Combine(storageOptions.RootPath, "governance", "user-accounts.json");
        _auditPath = Path.Combine(storageOptions.RootPath, "governance", "user-account-audit.jsonl");
    }

    public bool HasAccounts => ReadSnapshot().Accounts.Count > 0;

    public IReadOnlyList<UserAccountConfig> LoadAccounts()
        => ReadSnapshot().Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Username) &&
                              PasswordHashing.IsSupportedHash(account.PasswordHash))
            .Select(account => new UserAccountConfig(
                account.Username,
                account.PasswordHash,
                ParseRole(account.Role),
                account.RoleProfileName,
                account.PermissionNames,
                account.IsDisabled,
                account.PasswordResetRequired))
            .ToArray();

    public Task<IReadOnlyList<UserAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<UserAccountDto>>(
            ReadSnapshot().Accounts
                .Select(ToDto)
                .OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public async Task<IReadOnlyList<UserAccountAuditEventDto>> GetAuditEventsAsync(
        int? limit = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_auditPath))
        {
            return [];
        }

        var events = new List<UserAccountAuditEventDto>();
        var lines = await File.ReadAllLinesAsync(_auditPath, ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                    var auditEvent = JsonSerializer.Deserialize<UserAccountAuditEventDto>(line, AuditJsonOptions);
                if (auditEvent is not null)
                {
                    events.Add(auditEvent);
                }
            }
            catch (JsonException)
            {
                // Keep the account surface available even if one historical audit line is corrupt.
            }
            catch (NotSupportedException)
            {
                // Keep the account surface available even if one historical audit line is corrupt.
            }
        }

        return events
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(limit.GetValueOrDefault(100))
            .ToArray();
    }

    public async Task<UserAccountMutationResultDto> UpsertAsync(
        UserAccountUpsertRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;
        var validated = ValidateAccountRequest(
            request.Username,
            request.Role,
            request.RoleProfileName,
            request.PermissionNames,
            request.RequestedBy,
            request.Rationale,
            actor);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = ReadSnapshot();
            var accounts = snapshot.Accounts.ToList();
            var index = accounts.FindIndex(account => AccountKey(account.Username) == validated.AccountKey);
            var existing = index >= 0 ? accounts[index] : null;
            var passwordHash = ResolvePasswordHash(request.NewPassword, request.PasswordHash, existing?.PasswordHash);
            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new ArgumentException("A new account requires NewPassword or PasswordHash.", nameof(request));
            }

            var auditId = NewAuditId("user-account");
            var correlationId = NormalizeCorrelationId(request.CorrelationId, "user-account-upsert");
            var persisted = new PersistedUserAccount(
                Username: validated.Username,
                PasswordHash: passwordHash,
                Role: validated.Role,
                RoleProfileName: validated.RoleProfileName,
                PermissionNames: validated.PermissionNames,
                IsDisabled: request.IsDisabled ?? existing?.IsDisabled ?? false,
                PasswordResetRequired: request.PasswordResetRequired,
                CreatedAtUtc: existing?.CreatedAtUtc ?? now,
                CreatedBy: existing?.CreatedBy ?? validated.Actor,
                UpdatedAtUtc: now,
                UpdatedBy: validated.Actor,
                LastPasswordResetAtUtc: existing is null || !string.Equals(existing.PasswordHash, passwordHash, StringComparison.Ordinal)
                    ? now
                    : existing.LastPasswordResetAtUtc,
                DisabledAtUtc: request.IsDisabled == true
                    ? now
                    : request.IsDisabled == false
                        ? null
                        : existing?.DisabledAtUtc,
                DisabledBy: request.IsDisabled == true
                    ? validated.Actor
                    : request.IsDisabled == false
                        ? null
                        : existing?.DisabledBy,
                LastAuditId: auditId);

            if (index >= 0)
            {
                accounts[index] = persisted;
            }
            else
            {
                accounts.Add(persisted);
            }

            await SaveAsync(new UserAccountSnapshot(accounts), ct).ConfigureAwait(false);
            var dto = ToDto(persisted);
            var auditEvent = BuildAuditEvent(
                auditId,
                existing is null ? "user-account-created" : "user-account-updated",
                now,
                validated.Actor,
                dto,
                validated.Rationale,
                correlationId);
            await AppendAuditAsync(auditEvent, ct).ConfigureAwait(false);
            return new UserAccountMutationResultDto(dto, auditEvent);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<UserAccountMutationResultDto> ResetPasswordAsync(
        UserPasswordResetRequestDto request,
        string actor,
        int revokedSessionCount = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var username = NormalizeRequired(request.Username, nameof(request.Username));
        var accountKey = AccountKey(username);
        var resolvedActor = ResolveActor(actor, request.RequestedBy);
        var rationale = NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var passwordHash = ResolvePasswordHash(request.NewPassword, request.PasswordHash, existingHash: null)
            ?? throw new ArgumentException("Password reset requires NewPassword or PasswordHash.", nameof(request));
        var now = DateTimeOffset.UtcNow;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = ReadSnapshot();
            var accounts = snapshot.Accounts.ToList();
            var index = accounts.FindIndex(account => AccountKey(account.Username) == accountKey);
            if (index < 0)
            {
                throw new KeyNotFoundException($"User account '{username}' was not found.");
            }

            var existing = accounts[index];
            var auditId = NewAuditId("user-password-reset");
            var updated = existing with
            {
                PasswordHash = passwordHash,
                PasswordResetRequired = request.PasswordResetRequired,
                UpdatedAtUtc = now,
                UpdatedBy = resolvedActor,
                LastPasswordResetAtUtc = now,
                LastAuditId = auditId
            };
            accounts[index] = updated;
            await SaveAsync(new UserAccountSnapshot(accounts), ct).ConfigureAwait(false);

            var dto = ToDto(updated);
            var auditEvent = BuildAuditEvent(
                auditId,
                "user-password-reset",
                now,
                resolvedActor,
                dto,
                rationale,
                NormalizeCorrelationId(request.CorrelationId, "user-password-reset"),
                revokedSessionCount);
            await AppendAuditAsync(auditEvent, ct).ConfigureAwait(false);
            return new UserAccountMutationResultDto(dto, auditEvent, revokedSessionCount);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<UserAccountMutationResultDto> SetDisabledAsync(
        UserAccountDisableRequestDto request,
        string actor,
        int revokedSessionCount = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var username = NormalizeRequired(request.Username, nameof(request.Username));
        var accountKey = AccountKey(username);
        var resolvedActor = ResolveActor(actor, request.RequestedBy);
        var rationale = NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var now = DateTimeOffset.UtcNow;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = ReadSnapshot();
            var accounts = snapshot.Accounts.ToList();
            var index = accounts.FindIndex(account => AccountKey(account.Username) == accountKey);
            if (index < 0)
            {
                throw new KeyNotFoundException($"User account '{username}' was not found.");
            }

            var existing = accounts[index];
            var auditId = NewAuditId(request.IsDisabled ? "user-account-disabled" : "user-account-enabled");
            var updated = existing with
            {
                IsDisabled = request.IsDisabled,
                UpdatedAtUtc = now,
                UpdatedBy = resolvedActor,
                DisabledAtUtc = request.IsDisabled ? now : null,
                DisabledBy = request.IsDisabled ? resolvedActor : null,
                LastAuditId = auditId
            };
            accounts[index] = updated;
            await SaveAsync(new UserAccountSnapshot(accounts), ct).ConfigureAwait(false);

            var dto = ToDto(updated);
            var auditEvent = BuildAuditEvent(
                auditId,
                request.IsDisabled ? "user-account-disabled" : "user-account-enabled",
                now,
                resolvedActor,
                dto,
                rationale,
                NormalizeCorrelationId(request.CorrelationId, request.IsDisabled ? "user-account-disabled" : "user-account-enabled"),
                revokedSessionCount);
            await AppendAuditAsync(auditEvent, ct).ConfigureAwait(false);
            return new UserAccountMutationResultDto(dto, auditEvent, revokedSessionCount);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<UserSessionRevokeResultDto> RecordSessionRevocationAsync(
        UserSessionRevokeRequestDto request,
        string actor,
        int revokedSessionCount,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolvedActor = ResolveActor(actor, request.RequestedBy);
        var rationale = NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var now = DateTimeOffset.UtcNow;
        var auditId = NewAuditId("user-session-revoked");
        var correlationId = NormalizeCorrelationId(request.CorrelationId, "user-session-revoke");
        var username = string.IsNullOrWhiteSpace(request.Username) ? "*" : request.Username.Trim();

        var auditEvent = new UserAccountAuditEventDto(
            AuditId: auditId,
            EventType: request.RevokeAll ? "user-sessions-revoked-all" : "user-sessions-revoked",
            OccurredAtUtc: now,
            Actor: resolvedActor,
            Username: username,
            Rationale: rationale,
            CorrelationId: correlationId,
            Role: string.Empty,
            PermissionNames: [],
            PermissionMask: 0,
            IsDisabled: false,
            PasswordResetRequired: false,
            RevokedSessionCount: revokedSessionCount);
        await AppendAuditAsync(auditEvent, ct).ConfigureAwait(false);

        return new UserSessionRevokeResultDto(
            auditId,
            now,
            resolvedActor,
            request.RevokeAll ? null : username,
            request.RevokeAll,
            revokedSessionCount,
            rationale,
            correlationId);
    }

    private UserAccountSnapshot ReadSnapshot()
    {
        lock (_readGate)
        {
            if (!File.Exists(_path))
            {
                return new UserAccountSnapshot([]);
            }

            try
            {
                var snapshot = JsonSerializer.Deserialize<UserAccountSnapshot>(
                    File.ReadAllText(_path),
                    JsonOptions);
                return snapshot ?? new UserAccountSnapshot([]);
            }
            catch (JsonException)
            {
                return new UserAccountSnapshot([]);
            }
        }
    }

    private async Task SaveAsync(UserAccountSnapshot snapshot, CancellationToken ct)
    {
        var ordered = snapshot with
        {
            Accounts = snapshot.Accounts
                .OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        await AtomicFileWriter.WriteAsync(_path, json, ct).ConfigureAwait(false);
    }

    private async Task AppendAuditAsync(UserAccountAuditEventDto auditEvent, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(auditEvent, AuditJsonOptions);
        await AtomicFileWriter.AppendLinesAsync(_auditPath, [json], ct).ConfigureAwait(false);
    }

    private static ValidatedUserAccountRequest ValidateAccountRequest(
        string username,
        string role,
        string? roleProfileName,
        IReadOnlyList<string>? permissionNames,
        string requestedBy,
        string rationale,
        string actor)
    {
        var normalizedUsername = NormalizeRequired(username, nameof(username));
        if (!Enum.TryParse<UserRole>(role?.Trim(), ignoreCase: true, out var parsedRole))
        {
            throw new ArgumentException($"Unknown role '{role}'.", nameof(role));
        }

        UserPermission permissions;
        IReadOnlyList<string> resolvedPermissionNames;
        if (permissionNames is { Count: > 0 })
        {
            if (!RolePermissions.TryParsePermissionNames(permissionNames, out permissions, out var invalid) || invalid.Count > 0)
            {
                throw new ArgumentException($"Unknown permissions: {string.Join(", ", invalid)}.", nameof(permissionNames));
            }

            resolvedPermissionNames = RolePermissions.GetPermissionNames(permissions);
        }
        else
        {
            permissions = RolePermissions.For(parsedRole);
            resolvedPermissionNames = RolePermissions.GetPermissionNames(permissions);
        }

        return new ValidatedUserAccountRequest(
            normalizedUsername,
            AccountKey(normalizedUsername),
            parsedRole.ToString(),
            string.IsNullOrWhiteSpace(roleProfileName) ? null : roleProfileName.Trim(),
            resolvedPermissionNames,
            permissions,
            ResolveActor(actor, requestedBy),
            NormalizeRequired(rationale, nameof(rationale)));
    }

    private static string? ResolvePasswordHash(string? newPassword, string? passwordHash, string? existingHash)
    {
        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            return PasswordHashing.HashPassword(newPassword);
        }

        if (!string.IsNullOrWhiteSpace(passwordHash))
        {
            if (!PasswordHashing.IsSupportedHash(passwordHash))
            {
                throw new ArgumentException("PasswordHash must use the supported Meridian password hash format.", nameof(passwordHash));
            }

            return passwordHash.Trim();
        }

        return existingHash;
    }

    private static UserAccountDto ToDto(PersistedUserAccount account)
    {
        var role = ParseRole(account.Role);
        var permissionNames = account.PermissionNames.Count > 0
            ? account.PermissionNames
            : RolePermissions.GetPermissionNames(RolePermissions.For(role));
        RolePermissions.TryParsePermissionNames(permissionNames, out var permissions, out _);

        return new UserAccountDto(
            Username: account.Username,
            Role: role.ToString(),
            RoleProfileName: account.RoleProfileName,
            PermissionNames: permissionNames,
            PermissionMask: (long)permissions,
            IsDisabled: account.IsDisabled,
            PasswordResetRequired: account.PasswordResetRequired,
            CreatedAtUtc: account.CreatedAtUtc,
            UpdatedAtUtc: account.UpdatedAtUtc,
            CreatedBy: account.CreatedBy,
            UpdatedBy: account.UpdatedBy,
            LastPasswordResetAtUtc: account.LastPasswordResetAtUtc,
            DisabledAtUtc: account.DisabledAtUtc,
            DisabledBy: account.DisabledBy,
            LastAuditId: account.LastAuditId);
    }

    private static UserAccountAuditEventDto BuildAuditEvent(
        string auditId,
        string eventType,
        DateTimeOffset occurredAtUtc,
        string actor,
        UserAccountDto account,
        string rationale,
        string correlationId,
        int revokedSessionCount = 0)
        => new(
            auditId,
            eventType,
            occurredAtUtc,
            actor,
            account.Username,
            rationale,
            correlationId,
            account.Role,
            account.PermissionNames,
            account.PermissionMask,
            account.IsDisabled,
            account.PasswordResetRequired,
            revokedSessionCount);

    private static UserRole ParseRole(string role)
        => Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed) ? parsed : UserRole.ReadOnly;

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return trimmed;
    }

    private static string ResolveActor(string? actor, string? requestedBy)
    {
        if (!string.IsNullOrWhiteSpace(actor))
        {
            return actor.Trim();
        }

        return NormalizeRequired(requestedBy, nameof(requestedBy));
    }

    private static string NormalizeCorrelationId(string? correlationId, string prefix)
        => string.IsNullOrWhiteSpace(correlationId)
            ? $"{prefix}-{Guid.NewGuid():N}"
            : correlationId.Trim();

    private static string NewAuditId(string prefix)
        => $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..Math.Min(64, prefix.Length + 1 + 17 + 1 + 32)];

    private static string AccountKey(string username)
        => username.Trim().ToLowerInvariant();

    private sealed record UserAccountSnapshot(IReadOnlyList<PersistedUserAccount> Accounts);

    private sealed record PersistedUserAccount(
        string Username,
        string PasswordHash,
        string Role,
        string? RoleProfileName,
        IReadOnlyList<string> PermissionNames,
        bool IsDisabled,
        bool PasswordResetRequired,
        DateTimeOffset CreatedAtUtc,
        string CreatedBy,
        DateTimeOffset UpdatedAtUtc,
        string UpdatedBy,
        DateTimeOffset? LastPasswordResetAtUtc,
        DateTimeOffset? DisabledAtUtc,
        string? DisabledBy,
        string LastAuditId);

    private sealed record ValidatedUserAccountRequest(
        string Username,
        string AccountKey,
        string Role,
        string? RoleProfileName,
        IReadOnlyList<string> PermissionNames,
        UserPermission Permissions,
        string Actor,
        string Rationale);
}
