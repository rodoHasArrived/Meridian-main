using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Xml.Linq;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;
using Microsoft.AspNetCore.TestHost;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private static readonly XNamespace WorkbookTestSpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string WorkbookTestContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public async Task MapWorkstationEndpoints_OnboardingWorkbook_ShouldReturnMultiSheetXlsx()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.WorkstationDataUploadWorkbook);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(WorkbookTestContentType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var sheetNames = ReadWorkbookSheetNames(bytes);
        sheetNames.Should().Contain(["Instructions", "Securities", "Entities", "Field reference", "_meta"]);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OnboardingWorkbook_ShouldHonorTemplateFilter()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync($"{UiApiRoutes.WorkstationDataUploadWorkbook}?templateIds=asset-information");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sheetNames = ReadWorkbookSheetNames(await response.Content.ReadAsByteArrayAsync());
        sheetNames.Should().Contain("Securities");
        sheetNames.Should().NotContain("Entities");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataUploadCatalog_ShouldAdvertiseWorkbookDownload()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var catalog = await client.GetFromJsonAsync<DataUploadTemplateCatalogDto>(
            UiApiRoutes.WorkstationDataUploadTemplates,
            ServerJsonOptions);

        catalog.Should().NotBeNull();
        catalog!.WorkbookFileName.Should().Be("meridian-onboarding-workbook.xlsx");
        catalog.WorkbookAcceptedFileExtensions.Should().Equal(".xlsx");
        catalog.WorkbookMaxFileBytes.Should().BeGreaterThan(catalog.MaxFileBytes);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkbookPreview_ShouldRetainWorkbookAndReturnReadySheets()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workbook-preview", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);

        try
        {
            await using var app = await CreateAppAsync();
            var client = app.GetTestClient();
            var workbook = XlsxWorkbookWriter.CreateWorkbook(
            [
                WorkbookMetaSheet(("Entities", "entity-configuration"), ("Securities", "asset-information")),
                new XlsxWorksheet(
                    "Entities",
                    ["entity_id", "entity_name", "entity_type", "parent_entity_id", "base_currency"],
                    [
                        ["ENT-1", "Northwind Income Fund LP", "Fund", "", "USD"],
                        ["ENT-2", "Northwind Sleeve", "Sleeve", "ENT-1", "USD"],
                    ]),
                new XlsxWorksheet(
                    "Securities",
                    ["asset_id", "symbol", "asset_name", "asset_class", "currency"],
                    [
                        ["AST-1", "AAPL", "Apple Inc", "Equity", "USD"],
                    ]),
            ]);

            using var content = BuildWorkbookUploadContent(workbook);
            var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadWorkbookPreview, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadWorkbookPreviewResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("ReadyForReview");
            result.UploadedBy.Should().Be("ops-user");
            result.SheetCount.Should().Be(2);
            result.TotalParsedRowCount.Should().Be(3);
            result.CrossSheetIssues.Should().BeEmpty();
            result.Sheets.Should().OnlyContain(sheet => sheet.Status == "ReadyForReview");
            result.Sheets.Single(sheet => sheet.SheetName == "Entities").TemplateId.Should().Be("entity-configuration");
            result.Sheets.Single(sheet => sheet.SheetName == "Securities").PreviewRows[0]["symbol"].Should().Be("AAPL");
            File.Exists(Path.Combine(root, result.UploadId, result.FileName)).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkbookPreview_ShouldFlagMissingCellAndDanglingParent()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workbook-preview-issues", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);

        try
        {
            await using var app = await CreateAppAsync();
            var client = app.GetTestClient();
            var workbook = XlsxWorkbookWriter.CreateWorkbook(
            [
                WorkbookMetaSheet(("Entities", "entity-configuration")),
                new XlsxWorksheet(
                    "Entities",
                    ["entity_id", "entity_name", "entity_type", "parent_entity_id", "base_currency"],
                    [
                        ["ENT-1", "Northwind Income Fund LP", "Fund", "", "USD"],
                        // Missing required entity_name -> per-cell Error.
                        ["ENT-2", "", "Sleeve", "ENT-1", "USD"],
                        // parent_entity_id points at an id that is not present -> cross-sheet Error.
                        ["ENT-3", "Orphan Vehicle", "Vehicle", "ENT-404", "USD"],
                    ]),
            ]);

            using var content = BuildWorkbookUploadContent(workbook);
            var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadWorkbookPreview, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadWorkbookPreviewResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("NeedsSchemaRepair");
            result.NextAction.Should().Contain("re-upload");

            var entities = result.Sheets.Single(sheet => sheet.SheetName == "Entities");
            entities.Status.Should().Be("NeedsRepair");
            entities.Issues.Should().Contain(issue =>
                issue.Severity == "Error" &&
                issue.Field == "entity_name" &&
                issue.SheetName == "Entities" &&
                issue.CellReference!.StartsWith("Entities!", StringComparison.Ordinal));

            result.CrossSheetIssues.Should().Contain(issue =>
                issue.Severity == "Error" &&
                issue.Field == "parent_entity_id" &&
                issue.SheetName == "Entities" &&
                issue.Message.Contains("ENT-404", StringComparison.Ordinal));

            File.Exists(Path.Combine(root, result.UploadId, result.FileName)).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkbookPreview_ShouldNormalizeExcelDateSerials()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workbook-dates", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);

        try
        {
            await using var app = await CreateAppAsync();
            var client = app.GetTestClient();
            // XlsxWorkbookWriter writes DateOnly cells as Excel serials with a date-formatted style,
            // exactly as Excel would when an operator types a date into the workbook.
            var workbook = XlsxWorkbookWriter.CreateWorkbook(
            [
                WorkbookMetaSheet(("Entities", "entity-configuration")),
                new XlsxWorksheet(
                    "Entities",
                    ["entity_id", "entity_name", "entity_type", "effective_from"],
                    [
                        new object?[] { "ENT-1", "Northwind Fund", "Fund", new DateOnly(2026, 1, 1) },
                    ]),
            ]);

            using var content = BuildWorkbookUploadContent(workbook);
            var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadWorkbookPreview, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadWorkbookPreviewResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();

            var entities = result!.Sheets.Single(sheet => sheet.SheetName == "Entities");
            entities.PreviewRows[0]["effective_from"].Should().Be("2026-01-01");
            entities.Status.Should().Be("ReadyForReview");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkbookPreview_ShouldNotBeReadyWhenNoDataRows()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workbook-empty", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);

        try
        {
            await using var app = await CreateAppAsync();
            var client = app.GetTestClient();
            // A pristine workbook download: recognized tabs, headers, but no data rows.
            var workbook = XlsxWorkbookWriter.CreateWorkbook(
            [
                WorkbookMetaSheet(("Entities", "entity-configuration")),
                new XlsxWorksheet("Entities", ["entity_id", "entity_name", "entity_type"], []),
            ]);

            using var content = BuildWorkbookUploadContent(workbook);
            var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadWorkbookPreview, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadWorkbookPreviewResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.TotalParsedRowCount.Should().Be(0);
            result.Status.Should().Be("NeedsSchemaRepair");
            result.NextAction.Should().Contain("no data rows");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkbookPreview_ShouldRejectUnmappedSheet()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "workbook-unmapped", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);

        try
        {
            await using var app = await CreateAppAsync();
            var client = app.GetTestClient();
            // No _meta sheet and a name that matches no template -> the tab is unmapped.
            var workbook = XlsxWorkbookWriter.CreateWorkbook(
            [
                new XlsxWorksheet("Mystery Tab", ["foo", "bar"], [["a", "b"]]),
            ]);

            using var content = BuildWorkbookUploadContent(workbook);
            var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadWorkbookPreview, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadWorkbookPreviewResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("NeedsSchemaRepair");

            var sheet = result.Sheets.Single(candidate => candidate.SheetName == "Mystery Tab");
            sheet.TemplateId.Should().BeNull();
            sheet.Status.Should().Be("NeedsRepair");
            sheet.Issues.Should().Contain(issue => issue.Severity == "Error" && issue.Field == "sheet");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkbookPreview_ShouldRejectNonWorkbookPayload()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        using var content = BuildWorkbookUploadContent("this is not a workbook"u8.ToArray());

        var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadWorkbookPreview, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static XlsxWorksheet WorkbookMetaSheet(params (string SheetName, string TemplateId)[] entries)
    {
        var rows = entries
            .Select(entry => (IReadOnlyList<object?>)new object?[] { entry.SheetName, entry.TemplateId, "1" })
            .ToArray();
        return new XlsxWorksheet("_meta", ["sheetName", "templateId", "schemaVersion"], rows);
    }

    private static MultipartFormDataContent BuildWorkbookUploadContent(byte[] workbook)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(workbook);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(WorkbookTestContentType);
        content.Add(file, "file", "workbook.xlsx");
        return content;
    }

    private static IReadOnlyList<string> ReadWorkbookSheetNames(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/workbook.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        return document
            .Descendants(WorkbookTestSpreadsheetNamespace + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value ?? string.Empty)
            .ToArray();
    }
}
