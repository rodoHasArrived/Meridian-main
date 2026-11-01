using Meridian.Storage.Archival;

namespace Meridian.DataIntegration.Credentials;

public sealed partial class FileProviderCredentialStore
{
    // Callers hold the vault writer lock. Preserve publication receipts and all current credential
    // generations byte-for-byte; restoring redundancy is not another ownership/audit operation.
    private async Task RepairCurrentRecoveryGenerationAsync(CancellationToken ct)
    {
        var current = await ReadOrRestorePrimaryGenerationAsync(ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(_vaultBackupPath, current, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadOrRestorePrimaryGenerationAsync(CancellationToken ct)
    {
        if (File.Exists(VaultPath))
        {
            try
            {
                // Keep newer formats and malformed ownership as refusals, not recovery candidates.
                await LoadVaultFromFileAsync(VaultPath, ct).ConfigureAwait(false);
                return await File.ReadAllBytesAsync(VaultPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsVaultCorruption(ex))
            {
                // The valid backup remains intact until its atomic restoration has succeeded.
            }
        }

        if (!File.Exists(_vaultBackupPath))
            throw new InvalidDataException("No readable credential recovery generation is available.");
        await LoadVaultFromFileAsync(_vaultBackupPath, ct).ConfigureAwait(false);
        var recovery = await File.ReadAllBytesAsync(_vaultBackupPath, ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(VaultPath, recovery, ct).ConfigureAwait(false);
        return recovery;
    }
}
