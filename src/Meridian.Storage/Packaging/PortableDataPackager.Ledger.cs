using System.IO.Compression;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Packaging;

public sealed partial class PortableDataPackager
{
    /// <summary>Reads the verified ledger in a portable package without posting it into an
    /// existing book. The host controls the separate, scoped import/review workflow.</summary>
    public async Task<Meridian.Ledger.IReadOnlyLedger?> ReadLedgerAsync(string packagePath, CancellationToken ct = default)
    {
        var manifest = await ReadManifestFromPackageAsync(packagePath, PackageFormat.Zip, ct)
            ?? throw new InvalidDataException("Package manifest is missing.");
        return await ValidatePortableLedgerAsync(packagePath, manifest, ct);
    }

    private static async Task<Meridian.Ledger.IReadOnlyLedger?> ValidatePortableLedgerAsync(
        string packagePath, PackageManifest manifest, CancellationToken ct)
    {
        using var stream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (manifest.LedgerSnapshotPath is null)
        {
            if (manifest.LedgerSnapshotSha256 is not null || manifest.LedgerBalanceChecksumSha256 is not null ||
                archive.GetEntry("metadata/ledger.json") is not null)
                throw new InvalidDataException("Portable ledger snapshot has no manifest authority.");
            return null;
        }
        if (manifest.LedgerSnapshotPath != "metadata/ledger.json" ||
            archive.Entries.Count(entry => entry.FullName == manifest.LedgerSnapshotPath) != 1 ||
            manifest.LedgerSnapshotSha256 is null || manifest.LedgerBalanceChecksumSha256 is null)
            throw new InvalidDataException("Portable ledger manifest is incomplete or ambiguous.");
        var entry = archive.GetEntry(manifest.LedgerSnapshotPath)!;
        await using (var contents = entry.Open())
        {
            var hash = await Sha256Digest.ComputeAsync(contents, ct);
            if (!Sha256Digest.FixedEquals(hash, manifest.LedgerSnapshotSha256))
                throw new InvalidDataException("Portable ledger file checksum mismatch.");
        }
        await using var payload = entry.Open();
        var snapshot = await PortableLedgerSnapshot.ReadAsync(payload, ct);
        if (!Sha256Digest.FixedEquals(snapshot.BalanceChecksumSha256, manifest.LedgerBalanceChecksumSha256))
            throw new InvalidDataException("Portable ledger balance checksum does not match its manifest.");
        return snapshot.Restore();
    }
}
