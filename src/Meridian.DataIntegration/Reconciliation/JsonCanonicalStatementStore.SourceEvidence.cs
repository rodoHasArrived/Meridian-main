using Meridian.Contracts.Integrity;
using Meridian.Domain.Reconciliation;

namespace Meridian.DataIntegration.Reconciliation;

public sealed partial class JsonCanonicalStatementStore
{
    private const long MaximumEvidenceBytes = 32L * 1024 * 1024;

    public async Task<bool> TrySaveImportWithSourceAsync(
        CanonicalStatementImport import,
        IReadOnlyList<CanonicalStatementRow> rows,
        ReadOnlyMemory<byte> source,
        ReadOnlyMemory<byte> parserInput,
        CancellationToken ct = default)
    {
        await RetainSourceAsync(import.SourceFileHash, source, ct).ConfigureAwait(false);
        if (!string.Equals(import.SourceFileHash, import.CanonicalArtifactHash, StringComparison.OrdinalIgnoreCase))
            await RetainSourceAsync(import.CanonicalArtifactHash, parserInput, ct).ConfigureAwait(false);
        else if (!source.Span.SequenceEqual(parserInput.Span))
            throw new InvalidDataException("Statement source and parser input assert one hash for different bytes.");

        return await PublishImportAsync(import, rows, sourceEvidenceVersion: 1, ct).ConfigureAwait(false);
    }

    private async Task RetainSourceAsync(string hash, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var path = SourceEvidencePath(hash);
        if (content.Length > MaximumEvidenceBytes || !Sha256Digest.FixedEquals(Sha256Digest.Compute(content.Span), hash))
            throw new InvalidDataException("Statement source evidence exceeds its byte bound or does not match its asserted hash.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var claim = await AcquirePublicationClaimAsync(path, ct).ConfigureAwait(false);
        if (!File.Exists(path))
            await _atomicFileWriter.WriteAsync(path, content.ToArray(), ct).ConfigureAwait(false);
        // Do not trust an earlier writer or an injected persistence adapter merely because it returned.
        await ReadSourceAsync(hash, ct).ConfigureAwait(false);
        // A prior writer may have stopped after replacement but before directory durability. Reusing
        // the verified bytes still requires syncing their publication before an import can be complete.
        await _atomicFileWriter.SyncDirectoryAsync(Path.GetDirectoryName(path)!, CancellationToken.None).ConfigureAwait(false);
    }

    private string SourceEvidencePath(string hash)
    {
        if (hash.Length != 64 || hash.Any(static character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Statement source evidence requires a SHA-256 identity.");
        return Path.Combine(_folder, "source-evidence", hash.ToLowerInvariant() + ".bin");
    }

    private async Task<byte[]> ReadSourceAsync(string hash, CancellationToken ct)
    {
        var path = SourceEvidencePath(hash);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumEvidenceBytes)
            throw new InvalidDataException("Retained statement source exceeds its byte bound.");
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            if (output.Length + read > MaximumEvidenceBytes)
                throw new InvalidDataException("Retained statement source exceeds its byte bound.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        var content = output.ToArray();
        if (!Sha256Digest.FixedEquals(Sha256Digest.Compute(content), hash))
            throw new InvalidDataException("Retained statement source failed its SHA-256 verification.");
        return content;
    }

    public async Task<CanonicalStatementSourceEvidence> ReadSourceEvidenceAsync(string importId, CancellationToken ct = default)
    {
        var retained = await GetImportAsync(importId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("The statement import does not exist.");
        var source = await ReadSourceAsync(retained.Import.SourceFileHash, ct).ConfigureAwait(false);
        var parserInput = string.Equals(retained.Import.SourceFileHash, retained.Import.CanonicalArtifactHash, StringComparison.OrdinalIgnoreCase)
            ? source
            : await ReadSourceAsync(retained.Import.CanonicalArtifactHash, ct).ConfigureAwait(false);
        return new(source, parserInput);
    }

    private async Task VerifySourceEvidenceAsync(ImportEnvelope envelope, CancellationToken ct)
    {
        if (envelope.import is null)
            throw new InvalidDataException("Canonical statement import retained a null import payload.");
        if (envelope.sourceEvidenceVersion == 0)
            return; // Legacy envelopes retain their existing schema; replay does not invent missing bytes.
        if (envelope.sourceEvidenceVersion != 1)
            throw new InvalidDataException("Canonical statement import uses an unsupported source evidence version.");
        if (envelope.rows is null || envelope.rows.Count != envelope.import.NormalizedRowCount ||
            envelope.rows.Any(static row => row is null))
            throw new InvalidDataException("Canonical statement import rows are missing or do not match the retained row count.");
        await ReadSourceAsync(envelope.import.SourceFileHash, ct).ConfigureAwait(false);
        if (!string.Equals(envelope.import.SourceFileHash, envelope.import.CanonicalArtifactHash, StringComparison.OrdinalIgnoreCase))
            await ReadSourceAsync(envelope.import.CanonicalArtifactHash, ct).ConfigureAwait(false);
    }

}
