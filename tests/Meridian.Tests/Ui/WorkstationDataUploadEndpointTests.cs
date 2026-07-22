using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_DataUploadTemplates_ShouldExposeSourceUploadCatalogInDataBootstrap()
    {
        // The data workspace endpoint requires a backing read service; without one it returns
        // 503 instead of fabricated fallback data.
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
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
        catalog.Templates.Single(template => template.TemplateId == "trade-data")
            .SourceKinds.Should().Contain(["Manual upload", "SFTP file drop"]);
        catalog.Templates.Single(template => template.TemplateId == "trade-data")
            .SetupChecklist.Should().Contain(item => item.Contains("pinned host-key fingerprint", StringComparison.OrdinalIgnoreCase));
        catalog.Templates.Single(template => template.TemplateId == "trade-data")
            .MappingGuidance.Should().Contain(item => item.Contains("account_code", StringComparison.OrdinalIgnoreCase));

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

    [Fact]
    public async Task MapWorkstationEndpoints_BankStatementImport_ShouldRetainCsvAndIngestBankLines()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "bank-statement-import", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);
        var accountService = new InMemoryFundAccountService();
        var account = await accountService.CreateAccountAsync(CreateBankImportAccountRequest());

        try
        {
            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton<IFundAccountService>(accountService);
            }, currentUserPermissions: UserPermission.ManageDirectLending);
            var client = app.GetTestClient();
            using var content = BuildBankStatementImportContent(
                account.AccountId,
                "JPMorgan",
                """
                transaction_date,value_date,amount,currency,transaction_type,description,reference,closing_balance
                2026-06-01,2026-06-03,-50000.00,USD,Wire,Payment to broker,WIRE-20260601-001,950000.00
                2026-06-02,,1250.25,usd,Interest,Deposit interest,INT-20260602,951250.25
                """,
                statementDate: "2026-06-30");

            var response = await client.PostAsync(UiApiRoutes.WorkstationBankStatementImport, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<BankStatementImportResultDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("Imported");
            result.ImportedBy.Should().Be("ops-user");
            result.AccountId.Should().Be(account.AccountId);
            result.AccountCode.Should().Be(account.AccountCode);
            result.StatementDate.Should().Be(new DateOnly(2026, 6, 30));
            result.LineCount.Should().Be(2);
            result.RetainedPath.Should().Be($"workstation/data-uploads/{result.UploadId}/bank-statement.csv");
            File.Exists(Path.Combine(root, result.UploadId, result.FileName)).Should().BeTrue();
            result.NextAction.Should().Contain("Meridian-owned ledger posting still requires approval");

            var stored = await accountService.GetBankStatementLinesAsync(account.AccountId);
            stored.Should().HaveCount(2);
            stored.Should().Contain(line =>
                line.Amount == -50000m &&
                line.Currency == "USD" &&
                line.TransactionType == "Wire" &&
                line.Reference == "WIRE-20260601-001" &&
                line.ClosingBalance == 950000m);
            stored.Should().Contain(line =>
                line.Amount == 1250.25m &&
                line.ValueDate == line.TransactionDate &&
                line.Description == "Deposit interest");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BankStatementImport_ShouldRejectSecurityMasterOnlyUser()
    {
        var accountService = new InMemoryFundAccountService();
        var account = await accountService.CreateAccountAsync(CreateBankImportAccountRequest());
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IFundAccountService>(accountService);
        }, currentUserPermissions: UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();
        using var content = BuildBankStatementImportContent(
            account.AccountId,
            "JPMorgan",
            """
            transaction_date,value_date,amount,currency,transaction_type,description,reference,closing_balance
            2026-06-01,2026-06-03,500.00,USD,Deposit,Unauthorized source row,REF-1,1000.00
            """);

        var response = await client.PostAsync(UiApiRoutes.WorkstationBankStatementImport, content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var stored = await accountService.GetBankStatementLinesAsync(account.AccountId);
        stored.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BankStatementImport_ShouldRejectInvalidRowsWithoutApplyingEvidence()
    {
        var accountService = new InMemoryFundAccountService();
        var account = await accountService.CreateAccountAsync(CreateBankImportAccountRequest());
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IFundAccountService>(accountService);
        }, currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();
        using var content = BuildBankStatementImportContent(
            account.AccountId,
            "JPMorgan",
            """
            transaction_date,value_date,amount,currency,transaction_type,description,reference,closing_balance
            not-a-date,2026-06-03,500.00,USD,Deposit,Bad source row,REF-1,1000.00
            """);

        var response = await client.PostAsync(UiApiRoutes.WorkstationBankStatementImport, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsDto>(ServerJsonOptions);
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey("transaction_date");

        var stored = await accountService.GetBankStatementLinesAsync(account.AccountId);
        stored.Should().BeEmpty();
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

    private static MultipartFormDataContent BuildBankStatementImportContent(
        Guid accountId,
        string bankName,
        string csv,
        string? statementDate = null)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(accountId.ToString("D"), Encoding.UTF8), "accountId");
        content.Add(new StringContent(bankName, Encoding.UTF8), "bankName");
        if (!string.IsNullOrWhiteSpace(statementDate))
        {
            content.Add(new StringContent(statementDate, Encoding.UTF8), "statementDate");
        }

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv.Replace("\r\n", "\n")));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(file, "file", "bank-statement.csv");
        return content;
    }

    private static CreateAccountRequest CreateBankImportAccountRequest()
        => new(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: $"BANK-{Guid.NewGuid():N}",
            DisplayName: "Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow,
            CreatedBy: "test",
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "123456789",
                BankName: "JPMorgan",
                BranchName: null,
                Iban: null,
                BicSwift: "CHASUS33",
                RoutingNumber: "021000021",
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: "Meridian Fund",
                BeneficiaryAddress: null));

    private sealed record ValidationProblemDetailsDto(
        string Type,
        string Title,
        int Status,
        IReadOnlyDictionary<string, string[]> Errors);
}
