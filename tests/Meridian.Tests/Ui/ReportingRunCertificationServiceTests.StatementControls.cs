using ClosedXML.Excel;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Parquet;

namespace Meridian.Tests.Ui;

public sealed partial class ReportingRunCertificationServiceTests
{
    [Fact]
    public async Task StatementControlPackPublishesExactWorkbookParquetAndLineMapping()
    {
        var report = BuildCanonicalCapitalReportPack(includeStatementControls: true);
        var template = new DefaultReportingTemplateCatalog().Get(DefaultReportingTemplateCatalog.FinancialStatementControlTemplateId);
        var manifest = await BuildCertifiedManifestAsync("run-controls", ReportingOutputFormatDto.ClientPackage,
            CapitalAccountRows(), template, report);
        var producer = new DeterministicReportingCertifiedArtifactProducer(new DocumentsReportingPrimaryDocumentRenderer(),
            new StubCertifiedLedgerPresentationSource(BindCanonicalPresentation(manifest, report)));

        var first = await producer.ProduceAsync(manifest);
        var second = await producer.ProduceAsync(manifest);
        var parquet = first.Artifacts.Single(artifact => artifact.ContentType == "application/vnd.apache.parquet");
        parquet.Content.ToArray().Should().Equal(second.Artifacts.Single(artifact => artifact.ArtifactId == parquet.ArtifactId).Content.ToArray());
        var controlsJson = first.Artifacts.Single(artifact => artifact.FileName.EndsWith(".statement-controls.json", StringComparison.Ordinal));
        System.Text.Encoding.UTF8.GetString(controlsJson.Content.Span).Should().Be(report.StatementControl!.Serialize());
        var xlsx = first.Artifacts.Single(artifact => artifact.FileName.EndsWith(".xlsx", StringComparison.Ordinal));
        using var workbook = new XLWorkbook(new MemoryStream(xlsx.Content.ToArray()));
        var table = workbook.Worksheet("Statement Controls");
        using var reader = await ParquetReader.CreateAsync(new MemoryStream(parquet.Content.ToArray()));
        using var group = reader.OpenRowGroupReader(0);
        var fields = reader.Schema.GetDataFields();
        for (var index = 0; index < fields.Length; index++)
        {
            var parquetColumn = await group.ReadColumnAsync(fields[index]);
            fields[index].Name.Should().Be(table.Cell(1, index + 1).GetString());
            parquetColumn.Data.Cast<string>().Should().Equal(Enumerable.Range(2, report.StatementControl.Rows.Count)
                .Select(row => table.Cell(row, index + 1).GetString()));
        }
    }
}
