using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Microsoft.AspNetCore.TestHost;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_DataUploadTemplates_ShouldExposeSourceUploadCatalogInDataBootstrap()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var catalog = await client.GetFromJsonAsync<DataUploadTemplateCatalogDto>(
            UiApiRoutes.WorkstationDataUploadTemplates,
            ServerJsonOptions);
        var data = await client.GetFromJsonAsync<WorkstationDataPayload>(
            UiApiRoutes.WorkstationData,
            ServerJsonOptions);

        catalog.Should().NotBeNull();
        catalog!.AcceptedFileExtensions.Should().Equal(".csv");
        catalog.Templates.Select(template => template.TemplateId).Should().Contain([
            "trade-data",
            "transaction-data",
            "asset-information",
            "entity-configuration"
        ]);
        catalog.Templates.Single(template => template.TemplateId == "trade-data")
            .Fields.Where(field => field.Required).Select(field => field.Name)
            .Should().Contain(["trade_id", "trade_date", "account_code", "symbol", "side", "quantity", "price"]);

        data.Should().NotBeNull();
        data!.UploadTemplates.Templates.Select(template => template.TemplateId)
            .Should().Equal(catalog.Templates.Select(template => template.TemplateId));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataUploadPreview_ShouldRetainCsvAndReturnBoundedPreviewRows()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "data-uploads", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);

        try
        {
            await using var app = await CreateAppAsync();
            var client = app.GetTestClient();
            using var content = BuildUploadContent(
                "trade-data",
                """
                trade_id,trade_date,account_code,symbol,side,quantity,price,currency,strategy_id,source_system,source_document_id
                TRD-1,2026-06-01,FUND-A,AAPL,Buy,100,187.25,USD,income-core,Interactive Brokers,confirm-1
                """);

            var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadPreview, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadPreviewResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("ReadyForReview");
            result.UploadedBy.Should().Be("ops-user");
            result.TemplateId.Should().Be("trade-data");
            result.ParsedRowCount.Should().Be(1);
            result.PreviewRowCount.Should().Be(1);
            result.Headers.Should().Contain(["trade_id", "trade_date", "account_code", "symbol"]);
            result.PreviewRows[0]["symbol"].Should().Be("AAPL");
            result.RetainedPath.Should().Be($"workstation/data-uploads/{result.UploadId}/upload.csv");
            File.Exists(Path.Combine(root, result.UploadId, result.FileName)).Should().BeTrue();
            result.NextAction.Should().Contain("validation and reconciliation");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataUploadPreview_ShouldReturnSchemaIssuesWithoutApplyingRows()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "data-upload-schema", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);

        try
        {
            await using var app = await CreateAppAsync();
            var client = app.GetTestClient();
            using var content = BuildUploadContent(
                "trade-data",
                """
                trade_id,trade_date,symbol
                TRD-1,2026-06-01,AAPL
                """);

            var response = await client.PostAsync(UiApiRoutes.WorkstationDataUploadPreview, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadPreviewResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("NeedsSchemaRepair");
            result.Issues.Should().Contain(issue =>
                issue.Severity == "Error" &&
                issue.Field == "account_code" &&
                issue.Message.Contains("missing", StringComparison.OrdinalIgnoreCase));
            result.NextAction.Should().Contain("Repair the template headers");
            File.Exists(Path.Combine(root, result.UploadId, result.FileName)).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    private static MultipartFormDataContent BuildUploadContent(string templateId, string csv)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(templateId, Encoding.UTF8), "templateId");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv.Replace("\r\n", "\n")));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(file, "file", "upload.csv");
        return content;
    }
}
