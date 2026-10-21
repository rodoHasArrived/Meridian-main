using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Integrity;
using Meridian.Core.Logging;
using Meridian.Storage.Archival;
using Serilog;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.DataIntegration.Credentials;

public sealed class FileProviderCredentialStore : IScopedProviderCredentialStore, ILegacyProviderCredentialImporter, IScopedOAuthTokenVault
{
    private static readonly ILogger Log = LoggingSetup.ForContext<FileProviderCredentialStore>();

    private const int VaultVersion = 1;

    // Vaults holding tenant/connection-scoped state use a protection tag that binaries predating
    // scoped ownership reject as unreadable. Those binaries ignore the envelope version and would
    // otherwise load the vault, drop every Scope and scoped OAuth record they do not model, and
    // rewrite it. An unreadable primary and backup make them fail closed instead, so a rollback
    // cannot silently take scoped credentials offline. Vaults without scoped state keep the
    // version 1 format and stay readable by older binaries.
    private const int ScopedVaultVersion = 2;
    private const string ScopedProtectionSuffix = "+scoped-v2";
    private const string VaultFileName = "provider-credentials.vault";
    private const string VaultBackupFileName = "provider-credentials.vault.bak";
    private const string KeyFileName = "provider-credentials.key";
    private const string AuditFileName = "provider-credentials.audit.jsonl";
    // Write-ahead journal of audit records whose vault mutation committed before the audit append
    // was proven durable (PRD-002: credential/audit commit ambiguity). Never holds secrets.
    private const string PendingAuditFileName = "provider-credentials.audit.pending.jsonl";
    private const string EnvironmentFallbackOverride = "MDC_PROVIDER_ALLOW_ENV_FALLBACK";
    private const string PackagedBuildEnvVar = "MDC_PACKAGED_BUILD";
    private const string CustomerBuildEnvVar = "MERIDIAN_CUSTOMER_BUILD";
    private const int DefaultRotationWindowDays = 90;
    private static readonly byte[] Entropy = "Meridian.ProviderCredentialStore.v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directoryPath;
    private readonly string _vaultBackupPath;
    private readonly string _keyPath;
    private readonly string _auditPath;
    private readonly string _pendingAuditPath;
    private readonly Func<string, string?>? _configurationValue;

    /// <param name="dataRoot">Root of this store's encrypted vault and audit files.</param>
    /// <param name="configurationValue">
    /// Optional host-owned credential settings. When supplied, missing values remain missing;
    /// process and user environment variables are consulted only when this accessor is omitted.
    /// </param>
    public FileProviderCredentialStore(string dataRoot, Func<string, string?>? configurationValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        _configurationValue = configurationValue;
        _directoryPath = Path.Combine(Path.GetFullPath(dataRoot), ".mdc");
        VaultPath = Path.Combine(_directoryPath, VaultFileName);
        _vaultBackupPath = Path.Combine(_directoryPath, VaultBackupFileName);
        _keyPath = Path.Combine(_directoryPath, KeyFileName);
        _auditPath = Path.Combine(_directoryPath, AuditFileName);
        _pendingAuditPath = Path.Combine(_directoryPath, PendingAuditFileName);
    }

    public string VaultPath { get; }

