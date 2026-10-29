using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Archival;

namespace Meridian.Application.SecurityMaster;

/// <summary>Content-addressed calculation evidence committed through the durable atomic writer.</summary>
public sealed class FileSecurityCashFlowReceiptStore : ISecurityCashFlowReceiptStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSecurityCashFlowReceiptStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async Task RetainAsync(SecurityCashFlowCalculationReceipt receipt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!string.Equals(receipt.ContentSha256, SecurityMasterCashFlowService.ReceiptHash(receipt), StringComparison.Ordinal))
            throw new ArgumentException("Calculation receipt content digest is invalid.", nameof(receipt));
        var path = ReceiptPath(receipt.ContentSha256);
        var json = JsonSerializer.Serialize(receipt, SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                if (!string.Equals(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), json, StringComparison.Ordinal))
                    throw new InvalidOperationException("Retained calculation evidence is immutable and cannot be replaced.");
                return;
            }
            await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<SecurityCashFlowCalculationReceipt?> GetAsync(string contentSha256, CancellationToken ct = default)
    {
        var path = ReceiptPath(contentSha256);
        if (!File.Exists(path))
            return null;
        var receipt = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt);
        if (receipt is null || receipt.ContentSha256 != contentSha256 || SecurityMasterCashFlowService.ReceiptHash(receipt) != contentSha256)
            throw new InvalidDataException("Retained calculation evidence has an invalid content digest.");
        return receipt;
    }

    private string ReceiptPath(string digest)
    {
        if (digest is null || digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw new ArgumentException("Receipt identity must be a lowercase SHA-256 digest.", nameof(digest));
        return Path.Combine(_directory, digest + ".json");
    }
}
