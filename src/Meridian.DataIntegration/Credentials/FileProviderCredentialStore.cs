using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Storage.Archival;

namespace Meridian.DataIntegration.Credentials;

public sealed class FileProviderCredentialStore : IProviderCredentialStore
{
    private const int VaultVersion = 1;
    private const string VaultFileName = "provider-credentials.vault";
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
    private readonly string _keyPath;
    private readonly string _auditPath;

    public FileProviderCredentialStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        _directoryPath = Path.Combine(Path.GetFullPath(dataRoot), ".mdc");
        VaultPath = Path.Combine(_directoryPath, VaultFileName);
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
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            vault.Providers.TryGetValue(descriptor.ProviderId, out var existing);

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

            var updated = new ProviderCredentialVaultRecord
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

            vault.Providers[descriptor.ProviderId] = updated;
            await WriteVaultAsync(vault, ct).ConfigureAwait(false);
            await AppendAuditAsync(
                descriptor,
                "save",
                request.Actor,
                BuildStatus(descriptor, ToReadResult(descriptor, updated, ProviderCredentialSourceDto.LocalEncryptedStore)),
                fields.Keys.OrderBy(static field => field, StringComparer.OrdinalIgnoreCase).ToArray(),
                ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            if (!vault.Providers.TryGetValue(descriptor.ProviderId, out var record))
            {
                return;
            }

            record.LastVerifiedAt = verifiedAt;
            record.LastError = update.Success ? null : SanitizeError(update.ErrorMessage);
            record.ExternalAccountId = update.Success ? NullIfBlank(update.ExternalAccountId) : record.ExternalAccountId;
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
        if (!File.Exists(VaultPath))
        {
            return new ProviderCredentialVault();
        }

        var envelopeJson = await File.ReadAllTextAsync(VaultPath, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return new ProviderCredentialVault();
        }

        var envelope = JsonSerializer.Deserialize<ProtectedVaultEnvelope>(envelopeJson, JsonOptions)
            ?? throw new InvalidOperationException("Provider credential vault envelope is invalid.");
        var protectedBytes = Convert.FromBase64String(envelope.CipherText);
        var plainBytes = Unprotect(envelope.Protection, protectedBytes, ct);
        var vaultJson = Encoding.UTF8.GetString(plainBytes);
        var vault = JsonSerializer.Deserialize<ProviderCredentialVault>(vaultJson, JsonOptions);
        return vault ?? new ProviderCredentialVault();
    }

    private async Task WriteVaultAsync(ProviderCredentialVault vault, CancellationToken ct)
    {
        Directory.CreateDirectory(_directoryPath);
        vault.Version = VaultVersion;
        vault.UpdatedAt = DateTimeOffset.UtcNow;

        var vaultJson = JsonSerializer.Serialize(vault, JsonOptions);
        var plainBytes = Encoding.UTF8.GetBytes(vaultJson);
        var (protection, protectedBytes) = Protect(plainBytes, ct);
        var envelope = new ProtectedVaultEnvelope(VaultVersion, protection, Convert.ToBase64String(protectedBytes));
        var envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
        await AtomicFileWriter.WriteAsync(VaultPath, envelopeJson, ct).ConfigureAwait(false);
    }

    private (string Protection, byte[] ProtectedBytes) Protect(byte[] plainBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            return ("dpapi-current-user", ProtectWithDpapi(plainBytes));
        }

        return ("local-aes-gcm", ProtectWithLocalKey(plainBytes, ct));
    }

    private byte[] Unprotect(string protection, byte[] protectedBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return protection switch
        {
            "dpapi-current-user" when OperatingSystem.IsWindows() => UnprotectWithDpapi(protectedBytes),
            "dpapi-current-user" => throw new PlatformNotSupportedException("DPAPI protected credential vaults can only be opened by the Windows user profile that created them."),
            "local-aes-gcm" => UnprotectWithLocalKey(protectedBytes, ct),
            _ => throw new InvalidOperationException($"Unsupported provider credential vault protection '{protection}'.")
        };
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWithDpapi(byte[] plainBytes)
        => ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWithDpapi(byte[] protectedBytes)
        => ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);

    private byte[] ProtectWithLocalKey(byte[] plainBytes, CancellationToken ct)
    {
        var key = GetOrCreateLocalKey(ct);
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

    private byte[] UnprotectWithLocalKey(byte[] protectedBytes, CancellationToken ct)
    {
        if (protectedBytes.Length < 28)
        {
            throw new InvalidOperationException("Provider credential vault payload is truncated.");
        }

        var key = GetOrCreateLocalKey(ct);
        var nonce = protectedBytes.AsSpan(0, 12).ToArray();
        var tag = protectedBytes.AsSpan(12, 16).ToArray();
        var cipher = protectedBytes.AsSpan(28).ToArray();
        var plainBytes = new byte[cipher.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plainBytes);
        return plainBytes;
    }

    private byte[] GetOrCreateLocalKey(CancellationToken ct)
    {
        Directory.CreateDirectory(_directoryPath);
        if (File.Exists(_keyPath))
        {
            return File.ReadAllBytes(_keyPath);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        AtomicFileWriter.WriteAsync(_keyPath, key, ct).GetAwaiter().GetResult();
        TrySetHidden(_keyPath);
        return key;
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

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
