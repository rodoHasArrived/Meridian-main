using System.IO.Compression;
using System.Text;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Packaging;

public sealed partial class PortableDataPackager
{
    /// <summary>Reads opaque accounting evidence only after the configured adapter verifies it.
    /// This operation grants no posting authority.</summary>
    public async Task<PortableLedgerPayload?> ReadLedgerPayloadAsync(string packagePath, CancellationToken ct = default)
    {
        var manifest = await ReadManifestFromPackageAsync(packagePath, PackageFormat.Zip, ct)
            ?? throw new InvalidDataException("Package manifest is missing.");
        return await ValidatePortableLedgerAsync(packagePath, manifest, ct);
    }

    private async Task<PortableLedgerPayload?> ValidatePortableLedgerAsync(
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
        await using var payloadStream = entry.Open();
        // Preserve the exact JSON text that was hashed; malformed UTF-8 must not be silently
        // replaced with different journal evidence during decoding.
        using var reader = new StreamReader(payloadStream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var payload = new PortableLedgerPayload(await reader.ReadToEndAsync(ct), manifest.LedgerBalanceChecksumSha256);
        await ValidateLedgerPayloadAsync(payload, ct);
        return payload;
    }

    private Task ValidateLedgerPayloadAsync(PortableLedgerPayload payload, CancellationToken ct)
    {
        if (_ledgerValidator is null)
            throw new InvalidOperationException("Portable ledger accounting validation is not configured.");
        if (string.IsNullOrWhiteSpace(payload.Json) || !Sha256Digest.IsCanonical(payload.BalanceChecksumSha256))
            throw new InvalidDataException("Portable ledger payload or balance checksum is invalid.");
        return _ledgerValidator.ValidateAsync(payload, ct);
    }

    // Both JSON authorities must survive extraction under the same names that were verified.
    // Normalize portable path spellings conservatively for Windows as well as Linux consumers.
    private static void ValidateReservedEntryPaths(ZipArchive archive)
    {
        foreach (var reserved in new[] { ManifestFileName, "metadata/ledger.json" })
        {
            var count = 0;
            foreach (var entry in archive.Entries)
            {
                var parts = new List<string>();
                foreach (var part in entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    var segment = part.TrimEnd(' ');
                    if (segment == ".")
                        continue;
                    if (segment == "..")
                    {
                        if (parts.Count > 0 && parts[^1] != "..")
                            parts.RemoveAt(parts.Count - 1);
                        else
                            parts.Add(segment);
                        continue;
                    }
                    // NTFS stream suffixes and trailing dots/spaces can target the same file.
                    var streamIndex = segment.IndexOf(':');
                    if (streamIndex >= 0)
                        segment = segment[..streamIndex];
                    segment = segment.TrimEnd(' ', '.');
                    if (segment.Length > 0)
                        parts.Add(segment);
                }
                if (!string.Equals(string.Join('/', parts), reserved, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(entry.FullName, reserved, StringComparison.Ordinal) || ++count > 1)
                    throw new InvalidDataException($"Package contains duplicate or aliased reserved entry '{reserved}'.");
            }
        }
    }
}
