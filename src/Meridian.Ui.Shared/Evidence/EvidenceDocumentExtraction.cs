using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public interface IEvidenceDocumentExtractor
{
    Task<EvidenceDocumentExtractionResultDto> ExtractAsync(
        EvidenceDocumentExtractionRequestDto request,
        CancellationToken ct = default);
}

public sealed class ManualEvidenceDocumentExtractor : IEvidenceDocumentExtractor
{
    public const string ExtractorId = "manual-metadata-v1";
    public const string FixtureExtractorId = "deterministic-fixture-v1";

    public Task<EvidenceDocumentExtractionResultDto> ExtractAsync(
        EvidenceDocumentExtractionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var fields = request.ManualFields.Count == 0
            ? BuildDeterministicFixtureFields(request)
            : request.ManualFields.ToArray();
        var status = ResolveStatus(fields);
        var extractorId = request.ManualFields.Count == 0 && fields.Length > 0
            ? FixtureExtractorId
            : ExtractorId;
        var summary = BuildSummary(request.ManualFields.Count, fields.Length, extractorId);
        return Task.FromResult(new EvidenceDocumentExtractionResultDto(status, fields, extractorId, summary));
    }

    private static EvidenceArtifactExtractionFieldDto[] BuildDeterministicFixtureFields(
        EvidenceDocumentExtractionRequestDto request)
    {
        if (!IsFixtureRequest(request))
        {
            return [];
        }

        var key = string.Join(
            ' ',
            request.FileName,
            request.ContentType,
            request.IntakeChannel,
            request.SourceSystem,
            request.SourceReference).ToLowerInvariant();
        if (key.Contains("capital-notice", StringComparison.Ordinal) ||
            key.Contains("capital notice", StringComparison.Ordinal))
        {
            return
            [
                ReadyField(
                    "noticeDate",
                    "2026-06-15",
                    "capital-notice",
                    request.SourceReference,
                    "Fixture capital notice date matched the deterministic intake profile."),
                ReadyField(
                    "capitalCallAmount",
                    "125000.00",
                    "capital-notice",
                    request.SourceReference,
                    "Fixture capital call amount matched the deterministic intake profile.")
            ];
        }

        if (key.Contains("custodian", StringComparison.Ordinal))
        {
            return
            [
                ReadyField(
                    "accountId",
                    "fund-alpha-cash",
                    "account",
                    request.SourceReference,
                    "Fixture custodian account matched the deterministic intake profile."),
                ReadyField(
                    "endingCash",
                    "1024.50",
                    "account",
                    request.SourceReference,
                    "Fixture custodian ending cash matched the deterministic intake profile.")
            ];
        }

        if (key.Contains("invoice", StringComparison.Ordinal))
        {
            return
            [
                ReadyField(
                    "invoiceNumber",
                    "INV-2026-0001",
                    "invoice",
                    request.SourceReference,
                    "Fixture invoice number matched the deterministic intake profile."),
                ReadyField(
                    "invoiceAmount",
                    "2500.00",
                    "invoice",
                    request.SourceReference,
                    "Fixture invoice amount matched the deterministic intake profile.")
            ];
        }

        return
        [
            ReadyField(
                "statementDate",
                "2026-06-30",
                "period",
                request.SourceReference,
                "Fixture statement date matched the deterministic intake profile."),
            ReadyField(
                "endingCash",
                "1024.50",
                "account",
                request.SourceReference,
                "Fixture ending cash matched the deterministic intake profile.")
        ];
    }

    private static bool IsFixtureRequest(EvidenceDocumentExtractionRequestDto request)
    {
        return ContainsFixtureToken(request.IntakeChannel) ||
            ContainsFixtureToken(request.SourceSystem) ||
            ContainsFixtureToken(request.SourceReference) ||
            ContainsFixtureToken(request.FileName);
    }

    private static bool ContainsFixtureToken(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            (value.Contains("fixture", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("demo", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("sample", StringComparison.OrdinalIgnoreCase));
    }

    private static EvidenceArtifactExtractionFieldDto ReadyField(
        string fieldName,
        string value,
        string linkedRecordKind,
        string? linkedRecordId,
        string validationMessage)
        => new(
            fieldName,
            value,
            value,
            1.0m,
            "Fixture",
            EvidenceStatusDto.Ready,
            validationMessage,
            linkedRecordKind,
            string.IsNullOrWhiteSpace(linkedRecordId) ? null : linkedRecordId);

    private static string BuildSummary(int manualFieldCount, int fieldCount, string extractorId)
    {
        if (fieldCount == 0)
        {
            return "No deterministic extraction fields were supplied.";
        }

        return extractorId == FixtureExtractorId
            ? $"Deterministic fixture extraction supplied {fieldCount} field(s)."
            : $"Manual metadata supplied {manualFieldCount} deterministic extraction field(s).";
    }

    private static EvidenceExtractionStatusDto ResolveStatus(
        IReadOnlyCollection<EvidenceArtifactExtractionFieldDto> fields)
    {
        if (fields.Count == 0)
        {
            return EvidenceExtractionStatusDto.NotExtracted;
        }

        if (fields.Any(static field =>
                field.ValidationStatus is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing))
        {
            return EvidenceExtractionStatusDto.NeedsReview;
        }

        if (fields.Any(static field =>
                field.ValidationStatus is EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale or EvidenceStatusDto.Unknown))
        {
            return EvidenceExtractionStatusDto.NeedsReview;
        }

        return EvidenceExtractionStatusDto.Extracted;
    }
}
