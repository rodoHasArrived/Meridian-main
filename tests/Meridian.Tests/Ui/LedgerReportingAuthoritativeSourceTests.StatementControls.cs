using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class LedgerReportingAuthoritativeSourceTests
{
    [Fact]
    public async Task FinalStatementControlsBindRetainedCloseAndRefuseChangedCurrentJournalPopulation()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with { AsOfDate = fixture.Period.EndDate, OutputFormat = ReportingOutputFormatDto.Xlsx };
        var record = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 1);
        fixture.JournalStore.Records.Add(record);
        fixture.JournalStore.RetainedClose = LedgerPeriodCloseSnapshot.Capture(fixture.Period,
            new PeriodCloseEventRecord(Guid.NewGuid(), fixture.Period.PeriodId, "SoftClosed", "HardClosed", "controller",
                "Closed period", fixture.Period.ClosedAt!.Value), fixture.JournalStore.Records);
        var intent = new ReportingAuthoritativeSourceCaptureIntent(DefaultReportingTemplateCatalog.FinancialStatementControlTemplateId);

        var result = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);

        var pack = result.CertifiedLedgerPresentation!.ReportPack;
        pack.StatementControl!.Definition.CloseSnapshotHashSha256.Should().Be(fixture.JournalStore.RetainedClose.ContentHashSha256);
        pack.StatementControl.Rows.Single(row => row.RowKey == "equity").Amount.Should().Be(125m);
        var retained = ReportingLedgerPopulationSnapshot.Decode(result.Checkpoint);
        var retainedHash = retained.Replay().Signature.PayloadChecksumSha256;
        retainedHash.Should().Be(pack.Signature.PayloadChecksumSha256);
        retained.ReportAmounts.Should().Contain(amount => amount.Label.StartsWith("Trial Balance /", StringComparison.Ordinal));

        fixture.JournalStore.Records.Add(Record(fixture, new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero), 2));

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent).AsTask();
        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>().WithMessage("*differs from the retained hard-close population*");
        retained.Replay().Signature.PayloadChecksumSha256.Should().Be(retainedHash);
    }

    [Fact]
    public async Task FinalStatementControlsBindEarlierBookPeriodAndKeepItsExactAmountSupport()
    {
        var fixture = CreateFixture();
        var prior = fixture.Period with
        {
            PeriodId = Guid.NewGuid(), PeriodNo = 6, Label = "2026-06", StartDate = new(2026, 6, 1),
            EndDate = new(2026, 6, 30), Version = 3
        };
        var opening = Record(fixture, new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero), 1) with { PeriodId = prior.PeriodId };
        var activity = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 2);
        fixture.JournalStore.Records.AddRange([opening, activity]);
        fixture.JournalStore.ComparisonPeriod = prior;
        fixture.JournalStore.RetainedComparisonClose = Close(prior, [opening]);
        fixture.JournalStore.RetainedClose = Close(fixture.Period, fixture.JournalStore.Records);
        fixture.Parameters = fixture.Parameters with
        {
            AsOfDate = fixture.Period.EndDate,
            OutputFormat = ReportingOutputFormatDto.Xlsx,
            TemplateParameters = new Dictionary<string, string>
            {
                [LedgerReportingAuthoritativeSource.ComparisonPeriodParameter] = prior.PeriodId.ToString("D")
            }
        };
        var intent = new ReportingAuthoritativeSourceCaptureIntent(DefaultReportingTemplateCatalog.FinancialStatementControlTemplateId);

        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);
        var report = capture.CertifiedLedgerPresentation!.ReportPack;
        var controls = report.StatementControl!;
        controls.Definition.Comparison!.PeriodId.Should().Be(prior.PeriodId);
        controls.Definition.Comparison.CloseSnapshotHashSha256.Should().Be(fixture.JournalStore.RetainedComparisonClose.ContentHashSha256);
        controls.Rows.Single(row => row.RowKey == "equity").Amount.Should().Be(250m);
        controls.Comparison!.Prior.Rows.Single(row => row.RowKey == "equity").Amount.Should().Be(125m);
        var comparativeRows = controls.ToTable().Rows.Where(row => row[^1] == "Comparative").ToArray();
        comparativeRows.Should().ContainSingle(row => row[1] == "equity" && row[4] == "125"
            && row[row.Count - 2] == prior.PeriodId.ToString("D"));
        var retained = ReportingLedgerPopulationSnapshot.Decode(capture.Checkpoint);
        ReportAmountBindingBuilder.MatchesRetained(retained).Should().BeTrue();
        retained.ReportAmounts.Single(amount => amount.Label.EndsWith(" / Ending equity / Total", StringComparison.Ordinal)
            && amount.Label.StartsWith("Comparative ", StringComparison.Ordinal)).JournalEntryIds.Should().Equal(opening.Entry.JournalEntryId);

        fixture.JournalStore.Records.Add(Record(fixture, opening.Entry.Timestamp.AddHours(1), 3) with { PeriodId = prior.PeriodId });
        fixture.JournalStore.RetainedClose = Close(fixture.Period, fixture.JournalStore.Records);

        var recapture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent).AsTask();
        await recapture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>().WithMessage("*comparative hard-close population differs*");
        retained.Replay().Signature.Should().Be(report.Signature);
    }

    private static LedgerPeriodCloseSnapshot Close(LedgerAccountingPeriod period, IReadOnlyList<LedgerJournalEntryRecord> records)
        => LedgerPeriodCloseSnapshot.Capture(period, new PeriodCloseEventRecord(Guid.NewGuid(), period.PeriodId, "SoftClosed",
            "HardClosed", "controller", "Synthetic close", period.ClosedAt!.Value), records);
}
