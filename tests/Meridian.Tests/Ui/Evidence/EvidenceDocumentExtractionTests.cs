using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Xunit;

namespace Meridian.Tests.Ui.Evidence;

/// <summary>
/// Guards the manual evidence-document extractor behind statement/notice intake. The
/// failure mode under guard: extraction results reporting Extracted when a field
/// actually needs review, or fixture-mode extraction leaking into real (non-fixture)
/// intake — either would let unverified document metadata flow into evidence packets.
/// </summary>
public sealed class EvidenceDocumentExtractionTests
{
    private readonly ManualEvidenceDocumentExtractor _extractor = new();

    [Fact]
    public async Task ExtractAsync_WithManualFields_ReturnsManualExtractorAndExtractedStatus()
    {
        var fields = new[] { ReadyField("statementDate"), ReadyField("endingCash") };
        var request = MakeRequest(fileName: "statement.pdf", manualFields: fields);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await _extractor.ExtractAsync(request, cts.Token);

        result.Status.Should().Be(EvidenceExtractionStatusDto.Extracted);
        result.ExtractorId.Should().Be(ManualEvidenceDocumentExtractor.ExtractorId);
        result.Fields.Should().BeEquivalentTo(fields);
        result.Summary.Should().Be("Manual metadata supplied 2 deterministic extraction field(s).");
    }

    [Theory]
    [InlineData(EvidenceStatusDto.Blocked)]
    [InlineData(EvidenceStatusDto.Missing)]
    [InlineData(EvidenceStatusDto.ReviewRequired)]
    [InlineData(EvidenceStatusDto.Stale)]
    [InlineData(EvidenceStatusDto.Unknown)]
    public async Task ExtractAsync_WithManualFieldNeedingReview_ReturnsNeedsReview(EvidenceStatusDto fieldStatus)
    {
        var request = MakeRequest(
            fileName: "statement.pdf",
            manualFields: new[] { ReadyField("statementDate"), Field("endingCash", fieldStatus) });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await _extractor.ExtractAsync(request, cts.Token);

        result.Status.Should().Be(EvidenceExtractionStatusDto.NeedsReview,
            "one unvalidated field must force the whole extraction into review");
    }

    [Fact]
    public async Task ExtractAsync_NonFixtureRequestWithoutManualFields_ReturnsNotExtracted()
    {
        var request = MakeRequest(fileName: "statement.pdf");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await _extractor.ExtractAsync(request, cts.Token);

        result.Status.Should().Be(EvidenceExtractionStatusDto.NotExtracted);
        result.Fields.Should().BeEmpty("real (non-fixture) intake must never receive fabricated fields");
        result.Summary.Should().Be("No deterministic extraction fields were supplied.");
    }

    [Theory]
    [InlineData("fixture-capital-notice.pdf", "noticeDate", "capitalCallAmount")]
    [InlineData("demo-custodian-file.csv", "accountId", "endingCash")]
    [InlineData("sample-invoice.pdf", "invoiceNumber", "invoiceAmount")]
    [InlineData("fixture-statement.pdf", "statementDate", "endingCash")]
    public async Task ExtractAsync_FixtureRequests_ReturnDeterministicProfileFields(
        string fileName, string expectedField1, string expectedField2)
    {
        var request = MakeRequest(fileName: fileName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await _extractor.ExtractAsync(request, cts.Token);

        result.Status.Should().Be(EvidenceExtractionStatusDto.Extracted);
        result.ExtractorId.Should().Be(ManualEvidenceDocumentExtractor.FixtureExtractorId);
        result.Fields.Select(f => f.FieldName).Should().Equal(expectedField1, expectedField2);
        result.Fields.Should().OnlyContain(f => f.ReviewState == "Fixture" && f.ConfidenceScore == 1.0m);
    }

    [Fact]
    public async Task ExtractAsync_FixtureTokenInSourceSystem_TriggersFixtureExtraction()
    {
        var request = MakeRequest(fileName: "statement.pdf", sourceSystem: "demo-bank");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await _extractor.ExtractAsync(request, cts.Token);

        result.ExtractorId.Should().Be(ManualEvidenceDocumentExtractor.FixtureExtractorId,
            "fixture tokens are detected in SourceSystem as well as the file name");
    }

    [Fact]
    public async Task ExtractAsync_WithCancelledTokenOrNullRequest_Throws()
    {
        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cancelled.Cancel();

        var cancelledAct = () => _extractor.ExtractAsync(MakeRequest("statement.pdf"), cancelled.Token);
        var nullAct = () => _extractor.ExtractAsync(null!);

        await cancelledAct.Should().ThrowAsync<OperationCanceledException>();
        await nullAct.Should().ThrowAsync<ArgumentNullException>();
    }

    private static EvidenceDocumentExtractionRequestDto MakeRequest(
        string fileName,
        string? sourceSystem = null,
        IReadOnlyList<EvidenceArtifactExtractionFieldDto>? manualFields = null) =>
        new(
            FileName: fileName,
            ContentType: "application/pdf",
            IntakeChannel: "upload",
            SourceSystem: sourceSystem,
            SourceReference: null,
            ManualFields: manualFields ?? Array.Empty<EvidenceArtifactExtractionFieldDto>());

    private static EvidenceArtifactExtractionFieldDto ReadyField(string name) =>
        Field(name, EvidenceStatusDto.Ready);

    private static EvidenceArtifactExtractionFieldDto Field(string name, EvidenceStatusDto status) =>
        new(
            FieldName: name,
            ExtractedValue: "2026-06-30",
            ExpectedValue: "2026-06-30",
            ConfidenceScore: 1.0m,
            ReviewState: "Reviewed",
            ValidationStatus: status,
            ValidationMessage: null,
            LinkedRecordKind: null,
            LinkedRecordId: null);
}
