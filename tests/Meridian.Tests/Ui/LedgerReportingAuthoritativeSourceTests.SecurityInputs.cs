using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class LedgerReportingAuthoritativeSourceTests
{
    [Fact]
    public async Task Capture_RetainsExactReceiptInputsAcrossRestartWithoutReadingCurrentSecurity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "report-security-receipt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var receipt = ReportingSecurityInputTestData.Receipt();
            await new FileSecurityCashFlowReceiptStore(directory).RetainAsync(receipt);
            var fixture = CreateFixture(receiptStore: new FileSecurityCashFlowReceiptStore(directory));
            var original = IncomeAccrualRecord(fixture);
            var record = original with { Entry = ReportingSecurityInputTestData.WithReceipt(original.Entry, receipt) };
            fixture.JournalStore.Records.Add(record);

            var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
            var restored = JsonSerializer.Deserialize<ReportingAuthoritativeSourceCheckpoint>(JsonSerializer.Serialize(capture.Checkpoint))!;
            Directory.Delete(directory, recursive: true);

            ReportingRetainedLedgerPopulationValidation.Validate(restored, capture.DatasetRows);
            ReportingLedgerPopulationSnapshot.Decode(restored).Journals.Should().ContainSingle();
            var input = restored.SecurityInputs!.Bindings.Should().ContainSingle().Which;
            using var json = JsonDocument.Parse(input.ReceiptPayloadJson);
            json.RootElement.GetProperty("Security").GetProperty("AssetSpecificTerms").GetProperty("couponRate").GetDecimal().Should().Be(5.25m);
            input.SecurityId.Should().Be(receipt.Security.SecurityId);
            input.ReceiptSha256.Should().Be(receipt.ContentSha256);
            restored.SecurityInputs.Gaps.Should().BeEmpty();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Capture_ContradictoryJournalReceiptLinksRemainExplicitGaps()
    {
        var receipt = ReportingSecurityInputTestData.Receipt();
        var receipts = Substitute.For<ISecurityCashFlowReceiptStore>();
        var fixture = CreateFixture(receiptStore: receipts);
        var original = IncomeAccrualRecord(fixture);
        var linked = ReportingSecurityInputTestData.WithReceipt(original.Entry, receipt);
        var conflicting = new JournalEntry(linked.JournalEntryId, linked.Timestamp, linked.Description, linked.Lines,
            linked.Metadata with { EvidenceReferences = [linked.Metadata.EvidenceReferences[0] with { ContentHash = new string('a', 64) }] });
        fixture.JournalStore.Records.Add(original with { Entry = conflicting });

        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        capture.Checkpoint.SecurityInputs!.Bindings.Should().BeEmpty();
        capture.Checkpoint.SecurityInputs.Gaps.Should().ContainSingle().Which.Reason.Should().Contain("unambiguous");
        await receipts.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
        ReportingRetainedLedgerPopulationValidation.Validate(capture.Checkpoint, capture.DatasetRows);
    }

    [Fact]
    public async Task RetainedInputTamperingAndOmittedBindingsFailCheckpointValidation()
    {
        var receipt = ReportingSecurityInputTestData.Receipt();
        var receipts = Substitute.For<ISecurityCashFlowReceiptStore>();
        receipts.GetAsync(receipt.ContentSha256, Arg.Any<CancellationToken>()).Returns(receipt);
        var fixture = CreateFixture(receiptStore: receipts);
        var original = IncomeAccrualRecord(fixture);
        fixture.JournalStore.Records.Add(original with { Entry = ReportingSecurityInputTestData.WithReceipt(original.Entry, receipt) });
        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var inputs = capture.Checkpoint.SecurityInputs!;
        var changed = inputs with { Bindings = [inputs.Bindings[0] with { ReceiptPayloadJson = inputs.Bindings[0].ReceiptPayloadJson.Replace("5.25", "5.75", StringComparison.Ordinal) }] };
        changed = changed with { ContentHashSha256 = ReportingSecurityInputEvidence.ComputeHash(changed) };
        Action tamper = () => ReportingRetainedLedgerPopulationValidation.Validate(capture.Checkpoint with { SecurityInputs = changed }, capture.DatasetRows);
        tamper.Should().Throw<InvalidDataException>();
        var omitted = inputs with { Bindings = [] };
        omitted = omitted with { ContentHashSha256 = ReportingSecurityInputEvidence.ComputeHash(omitted) };
        Action omit = () => ReportingRetainedLedgerPopulationValidation.Validate(capture.Checkpoint with { SecurityInputs = omitted }, capture.DatasetRows);
        omit.Should().Throw<InvalidDataException>();
    }
}
