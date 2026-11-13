using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Packaging;

/// <summary>Composes accounting validation with the model-neutral package transport.</summary>
public static class PortableLedgerPackaging
{
    public static PortableDataPackager CreatePackager(string dataRoot, CompressionProfileManager? compressionManager = null)
        => new(dataRoot, compressionManager, new PortableLedgerValidator());

    /// <summary>Reads a validated ledger without posting it into an existing book. The authorized
    /// host controls the separate, scoped import/review workflow.</summary>
    public static async Task<IReadOnlyLedger?> ReadLedgerAsync(this PortableDataPackager packager,
        string packagePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packager);
        var payload = await packager.ReadLedgerPayloadAsync(packagePath, ct).ConfigureAwait(false);
        return payload is null ? null : PortableLedgerValidator.Restore(payload);
    }
}

/// <summary>The accounting adapter replays journals and verifies the balance digest before the
/// lower-level transport may publish, expose or extract accounting evidence.</summary>
public sealed class PortableLedgerValidator : IPortableLedgerValidator
{
    public Task ValidateAsync(PortableLedgerPayload payload, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ = Restore(payload);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    internal static IReadOnlyLedger Restore(PortableLedgerPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var snapshot = PortableLedgerSnapshot.Deserialize(payload.Json);
        if (!Sha256Digest.FixedEquals(snapshot.BalanceChecksumSha256, payload.BalanceChecksumSha256))
            throw new InvalidDataException("Portable ledger balance checksum does not match its manifest.");
        return snapshot.Restore();
    }
}
