using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;

namespace Meridian.Application.SecurityMaster;

/// <summary>Tenant-separated, content-addressed immutable evidence with cross-process create-only publication.</summary>
/// <remarks>
/// Concurrency posture: immutable per-entity files. Durable unique temporary files publish with
/// atomic create-only rename; independent processes cannot replace a committed receipt. Exact
/// content retries are idempotent, conflicting content refuses, and no process-local cache owns state.
/// </remarks>
public sealed class FileSecurityMasterImpactPreviewStore(string directory) : ISecurityMasterImpactPreviewStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _directory = Path.GetFullPath(directory);
    internal static string Hash<T>(T value) => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(value, JsonOptions));
    internal static string ReceiptHash(SecurityMasterImpactPreviewReceipt receipt) => Hash(receipt with { ContentSha256 = string.Empty });

    public async Task RetainAsync(SecurityMasterImpactPreviewReceipt receipt, CancellationToken ct = default)
    {
        if (!Sha256Digest.FixedEquals(receipt.ContentSha256, ReceiptHash(receipt)))
            throw new InvalidDataException("Impact evidence digest is invalid.");
        var path = ReceiptPath(receipt.Scope, receipt.ContentSha256);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(receipt, JsonOptions);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await AtomicFileWriter.WriteAsync(temporary, json, ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: false);
                await AtomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(path)!, CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(path))
            {
                if (!string.Equals(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), json, StringComparison.Ordinal))
                    throw new InvalidDataException("Immutable impact evidence cannot be replaced.");
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<SecurityMasterImpactPreviewReceipt?> GetAsync(SecurityMasterImpactScope scope, string receiptId, CancellationToken ct = default)
    {
        var path = ReceiptPath(scope, receiptId);
        if (!File.Exists(path))
            return null;
        var receipt = JsonSerializer.Deserialize<SecurityMasterImpactPreviewReceipt>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), JsonOptions);
        if (receipt is null || receipt.Scope != scope || receipt.ContentSha256 != receiptId
            || !Sha256Digest.FixedEquals(receiptId, ReceiptHash(receipt)))
            throw new InvalidDataException("Impact evidence scope or digest is invalid.");
        return receipt;
    }

    private string ReceiptPath(SecurityMasterImpactScope scope, string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        if (!Sha256Digest.IsCanonical(digest))
            throw new ArgumentException("Receipt identity must be a lowercase SHA-256 digest.");
        return Path.Combine(_directory, Hash(scope), digest + ".json");
    }
}
