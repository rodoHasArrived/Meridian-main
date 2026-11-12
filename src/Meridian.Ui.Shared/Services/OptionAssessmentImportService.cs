using System.Text.Json;
using Meridian.Contracts.Options;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Retains imported broker claims and their exact source bytes. No broker or OMS is called.</summary>
public sealed class OptionAssessmentImportService(string dataRoot, IEvidenceArtifactStore evidence, TimeProvider clock)
{
    public const int MaxSourceBytes = 8 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public OptionAssessmentImportPreview Preview(OptionAssessmentImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = OptionAssessmentValidation.Normalize(request.Assessment, request.LegsCsv);
        if (normalized.AssessedAt > clock.GetUtcNow())
            throw new ArgumentException("Broker assessment time cannot be in the future.");
        var source = Source(request);
        if (request.CorrectsAssessmentId is { } correction && !IsId(correction))
            throw new ArgumentException("Correction identity is invalid.");
        var sourceHash = OptionAssessmentValidation.Digest(source);
        var fingerprint = OptionAssessmentValidation.Digest(OptionAssessmentValidation.CanonicalBytes(new
        {
            assessment = normalized,
            sourceHash,
            request.SourceFileName,
            request.SourceContentType,
            request.CorrectsAssessmentId
        }));
        return new(normalized, OptionAssessmentValidation.BasketFingerprint(normalized), fingerprint, sourceHash,
            source.LongLength, request.CorrectsAssessmentId,
            ["Imported broker claim as of the stated assessment time; source retention does not authenticate the broker's signature or establish current acceptance.",
             "Permission and restriction statements are separately retained observations. No cash or stock is reserved and no executable order is submitted."]);
    }

