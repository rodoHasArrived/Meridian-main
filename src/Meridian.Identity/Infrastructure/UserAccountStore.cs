using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

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
    private const int MutationIntentVersion = 1;
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
    private readonly string _mutationIntentPath;
    private readonly ILogger<FileUserAccountStore>? _logger;

    public FileUserAccountStore(StorageOptions storageOptions, ILogger<FileUserAccountStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _path = Path.Combine(storageOptions.RootPath, "governance", "user-accounts.json");
        _auditPath = Path.Combine(storageOptions.RootPath, "governance", "user-account-audit.jsonl");
        _mutationIntentPath = Path.Combine(storageOptions.RootPath, "governance", "user-account-mutation-intent.json");
        _logger = logger;
        RecoverPendingMutation();
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
                account.PasswordResetRequired,
                account.CompanyId))
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
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RecoverPendingMutation();
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
                catch (JsonException ex)
                {
                    // Keep the account surface available even if one historical audit line is corrupt.
                    _logger?.LogWarning(ex, "Skipping corrupt user-account audit line in {Path}.", _auditPath);
                }
                catch (NotSupportedException ex)
                {
                    // Keep the account surface available even if one historical audit line is corrupt.
                    _logger?.LogWarning(ex, "Skipping unreadable user-account audit line in {Path}.", _auditPath);
                }
            }

            return events
                .OrderByDescending(item => item.OccurredAtUtc)
                .Take(limit.GetValueOrDefault(100))
                .ToArray();
        }
        finally
        {
            _writeGate.Release();
        }
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
            request.CompanyId,
            request.RequestedBy,
            request.Rationale,
            actor);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RecoverPendingMutation();
            var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            var accounts = snapshot.Accounts.ToList();
            var index = accounts.FindIndex(account => AccountKey(account.Username) == validated.AccountKey);
            var existing = index >= 0 ? accounts[index] : null;
            var passwordHash = ResolvePasswordHash(request.NewPassword, request.PasswordHash, existing?.PasswordHash);
            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new ArgumentException("A new account requires NewPassword or PasswordHash.", nameof(request));
            }

            var auditId = IdentityGovernanceNormalization.NewAuditId("user-account", now);
            var correlationId = IdentityGovernanceNormalization.NormalizeCorrelationId(request.CorrelationId, "user-account-upsert");
            var persisted = new PersistedUserAccount(
                Username: validated.Username,
                PasswordHash: passwordHash,
                Role: validated.Role,
                RoleProfileName: validated.RoleProfileName,
                CompanyId: validated.CompanyId,
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

            var dto = ToDto(persisted);
            var auditEvent = BuildAuditEvent(
                auditId,
                existing is null ? "user-account-created" : "user-account-updated",
                now,
                validated.Actor,
                dto,
                validated.Rationale,
                correlationId);
            await CommitAccountMutationAsync(
                new UserAccountSnapshot(accounts),
                auditEvent,
                ct).ConfigureAwait(false);
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
        var username = IdentityGovernanceNormalization.NormalizeRequired(request.Username, nameof(request.Username));
        var accountKey = AccountKey(username);
        var resolvedActor = IdentityGovernanceNormalization.ResolveActor(actor, request.RequestedBy);
        var rationale = IdentityGovernanceNormalization.NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var passwordHash = ResolvePasswordHash(request.NewPassword, request.PasswordHash, existingHash: null)
            ?? throw new ArgumentException("Password reset requires NewPassword or PasswordHash.", nameof(request));
        var now = DateTimeOffset.UtcNow;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RecoverPendingMutation();
            var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            var accounts = snapshot.Accounts.ToList();
            var index = accounts.FindIndex(account => AccountKey(account.Username) == accountKey);
            if (index < 0)
            {
                throw new KeyNotFoundException($"User account '{username}' was not found.");
            }

            var existing = accounts[index];
            var auditId = IdentityGovernanceNormalization.NewAuditId("user-password-reset", now);
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
            var dto = ToDto(updated);
            var auditEvent = BuildAuditEvent(
                auditId,
                "user-password-reset",
                now,
                resolvedActor,
                dto,
                rationale,
                IdentityGovernanceNormalization.NormalizeCorrelationId(request.CorrelationId, "user-password-reset"),
                revokedSessionCount);
            await CommitAccountMutationAsync(
                new UserAccountSnapshot(accounts),
                auditEvent,
                ct).ConfigureAwait(false);
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
        var username = IdentityGovernanceNormalization.NormalizeRequired(request.Username, nameof(request.Username));
        var accountKey = AccountKey(username);
        var resolvedActor = IdentityGovernanceNormalization.ResolveActor(actor, request.RequestedBy);
        var rationale = IdentityGovernanceNormalization.NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var now = DateTimeOffset.UtcNow;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RecoverPendingMutation();
            var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            var accounts = snapshot.Accounts.ToList();
            var index = accounts.FindIndex(account => AccountKey(account.Username) == accountKey);
            if (index < 0)
            {
                throw new KeyNotFoundException($"User account '{username}' was not found.");
            }

            var existing = accounts[index];
            var auditId = IdentityGovernanceNormalization.NewAuditId(request.IsDisabled ? "user-account-disabled" : "user-account-enabled", now);
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
            var dto = ToDto(updated);
            var auditEvent = BuildAuditEvent(
                auditId,
                request.IsDisabled ? "user-account-disabled" : "user-account-enabled",
                now,
                resolvedActor,
                dto,
                rationale,
                IdentityGovernanceNormalization.NormalizeCorrelationId(request.CorrelationId, request.IsDisabled ? "user-account-disabled" : "user-account-enabled"),
                revokedSessionCount);
            await CommitAccountMutationAsync(
                new UserAccountSnapshot(accounts),
                auditEvent,
                ct).ConfigureAwait(false);
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
        var resolvedActor = IdentityGovernanceNormalization.ResolveActor(actor, request.RequestedBy);
        var rationale = IdentityGovernanceNormalization.NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var now = DateTimeOffset.UtcNow;
        var auditId = IdentityGovernanceNormalization.NewAuditId("user-session-revoked", now);
        var correlationId = IdentityGovernanceNormalization.NormalizeCorrelationId(request.CorrelationId, "user-session-revoke");
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
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RecoverPendingMutation();
            await AppendAuditIfMissingAsync(auditEvent, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

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
        _writeGate.Wait();
        try
        {
            RecoverPendingMutation();
            lock (_readGate)
            {
                if (!File.Exists(_path))
                {
                    return new UserAccountSnapshot([]);
                }

                return ParseSnapshotSafe(File.ReadAllText(_path));
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Async snapshot read for the mutation paths, which already serialize on
    /// <see cref="_writeGate"/> — the blocking <see cref="_readGate"/> is not taken here.
    /// The snapshot file is written via <see cref="AtomicFileWriter"/> (write + rename),
    /// so a plain read never observes a partial write.
    /// </summary>
    private async Task<UserAccountSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return new UserAccountSnapshot([]);
        }

        var json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
        return ParseSnapshotSafe(json);
    }

    private UserAccountSnapshot ParseSnapshotSafe(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<UserAccountSnapshot>(json, JsonOptions);
            return snapshot ?? new UserAccountSnapshot([]);
        }
        catch (JsonException ex)
        {
            // Fail safe (no accounts) so the surface stays available, but surface the
            // data-integrity problem instead of silently masking a corrupt governance file
            // as "no accounts exist".
            _logger?.LogError(
                ex,
                "Corrupt user-account governance file at {Path}; treating as no accounts. Manual data-integrity review required.",
                _path);
            return new UserAccountSnapshot([]);
        }
    }

    private async Task SaveAsync(UserAccountSnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(OrderSnapshot(snapshot), JsonOptions);
        await AtomicFileWriter.WriteAsync(_path, json, ct).ConfigureAwait(false);
    }

    private void Save(UserAccountSnapshot snapshot)
        => AtomicFileWriter.Write(_path, JsonSerializer.Serialize(OrderSnapshot(snapshot), JsonOptions));

    private async Task CommitAccountMutationAsync(
        UserAccountSnapshot snapshot,
        UserAccountAuditEventDto auditEvent,
        CancellationToken ct)
    {
        var intent = new UserAccountMutationIntent(
            MutationIntentVersion,
            auditEvent.AuditId,
            DateTimeOffset.UtcNow,
            OrderSnapshot(snapshot),
            auditEvent);
        ValidateMutationIntent(intent);

        // The atomic intent is the commit point. Before it exists, cancellation leaves no effect;
        // after it exists, completion is deliberately non-cancellable so callers never observe a
        // cancellation while a recoverable mutation is already committed.
        await AtomicFileWriter.WriteAsync(
            _mutationIntentPath,
            JsonSerializer.Serialize(intent, JsonOptions),
            ct).ConfigureAwait(false);

        await AppendAuditIfMissingAsync(auditEvent, CancellationToken.None).ConfigureAwait(false);
        await SaveAsync(intent.Snapshot, CancellationToken.None).ConfigureAwait(false);
        await DeleteMutationIntentAsync().ConfigureAwait(false);
    }

    private async Task AppendAuditIfMissingAsync(UserAccountAuditEventDto auditEvent, CancellationToken ct)
    {
        if (File.Exists(_auditPath))
        {
            var existingLines = await File.ReadAllLinesAsync(_auditPath, ct).ConfigureAwait(false);
            if (ContainsAuditId(existingLines, auditEvent.AuditId))
            {
                return;
            }
        }

        var json = JsonSerializer.Serialize(auditEvent, AuditJsonOptions);
        await AtomicFileWriter.AppendLinesAsync(_auditPath, [json], ct).ConfigureAwait(false);
    }

    private void AppendAuditIfMissing(UserAccountAuditEventDto auditEvent)
    {
        var existing = File.Exists(_auditPath) ? File.ReadAllText(_auditPath) : string.Empty;
        if (ContainsAuditId(existing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries), auditEvent.AuditId))
        {
            return;
        }

        var separator = existing.Length == 0 || existing.EndsWith('\n')
            ? string.Empty
            : Environment.NewLine;
        var line = JsonSerializer.Serialize(auditEvent, AuditJsonOptions);
        AtomicFileWriter.Write(_auditPath, existing + separator + line + Environment.NewLine);
    }

    private void RecoverPendingMutation()
    {
        if (!File.Exists(_mutationIntentPath))
        {
            return;
        }

        UserAccountMutationIntent intent;
        try
        {
            intent = JsonSerializer.Deserialize<UserAccountMutationIntent>(
                File.ReadAllText(_mutationIntentPath),
                JsonOptions) ?? throw new InvalidDataException("The user-account mutation intent is empty.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"The user-account mutation intent at '{_mutationIntentPath}' is corrupt; identity mutations are blocked pending recovery.",
                ex);
        }

        ValidateMutationIntent(intent);
        AppendAuditIfMissing(intent.AuditEvent);
        Save(intent.Snapshot);
        DeleteMutationIntent();
        _logger?.LogWarning(
            "Recovered committed user-account mutation {AuditId} from {Path}.",
            intent.AuditEvent.AuditId,
            _mutationIntentPath);
    }

    private static void ValidateMutationIntent(UserAccountMutationIntent intent)
    {
        if (intent.Version != MutationIntentVersion ||
            string.IsNullOrWhiteSpace(intent.MutationId) ||
            !string.Equals(intent.MutationId, intent.AuditEvent.AuditId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The user-account mutation intent has an invalid version or audit identity.");
        }

        var target = intent.Snapshot.Accounts.SingleOrDefault(account =>
            AccountKey(account.Username) == AccountKey(intent.AuditEvent.Username));
        if (target is null || !string.Equals(target.LastAuditId, intent.AuditEvent.AuditId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The user-account mutation intent does not bind its target account snapshot to the audit event.");
        }
    }

    private static bool ContainsAuditId(IEnumerable<string> lines, string auditId)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            UserAccountAuditEventDto? existing;
            try
            {
                existing = JsonSerializer.Deserialize<UserAccountAuditEventDto>(line, AuditJsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw new InvalidDataException(
                    "The user-account audit stream is corrupt; mutation recovery cannot prove idempotency.",
                    ex);
            }

            if (existing is null || string.IsNullOrWhiteSpace(existing.AuditId))
            {
                throw new InvalidDataException(
                    "The user-account audit stream contains an entry without an audit identity.");
            }

            if (string.Equals(existing.AuditId, auditId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task DeleteMutationIntentAsync()
    {
        File.Delete(_mutationIntentPath);
        var directory = Path.GetDirectoryName(_mutationIntentPath);
        if (!string.IsNullOrEmpty(directory))
        {
            await AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void DeleteMutationIntent()
    {
        File.Delete(_mutationIntentPath);
        var directory = Path.GetDirectoryName(_mutationIntentPath);
        if (!string.IsNullOrEmpty(directory))
        {
            AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static UserAccountSnapshot OrderSnapshot(UserAccountSnapshot snapshot)
        => snapshot with
        {
            Accounts = snapshot.Accounts
                .OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static ValidatedUserAccountRequest ValidateAccountRequest(
        string username,
        string role,
        string? roleProfileName,
        IReadOnlyList<string>? permissionNames,
        string? companyId,
        string requestedBy,
        string rationale,
        string actor)
    {
        var normalizedUsername = IdentityGovernanceNormalization.NormalizeRequired(username, nameof(username));
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
            string.IsNullOrWhiteSpace(companyId) ? null : companyId.Trim(),
            resolvedPermissionNames,
            permissions,
            IdentityGovernanceNormalization.ResolveActor(actor, requestedBy),
            IdentityGovernanceNormalization.NormalizeRequired(rationale, nameof(rationale)));
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
            CompanyId: account.CompanyId,
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
            revokedSessionCount,
            account.CompanyId);

    private static UserRole ParseRole(string role)
        => Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed) ? parsed : UserRole.ReadOnly;

    private static string AccountKey(string username)
        => username.Trim().ToLowerInvariant();

    private sealed record UserAccountSnapshot(IReadOnlyList<PersistedUserAccount> Accounts);

    private sealed record UserAccountMutationIntent(
        int Version,
        string MutationId,
        DateTimeOffset CreatedAtUtc,
        UserAccountSnapshot Snapshot,
        UserAccountAuditEventDto AuditEvent);

    private sealed record PersistedUserAccount(
        string Username,
        string PasswordHash,
        string Role,
        string? RoleProfileName,
        string? CompanyId,
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
        string? CompanyId,
        IReadOnlyList<string> PermissionNames,
        UserPermission Permissions,
        string Actor,
        string Rationale);
}