    public async Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);
        var readResult = await ReadForProviderAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
        return WithPendingAuditMetadata(BuildStatus(descriptor, readResult));
    }

    public Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default)
        => ReadInternalAsync(providerId, null, ct);

    public Task<ProviderCredentialReadResult?> ReadScopedAsync(string providerId, ProviderCredentialScope scope, CancellationToken ct = default)
        => ReadInternalAsync(providerId, scope ?? throw new ArgumentNullException(nameof(scope)), ct);

    public async Task<ProviderCredentialStoreStatus> GetScopedStatusAsync(string providerId, ProviderCredentialScope scope, CancellationToken ct = default)
        => WithPendingAuditMetadata(BuildStatus(RequireDescriptor(providerId), await ReadScopedAsync(providerId, scope, ct).ConfigureAwait(false)));

    /// <summary>
    /// Adds <c>auditPending</c> (the number of journaled audit records not yet appended to the audit
    /// log) so the workstation can surface a committed-but-unaudited mutation instead of hiding it.
    /// </summary>
    private ProviderCredentialStoreStatus WithPendingAuditMetadata(ProviderCredentialStoreStatus status)
    {
        var pending = CountPendingAuditRecords();
        if (pending == 0)
            return status;
        var metadata = new Dictionary<string, string>(status.AuditMetadata, StringComparer.Ordinal)
        {
            ["auditPending"] = pending.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return status with { AuditMetadata = metadata };
    }

    private async Task<ProviderCredentialReadResult?> ReadInternalAsync(string providerId, ProviderCredentialScope? scope, CancellationToken ct)
    {
        var descriptor = RequireDescriptor(providerId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Both generations are published by atomic replacement. Read the published
            // snapshot without creating or opening a writable lock on a secret volume.
            if (File.Exists(VaultPath) || File.Exists(_vaultBackupPath))
            {
                var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
                if (vault.Providers.TryGetValue(scope?.StorageKey(descriptor.ProviderId) ?? descriptor.ProviderId, out var localRecord))
                {
                    ValidateRecordScope(localRecord, scope);
                    return ToReadResult(descriptor, localRecord, ProviderCredentialSourceDto.LocalEncryptedStore);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        if (scope is not null)
            return null;
        var fallbackRecord = ReadEnvironmentFallback(descriptor);
        return fallbackRecord is null
            ? null
            : ToReadResult(descriptor, fallbackRecord, ProviderCredentialSourceDto.Environment);
    }

    public Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
        => SaveCredentialsAsync(request, false, null, ct);

    public Task SaveScopedAsync(ProviderCredentialSaveRequest request, ProviderCredentialScope scope, CancellationToken ct = default)
        => SaveCredentialsAsync(request, false, scope ?? throw new ArgumentNullException(nameof(scope)), ct);

    public Task SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
        => SaveCredentialsAsync(request, true, null, ct);

    public Task<string> SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request,
        ProviderCredentialReadResult expectedConnection, CancellationToken ct = default)
        => SaveCredentialsAsync(request, true, null, ct, expectedConnection);

    private async Task<string> SaveCredentialsAsync(ProviderCredentialSaveRequest request, bool retainCurrentGenerationAsBackup,
        ProviderCredentialScope? scope, CancellationToken ct, ProviderCredentialReadResult? expectedConnection = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var descriptor = RequireDescriptor(request.ProviderId);
        if (scope is not null)
        {
            if (!string.Equals(descriptor.NormalizeEnvironment(scope.Environment), scope.Environment, StringComparison.Ordinal)
                || (request.Environment is not null && !string.Equals(descriptor.NormalizeEnvironment(request.Environment), scope.Environment, StringComparison.Ordinal)))
                throw new InvalidDataException("Credential environment does not match its ownership scope.");
            request = request with { Environment = scope.Environment };
        }
        var storageKey = scope?.StorageKey(descriptor.ProviderId) ?? descriptor.ProviderId;
        var normalizedCredentials = NormalizeCredentialFields(descriptor, request.Credentials ?? new Dictionary<string, string?>());
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            vault.Providers.TryGetValue(storageKey, out var existing);
            if (existing is not null)
                ValidateRecordScope(existing, scope);

            if (expectedConnection is not null)
            {
                var currentRecord = existing ?? (scope is null ? ReadEnvironmentFallback(descriptor) : null);
                var current = currentRecord is null ? null : ToReadResult(descriptor, currentRecord,
                    existing is null ? ProviderCredentialSourceDto.Environment : ProviderCredentialSourceDto.LocalEncryptedStore);
                if (current is null || current.Source != expectedConnection.Source ||
                    current.CredentialGeneration != expectedConnection.CredentialGeneration ||
                    !string.Equals(current.Environment, expectedConnection.Environment, StringComparison.Ordinal) ||
                    current.Credentials.Count != expectedConnection.Credentials.Count ||
                    current.Credentials.Any(pair => expectedConnection.Get(pair.Key) != pair.Value))
                    throw new ProviderCredentialConflictException();
            }

            var updated = CreateUpdatedRecord(descriptor, request, normalizedCredentials, existing, now);
            updated.Scope = scope;
            if (scope is not null)
                updated.ExternalAccountId = scope.ExternalAccountId;

            vault.Providers[storageKey] = updated;
            var auditLine = BuildAuditLine(
                descriptor,
                "save",
                request.Actor,
                BuildStatus(descriptor, ToReadResult(descriptor, updated, ProviderCredentialSourceDto.LocalEncryptedStore)),
                updated.Fields.Keys.OrderBy(static field => field, StringComparer.OrdinalIgnoreCase).ToArray(),
                scope);
            await CommitWithAuditAsync(
                vault,
                auditLine,
                () => WriteVaultAsync(vault, ct, retainCurrentGenerationAsBackup: retainCurrentGenerationAsBackup),
                descriptor.ProviderId, "save",
                new AuditCommitCheck("provider-save", storageKey, updated.Metadata["credentialGeneration"], null),
                ct).ConfigureAwait(false);
            return updated.Metadata["credentialGeneration"];
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ProviderCredentialVaultRecord CreateUpdatedRecord(
        ProviderCredentialCatalogEntry descriptor,
        ProviderCredentialSaveRequest request,
        IReadOnlyDictionary<string, string?> normalizedCredentials,
        ProviderCredentialVaultRecord? existing,
        DateTimeOffset now)
    {
        var fields = new Dictionary<string, string>(existing?.Fields ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in normalizedCredentials)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                fields.Remove(key.Trim());
                continue;
            }

            fields[key.Trim()] = value.Trim();
        }

        var environment = descriptor.NormalizeEnvironment(request.Environment ?? existing?.Environment);
        var metadata = new Dictionary<string, string>(existing?.Metadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        if (request.Metadata is not null)
        {
            foreach (var (key, value) in request.Metadata)
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    metadata[key.Trim()] = value.Trim();
                }
            }
        }
        metadata["lastRotatedAt"] = now.ToString("O");
        metadata["rotationDueAt"] = now.AddDays(DefaultRotationWindowDays).ToString("O");
        metadata["verificationRequired"] = "true";
        metadata["credentialGeneration"] = Guid.NewGuid().ToString("N");
        metadata["credentialStore"] = "local-encrypted-vault";

        return new ProviderCredentialVaultRecord
        {
            ProviderId = descriptor.ProviderId,
            Fields = fields,
            Environment = string.IsNullOrWhiteSpace(environment) ? null : environment,
            SavedAt = existing?.SavedAt ?? now,
            UpdatedAt = now,
            LastVerifiedAt = null,
            LastSuccessfulAt = existing?.LastSuccessfulAt,
            LastFailureAt = existing?.LastFailureAt,
            LastError = null,
            ExternalAccountId = null,
            Metadata = metadata
        };
    }

    /// <summary>Imports one validated legacy snapshot without replacing retained vault records.</summary>
    public async Task ImportLegacyAsync(IReadOnlyList<ProviderCredentialSaveRequest> requests, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        // Validate the entire input before any durable mutation, including providers already present.
        var prepared = requests.Select(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var descriptor = RequireDescriptor(request.ProviderId);
            return (Descriptor: descriptor, Request: request,
                Fields: NormalizeCredentialFields(descriptor, request.Credentials));
        }).GroupBy(item => item.Descriptor.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                string? environment = null;
                foreach (var item in group)
                {
                    var candidateEnvironment = NormalizeOptional(item.Request.Environment);
                    if (candidateEnvironment is not null)
                    {
                        candidateEnvironment = item.Descriptor.NormalizeEnvironment(candidateEnvironment);
                        if (environment is not null && !environment.Equals(candidateEnvironment, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Legacy provider aliases contain conflicting environments.");
                        environment = candidateEnvironment;
                    }
                    foreach (var (key, value) in item.Fields)
                    {
                        var normalized = NormalizeOptional(value);
                        if (normalized is null)
                            continue;
                        if (fields.TryGetValue(key, out var existing) && !string.Equals(existing, normalized, StringComparison.Ordinal))
                            throw new InvalidOperationException("Legacy provider aliases contain conflicting credential fields.");
                        fields[key] = normalized;
                    }
                }
                return (first.Descriptor, Request: first.Request with { Environment = environment }, Fields: fields);
            }).ToArray();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            foreach (var item in prepared)
            {
                // The marker survives deletion and is committed with the first import. Audit
                // retries must never interpret an operator deletion as a missing legacy import.
                if (!vault.LegacyImportedProviderIds.Add(item.Descriptor.ProviderId))
                    continue;
                if (vault.Providers.ContainsKey(item.Descriptor.ProviderId))
                    continue;
                var record = CreateUpdatedRecord(item.Descriptor, item.Request, item.Fields, null, DateTimeOffset.UtcNow);
                vault.Providers.Add(item.Descriptor.ProviderId, record);
            }

            // A successful import acknowledges that the plaintext source may be erased.
            // Refresh both encrypted generations even on retry after an audit failure.
            await WriteVaultAsync(vault, ct, retainCurrentGenerationAsBackup: true).ConfigureAwait(false);
            // Record every attempted provider, including retries after an audit-write failure.
            // The sidecar is retained until all these audit appends succeed.
            foreach (var item in prepared)
            {
                vault.Providers.TryGetValue(item.Descriptor.ProviderId, out var record);
                await AppendAuditAsync(item.Descriptor, "legacy-import-or-preserve", "credential-vault-migration",
                    BuildStatus(item.Descriptor, record is null ? null : ToReadResult(item.Descriptor, record, ProviderCredentialSourceDto.LocalEncryptedStore)),
                    record is null ? [] : record.Fields.Keys.OrderBy(static field => field, StringComparer.OrdinalIgnoreCase).ToArray(), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, OAuthToken>> ReadScopedOAuthTokensAsync(ProviderCredentialScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            var result = new Dictionary<string, OAuthToken>(StringComparer.Ordinal);
            foreach (var pair in vault.ScopedOAuthTokens)
            {
                var record = pair.Value;
                if (record.Scope is null || string.IsNullOrWhiteSpace(record.ProviderName) || record.Token is null ||
                    !string.Equals(pair.Key, record.Scope.StorageKey(record.ProviderName), StringComparison.Ordinal))
                    throw new InvalidDataException("OAuth credential ownership is invalid.");
                if (record.Scope == scope)
                    result.Add(record.ProviderName, record.Token);
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveScopedOAuthTokenAsync(string providerName, OAuthToken? token, ProviderCredentialScope scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(scope);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            var key = scope.StorageKey(providerName);
            if (vault.ScopedOAuthTokens.TryGetValue(key, out var existing) &&
                (existing.Scope != scope || existing.ProviderName != providerName))
                throw new InvalidDataException("OAuth credential ownership is invalid.");
            if (token is null)
                vault.ScopedOAuthTokens.Remove(key);
            else
                vault.ScopedOAuthTokens[key] = new ScopedOAuthVaultRecord { ProviderName = providerName, Scope = scope, Token = token };
            // A completed remote rotation must recover this owner's replacement token.
            var action = token is null ? "oauth-delete" : "oauth-save";
            var committed = await CommitWithAuditAsync(
                vault,
                BuildOAuthAuditLine(providerName, action, scope),
                () => WriteVaultAsync(vault, ct, discardPreviousGeneration: token is null, retainCurrentGenerationAsBackup: true),
                providerName, action,
                new AuditCommitCheck(token is null ? "scoped-oauth-delete" : "scoped-oauth-save", key, null, null, token is null ? null : ComputeTokenDigest(token)),
                ct).ConfigureAwait(false);
            ThrowIfAuditDeferred(committed, providerName, action);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyDictionary<string, OAuthToken>> ReadOAuthTokensAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            return new Dictionary<string, OAuthToken>(vault.OAuthTokens, StringComparer.Ordinal);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveOAuthTokenAsync(string providerName, OAuthToken? token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            if (token is null)
            {
                vault.OAuthTokens.Remove(providerName);
                vault.LegacyImportedOAuthProviders.Add(providerName);
            }
            else
                vault.OAuthTokens[providerName] = token;
            // Rotation can invalidate the previous refresh token at the provider immediately.
            // Acknowledging the save requires the replacement in both recovery generations.
            var action = token is null ? "oauth-delete" : "oauth-save";
            var committed = await CommitWithAuditAsync(
                vault,
                BuildOAuthAuditLine(providerName, action, scope: null),
                () => WriteVaultAsync(vault, ct, discardPreviousGeneration: token is null, retainCurrentGenerationAsBackup: true),
                providerName, action,
                new AuditCommitCheck(token is null ? "oauth-delete" : "oauth-save", providerName, null, null, token is null ? null : ComputeTokenDigest(token)),
                ct).ConfigureAwait(false);
            ThrowIfAuditDeferred(committed, providerName, action);
        }
        finally { _gate.Release(); }
    }

    public async Task ImportOAuthTokensAsync(IReadOnlyDictionary<string, OAuthToken> tokens, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        foreach (var pair in tokens)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
        }
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            foreach (var pair in tokens)
            {
                if (!vault.LegacyImportedOAuthProviders.Add(pair.Key))
                    continue;
                vault.OAuthTokens.TryAdd(pair.Key, pair.Value);
            }
            // A successful import acknowledges that the plaintext source may be erased.
            // Refresh both encrypted generations even on retry after an audit failure.
            await WriteVaultAsync(vault, ct, retainCurrentGenerationAsBackup: true).ConfigureAwait(false);
            foreach (var provider in tokens.Keys)
                await AppendOAuthAuditAsync(provider, "oauth-import-or-preserve", ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private Task AppendOAuthAuditAsync(string providerName, string action, CancellationToken ct, ProviderCredentialScope? scope = null)
        => AppendAuditLineAsync(BuildOAuthAuditLine(providerName, action, scope), ct);

    private static string BuildOAuthAuditLine(string providerName, string action, ProviderCredentialScope? scope)
        => JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.UtcNow, ProviderId = providerName, Action = action, Scope = scope }, JsonOptions);

    // ── Credential/audit commit (PRD-002) ───────────────────────────────────────────────────────
    //
    // A vault mutation and its audit record are two files. Writing the vault first and then
    // appending the audit line left one ambiguous outcome: an audit append that fails after the
    // vault committed surfaced as a failed save even though the secret was persisted. The audit
    // record is now journaled to a write-ahead sidecar BEFORE the vault write and removed only after
    // the audit append is durable. An orphaned journal is therefore an unambiguous marker: the
    // mutation committed and its audit is owed. The journal is replayed by the next mutation and by
    // ReplayPendingAuditAsync at host start. Audit records never contain secrets, so neither does
    // the journal. Legacy imports keep their own retry-by-sidecar contract and do not journal.

    /// <summary>
    /// Runs <paramref name="writeVault"/> with the audit record journaled ahead of it in two phases.
    /// The record is journaled as an <c>intent</c> before the vault write and marked <c>committed</c>
    /// after it, so replay never reports a mutation whose vault write did not complete: an intent that
    /// is still pending after a crash is verified against the vault (<paramref name="check"/>) and is
    /// appended only when the vault proves the mutation landed. Returns <c>true</c> when the audit line
    /// was appended durably, <c>false</c> when the vault committed but the append failed and the
    /// committed record stays journaled for replay. A journal write failure throws before the vault is
    /// touched; a vault write failure removes the intent again. Callers hold <c>_gate</c> and the vault lock.
    /// </summary>
    private async Task<bool> CommitWithAuditAsync(
        ProviderCredentialVault vault, string auditLine, Func<Task> writeVault, string providerId, string action, AuditCommitCheck check, CancellationToken ct)
    {
        await ReplayPendingAuditCoreAsync(ct).ConfigureAwait(false);
        var journal = await ReadPendingAuditEntriesAsync(ct).ConfigureAwait(false);
        // A stable operation id rides on the audit record itself, so replay can tell an already
        // appended record (crash after the append, before the journal line was removed) from an owed one.
        // The same id is written into the vault as its publication marker, so an intent is proven only
        // by the publication it caused, never by state that happened to exist before it.
        var operationId = Guid.NewGuid().ToString("N");
        auditLine = StampOperationId(auditLine, operationId);
        // Receipts for earlier publications whose audit is still owed ride forward on this
        // publication, so a later mutation never replaces the only proof an earlier intent needs.
        // Only receipts the current vault already holds are carried: an intent whose own write never
        // landed gains nothing from a later publication.
        var receipts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outstanding in journal)
        {
            if (!outstanding.Malformed && outstanding.Check.PublicationId is { } retained && vault.HoldsPublication(retained))
                receipts.Add(retained);
        }
        receipts.Add(operationId);
        vault.PublicationReceipts = receipts;
        vault.LastPublicationId = operationId;
        check = check with { PublicationId = operationId };
        var entry = PendingAuditEntry.Intent(providerId, action, check, auditLine);
        journal.Add(entry);
        await WritePendingAuditAsync(journal, ct).ConfigureAwait(false);

        try
        {
            await writeVault().ConfigureAwait(false);
        }
        catch
        {
            // WriteVaultAsync publishes the primary and then the recovery generation; a failure after
            // the primary is live (backup write) means the mutation committed and its audit is owed.
            // Only an intent the vault does not prove is removed; an unverifiable one stays an intent
            // for replay to verify. Nothing here may mask the original failure.
            try
            {
                var published = await LoadVaultAsync(CancellationToken.None).ConfigureAwait(false);
                if (check.IsSatisfiedBy(published))
                    journal[journal.IndexOf(entry)] = entry.AsCommitted();
                else
                    journal.Remove(entry);
                await WritePendingAuditAsync(journal, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception journalEx) when (journalEx is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or CryptographicException)
            {
                Log.Warning(journalEx, "Could not reconcile the credential audit journal after a failed vault write for {ProviderId}/{Action}; the intent stays journaled for verified replay", providerId, action);
            }
            throw;
        }

        // From here the vault mutation is durable. A journal or audit-log failure is a deferred-audit
        // outcome, never an ordinary mutation failure: the caller must not be told a committed
        // mutation failed, and OAuth callers get the typed deferred contract through the return value.
        var committed = entry.AsCommitted();
        var marked = await TryWritePendingAuditAsync(journal, entry, committed, providerId, action).ConfigureAwait(false);

        try
        {
            await AppendAuditLineAsync(auditLine, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex,
                "Credential audit deferred for {ProviderId}/{Action}; the vault mutation committed and the audit record is journaled at {PendingAuditPath}",
                providerId, action, _pendingAuditPath);
            return false;
        }

        journal.Remove(marked ? committed : entry);
        try
        {
            await WritePendingAuditAsync(journal, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The record is durable in the audit log; replay deduplicates it by operationId.
            Log.Warning(ex, "Credential audit appended for {ProviderId}/{Action} but the journal could not be cleared at {PendingAuditPath}; replay deduplicates by operation id",
                providerId, action, _pendingAuditPath);
        }
        return true;
    }

    /// <summary>Marks the journaled intent committed; a journal write failure is logged, not thrown.</summary>
    private async Task<bool> TryWritePendingAuditAsync(List<PendingAuditEntry> journal, PendingAuditEntry intent, PendingAuditEntry committed, string providerId, string action)
    {
        try
        {
            journal[journal.IndexOf(intent)] = committed;
            await WritePendingAuditAsync(journal, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The on-disk journal still holds the intent; replay verifies it against the vault.
            journal[journal.IndexOf(committed)] = intent;
            Log.Warning(ex, "Could not mark the credential audit journal committed for {ProviderId}/{Action} at {PendingAuditPath}; the intent stays journaled for verified replay",
                providerId, action, _pendingAuditPath);
            return false;
        }
    }

    private void ThrowIfAuditDeferred(bool committed, string providerId, string action)
    {
        if (!committed)
            throw new ProviderCredentialAuditDeferredException(providerId, action, _pendingAuditPath);
    }

    /// <summary>
    /// Appends every journaled audit record that a committed mutation still owes to the audit log,
    /// then removes the journal. Intents whose vault write cannot be proven are discarded with a
    /// warning. Idempotent and safe at host start; the next mutation also replays. Returns the number
    /// of records appended.
    /// </summary>
    public async Task<int> ReplayPendingAuditAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_pendingAuditPath))
            return 0;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            return await ReplayPendingAuditCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> ReplayPendingAuditCoreAsync(CancellationToken ct)
    {
        var journal = await ReadPendingAuditEntriesAsync(ct).ConfigureAwait(false);
        if (journal.Count == 0)
            return 0;

        var replayedAt = DateTimeOffset.UtcNow;
        var replayed = 0;
        var changed = false;
        ProviderCredentialVault? vault = null;
        HashSet<string>? appendedOperationIds = null;
        try
        {
            while (journal.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var entry = journal[0];
                if (entry.Malformed)
                {
                    // Schema-invalid journal lines are retained for review beside the journal and never
                    // appended to the audit log; a damaged journal must not refuse startup.
                    await QuarantinePendingAuditLineAsync(entry.Record).ConfigureAwait(false);
                    Log.Warning("Quarantined a malformed credential audit journal line at {QuarantinePath}", _pendingAuditPath + ".rejected");
                    journal.RemoveAt(0);
                    changed = true;
                    continue;
                }

                if (entry.OperationId is not null)
                {
                    appendedOperationIds ??= await ReadAppendedOperationIdsAsync(ct).ConfigureAwait(false);
                    if (appendedOperationIds.Contains(entry.OperationId))
                    {
                        // Already durable in the audit log: the crash landed between the append and the
                        // journal cleanup. Replay is idempotent, so drop the journal line without appending.
                        journal.RemoveAt(0);
                        changed = true;
                        continue;
                    }
                }

                if (!entry.Committed)
                {
                    vault ??= await LoadVaultAsync(ct).ConfigureAwait(false);
                    if (!entry.Check.IsSatisfiedBy(vault))
                    {
                        Log.Warning(
                            "Discarded a credential audit intent for {ProviderId}/{Action} whose vault mutation never committed",
                            entry.ProviderId, entry.Action);
                        journal.RemoveAt(0);
                        changed = true;
                        continue;
                    }
                }

                await AppendAuditLineAsync(MarkReplayed(entry.Record, replayedAt), CancellationToken.None).ConfigureAwait(false);
                if (entry.OperationId is not null)
                    appendedOperationIds?.Add(entry.OperationId);
                journal.RemoveAt(0);
                changed = true;
                replayed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Credential audit replay stopped with {PendingAuditCount} record(s) still journaled at {PendingAuditPath}",
                journal.Count, _pendingAuditPath);
        }
        finally
        {
            if (changed)
                await WritePendingAuditAsync(journal, CancellationToken.None).ConfigureAwait(false);
        }

        if (replayed > 0)
            Log.Information("Replayed {ReplayedAuditCount} journaled credential audit record(s)", replayed);
        return replayed;
    }

    private static string StampOperationId(string line, string operationId)
    {
        if (JsonNode.Parse(line) is JsonObject record)
        {
            record["operationId"] = operationId;
            return record.ToJsonString(JsonOptions);
        }
        return line;
    }

    /// <summary>Operation ids already present in the audit log; read only while a journal is non-empty.</summary>
    private async Task<HashSet<string>> ReadAppendedOperationIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(_auditPath))
            return ids;
        foreach (var line in await File.ReadAllLinesAsync(_auditPath, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                if (JsonNode.Parse(line) is JsonObject record && record["operationId"] is JsonValue value &&
                    value.TryGetValue<string>(out var id) && !string.IsNullOrEmpty(id))
                    ids.Add(id);
            }
            catch (JsonException)
            {
                // An unparseable audit line carries no operation id; it cannot match a journal entry.
            }
        }
        return ids;
    }

    private async Task QuarantinePendingAuditLineAsync(string line)
    {
        var quarantinePath = _pendingAuditPath + ".rejected";
        await using var stream = new FileStream(quarantinePath, FileMode.Append, FileAccess.Write, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), CancellationToken.None).ConfigureAwait(false);
        await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static string MarkReplayed(string line, DateTimeOffset replayedAt)
    {
        try
        {
            if (JsonNode.Parse(line) is JsonObject record)
            {
                record["replayedAt"] = replayedAt;
                return record.ToJsonString(JsonOptions);
            }
        }
        catch (JsonException)
        {
            // A damaged journal line is still worth appending verbatim rather than losing.
        }
        return line;
    }

    private int CountPendingAuditRecords()
    {
        try
        {
            return File.Exists(_pendingAuditPath)
                ? File.ReadLines(_pendingAuditPath).Count(static line => !string.IsNullOrWhiteSpace(line))
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private async Task<List<PendingAuditEntry>> ReadPendingAuditEntriesAsync(CancellationToken ct)
    {
        if (!File.Exists(_pendingAuditPath))
            return [];
        var lines = await File.ReadAllLinesAsync(_pendingAuditPath, ct).ConfigureAwait(false);
        return lines.Where(static line => !string.IsNullOrWhiteSpace(line)).Select(PendingAuditEntry.Parse).ToList();
    }

    private async Task WritePendingAuditAsync(List<PendingAuditEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            if (File.Exists(_pendingAuditPath))
            {
                File.Delete(_pendingAuditPath);
                await AtomicFileWriter.SyncDirectoryAsync(_directoryPath, CancellationToken.None).ConfigureAwait(false);
            }
            return;
        }

        var created = !File.Exists(_pendingAuditPath);
        var content = string.Join("\n", entries.Select(static entry => entry.Serialize())) + "\n";
        await AtomicFileWriter.WriteAsync(_pendingAuditPath, content, ct).ConfigureAwait(false);
        if (created)
            await AtomicFileWriter.SyncDirectoryAsync(_directoryPath, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// What the vault must contain for a journaled intent to count as a committed mutation. Replay
    /// evaluates it only for intents left behind by a crash between the journal write and the commit
    /// marker; a committed entry needs no proof.
    /// </summary>
    private sealed record AuditCommitCheck(
        string Kind, string Key, string? CredentialGeneration, DateTimeOffset? VerifiedAt, string? TokenDigest = null, string? PublicationId = null)
    {
        /// <summary>
        /// An intent is proven only by the publication it caused: the vault must hold this intent's
        /// publication receipt. For the latest publication the kind-specific state must also match;
        /// a receipt retained across later publications proves on its own, because a later
        /// publication only carries forward receipts the vault already held. A pre-existing state (an
        /// already-absent key, an older token) never satisfies an intent whose write did not land.
        /// </summary>
        public bool IsSatisfiedBy(ProviderCredentialVault vault)
            => PublicationId is not null
               && vault.HoldsPublication(PublicationId)
               && (!string.Equals(vault.LastPublicationId, PublicationId, StringComparison.Ordinal) || IsStateConsistentWith(vault));

        private bool IsStateConsistentWith(ProviderCredentialVault vault) => Kind switch
        {
            "provider-save" => vault.Providers.TryGetValue(Key, out var saved)
                && string.Equals(saved.Metadata.GetValueOrDefault("credentialGeneration"), CredentialGeneration, StringComparison.Ordinal),
            "provider-delete" => !vault.Providers.ContainsKey(Key),
            "provider-verify" => vault.Providers.TryGetValue(Key, out var verified) && verified.LastVerifiedAt == VerifiedAt,
            // A save is proven only by the replacement token itself, not by any token being present:
            // a pre-existing token must not satisfy the intent of a rotation that never landed.
            "oauth-save" => vault.OAuthTokens.TryGetValue(Key, out var token) && DigestMatches(token),
            "oauth-delete" => !vault.OAuthTokens.ContainsKey(Key),
            "scoped-oauth-save" => vault.ScopedOAuthTokens.TryGetValue(Key, out var scoped) && scoped.Token is not null && DigestMatches(scoped.Token),
            "scoped-oauth-delete" => !vault.ScopedOAuthTokens.ContainsKey(Key),
            _ => false
        };

        private bool DigestMatches(OAuthToken token)
            => TokenDigest is not null && string.Equals(ComputeTokenDigest(token), TokenDigest, StringComparison.Ordinal);
    }

    /// <summary>
    /// A non-secret fingerprint of a token: SHA-256 over its access token, refresh token and expiry.
    /// Journaled with an OAuth save intent so replay can prove that this replacement, not an earlier
    /// token under the same key, is what the vault holds. The journal never stores token material.
    /// </summary>
    internal static string ComputeTokenDigest(OAuthToken token)
    {
        var material = string.Join("\n", token.AccessToken, token.RefreshToken ?? string.Empty, token.ExpiresAt.ToString("O"));
        return Sha256Digest.ComputeUtf8(material);
    }

    /// <summary>One journal line: the audit record plus its commit phase and the proof replay needs.</summary>
    private sealed class PendingAuditEntry
    {
        private PendingAuditEntry(string providerId, string action, bool committed, AuditCommitCheck check, string record, bool malformed = false)
        {
            ProviderId = providerId;
            Action = action;
            Committed = committed;
            Check = check;
            Record = record;
            Malformed = malformed;
            OperationId = malformed ? null : ReadOperationId(record);
        }

        public string ProviderId { get; }
        public string Action { get; }
        public bool Committed { get; }
        public AuditCommitCheck Check { get; }
        /// <summary>The audit log line exactly as it will be appended (the raw journal line when malformed).</summary>
        public string Record { get; }
        /// <summary>A journal line that is not a structurally valid entry; quarantined by replay, never appended.</summary>
        public bool Malformed { get; }
        /// <summary>The record's stable operation id, when it carries one; replay deduplicates on it.</summary>
        public string? OperationId { get; }

        public static PendingAuditEntry Intent(string providerId, string action, AuditCommitCheck check, string record)
            => new(providerId, action, committed: false, check, record);

        public PendingAuditEntry AsCommitted() => new(ProviderId, Action, committed: true, Check, Record);

        private static string? ReadOperationId(string record)
        {
            try
            {
                return JsonNode.Parse(record) is JsonObject node && node["operationId"] is JsonValue value &&
                       value.TryGetValue<string>(out var id) && !string.IsNullOrEmpty(id)
                    ? id
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public string Serialize()
        {
            var node = new JsonObject
            {
                ["phase"] = Committed ? "committed" : "intent",
                ["providerId"] = ProviderId,
                ["action"] = Action,
                ["check"] = JsonSerializer.SerializeToNode(Check, JsonOptions),
                ["record"] = JsonNode.Parse(Record)
            };
            return node.ToJsonString(JsonOptions);
        }

        /// <summary>
        /// Never throws: a line that is not a structurally valid journal entry comes back
        /// <see cref="Malformed"/> so replay can quarantine it instead of refusing startup.
        /// </summary>
        public static PendingAuditEntry Parse(string line)
        {
            try
            {
                if (JsonNode.Parse(line) is not JsonObject node)
                    return AsMalformed(line);

                if (node["phase"] is null && node["record"] is null && node["action"] is JsonValue)
                {
                    // A bare audit record predates the two-phase journal; nothing can disprove it, so
                    // it keeps the original contract and is appended as written.
                    return new PendingAuditEntry("unknown", "unknown", committed: true,
                        new AuditCommitCheck("legacy", string.Empty, null, null), line);
                }

                if (node["record"] is not JsonObject record ||
                    node["phase"] is not JsonValue phase || !phase.TryGetValue<string>(out var phaseText) ||
                    node["providerId"] is not JsonValue providerValue || !providerValue.TryGetValue<string>(out var providerId) ||
                    node["action"] is not JsonValue actionValue || !actionValue.TryGetValue<string>(out var action) ||
                    node["check"] is not JsonObject checkNode)
                    return AsMalformed(line);

                var check = checkNode.Deserialize<AuditCommitCheck>(JsonOptions);
                if (check is null || string.IsNullOrEmpty(check.Kind))
                    return AsMalformed(line);

                return new PendingAuditEntry(
                    providerId,
                    action,
                    committed: string.Equals(phaseText, "committed", StringComparison.Ordinal),
                    check,
                    record.ToJsonString(JsonOptions));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException)
            {
                return AsMalformed(line);
            }
        }

        private static PendingAuditEntry AsMalformed(string line)
            => new("unknown", "unknown", committed: false, new AuditCommitCheck("malformed", string.Empty, null, null), line, malformed: true);
    }

    private async Task AppendAuditLineAsync(string line, CancellationToken ct)
    {
        // Every caller holds the vault writer lock. A newline commits a complete record;
        // interrupted suffixes are retained separately before either audit surface appends.
        var created = !File.Exists(_auditPath);
        await using var stream = new FileStream(_auditPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await RecoverAuditTailAsync(stream, ct).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.End);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
        if (created)
            await AtomicFileWriter.SyncDirectoryAsync(_directoryPath, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RecoverAuditTailAsync(FileStream stream, CancellationToken ct)
    {
        if (stream.Length == 0)
            return;
        stream.Seek(-1, SeekOrigin.End);
        if (stream.ReadByte() == '\n')
            return;

        var prefixLength = stream.Length;
        var buffer = new byte[4096];
        while (prefixLength > 0)
        {
            var count = (int)Math.Min(prefixLength, buffer.Length);
            var start = prefixLength - count;
            stream.Position = start;
            await stream.ReadExactlyAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            var newline = Array.LastIndexOf(buffer, (byte)'\n', count - 1, count);
            if (newline >= 0)
            {
                prefixLength = start + newline + 1;
                break;
            }
            prefixLength = start;
        }

        // Stream only the incomplete final record. Normal appends inspect one byte and
        // never copy the accumulated audit history. Preserve interrupted bytes for review.
        stream.Position = prefixLength;
        var interruptedPath = _auditPath + ".partial-" + Guid.NewGuid().ToString("N");
        await AtomicFileWriter.WriteStreamAsync(interruptedPath,
            destination => stream.CopyToAsync(destination, ct), ct).ConfigureAwait(false);
        stream.SetLength(prefixLength);
        stream.Flush(flushToDisk: true);
        Log.Warning("Retained an interrupted credential audit suffix at {AuditRecoveryPath}", interruptedPath);
    }

    private async Task<FileStream> AcquireVaultLockAsync(CancellationToken ct)
    {
        EnsureVaultDirectory();
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Keep this file in place: unlinking a lock file permits a second lock identity.
                return new FileStream(VaultPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33 && started.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    public Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
        => DeleteInternalAsync(providerId, null, actor, ct);

    public Task DeleteScopedAsync(string providerId, ProviderCredentialScope scope, string? actor = null, CancellationToken ct = default)
        => DeleteInternalAsync(providerId, scope ?? throw new ArgumentNullException(nameof(scope)), actor, ct);

    private async Task DeleteInternalAsync(string providerId, ProviderCredentialScope? scope, string? actor, CancellationToken ct)
    {
        var descriptor = RequireDescriptor(providerId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            var storageKey = scope?.StorageKey(descriptor.ProviderId) ?? descriptor.ProviderId;
            if (vault.Providers.TryGetValue(storageKey, out var existing))
                ValidateRecordScope(existing, scope);
            vault.Providers.Remove(storageKey);
            if (scope is null)
                vault.LegacyImportedProviderIds.Add(descriptor.ProviderId);
            await CommitWithAuditAsync(
                vault,
                BuildAuditLine(descriptor, "delete", actor, BuildStatus(descriptor, null), [], scope),
                () => WriteVaultAsync(vault, ct, discardPreviousGeneration: true),
                descriptor.ProviderId, "delete",
                new AuditCommitCheck("provider-delete", storageKey, null, null),
                ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
        => RecordVerificationInternalAsync(update, null, ct);

    public Task RecordScopedVerificationAsync(ProviderCredentialVerificationUpdate update, ProviderCredentialScope scope, CancellationToken ct = default)
        => RecordVerificationInternalAsync(update, scope ?? throw new ArgumentNullException(nameof(scope)), ct);

    private async Task RecordVerificationInternalAsync(ProviderCredentialVerificationUpdate update, ProviderCredentialScope? scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        var descriptor = RequireDescriptor(update.ProviderId);
        if (scope is not null && update.ExternalAccountId is not null && !string.Equals(update.ExternalAccountId.Trim(), scope.ExternalAccountId, StringComparison.Ordinal))
            throw new InvalidDataException("Verified account does not match its credential ownership scope.");
        var storageKey = scope?.StorageKey(descriptor.ProviderId) ?? descriptor.ProviderId;
        var verifiedAt = update.VerifiedAt ?? DateTimeOffset.UtcNow;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            if (!vault.Providers.TryGetValue(storageKey, out var record))
            {
                if (update.ExpectedCredentialGeneration is not null)
                    throw new ProviderCredentialConflictException();
                return;
            }

            ValidateRecordScope(record, scope);
            if (update.ExpectedCredentialGeneration is not null &&
                record.Metadata.GetValueOrDefault("credentialGeneration", string.Empty) != update.ExpectedCredentialGeneration)
                throw new ProviderCredentialConflictException();

            record.LastVerifiedAt = verifiedAt;
            record.LastError = update.Success ? null : SanitizeError(update.ErrorMessage);
            record.ExternalAccountId = update.Success ? scope?.ExternalAccountId ?? NormalizeOptional(update.ExternalAccountId) : record.ExternalAccountId;
            if (update.Success)
            {
                record.LastSuccessfulAt = verifiedAt;
                record.Metadata["verificationRequired"] = "false";
                record.Metadata["lastVerifiedBy"] = string.IsNullOrWhiteSpace(update.Actor) ? "local-operator" : update.Actor.Trim();
            }
            else
            {
                record.LastFailureAt = verifiedAt;
                record.Metadata["verificationRequired"] = "true";
            }

            vault.Providers[storageKey] = record;
            var action = update.Success ? "verify-success" : "verify-failure";
            await CommitWithAuditAsync(
                vault,
                BuildAuditLine(
                    descriptor,
                    action,
                    update.Actor,
                    BuildStatus(descriptor, ToReadResult(descriptor, record, ProviderCredentialSourceDto.LocalEncryptedStore)),
                    [],
                    scope),
                () => WriteVaultAsync(vault, ct),
                descriptor.ProviderId, action,
                new AuditCommitCheck("provider-verify", storageKey, null, verifiedAt),
                ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateRecordScope(ProviderCredentialVaultRecord record, ProviderCredentialScope? scope)
    {
        if (record.Scope != scope || (scope is not null
            && (record.Environment != scope.Environment || record.ExternalAccountId != scope.ExternalAccountId)))
            throw new InvalidDataException("Stored credentials do not match the requested ownership scope.");
    }

    private static ProviderCredentialCatalogEntry RequireDescriptor(string providerId)
        => ProviderCredentialCatalog.Find(providerId)
           ?? throw new ArgumentException($"Provider '{providerId}' is not in the provider credential catalog.", nameof(providerId));

    private static IReadOnlyDictionary<string, string?> NormalizeCredentialFields(
        ProviderCredentialCatalogEntry descriptor,
        IReadOnlyDictionary<string, string?> credentials)
    {
        var allowedFields = descriptor.RequiredFields.ToDictionary(
            static field => field.Name,
            static field => field.Name,
            StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var unknownFields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in credentials)
        {
            var trimmedKey = key?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedKey))
            {
                continue;
            }

            if (!allowedFields.TryGetValue(trimmedKey, out var canonicalName))
            {
                if (TryNormalizeProviderManagedField(descriptor, trimmedKey, out canonicalName))
                {
                    normalized[canonicalName] = value;
                    continue;
                }

                unknownFields.Add(trimmedKey);
                continue;
            }

            normalized[canonicalName] = value;
        }

        if (unknownFields.Count > 0)
        {
            throw new ProviderCredentialValidationException(descriptor.ProviderId, unknownFields.ToArray());
        }

        return normalized;
    }

    private static bool TryNormalizeProviderManagedField(
        ProviderCredentialCatalogEntry descriptor,
        string fieldName,
        out string canonicalName)
    {
        canonicalName = fieldName;
        if (!descriptor.ProviderId.Equals("plaid", StringComparison.OrdinalIgnoreCase) ||
            !fieldName.StartsWith("AccessToken:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var itemId = fieldName["AccessToken:".Length..].Trim();
        if (itemId.Length == 0 ||
            itemId.Length > 128 ||
            itemId.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
        {
            return false;
        }

        canonicalName = $"AccessToken:{itemId}";
        return true;
    }

    private static ProviderCredentialStoreStatus BuildStatus(
        ProviderCredentialCatalogEntry descriptor,
        ProviderCredentialReadResult? readResult)
    {
        if (!descriptor.RequiresCredentials)
        {
            return new ProviderCredentialStoreStatus(
                descriptor.ProviderId,
                descriptor.DisplayName,
                ProviderCredentialStateDto.NotRequired,
                ProviderCredentialSourceDto.NotRequired,
                ProviderVerificationStateDto.NotRequired,
                SavedAt: null,
                UpdatedAt: null,
                LastVerifiedAt: null,
                LastSuccessfulAt: null,
                LastFailureAt: null,
                LastError: null,
                MaskedKeyPreview: null,
                Environment: descriptor.DefaultEnvironment,
                ExternalAccountId: null,
                MissingFields: [],
                PresentFields: [],
                AuditMetadata: new Dictionary<string, string>());
        }

        var missingFields = descriptor.RequiredFields
            .Where(field => field.Required && string.IsNullOrWhiteSpace(readResult?.Get(field.Name)))
            .Select(field => field.Name)
            .ToArray();
        var presentFields = descriptor.RequiredFields
            .Where(field => !string.IsNullOrWhiteSpace(readResult?.Get(field.Name)))
            .Select(field => field.Name)
            .ToArray();

        var credentialSource = readResult?.Source ?? ProviderCredentialSourceDto.None;
        var hasError = !string.IsNullOrWhiteSpace(readResult?.LastError);
        var verificationRequired = readResult?.AuditMetadata.TryGetValue("verificationRequired", out var required) == true &&
            string.Equals(required, "true", StringComparison.OrdinalIgnoreCase);
        var credentialState = missingFields.Length == descriptor.RequiredFields.Count
            ? ProviderCredentialStateDto.Missing
            : missingFields.Length > 0
                ? ProviderCredentialStateDto.Partial
                : hasError
                    ? ProviderCredentialStateDto.Invalid
                    : !verificationRequired && readResult?.LastSuccessfulAt is not null
                        ? ProviderCredentialStateDto.Verified
                        : ProviderCredentialStateDto.Configured;

        var verificationState = credentialState switch
        {
            ProviderCredentialStateDto.Verified => ProviderVerificationStateDto.Verified,
            ProviderCredentialStateDto.Invalid => ProviderVerificationStateDto.Failed,
            ProviderCredentialStateDto.Missing or ProviderCredentialStateDto.Partial => ProviderVerificationStateDto.NotVerified,
            _ => readResult?.LastVerifiedAt is not null
                ? ProviderVerificationStateDto.Stale
                : ProviderVerificationStateDto.NotVerified
        };

        return new ProviderCredentialStoreStatus(
            descriptor.ProviderId,
            descriptor.DisplayName,
            credentialState,
            credentialSource,
            verificationState,
            SavedAt: readResult?.SavedAt,
            UpdatedAt: readResult?.AuditMetadata.TryGetValue("updatedAt", out var updatedAt) == true &&
                     DateTimeOffset.TryParse(updatedAt, out var parsedUpdatedAt)
                ? parsedUpdatedAt
                : null,
            LastVerifiedAt: readResult?.LastVerifiedAt,
            LastSuccessfulAt: readResult?.LastSuccessfulAt,
            LastFailureAt: readResult?.LastFailureAt,
            LastError: readResult?.LastError,
            MaskedKeyPreview: MaskCredentialPreview(readResult),
            Environment: string.IsNullOrWhiteSpace(readResult?.Environment)
                ? descriptor.DefaultEnvironment
                : readResult.Environment,
            ExternalAccountId: readResult?.ExternalAccountId,
            MissingFields: missingFields,
            PresentFields: presentFields,
            AuditMetadata: readResult?.AuditMetadata ?? new Dictionary<string, string>());
    }

    private static ProviderCredentialReadResult ToReadResult(
        ProviderCredentialCatalogEntry descriptor,
        ProviderCredentialVaultRecord record,
        ProviderCredentialSourceDto source)
    {
        var metadata = new Dictionary<string, string>(record.Metadata, StringComparer.OrdinalIgnoreCase);
        metadata["updatedAt"] = record.UpdatedAt.ToString("O");

        return new ProviderCredentialReadResult(
            descriptor.ProviderId,
            source,
            new Dictionary<string, string>(record.Fields, StringComparer.OrdinalIgnoreCase),
            string.IsNullOrWhiteSpace(record.Environment) ? descriptor.DefaultEnvironment : record.Environment,
            record.ExternalAccountId,
            record.SavedAt,
            record.LastVerifiedAt,
            record.LastSuccessfulAt,
            record.LastFailureAt,
            record.LastError,
            metadata);
    }

    private static string? MaskCredentialPreview(ProviderCredentialReadResult? readResult)
    {
        if (readResult is null)
        {
            return null;
        }

        var preferred = readResult.Credentials.TryGetValue("KeyId", out var keyId)
            ? keyId
            : readResult.Credentials.TryGetValue("ApiKey", out var apiKey)
                ? apiKey
                : readResult.Credentials.Values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        return MaskValue(preferred);
    }

    private static string? MaskValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 4)
        {
            return new string('*', trimmed.Length);
        }

        return string.Concat(new string('*', Math.Min(12, trimmed.Length - 4)), trimmed.AsSpan(trimmed.Length - 4));
    }

    private ProviderCredentialVaultRecord? ReadEnvironmentFallback(ProviderCredentialCatalogEntry descriptor)
    {
        if (!descriptor.RequiresCredentials || !ShouldAllowEnvironmentFallback())
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in descriptor.RequiredFields)
        {
            var value = ReadFirstEnvironmentValue(field.EnvironmentNames);
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields[field.Name] = value.Trim();
            }
        }

        if (fields.Count == 0)
        {
            return null;
        }

        var environment = descriptor.EnvironmentNames is { Count: > 0 }
            ? descriptor.NormalizeEnvironment(ReadFirstEnvironmentValue(descriptor.EnvironmentNames))
            : descriptor.DefaultEnvironment;

        return new ProviderCredentialVaultRecord
        {
            ProviderId = descriptor.ProviderId,
            Fields = fields,
            Environment = string.IsNullOrWhiteSpace(environment) ? null : environment,
            SavedAt = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["legacyFallback"] = "environment",
                ["environmentFallbackAllowed"] = "true",
                ["migrationRequired"] = "store-provider-secrets-in-vault"
            }
        };
    }

    private bool ShouldAllowEnvironmentFallback()
    {
        if (IsTruthy(ReadFallbackPolicyValue(EnvironmentFallbackOverride)))
        {
            return true;
        }

        if (IsTruthy(ReadFallbackPolicyValue(PackagedBuildEnvVar)) ||
            IsTruthy(ReadFallbackPolicyValue(CustomerBuildEnvVar)))
        {
            return false;
        }

        return IsDevelopmentLike(ReadFallbackPolicyValue("DOTNET_ENVIRONMENT")) ||
               IsDevelopmentLike(ReadFallbackPolicyValue("ASPNETCORE_ENVIRONMENT"));
    }

    private string? ReadFallbackPolicyValue(string name)
        => _configurationValue is null ? Environment.GetEnvironmentVariable(name) : _configurationValue(name);

    private static bool IsDevelopmentLike(string? environment)
        => environment is not null &&
           (environment.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("Test", StringComparison.OrdinalIgnoreCase));

    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private string? ReadFirstEnvironmentValue(IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var value = _configurationValue is null ? ReadEnvironmentValue(name) : _configurationValue(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? ReadEnvironmentValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }
        catch (Exception ex) when (
            ex is PlatformNotSupportedException
            || ex is System.Security.SecurityException
            || ex is UnauthorizedAccessException
            || ex is IOException)
        {
            return null;
        }
    }

    private async Task<ProviderCredentialVault> LoadVaultAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(VaultPath) && File.Exists(_vaultBackupPath))
                return await LoadVaultFromFileAsync(_vaultBackupPath, ct).ConfigureAwait(false);
            return await LoadVaultFromFileAsync(VaultPath, ct).ConfigureAwait(false);
        }
        catch (Exception primaryFailure) when (IsVaultCorruption(primaryFailure))
        {
            // A single corrupt write must not lock operators out of every provider
            // credential. Fall back to the rolling last-known-good backup — loudly, and
            // leaving the corrupt primary on disk for inspection.
            Log.Error(
                primaryFailure,
                "Provider credential vault at {VaultPath} is unreadable; attempting last-known-good backup at {BackupPath}",
                VaultPath, _vaultBackupPath);

            if (!File.Exists(_vaultBackupPath))
            {
                Log.Error("No provider credential vault backup exists at {BackupPath}; giving up", _vaultBackupPath);
                throw;
            }

            try
            {
                var vault = await LoadVaultFromFileAsync(_vaultBackupPath, ct).ConfigureAwait(false);
                Log.Warning(
                    "Recovered provider credentials from backup {BackupPath}; changes made after the backup was taken are lost and the corrupt vault is preserved at {VaultPath}",
                    _vaultBackupPath, VaultPath);
                return vault;
            }
            catch (Exception backupFailure) when (IsVaultCorruption(backupFailure))
            {
                Log.Error(backupFailure, "Provider credential vault backup at {BackupPath} is also unreadable", _vaultBackupPath);
                throw primaryFailure;
            }
        }
    }

    private static bool IsVaultCorruption(Exception ex)
        => ex is JsonException or FormatException or InvalidOperationException or CryptographicException;

    private async Task<ProviderCredentialVault> LoadVaultFromFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return new ProviderCredentialVault();
        }

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        // Holding one file handle keeps this generation stable while a writer replaces
        // the path. Delete sharing allows that replacement on Windows without writing
        // through the handle from which credentials are being decrypted.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        var envelopeJson = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            throw new InvalidOperationException("Provider credential vault is empty or truncated.");
        }

        var envelope = JsonSerializer.Deserialize<ProtectedVaultEnvelope>(envelopeJson, JsonOptions)
            ?? throw new InvalidOperationException("Provider credential vault envelope is invalid.");
        // Not a corruption type: recovering from the backup would let this release overwrite a
        // newer primary it cannot model, which is the rollback loss this format guards against.
        if (envelope.Version > ScopedVaultVersion)
            throw new NotSupportedException(
                $"Provider credential vault format version {envelope.Version} was written by a newer Meridian release and cannot be opened by this one.");
        var protectedBytes = Convert.FromBase64String(envelope.CipherText);
        var plainBytes = await UnprotectAsync(envelope.Protection, protectedBytes, ct).ConfigureAwait(false);
        var vaultJson = Encoding.UTF8.GetString(plainBytes);
        var vault = JsonSerializer.Deserialize<ProviderCredentialVault>(vaultJson, JsonOptions)
            ?? throw new InvalidOperationException("Provider credential vault payload is invalid.");

        // Existing vaults retain accepted configuration aliases such as nasdaqdatalink and ib.
        // Normalize the immutable read snapshot; the next ordinary mutation persists canonical
        // keys under the existing writer lock. Retain the newest whole record if aliases coexist,
        // rather than combining credential fields from different saved generations.
        foreach (var pair in vault.Providers)
        {
            if (pair.Value.Scope is not { } retainedScope)
            {
                if (pair.Key.Contains("@scope:", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Stored scoped credentials have no ownership scope.");
                continue;
            }

            // Check the persisted ownership identity before re-keying an accepted provider alias.
            // Never repair a mismatched scope by moving its secrets into another owner's record.
            ValidateRecordScope(pair.Value, retainedScope);
            if (!string.Equals(pair.Key, retainedScope.StorageKey(pair.Value.ProviderId), StringComparison.Ordinal))
                throw new InvalidDataException("Stored credential identity does not match its ownership scope.");
        }

        vault.Providers = vault.Providers
            .GroupBy(pair => pair.Value.Scope is { } retainedScope
                ? retainedScope.StorageKey(ProviderCredentialCatalog.NormalizeProviderId(pair.Value.ProviderId))
                : ProviderCredentialCatalog.NormalizeProviderId(pair.Key), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var record = group.OrderByDescending(pair => pair.Value.UpdatedAt)
                        .ThenByDescending(pair => string.Equals(pair.Key, group.Key, StringComparison.Ordinal))
                        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                        .First().Value;
                    record.ProviderId = record.Scope is null
                        ? group.Key
                        : ProviderCredentialCatalog.NormalizeProviderId(record.ProviderId);
                    return record;
                },
                StringComparer.OrdinalIgnoreCase);
        vault.LegacyImportedProviderIds = vault.LegacyImportedProviderIds
            .Select(ProviderCredentialCatalog.NormalizeProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return vault;
    }

    private async Task WriteVaultAsync(ProviderCredentialVault vault, CancellationToken ct,
        bool discardPreviousGeneration = false, bool retainCurrentGenerationAsBackup = false)
    {
        EnsureVaultDirectory();
        var scopedFormat = ContainsScopedState(vault);
        vault.UpdatedAt = DateTimeOffset.UtcNow;
        var envelopeJson = await SerializeEnvelopeAsync(vault, scopedFormat, ct).ConfigureAwait(false);

        // Roll the current (readable) vault to the last-known-good backup before replacing
        // it, so a corrupting write can always fall back one generation in LoadVaultAsync.
        // Callers hold the cross-instance writer lock. Publish the backup atomically too,
        // so readers never observe an in-place copy's truncated intermediate generation.
        if (discardPreviousGeneration)
        {
            // Deletion must remove every recoverable copy before it can be acknowledged.
            File.Delete(_vaultBackupPath);
        }
        else if (File.Exists(VaultPath))
        {
            try
            {
                // Keep a usable recovery copy when this mutation follows primary corruption.
                var previousVault = await LoadVaultFromFileAsync(VaultPath, ct).ConfigureAwait(false);
                var previousGeneration = await File.ReadAllBytesAsync(VaultPath, ct).ConfigureAwait(false);
                if (scopedFormat && !IsScopedEnvelope(previousGeneration))
                {
                    // Re-protect the unchanged previous generation so an older binary cannot fall
                    // back to it and overwrite the scoped primary written below.
                    previousGeneration = Encoding.UTF8.GetBytes(
                        await SerializeEnvelopeAsync(previousVault, scopedFormat: true, ct).ConfigureAwait(false));
                }

                await AtomicFileWriter.WriteAsync(_vaultBackupPath, previousGeneration, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsVaultCorruption(ex))
            {
                Log.Warning("Retained provider credential recovery backup because the primary vault is corrupt");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not refresh provider credential vault backup at {BackupPath}", _vaultBackupPath);
            }
        }

        if (scopedFormat && !discardPreviousGeneration)
            await EnsureBackupUsesScopedFormatAsync(ct).ConfigureAwait(false);

        await AtomicFileWriter.WriteAsync(VaultPath, envelopeJson, ct).ConfigureAwait(false);
        if (discardPreviousGeneration || retainCurrentGenerationAsBackup)
            await AtomicFileWriter.WriteAsync(_vaultBackupPath, envelopeJson, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool ContainsScopedState(ProviderCredentialVault vault)
        => vault.ScopedOAuthTokens.Count > 0 || vault.Providers.Values.Any(static record => record.Scope is not null);

    private async Task<string> SerializeEnvelopeAsync(ProviderCredentialVault vault, bool scopedFormat, CancellationToken ct)
    {
        var version = scopedFormat ? ScopedVaultVersion : VaultVersion;
        vault.Version = version;
        var plainBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(vault, JsonOptions));
        var (protection, protectedBytes) = await ProtectAsync(plainBytes, ct).ConfigureAwait(false);
        var envelope = new ProtectedVaultEnvelope(version, scopedFormat ? protection + ScopedProtectionSuffix : protection,
            Convert.ToBase64String(protectedBytes));
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private static bool IsScopedEnvelope(byte[] envelopeBytes)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<ProtectedVaultEnvelope>(envelopeBytes, JsonOptions);
            return envelope?.Protection?.EndsWith(ScopedProtectionSuffix, StringComparison.Ordinal) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Guarantees that a backup surviving a scoped write is not readable by older binaries. This
    /// covers a backup left behind when the primary was missing, corrupt, or could not be copied.
    /// An unreadable backup is left in place because older binaries cannot open it either. I/O
    /// failures propagate so the scoped primary is never published beside a legacy backup.
    /// </summary>
    private async Task EnsureBackupUsesScopedFormatAsync(CancellationToken ct)
    {
        if (!File.Exists(_vaultBackupPath))
            return;

        var backupGeneration = await File.ReadAllBytesAsync(_vaultBackupPath, ct).ConfigureAwait(false);
        if (IsScopedEnvelope(backupGeneration))
            return;

        ProviderCredentialVault backupVault;
        try
        {
            backupVault = await LoadVaultFromFileAsync(_vaultBackupPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsVaultCorruption(ex))
        {
            return;
        }

        var upgraded = await SerializeEnvelopeAsync(backupVault, scopedFormat: true, ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(_vaultBackupPath, upgraded, ct).ConfigureAwait(false);
    }

    private async Task<(string Protection, byte[] ProtectedBytes)> ProtectAsync(byte[] plainBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            return ("dpapi-current-user", ProtectWithDpapi(plainBytes));
        }

        return ("local-aes-gcm", await ProtectWithLocalKeyAsync(plainBytes, ct).ConfigureAwait(false));
    }

    private async Task<byte[]> UnprotectAsync(string protection, byte[] protectedBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (protection.EndsWith(ScopedProtectionSuffix, StringComparison.Ordinal))
            protection = protection[..^ScopedProtectionSuffix.Length];
        return protection switch
        {
            "dpapi-current-user" when OperatingSystem.IsWindows() => UnprotectWithDpapi(protectedBytes),
            "dpapi-current-user" => throw new PlatformNotSupportedException("DPAPI protected credential vaults can only be opened by the Windows user profile that created them."),
            "local-aes-gcm" => await UnprotectWithLocalKeyAsync(protectedBytes, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported provider credential vault protection '{protection}'.")
        };
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWithDpapi(byte[] plainBytes)
        => ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWithDpapi(byte[] protectedBytes)
        => ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);

    private async Task<byte[]> ProtectWithLocalKeyAsync(byte[] plainBytes, CancellationToken ct)
    {
        var key = await GetOrCreateLocalKeyAsync(ct).ConfigureAwait(false);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plainBytes.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var output = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, output, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, output, nonce.Length + tag.Length, cipher.Length);
        return output;
    }

    private async Task<byte[]> UnprotectWithLocalKeyAsync(byte[] protectedBytes, CancellationToken ct)
    {
        if (protectedBytes.Length < 28)
        {
            throw new InvalidOperationException("Provider credential vault payload is truncated.");
        }

        var key = await GetOrCreateLocalKeyAsync(ct).ConfigureAwait(false);
        var nonce = protectedBytes.AsSpan(0, 12).ToArray();
        var tag = protectedBytes.AsSpan(12, 16).ToArray();
        var cipher = protectedBytes.AsSpan(28).ToArray();
        var plainBytes = new byte[cipher.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plainBytes);
        return plainBytes;
    }

    // The AES-GCM key lives in the same directory as the ciphertext it opens, so on Unix the file
    // mode is the only thing separating the two: a reader who can open the vault can open the key
    // beside it. Owner-only is therefore a correctness requirement of the encryption, not
    // defence in depth.
    private const UnixFileMode OwnerOnlyFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private async Task<byte[]> GetOrCreateLocalKeyAsync(CancellationToken ct)
    {
        EnsureVaultDirectory();
        if (File.Exists(_keyPath))
        {
            RestrictExistingKeyFile(_keyPath);
            return await File.ReadAllBytesAsync(_keyPath, ct).ConfigureAwait(false);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        await AtomicFileWriter.WriteAsync(_keyPath, key, OwnerOnlyFileMode, ct).ConfigureAwait(false);
        TrySetHidden(_keyPath);
        return key;
    }

    // Only a directory this store creates is narrowed. An existing .mdc is left as the deployment
    // configured it - it is shared with other credential state, and silently removing access a
    // running install depends on would be a worse failure than the one being prevented. The key
    // file's own mode is what actually protects the key, and that is enforced either way.
    private void EnsureVaultDirectory()
    {
        if (OperatingSystem.IsWindows() || Directory.Exists(_directoryPath))
        {
            Directory.CreateDirectory(_directoryPath);
            return;
        }

        Directory.CreateDirectory(_directoryPath, OwnerOnlyDirectoryMode);
    }

    private static void RestrictExistingKeyFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            var exposed = mode & ~OwnerOnlyFileMode;
            if (exposed == UnixFileMode.None)
            {
                return;
            }

            File.SetUnixFileMode(path, OwnerOnlyFileMode);
            Log.Warning(
                "Provider credential vault key at {KeyPath} was reachable beyond its owner ({ExposedMode}); tightened to owner-only. The key should be treated as disclosed and provider credentials rotated.",
                path,
                exposed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Log.Warning(ex, "Could not verify permissions on the provider credential vault key at {KeyPath}", path);
        }
    }

    private static void TrySetHidden(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Encryption still protects the vault payload; hiding the local key is best effort.
        }
    }

    private Task AppendAuditAsync(
        ProviderCredentialCatalogEntry descriptor,
        string action,
        string? actor,
        ProviderCredentialStoreStatus status,
        IReadOnlyList<string> fields,
        CancellationToken ct,
        ProviderCredentialScope? scope = null)
        => AppendAuditLineAsync(BuildAuditLine(descriptor, action, actor, status, fields, scope), ct);

    private static string BuildAuditLine(
        ProviderCredentialCatalogEntry descriptor,
        string action,
        string? actor,
        ProviderCredentialStoreStatus status,
        IReadOnlyList<string> fields,
        ProviderCredentialScope? scope)
    {
        var entry = new ProviderCredentialAuditEntry(
            Timestamp: DateTimeOffset.UtcNow,
            ProviderId: descriptor.ProviderId,
            Action: action,
            Actor: string.IsNullOrWhiteSpace(actor) ? "local-operator" : actor.Trim(),
            CredentialState: status.CredentialState,
            CredentialSource: status.CredentialSource,
            VerificationState: status.VerificationState,
            FieldNames: fields,
            Environment: status.Environment,
            ExternalAccountId: status.ExternalAccountId,
            Scope: scope);
        return JsonSerializer.Serialize(entry, JsonOptions);
    }

    private static string? SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return message.Trim();
    }

    private sealed record ProtectedVaultEnvelope(int Version, string Protection, string CipherText);

    private sealed record ProviderCredentialAuditEntry(
        DateTimeOffset Timestamp,
        string ProviderId,
        string Action,
        string Actor,
        ProviderCredentialStateDto CredentialState,
        ProviderCredentialSourceDto CredentialSource,
        ProviderVerificationStateDto VerificationState,
        IReadOnlyList<string> FieldNames,
        string? Environment,
        string? ExternalAccountId,
        ProviderCredentialScope? Scope);

    private sealed class ProviderCredentialVault
    {
        public int Version { get; set; } = VaultVersion;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>
        /// The operation id of the mutation that last published this vault. Changes only when a
        /// publication lands, so replay can prove a journaled intent by it rather than by a
        /// pre-existing state (an already-absent key, an older token) that the intent did not create.
        /// </summary>
        public string? LastPublicationId { get; set; }
        /// <summary>
        /// Publication receipts still needed by journaled audit records: the latest publication plus
        /// every earlier one whose audit append is still owed. A later publication carries them
        /// forward, so it cannot erase the proof an earlier intent relies on; receipts drop out once
        /// their journal line is gone.
        /// </summary>
        public HashSet<string> PublicationReceipts { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, ProviderCredentialVaultRecord> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HoldsPublication(string publicationId)
            => string.Equals(LastPublicationId, publicationId, StringComparison.Ordinal)
               || PublicationReceipts.Contains(publicationId);
        public Dictionary<string, OAuthToken> OAuthTokens { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, ScopedOAuthVaultRecord> ScopedOAuthTokens { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> LegacyImportedProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LegacyImportedOAuthProviders { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ScopedOAuthVaultRecord
    {
        public string ProviderName { get; set; } = string.Empty;
        public ProviderCredentialScope? Scope { get; set; }
        public OAuthToken? Token { get; set; }
    }

    private sealed class ProviderCredentialVaultRecord
    {
        public ProviderCredentialScope? Scope { get; set; }
        public string ProviderId { get; set; } = string.Empty;
        public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Environment { get; set; }
        public DateTimeOffset? SavedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastVerifiedAt { get; set; }
        public DateTimeOffset? LastSuccessfulAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public string? LastError { get; set; }
        public string? ExternalAccountId { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
