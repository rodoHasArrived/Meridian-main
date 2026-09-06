using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using Meridian.Contracts.Configuration;
using Meridian.Core.Logging;
using Meridian.Storage.Archival;
using Serilog;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.DataIntegration.Credentials;

public sealed class FileProviderCredentialStore : IProviderCredentialStore, ILegacyProviderCredentialImporter, IOAuthTokenVault
{
    private static readonly ILogger Log = LoggingSetup.ForContext<FileProviderCredentialStore>();

    private const int VaultVersion = 1;
    private const string VaultFileName = "provider-credentials.vault";
    private const string VaultBackupFileName = "provider-credentials.vault.bak";
    private const string KeyFileName = "provider-credentials.key";
    private const string AuditFileName = "provider-credentials.audit.jsonl";
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

    public FileProviderCredentialStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        _directoryPath = Path.Combine(Path.GetFullPath(dataRoot), ".mdc");
        VaultPath = Path.Combine(_directoryPath, VaultFileName);
        _vaultBackupPath = Path.Combine(_directoryPath, VaultBackupFileName);
        _keyPath = Path.Combine(_directoryPath, KeyFileName);
        _auditPath = Path.Combine(_directoryPath, AuditFileName);
    }

    public string VaultPath { get; }

    public async Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);
        var readResult = await ReadForProviderAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
        return BuildStatus(descriptor, readResult);
    }

    public async Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            if (vault.Providers.TryGetValue(descriptor.ProviderId, out var localRecord))
            {
                return ToReadResult(descriptor, localRecord, ProviderCredentialSourceDto.LocalEncryptedStore);
            }
        }
        finally
        {
            _gate.Release();
        }

        var fallbackRecord = ReadEnvironmentFallback(descriptor);
        return fallbackRecord is null
            ? null
            : ToReadResult(descriptor, fallbackRecord, ProviderCredentialSourceDto.Environment);
    }

    public async Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var descriptor = RequireDescriptor(request.ProviderId);
        var normalizedCredentials = NormalizeCredentialFields(descriptor, request.Credentials ?? new Dictionary<string, string?>());
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            vault.Providers.TryGetValue(descriptor.ProviderId, out var existing);

            var updated = CreateUpdatedRecord(descriptor, request, normalizedCredentials, existing, now);

            vault.Providers[descriptor.ProviderId] = updated;
            await WriteVaultAsync(vault, ct).ConfigureAwait(false);
            await AppendAuditAsync(
                descriptor,
                "save",
                request.Actor,
                BuildStatus(descriptor, ToReadResult(descriptor, updated, ProviderCredentialSourceDto.LocalEncryptedStore)),
                updated.Fields.Keys.OrderBy(static field => field, StringComparer.OrdinalIgnoreCase).ToArray(),
                ct).ConfigureAwait(false);
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
        }).ToArray();
        if (prepared.Select(item => item.Descriptor.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != prepared.Length)
            throw new InvalidOperationException("Legacy credential snapshot contains duplicate provider identities.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            var imported = new List<(ProviderCredentialCatalogEntry Descriptor, ProviderCredentialVaultRecord Record)>();
            foreach (var item in prepared)
            {
                // A retained vault value is authoritative even after a partial migration or rotation.
                if (vault.Providers.ContainsKey(item.Descriptor.ProviderId))
                    continue;
                var record = CreateUpdatedRecord(item.Descriptor, item.Request, item.Fields, null, DateTimeOffset.UtcNow);
                vault.Providers.Add(item.Descriptor.ProviderId, record);
                imported.Add((item.Descriptor, record));
            }

            if (imported.Count > 0)
                await WriteVaultAsync(vault, ct).ConfigureAwait(false);
            // Record every attempted provider, including retries after an audit-write failure.
            // The sidecar is retained until all these audit appends succeed.
            foreach (var item in prepared)
            {
                var record = vault.Providers[item.Descriptor.ProviderId];
                await AppendAuditAsync(item.Descriptor, "legacy-import-or-preserve", "credential-vault-migration",
                    BuildStatus(item.Descriptor, ToReadResult(item.Descriptor, record, ProviderCredentialSourceDto.LocalEncryptedStore)),
                    record.Fields.Keys.OrderBy(static field => field, StringComparer.OrdinalIgnoreCase).ToArray(), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, OAuthToken>> ReadOAuthTokensAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
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
                vault.OAuthTokens.Remove(providerName);
            else
                vault.OAuthTokens[providerName] = token;
            await WriteVaultAsync(vault, ct).ConfigureAwait(false);
            await AppendOAuthAuditAsync(providerName, token is null ? "oauth-delete" : "oauth-save", ct).ConfigureAwait(false);
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
            var changed = false;
            foreach (var pair in tokens)
                changed |= vault.OAuthTokens.TryAdd(pair.Key, pair.Value);
            if (changed)
                await WriteVaultAsync(vault, ct).ConfigureAwait(false);
            foreach (var provider in tokens.Keys)
                await AppendOAuthAuditAsync(provider, "oauth-import-or-preserve", ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private Task AppendOAuthAuditAsync(string providerName, string action, CancellationToken ct)
        => AtomicFileWriter.AppendLinesAsync(_auditPath,
            [JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.UtcNow, ProviderId = providerName, Action = action }, JsonOptions)], ct);

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

    public async Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            vault.Providers.Remove(descriptor.ProviderId);
            await WriteVaultAsync(vault, ct).ConfigureAwait(false);
            await AppendAuditAsync(
                descriptor,
                "delete",
                actor,
                BuildStatus(descriptor, null),
                [],
                ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var descriptor = RequireDescriptor(update.ProviderId);
        var verifiedAt = update.VerifiedAt ?? DateTimeOffset.UtcNow;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            if (!vault.Providers.TryGetValue(descriptor.ProviderId, out var record))
            {
                return;
            }

            record.LastVerifiedAt = verifiedAt;
            record.LastError = update.Success ? null : SanitizeError(update.ErrorMessage);
            record.ExternalAccountId = update.Success ? NormalizeOptional(update.ExternalAccountId) : record.ExternalAccountId;
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

            vault.Providers[descriptor.ProviderId] = record;
            await WriteVaultAsync(vault, ct).ConfigureAwait(false);
            await AppendAuditAsync(
                descriptor,
                update.Success ? "verify-success" : "verify-failure",
                update.Actor,
                BuildStatus(descriptor, ToReadResult(descriptor, record, ProviderCredentialSourceDto.LocalEncryptedStore)),
                [],
                ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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
        var credentialState = missingFields.Length == descriptor.RequiredFields.Count
            ? ProviderCredentialStateDto.Missing
            : missingFields.Length > 0
                ? ProviderCredentialStateDto.Partial
                : hasError
                    ? ProviderCredentialStateDto.Invalid
                    : readResult?.LastSuccessfulAt is not null
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

    private static ProviderCredentialVaultRecord? ReadEnvironmentFallback(ProviderCredentialCatalogEntry descriptor)
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

    private static bool ShouldAllowEnvironmentFallback()
    {
        if (IsTruthy(Environment.GetEnvironmentVariable(EnvironmentFallbackOverride)))
        {
            return true;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable(PackagedBuildEnvVar)) ||
            IsTruthy(Environment.GetEnvironmentVariable(CustomerBuildEnvVar)))
        {
            return false;
        }

        return IsDevelopmentLike(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")) ||
               IsDevelopmentLike(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
    }

    private static bool IsDevelopmentLike(string? environment)
        => environment is not null &&
           (environment.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("Test", StringComparison.OrdinalIgnoreCase));

    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static string? ReadFirstEnvironmentValue(IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var value = ReadEnvironmentValue(name);
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

        var envelopeJson = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return new ProviderCredentialVault();
        }

        var envelope = JsonSerializer.Deserialize<ProtectedVaultEnvelope>(envelopeJson, JsonOptions)
            ?? throw new InvalidOperationException("Provider credential vault envelope is invalid.");
        var protectedBytes = Convert.FromBase64String(envelope.CipherText);
        var plainBytes = await UnprotectAsync(envelope.Protection, protectedBytes, ct).ConfigureAwait(false);
        var vaultJson = Encoding.UTF8.GetString(plainBytes);
        var vault = JsonSerializer.Deserialize<ProviderCredentialVault>(vaultJson, JsonOptions);
        return vault ?? new ProviderCredentialVault();
    }

    private async Task WriteVaultAsync(ProviderCredentialVault vault, CancellationToken ct)
    {
        EnsureVaultDirectory();
        vault.Version = VaultVersion;
        vault.UpdatedAt = DateTimeOffset.UtcNow;

        var vaultJson = JsonSerializer.Serialize(vault, JsonOptions);
        var plainBytes = Encoding.UTF8.GetBytes(vaultJson);
        var (protection, protectedBytes) = await ProtectAsync(plainBytes, ct).ConfigureAwait(false);
        var envelope = new ProtectedVaultEnvelope(VaultVersion, protection, Convert.ToBase64String(protectedBytes));
        var envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);

        // Roll the current (readable) vault to the last-known-good backup before replacing
        // it, so a corrupting write can always fall back one generation in LoadVaultAsync.
        // Callers hold _gate, so the copy/write pair cannot interleave with another writer.
        if (File.Exists(VaultPath))
        {
            try
            {
                File.Copy(VaultPath, _vaultBackupPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not refresh provider credential vault backup at {BackupPath}", _vaultBackupPath);
            }
        }

        await AtomicFileWriter.WriteAsync(VaultPath, envelopeJson, ct).ConfigureAwait(false);
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

    private async Task AppendAuditAsync(
        ProviderCredentialCatalogEntry descriptor,
        string action,
        string? actor,
        ProviderCredentialStoreStatus status,
        IReadOnlyList<string> fields,
        CancellationToken ct)
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
            ExternalAccountId: status.ExternalAccountId);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await AtomicFileWriter.AppendLinesAsync(_auditPath, [json], ct).ConfigureAwait(false);
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
        string? ExternalAccountId);

    private sealed class ProviderCredentialVault
    {
        public int Version { get; set; } = VaultVersion;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<string, ProviderCredentialVaultRecord> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OAuthToken> OAuthTokens { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ProviderCredentialVaultRecord
    {
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
