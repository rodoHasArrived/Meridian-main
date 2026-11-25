using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.SecurityMaster;

/// <summary>Immutable exact-response evidence. Content-addressed publication is atomic across processes.</summary>
public sealed class FileBondProviderResponseStore(string dataRoot) : IBondProviderResponseStore
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private readonly string _directory = Path.Combine(Path.GetFullPath(dataRoot), "security-master", "bond-provider-responses");

    public async Task<BondProviderResponseDto> RetainAsync(string tenantId, string companyId, string providerId,
        string rawResponseJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (providerId != providerId.Trim().ToLowerInvariant() || !providerId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new ArgumentException("Provider identity must be canonical.");
        ArgumentException.ThrowIfNullOrWhiteSpace(rawResponseJson);
        if (Encoding.UTF8.GetByteCount(rawResponseJson) > MaximumBytes)
            throw new ArgumentException("Provider response evidence is limited to sixteen MiB.");
        using var json = JsonDocument.Parse(rawResponseJson);
        var hash = Sha256Digest.ComputeUtf8(rawResponseJson);
        var path = ResponsePath(tenantId, companyId, hash);
        var existing = await GetAsync(tenantId, companyId, hash, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ProviderId != providerId)
                throw new InvalidOperationException("Response hash is already bound to another provider.");
            return existing;
        }
        var record = new BondProviderResponseEnvelope(1, tenantId, companyId, providerId, hash,
            MetadataHash(tenantId, companyId, providerId, hash), rawResponseJson);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, BondProviderResponseStoreJsonContext.Default.BondProviderResponseEnvelope, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            try
            { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { /* Another writer published the same content address. */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var retained = await GetAsync(tenantId, companyId, hash, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Published provider response evidence is unavailable.");
        if (retained.ProviderId != providerId)
            throw new InvalidOperationException("Response hash is already bound to another provider.");
        return retained;
    }

    public async Task<BondProviderResponseDto?> GetAsync(string tenantId, string companyId, string contentSha256,
        CancellationToken ct = default)
    {
        var path = ResponsePath(tenantId, companyId, contentSha256);
        if (!File.Exists(path))
            return null;
        try
        {
            var size = new FileInfo(path).Length;
            // JSON escaping expands the sixteen MiB UTF-8 source by at most sixfold.
            if (size > MaximumBytes * 6L + 4096)
                throw new InvalidDataException("Retained provider response exceeds its size bound.");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var record = await JsonSerializer.DeserializeAsync(stream, BondProviderResponseStoreJsonContext.Default.BondProviderResponseEnvelope, ct).ConfigureAwait(false);
            if (record is null || record.SchemaVersion != 1 || record.TenantId != tenantId || record.CompanyId != companyId
                || record.ContentSha256 != contentSha256 || string.IsNullOrWhiteSpace(record.ProviderId)
                || !Sha256Digest.FixedEquals(MetadataHash(tenantId, companyId, record.ProviderId, contentSha256), record.MetadataSha256)
                || record.RawResponseJson is null || Encoding.UTF8.GetByteCount(record.RawResponseJson) > MaximumBytes
                || !Sha256Digest.FixedEquals(Sha256Digest.ComputeUtf8(record.RawResponseJson), contentSha256))
                throw new InvalidDataException("Retained provider response scope or content hash is invalid.");
            using var json = JsonDocument.Parse(record.RawResponseJson);
            return new(record.ProviderId, contentSha256, $"/api/reference-data/bonds/provider-responses/{contentSha256}", record.RawResponseJson);
        }
        catch (JsonException ex) { throw new InvalidDataException("Retained provider response is corrupt.", ex); }
    }

    private static string MetadataHash(string tenantId, string companyId, string providerId, string hash)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { tenantId, companyId, providerId, hash }));

    private string ResponsePath(string tenantId, string companyId, string contentSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        if (tenantId != tenantId.Trim() || companyId != companyId.Trim() || !Sha256Digest.IsCanonical(contentSha256))
            throw new ArgumentException("Canonical tenant/company identities and response SHA-256 are required.");
        var scopeHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { tenantId, companyId }));
        return Path.Combine(_directory, scopeHash, contentSha256 + ".json");
    }
}

internal sealed record BondProviderResponseEnvelope(int SchemaVersion, string TenantId, string CompanyId,
    string ProviderId, string ContentSha256, string MetadataSha256, string RawResponseJson);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondProviderResponseEnvelope))]
internal partial class BondProviderResponseStoreJsonContext : JsonSerializerContext;
