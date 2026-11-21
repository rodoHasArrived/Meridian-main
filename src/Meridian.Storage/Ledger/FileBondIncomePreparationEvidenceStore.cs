using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>Immutable scoped calculation artifacts used as the canonical income event source.</summary>
public sealed class FileBondIncomePreparationEvidenceStore(string root) : IBondIncomePreparationEvidenceStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    public async Task VerifyAsync(RetainedEvidenceIdentityDto expected, OpenLotAmortizationInstructionDto instruction,
        Guid periodId, Guid eventId, long eventVersion, CancellationToken ct = default)
    {
        const string prefix = "urn:meridian:bond-income:";
        if (!expected.EvidenceUri.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The prepared income event has no retained calculation artifact locator.");
        var parts = expected.EvidenceUri[prefix.Length..].Split(':');
        if (parts.Length != 3 || !Sha256Digest.IsCanonical(parts[0])
            || parts[1] != eventId.ToString("D") || parts[2] != eventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The prepared income event source locator is outside its exact identity.");
        var path = Path.Combine(Path.GetFullPath(root), parts[0], $"{eventId:D}-{eventVersion}.json");
        if (!File.Exists(path))
            throw new InvalidOperationException("The prepared income calculation artifact is missing.");
        var retained = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false))
            ?? throw new InvalidOperationException("The prepared income calculation artifact is unreadable.");
        var artifact = retained.Artifact;
        if (artifact.EventId != eventId || artifact.EventVersion != eventVersion || artifact.PeriodId != periodId
            || artifact.LedgerBookId != instruction.ExpectedLot.LedgerBookId || artifact.AccountId != instruction.BondIncome?.AccountId
            || artifact.CouponRecognition is not null || !OpenLotAmortization.SameInstruction(artifact.Instruction, instruction))
            throw new InvalidOperationException("The prepared income calculation source does not match the exact book, period, lot and instruction.");
        var actual = await RetainAsync(artifact, expected.RetainedBy, expected.RetainedAtUtc, true, ct).ConfigureAwait(false);
        if (actual != expected)
            throw new InvalidOperationException("The prepared income source intake proof changed after preparation.");
    }

    public async Task<RetainedEvidenceIdentityDto> RetainAsync(BondIncomePreparationArtifactDto artifact,
        string actor, DateTimeOffset preparedAtUtc, bool mustExist, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artifact.TenantId) || string.IsNullOrWhiteSpace(artifact.CompanyId)
            || string.IsNullOrWhiteSpace(actor) || artifact.AccountId == Guid.Empty || artifact.LedgerBookId == Guid.Empty
            || artifact.PeriodId == Guid.Empty || artifact.EventId == Guid.Empty || artifact.EventVersion <= 0
            || preparedAtUtc == default || preparedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Income preparation evidence requires complete authenticated scope and UTC actor provenance.");
        var scope = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        { artifact.TenantId, artifact.CompanyId, artifact.AccountId, artifact.LedgerBookId }));
        var directory = Path.Combine(Path.GetFullPath(root), scope);
        var path = Path.Combine(directory, $"{artifact.EventId:D}-{artifact.EventVersion}.json");
        var contentHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(artifact));
        var expectedFingerprint = artifact.CouponRecognition is { } coupon
            ? BondCouponRecognitionCalculator.Fingerprint(coupon)
            : Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(artifact.Instruction));
        var sourceSystem = artifact.CouponRecognition is null ? "Meridian.BondIncome" : "Meridian.BondCouponRecognition";
        if (!Sha256Digest.FixedEquals(artifact.PreviewFingerprint,
                expectedFingerprint)
            || artifact.CouponRecognition is { } selected
                && !OpenLotAmortization.SameInstruction(selected.IncomeInstruction, artifact.Instruction))
            throw new ArgumentException("The operator review must identify the exact prepared instruction fingerprint.");
        if (artifact.Instruction.ExpectedLot.LedgerBookId != artifact.LedgerBookId
            || artifact.Instruction.BondIncome?.AccountId != artifact.AccountId
            || artifact.CouponRecognition is { } scoped && (scoped.EventId != artifact.EventId
                || scoped.Scope.TenantId != artifact.TenantId || scoped.Scope.CompanyId != artifact.CompanyId
                || !Guid.TryParse(scoped.Scope.AccountId, out var couponAccount) || couponAccount != artifact.AccountId
                || !Guid.TryParse(scoped.Scope.BookId, out var couponBook) || couponBook != artifact.LedgerBookId
                || scoped.Request.PeriodId != artifact.PeriodId))
            throw new ArgumentException("Prepared calculation evidence must match its exact authoritative account/book and coupon scope.");
        var gate = Gates.GetOrAdd(path, _ => new(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            await using var lease = await AcquireFileLeaseAsync(path + ".lock", ct).ConfigureAwait(false);
            if (File.Exists(path))
            {
                var retainedJson = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                var retained = JsonSerializer.Deserialize<Envelope>(retainedJson)
                    ?? throw new InvalidOperationException("Retained income preparation evidence is unreadable.");
                using var raw = JsonDocument.Parse(retainedJson);
                var actualHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(retained.Artifact));
                if (retained.SchemaVersion != 1 || actualHash != retained.ContentSha256
                    || retained.Evidence.ContentHashSha256 != actualHash
                    || retained.OperatorFingerprintReview is not { } review
                    || review.Actor != retained.Evidence.ReviewedBy
                    || review.Actor != retained.Evidence.RetainedBy
                    || review.ReviewedAtUtc != retained.Evidence.ReviewedAtUtc
                    || review.ReviewedAtUtc != retained.Evidence.RetainedAtUtc
                    || review.PreviewFingerprint != artifact.PreviewFingerprint
                    || retained.Evidence.EffectiveDate != (artifact.CouponRecognition?.Request.ThroughDate ?? artifact.Instruction.AsOfDate)
                    || retained.Evidence.SubjectType != AssetAccountingEvidenceSubjects.Event
                    || retained.Evidence.SubjectId != artifact.EventId.ToString("D")
                    || retained.Evidence.EvidenceVersion != artifact.EventVersion
                    || retained.Evidence.SourceReference != $"{artifact.EventId:D}/{artifact.EventVersion}"
                    || retained.Evidence.SourceSystem != sourceSystem
                    || retained.Evidence.EvidenceId != $"bond-income:{artifact.EventId:D}/{artifact.EventVersion}"
                    || retained.Evidence.EvidenceUri != $"urn:meridian:bond-income:{scope}:{artifact.EventId:D}:{artifact.EventVersion}"
                    || !JsonElement.DeepEquals(raw.RootElement, JsonSerializer.SerializeToElement(retained))
                    || !RetainedEvidenceIdentityValidator.IsComplete(retained.Evidence)
                    || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(retained.Artifact), JsonSerializer.SerializeToElement(artifact)))
                    throw new InvalidOperationException("Retained income preparation evidence is changed or conflicts with this event identity.");
                return retained.Evidence;
            }
            if (mustExist)
                throw new InvalidOperationException("The prepared event's retained calculation artifact is missing; recover the evidence before proceeding.");
            var source = $"{artifact.EventId:D}/{artifact.EventVersion}";
            // The authenticated operator explicitly supplied the exact preview fingerprint.
            // Accepted describes intake of that reviewed calculation; it grants no approval,
            // posting, or independent-review authority to this actor.
            var operatorReview = new BondIncomeOperatorFingerprintReviewDto(actor, artifact.PreviewFingerprint, preparedAtUtc);
            var evidence = new RetainedEvidenceIdentityDto("bond-income:" + source,
                $"urn:meridian:bond-income:{scope}:{artifact.EventId:D}:{artifact.EventVersion}", contentHash,
                sourceSystem, source, RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
                actor, preparedAtUtc, artifact.CouponRecognition?.Request.ThroughDate ?? artifact.Instruction.AsOfDate, artifact.EventVersion,
                preparedAtUtc, actor, AssetAccountingEvidenceSubjects.Event, artifact.EventId.ToString("D"));
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(new Envelope(1, artifact, contentHash, evidence, operatorReview)), ct).ConfigureAwait(false);
            return evidence;
        }
        finally { gate.Release(); }
    }

    private static async Task<FileStream> AcquireFileLeaseAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 599) { await Task.Delay(50, ct).ConfigureAwait(false); }
        }
        throw new IOException("Income preparation evidence is busy.");
    }

    private sealed record Envelope(int SchemaVersion, BondIncomePreparationArtifactDto Artifact,
        string ContentSha256, RetainedEvidenceIdentityDto Evidence,
        BondIncomeOperatorFingerprintReviewDto OperatorFingerprintReview);
}