    public async Task<RetainedOptionAssessment> AcceptAsync(string tenant, string company, string actor,
        Guid accountId, AcceptOptionAssessmentImportRequest command, CancellationToken ct = default)
    {
        ValidateScope(tenant, company, actor);
        if (command.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("A stable idempotency key is required.");
        var preview = Preview(command.Import);
        if (preview.Assessment.FundAccountId != accountId)
            throw new ArgumentException("The assessment belongs to a different fund account.");
        if (!string.Equals(preview.PreviewFingerprint, command.PreviewFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The reviewed preview no longer matches these inputs. Preview again.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = DirectoryFor(tenant, company, accountId);
            Directory.CreateDirectory(directory);
            // Prevent independent hosts sharing a data root from accepting conflicting corrections.
            await using var lease = new FileStream(Path.Combine(directory, ".import-lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var id = command.IdempotencyKey.ToString("N");
            var path = Path.Combine(directory, id + ".json");
            if (File.Exists(path))
            {
                var previous = await ReadReceipt(path, ct).ConfigureAwait(false);
                if (previous.Record.Assessment.FundAccountId != accountId)
                    throw new InvalidDataException("Retained assessment account is inconsistent.");
                if (previous.Actor != actor || previous.PreviewFingerprint != preview.PreviewFingerprint)
                    throw new InvalidOperationException("The idempotency key is already bound to a different actor or import.");
                await Verify(previous, tenant, company, ct).ConfigureAwait(false);
                return previous.Record;
            }
            var retained = await ReadReceipts(directory, ct).ConfigureAwait(false);
            if (preview.CorrectsAssessmentId is { } priorId)
            {
                var prior = retained.SingleOrDefault(item => item.Record.AssessmentId == priorId)
                    ?? throw new InvalidOperationException("The assessment being corrected is unavailable in this account.");
                if (retained.Any(item => item.Record.CorrectsAssessmentId == priorId))
                    throw new InvalidOperationException("That assessment was already corrected. Select its current correction.");
                await Verify(prior, tenant, company, ct).ConfigureAwait(false);
                if (prior.Record.Assessment.ProviderId != preview.Assessment.ProviderId
                    || prior.Record.Assessment.ExternalAccountId != preview.Assessment.ExternalAccountId)
                    throw new InvalidOperationException("A correction cannot move a broker claim to another broker account.");
            }
            var source = Source(command.Import);
            var original = await Retain(command.Import.SourceFileName, command.Import.SourceContentType, source,
                preview.Assessment, tenant, company, actor, "broker-option-assessment-source", ct).ConfigureAwait(false);
            var canonical = OptionAssessmentValidation.CanonicalBytes(preview.Assessment);
            var assessmentDocument = await Retain($"option-assessment-{id}.json", "application/json", canonical,
                preview.Assessment, tenant, company, actor, "broker-option-assessment", ct).ConfigureAwait(false);
            var record = new RetainedOptionAssessment(id, preview.Assessment, preview.BasketFingerprint,
                OptionAssessmentValidation.Digest(canonical), clock.GetUtcNow(), actor,
                original.VaultIdentity.VaultId, DocumentId(original), preview.SourceSha256,
                assessmentDocument.VaultIdentity.VaultId, DocumentId(assessmentDocument), preview.CorrectsAssessmentId);
            var receipt = new ImportReceipt(tenant, company, actor, preview.PreviewFingerprint, record);
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(receipt, Json), ct).ConfigureAwait(false);
            return record;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<RetainedOptionAssessment>> ListAsync(string tenant, string company, Guid account,
        CancellationToken ct = default)
    {
        ValidateScope(tenant, company, "read");
        var receipts = await ReadReceipts(DirectoryFor(tenant, company, account), ct).ConfigureAwait(false);
        foreach (var receipt in receipts)
        {
            if (receipt.Record.Assessment.FundAccountId != account)
                throw new InvalidDataException("Retained assessment account is inconsistent.");
            await Verify(receipt, tenant, company, ct).ConfigureAwait(false);
        }
        var corrected = receipts.Select(item => item.Record.CorrectsAssessmentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return receipts.Select(item => item.Record with { IsSuperseded = corrected.Contains(item.Record.AssessmentId) })
            .OrderByDescending(item => item.RecordedAt).ToArray();
    }

    private async Task Verify(ImportReceipt receipt, string tenant, string company, CancellationToken ct)
    {
        if (receipt.Tenant != tenant || receipt.Company != company)
            throw new InvalidDataException("Retained assessment scope is inconsistent.");
        var row = receipt.Record;
        if (OptionAssessmentValidation.BasketFingerprint(row.Assessment) != row.BasketFingerprint
            || OptionAssessmentValidation.Digest(OptionAssessmentValidation.CanonicalBytes(row.Assessment)) != row.ContentSha256)
            throw new InvalidDataException("Retained assessment fingerprint is inconsistent.");
        await VerifyDocument(row.SourceVaultId, row.SourceDocumentId, row.SourceSha256, tenant, company, ct).ConfigureAwait(false);
        await VerifyDocument(row.AssessmentVaultId, row.AssessmentDocumentId, row.ContentSha256, tenant, company, ct).ConfigureAwait(false);
    }

    private async Task VerifyDocument(string vault, string document, string expected, string tenant, string company, CancellationToken ct)
    {
        if (!await evidence.VerifyRetainedContentAsync(vault, tenant, company, ct).ConfigureAwait(false))
            throw new InvalidDataException("Retained broker assessment evidence is unavailable or failed verification.");
        var content = await evidence.TryOpenDocumentContentAsync(vault, document, tenant, company, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Retained broker assessment source document is unavailable.");
        await using var stream = content.Content;
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, ct).ConfigureAwait(false);
        if (OptionAssessmentValidation.Digest(bytes.ToArray()) != expected)
            throw new InvalidDataException("Retained broker assessment source bytes do not match their digest.");
    }

    private async Task<EvidenceVaultIntakeResponseDto> Retain(string file, string contentType, byte[] bytes,
        BrokerOptionAssessment assessment, string tenant, string company, string actor, string kind, CancellationToken ct)
    {
        return await evidence.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
            "account", assessment.FundAccountId.ToString(), "Api", file, Convert.ToBase64String(bytes), contentType,
            assessment.ProviderId, assessment.BrokerReference, actor, OptionAssessmentValidation.Digest(bytes),
            null,
            new($"option-assessment:{assessment.FundAccountId}", null, null, null, null))
        { TenantId = tenant, Scope = company, Actor = actor }, ct).ConfigureAwait(false);
    }

    private static string DocumentId(EvidenceVaultIntakeResponseDto intake) =>
        intake.Document?.DocumentId ?? intake.VaultIdentity.Documents.FirstOrDefault()?.DocumentId
        ?? throw new InvalidDataException("Evidence intake did not return a retained document identity.");

    private static byte[] Source(OptionAssessmentImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFileName) || request.SourceFileName.Length > 200
            || request.SourceFileName.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A source document file name without a path is required.");
        if (string.IsNullOrWhiteSpace(request.SourceContentType) || request.SourceContentType.Length > 128)
            throw new ArgumentException("Source content type is required.");
        if (string.IsNullOrWhiteSpace(request.SourceContentBase64) || request.SourceContentBase64.Length > MaxSourceBytes * 4 / 3 + 4)
            throw new ArgumentException("Attach a broker source document of at most 8 MiB.");
        byte[] source;
        try
        { source = Convert.FromBase64String(request.SourceContentBase64); }
        catch (FormatException) { throw new ArgumentException("Source document encoding is invalid."); }
        if (source.Length is < 1 or > MaxSourceBytes)
            throw new ArgumentException("Attach a broker source document of 1 byte to 8 MiB.");
        return source;
    }

    private string DirectoryFor(string tenant, string company, Guid account) => Path.Combine(dataRoot, "workstation", "option-assessments",
        OptionAssessmentValidation.Digest(OptionAssessmentValidation.CanonicalBytes(new { tenant, company })), account.ToString("N"));
    private static bool IsId(string value) => value.Length == 32 && value.All(char.IsAsciiHexDigit);
    private static void ValidateScope(string tenant, string company, string actor)
    { if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Authenticated tenant, company and actor are required."); }
    private static async Task<List<ImportReceipt>> ReadReceipts(string directory, CancellationToken ct)
    {
        var records = new List<ImportReceipt>();
        if (!Directory.Exists(directory))
            return records;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
            records.Add(await ReadReceipt(path, ct).ConfigureAwait(false));
        return records;
    }
    private static async Task<ImportReceipt> ReadReceipt(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ImportReceipt>(stream, Json, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Retained option assessment receipt is invalid; no empty history is substituted.");
    }
    private sealed record ImportReceipt(string Tenant, string Company, string Actor, string PreviewFingerprint, RetainedOptionAssessment Record);
}
